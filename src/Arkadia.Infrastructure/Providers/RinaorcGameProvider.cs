using Arkadia.Core.Models;
using Arkadia.Core.Providers;

namespace Arkadia.Infrastructure.Providers;

public sealed class RinaorcGameProvider : IGameProvider
{
    private readonly Func<IEnumerable<string>> _launcherPathProvider;
    private readonly Func<string, LaunchResult> _startExecutable;

    public RinaorcGameProvider(
        IEnumerable<string>? launcherPaths = null,
        Func<string, LaunchResult>? startExecutable = null)
    {
        _launcherPathProvider = launcherPaths is null
            ? GetDefaultLauncherPaths
            : () => launcherPaths;
        _startExecutable = startExecutable ?? GameExecutableLauncher.Start;
    }

    public string Id => "rinaorc";

    public Task<ProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var launcherPath = FindLauncherPath();
        return Task.FromResult(new ProviderStatus(
            launcherPath is not null,
            launcherPath is null ? null : "Rinaorc",
            launcherPath is null ? "The Rinaorc launcher was not found in the default locations." : null));
    }

    public Task<IReadOnlyList<GameEntry>> ScanInstalledAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var launcherPath = FindLauncherPath();
        IReadOnlyList<GameEntry> games = launcherPath is null
            ? []
            : [new GameEntry(
                Id,
                "launcher",
                "Rinaorc",
                launcherPath,
                true,
                false,
                ArtworkFilePath: launcherPath)];
        return Task.FromResult(games);
    }

    public Task<LaunchResult> LaunchAsync(GameEntry game, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (game.ProviderId != Id || game.ExternalId != "launcher")
        {
            return Task.FromResult(new LaunchResult(false, "The Rinaorc launcher entry is invalid."));
        }

        var launcherPath = FindLauncherPath();
        return Task.FromResult(launcherPath is null
            ? new LaunchResult(false, "The Rinaorc launcher could not be found.")
            : _startExecutable(launcherPath));
    }

    private string? FindLauncherPath() =>
        _launcherPathProvider()
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));

    private static IEnumerable<string> GetDefaultLauncherPaths()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        foreach (var path in new[]
        {
            Path.Combine(programFiles, "Rinaorc", "Rinaorc.exe"),
            Path.Combine(programFilesX86, "Rinaorc", "Rinaorc.exe"),
            Path.Combine(localAppData, "Programs", "Rinaorc", "Rinaorc.exe"),
            Path.Combine(localAppData, "Rinaorc", "Rinaorc.exe"),
            Path.Combine(roamingAppData, "Rinaorc", "Rinaorc.exe"),
            Path.Combine(programFiles, "Rinaorc", "Launcher.exe"),
            Path.Combine(programFilesX86, "Rinaorc", "Launcher.exe"),
            Path.Combine(localAppData, "Rinaorc", "Launcher.exe"),
            Path.Combine(roamingAppData, "Rinaorc", "Launcher.exe")
        })
        {
            yield return path;
        }

        foreach (var path in InstalledApplicationLocator.FindExecutablePaths(
                     "Rinaorc",
                     "Rinaorc.exe",
                     "RinaorcLauncher.exe",
                     "Launcher.exe"))
        {
            yield return path;
        }
    }
}
