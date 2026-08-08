#Requires -Version 7.0
param(
    [string]$XrayVersion = "v26.7.28",
    [string]$KeystorePath = "",
    [string]$KeyAlias = "",
    [string]$StorePassEnv = "ANDROID_KEYSTORE_PASSWORD",
    [string]$KeyPassEnv = "ANDROID_KEY_PASSWORD",
    [switch]$AllowUnsigned
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "src\v2rayF.Android\v2rayF.Android.csproj"
$Dist = Join-Path $Root "dist"
$PublishDir = Join-Path $Dist "v2rayF-android-arm64\publish"

function Get-ProjectVersion([string]$csproj) {
    [xml]$xml = Get-Content -LiteralPath $csproj
    $ver = $xml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $ver) { throw "Version not found in $csproj" }
    return [string]$ver
}

function Get-VersionCode([string]$semver) {
    $parts = $semver.Split('.')
    if ($parts.Count -lt 3) { throw "Expected major.minor.patch version, got '$semver'" }
    return ([int]$parts[0] * 10000) + ([int]$parts[1] * 100) + [int]$parts[2]
}

& (Join-Path $Root "scripts\package-android.ps1") -XrayVersion $XrayVersion

$version = Get-ProjectVersion $Project
$versionCode = Get-VersionCode $version
Write-Host "Publishing Android $version (versionCode $versionCode)"

$publishArgs = @(
    "publish", $Project,
    "-c", "Release",
    "-f", "net10.0-android",
    "-r", "android-arm64",
    "--self-contained", "true",
    "-o", $PublishDir,
    "-p:ApplicationDisplayVersion=$version",
    "-p:ApplicationVersion=$versionCode",
    "-p:Version=$version"
)

$hasKeystore = -not [string]::IsNullOrWhiteSpace($KeystorePath)
if ($hasKeystore) {
    if (-not (Test-Path -LiteralPath $KeystorePath)) {
        throw "Keystore not found: $KeystorePath"
    }
    if ([string]::IsNullOrWhiteSpace($KeyAlias)) {
        throw "KeyAlias is required when signing."
    }
    $storePass = [Environment]::GetEnvironmentVariable($StorePassEnv)
    $keyPass = [Environment]::GetEnvironmentVariable($KeyPassEnv)
    if ([string]::IsNullOrWhiteSpace($storePass) -or [string]::IsNullOrWhiteSpace($keyPass)) {
        throw "Missing env $StorePassEnv and/or $KeyPassEnv for APK signing."
    }

    $publishArgs += @(
        "-p:AndroidKeyStore=true",
        "-p:AndroidSigningKeyStore=$KeystorePath",
        "-p:AndroidSigningKeyAlias=$KeyAlias",
        "-p:AndroidSigningStorePass=env:$StorePassEnv",
        "-p:AndroidSigningKeyPass=env:$KeyPassEnv"
    )
    Write-Host "Signing APK with keystore alias '$KeyAlias'"
}
elseif (-not $AllowUnsigned) {
    throw "Android release builds must be signed. Pass -KeystorePath / secrets, or -AllowUnsigned for local debug only."
}
else {
    Write-Warning "Building UNSIGNED/debug-signed Android package (not suitable for upgrades)."
}

if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Android publish failed" }

$apk = Get-ChildItem -Path $PublishDir -Recurse -Filter "*.apk" |
    Where-Object { $_.Name -notmatch '-Signed\.apk$' -or $true } |
    Sort-Object Length -Descending |
    Select-Object -First 1
if (-not $apk) { throw "APK not found in publish output" }

# Prefer *-Signed.apk when present (dotnet Android naming)
$signed = Get-ChildItem -Path $PublishDir -Recurse -Filter "*-Signed.apk" | Select-Object -First 1
if ($signed) { $apk = $signed }

$zipPath = Join-Path $Dist "v2rayF-android-arm64.zip"
Copy-Item $apk.FullName (Join-Path $PublishDir "v2rayF-android-arm64.apk") -Force
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $PublishDir "v2rayF-android-arm64.apk") -DestinationPath $zipPath -Force

Write-Host "Created $zipPath from $($apk.Name)"
