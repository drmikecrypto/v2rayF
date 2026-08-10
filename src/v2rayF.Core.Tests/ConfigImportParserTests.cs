using System.Text;
using System.Text.Json.Nodes;
using v2rayF.Models;
using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class ConfigImportParserTests
{
    [Fact]
    public void Parse_BulkShareLinks()
    {
        var text = """
            vless://11111111-1111-1111-1111-111111111111@1.2.3.4:443?encryption=none&security=reality&pbk=pk&sid=ab&type=tcp#A
            vmess://eyJ2IjoiMiIsInBzIjoiQiIsImFkZCI6IjUuNi43LjgiLCJwb3J0IjoiODAiLCJpZCI6IjIyMjIyMjIyLTIyMjItMjIyMi0yMjIyLTIyMjIyMjIyMjIyMiIsImFpZCI6IjAiLCJzY3kiOiJhdXRvIiwibmV0Ijoid3MiLCJ0bHMiOiIiLCJwYXRoIjoiL3dzIiwiaG9zdCI6IiJ9
            """;

        var servers = ConfigImportParser.Parse(text);
        Assert.True(servers.Count >= 2);
    }

    [Fact]
    public void Parse_XrayJsonOutbound()
    {
        var json = new JsonObject
        {
            ["outbounds"] = new JsonArray
            {
                new JsonObject
                {
                    ["tag"] = "proxy",
                    ["protocol"] = "vless",
                    ["settings"] = new JsonObject
                    {
                        ["vnext"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["address"] = "9.9.9.9",
                                ["port"] = 8443,
                                ["users"] = new JsonArray
                                {
                                    new JsonObject
                                    {
                                        ["id"] = "33333333-3333-3333-3333-333333333333",
                                        ["encryption"] = "none",
                                        ["flow"] = "xtls-rprx-vision"
                                    }
                                }
                            }
                        }
                    },
                    ["streamSettings"] = new JsonObject
                    {
                        ["network"] = "tcp",
                        ["security"] = "reality",
                        ["realitySettings"] = new JsonObject
                        {
                            ["serverName"] = "www.example.com",
                            ["publicKey"] = "pk",
                            ["shortId"] = "abcd",
                            ["fingerprint"] = "chrome"
                        }
                    }
                },
                new JsonObject { ["tag"] = "direct", ["protocol"] = "freedom" }
            }
        }.ToJsonString();

        var servers = ConfigImportParser.Parse(json);
        Assert.Single(servers);
        Assert.Equal(ProxyProtocol.VLESS, servers[0].Protocol);
        Assert.Equal("9.9.9.9", servers[0].Address);
        Assert.Equal(8443, servers[0].Port);
        Assert.Equal("reality", servers[0].Security);
        Assert.Equal("pk", servers[0].PublicKey);
    }

    [Fact]
    public void ParseBytes_ScansEmbeddedLinks()
    {
        var dump = Encoding.UTF8.GetBytes(
            """{"app":"v2box","uri":"trojan://secret@10.0.0.1:443?security=tls&sni=a.com#T"}""");

        var servers = ConfigImportParser.ParseBytes(dump, "export.v2box");
        Assert.Single(servers);
        Assert.Equal(ProxyProtocol.Trojan, servers[0].Protocol);
        Assert.Equal("10.0.0.1", servers[0].Address);
    }

    [Fact]
    public void Parse_IgnoresBareSubscriptionUrl()
    {
        var servers = ConfigImportParser.Parse("https://example.com/sub");
        Assert.Empty(servers);
    }
}
