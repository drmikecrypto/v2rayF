using System;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using v2rayF.Services;

namespace v2rayF.Desktop.Services;

/// <summary>
/// Windows: AES key wrapped with DPAPI (CurrentUser).
/// macOS/Linux: AES key in a 0600 file under the data directory.
/// </summary>
public sealed class DesktopSecretProtector : ISecretProtector
{
    private readonly ISecretProtector _inner;

    public DesktopSecretProtector(ICoreEnvironment environment)
    {
        var dataDir = environment.GetDataDirectory();
        byte[] key;
        if (OperatingSystem.IsWindows())
        {
            key = SecretKeyMaterial.LoadOrCreate(
                dataDir,
                wrap: ProtectDpapi,
                unwrap: UnprotectDpapi);
        }
        else
        {
            key = SecretKeyMaterial.LoadOrCreate(dataDir);
        }

        _inner = new AesGcmSecretProtector(key);
    }

    public string Protect(string plaintext) => _inner.Protect(plaintext);

    public string Unprotect(string maybeProtected) => _inner.Unprotect(maybeProtected);

    public bool IsProtected(string value) => _inner.IsProtected(value);

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectDpapi(byte[] plain) =>
        ProtectedData.Protect(plain, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectDpapi(byte[] wrapped) =>
        ProtectedData.Unprotect(wrapped, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
}
