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
        new("steam", "3", "Forest Run", null, true, true)
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
}
