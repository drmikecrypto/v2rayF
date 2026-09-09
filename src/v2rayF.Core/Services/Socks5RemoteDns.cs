using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

/// <summary>
/// SOCKS5 CONNECT with ATYP=DOMAIN so the core resolves DNS (socks5h semantics).
/// .NET <see cref="System.Net.WebProxy"/> only accepts scheme socks5 (local DNS); poisoned
/// clearnet DNS would fail every gen204 host even when the tunnel works.
/// </summary>
public static class Socks5RemoteDns
{
    public static async Task<Stream> ConnectAsync(
        int socksPort,
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        Stream? stream = null;
        try
        {
            await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, socksPort), cancellationToken)
                .ConfigureAwait(false);
            stream = new NetworkStream(socket, ownsSocket: true);
            socket = null; // owned by stream
            await HandshakeAndConnectAsync(stream, host, port, cancellationToken).ConfigureAwait(false);
            return stream;
        }
        catch
        {
            if (stream is not null)
                await stream.DisposeAsync().ConfigureAwait(false);
            else
                socket?.Dispose();
            throw;
        }
    }

    private static async Task HandshakeAndConnectAsync(
        Stream stream,
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, cancellationToken).ConfigureAwait(false);
        var method = new byte[2];
        await ReadExactAsync(stream, method, cancellationToken).ConfigureAwait(false);
        if (method[0] != 0x05 || method[1] != 0x00)
            throw new IOException($"SOCKS5 auth rejected (ver={method[0]}, method={method[1]}).");

        var hostBytes = Encoding.UTF8.GetBytes(host);
        if (hostBytes.Length is 0 or > 255)
            throw new ArgumentException("SOCKS5 domain name length invalid.", nameof(host));

        var req = new byte[4 + 1 + hostBytes.Length + 2];
        req[0] = 0x05;
        req[1] = 0x01; // CONNECT
        req[2] = 0x00;
        req[3] = 0x03; // DOMAIN
        req[4] = (byte)hostBytes.Length;
        hostBytes.CopyTo(req, 5);
        BinaryPrimitives.WriteUInt16BigEndian(req.AsSpan(5 + hostBytes.Length), (ushort)port);
        await stream.WriteAsync(req, cancellationToken).ConfigureAwait(false);

        var head = new byte[4];
        await ReadExactAsync(stream, head, cancellationToken).ConfigureAwait(false);
        if (head[0] != 0x05)
            throw new IOException($"SOCKS5 bad reply version {head[0]}.");
        if (head[1] != 0x00)
            throw new IOException($"SOCKS5 CONNECT failed (rep={head[1]}).");

        await SkipBindAddressAsync(stream, head[3], cancellationToken).ConfigureAwait(false);
    }

    private static async Task SkipBindAddressAsync(Stream stream, byte atyp, CancellationToken cancellationToken)
    {
        switch (atyp)
        {
            case 0x01: // IPv4
                await ReadExactAsync(stream, new byte[4 + 2], cancellationToken).ConfigureAwait(false);
                break;
            case 0x03: // DOMAIN
            {
                var lenBuf = new byte[1];
                await ReadExactAsync(stream, lenBuf, cancellationToken).ConfigureAwait(false);
                await ReadExactAsync(stream, new byte[lenBuf[0] + 2], cancellationToken).ConfigureAwait(false);
                break;
            }
            case 0x04: // IPv6
                await ReadExactAsync(stream, new byte[16 + 2], cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new IOException($"SOCKS5 unknown ATYP {atyp}.");
        }
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("SOCKS5 stream closed early.");
            offset += read;
        }
    }
}
