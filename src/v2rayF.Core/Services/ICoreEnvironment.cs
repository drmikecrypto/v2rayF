using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

public interface ICoreEnvironment
{
    Task EnsureCoreAsync(CancellationToken cancellationToken = default);

    string GetCorePath();

    /// <summary>Path to sing-box binary (may be missing until dual-core package is installed).</summary>
    string GetSingBoxPath();

    string GetCoresDirectory();

    string GetDataDirectory();

    ICoreProcessHost CreateProcessHost();
}
