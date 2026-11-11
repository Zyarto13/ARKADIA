using Arkadia.Core.Models;
using Xunit;

namespace Arkadia.App.Tests;

public sealed class AppShortcutTests
{
    [Fact]
    public void FaviconUriUsesTheHttpsWebsiteHost()
    {
        var shortcut = new AppShortcut(
            Guid.NewGuid(),
            "Example",
            "https://games.example.com/library?id=4");

        Assert.Equal(new Uri("https://games.example.com/favicon.ico"), shortcut.FaviconUri);
        Assert.Equal("games.example.com", shortcut.DisplayAddress);
    }

    [Theory]
    [InlineData("steam://open/main")]
    [InlineData("http://example.com")]
    public void FaviconUriIsUnavailableForNonHttpsAddresses(string address)
    {
        var shortcut = new AppShortcut(Guid.NewGuid(), "Shortcut", address);

        Assert.Null(shortcut.FaviconUri);
    }
}
