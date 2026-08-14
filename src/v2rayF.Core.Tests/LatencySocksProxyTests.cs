using System.Net;
using System.Net.Http;
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
