using Arkadia.Core.Models;
using Arkadia.Core.Services;
using Xunit;

namespace Arkadia.Core.Tests;

public sealed class GameLibraryFilterTests
{
    private static readonly GameEntry[] Games =
    [
        new("steam", "1", "Space Quest", null, true, true),
        new("epic", "2", "Space Tactics", null, true, true),
        new("steam", "3", "Forest Run", null, true, true),
        new("steam", "4", "Uninstalled Game", null, false, true)
    ];

    [Fact]
    public void FilterReturnsAllGamesWhenNoFiltersAreSelected()
    {
        var result = GameLibraryFilter.Filter(Games, null, GameLibraryFilter.AllProviders);

        Assert.Equal(Games, result);
    }

    [Fact]
    public void FilterMatchesTitlesWithoutCaseSensitivityAndTrimsTheQuery()
    {
        var result = GameLibraryFilter.Filter(Games, "  SPACE ", GameLibraryFilter.AllProviders);

        Assert.Equal(["Space Quest", "Space Tactics"], result.Select(game => game.Title));
    }

    [Fact]
    public void FilterCombinesTitleAndProviderFilters()
    {
        var result = GameLibraryFilter.Filter(Games, "space", "STEAM");

        Assert.Equal(["Space Quest"], result.Select(game => game.Title));
    }

    [Fact]
    public void FilterReturnsNoGamesWhenNoTitleMatches()
    {
        var result = GameLibraryFilter.Filter(Games, "missing", GameLibraryFilter.AllProviders);

        Assert.Empty(result);
    }

    [Fact]
    public void FilterTreatsAnEmptyProviderAsAllProviders()
    {
        var result = GameLibraryFilter.Filter(Games, null, null);

        Assert.Equal(Games, result);
    }

    [Fact]
    public void FilterCanSelectInstalledAndOwnedCollections()
    {
        var installed = GameLibraryFilter.Filter(
            Games,
            null,
            GameLibraryFilter.AllProviders,
            GameLibraryFilter.InstalledGames);
        var owned = GameLibraryFilter.Filter(
            Games,
            null,
            GameLibraryFilter.AllProviders,
            GameLibraryFilter.OwnedGames);

        Assert.Equal(["Space Quest", "Space Tactics", "Forest Run"], installed.Select(game => game.Title));
        Assert.Equal(
            ["Space Quest", "Space Tactics", "Forest Run", "Uninstalled Game"],
            owned.Select(game => game.Title));
    }

    [Fact]
    public void SteamGamesExposePrimaryAndFallbackArtworkUris()
    {
        var game = new GameEntry("steam", "1234", "Space Quest", null, true, true);

        Assert.Equal(
            [
                new Uri("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1234/header.jpg"),
                new Uri("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1234/capsule_616x353.jpg"),
                new Uri("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1234/library_600x900.jpg"),
                new Uri("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1234/library_hero.jpg"),
                new Uri("https://cdn.akamai.steamstatic.com/steam/apps/1234/header.jpg"),
                new Uri("https://cdn.akamai.steamstatic.com/steam/apps/1234/library_600x900.jpg")
            ],
            game.ArtworkUris);
    }

    [Fact]
    public void GamesWithoutSteamArtworkExposeNoArtworkUris()
    {
        var game = new GameEntry("epic", "1234", "Space Quest", null, true, true);

        Assert.Empty(game.ArtworkUris);
    }
}
