using Arkadia.Core.Services;
using Xunit;

namespace Arkadia.Core.Tests;

public sealed class LaunchUriPolicyTests
{
    [Theory]
    [InlineData("steam://open/main")]
    [InlineData("spotify:")]
    [InlineData("https://www.deezer.com/")]
    [InlineData("discord://")]
    [InlineData("com.epicgames.launcher://")]
    [InlineData("https://music.youtube.com/")]
    public void IsAllowedAcceptsApprovedLauncherUris(string uri)
    {
        Assert.True(LaunchUriPolicy.IsAllowed(uri));
    }

    [Theory]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("powershell://run")]
    [InlineData("not a uri")]
    [InlineData("")]
    public void IsAllowedRejectsUnapprovedOrMalformedUris(string uri)
    {
        Assert.False(LaunchUriPolicy.IsAllowed(uri));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("steam://open/main")]
    [InlineData("spotify:")]
    [InlineData("discord://")]
    [InlineData("com.epicgames.launcher://")]
    public void IsAllowedUserShortcutAcceptsHttpsAndSupportedAppLinks(string uri)
    {
        Assert.True(LaunchUriPolicy.IsAllowedUserShortcut(uri));
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    [InlineData("https://user@example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a uri")]
    public void IsAllowedUserShortcutRejectsUnsafeOrUnsupportedUris(string uri)
    {
        Assert.False(LaunchUriPolicy.IsAllowedUserShortcut(uri));
    }
}
