#Requires -Version 5.1
<#
    把 docs\nexus-description.bbcode 整份送到 Nexus Mods 的模組介紹頁，送完讀回來比對。

    不加 -Send：只讀，比對 Nexus 上現在那份跟檔案有沒有差別就停。加 -Send 才真的改。

    流程：
      1. GET   /v3/games/{遊戲}/mods/{模組編號}     取模組的內部 id
      2. GET   /v1/games/{遊戲}/mods/{模組編號}.json 讀現在的介紹頁
      3. PATCH /v3/mods/{id}                        送新的介紹頁（只送 description，summary 不動）
      4. 再做一次第 2 步，比對

    Nexus 讀回的介紹頁把換行存成 <br />、清單項目補上 [/*]，比對前換回來、去掉每行頭尾空白。
    API 金鑰從 -ApiKeyFile 讀，不接受命令列參數、不印出來。

    用法：
        .\tools\nexus-description.ps1          # 只比對
        .\tools\nexus-description.ps1 -Send    # 真的送（只准切在定版標籤、沒有未提交的改動時）
#>
[CmdletBinding()]
param(
    [string]$GameDomain = 'mountandblade2bannerlord',
    [string]$ModId = '13395',
    [string]$ApiKeyFile = (Join-Path $env:USERPROFILE '.vividworld\nexus-apikey.txt'),
    [switch]$Send
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

if ($Send) {
    . (Join-Path $PSScriptRoot 'release-guard.ps1')
    Assert-ReleaseTree -RepoRoot $repoRoot
}

$bbcode = [IO.File]::ReadAllText((Join-Path $repoRoot 'docs\nexus-description.bbcode'), [Text.Encoding]::UTF8).TrimStart([char]0xFEFF)

if (-not (Test-Path $ApiKeyFile)) { Write-Error "找不到 API 金鑰檔 $ApiKeyFile" }
$apiKey = ([IO.File]::ReadAllText($ApiKeyFile)).Trim()
$headers = @{ apikey = $apiKey; Accept = 'application/json' }

function Normalize([string]$text) {
    $t = $text -replace '<br\s*/?>', "`n" -replace "`r", ''
    $t = [Net.WebUtility]::HtmlDecode($t)
    # Nexus 存的時候會替每個清單項目補上結尾的 [/*]
    return (($t -split "`n" | ForEach-Object { ($_.Trim() -replace '\[/\*\]$', '').Trim() } | Where-Object { $_ -ne '' }) -join "`n")
}
function Get-LiveDescription {
    $m = Invoke-RestMethod -Headers $headers "https://api.nexusmods.com/v1/games/$GameDomain/mods/$ModId.json"
    return [string]$m.description
}
function Compare-Description([string]$live) {
    $a = (Normalize $bbcode) -split "`n"
    $b = (Normalize $live) -split "`n"
    $diff = @(Compare-Object $a $b)
    if ($diff.Count -eq 0) { Write-Host '比對：Nexus 上的介紹頁跟檔案一樣' -ForegroundColor Green; return $true }
    Write-Host "比對：有 $($diff.Count) 行不一樣（<= 只在檔案裡，=> 只在 Nexus 上）" -ForegroundColor Yellow
    foreach ($d in $diff) { Write-Host ("  {0} {1}" -f $d.SideIndicator, $d.InputObject) }
    return $false
}

$mod = (Invoke-RestMethod -Headers $headers "https://api.nexusmods.com/v3/games/$GameDomain/mods/$ModId").data
Write-Host "模組：$($mod.name)（$GameDomain/$ModId，內部 id $($mod.id)）"
$same = Compare-Description (Get-LiveDescription)

if (-not $Send) {
    Write-Host "`n只比對，沒有送出任何東西。要送加 -Send。" -ForegroundColor Yellow
    return
}
if ($same) { Write-Host '一樣，不送。'; return }

$body = @{ description = $bbcode } | ConvertTo-Json -Compress
$null = Invoke-RestMethod -Method Patch -Headers $headers -ContentType 'application/json; charset=utf-8' `
    -Body ([Text.Encoding]::UTF8.GetBytes($body)) "https://api.nexusmods.com/v3/mods/$($mod.id)"
Write-Host '介紹頁已送出，讀回比對…' -ForegroundColor Green
# 讀回走 v1，有快取：送出後十幾秒內讀到的常是舊的，所以隔 10 秒重讀，最多一分鐘
for ($try = 1; $try -le 6; $try++) {
    Start-Sleep -Seconds 10
    if (Compare-Description (Get-LiveDescription)) { return }
}
Write-Warning '一分鐘後讀回來仍跟檔案不一樣，要到網站上看是哪裡被改了'
