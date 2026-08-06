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
}
