namespace Arkadia.Core.Services;

public static class LaunchUriPolicy
{
    private static readonly HashSet<string> AllowedSchemes =
        ["steam", "spotify", "discord", "com.epicgames.launcher", "https"];

    public static bool IsAllowed(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri) &&
        AllowedSchemes.Contains(parsedUri.Scheme);

    public static bool IsAllowedUserShortcut(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri) || !AllowedSchemes.Contains(parsedUri.Scheme))
        {
            return false;
        }

        return parsedUri.Scheme != Uri.UriSchemeHttps
               || (!string.IsNullOrWhiteSpace(parsedUri.Host) && string.IsNullOrEmpty(parsedUri.UserInfo));
    }
}
