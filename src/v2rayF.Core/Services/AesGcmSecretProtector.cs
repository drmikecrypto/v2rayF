using System;
using System.Security.Cryptography;
using System.Text;

namespace v2rayF.Services;

/// <summary>AES-GCM protector using a 32-byte key. Payload format: v2enc:base64(nonce|tag|cipher).</summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    public const string Prefix = "v2enc:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public AesGcmSecretProtector(byte[] key)
    {
        if (key is null || key.Length != 32)
            throw new ArgumentException("AES-GCM key must be 32 bytes.", nameof(key));
        _key = (byte[])key.Clone();
    }

    public bool IsProtected(string value) =>
        !string.IsNullOrEmpty(value) && value.StartsWith(Prefix, StringComparison.Ordinal);

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return plaintext;

        if (IsProtected(plaintext))
            return plaintext;

        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        var packed = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, packed, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, packed, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, packed, NonceSize + TagSize, cipher.Length);
        return Prefix + Convert.ToBase64String(packed);
    }

    public string Unprotect(string maybeProtected)
    {
        if (string.IsNullOrEmpty(maybeProtected) || !IsProtected(maybeProtected))
            return maybeProtected;

        var packed = Convert.FromBase64String(maybeProtected[Prefix.Length..]);
        if (packed.Length < NonceSize + TagSize)
            throw new CryptographicException("Protected payload is truncated.");

        var nonce = packed.AsSpan(0, NonceSize);
        var tag = packed.AsSpan(NonceSize, TagSize);
        var cipher = packed.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
