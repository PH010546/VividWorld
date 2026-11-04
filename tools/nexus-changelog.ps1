#Requires -Version 5.1
<#
    追加一個版本的英文更新紀錄到 Nexus Mods，並核對送出去的結果。

    不加 -Send：只讀，把用哪個帳號、哪一版、要追加哪些字印出來就停。

    會擋下來、不送的情況：
      - 版本字串不合格式，或 Nexus 上沒有這個版本的檔案（-AllowNoFile 才放行，給定版後沒推、直接跳過的版本用）
      - module\CHANGELOG.en.txt 沒有這一版（沒有用 -Text 直接給字的時候）
      - Nexus 上這一版已經有同樣的字（這條端點只能追加不能改，重送會多一份；-AllowDuplicate 才放行）

    送出後核對，對不上就印警告，最後以結束碼 1 收尾：
      - 伺服器回的版本與文字要跟送出去的一樣（回應全文印在日誌裡）
      - 讀回 v1 的 changelogs.json 要看得到這些字

    v3 沒有讀更新紀錄的端點，只能從 v1 讀回。v1 讀得到不等於網站上的編輯區有內容，
    網站上有沒有要到頁面上看。

    API 金鑰：有環境變數 NEXUS_API_KEY 就用它（GitHub Actions 從 Secrets 給），否則從 -ApiKeyFile 讀。

    用法：
        .\tools\nexus-changelog.ps1 -Version 1.3.0                         # 只看計畫
        .\tools\nexus-changelog.ps1 -Version 1.3.0 -Send                   # 真的追加
    tools\nexus-upload.ps1 上傳新版本時也用這支。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$GameDomain = 'mountandblade2bannerlord',
    [string]$ModId = '13395',
    [string]$ApiKeyFile = (Join-Path $env:USERPROFILE '.vividworld\nexus-apikey.txt'),
    # 不給就取 module\CHANGELOG.en.txt 裡這一版那一節
    [string]$Text,
    [switch]$AllowNoFile,
    [switch]$AllowDuplicate,
    [switch]$Send
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$apiBase = 'https://api.nexusmods.com/v3'
$v1Base = "https://api.nexusmods.com/v1/games/$GameDomain/mods/$ModId"

[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Net.Http

$Version = $Version.Trim().TrimStart('v')
if ($Version -notmatch '^\d+\.\d+\.\d+$') { Write-Error "版本字串 '$Version' 不合格式，要像 1.3.0" }

# ── 要追加的字 ─────────────────────────────────────────
if (-not $Text) {
    $path = Join-Path $repoRoot 'module\CHANGELOG.en.txt'
    $lines = [IO.File]::ReadAllLines($path, [Text.Encoding]::UTF8)
    $start = [Array]::IndexOf($lines, "v$Version")
    if ($start -lt 0) { Write-Error "$path 裡沒有 'v$Version' 這一行" }
    $body = New-Object System.Collections.Generic.List[string]
    for ($i = $start + 1; $i -lt $lines.Length -and $lines[$i].Trim() -ne ''; $i++) { $body.Add($lines[$i]) }
    if ($body.Count -eq 0) { Write-Error "$path 的 'v$Version' 底下是空的" }
    $Text = $body -join "`n"
}

# ── API 金鑰 ───────────────────────────────────────────
if ($env:NEXUS_API_KEY) {
    $apiKey = $env:NEXUS_API_KEY.Trim()
} else {
    if (-not (Test-Path $ApiKeyFile)) { Write-Error "找不到 API 金鑰檔 $ApiKeyFile" }
    $apiKey = ([IO.File]::ReadAllText($ApiKeyFile)).Trim()
}
if (-not $apiKey) { Write-Error 'API 金鑰是空的' }

$http = New-Object System.Net.Http.HttpClient
$http.Timeout = [TimeSpan]::FromMinutes(2)

# 回傳 (狀態碼, 回應全文)；失敗時不丟例外，由呼叫的地方決定怎麼辦
function Invoke-Raw([string]$method, [string]$url, $body) {
    $req = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($method)), $url
    $req.Headers.Add('apikey', $apiKey)
    $req.Headers.Add('Accept', 'application/json')
    if ($null -ne $body) {
        $json = $body | ConvertTo-Json -Depth 10 -Compress
        $req.Content = New-Object System.Net.Http.StringContent ($json, [Text.Encoding]::UTF8, 'application/json')
    }
    $resp = $http.SendAsync($req).GetAwaiter().GetResult()
    return @([int]$resp.StatusCode, $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult())
}
function Get-Json([string]$url) {
    $r = Invoke-Raw 'GET' $url $null
    if ($r[0] -lt 200 -or $r[0] -ge 300) { Write-Error "GET $url 失敗：HTTP $($r[0])`n$($r[1])" }
    return ($r[1] | ConvertFrom-Json)
}
function Get-LiveLines {
    $all = Get-Json "$v1Base/changelogs.json"
    return @($all.$Version)
}
function Test-Contains([string[]]$live, [string[]]$want) {
    if ($want.Count -eq 0 -or $live.Count -lt $want.Count) { return $false }
    for ($i = 0; $i -le $live.Count - $want.Count; $i++) {
        $same = $true
        for ($j = 0; $j -lt $want.Count; $j++) { if ($live[$i + $j].Trim() -ne $want[$j].Trim()) { $same = $false; break } }
        if ($same) { return $true }
    }
    return $false
}

