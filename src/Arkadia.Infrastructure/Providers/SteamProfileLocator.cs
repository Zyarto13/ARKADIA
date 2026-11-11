using System.Globalization;
using System.Text.RegularExpressions;

namespace Arkadia.Infrastructure.Providers;

internal static partial class SteamProfileLocator
{
    private const ulong SteamIdBase = 76561197960265728;

    public static string? FindSteamRoot()
    {
        var candidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        foreach (var candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var path = candidate == Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                ? Path.Combine(candidate, "Programs", "Steam")
                : Path.Combine(candidate, "Steam");
            if (File.Exists(Path.Combine(path, "steam.exe"))
                || Directory.Exists(Path.Combine(path, "config")))
            {
                return path;
            }
        }

        return null;
    }

    public static string? FindMostRecentSteamId(string? steamRoot)
    {
        if (steamRoot is null)
        {
            return null;
        }

        var loginUsersPath = Path.Combine(steamRoot, "config", "loginusers.vdf");
        if (!File.Exists(loginUsersPath))
        {
            return null;
        }

        var contents = File.ReadAllText(loginUsersPath);
        var profiles = UserBlockRegex().Matches(contents)
            .Cast<Match>()
            .Select(match => new
            {
                SteamId = match.Groups["steamId"].Value,
                MostRecent = HasFieldValue(match.Groups["fields"].Value, "MostRecent", "1"),
                AutoLogin = HasFieldValue(match.Groups["fields"].Value, "AutoLogin", "1"),
                Timestamp = GetTimestamp(match.Groups["fields"].Value)
            })
            .Where(profile =>
                ulong.TryParse(profile.SteamId, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                && id >= SteamIdBase)
            .ToArray();

        var mostRecent = profiles.FirstOrDefault(profile => profile.MostRecent);
        if (mostRecent is not null)
        {
            return mostRecent.SteamId;
        }

        var timestampedProfiles = profiles
            .Where(profile => profile.Timestamp.HasValue)
            .OrderByDescending(profile => profile.Timestamp)
            .ThenByDescending(profile => profile.AutoLogin)
            .ToArray();
        if (timestampedProfiles.Length > 0)
        {
            return timestampedProfiles[0].SteamId;
        }

        var autoLoginProfiles = profiles.Where(profile => profile.AutoLogin).ToArray();
        if (autoLoginProfiles.Length == 1)
        {
            return autoLoginProfiles[0].SteamId;
        }

        return profiles.Length == 1 ? profiles[0].SteamId : null;
    }

    private static bool HasFieldValue(string fields, string field, string expectedValue) =>
        Regex.IsMatch(
            fields,
            $"\"{Regex.Escape(field)}\"\\s*\"{Regex.Escape(expectedValue)}\"",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static long? GetTimestamp(string fields)
    {
        var match = TimestampRegex().Match(fields);
        return match.Success
            && long.TryParse(match.Groups["timestamp"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp)
            ? timestamp
            : null;
    }

    [GeneratedRegex(
        "(?s)\"(?<steamId>7656119\\d{10})\"\\s*\\{(?<fields>[^{}]*)\\}",
        RegexOptions.CultureInvariant)]
    private static partial Regex UserBlockRegex();

    [GeneratedRegex(
        "\"timestamp\"\\s*\"(?<timestamp>\\d+)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimestampRegex();
}
