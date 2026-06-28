#Requires -Version 7.0
param(
    [string]$XrayVersion = "v26.3.27"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "src\v2rayF.Android\v2rayF.Android.csproj"
$Dist = Join-Path $Root "dist"
$PublishDir = Join-Path $Dist "v2rayF-android-arm64\publish"

& (Join-Path $Root "scripts\package-android.ps1") -XrayVersion $XrayVersion

if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null

dotnet publish $Project -c Release -f net10.0-android -r android-arm64 --self-contained true -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "Android publish failed" }

$apk = Get-ChildItem -Path $PublishDir -Recurse -Filter "*.apk" | Select-Object -First 1
if (-not $apk) { throw "APK not found in publish output" }

$zipPath = Join-Path $Dist "v2rayF-android-arm64.zip"
Copy-Item $apk.FullName (Join-Path $PublishDir "v2rayF-android-arm64.apk") -Force
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $PublishDir "v2rayF-android-arm64.apk") -DestinationPath $zipPath -Force

Write-Host "Created $zipPath"
