using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Arkadia.Core.Models;
using Arkadia.Core.Providers;
using Arkadia.Core.Services;

namespace Arkadia.Infrastructure.Providers;

public sealed partial class SteamGameProvider : IGameProvider, ISteamOwnedGameProvider
{
    private const string SteamworksCommonRedistributablesAppId = "228980";
    private const ulong SteamIdBase = 76561197960265728;
    private readonly string? _steamRoot;
    private readonly IExternalLauncher _launcher;
    private readonly HttpClient _httpClient;

    public SteamGameProvider(
        IExternalLauncher launcher,
        string? steamRoot = null,
        string? steamUserDataDirectory = null)
        : this(launcher, new HttpClient(), steamRoot, steamUserDataDirectory)
    {
    }

    public SteamGameProvider(
        IExternalLauncher launcher,
        HttpClient httpClient,
        string? steamRoot = null,
        string? steamUserDataDirectory = null)
    {
        _launcher = launcher;
        _httpClient = httpClient;
        _steamRoot = steamRoot ?? FindSteamRoot();
        _steamUserDataDirectory = steamUserDataDirectory
            ?? (_steamRoot is null ? null : Path.Combine(_steamRoot, "userdata"));
    }

    private readonly string? _steamUserDataDirectory;

    public string Id => "steam";

