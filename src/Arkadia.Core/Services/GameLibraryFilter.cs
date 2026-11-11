using Arkadia.Core.Models;

namespace Arkadia.Core.Services;

public static class GameLibraryFilter
{
    public const string AllProviders = "all";

    public static IEnumerable<GameEntry> Filter(
        IEnumerable<GameEntry> games,
        string? searchText,
        string? providerId)
    {
        ArgumentNullException.ThrowIfNull(games);

        var search = searchText?.Trim();
        var provider = string.IsNullOrWhiteSpace(providerId) ? AllProviders : providerId;

        return games.Where(game =>
            (provider.Equals(AllProviders, StringComparison.OrdinalIgnoreCase)
                || game.ProviderId.Equals(provider, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrEmpty(search)
                || game.Title.Contains(search, StringComparison.OrdinalIgnoreCase)));
    }
}
