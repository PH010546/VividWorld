#Requires -Version 5.1
<#
    發佈前的檢查：只准在「切在一個定版標籤上、沒有未提交的改動」的工作資料夾裡打包、出口、上傳。
    開發與測試都在 master 上進行，master 不保證隨時能發；能發的是定版時打的標籤 v<版本>
    （tools\release-tag.ps1 -Create 打的）。發版時由 release-tag.ps1 在暫存資料夾開一份那個標籤的
    工作副本，在那裡跑打包與出口，master 的進度不受影響。
    打包與出口讀的是工作資料夾，切在哪就包哪一份——這支檢查擋掉「切在 master 或開發分支上、
    或手上有沒提交的改動時」誤打包。
    用法（被 package.ps1、export-public.ps1、nexus-upload.ps1、nexus-description.ps1 引用）：
        . (Join-Path $PSScriptRoot 'release-guard.ps1')
        Assert-ReleaseTree -RepoRoot $repoRoot
#>

function Assert-ReleaseTree {
    param([Parameter(Mandatory)][string]$RepoRoot)

    # 不在標籤上時 git 會往 stderr 寫一行；呼叫端多半開著 ErrorActionPreference=Stop，
    # Windows PowerShell 5.1 會把那一行當成終止性錯誤，所以只在這一個呼叫放寬
    $prevPref = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $tag = (& git -C $RepoRoot describe --tags --exact-match --match 'v*' HEAD 2>$null)
    } finally {
        $ErrorActionPreference = $prevPref
    }
    if ($LASTEXITCODE -ne 0 -or -not $tag) {
        $where = (& git -C $RepoRoot rev-parse --abbrev-ref HEAD 2>$null)
        Write-Error ("目前在 '$where'，不是切在定版標籤上。master 不保證能發；" +
            "要發哪一版用 tools\release-tag.ps1 -Tag v<版本> 從那個標籤打包、出口、上傳。")
    }
    $tag = $tag.Trim()

    [xml]$subModule = Get-Content (Join-Path $RepoRoot 'module\SubModule.xml') -Raw -Encoding UTF8
    $version = $subModule.Module.Version.value
    if ($tag -ne $version) {
        Write-Error "標籤 '$tag' 跟 SubModule.xml 的版本 '$version' 對不上：定版時版本號沒有改到，或標籤打錯 commit。"
    }

    $dirty = @(& git -C $RepoRoot status --porcelain)
    if ($LASTEXITCODE -ne 0) { Write-Error "git status 失敗。" }
    if ($dirty.Count -gt 0) {
        $preview = ($dirty | Select-Object -First 10) -join "`n  "
        $more = if ($dirty.Count -gt 10) { "`n  …另外 $($dirty.Count - 10) 項" } else { '' }
        Write-Error "工作資料夾有 $($dirty.Count) 項沒提交的改動，打包出去的會跟標籤對不上：`n  $preview$more"
    }

    $head = (& git -C $RepoRoot rev-parse --short HEAD).Trim()
    Write-Host "發佈檢查通過：標籤 $tag，commit $head，沒有未提交的改動" -ForegroundColor Green
}
