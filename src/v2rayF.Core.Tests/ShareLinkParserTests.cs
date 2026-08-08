using System.Text.Json.Nodes;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class ShareLinkParserTests
{
    [Fact]
    public void Parse_VlessVisionReality_ExtractsFields()
    {
        var link =
            "vless://11111111-2222-3333-4444-555555555555@example.com:443" +
            "?encryption=none&flow=xtls-rprx-vision&security=reality&sni=www.microsoft.com" +
            "&fp=chrome&pbk=publicKeyHere&sid=abcd&type=tcp&spx=%2F&alpn=h2%2Chttp%2F1.1#MyNode";

        var server = ShareLinkParser.Parse(link);
        Assert.NotNull(server);
        Assert.Equal(ProxyProtocol.VLESS, server!.Protocol);
        Assert.Equal("example.com", server.Address);
        Assert.Equal(443, server.Port);
        Assert.Equal("11111111-2222-3333-4444-555555555555", server.UserId);
        Assert.Equal("reality", server.Security);
        Assert.Equal("xtls-rprx-vision", server.Flow);
        Assert.Equal("www.microsoft.com", server.Sni);
        Assert.Equal("publicKeyHere", server.PublicKey);
        Assert.Equal("abcd", server.ShortId);
        Assert.Equal("h2,http/1.1", server.Alpn);
        Assert.Equal("MyNode", server.Name);
    }

    [Fact]
    public void Parse_VlessVisionTls_WorksWithoutReality()
    {
        var link =
            "vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@tls.example:443" +
            "?security=tls&flow=vision&type=tcp&sni=tls.example&fp=firefox&alpn=h2#VisionTLS";

        var server = ShareLinkParser.Parse(link);
        Assert.NotNull(server);
        Assert.Equal("tls", server!.Security);
        Assert.Equal("xtls-rprx-vision", server.Flow);
        Assert.Equal("firefox", server.Fingerprint);
    }

    [Fact]
    public void Parse_VlessWsTls_AndGrpc()
    {
        var ws =
            "vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@ws.example:443" +
            "?type=ws&security=tls&path=%2Fray&host=ws.example&sni=ws.example&fp=chrome#WS";
        var grpc =
            "vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@grpc.example:443" +
            "?type=grpc&security=reality&serviceName=GunService&mode=multi&pbk=pk&sid=ab&sni=grpc.example#gRPC";

        var wsServer = ShareLinkParser.Parse(ws)!;
        Assert.Equal("ws", wsServer.Network);
        Assert.Equal("/ray", wsServer.Path);
        Assert.Equal("ws.example", wsServer.Host);

        var grpcServer = ShareLinkParser.Parse(grpc)!;
        Assert.Equal("grpc", grpcServer.Network);
        Assert.Equal("GunService", grpcServer.ServiceName);
        Assert.Equal("multi", grpcServer.Mode);
        Assert.Equal("reality", grpcServer.Security);
    }

    [Fact]
    public void Parse_VlessHttpupgradeAndXhttp_NormalizeNetwork()
    {
        var httpUpgrade =
            "vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@h.example:443" +
            "?type=httpupgrade&security=tls&path=%2Fup&host=h.example#HU";
        var split =
            "vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@x.example:443" +
            "?type=splithttp&security=tls&path=%2Fx&mode=auto#XH";

        Assert.Equal("httpupgrade", ShareLinkParser.Parse(httpUpgrade)!.Network);
        Assert.Equal("xhttp", ShareLinkParser.Parse(split)!.Network);
    }

    [Fact]
    public void Parse_Trojan_WsTlsAndReality()
    {
        var tls = "trojan://secret-pass@node.example:443?security=tls&sni=node.example&type=ws&path=%2Ft&host=node.example#T1";
        var reality =
            "trojan://secret-pass@r.example:443?security=reality&type=tcp&sni=www.microsoft.com&pbk=pk&sid=cd&fp=chrome#TR";

        var a = ShareLinkParser.Parse(tls)!;
        Assert.Equal(ProxyProtocol.Trojan, a.Protocol);
        Assert.Equal("ws", a.Network);
        Assert.Equal("tls", a.Security);
        Assert.Equal("/t", a.Path);

        var b = ShareLinkParser.Parse(reality)!;
        Assert.Equal("reality", b.Security);
        Assert.Equal("pk", b.PublicKey);
    }

    [Fact]
    public void ParseBulk_IgnoresHttpSubscriptionUrl()
    {
        var result = ShareLinkParser.ParseBulk("https://example.com/sub");
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_UnknownScheme_ReturnsNull()
    {
        Assert.Null(ShareLinkParser.Parse("https://example.com"));
    }

    [Fact]
    public void VisionFlow_ClearedOnNonTcp()
    {
        var link =
            "vless://aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee@ws.example:443" +
            "?type=ws&security=tls&flow=xtls-rprx-vision&path=%2F#bad";
        var server = ShareLinkParser.Parse(link)!;
        Assert.Equal("", server.Flow);
    }
}

public class StreamSettingsBuilderTests
{
    [Fact]
    public void BuildStream_VisionReality_Tcp()
    {
        var server = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "1.2.3.4",
            Port = 443,
            UserId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            Network = "tcp",
            Security = "reality",
            Flow = "xtls-rprx-vision",
            Sni = "www.microsoft.com",
            PublicKey = "pk",
            ShortId = "ab",
            Fingerprint = "chrome"
        };

        var json = XrayConfigBuilder.Build(server, new AppSettings());
        var root = JsonNode.Parse(json)!.AsObject();
        var outbound = root["outbounds"]!.AsArray().First(o => o!["tag"]?.GetValue<string>() == "proxy")!;
        Assert.Equal("xtls-rprx-vision", outbound["settings"]!["vnext"]![0]!["users"]![0]!["flow"]!.GetValue<string>());
        Assert.Equal("reality", outbound["streamSettings"]!["security"]!.GetValue<string>());
        Assert.Equal("pk", outbound["streamSettings"]!["realitySettings"]!["publicKey"]!.GetValue<string>());
    }

    [Fact]
    public void Build_WithTunFd_UsesGvisorStack()
    {
        var server = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "1.2.3.4",
            Port = 443,
            UserId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            Network = "tcp",
            Security = "reality",
            PublicKey = "pk"
        };

        var settings = new AppSettings { EnableTunMode = true };
        var json = XrayConfigBuilder.Build(server, settings, tunFd: 42);
        var root = JsonNode.Parse(json)!.AsObject();
        var tun = root["inbounds"]!.AsArray().First(i => i!["tag"]?.GetValue<string>() == "tun-in")!;
        Assert.Equal("gvisor", tun["settings"]!["stack"]!.GetValue<string>());
        Assert.Equal(42, tun["settings"]!["fd"]!.GetValue<int>());
        Assert.False(tun["settings"]!["auto_route"]!.GetValue<bool>());
    }

    [Fact]
    public void Build_WindowsTunWithoutFd_UsesSystemStack()
    {
        var server = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "1.2.3.4",
            Port = 443,
            UserId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            Network = "tcp",
            Security = "none"
        };

        var settings = new AppSettings { EnableTunMode = true };
        var json = XrayConfigBuilder.Build(server, settings, tunFd: null);
        var root = JsonNode.Parse(json)!.AsObject();
        var tun = root["inbounds"]!.AsArray().First(i => i!["tag"]?.GetValue<string>() == "tun-in")!;
        Assert.Equal("system", tun["settings"]!["stack"]!.GetValue<string>());
        Assert.True(tun["settings"]!["auto_route"]!.GetValue<bool>());
    }

    [Fact]
    public void BuildStream_GrpcMulti_AndWsTlsAlpn()
    {
        var grpc = new ProxyServer
        {
            Protocol = ProxyProtocol.VLESS,
            Address = "1.2.3.4",
            Port = 443,
            UserId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            Network = "grpc",
            Security = "tls",
            ServiceName = "GunService",
            Mode = "multi",
            Sni = "grpc.example",
            Alpn = "h2"
        };
        var stream = XrayConfigBuilder.BuildStreamSettings(grpc);
        Assert.Equal("grpc", stream["network"]!.GetValue<string>());
        Assert.True(stream["grpcSettings"]!["multiMode"]!.GetValue<bool>());
        Assert.Equal("GunService", stream["grpcSettings"]!["serviceName"]!.GetValue<string>());
        Assert.Equal("h2", stream["tlsSettings"]!["alpn"]![0]!.GetValue<string>());

        var ws = new ProxyServer
        {
            Protocol = ProxyProtocol.Trojan,
            Address = "1.2.3.4",
            Port = 443,
            Password = "x",
            Network = "ws",
            Security = "tls",
            Path = "/ray",
            Host = "cdn.example",
            Sni = "cdn.example"
        };
        var wsStream = XrayConfigBuilder.BuildStreamSettings(ws);
        Assert.Equal("/ray", wsStream["wsSettings"]!["path"]!.GetValue<string>());
        Assert.Equal("cdn.example", wsStream["wsSettings"]!["headers"]!["Host"]!.GetValue<string>());
    }

    [Fact]
    public void BuildStream_XhttpAndHttpupgrade()
    {
        var xhttp = new ProxyServer
        {
            Network = "splithttp",
            Security = "tls",
            Path = "/x",
            Mode = "auto",
            Address = "1.1.1.1",
            Sni = "x.example"
        };
        var stream = XrayConfigBuilder.BuildStreamSettings(xhttp);
        Assert.Equal("xhttp", stream["network"]!.GetValue<string>());
        Assert.Equal("auto", stream["xhttpSettings"]!["mode"]!.GetValue<string>());

        var hu = new ProxyServer
        {
            Network = "httpupgrade",
            Security = "tls",
            Path = "/up",
            Host = "h.example",
            Address = "1.1.1.1"
        };
        var huStream = XrayConfigBuilder.BuildStreamSettings(hu);
        Assert.Equal("httpupgrade", huStream["network"]!.GetValue<string>());
        Assert.Equal("/up", huStream["httpupgradeSettings"]!["path"]!.GetValue<string>());
    }
}
