Place the Android Xray core here (not committed to git):

  libxray.so  — rename/copy from Xray-android-arm64-v8a.zip (xray binary)

Download: https://github.com/XTLS/Xray-core/releases

Run: pwsh -File scripts/package-android.ps1

Android 10+ cannot execute binaries from the app files directory (SELinux).
The core must be packaged as lib*.so under NativeLibs/<abi>/ so the system
extracts it to nativeLibraryDir, which allows exec().
