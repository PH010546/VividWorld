#Requires -Version 5.1
<#
    把正式發行包上傳到 Nexus Mods，在既有的模組檔案底下建新版本，並追加英文的玩家版更新紀錄。

    不加 -Send：只讀（查模組、查檔案、查上一版），把「要傳什麼、建成什麼版本、追加哪些字」
    整份印出來就停，一個請求都不寫。加 -Send 才真的上傳。

    流程（Nexus API v3，https://api.nexusmods.com/openapi.yaml）：
      1. GET  /games/{遊戲}/mods/{模組編號}         取模組的內部 id
      2. GET  /mods/{id}/files                     找要加版本的那個模組檔案
      3. GET  /mod-files/{id}/versions             上一版的版本字串、檔名、類別
      4. POST /uploads                             建上傳（帶 md5）→ PUT 到回傳的網址 → POST /uploads/{id}/finalise
      5. GET  /uploads/{id}                        等到 state = available
      6. POST /mod-files/{id}/versions             建新版本（主要檔案、設成 mod manager 預設下載、更新模組版本號；上一版不封存）
      7. POST /mods/{id}/changelogs                追加英文那一份（只能追加，不能改）
      8. GET  /mod-files/{id}/versions             讀回來，印出每一版的分類與誰是預設下載
    任何一步失敗就停，把伺服器回的原文印出來；之後那一版改由人手動上傳。

    API 金鑰從 -ApiKeyFile 讀（預設 使用者資料夾\.vividworld\nexus-apikey.txt），
    不接受命令列參數、不印出來、不寫進任何檔案。

    用法：
        .\tools\nexus-upload.ps1              # 只看計畫
        .\tools\nexus-upload.ps1 -Send        # 真的上傳（只准在主分支、沒有未提交的改動時）
