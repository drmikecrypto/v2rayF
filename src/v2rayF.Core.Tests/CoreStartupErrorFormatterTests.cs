using v2rayF.Services;

namespace v2rayF.Core.Tests;

public class CoreStartupErrorFormatterTests
{
    [Fact]
    public void Format_Empty_ReturnsGenericImmediateExit()
    {
        Assert.Equal(
            "Xray core exited immediately after start.",
            CoreStartupErrorFormatter.Format(""));
    }

    [Fact]
    public void Format_PortConflict_ReturnsFriendlyMessage()
    {
        var msg = CoreStartupErrorFormatter.Format("failed to listen on 127.0.0.1:10808 bind: address already in use");
        Assert.Contains("10808/10809", msg);
        Assert.Contains("already in use", msg);
    }

    [Fact]
    public void Format_MissingWintun_ReturnsFriendlyMessage()
    {
        var msg = CoreStartupErrorFormatter.Format(
            "Failed to start: main: failed to create server > Error loading wintun.dll DLL: Unable to load library");
        Assert.Contains("wintun.dll", msg);
        Assert.Contains("cores", msg);
    }

    [Fact]
    public void Format_AccessDenied_ReturnsAdminHint()
    {
        var msg = CoreStartupErrorFormatter.Format("Failed to start: main: failed to create server > Access is denied.");
        Assert.Contains("Administrator", msg);
    }

    [Fact]
    public void Format_AndroidTunFdLost_ReturnsFriendlyMessage()
    {
        var msg = CoreStartupErrorFormatter.Format("read Android Tun Fd 57 bad file descriptor SetNonblock");
        Assert.Contains("VPN tunnel fd", msg);
    }
}
