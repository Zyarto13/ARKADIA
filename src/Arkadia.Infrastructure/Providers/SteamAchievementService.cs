using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Arkadia.Core.Models;
using Arkadia.Core.Services;

namespace Arkadia.Infrastructure.Providers;

public sealed class SteamAchievementService : IGameAchievementService
{
    private readonly HttpClient _httpClient;
    private readonly string? _steamRoot;

    public SteamAchievementService(HttpClient httpClient, string? steamRoot = null)
    {
        _httpClient = httpClient;
        _steamRoot = steamRoot ?? SteamProfileLocator.FindSteamRoot();
    }

    public async Task<IReadOnlyList<GameAchievement>> GetAchievementsAsync(
        GameEntry game,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (game.ProviderId != "steam" || !uint.TryParse(game.ExternalId, out _))
        {
            throw new ArgumentException("Achievements are only available for valid Steam game entries.", nameof(game));
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("A Steam Web API key is required.", nameof(apiKey));
        }

        var steamId = SteamProfileLocator.FindMostRecentSteamId(_steamRoot)
            ?? throw new InvalidOperationException("No recently used Steam profile could be found.");
        var language = Uri.EscapeDataString(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr"
            ? "french"
            : "english");
        var schemaUri = new UriBuilder("https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/")
        {
            Query = $"key={Uri.EscapeDataString(apiKey)}&appid={game.ExternalId}&l={language}"
        }.Uri;

        using var schemaDocument = await GetJsonAsync(schemaUri, cancellationToken);
        if (!schemaDocument.RootElement.TryGetProperty("game", out var schemaGame)
            || schemaGame.ValueKind != JsonValueKind.Object
            || !schemaGame.TryGetProperty("availableGameStats", out var availableStats)
            || availableStats.ValueKind != JsonValueKind.Object
            || !availableStats.TryGetProperty("achievements", out var schemaAchievements))
        {
            throw new InvalidOperationException(
                "Steam did not expose an achievement schema for this game.");
        }

        if (schemaAchievements.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Steam returned an invalid achievement schema.");
        }

        var playerUri = new UriBuilder("https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/")
        {
            Query = $"key={Uri.EscapeDataString(apiKey)}&steamid={steamId}&appid={game.ExternalId}&l={language}"
        }.Uri;
        using var playerDocument = await GetJsonAsync(playerUri, cancellationToken);
        if (!playerDocument.RootElement.TryGetProperty("playerstats", out var playerStats)
            || playerStats.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Steam did not return player achievement data.");
        }

        if (playerStats.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.False)
        {
            var message = GetString(playerStats, "error");
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(message)
                    ? "Steam did not return achievement data for this game."
                    : SanitizeApiError(message));
        }

        var playerAchievements = playerStats.TryGetProperty("achievements", out var playerEntries)
                                && playerEntries.ValueKind == JsonValueKind.Array
            ? playerEntries.EnumerateArray()
                .Where(entry => !string.IsNullOrWhiteSpace(GetString(entry, "apiname")))
                .ToDictionary(
                    entry => GetString(entry, "apiname")!,
                    StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        var results = new List<GameAchievement>();
        foreach (var schemaAchievement in schemaAchievements.EnumerateArray())
        {
            var apiName = GetString(schemaAchievement, "name");
            if (string.IsNullOrWhiteSpace(apiName))
            {
                continue;
            }

            var isUnlocked = playerAchievements.TryGetValue(apiName, out var playerAchievement)
                             && GetBoolean(playerAchievement, "achieved");
            var icon = GetString(schemaAchievement, isUnlocked ? "icon" : "icongray");
            DateTimeOffset? unlockedAt = isUnlocked
                                         && playerAchievement.TryGetProperty("unlocktime", out var unlockTime)
                                         && unlockTime.TryGetInt64(out var unixTime)
                                         && unixTime is > 0 and <= 253402300799
                ? DateTimeOffset.FromUnixTimeSeconds(unixTime)
                : null;
            results.Add(new GameAchievement(
                GetString(schemaAchievement, "displayName") ?? apiName,
                GetString(schemaAchievement, "description") ?? string.Empty,
                string.IsNullOrWhiteSpace(icon)
                    ? null
                    : $"https://steamcdn-a.akamaihd.net/steamcommunity/public/images/apps/{game.ExternalId}/{Uri.EscapeDataString(icon)}",
                isUnlocked,
                unlockedAt));
        }

        return results
            .OrderByDescending(achievement => achievement.IsUnlocked)
            .ThenBy(achievement => achievement.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private async Task<JsonDocument> GetJsonAsync(Uri requestUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool GetBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetInt32(out var value)
        && value != 0;

    private static string SanitizeApiError(string message)
    {
        var sanitized = string.Concat(message.Where(character => !char.IsControl(character))).Trim();
        return sanitized.Length <= 240 ? sanitized : sanitized[..240];
    }
}
