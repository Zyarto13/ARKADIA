using Arkadia.Core.Models;
using Arkadia.Core.Providers;

namespace Arkadia.Infrastructure.Providers;

public sealed class MinecraftGameProvider : IGameProvider
{
    private readonly Func<IEnumerable<string>> _launcherPathProvider;
    private readonly Func<string, LaunchResult> _startExecutable;

    public MinecraftGameProvider(
        IEnumerable<string>? launcherPaths = null,
        Func<string, LaunchResult>? startExecutable = null)
    {
        _launcherPathProvider = launcherPaths is null
            ? GetDefaultLauncherPaths
            : () => launcherPaths;
        _startExecutable = startExecutable ?? GameExecutableLauncher.Start;
    }

    public string Id => "minecraft";

    public Task<ProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var launcherPath = FindLauncherPath();
        return Task.FromResult(new ProviderStatus(
            launcherPath is not null,
            launcherPath is null ? null : "Minecraft Launcher",
            launcherPath is null ? "The Minecraft Launcher was not found on this PC." : null));
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
                "Minecraft Launcher",
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
            return Task.FromResult(new LaunchResult(false, "The Minecraft launcher entry is invalid."));
        }

        var launcherPath = FindLauncherPath();
        return Task.FromResult(launcherPath is null
            ? new LaunchResult(false, "The Minecraft Launcher could not be found.")
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

        foreach (var path in new[]
        {
            Path.Combine(programFiles, "Minecraft Launcher", "MinecraftLauncher.exe"),
            Path.Combine(programFilesX86, "Minecraft Launcher", "MinecraftLauncher.exe"),
            Path.Combine(localAppData, "Programs", "Minecraft Launcher", "MinecraftLauncher.exe")
        })
        {
            yield return path;
        }

        foreach (var path in InstalledApplicationLocator.FindExecutablePaths(
                     "Minecraft Launcher",
                     "MinecraftLauncher.exe",
                     "Minecraft.exe"))
        {
            yield return path;
        }
    }
}
