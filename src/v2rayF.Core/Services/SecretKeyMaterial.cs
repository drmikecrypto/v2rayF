using System;
using System.IO;
using System.Security.Cryptography;

namespace v2rayF.Services;

/// <summary>
/// Loads or creates a 32-byte AES key from a file (Unix mode 0600 when supported).
/// Optional <paramref name="wrap"/> / <paramref name="unwrap"/> let platforms store the key via DPAPI/Keystore.
/// </summary>
public static class SecretKeyMaterial
{
    public const string KeyFileName = "secret.key";
    public const string WrappedKeyFileName = "secret.key.wrapped";

    public static byte[] LoadOrCreate(
        string dataDirectory,
        Func<byte[], byte[]>? wrap = null,
        Func<byte[], byte[]>? unwrap = null)
    {
        Directory.CreateDirectory(dataDirectory);

        if (wrap is not null && unwrap is not null)
        {
            var wrappedPath = Path.Combine(dataDirectory, WrappedKeyFileName);
            if (File.Exists(wrappedPath))
            {
                var wrapped = File.ReadAllBytes(wrappedPath);
                return unwrap(wrapped);
            }

            var key = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(wrappedPath, wrap(key));
            TryRestrictUnix(wrappedPath);
            return key;
        }

        var path = Path.Combine(dataDirectory, KeyFileName);
        if (File.Exists(path))
        {
            var existing = File.ReadAllBytes(path);
            if (existing.Length == 32)
                return existing;
        }

        var fresh = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(path, fresh);
        TryRestrictUnix(path);
        return fresh;
    }

    private static void TryRestrictUnix(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Best-effort ACL tightening.
        }
    }
}
