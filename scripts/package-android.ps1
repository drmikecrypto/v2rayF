#Requires -Version 7.0
param(
    [string]$XrayVersion = "v26.7.28",
    [string]$SingBoxVersion = "1.12.12",
    [switch]$AllowUnpatchedSingBox
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Tools = Join-Path $Root ".tools\xray"
$SingBoxTools = Join-Path $Root ".tools\sing-box"
$Assets = Join-Path $Root "src\v2rayF.Android\Assets"
$NativeLibs = Join-Path $Root "src\v2rayF.Android\NativeLibs\arm64-v8a"
$PatchFile = Join-Path $Root "scripts\patches\sing-box-android-tun-fd.patch"
$ZipName = "Xray-android-arm64-v8a.zip"
$BaseUrl = "https://github.com/XTLS/Xray-core/releases/download/$XrayVersion/$ZipName"
$SingBoxTags = "with_gvisor,with_quic,with_dhcp,with_wireguard,with_utls,with_acme,with_clash_api"

function Resolve-AndroidNdkClang([string]$Triple) {
    $ndk = $env:ANDROID_NDK_HOME
    if ([string]::IsNullOrWhiteSpace($ndk)) { $ndk = $env:NDK_ROOT }
    if ([string]::IsNullOrWhiteSpace($ndk)) { return $null }

    $prebuilt = Join-Path $ndk "toolchains\llvm\prebuilt"
    if (-not (Test-Path $prebuilt)) { return $null }

    $hostDir = Get-ChildItem -Path $prebuilt -Directory | Select-Object -First 1
    if (-not $hostDir) { return $null }

    $bin = Join-Path $hostDir.FullName "bin"
    if ($IsLinux) {
        $clang = Join-Path $bin "$Triple-clang"
    }
    elseif ($IsWindows) {
        $clang = Join-Path $bin "$Triple-clang.cmd"
        if (-not (Test-Path $clang)) { $clang = Join-Path $bin "$Triple-clang.exe" }
    }
    else {
        return $null
    }

    if (-not (Test-Path $clang)) { return $null }
    return $clang
}

function Apply-SingBoxTunFdPatch([string]$InboundPath) {
    $content = Get-Content -LiteralPath $InboundPath -Raw
    if ($content -match 'SING_BOX_TUN_FD') {
        Write-Host "sing-box TUN fd patch already applied."
        return
    }

    $needle = @'
			if HookBeforeCreatePlatformInterface != nil {
				HookBeforeCreatePlatformInterface()
			}
			tunInterface, err = tun.New(tunOptions)
'@
    $replacement = @'
			if HookBeforeCreatePlatformInterface != nil {
				HookBeforeCreatePlatformInterface()
			}
			if C.IsAndroid && tunOptions.FileDescriptor == 0 {
				if fdStr := os.Getenv("SING_BOX_TUN_FD"); fdStr != "" {
					if fd, parseErr := strconv.Atoi(fdStr); parseErr == nil && fd > 0 {
						tunOptions.FileDescriptor = fd
					}
				}
			}
			tunInterface, err = tun.New(tunOptions)
'@

    if (-not $content.Contains($needle)) {
        if (Test-Path $PatchFile) {
            Push-Location (Split-Path -Parent $InboundPath)
            try {
                git apply $PatchFile
                Write-Host "Applied sing-box patch via git apply."
                return
            }
            catch {
                throw "Could not apply sing-box TUN fd patch to $InboundPath: $($_.Exception.Message)"
            }
            finally {
                Pop-Location
            }
        }

        throw "Could not locate sing-box TUN fd patch anchor in $InboundPath"
    }

    Set-Content -LiteralPath $InboundPath -Value ($content.Replace($needle, $replacement)) -NoNewline
    Write-Host "Applied sing-box Android TUN fd patch."
}

function Build-PatchedSingBoxAndroid([string]$Version, [string]$OutPath) {
    if (-not (Get-Command go -ErrorAction SilentlyContinue)) {
        throw "Go is required to build patched sing-box for Android."
    }

    $cc = Resolve-AndroidNdkClang "aarch64-linux-android21"
    if (-not $cc) {
        throw "Android NDK clang not found (set ANDROID_NDK_HOME)."
    }

    $srcRoot = Join-Path $SingBoxTools "src-v$Version"
    if (-not (Test-Path $srcRoot)) {
        $srcArchive = Join-Path $SingBoxTools "sing-box-v$Version-src.tar.gz"
        if (-not (Test-Path $srcArchive)) {
            Write-Host "Downloading sing-box v$Version source ..."
            Invoke-WebRequest -Uri "https://github.com/SagerNet/sing-box/archive/refs/tags/v$Version.tar.gz" `
                -OutFile $srcArchive -UserAgent "v2rayF-setup"
        }
        New-Item -ItemType Directory -Force -Path $SingBoxTools | Out-Null
        tar -xzf $srcArchive -C $SingBoxTools
        $extracted = Get-ChildItem -Path $SingBoxTools -Directory |
            Where-Object { $_.Name -like "sing-box-*" } |
            Sort-Object Name -Descending |
            Select-Object -First 1
        if (-not $extracted) { throw "sing-box source extract failed." }
        Move-Item $extracted.FullName $srcRoot -Force
    }

    $inbound = Join-Path $srcRoot "protocol\tun\inbound.go"
    if (-not (Test-Path $inbound)) { throw "Missing $inbound" }
    Apply-SingBoxTunFdPatch $inbound

    Write-Host "Building patched libsingbox.so (sing-box v$Version, android/arm64) ..."
    Push-Location $srcRoot
    try {
        $env:CGO_ENABLED = "1"
        $env:GOOS = "android"
        $env:GOARCH = "arm64"
        $env:CC = $cc
        $cxx = $cc -replace '-clang(\.cmd|\.exe)?$', '-clang++$1'
        if (Test-Path $cxx) { $env:CXX = $cxx }

        $ldflags = "-s -w"
        & go build -v -trimpath -tags $SingBoxTags -ldflags $ldflags `
            -o $OutPath ./cmd/sing-box
        if ($LASTEXITCODE -ne 0) { throw "go build sing-box failed with exit code $LASTEXITCODE." }
    }
    finally {
        Pop-Location
        Remove-Item Env:CGO_ENABLED, Env:GOOS, Env:GOARCH, Env:CC, Env:CXX -ErrorAction SilentlyContinue
    }
}

function Install-StockSingBoxAndroid([string]$Version, [string]$OutPath) {
    $SingBoxAsset = "sing-box-$Version-android-arm64.tar.gz"
    $SingBoxUrl = "https://github.com/SagerNet/sing-box/releases/download/v$Version/$SingBoxAsset"
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
    $singBox = Get-ChildItem -Path $sbExtract -Recurse -File |
        Where-Object { $_.Name -eq "sing-box" } |
        Select-Object -First 1
    if (-not $singBox) { throw "Missing sing-box binary in $SingBoxAsset" }
    Copy-Item $singBox.FullName $OutPath -Force
}

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

$singBoxOut = Join-Path $NativeLibs "libsingbox.so"
$built = $false
try {
    Build-PatchedSingBoxAndroid -Version $SingBoxVersion -OutPath $singBoxOut
    $built = $true
    Write-Host "Installed patched libsingbox.so (SING_BOX_TUN_FD support)"
}
catch {
    if (-not $AllowUnpatchedSingBox) {
        throw
    }
    Write-Warning "Patched sing-box build failed: $($_.Exception.Message)"
    Install-StockSingBoxAndroid -Version $SingBoxVersion -OutPath $singBoxOut
    Write-Warning "Installed STOCK libsingbox.so — Android VPN Connect will fail until NDK+Go build succeeds."
}

Write-Host "Android assets ready in src/v2rayF.Android/Assets/ and NativeLibs/arm64-v8a/"
