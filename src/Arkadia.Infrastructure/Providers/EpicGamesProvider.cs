using System.Text.Json;
using Arkadia.Core.Models;
using Arkadia.Core.Providers;
using Arkadia.Core.Services;

namespace Arkadia.Infrastructure.Providers;

public sealed class EpicGamesProvider : IGameProvider
{
    private readonly string _manifestDirectory;
    private readonly IExternalLauncher _launcher;

    public EpicGamesProvider(IExternalLauncher launcher, string? manifestDirectory = null)
    {
        _launcher = launcher;
        _manifestDirectory = manifestDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic",
            "EpicGamesLauncher",
            "Data",
            "Manifests");
    }

    public string Id => "epic";

    public Task<ProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ProviderStatus(
            Directory.Exists(_manifestDirectory),
            Directory.Exists(_manifestDirectory) ? "Epic Games" : null,
            Directory.Exists(_manifestDirectory) ? null : "Epic Games was not found on this PC."));
    }

    public Task<IReadOnlyList<GameEntry>> ScanInstalledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(_manifestDirectory))
        {
            return Task.FromResult<IReadOnlyList<GameEntry>>([]);
        }

        var games = new List<GameEntry>();
        foreach (var manifestPath in Directory.EnumerateFiles(_manifestDirectory, "*.item"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            var appName = ReadString(root, "AppName");
            var title = ReadString(root, "DisplayName");
            var installLocation = ReadString(root, "InstallLocation");
            if (string.IsNullOrWhiteSpace(appName)
                || string.IsNullOrWhiteSpace(title)
                || string.IsNullOrWhiteSpace(installLocation)
                || !Directory.Exists(installLocation)
                || IsIncomplete(root))
            {
                continue;
            }

            games.Add(new GameEntry(Id, appName, title, installLocation, true, true));
        }

        IReadOnlyList<GameEntry> result = games
            .DistinctBy(game => game.ExternalId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<LaunchResult> LaunchAsync(GameEntry game, CancellationToken cancellationToken)
    {
        if (game.ProviderId != Id
            || string.IsNullOrWhiteSpace(game.ExternalId)
            || game.ExternalId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
        {
            return Task.FromResult(new LaunchResult(false, "The Epic Games entry is invalid."));
        }

        var appName = Uri.EscapeDataString(game.ExternalId);
        return _launcher.OpenUriAsync(
            $"com.epicgames.launcher://apps/{appName}?action=launch&silent=true",
            cancellationToken);
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool IsIncomplete(JsonElement element) =>
        element.TryGetProperty("bIsIncomplete", out var property)
        && property.ValueKind == JsonValueKind.True;
}
