#Requires -Version 7.0
param(
    [string]$XrayVersion = "v26.3.27"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Tools = Join-Path $Root ".tools\xray"
$Assets = Join-Path $Root "src\v2rayF.Android\Assets"
$NativeLibs = Join-Path $Root "src\v2rayF.Android\NativeLibs\arm64-v8a"
$ZipName = "Xray-android-arm64-v8a.zip"
$BaseUrl = "https://github.com/XTLS/Xray-core/releases/download/$XrayVersion/$ZipName"

New-Item -ItemType Directory -Force -Path $Tools, $Assets, $NativeLibs | Out-Null

$zipPath = Join-Path $Tools $ZipName
if (-not (Test-Path $zipPath)) {
    Write-Host "Downloading $ZipName ..."
    Invoke-WebRequest -Uri $BaseUrl -OutFile $zipPath -UserAgent "v2rayF-setup"
}

$extractDir = Join-Path $Tools ($ZipName -replace '\.zip$', '')
if (-not (Test-Path $extractDir)) {
    Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force
}

foreach ($name in @("geoip.dat", "geosite.dat")) {
    $file = Get-ChildItem -Path $extractDir -Recurse -File -Filter $name | Select-Object -First 1
    if (-not $file) { throw "Missing $name in $ZipName" }
    Copy-Item $file.FullName (Join-Path $Assets $name) -Force
    Write-Host "Installed $name"
}

$xray = Get-ChildItem -Path $extractDir -Recurse -File -Filter "xray" | Select-Object -First 1
if (-not $xray) { throw "Missing xray in $ZipName" }
Copy-Item $xray.FullName (Join-Path $NativeLibs "libxray.so") -Force
Write-Host "Installed libxray.so (Xray core for arm64-v8a)"

Write-Host "Android assets ready in src/v2rayF.Android/Assets/ and NativeLibs/arm64-v8a/"
