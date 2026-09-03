using System.Net;
using System.Net.Http;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class LatencySocksProxyTests
{
    [Fact]
    public void SocksProxyScheme_IsSocks5_NotSocks5h()
    {
        Assert.Equal("socks5", LatencyService.SocksProxyScheme);
        Assert.DoesNotContain("socks5h", LatencyService.SocksProxyScheme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProbeUrls_CloudflareFirst_RacedFallbacks_NotHomepage()
    {
        Assert.Equal("https://cp.cloudflare.com/generate_204", LatencyService.PingUrls[0]);
        Assert.Equal(LatencyService.GoogleProbeUrl, LatencyService.PingUrls[0]);
        Assert.DoesNotContain(LatencyService.PingUrls, u => u == "https://www.google.com/");
        Assert.Equal(1, LatencyService.TimedProbeCount);
        Assert.Equal(1, LatencyService.ConnectHealthTimedProbeCount);
        Assert.Equal(2000, LatencyService.HttpConnectTimeoutMs);
        Assert.Equal(12000, LatencyService.ConnectHealthProbeMs);
        Assert.Equal(16000, LatencyService.ConnectHealthProbeVisionMs);
        Assert.Equal(50, LatencyService.SocksPollTimeoutMs);
        Assert.Equal(3, LatencyService.ResolveWorkerCount(mobile: false));
        Assert.Equal(2, LatencyService.ResolveWorkerCount(mobile: true));
    }

    [Fact]
    public void UiLatencyMs_ShowsTcpOnlyWhenProxyPathOk()
    {
        var ok = new LatencyService.LatencyResult(TcpMs: 102, ProxyPathMs: 1800, ProxyPathOk: true);
        Assert.Equal(102, ok.UiLatencyMs);
        Assert.Equal(1800, ok.LatencyMs);

        var dead = new LatencyService.LatencyResult(TcpMs: 98, ProxyPathMs: -1, ProxyPathOk: false);
        Assert.Equal(-1, dead.UiLatencyMs);

        var domainWs = new LatencyService.LatencyResult(TcpMs: -1, ProxyPathMs: 240, ProxyPathOk: true);
        Assert.Equal(240, domainWs.UiLatencyMs);
    }

    [Fact]
    public void RankedServer_UiLatencyPrefersTcp()
    {
        var server = new ProxyServer { Name = "n", Address = "1.1.1.1", Port = 443 };
        var ranked = new SmartConnectService.RankedServer(server, Score: 200, LatencyMs: 200, ProxyPathOk: true, TcpMs: 110);
        Assert.Equal(110, ranked.UiLatencyMs);

        var failed = new SmartConnectService.RankedServer(server, Score: int.MaxValue - 1, LatencyMs: 40, ProxyPathOk: false, TcpMs: 40);
        Assert.Equal(-1, failed.UiLatencyMs);
    }

    [Fact]
    public async Task DotNet_HttpClient_RejectsSocks5h_AcceptsSocks5Scheme()
    {
        // Construction alone may accept socks5h; SocketsHttpHandler rejects it on send.
        using (var bad = new HttpClient(new SocketsHttpHandler
        {
            Proxy = new WebProxy("socks5h://127.0.0.1:1"),
            UseProxy = true
        }))
        {
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => bad.GetAsync("https://example.com/"));
            Assert.True(
                ex is NotSupportedException ||
                ex.InnerException is NotSupportedException ||
                ex.Message.Contains("socks", StringComparison.OrdinalIgnoreCase) ||
                (ex.InnerException?.Message.Contains("socks", StringComparison.OrdinalIgnoreCase) ?? false),
                $"Expected SOCKS scheme rejection, got {ex.GetType().Name}: {ex.Message}");
        }

        // socks5 is an allowed scheme; connect to closed port fails with a connect/network error, not scheme.
        using var good = new HttpClient(new SocketsHttpHandler
        {
            Proxy = new WebProxy("socks5://127.0.0.1:1"),
            UseProxy = true,
            ConnectTimeout = TimeSpan.FromMilliseconds(200)
        })
        {
            Timeout = TimeSpan.FromSeconds(1)
        };

        var connectEx = await Assert.ThrowsAnyAsync<Exception>(() => good.GetAsync("https://example.com/"));
        Assert.False(connectEx is NotSupportedException);
        Assert.False(connectEx.InnerException is NotSupportedException);
    }
}
