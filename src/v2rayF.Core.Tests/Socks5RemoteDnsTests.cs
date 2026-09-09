using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class Socks5RemoteDnsTests
{
    [Fact]
    public async Task ConnectAsync_SendsDomainAtyp_AndReturnsStream()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var greet = new byte[3];
            await ReadExact(stream, greet);
            Assert.Equal(new byte[] { 0x05, 0x01, 0x00 }, greet);
            await stream.WriteAsync(new byte[] { 0x05, 0x00 });

            var head = new byte[5];
            await ReadExact(stream, head);
            Assert.Equal(0x05, head[0]);
            Assert.Equal(0x01, head[1]); // CONNECT
            Assert.Equal(0x03, head[3]); // DOMAIN
            var hostLen = head[4];
            var hostAndPort = new byte[hostLen + 2];
            await ReadExact(stream, hostAndPort);
            var host = Encoding.UTF8.GetString(hostAndPort, 0, hostLen);
            var destPort = BinaryPrimitives.ReadUInt16BigEndian(hostAndPort.AsSpan(hostLen));
            Assert.Equal("cp.cloudflare.com", host);
            Assert.Equal(443, destPort);

            // Reply: success + bind IPv4 0.0.0.0:0
            await stream.WriteAsync(new byte[] { 0x05, 0x00, 0x00, 0x01, 0, 0, 0, 0, 0, 0 });
            // Keep half-open until client disposes
            await Task.Delay(200);
        });

        await using var connected = await Socks5RemoteDns.ConnectAsync(
            port, "cp.cloudflare.com", 443, CancellationToken.None);
        Assert.NotNull(connected);
        await serverTask;
        listener.Stop();
    }

    private static async Task ReadExact(Stream stream, byte[] buffer)
    {
        var o = 0;
        while (o < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(o));
            if (n == 0) throw new EndOfStreamException();
            o += n;
        }
    }
}
