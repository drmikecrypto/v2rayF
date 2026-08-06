using System;

namespace v2rayF.Services;

/// <summary>Helpers to encrypt/decrypt known sensitive model fields.</summary>
public static class SecretFieldProtector
{
    public static string ProtectField(ISecretProtector protector, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? "";
        if (protector.IsProtected(value))
            return value;
        return protector.Protect(value);
    }

    public static string UnprotectField(ISecretProtector protector, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? "";
        try
        {
            return protector.Unprotect(value);
        }
        catch (Exception)
        {
            // Corrupted ciphertext — return empty rather than crashing load.
            return "";
        }
    }
}
