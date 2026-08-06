namespace v2rayF.Services;

/// <summary>Protects sensitive strings at rest (OS-backed key material when available).</summary>
public interface ISecretProtector
{
    /// <summary>Encrypts plaintext. Empty input is returned unchanged.</summary>
    string Protect(string plaintext);

    /// <summary>
    /// Decrypts a previously protected payload. Unmarked (plaintext legacy) values are returned as-is.
    /// </summary>
    string Unprotect(string maybeProtected);

    /// <summary>True when the value uses the protected encoding prefix.</summary>
    bool IsProtected(string value);
}