    public Task<ProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ProviderStatus(
            _steamRoot is not null,
            _steamRoot is null ? null : "Steam",
            _steamRoot is null ? "Steam was not found on this PC." : null));
    }

    public Task<IReadOnlyList<GameEntry>> ScanInstalledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_steamRoot is null)
        {
            return Task.FromResult<IReadOnlyList<GameEntry>>([]);
        }

        var libraries = FindLibraries(_steamRoot);
        var playtimeByAppId = ReadPlaytimeMinutes();
        var games = new List<GameEntry>();
        foreach (var library in libraries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestDirectory = Path.Combine(library, "steamapps");
            if (!Directory.Exists(manifestDirectory))
            {
                continue;
            }

            foreach (var manifestPath in Directory.EnumerateFiles(manifestDirectory, "appmanifest_*.acf"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var manifest = File.ReadAllText(manifestPath);
                var appId = ReadValue(manifest, "appid");
                var title = ReadValue(manifest, "name");
                var installDirectory = ReadValue(manifest, "installdir");
                if (!uint.TryParse(appId, out _)
                    || appId == SteamworksCommonRedistributablesAppId
                    || string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var installPath = string.IsNullOrWhiteSpace(installDirectory)
                    ? null
                    : Path.Combine(manifestDirectory, "common", installDirectory);
                if (installPath is not null && !Directory.Exists(installPath))
                {
                    continue;
                }

                games.Add(new GameEntry(
                    Id,
                    appId,
                    title,
                    installPath,
                    true,
                    true,
                    playtimeByAppId.TryGetValue(appId, out var playtimeMinutes)
                        ? playtimeMinutes
                        : null,
                    FindSteamArtworkFilePath(appId, manifestDirectory, _steamRoot)));
            }
        }

        IReadOnlyList<GameEntry> result = games
            .DistinctBy(game => game.ExternalId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return Task.FromResult(result);
    }

    public async Task<IReadOnlyList<GameEntry>> ScanOwnedAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("A Steam Web API key is required.", nameof(apiKey));
        }

        var steamId = FindMostRecentSteamId()
            ?? throw new InvalidOperationException("No recently used Steam profile could be found.");
        var requestUri = new UriBuilder("https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/")
        {
            Query = $"key={Uri.EscapeDataString(apiKey.Trim())}"
                + $"&steamid={steamId}"
                + "&include_appinfo=true"
                + "&include_played_free_games=true"
                + "&include_free_sub=true"
        }.Uri;

        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("response", out var responseBody)
            || responseBody.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Steam did not return an owned-games response.");
        }

        if (!responseBody.TryGetProperty("games", out var gamesElement))
        {
            throw new InvalidOperationException(
                "Steam did not return the owned-games list. The profile's game details may be private.");
        }

        if (gamesElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Steam returned an invalid owned-games list.");
        }

        return gamesElement.EnumerateArray()
            .Select(game => new
            {
                AppId = GetUnsignedInteger(game, "appid"),
                Title = GetString(game, "name"),
                Playtime = GetUnsignedInteger(game, "playtime_forever")
            })
            .Where(game => game.AppId.HasValue && !string.IsNullOrWhiteSpace(game.Title))
            .Select(game => new GameEntry(
                Id,
                game.AppId!.Value.ToString(CultureInfo.InvariantCulture),
                game.Title!,
                null,
                false,
                true,
                game.Playtime is > 0
                    ? (int)Math.Min(game.Playtime.Value, (uint)int.MaxValue)
                    : null))
            .OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public Task<LaunchResult> LaunchAsync(GameEntry game, CancellationToken cancellationToken)
    {
        if (game.ProviderId != Id || !uint.TryParse(game.ExternalId, out _))
        {
            return Task.FromResult(new LaunchResult(false, "The Steam game entry is invalid."));
        }

        return _launcher.OpenUriAsync($"steam://rungameid/{game.ExternalId}", cancellationToken);
    }

    private static HashSet<string> FindLibraries(string steamRoot)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(steamRoot)
        };
        var libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFile))
        {
            return libraries;
        }

        var contents = File.ReadAllText(libraryFile);
        foreach (Match match in LibraryPathRegex().Matches(contents))
        {
            var path = UnescapeVdf(match.Groups["path"].Value);
            if (Path.IsPathRooted(path) && Directory.Exists(path))
            {
                libraries.Add(Path.GetFullPath(path));
            }
        }

        return libraries;
    }

    private static string? FindSteamArtworkFilePath(string appId, string librarySteamAppsDirectory, string steamRoot)
    {
        var artworkNames = new[]
        {
            $"{appId}_header.jpg",
            $"{appId}_library_hero.jpg",
            $"{appId}_library_hero_2x.jpg",
            $"{appId}_library_600x900.jpg",
            $"{appId}_library_600x900_2x.jpg"
        };
        var cacheDirectories = new[]
        {
            Path.Combine(steamRoot, "appcache", "librarycache"),
            Path.Combine(librarySteamAppsDirectory, "librarycache")
        };

        return cacheDirectories
            .SelectMany(directory => artworkNames.Select(name => Path.Combine(directory, name)))
            .FirstOrDefault(File.Exists);
    }

    private static string? FindSteamRoot()
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
                || Directory.Exists(Path.Combine(path, "steamapps")))
            {
                return path;
            }
        }

        return null;
    }

    private string? FindMostRecentSteamId()
    {
        if (_steamRoot is null)
        {
            return null;
        }

        var loginUsersPath = Path.Combine(_steamRoot, "config", "loginusers.vdf");
        if (!File.Exists(loginUsersPath))
        {
            return null;
        }

        var contents = File.ReadAllText(loginUsersPath);
        foreach (Match user in UserBlockRegex().Matches(contents))
        {
            if (Regex.IsMatch(user.Groups["fields"].Value, "\"MostRecent\"\\s*\"1\"")
                && ulong.TryParse(user.Groups["steamId"].Value, out var steamId)
                && steamId >= SteamIdBase)
            {
                return steamId.ToString(CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static string? ReadValue(string contents, string key)
    {
        var match = Regex.Match(
            contents,
            $"(?m)^\\s*\"{Regex.Escape(key)}\"\\s*\"(?<value>(?:\\\\.|[^\"])*)\"");
        return match.Success ? UnescapeVdf(match.Groups["value"].Value) : null;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static uint? GetUnsignedInteger(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetUInt32(out var value)
            ? value
            : null;

    private Dictionary<string, int> ReadPlaytimeMinutes()
    {
        var configPath = FindMostRecentUserConfigPath();
        if (configPath is null)
        {
            return new Dictionary<string, int>();
        }

        try
        {
            var contents = File.ReadAllText(configPath);
            return SteamPlaytimeRegex().Matches(contents)
                .Cast<Match>()
                .Select(match => new
                {
                    AppId = match.Groups["appId"].Value,
                    Minutes = int.TryParse(match.Groups["minutes"].Value, out var value) ? value : 0
                })
                .Where(entry => entry.Minutes > 0)
                .ToDictionary(entry => entry.AppId, entry => entry.Minutes, StringComparer.Ordinal);
        }
        catch (IOException exception)
        {
            Trace.TraceWarning("Could not read the local Steam playtime cache: {0}", exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            Trace.TraceWarning("Access to the local Steam playtime cache was denied: {0}", exception.Message);
        }

        return new Dictionary<string, int>();
    }

    private string? FindMostRecentUserConfigPath()
    {
        if (_steamRoot is null || _steamUserDataDirectory is null || !Directory.Exists(_steamUserDataDirectory))
        {
            return null;
        }

        var loginUsersPath = Path.Combine(_steamRoot, "config", "loginusers.vdf");
        if (File.Exists(loginUsersPath))
        {
            try
            {
                var loginUsers = File.ReadAllText(loginUsersPath);
                foreach (Match user in UserBlockRegex().Matches(loginUsers))
                {
                    if (!Regex.IsMatch(user.Groups["fields"].Value, "\"MostRecent\"\\s*\"1\""))
                    {
                        continue;
                    }

                    if (ulong.TryParse(user.Groups["steamId"].Value, out var steamId)
                        && steamId >= SteamIdBase)
                    {
                        var accountId = (steamId - SteamIdBase).ToString(
                            System.Globalization.CultureInfo.InvariantCulture);
                        var configPath = Path.Combine(_steamUserDataDirectory, accountId, "config", "localconfig.vdf");
                        if (File.Exists(configPath))
                        {
                            return configPath;
                        }
                    }
                }
            }
            catch (IOException exception)
            {
                Trace.TraceWarning("Could not read the Steam login profile list: {0}", exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                Trace.TraceWarning("Access to the Steam login profile list was denied: {0}", exception.Message);
            }
        }

        try
        {
            return Directory.EnumerateFiles(_steamUserDataDirectory, "localconfig.vdf", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault();
        }
        catch (IOException exception)
        {
            Trace.TraceWarning("Could not locate a Steam playtime cache: {0}", exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            Trace.TraceWarning("Access to Steam user data was denied: {0}", exception.Message);
        }

        return null;
    }

    private static string UnescapeVdf(string value) =>
        value.Replace("\\\\", "\\", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal);

    [GeneratedRegex(
        "(?m)^\\s*(?:\"path\"|\"\\d+\")\\s*\"(?<path>(?:\\\\.|[^\"])*)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LibraryPathRegex();

    [GeneratedRegex(
        "(?s)\"(?<steamId>7656119\\d{10})\"\\s*\\{(?<fields>[^{}]*)\\}",
        RegexOptions.CultureInvariant)]
    private static partial Regex UserBlockRegex();

    [GeneratedRegex(
        "\"(?<appId>\\d+)\"\\s*\\{[^{}]*?\"Playtime\"\\s*\"(?<minutes>\\d+)\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex SteamPlaytimeRegex();
}
