namespace v2rayF.Services;

/// <summary>No-op protector for tests and early bootstrap before platform wiring.</summary>
public sealed class PassthroughSecretProtector : ISecretProtector
{
    public string Protect(string plaintext) => plaintext;

    public string Unprotect(string maybeProtected) => maybeProtected;

    public bool IsProtected(string value) => false;
}
