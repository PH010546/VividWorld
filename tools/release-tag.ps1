#Requires -Version 5.1
<#
    定版與推版。開發與測試都在 master 上，master 不保證隨時能發；確定可以發的那個 commit
    打一個標籤 v<版本>（定版），之後隨時可以從那個標籤推出去，master 照常往下開發。

    用法：
        .\tools\release-tag.ps1 -List
            列出每個定版標籤：commit、日期、Nexus 上有沒有這一版（讀 Nexus API，查不到就寫查不到）

        .\tools\release-tag.ps1 -Create
            在 master 或 fix/ 分支上（修正版）、沒有未提交的改動時，照 SubModule.xml 的版本打標籤並 push。
            先檢查版本號沒有被用過、三份更新紀錄（CHANGELOG.md、module\CHANGELOG.txt、.en.txt）都有這一版

        .\tools\release-tag.ps1 -Tag v1.3.0 -Step Package
        .\tools\release-tag.ps1 -Tag v1.3.0 -Step Export -Message "v1.3.0: ..."
        .\tools\release-tag.ps1 -Tag v1.3.0 -Step Upload [-Send]
        .\tools\release-tag.ps1 -Tag v1.3.0 -Step Description [-Send]
            在暫存資料夾開一份那個標籤的工作副本（git worktree），用那個標籤裡的工具跑：
            Package 正式打包（壓縮檔另外複製一份到主工作資料夾的 dist\）、Export 公開出口
            （同步點記回主工作資料夾的 .public-sync）、Upload 上傳 Nexus、Description 送介紹頁。
            Upload 與 Description 不加 -Send 只看計畫

        .\tools\release-tag.ps1 -Tag v1.3.0 -Cleanup
            刪掉那份工作副本
#>
[CmdletBinding(DefaultParameterSetName = 'List')]
param(
    [Parameter(ParameterSetName = 'List')][switch]$List,
    [Parameter(ParameterSetName = 'Create', Mandatory)][switch]$Create,
    [Parameter(ParameterSetName = 'Step', Mandatory)]
    [Parameter(ParameterSetName = 'Cleanup', Mandatory)]
    [string]$Tag,
    [Parameter(ParameterSetName = 'Step', Mandatory)]
    [ValidateSet('Package', 'Export', 'Upload', 'Description')][string]$Step,
    [Parameter(ParameterSetName = 'Step')][string]$Message,
    [Parameter(ParameterSetName = 'Step')][switch]$Send,
    [Parameter(ParameterSetName = 'Cleanup', Mandatory)][switch]$Cleanup,
    [string]$GameDomain = 'mountandblade2bannerlord',
    [string]$ModId = '13395',
    [string]$ApiKeyFile = (Join-Path $env:USERPROFILE '.vividworld\nexus-apikey.txt')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$worktreeBase = Join-Path $env:LOCALAPPDATA 'VividWorld\release'

# git 會把進度訊息寫到 stderr；Windows PowerShell 5.1 在 Stop 底下會把它當成終止性錯誤，
# 所以呼叫 git 時放寬，改看結束碼
function Invoke-Git {
    $prevPref = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & git -C $repoRoot @args 2>&1 | ForEach-Object { "$_" }
    } finally {
        $ErrorActionPreference = $prevPref
    }
    if ($LASTEXITCODE -ne 0) { Write-Error ("git $($args -join ' ') 失敗：`n" + ($out -join "`n")) }
    return $out
}

