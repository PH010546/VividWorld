#Requires -Version 5.1
<#
    發佈前的檢查：只准從主分支、而且沒有未提交的改動時打包或出口。

    開發在 feat/<代號> 分支上進行，驗收通過才合回主分支，所以主分支上只有
    驗收過的東西。打包與出口讀的是工作資料夾，切在哪條分支就包哪一份——
    這支檢查擋掉「切在開發分支上、或手上有沒提交的改動時」誤打包。

    主分支認兩個名字：開發 repo 是 master，公開 repo 是 main。

    用法（被 package.ps1 與 export-public.ps1 引用）：
        . (Join-Path $PSScriptRoot 'release-guard.ps1')
        Assert-ReleaseTree -RepoRoot $repoRoot
#>

$ReleaseBranches = @('master', 'main')

function Assert-ReleaseTree {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $branch = (& git -C $RepoRoot rev-parse --abbrev-ref HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or -not $branch) {
        Write-Error "讀不到目前的分支：$RepoRoot 不是 git repo，或 git 不在 PATH 上。"
    }
    $branch = $branch.Trim()

    if ($ReleaseBranches -notcontains $branch) {
        Write-Error ("目前在分支 '$branch'。只准從主分支（" + ($ReleaseBranches -join '／') +
            "）打包或出口 —— 開發分支上有還沒驗收的東西。先把 feature 合回主分支再跑。")
    }

    $dirty = @(& git -C $RepoRoot status --porcelain)
    if ($LASTEXITCODE -ne 0) { Write-Error "git status 失敗。" }
    if ($dirty.Count -gt 0) {
        $preview = ($dirty | Select-Object -First 10) -join "`n  "
        $more = if ($dirty.Count -gt 10) { "`n  …另外 $($dirty.Count - 10) 項" } else { '' }
        Write-Error "工作資料夾有 $($dirty.Count) 項沒提交的改動，打包出去的會跟任何一個 commit 都對不上：`n  $preview$more"
    }

    $head = (& git -C $RepoRoot rev-parse --short HEAD).Trim()
    Write-Host "發佈檢查通過：分支 $branch，commit $head，沒有未提交的改動" -ForegroundColor Green
}
