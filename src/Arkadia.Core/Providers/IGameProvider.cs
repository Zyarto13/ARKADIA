using Arkadia.Core.Models;

namespace Arkadia.Core.Providers;

public interface IGameProvider
{
    string Id { get; }
    Task<ProviderStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<GameEntry>> ScanInstalledAsync(CancellationToken cancellationToken);
    Task<LaunchResult> LaunchAsync(GameEntry game, CancellationToken cancellationToken);
}
