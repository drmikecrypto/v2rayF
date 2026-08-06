using System;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using v2rayF.Services;
using AndroidKeyGenParameterSpec = global::Android.Security.Keystore.KeyGenParameterSpec;
using AndroidKeyProperties = global::Android.Security.Keystore.KeyProperties;
using AndroidKeyStorePurpose = global::Android.Security.Keystore.KeyStorePurpose;

namespace v2rayF.Android.Services;

/// <summary>
/// AES key stored in app-private files, wrapped by Android Keystore (AES/GCM).
/// Falls back to a 0600-style private file when Keystore init fails.
/// </summary>
public sealed class AndroidSecretProtector : ISecretProtector
{
    private const string KeystoreAlias = "v2rayF.master";
    private const string AndroidKeyStore = "AndroidKeyStore";
    private readonly ISecretProtector _inner;

    public AndroidSecretProtector(ICoreEnvironment environment)
    {
        var dataDir = environment.GetDataDirectory();
        byte[] key;
        try
        {
            EnsureKeystoreKey();
            key = SecretKeyMaterial.LoadOrCreate(
                dataDir,
                wrap: WrapWithKeystore,
                unwrap: UnwrapWithKeystore);
        }
        catch
        {
            key = SecretKeyMaterial.LoadOrCreate(dataDir);
        }

        _inner = new AesGcmSecretProtector(key);
    }

    public string Protect(string plaintext) => _inner.Protect(plaintext);

    public string Unprotect(string maybeProtected) => _inner.Unprotect(maybeProtected);

    public bool IsProtected(string value) => _inner.IsProtected(value);

    private static void EnsureKeystoreKey()
    {
        var ks = KeyStore.GetInstance(AndroidKeyStore)!;
        ks.Load(null);
        if (ks.ContainsAlias(KeystoreAlias))
            return;

        var builder = new AndroidKeyGenParameterSpec.Builder(
                KeystoreAlias,
                AndroidKeyStorePurpose.Encrypt | AndroidKeyStorePurpose.Decrypt)
            .SetBlockModes(AndroidKeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(AndroidKeyProperties.EncryptionPaddingNone)
            .SetKeySize(256);

        var keyGen = KeyGenerator.GetInstance(
            AndroidKeyProperties.KeyAlgorithmAes,
            AndroidKeyStore)!;
        keyGen.Init(builder.Build());
        keyGen.GenerateKey();
    }

    private static byte[] WrapWithKeystore(byte[] plain)
    {
        var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        var ks = KeyStore.GetInstance(AndroidKeyStore)!;
        ks.Load(null);
        var entry = (KeyStore.SecretKeyEntry)ks.GetEntry(KeystoreAlias, null)!;
        cipher.Init(CipherMode.EncryptMode, entry.SecretKey);
        var iv = cipher.GetIV()!;
        var cipherText = cipher.DoFinal(plain)!;
        var packed = new byte[1 + iv.Length + cipherText.Length];
        packed[0] = (byte)iv.Length;
        Buffer.BlockCopy(iv, 0, packed, 1, iv.Length);
        Buffer.BlockCopy(cipherText, 0, packed, 1 + iv.Length, cipherText.Length);
        return packed;
    }

    private static byte[] UnwrapWithKeystore(byte[] wrapped)
    {
        if (wrapped.Length < 2)
            throw new InvalidOperationException("Wrapped key truncated.");

        var ivLen = wrapped[0];
        if (wrapped.Length < 1 + ivLen)
            throw new InvalidOperationException("Wrapped key IV truncated.");

        var iv = new byte[ivLen];
        Buffer.BlockCopy(wrapped, 1, iv, 0, ivLen);
        var cipherText = new byte[wrapped.Length - 1 - ivLen];
        Buffer.BlockCopy(wrapped, 1 + ivLen, cipherText, 0, cipherText.Length);

        var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        var ks = KeyStore.GetInstance(AndroidKeyStore)!;
        ks.Load(null);
        var entry = (KeyStore.SecretKeyEntry)ks.GetEntry(KeystoreAlias, null)!;
        cipher.Init(CipherMode.DecryptMode, entry.SecretKey, new GCMParameterSpec(128, iv));
        return cipher.DoFinal(cipherText)!;
    }
}
