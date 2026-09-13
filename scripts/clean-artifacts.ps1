#Requires -Version 7.0
<#
.SYNOPSIS
  Remove local build/release junk (D23) — dist/, release-assets/, _sandbox/, bin/obj optional.
#>
param(
    [switch]$IncludeBinObj
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot

$targets = @(
    (Join-Path $Root "dist"),
    (Join-Path $Root "release-assets"),
    (Join-Path $Root "_sandbox")
)

foreach ($path in $targets) {
    if (Test-Path -LiteralPath $path) {
        Write-Host "Removing $path"
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

if ($IncludeBinObj) {
    Get-ChildItem -LiteralPath (Join-Path $Root "src") -Directory -Recurse -Filter "bin" |
        ForEach-Object { Write-Host "Removing $($_.FullName)"; Remove-Item $_.FullName -Recurse -Force }
    Get-ChildItem -LiteralPath (Join-Path $Root "src") -Directory -Recurse -Filter "obj" |
        ForEach-Object { Write-Host "Removing $($_.FullName)"; Remove-Item $_.FullName -Recurse -Force }
}

Write-Host "clean-artifacts done."
