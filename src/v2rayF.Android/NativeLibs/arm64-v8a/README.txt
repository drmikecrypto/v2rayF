Place Android cores here (not committed to git):

  libxray.so     — from Xray-android-arm64-v8a.zip (xray binary)
  libsingbox.so  — from sing-box-*-android-arm64.tar.gz (sing-box binary)

Download:
  https://github.com/XTLS/Xray-core/releases
  https://github.com/SagerNet/sing-box/releases

Run: pwsh -File scripts/package-android.ps1

Android 10+ cannot execute binaries from the app files directory (SELinux).
Cores must be packaged as lib*.so under NativeLibs/<abi>/ so the system
extracts them to nativeLibraryDir, which allows exec(). In-app Update installs
a new APK that refreshes these native libs — uninstall is not required.
