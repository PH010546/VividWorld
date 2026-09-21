#Requires -Version 5.1
<#
    VividWorld 發行打包腳本。

    建置 Release，把玩家真正需要的檔案組成 dist\VividWorld\，再壓成一個
    可以直接解壓到遊戲 Modules\ 底下的 zip。

    與 tools\deploy.ps1 的差別：
      deploy.ps1  改寫 Id/Name 成 VividWorld.Dev，裝進遊戲資料夾，給開發用
      package.ps1 一個字都不改，用 module\SubModule.xml 原本的正式身分出貨

    版本號的唯一來源是 module\SubModule.xml 的 <Version>，這裡只讀不寫。
#>
[CmdletBinding()]
param(
    [string]$GameFolder = 'C:\Game\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string]$OutDir,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $repoRoot 'dist' }

# 自動偵測本機使用者的 .dotnet SDK（同 deploy.ps1：避免系統層級那支沒有 SDK 的 dotnet.exe 搶先）
$userDotnet = Join-Path $env:USERPROFILE '.dotnet'
if (Test-Path (Join-Path $userDotnet 'dotnet.exe')) {
    $env:DOTNET_ROOT = $userDotnet
    $env:PATH = "$userDotnet;$env:PATH"
}

# ── 1. 從 SubModule.xml 讀身分與版本 ────────────────────────────────
$srcXmlPath = Join-Path $repoRoot 'module\SubModule.xml'
if (-not (Test-Path $srcXmlPath)) { Write-Error "找不到 $srcXmlPath" }

[xml]$subModule = Get-Content $srcXmlPath -Raw -Encoding UTF8
$moduleId   = $subModule.Module.Id.value
$moduleName = $subModule.Module.Name.value
$version    = $subModule.Module.Version.value

if ([string]::IsNullOrWhiteSpace($moduleId))   { Write-Error 'SubModule.xml 沒有 <Id value="..."/>' }
if ([string]::IsNullOrWhiteSpace($version))    { Write-Error 'SubModule.xml 沒有 <Version value="..."/>' }
if ($moduleId -like '*.Dev') { Write-Error "SubModule.xml 的 Id 是 '$moduleId' —— 出貨不能帶 dev 身分" }

# 原生用 ApplicationVersion.FromString 解析這個字串：首字是類型（a/b/e/v/d），
# 其餘以 . 切開後逐段 Convert.ToInt32 ⇒ 帶後綴的 "v0.9.0-beta" 會丟例外（帳本 D-74）。
if ($version -notmatch '^[abevd](\d+)\.(\d+)\.(\d+)(\.\d+)?$') {
    Write-Error "版本號 '$version' 原生解析不了。格式必須是 v0.9.0 這種純數字（帳本 D-74）"
}

Write-Host "打包 $moduleName ($moduleId) $version" -ForegroundColor Cyan

# ── 2. 建置 Release ──────────────────────────────────────────────────
$csproj = Join-Path $repoRoot 'src\VividWorld.Module\VividWorld.Module.csproj'
$buildOut = Join-Path $repoRoot 'src\VividWorld.Module\bin\Release'

if ($SkipBuild) {
    Write-Host "略過建置（-SkipBuild），沿用既有的 bin\Release"
    if (-not (Test-Path $buildOut)) { Write-Error "找不到建置輸出：$buildOut —— 拿掉 -SkipBuild 再跑一次" }
} else {
    Write-Host "建置 $csproj (Release)"
    dotnet build $csproj -c Release "-p:GameFolder=$GameFolder"
    if ($LASTEXITCODE -ne 0) { Write-Error "建置失敗，離開碼 $LASTEXITCODE" }
    if (-not (Test-Path $buildOut)) { Write-Error "找不到建置輸出：$buildOut" }
}

# ── 3. 清空並重建暫存目錄 ────────────────────────────────────────────
# 先組在 _stage\<moduleId>\ 底下：壓縮時對著 _stage 打包，zip 最上層就會是模組資料夾。
$stageParent = Join-Path $OutDir '_stage'
if (Test-Path $stageParent) { Remove-Item -Recurse -Force $stageParent }
$stageRoot = Join-Path $stageParent $moduleId
New-Item -ItemType Directory -Force $stageRoot | Out-Null
$finalRoot = Join-Path $OutDir $moduleId
if (Test-Path $finalRoot) { Remove-Item -Recurse -Force $finalRoot }

$stageBin = Join-Path $stageRoot 'bin\Win64_Shipping_Client'
New-Item -ItemType Directory -Force $stageBin | Out-Null

# ── 4. 組件 ──────────────────────────────────────────────────────────
$dlls = @('VividWorld.dll', 'VividWorld.Core.dll', 'Newtonsoft.Json.dll', '0Harmony.dll')
foreach ($dll in $dlls) {
    $src = Join-Path $buildOut $dll
    if (-not (Test-Path $src)) { Write-Error "建置輸出缺少 $dll —— 檢查 csproj 的 Private 設定" }
    Copy-Item $src $stageBin -Force
}

# 遊戲自己的組件絕不能被打包進去（會蓋掉玩家的遊戲檔）
$strays = Get-ChildItem $stageBin -Filter 'TaleWorlds.*' -ErrorAction SilentlyContinue
if ($strays) { Write-Error "暫存目錄裡出現遊戲組件：$($strays.Name -join ', ')" }

# ── 5. SubModule.xml 與內容資料夾 ────────────────────────────────────
Copy-Item $srcXmlPath (Join-Path $stageRoot 'SubModule.xml') -Force

foreach ($name in @('GUI', 'ModuleData')) {
    $src = Join-Path $repoRoot "module\$name"
    if (-not (Test-Path $src)) { continue }
    $dst = Join-Path $stageRoot $name
    New-Item -ItemType Directory -Force $dst | Out-Null
    $items = Get-ChildItem $src -Force
    if ($items.Count -gt 0) {
        Copy-Item (Join-Path $src '*') $dst -Recurse -Force
    }
}

# .gitkeep 只是讓空目錄進得了 git，不該出現在玩家的模組資料夾
Get-ChildItem $stageRoot -Recurse -Force -Filter '.gitkeep' | Remove-Item -Force

# 開發用的檔案不出貨。留在 repo 裡是因為測試與假事件產生器要讀它（EventCatalogTests.cs:818、
# EventCatalogStore.cs:44），但玩家的模組資料夾裡不該有測試夾具。
$devOnly = @('ModuleData\vividworld_sample_events.json')
foreach ($rel in $devOnly) {
    $path = Join-Path $stageRoot $rel
    if (Test-Path $path) { Remove-Item -Force $path }
}

# ── 6. 授權與說明 ────────────────────────────────────────────────────
# Harmony 隨模組出貨（遊戲不提供），MIT 授權要求保留授權條款全文
$harmonyLicense = Join-Path $repoRoot 'lib\0Harmony.LICENSE.txt'
if (Test-Path $harmonyLicense) { Copy-Item $harmonyLicense $stageRoot -Force }

foreach ($doc in @('README.md', 'README.en.md', 'LICENSE')) {
    $src = Join-Path $repoRoot $doc
    if (Test-Path $src) { Copy-Item $src $stageRoot -Force }
}

# ── 7. 自我檢查 ──────────────────────────────────────────────────────
$problems = @()
foreach ($dll in $dlls) {
    if (-not (Test-Path (Join-Path $stageBin $dll))) { $problems += "缺少 bin\Win64_Shipping_Client\$dll" }
}
foreach ($required in @('SubModule.xml',
                        'ModuleData\vividworld_events.json',
                        'ModuleData\vividworld_situations.json',
                        'ModuleData\vividworld_situation_events.json',
                        'ModuleData\Languages\std_module_strings_xml.xml',
                        'ModuleData\Languages\CNt\std_module_strings_xml.xml',
                        'GUI\Prefabs\VividWorldChronicle.xml')) {
    if (-not (Test-Path (Join-Path $stageRoot $required))) { $problems += "缺少 $required" }
}
$leftovers = Get-ChildItem $stageRoot -Recurse -Force -Filter '.gitkeep' -ErrorAction SilentlyContinue
if ($leftovers) { $problems += ".gitkeep 沒清乾淨：$($leftovers.Count) 個" }
foreach ($rel in $devOnly) {
    if (Test-Path (Join-Path $stageRoot $rel)) { $problems += "開發用檔案混進出貨包：$rel" }
}

if ($problems.Count -gt 0) {
    Write-Error ("打包內容不完整：`n  " + ($problems -join "`n  "))
}

# ── 8. 壓縮 ──────────────────────────────────────────────────────────
# zip 內最上層就是模組資料夾，玩家解壓到 Modules\ 底下即可。
# 不用 Compress-Archive：PowerShell 5.1 那支會把路徑分隔符寫成反斜線，
# 不合 zip 規格，有些解壓工具會把整條路徑當成一個檔名。
# CreateFromDirectory 在這台機器的 .NET Framework 上一樣寫反斜線，所以逐檔自己加，
# 項目名稱明確換成正斜線。
Add-Type -AssemblyName System.IO.Compression           # ZipArchiveMode / CompressionLevel
Add-Type -AssemblyName System.IO.Compression.FileSystem  # ZipFile / ZipFileExtensions
$zipPath = Join-Path $OutDir "$moduleId-$version.zip"
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

$archive = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $prefixLength = $stageParent.TrimEnd('\').Length + 1
    foreach ($file in (Get-ChildItem $stageParent -Recurse -File -Force | Sort-Object FullName)) {
        $entryName = $file.FullName.Substring($prefixLength).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $file.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $archive.Dispose() }

# 檢查一遍分隔符，不要相信它
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $badEntries = @($zip.Entries | Where-Object { $_.FullName -like '*\*' })
} finally { $zip.Dispose() }
if ($badEntries.Count -gt 0) {
    Write-Error "zip 裡有 $($badEntries.Count) 個項目用反斜線當分隔符，例如：$($badEntries[0].FullName)"
}

# 暫存搬成最終的資料夾（玩家也可以直接拿這個資料夾丟進 Modules\）
Move-Item $stageRoot $finalRoot
Remove-Item -Recurse -Force $stageParent
$stageRoot = $finalRoot
$stageBin  = Join-Path $stageRoot 'bin\Win64_Shipping_Client'

# ── 9. 清單：這包裡到底是什麼 ────────────────────────────────────────
Write-Host ""
Write-Host "組件指紋（部署後可以拿遊戲裡那份比對）：" -ForegroundColor Cyan
foreach ($dll in $dlls) {
    $file = Get-Item (Join-Path $stageBin $dll)
    $hash = (Get-FileHash $file.FullName -Algorithm MD5).Hash.ToLower()
    Write-Host ("  {0,-22} {1,9:N0} bytes  {2}" -f $dll, $file.Length, $hash)
}

$fileCount = (Get-ChildItem $stageRoot -Recurse -File -Force).Count
$totalSize = (Get-ChildItem $stageRoot -Recurse -File -Force | Measure-Object -Property Length -Sum).Sum
$zipSize   = (Get-Item $zipPath).Length

Write-Host ""
Write-Host "完成。" -ForegroundColor Green
Write-Host ("  資料夾  {0}  （{1} 個檔案，{2:N0} bytes）" -f $stageRoot, $fileCount, $totalSize)
Write-Host ("  壓縮檔  {0}  （{1:N0} bytes）" -f $zipPath, $zipSize)
Write-Host ""
Write-Host "玩家安裝方式：解壓後把 $moduleId\ 整個資料夾放進遊戲的 Modules\ 底下。"