function Get-ReleaseTags {
    $tags = @(& git -C $repoRoot tag --list 'v*')
    return @($tags | Where-Object { $_ -match '^v\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.TrimStart('v') } -Descending)
}

# Nexus 上這個模組每個版本的版本字串與分類；讀不到就回 $null（列清單時寫「查不到」，不中止）
function Get-NexusVersions {
    if (-not (Test-Path $ApiKeyFile)) { return $null }
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $headers = @{ apikey = ([IO.File]::ReadAllText($ApiKeyFile)).Trim(); Accept = 'application/json' }
        $base = 'https://api.nexusmods.com/v3'
        $mod = (Invoke-RestMethod -Uri "$base/games/$GameDomain/mods/$ModId" -Headers $headers).data
        $files = @((Invoke-RestMethod -Uri "$base/mods/$($mod.id)/files" -Headers $headers).data.mod_files)
        $result = @{}
        foreach ($f in $files) {
            foreach ($v in @((Invoke-RestMethod -Uri "$base/mod-files/$($f.id)/versions" -Headers $headers).data.versions)) {
                $result['v' + $v.version.TrimStart('v')] = $v.category
            }
        }
        return $result
    } catch {
        Write-Warning "讀不到 Nexus 的版本清單：$($_.Exception.Message)"
        return $null
    }
}

# ── 列清單 ───────────────────────────────────────────────────────────
if ($PSCmdlet.ParameterSetName -eq 'List') {
    $tags = Get-ReleaseTags
    if ($tags.Count -eq 0) { Write-Host '還沒有任何定版標籤。'; return }
    $nexus = Get-NexusVersions
    Write-Host '定版標籤（新的在上面）：'
    foreach ($t in $tags) {
        $line = Invoke-Git log -1 --format='%h %ad' --date=short "$t^{commit}"
        $state = if ($null -eq $nexus) { 'Nexus：查不到' }
                 elseif ($nexus.ContainsKey($t)) { "Nexus：已推（$($nexus[$t])）" }
                 else { 'Nexus：還沒推' }
        Write-Host ("  {0,-9} {1}  {2}" -f $t, $line, $state)
    }
    return
}

# ── 定版 ─────────────────────────────────────────────────────────────
if ($PSCmdlet.ParameterSetName -eq 'Create') {
    $branch = (Invoke-Git rev-parse --abbrev-ref HEAD).Trim()
    if ($branch -ne 'master' -and -not $branch.StartsWith('fix/')) { Write-Error "定版只在 master 或 fix/ 分支（修正版）上打，現在在 '$branch'。" }
    $dirty = @(& git -C $repoRoot status --porcelain)
    if ($dirty.Count -gt 0) { Write-Error "有 $($dirty.Count) 項沒提交的改動，先 commit 再定版：`n  $(($dirty | Select-Object -First 10) -join "`n  ")" }

    [xml]$subModule = Get-Content (Join-Path $repoRoot 'module\SubModule.xml') -Raw -Encoding UTF8
    $version = $subModule.Module.Version.value
    if ($version -notmatch '^v\d+\.\d+\.\d+$') { Write-Error "SubModule.xml 的版本 '$version' 不是 v<主>.<次>.<修> 的格式" }
    if ((Get-ReleaseTags) -contains $version) { Write-Error "標籤 $version 已經有了：版本號還沒加" }

    $checks = @(
        @{ Path = 'CHANGELOG.md'; Line = "## $version" },
        @{ Path = 'CHANGELOG.en.md'; Line = "## $version" },
        @{ Path = 'module\CHANGELOG.txt'; Line = $version },
        @{ Path = 'module\CHANGELOG.en.txt'; Line = $version }
    )
    foreach ($c in $checks) {
        $lines = [IO.File]::ReadAllLines((Join-Path $repoRoot $c.Path), [Text.Encoding]::UTF8) | ForEach-Object { $_.Trim() }
        if ($lines -notcontains $c.Line) { Write-Error "$($c.Path) 裡沒有 '$($c.Line)' 這一行：更新紀錄還沒寫這一版" }
    }

    $head = (Invoke-Git rev-parse --short HEAD).Trim()
    Invoke-Git tag -a $version -m "Release $version" | Out-Null
    Invoke-Git push origin $version --quiet | Out-Null
    Write-Host "已定版：標籤 $version 打在 $head，已 push。" -ForegroundColor Green
    Write-Host "要推的時候：tools\release-tag.ps1 -Tag $version -Step Package（再 Export、Upload）"
    return
}

# ── 標籤的工作副本 ───────────────────────────────────────────────────
if ((Get-ReleaseTags) -notcontains $Tag) { Write-Error "沒有 '$Tag' 這個定版標籤。用 -List 看有哪些。" }
$worktree = Join-Path $worktreeBase $Tag

if ($PSCmdlet.ParameterSetName -eq 'Cleanup') {
    if (Test-Path $worktree) {
        Invoke-Git worktree remove --force $worktree | Out-Null
        Write-Host "已刪掉工作副本：$worktree" -ForegroundColor Green
    } else {
        Write-Host "沒有這份工作副本：$worktree"
    }
    Invoke-Git worktree prune | Out-Null
    return
}

$tagCommit = (Invoke-Git rev-parse "$Tag^{commit}").Trim()
if (-not (Test-Path $worktree)) {
    New-Item -ItemType Directory -Force $worktreeBase | Out-Null
    Invoke-Git worktree add --detach $worktree $Tag | Out-Null
    Write-Host "開了 $Tag 的工作副本：$worktree"
}
$wtHead = (& git -C $worktree rev-parse HEAD).Trim()
if ($wtHead -ne $tagCommit) {
    Write-Error "工作副本 $worktree 不在 $Tag 上（HEAD $($wtHead.Substring(0,7))）。先跑 -Tag $Tag -Cleanup 再重來。"
}

$tools = Join-Path $worktree 'tools'
switch ($Step) {
    'Package' {
        & (Join-Path $tools 'package.ps1')
        $zip = Join-Path $worktree "dist\VividWorld-$Tag.zip"
        if (-not (Test-Path $zip)) { Write-Error "打包完找不到 $zip" }
        $dist = Join-Path $repoRoot 'dist'
        New-Item -ItemType Directory -Force $dist | Out-Null
        Copy-Item $zip $dist -Force
        Write-Host "發行包另存一份到 $(Join-Path $dist (Split-Path -Leaf $zip))" -ForegroundColor Green
    }
    'Export' {
        if (-not $Message) { Write-Error '公開出口要帶 -Message（中性英文）。' }
        & (Join-Path $tools 'export-public.ps1') -Message $Message -SyncRoot $repoRoot
    }
    'Upload' {
        if ($Send) { & (Join-Path $tools 'nexus-upload.ps1') -Send } else { & (Join-Path $tools 'nexus-upload.ps1') }
    }
    'Description' {
        if ($Send) { & (Join-Path $tools 'nexus-description.ps1') -Send } else { & (Join-Path $tools 'nexus-description.ps1') }
    }
}
