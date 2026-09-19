using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class ServerStoreSourceTests
{
    [Fact]
    public async Task RoundTrips_Source_Field()
    {
        var dir = Path.Combine(Path.GetTempPath(), "v2rayF-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // ServerStore uses AppServices data dir — serialize CloneForDisk shape directly
            var server = new ProxyServer
            {
                Name = "FreeNode",
                Protocol = ProxyProtocol.VLESS,
                Address = "1.2.3.4",
                Port = 443,
                Source = PulseFreeConstants.SourceId,
                LatencyMs = 42,
                RawLink = "vless://u@1.2.3.4:443"
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var path = Path.Combine(dir, "servers.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new List<ProxyServer> { server }, options));

            await using var stream = File.OpenRead(path);
            var loaded = await JsonSerializer.DeserializeAsync<List<ProxyServer>>(stream, options);
            Assert.NotNull(loaded);
            Assert.Single(loaded!);
            Assert.Equal(PulseFreeConstants.SourceId, loaded[0].Source);
            Assert.True(loaded[0].IsPulseFree);
            Assert.Equal(42, loaded[0].LatencyMs);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}
