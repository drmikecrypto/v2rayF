#Requires -Version 7.0
param(
    [string[]]$Rid = @(),
    [string]$XrayVersion = "v26.3.27"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $Root "src\v2rayF.Desktop\v2rayF.Desktop.csproj"
$Dist = Join-Path $Root "dist"
$Tools = Join-Path $Root ".tools\xray"
$Tag = $XrayVersion
$BaseUrl = "https://github.com/XTLS/Xray-core/releases/download/$Tag"

$AllTargets = @(
    @{ Rid = "win-x64";      Zip = "Xray-windows-64.zip";        CoreName = "xray.exe" },
    @{ Rid = "win-arm64";    Zip = "Xray-windows-arm64-v8a.zip"; CoreName = "xray.exe" },
    @{ Rid = "linux-x64";    Zip = "Xray-linux-64.zip";          CoreName = "xray" },
    @{ Rid = "linux-arm64";  Zip = "Xray-linux-arm64-v8a.zip";   CoreName = "xray" },
    @{ Rid = "osx-x64";      Zip = "Xray-macos-64.zip";          CoreName = "xray" },
    @{ Rid = "osx-arm64";    Zip = "Xray-macos-arm64-v8a.zip";   CoreName = "xray" }
)

if ($Rid.Count -gt 0) {
    $Targets = $AllTargets | Where-Object { $Rid -contains $_.Rid }
    if ($Targets.Count -eq 0) { throw "Unknown -Rid value. Valid: $($AllTargets.Rid -join ', ')" }
} else {
    $Targets = $AllTargets
}

New-Item -ItemType Directory -Force -Path $Dist, $Tools | Out-Null

function Get-XrayBundle {
    param([string]$ZipName, [string]$CoreName)
    $zipPath = Join-Path $Tools $ZipName
    $extractDir = Join-Path $Tools ($ZipName -replace '\.zip$', '')

    if (-not (Test-Path $zipPath)) {
        Write-Host "Downloading $ZipName ..."
        Invoke-WebRequest -Uri "$BaseUrl/$ZipName" -OutFile $zipPath -UserAgent "v2rayF-setup"
    }

    if (-not (Test-Path $extractDir)) {
        Write-Host "Extracting $ZipName ..."
        Expand-Archive -Path $zipPath -DestinationPath $extractDir -Force
    }

    $binary = Get-ChildItem -Path $extractDir -Recurse -File | Where-Object { $_.Name -eq $CoreName } | Select-Object -First 1
    if (-not $binary) { throw "Could not find $CoreName in $ZipName" }

    $geoFiles = @()
    foreach ($geoName in @("geoip.dat", "geosite.dat")) {
        $geo = Get-ChildItem -Path $extractDir -Recurse -File -Filter $geoName | Select-Object -First 1
        if ($geo) { $geoFiles += $geo.FullName }
    }

    return @{ Binary = $binary.FullName; GeoFiles = $geoFiles }
}

function Install-CoresBundle {
    param([string]$DestDir, [hashtable]$Bundle, [string]$CoreName)
    New-Item -ItemType Directory -Force -Path $DestDir | Out-Null
    Copy-Item -Path $Bundle.Binary -Destination (Join-Path $DestDir $CoreName) -Force
    foreach ($geo in $Bundle.GeoFiles) {
        Copy-Item -Path $geo -Destination (Join-Path $DestDir (Split-Path $geo -Leaf)) -Force
    }
}

if ($Rid.Count -eq 0 -and $IsWindows) {
    $winBundle = Get-XrayBundle -ZipName "Xray-windows-64.zip" -CoreName "xray.exe"
    $coresDir = Join-Path $Root "src\v2rayF.Desktop\cores"
    Install-CoresBundle -DestDir $coresDir -Bundle $winBundle -CoreName "xray.exe"
    Write-Host "Installed dev cores to src\v2rayF\cores\"
}

foreach ($t in $Targets) {
    $rid = $t.Rid
    $outDir = Join-Path $Dist "v2rayF-$rid"
    $publishDir = Join-Path $outDir "publish"

    Write-Host "`n=== Publishing v2rayF for $rid ==="
    if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
    New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

    dotnet publish $Project -c Release -r $rid --self-contained true `
        -p:PublishSingleFile=false `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $rid" }

    if ($rid -like "win-*") {
        $desktopExe = Join-Path $publishDir "v2rayF.Desktop.exe"
        $desktopDeps = Join-Path $publishDir "v2rayF.Desktop.deps.json"
        $desktopRuntime = Join-Path $publishDir "v2rayF.Desktop.runtimeconfig.json"
        if (Test-Path $desktopExe) {
            Move-Item -Force $desktopExe (Join-Path $publishDir "v2rayF.exe")
        }
        if (Test-Path $desktopDeps) {
            Move-Item -Force $desktopDeps (Join-Path $publishDir "v2rayF.deps.json")
        }
        if (Test-Path $desktopRuntime) {
            Move-Item -Force $desktopRuntime (Join-Path $publishDir "v2rayF.runtimeconfig.json")
        }
    }

    $bundle = Get-XrayBundle -ZipName $t.Zip -CoreName $t.CoreName
    Install-CoresBundle -DestDir (Join-Path $publishDir "cores") -Bundle $bundle -CoreName $t.CoreName

    if ($rid -like "linux-*") {
        Copy-Item (Join-Path $Root "scripts\run-linux.sh") (Join-Path $publishDir "run-v2rayF.sh") -Force
    }
    elseif ($rid -like "osx-*") {
        Copy-Item (Join-Path $Root "scripts\run-macos.sh") (Join-Path $publishDir "run-v2rayF.sh") -Force
    }

    $zipPath = Join-Path $Dist "v2rayF-$rid.zip"
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
    Write-Host "Created $zipPath"
}

Write-Host "`nDone. Distributions are in: $Dist"
