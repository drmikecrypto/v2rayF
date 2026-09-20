# Requires: .NET SDK, network for Pulse Worker probe
# Usage: pwsh -File scripts/run-golden-matrix-lab.ps1
# Proves automated soak contracts for the current release pin.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$sandbox = Join-Path $root "_sandbox"
New-Item -ItemType Directory -Force -Path $sandbox | Out-Null
$report = Join-Path $sandbox "golden-matrix-lab-$stamp.md"

$lines = New-Object System.Collections.Generic.List[string]
function Log([string]$s) { $lines.Add($s); Write-Host $s }

Log "# Golden matrix lab — v2.6.4.2"
Log ""
Log "Generated: $(Get-Date -Format o)"
Log "Host: $env:COMPUTERNAME"
Log ""
Log "Private report — do not commit. Field phone / IR cells remain for a human."
Log ""

# --- Version pin ---
$props = Get-Content (Join-Path $root "Directory.Build.props") -Raw
if ($props -notmatch "<V2rayFVersion>2\.6\.4\.2</V2rayFVersion>") {
    throw "Expected V2rayFVersion 2.6.4.2 in Directory.Build.props"
}
Log "## Version"
Log "- Directory.Build.props: **2.6.4.2** OK"
Log ""

# --- Core regression (Android soak contracts encoded as tests) ---
Log "## Core.Tests (soak-related)"
dotnet test (Join-Path $root "src/v2rayF.Core.Tests/v2rayF.Core.Tests.csproj") `
    -c Release --verbosity minimal `
    --filter "FullyQualifiedName~DesktopTun|FullyQualifiedName~GamingBoost|FullyQualifiedName~PulseFree|FullyQualifiedName~Scorecard|FullyQualifiedName~NetworkProfiles|FullyQualifiedName~GoldenMatrix"
if ($LASTEXITCODE -ne 0) { throw "Filtered Core.Tests failed" }
Log "- Filtered soak contract tests: **PASS**"
Log ""

# Full suite once — trust bar for release branch
Log "## Core.Tests (full)"
dotnet test (Join-Path $root "src/v2rayF.Core.Tests/v2rayF.Core.Tests.csproj") -c Release --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "Full Core.Tests failed" }
Log "- Full Core.Tests: **PASS**"
Log ""

# --- Windows TUN honesty (adapter absent = fail-closed path) ---
Log "## Windows TUN honesty (lab)"
$adapter = Get-NetAdapter -Name "v2rayF" -ErrorAction SilentlyContinue
if ($null -eq $adapter) {
    Log "- Adapter ``v2rayF``: **absent** (idle machine) — fail-closed contract covered by DesktopTunInterfaceTests"
    Log "- Manual Connected checks still required with Admin + TUN on (see tip runbook)"
} else {
    Log "- Adapter ``v2rayF``: **present** Status=$($adapter.Status) — confirm kill switch only when Connected intentionally"
}
Log ""

# --- Free / Pulse Worker ---
Log "## Free / Pulse Worker"
$workerBase = "https://pulseconfigs-mirror.drmikecrypto.workers.dev"
$candidates = @(
    "$workerBase/candidates.json",
    "$workerBase/top5.txt",
    "$workerBase/index.json"
)
$ok = $false
foreach ($url in $candidates) {
    try {
        $resp = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 25 -Headers @{ "User-Agent" = "v2rayF-GoldenMatrixLab/1.0" }
        if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 300 -and $resp.Content.Length -gt 0) {
            Log "- GET $url → **$($resp.StatusCode)** ($($resp.Content.Length) bytes)"
            $ok = $true
            break
        }
    } catch {
        Log "- GET $url → fail ($($_.Exception.Message))"
    }
}
if (-not $ok) { throw "Pulse Worker shortlist unreachable from this network" }
Log "- Default Worker shortlist: **reachable**"
Log ""

# --- Scorecard scaffold for field ---
Log "## Field scorecard scaffold"
$field = Join-Path $sandbox "field-scorecard-2.6.4.2.md"
@"
# Field scorecard — v2.6.4.2 (private)

Fill on phone / IR path. Do not commit.

| Check | Pass | Notes |
|-------|------|-------|
| Chrome HTTPS | | |
| Instagram feed | | |
| Instagram Direct | | |
| WhatsApp chat | | |
| Telegram chat/media | | |
| YouTube playback | | |
| Maps load/search | | |
| Play Services FCM push | | |
| UDP game or voice (Gaming Boost) | | |
| Lock unlock Chrome without app | | |
| Windows TUN adapter present | | |
| Windows no Connected blackhole | | |
| Free Worker shortlist | | |
| Free slots ≤5 pulse-free | | |
| Free prefer 150 fill 450 | | |
| Gaming Boost UDP vs V2Box | | |

Phase C unlock: field soak of 2.6.4.1 accepted — Phase C open in PLAN (this lab is regression only).
"@ | Set-Content -Path $field -Encoding utf8
Log "- Wrote $field"
Log ""

Log "## Lab summary"
Log "- Automated gates: **GREEN**"
Log "- Field phone soak: **PENDING** (no adb / IR device in this lab)"
Log "- Phase C: **OPEN** (2.6.4.2 ships first slice; lab is regression evidence)"
Log ""

$lines | Set-Content -Path $report -Encoding utf8
Write-Host ""
Write-Host "Report: $report"
Write-Host "Field scaffold: $field"
exit 0
