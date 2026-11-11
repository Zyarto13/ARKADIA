using Arkadia.Core.Models;

namespace Arkadia.Core.Providers;

public interface ISteamOwnedGameProvider
{
    Task<IReadOnlyList<GameEntry>> ScanOwnedAsync(string apiKey, CancellationToken cancellationToken);
}
