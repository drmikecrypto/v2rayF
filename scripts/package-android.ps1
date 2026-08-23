#Requires -Version 7.0
param(
    [string]$XrayVersion = "v26.7.28",
    [string]$SingBoxVersion = "1.12.12"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Tools = Join-Path $Root ".tools\xray"
$SingBoxTools = Join-Path $Root ".tools\sing-box"
$Assets = Join-Path $Root "src\v2rayF.Android\Assets"
$NativeLibs = Join-Path $Root "src\v2rayF.Android\NativeLibs\arm64-v8a"
$ZipName = "Xray-android-arm64-v8a.zip"
$BaseUrl = "https://github.com/XTLS/Xray-core/releases/download/$XrayVersion/$ZipName"
$SingBoxAsset = "sing-box-$SingBoxVersion-android-arm64.tar.gz"
$SingBoxUrl = "https://github.com/SagerNet/sing-box/releases/download/v$SingBoxVersion/$SingBoxAsset"

New-Item -ItemType Directory -Force -Path $Tools, $SingBoxTools, $Assets, $NativeLibs | Out-Null

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

# sing-box must be lib*.so under NativeLibs — Android 10+ blocks exec from filesDir.
$sbArchive = Join-Path $SingBoxTools $SingBoxAsset
$sbExtract = Join-Path $SingBoxTools ($SingBoxAsset -replace '\.tar\.gz$', '')
if (-not (Test-Path $sbArchive)) {
    Write-Host "Downloading $SingBoxAsset ..."
    Invoke-WebRequest -Uri $SingBoxUrl -OutFile $sbArchive -UserAgent "v2rayF-setup"
}
if (-not (Test-Path $sbExtract)) {
    New-Item -ItemType Directory -Force -Path $sbExtract | Out-Null
    tar -xzf $sbArchive -C $sbExtract
}
$singBox = Get-ChildItem -Path $sbExtract -Recurse -File | Where-Object { $_.Name -eq "sing-box" } | Select-Object -First 1
if (-not $singBox) { throw "Missing sing-box binary in $SingBoxAsset" }
Copy-Item $singBox.FullName (Join-Path $NativeLibs "libsingbox.so") -Force
Write-Host "Installed libsingbox.so (sing-box core for arm64-v8a)"

Write-Host "Android assets ready in src/v2rayF.Android/Assets/ and NativeLibs/arm64-v8a/"