$me = Get-Json 'https://api.nexusmods.com/v1/users/validate.json'
Write-Host "金鑰的帳號：$($me.user_id) $($me.name)（來源：$(if ($env:NEXUS_API_KEY) { 'NEXUS_API_KEY' } else { '金鑰檔' })）"

$mod = (Get-Json "$apiBase/games/$GameDomain/mods/$ModId").data
Write-Host "模組：$($mod.name)（內部 id $($mod.id)）"

# ── 這一版在 Nexus 上有沒有檔案 ─────────────────────────
function Find-FileVersion {
    foreach ($f in @((Get-Json "$apiBase/mods/$($mod.id)/files").data.mod_files)) {
        $hit = @((Get-Json "$apiBase/mod-files/$($f.id)/versions").data.versions | Where-Object { $_.version -eq $Version })
        if ($hit.Count -gt 0) { return $hit[-1] }
    }
    return $null
}
# 剛建好的版本可能還讀不到，真的要送時多等一下
$fileVersion = Find-FileVersion
for ($try = 1; -not $fileVersion -and $Send -and -not $AllowNoFile -and $try -le 6; $try++) {
    Start-Sleep -Seconds 10
    $fileVersion = Find-FileVersion
}
if ($fileVersion) {
    Write-Host "Nexus 上的檔案：版本 $($fileVersion.version)，分類 $($fileVersion.category)，上傳於 $($fileVersion.uploaded_at)"
} elseif ($AllowNoFile) {
    Write-Host "Nexus 上沒有 $Version 的檔案（這一版是跳過的，照樣追加）"
} else {
    Write-Error "Nexus 上沒有版本 $Version 的檔案，不送。"
}

# ── 現況與要追加的字 ────────────────────────────────────
$want = @($Text -split "`n" | ForEach-Object { $_.TrimEnd("`r") })
$before = Get-LiveLines
Write-Host "`nv1 讀到的 $Version 現況（$($before.Count) 行）："
foreach ($l in $before) { Write-Host "  $l" }
Write-Host "`n要追加的字：`n$Text"

$duplicate = Test-Contains $before $want
if ($duplicate -and -not $AllowDuplicate) {
    Write-Error "v1 上 $Version 已經有同樣的字。這條端點只能追加不能改，重送會多一份；確定要送才加 -AllowDuplicate。"
}
if (-not $Send) {
    Write-Host "`n只看計畫，沒有送出任何東西。要送加 -Send。" -ForegroundColor Yellow
    return
}

# ── 送出與核對 ─────────────────────────────────────────
$problems = New-Object System.Collections.Generic.List[string]
$r = Invoke-Raw 'POST' "$apiBase/mods/$($mod.id)/changelogs" @{ version = $Version; changelog = $Text }
Write-Host "`nPOST /mods/$($mod.id)/changelogs 回 HTTP $($r[0])："
Write-Host $r[1]
if ($r[0] -lt 200 -or $r[0] -ge 300) { throw "Nexus API 回 $($r[0])，更新紀錄沒有送成功。" }

$sent = ($r[1] | ConvertFrom-Json).data
if ($sent.version -ne $Version) { $problems.Add("伺服器回的版本是 '$($sent.version)'，送出去的是 '$Version'") }
if ($sent.changelog -ne $Text) { $problems.Add('伺服器回的文字跟送出去的不一樣') }

# 讀回走 v1，可能有快取，隔 10 秒重讀，最多一分鐘
$after = @()
$seen = $false
for ($try = 1; $try -le 6 -and -not $seen; $try++) {
    Start-Sleep -Seconds 10
    $after = Get-LiveLines
    $seen = if ($duplicate) { $after.Count -gt $before.Count } else { Test-Contains $after $want }
}
Write-Host "`nv1 讀回的 $Version（$($after.Count) 行）："
foreach ($l in $after) { Write-Host "  $l" }
if (-not $seen) { $problems.Add('一分鐘後 v1 還是讀不到新追加的字') }

if ($problems.Count -gt 0) {
    foreach ($p in $problems) { Write-Warning "更新紀錄 $($Version)：$p" }
    exit 1
}
Write-Host "`nAPI 核對通過。網站的編輯區要到頁面上看：https://www.nexusmods.com/$GameDomain/mods/$ModId" -ForegroundColor Green
