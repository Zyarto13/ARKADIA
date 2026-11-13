using System.Text.Json;
using Arkadia.Core.Models;
using Arkadia.Core.Providers;

namespace Arkadia.Infrastructure.Providers;

public sealed class CurseForgeGameProvider : IGameProvider
{
    private readonly string _instancesDirectory;
    private readonly Func<IEnumerable<string>> _launcherPathProvider;
    private readonly Func<string, LaunchResult> _startExecutable;

    public CurseForgeGameProvider(
        string? instancesDirectory = null,
        IEnumerable<string>? launcherPaths = null,
        Func<string, LaunchResult>? startExecutable = null)
    {
        _instancesDirectory = instancesDirectory ?? GetDefaultInstancesDirectory();
        _launcherPathProvider = launcherPaths is null
            ? GetDefaultLauncherPaths
            : () => launcherPaths;
        _startExecutable = startExecutable ?? GameExecutableLauncher.Start;
    }

    public string Id => "curseforge";

    public Task<ProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var launcherPath = FindLauncherPath();
        var available = Directory.Exists(_instancesDirectory) || launcherPath is not null;
        return Task.FromResult(new ProviderStatus(
            available,
            available ? "CurseForge" : null,
            available ? null : "CurseForge was not found on this PC."));
    }

    public Task<IReadOnlyList<GameEntry>> ScanInstalledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(_instancesDirectory))
        {
            return Task.FromResult<IReadOnlyList<GameEntry>>([]);
        }

        var instancesRoot = Path.GetFullPath(_instancesDirectory);
        var games = new List<GameEntry>();
        foreach (var instanceDirectory in Directory.EnumerateDirectories(instancesRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestPath = Path.Combine(instanceDirectory, "minecraftinstance.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var title = ReadString(document.RootElement, "name");
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var normalizedInstancePath = Path.GetFullPath(instanceDirectory);
            games.Add(new GameEntry(
                Id,
                Path.GetFileName(normalizedInstancePath),
                title,
                normalizedInstancePath,
                true,
                true,
                ArtworkFilePath: FindArtworkFilePath(normalizedInstancePath)));
        }

        IReadOnlyList<GameEntry> result = games
            .DistinctBy(game => game.ExternalId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(game => game.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<LaunchResult> LaunchAsync(GameEntry game, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsValidInstance(game))
        {
            return Task.FromResult(new LaunchResult(false, "The CurseForge instance entry is invalid."));
        }

        var launcherPath = FindLauncherPath();
        return Task.FromResult(launcherPath is null
            ? new LaunchResult(false, "The CurseForge launcher could not be found.")
            : _startExecutable(launcherPath));
    }

    private bool IsValidInstance(GameEntry game)
    {
        if (game.ProviderId != Id || string.IsNullOrWhiteSpace(game.InstallPath))
        {
            return false;
        }

        try
        {
            var root = Path.GetFullPath(_instancesDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var instancePath = Path.GetFullPath(game.InstallPath);
            return instancePath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(instancePath)
                && File.Exists(Path.Combine(instancePath, "minecraftinstance.json"));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private string? FindLauncherPath() =>
        _launcherPathProvider()
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? FindArtworkFilePath(string instanceDirectory)
    {
        foreach (var fileName in new[] { "icon.png", "icon.jpg", "logo.png", "logo.jpg" })
        {
            var path = Path.Combine(instanceDirectory, fileName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string GetDefaultInstancesDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var candidates = new[]
        {
            Path.Combine(userProfile, "curseforge", "minecraft", "Instances"),
            Path.Combine(roamingAppData, "CurseForge", "Minecraft", "Instances"),
            Path.Combine(localAppData, "CurseForge", "Minecraft", "Instances"),
            Path.Combine(documents, "CurseForge", "Minecraft", "Instances")
        };
        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    private static IEnumerable<string> GetDefaultLauncherPaths()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        foreach (var path in new[]
        {
            Path.Combine(programFiles, "Overwolf", "CurseForge", "CurseForge.exe"),
            Path.Combine(programFilesX86, "Overwolf", "CurseForge", "CurseForge.exe"),
            Path.Combine(localAppData, "Programs", "CurseForge", "CurseForge.exe"),
            Path.Combine(localAppData, "CurseForge", "CurseForge.exe")
        })
        {
            yield return path;
        }

        foreach (var path in InstalledApplicationLocator.FindExecutablePaths(
                     "CurseForge",
                     "CurseForge.exe",
                     "CurseForge Windows.exe"))
        {
            yield return path;
        }
    }
}