#>
[CmdletBinding()]
param(
    [string]$GameDomain = 'mountandblade2bannerlord',
    [string]$ModId = '13395',
    [string]$ApiKeyFile = (Join-Path $env:USERPROFILE '.vividworld\nexus-apikey.txt'),
    # 預設 dist\VividWorld-<版本>.zip（package.ps1 的輸出）
    [string]$ZipPath,
    # 要加版本的模組檔案名稱；模組頁上只有一個有效的檔案時可以不給
    [string]$ModFileName,
    [int]$WaitSeconds = 300,
    [switch]$Send
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$apiBase = 'https://api.nexusmods.com/v3'

[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Net.Http

# ── 版本與要傳的檔案 ─────────────────────────────────────────────────
[xml]$subModule = Get-Content (Join-Path $repoRoot 'module\SubModule.xml') -Raw -Encoding UTF8
$version = $subModule.Module.Version.value
if (-not $ZipPath) { $ZipPath = Join-Path $repoRoot "dist\VividWorld-$version.zip" }
if (-not (Test-Path $ZipPath)) { Write-Error "找不到發行包 $ZipPath —— 先跑 tools\package.ps1" }
$zip = Get-Item $ZipPath

if ($Send) {
    . (Join-Path $PSScriptRoot 'release-guard.ps1')
    Assert-ReleaseTree -RepoRoot $repoRoot
}

# ── 玩家版更新紀錄：取這一版那一節（版本號那一行之後、下一個空行之前）──────
function Get-ChangelogSection([string]$path, [string]$ver) {
    $lines = [IO.File]::ReadAllLines($path, [Text.Encoding]::UTF8)
    $start = [Array]::IndexOf($lines, $ver)
    if ($start -lt 0) { Write-Error "$path 裡沒有 '$ver' 這一行" }
    $body = New-Object System.Collections.Generic.List[string]
    for ($i = $start + 1; $i -lt $lines.Length -and $lines[$i].Trim() -ne ''; $i++) { $body.Add($lines[$i]) }
    if ($body.Count -eq 0) { Write-Error "$path 的 '$ver' 底下是空的" }
    return ($body -join "`n")
}
# Nexus 上暫時只放英文；中文那份照樣隨發行包出貨
$changelogs = [ordered]@{
    'English' = Get-ChangelogSection (Join-Path $repoRoot 'module\CHANGELOG.en.txt') $version
}

# ── API 金鑰 ─────────────────────────────────────────────────────────
if (-not (Test-Path $ApiKeyFile)) { Write-Error "找不到 API 金鑰檔 $ApiKeyFile（Nexus 網站 設定 → API Keys 產生的個人金鑰，整串貼進去存檔）" }
$apiKey = ([IO.File]::ReadAllText($ApiKeyFile)).Trim()
if (-not $apiKey) { Write-Error "API 金鑰檔 $ApiKeyFile 是空的" }

$http = New-Object System.Net.Http.HttpClient
$http.Timeout = [TimeSpan]::FromMinutes(10)

function Invoke-Nexus([string]$method, [string]$path, $body) {
    $req = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::new($method)), ($apiBase + $path)
    $req.Headers.Add('apikey', $apiKey)
    $req.Headers.Add('Accept', 'application/json')
    if ($null -ne $body) {
        $json = $body | ConvertTo-Json -Depth 10 -Compress
        $req.Content = New-Object System.Net.Http.StringContent ($json, [Text.Encoding]::UTF8, 'application/json')
    }
    $resp = $http.SendAsync($req).GetAwaiter().GetResult()
    $text = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if (-not $resp.IsSuccessStatusCode) {
        Write-Host "`n$method $path 失敗：HTTP $([int]$resp.StatusCode) $($resp.ReasonPhrase)" -ForegroundColor Red
        Write-Host $text
        throw "Nexus API 回 $([int]$resp.StatusCode)，停在這一步。這一版改成手動上傳。"
    }
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    return ($text | ConvertFrom-Json).data
}

# ── 1～3. 只讀：模組、檔案、上一版 ─────────────────────────────────────
$mod = Invoke-Nexus 'GET' "/games/$GameDomain/mods/$ModId" $null
Write-Host "模組：$($mod.name)（$GameDomain/$ModId，內部 id $($mod.id)）"

$files = @((Invoke-Nexus 'GET' "/mods/$($mod.id)/files" $null).mod_files)
foreach ($f in $files) { Write-Host ("  檔案：{0}  有效={1}  版本數={2}  封存={3}  id={4}" -f $f.name, $f.is_active, $f.versions_count, $f.archived_count, $f.id) }
if ($ModFileName) {
    $target = @($files | Where-Object { $_.name -eq $ModFileName })
} else {
    $target = @($files | Where-Object { $_.is_active })
}
if ($target.Count -ne 1) { Write-Error "要加版本的檔案對到 $($target.Count) 個，用 -ModFileName 指定是哪一個" }
$target = $target[0]

$versions = @((Invoke-Nexus 'GET' "/mod-files/$($target.id)/versions" $null).versions | Sort-Object { [decimal]$_.position })
$previous = $versions | Where-Object { $_.category -notin @('archived', 'removed', 'old_version') } | Select-Object -Last 1
if (-not $previous) { $previous = $versions | Select-Object -Last 1 }
if (-not $previous) { Write-Error "檔案 '$($target.name)' 底下沒有任何版本，這支腳本只處理「在既有檔案下加新版本」" }

# 版本字串照上一版的寫法：上一版帶 v 就帶 v
$nexusVersion = if ($previous.version -match '^v') { $version } else { $version.TrimStart('v') }
if ($previous.version -eq $nexusVersion) { Write-Error "Nexus 上一版已經是 $nexusVersion —— 版本號沒有加" }

$md5Bytes = [Security.Cryptography.MD5]::Create().ComputeHash([IO.File]::ReadAllBytes($zip.FullName))
$md5Hex = -join ($md5Bytes | ForEach-Object { $_.ToString('x2') })

Write-Host ''
Write-Host '── 計畫 ──' -ForegroundColor Cyan
Write-Host "上傳：$($zip.Name)（$($zip.Length) bytes，md5 $md5Hex）"
Write-Host "加在檔案：$($target.name)（id $($target.id)）"
Write-Host "上一版：$($previous.version)  名稱 '$($previous.name)'  類別 $($previous.category)  上傳於 $($previous.uploaded_at)"
Write-Host "新版本：$nexusVersion  名稱 '$($previous.name)'  類別 main  mod manager 預設下載=是  更新模組版本號=是  上一版不封存（留成舊版）"
foreach ($k in $changelogs.Keys) {
    Write-Host "`n追加更新紀錄（$k，版本 $nexusVersion）：" -ForegroundColor Cyan
    Write-Host $changelogs[$k]
}

if (-not $Send) {
    Write-Host "`n只看計畫，沒有送出任何東西。確認無誤後加 -Send。" -ForegroundColor Yellow
    return
}

# ── 4. 上傳 ──────────────────────────────────────────────────────────
Write-Host "`n建上傳…"
$upload = Invoke-Nexus 'POST' '/uploads' @{ size_bytes = $zip.Length; filename = $zip.Name; md5 = $md5Hex }
Write-Host "  上傳 id $($upload.id)"

$put = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::Put), $upload.presigned_url
$put.Content = New-Object System.Net.Http.ByteArrayContent (, [IO.File]::ReadAllBytes($zip.FullName))
$put.Content.Headers.ContentDisposition = [System.Net.Http.Headers.ContentDispositionHeaderValue]::Parse("attachment; filename=`"$($zip.Name)`"")
$put.Content.Headers.ContentMD5 = $md5Bytes
# 預先簽好的網址把 Content-Type 也算進簽章；不帶這個標頭會被儲存端以 SignatureDoesNotMatch 拒絕。
# 值照 Nexus 官方 upload-action 送檔案時用的那一個
$put.Content.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/octet-stream')
$putResp = $http.SendAsync($put).GetAwaiter().GetResult()
if (-not $putResp.IsSuccessStatusCode) {
    Write-Host "PUT 檔案失敗：HTTP $([int]$putResp.StatusCode)" -ForegroundColor Red
    Write-Host ($putResp.Content.ReadAsStringAsync().GetAwaiter().GetResult())
    throw '上傳檔案失敗，停在這一步。這一版改成手動上傳。'
}
Write-Host '  檔案已送出'

$null = Invoke-Nexus 'POST' "/uploads/$($upload.id)/finalise" $null
Write-Host '  已結束上傳，等待 Nexus 處理…'

# ── 5. 等到可用 ──────────────────────────────────────────────────────
$deadline = (Get-Date).AddSeconds($WaitSeconds)
do {
    Start-Sleep -Seconds 5
    $state = (Invoke-Nexus 'GET' "/uploads/$($upload.id)" $null).state
    Write-Host "  狀態：$state"
} while ($state -ne 'available' -and (Get-Date) -lt $deadline)
if ($state -ne 'available') { throw "等了 $WaitSeconds 秒上傳仍是 '$state'（上傳 id $($upload.id)），停在這一步。" }

# ── 6. 建新版本 ──────────────────────────────────────────────────────
$created = Invoke-Nexus 'POST' "/mod-files/$($target.id)/versions" @{
    upload_id             = $upload.id
    name                  = $previous.name
    version               = $nexusVersion
    file_category         = 'main'
    # 「mod manager 的預設下載」建版本之後就沒有端點能改，只能在這裡設：一律給新上傳的這一版
    primary_mod_manager_download = $true
    update_mod_version    = $true
    # 上一版不封存，留成舊版；封存了沒有端點能改回來
    archive_existing_file = $false
    previous_version_id   = $previous.id
}
Write-Host "新版本已建立：檔案 $($created.file.name)（$($created.file.game_scoped_id)），版本 id $($created.version.id)" -ForegroundColor Green

# ── 7. 追加更新紀錄 ──────────────────────────────────────────────────
foreach ($k in $changelogs.Keys) {
    $null = Invoke-Nexus 'POST' "/mods/$($mod.id)/changelogs" @{ version = $nexusVersion; changelog = $changelogs[$k] }
    Write-Host "更新紀錄已追加：$k" -ForegroundColor Green
}

# ── 8. 讀回來核對 ────────────────────────────────────────────────────
$after = @((Invoke-Nexus 'GET' "/mod-files/$($target.id)/versions" $null).versions | Sort-Object { [decimal]$_.position })
Write-Host "`n讀回 Nexus 上的版本："
foreach ($v in $after) { Write-Host ("  {0,-10} {1,-12} 預設下載={2}" -f $v.version, $v.category, $v.is_primary) }
$newV = $after | Where-Object { $_.version -eq $nexusVersion } | Select-Object -Last 1
$oldV = $after | Where-Object { $_.id -eq $previous.id }
if (-not $newV -or $newV.category -ne 'main' -or -not $newV.is_primary) { Write-Warning "新版本 $nexusVersion 不是「main＋預設下載」，要到網站上手動改" }
if ($oldV -and $oldV.category -ne 'old_version') { Write-Warning "上一版 $($previous.version) 的分類是 '$($oldV.category)'，不是 old_version，要到網站上手動改" }

Write-Host "`n完成：https://www.nexusmods.com/$GameDomain/mods/$($ModId)?tab=files" -ForegroundColor Green
