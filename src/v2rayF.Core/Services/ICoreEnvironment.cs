using System.Threading;
using System.Threading.Tasks;

namespace v2rayF.Services;

public interface ICoreEnvironment
{
    Task EnsureCoreAsync(CancellationToken cancellationToken = default);

    string GetCorePath();

    string GetCoresDirectory();

    string GetDataDirectory();
}
