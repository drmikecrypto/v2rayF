using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class StatusSanitizerTests
{
    [Fact]
    public void Scrub_RedactsUuidAndShareLinks()
    {
        var input = "fail uuid 11111111-2222-3333-4444-555555555555 vless://abc@host:443 extra";
        var scrubbed = StatusSanitizer.Scrub(input);
        Assert.DoesNotContain("11111111-2222-3333-4444-555555555555", scrubbed);
        Assert.Contains("[id]", scrubbed);
        Assert.Contains("vless://[redacted]", scrubbed);
        Assert.DoesNotContain("abc@host", scrubbed);
    }

    [Fact]
    public void Scrub_RedactsModernSchemes()
    {
        foreach (var scheme in new[] { "hy2", "tuic", "anytls", "wg", "wireguard" })
        {
            var scrubbed = StatusSanitizer.Scrub($"{scheme}://secret@host:443#n");
            Assert.Contains($"{scheme}://[redacted]", scrubbed, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret@", scrubbed);
        }
    }

    [Fact]
    public void Scrub_CollapsesLogcatDumps()
    {
        var dump = "error\n--------- beginning of main\n09-13 12:00:00.000  1234/v2rayF  E  leak\nmore";
        var scrubbed = StatusSanitizer.Scrub(dump);
        Assert.Contains("[logcat]", scrubbed);
        Assert.DoesNotContain("beginning of main", scrubbed);
    }
}
