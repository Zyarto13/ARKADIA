using Arkadia.Core.Models;

namespace Arkadia.Core.Services;

public interface IExternalLauncher
{
    Task<LaunchResult> OpenUriAsync(string uri, CancellationToken cancellationToken);
}
