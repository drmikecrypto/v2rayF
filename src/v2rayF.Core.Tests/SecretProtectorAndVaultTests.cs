using System.Security.Cryptography;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class SecretProtectorAndVaultTests
{
    [Fact]
    public void AesGcmSecretProtector_RoundTrips()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var protector = new AesGcmSecretProtector(key);
        var sealedValue = protector.Protect("super-secret");
        Assert.True(protector.IsProtected(sealedValue));
        Assert.False(sealedValue == "super-secret");
        Assert.Equal("super-secret", protector.Unprotect(sealedValue));
    }

    [Fact]
    public void ProfileVault_ExportImport_RoundTripsServers()
    {
        var vault = new ProfileVault();
        var servers = new List<ProxyServer>
        {
            new()
            {
                Name = "A",
                Address = "1.2.3.4",
                Port = 443,
                UserId = "uuid-here",
                Protocol = ProxyProtocol.VLESS,
                RawLink = "vless://uuid-here@1.2.3.4:443"
            }
        };
        var bytes = vault.Export(servers, new AppSettings { DnsThroughProxy = true }, "passphrase-long");
        var payload = vault.Import(bytes, "passphrase-long");
        Assert.Single(payload.Servers);
        Assert.Equal("A", payload.Servers[0].Name);
        Assert.Equal("uuid-here", payload.Servers[0].UserId);
        Assert.True(payload.Settings!.DnsThroughProxy);
    }

    [Fact]
    public void ProfileVault_WrongPassphrase_Throws()
    {
        var vault = new ProfileVault();
        var bytes = vault.Export([], new AppSettings(), "correct-passphrase");
        Assert.Throws<InvalidOperationException>(() => vault.Import(bytes, "wrong-passphrase"));
    }
}
