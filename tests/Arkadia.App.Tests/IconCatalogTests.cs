using Arkadia.App.Icons;
using Xunit;

namespace Arkadia.App.Tests;

public sealed class IconCatalogTests
{
    [Theory]
    [InlineData("Home")]
    [InlineData("Games")]
    [InlineData("Achievements")]
    [InlineData("Sound")]
    [InlineData("Shortcuts")]
    [InlineData("Settings")]
    public void NavigationIconsHaveNonEmptyGeometry(string key)
    {
        var geometry = IconCatalog.ForNavigation(key);

        Assert.True(geometry.Bounds.Width > 0);
        Assert.True(geometry.Bounds.Height > 0);
    }

    [Theory]
    [InlineData("spotify")]
    [InlineData("discord")]
    [InlineData("steam")]
    [InlineData("epic")]
    [InlineData("curseforge")]
    [InlineData("minecraft")]
    [InlineData("last-game")]
    [InlineData("achievement")]
    [InlineData("system")]
    [InlineData("shortcuts")]
    public void DashboardIconsHaveNonEmptyGeometry(string key)
    {
        var geometry = IconCatalog.ForBrand(key);

        Assert.True(geometry.Bounds.Width > 0);
        Assert.True(geometry.Bounds.Height > 0);
    }
}
