#Requires -Version 5.1
<#
    VividWorld 開發部署腳本。
    建置 Release，並以 VividWorld.Dev 的身分安裝到遊戲的 Modules 資料夾。
    專案的模組身分與版本一律以 module/SubModule.xml 為準。
#>
[CmdletBinding()]
param(
    [string]$GameFolder = 'C:\Game\Steam\steamapps\common\Mount & Blade II Bannerlord'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

# 自動偵測並配置本機使用者 .dotnet SDK 路徑（避免系統層級無 SDK 的 dotnet.exe 搶先被呼叫）
$userDotnet = Join-Path $env:USERPROFILE '.dotnet'
if (Test-Path (Join-Path $userDotnet 'dotnet.exe')) {
    $env:DOTNET_ROOT = $userDotnet
    $env:PATH = "$userDotnet;$env:PATH"
}

$devId     = 'VividWorld.Dev'
$devName   = 'Vivid World (dev)'
$modulesDir = Join-Path $GameFolder 'Modules'
$targetDir  = Join-Path $modulesDir $devId
$targetBin  = Join-Path $targetDir 'bin\Win64_Shipping_Client'

if (-not (Test-Path $modulesDir)) {
    Write-Error "找不到遊戲 Modules 資料夾：$modulesDir"
}

# ── 1. 移除正式版目錄，避免與 dev 版重複註冊 ────────────────────────
$releaseDir = Join-Path $modulesDir 'VividWorld'
if (Test-Path $releaseDir) {
    Write-Host "移除既有的 Modules\VividWorld"
    Remove-Item -Recurse -Force $releaseDir
}

# ── 2. 建置 ──────────────────────────────────────────────────────────
$csproj = Join-Path $repoRoot 'src\VividWorld.Module\VividWorld.Module.csproj'
Write-Host "建置 $csproj (Release)"
dotnet build $csproj -c Release
if ($LASTEXITCODE -ne 0) { Write-Error "建置失敗，離開碼 $LASTEXITCODE" }

$outDir = Join-Path $repoRoot 'src\VividWorld.Module\bin\Release'
if (-not (Test-Path $outDir)) { Write-Error "找不到建置輸出：$outDir" }

# ── 3. 改寫 SubModule.xml 的 Id 與 Name ──────────────────────────────
if (-not (Test-Path $targetDir)) { New-Item -ItemType Directory -Force $targetDir | Out-Null }

$srcXml = Join-Path $repoRoot 'module\SubModule.xml'
$xml = Get-Content $srcXml -Raw -Encoding UTF8
$xml = $xml -replace '<Id value="VividWorld" />', "<Id value=""$devId"" />"
$xml = $xml -replace '<Name value="Vivid World" />', "<Name value=""$devName"" />"
$xml | Out-File (Join-Path $targetDir 'SubModule.xml') -Encoding utf8
Write-Host "已寫入 SubModule.xml (Id=$devId)"

# ── 4. 複製組件 ──────────────────────────────────────────────────────
if (-not (Test-Path $targetBin)) { New-Item -ItemType Directory -Force $targetBin | Out-Null }

$dlls = @('VividWorld.dll', 'VividWorld.Core.dll', 'Newtonsoft.Json.dll', '0Harmony.dll')
foreach ($dll in $dlls) {
    $src = Join-Path $outDir $dll
    if (-not (Test-Path $src)) { Write-Error "建置輸出缺少 $dll —— 檢查 csproj 的 Private 設定" }
    Copy-Item $src $targetBin -Force
    Write-Host "  複製 $dll"
}

# ── 5. 複製 GUI 與 ModuleData 的「內容」──────────────────────────────
# 資料夾對資料夾複製會產生 ModuleData\ModuleData 巢狀，所以來源一律加 \*
foreach ($name in @('GUI', 'ModuleData')) {
    $src = Join-Path $repoRoot "module\$name"
    if (-not (Test-Path $src)) { continue }
    $dst = Join-Path $targetDir $name
    if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Force $dst | Out-Null }
    $items = Get-ChildItem $src -Force
    if ($items.Count -gt 0) {
        Copy-Item (Join-Path $src '*') $dst -Recurse -Force
        # .gitkeep 只是讓空目錄進得了 git，不該進玩家的模組資料夾。
        # 事後清除比 Copy-Item -Exclude 可靠——後者搭配 -Recurse 在 PS 5.1 行為不穩。
        Get-ChildItem $dst -Recurse -Force -Filter '.gitkeep' | Remove-Item -Force
        Write-Host "  複製 $name\ 的內容"
    }
}

Write-Host ""
Write-Host "完成。啟動器應可看到 '$devName'。" -ForegroundColor Green