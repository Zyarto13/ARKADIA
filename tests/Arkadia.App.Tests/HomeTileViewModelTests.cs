using Arkadia.App.ViewModels;
using Xunit;

namespace Arkadia.App.Tests;

public sealed class HomeTileViewModelTests
{
    [Fact]
    public void SetContentCanPopulateAndClearTilePreviews()
    {
        var tile = new HomeTileViewModel("shortcuts", "▦", "Tile.Shortcuts", "Tile.Shortcuts.Description");

        Assert.False(tile.HasTileContent);

        tile.SetContent("2 shortcuts", previewItems: ["Browser", "Editor"]);

        Assert.True(tile.HasTileContent);
        Assert.Equal(["Browser", "Editor"], tile.PreviewItems);

        tile.SetContent(string.Empty);

        Assert.False(tile.HasTileContent);
        Assert.Empty(tile.PreviewItems);
        Assert.Empty(tile.PreviewGames);
    }
}
