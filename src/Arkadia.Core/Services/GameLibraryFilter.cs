using Arkadia.Core.Models;

namespace Arkadia.Core.Services;

public static class GameLibraryFilter
{
    public const string AllProviders = "all";
    public const string AllGames = "all";
    public const string InstalledGames = "installed";
    public const string OwnedGames = "owned";
    public const string NotInstalledGames = "not-installed";

    public static IEnumerable<GameEntry> MergeDuplicates(IEnumerable<GameEntry> games)
    {
        ArgumentNullException.ThrowIfNull(games);

        return games
            .GroupBy(
                game => $"{game.ProviderId}:{game.ExternalId}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var preferred = group.FirstOrDefault(game => game.IsInstalled) ?? group.First();
                return preferred with
                {
                    IsInstalled = group.Any(game => game.IsInstalled),
                    IsOwned = group.Any(game => game.IsOwned),
                    PlaytimeMinutes = group.Max(game => game.PlaytimeMinutes),
                    ArtworkFilePath = group
                        .Select(game => game.ArtworkFilePath)
                        .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
                };
            });
    }

    public static IEnumerable<GameEntry> Filter(
        IEnumerable<GameEntry> games,
        string? searchText,
        string? providerId,
        string? collection = AllGames)
    {
        ArgumentNullException.ThrowIfNull(games);

        var search = searchText?.Trim();
        var provider = string.IsNullOrWhiteSpace(providerId) ? AllProviders : providerId;
        var selectedCollection = string.IsNullOrWhiteSpace(collection) ? AllGames : collection;

        return games.Where(game =>
            (provider.Equals(AllProviders, StringComparison.OrdinalIgnoreCase)
                || game.ProviderId.Equals(provider, StringComparison.OrdinalIgnoreCase))
            && (selectedCollection.Equals(AllGames, StringComparison.OrdinalIgnoreCase)
                || selectedCollection.Equals(InstalledGames, StringComparison.OrdinalIgnoreCase) && game.IsInstalled
                || selectedCollection.Equals(OwnedGames, StringComparison.OrdinalIgnoreCase) && game.IsOwned
                || selectedCollection.Equals(NotInstalledGames, StringComparison.OrdinalIgnoreCase)
                    && game.IsOwned
                    && !game.IsInstalled)
            && (string.IsNullOrEmpty(search)
                || game.Title.Contains(search, StringComparison.OrdinalIgnoreCase)));
    }
}
