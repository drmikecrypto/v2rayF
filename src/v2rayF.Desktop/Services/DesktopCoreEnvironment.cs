using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using v2rayF.Services;

namespace v2rayF.Desktop.Services;

public sealed class DesktopCoreEnvironment : ICoreEnvironment
{
    public Task EnsureCoreAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public string GetCorePath()
    {
        var coresDir = GetCoresDirectory();
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(coresDir, "xray.exe")
            : Path.Combine(coresDir, "xray");
    }

    public string GetCoresDirectory() => Path.Combine(AppContext.BaseDirectory, "cores");

    public string GetDataDirectory()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "v2rayF");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
