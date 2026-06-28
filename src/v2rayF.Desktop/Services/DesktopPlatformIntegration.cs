using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using v2rayF.Services;

namespace v2rayF.Desktop.Services;

public sealed class DesktopPlatformIntegration : IPlatformIntegration
{
    private bool _enabled;
    private readonly List<Func<Task>> _disableActions = [];

    public bool IsMobile => false;

    public bool CanUseTunMode => IsWindowsAdministrator();

    public string TunRequirementMessage =>
        "TUN mode requires running v2rayF as Administrator.";

    public string? LastProxyMethod { get; private set; }

    public Task<int?> EstablishVpnAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<int?>(null);

    public async Task EnableProxyAsync(CancellationToken cancellationToken = default)
    {
        if (_enabled)
            return;

        _disableActions.Clear();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            EnableWindows();
            LastProxyMethod = "Windows registry";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var service = await GetMacNetworkServiceAsync().ConfigureAwait(false);
            await RunAsync("networksetup", $"-setwebproxy \"{service}\" 127.0.0.1 {XrayConfigBuilder.HttpPort}").ConfigureAwait(false);
            await RunAsync("networksetup", $"-setsecurewebproxy \"{service}\" 127.0.0.1 {XrayConfigBuilder.HttpPort}").ConfigureAwait(false);
            await RunAsync("networksetup", $"-setwebproxystate \"{service}\" on").ConfigureAwait(false);
            await RunAsync("networksetup", $"-setsecurewebproxystate \"{service}\" on").ConfigureAwait(false);
            _disableActions.Add(async () => await DisableMacOsAsync(service).ConfigureAwait(false));
            LastProxyMethod = "macOS networksetup";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            LastProxyMethod = await EnableLinuxAsync().ConfigureAwait(false);
        }

        _enabled = true;
    }

    public async Task DisableProxyAsync(CancellationToken cancellationToken = default)
    {
        if (!_enabled)
            return;

        foreach (var action in _disableActions)
        {
            try { await action().ConfigureAwait(false); }
            catch { /* best effort */ }
        }

        _disableActions.Clear();
        _enabled = false;
        LastProxyMethod = null;
    }

    [SupportedOSPlatform("windows")]
    private static bool IsWindowsAdministrator()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private void EnableWindows()
    {
        const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath, writable: true)
            ?? throw new InvalidOperationException("Unable to open Internet Settings registry key.");

        key.SetValue("ProxyEnable", 1, Microsoft.Win32.RegistryValueKind.DWord);
        key.SetValue("ProxyServer", $"127.0.0.1:{XrayConfigBuilder.HttpPort}");
        key.SetValue("ProxyOverride", "<local>", Microsoft.Win32.RegistryValueKind.String);

        InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);

        _disableActions.Add(() =>
        {
            DisableWindows();
            return Task.CompletedTask;
        });
    }

    [SupportedOSPlatform("windows")]
    private static void DisableWindows()
    {
        const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.SetValue("ProxyEnable", 0, Microsoft.Win32.RegistryValueKind.DWord);
        InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
    }

    private static async Task DisableMacOsAsync(string service)
    {
        await RunAsync("networksetup", $"-setwebproxystate \"{service}\" off").ConfigureAwait(false);
        await RunAsync("networksetup", $"-setsecurewebproxystate \"{service}\" off").ConfigureAwait(false);
    }

    private async Task<string> EnableLinuxAsync()
    {
        if (await TryEnableGnomeAsync().ConfigureAwait(false))
            return "GNOME gsettings";
        if (await TryEnableKdeAsync().ConfigureAwait(false))
            return "KDE kwriteconfig5";
        if (await TryEnableXfceAsync().ConfigureAwait(false))
            return "XFCE xfconf-query";

        throw new InvalidOperationException(
            "Could not set system proxy. Use manual proxy 127.0.0.1:10809 or enable TUN mode.");
    }

    private async Task<bool> TryEnableGnomeAsync()
    {
        if (!await CommandExistsAsync("gsettings").ConfigureAwait(false))
            return false;

        try
        {
            var savedMode = await RunAsync("gsettings", "get org.gnome.system.proxy mode").ConfigureAwait(false);
            await RunAsync("gsettings", "set org.gnome.system.proxy mode 'manual'").ConfigureAwait(false);
            await RunAsync("gsettings", "set org.gnome.system.proxy.http host '127.0.0.1'").ConfigureAwait(false);
            await RunAsync("gsettings", $"set org.gnome.system.proxy.http port '{XrayConfigBuilder.HttpPort}'").ConfigureAwait(false);
            await RunAsync("gsettings", "set org.gnome.system.proxy.https host '127.0.0.1'").ConfigureAwait(false);
            await RunAsync("gsettings", $"set org.gnome.system.proxy.https port '{XrayConfigBuilder.HttpPort}'").ConfigureAwait(false);

            _disableActions.Add(async () =>
            {
                var mode = string.IsNullOrWhiteSpace(savedMode) ? "'none'" : savedMode.Trim();
                await RunAsync("gsettings", $"set org.gnome.system.proxy mode {mode}").ConfigureAwait(false);
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryEnableKdeAsync()
    {
        if (!await CommandExistsAsync("kwriteconfig5").ConfigureAwait(false))
            return false;

        try
        {
            var proxy = $"http://127.0.0.1:{XrayConfigBuilder.HttpPort}";
            await RunAsync("kwriteconfig5", "--file kioslaverc --group \"Proxy Settings\" --key ProxyType 1").ConfigureAwait(false);
            await RunAsync("kwriteconfig5", $"--file kioslaverc --group \"Proxy Settings\" --key httpProxy \"{proxy}\"").ConfigureAwait(false);
            await RunAsync("kwriteconfig5", $"--file kioslaverc --group \"Proxy Settings\" --key httpsProxy \"{proxy}\"").ConfigureAwait(false);

            _disableActions.Add(async () =>
            {
                await RunAsync("kwriteconfig5", "--file kioslaverc --group \"Proxy Settings\" --key ProxyType 0").ConfigureAwait(false);
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryEnableXfceAsync()
    {
        if (!await CommandExistsAsync("xfconf-query").ConfigureAwait(false))
            return false;

        try
        {
            await RunAsync("xfconf-query", "-c xfce4-session -p /general/UseProxy -s true").ConfigureAwait(false);
            await RunAsync("xfconf-query", "-c xfce4-session -p /general/ProxyType -s 1").ConfigureAwait(false);
            await RunAsync("xfconf-query", "-c xfce4-session -p /general/ProxyHost -s 127.0.0.1").ConfigureAwait(false);
            await RunAsync("xfconf-query", "-c xfce4-session -p /general/ProxyPort -s " + XrayConfigBuilder.HttpPort).ConfigureAwait(false);

            _disableActions.Add(async () =>
            {
                await RunAsync("xfconf-query", "-c xfce4-session -p /general/UseProxy -s false").ConfigureAwait(false);
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> GetMacNetworkServiceAsync()
    {
        var output = await RunAsync("networksetup", "-listallnetworkservices").ConfigureAwait(false);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith('*') || line.Contains("An asterisk", StringComparison.Ordinal))
                continue;
            return line;
        }

        return "Wi-Fi";
    }

    private static async Task<bool> CommandExistsAsync(string command)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which",
                    Arguments = command,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> RunAsync(string fileName, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        return output.Trim();
    }

    [DllImport("wininet.dll", SetLastError = true, EntryPoint = "InternetSetOption")]
    private static extern bool InternetSetOptionWindows(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private static void InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            InternetSetOptionWindows(hInternet, dwOption, lpBuffer, dwBufferLength);
    }
}
