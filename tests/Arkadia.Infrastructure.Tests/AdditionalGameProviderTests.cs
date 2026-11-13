using System.Text.Json;
using Arkadia.Core.Models;
using Arkadia.Infrastructure.Providers;

namespace Arkadia.Infrastructure.Tests;

public sealed class AdditionalGameProviderTests
{
    [Fact]
    public async Task MinecraftProviderFindsAndLaunchesOnlyTheDetectedLauncher()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var launcherPath = Path.Combine(root, "MinecraftLauncher.exe");
            await File.WriteAllTextAsync(launcherPath, "fixture");
            string? launchedPath = null;
            var provider = new MinecraftGameProvider(
                [launcherPath],
                path =>
                {
                    launchedPath = path;
                    return new LaunchResult(true);
                });

            var game = Assert.Single(await provider.ScanInstalledAsync(CancellationToken.None));
            var result = await provider.LaunchAsync(game, CancellationToken.None);

            Assert.Equal("minecraft", game.ProviderId);
            Assert.True(result.Succeeded);
            Assert.Equal(launcherPath, launchedPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CurseForgeProviderReadsLocalInstancesAndOpensItsLauncher()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var instancesDirectory = Path.Combine(root, "Instances");
            var instanceDirectory = Path.Combine(instancesDirectory, "ExamplePack");
            var launcherPath = Path.Combine(root, "CurseForge.exe");
            Directory.CreateDirectory(instanceDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(instanceDirectory, "minecraftinstance.json"),
                JsonSerializer.Serialize(new { name = "Example Modpack" }));
            await File.WriteAllTextAsync(launcherPath, "fixture");
            string? launchedPath = null;
            var provider = new CurseForgeGameProvider(
                instancesDirectory,
                [launcherPath],
                path =>
                {
                    launchedPath = path;
                    return new LaunchResult(true);
                });

            var game = Assert.Single(await provider.ScanInstalledAsync(CancellationToken.None));
            var result = await provider.LaunchAsync(game, CancellationToken.None);

            Assert.Equal("curseforge", game.ProviderId);
            Assert.Equal("Example Modpack", game.Title);
            Assert.True(result.Succeeded);
            Assert.Equal(launcherPath, launchedPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CurseForgeProviderRejectsInstancesOutsideItsManagedDirectory()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var instancesDirectory = Path.Combine(root, "Instances");
            var externalInstance = Path.Combine(root, "Elsewhere", "Pack");
            var launcherPath = Path.Combine(root, "CurseForge.exe");
            Directory.CreateDirectory(externalInstance);
            await File.WriteAllTextAsync(
                Path.Combine(externalInstance, "minecraftinstance.json"),
                """{"name":"External Pack"}""");
            await File.WriteAllTextAsync(launcherPath, "fixture");
            var provider = new CurseForgeGameProvider(
                instancesDirectory,
                [launcherPath],
                _ => new LaunchResult(true));

            var result = await provider.LaunchAsync(
                new GameEntry("curseforge", "Pack", "External Pack", externalInstance, true, true),
                CancellationToken.None);

            Assert.False(result.Succeeded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RinaorcProviderReturnsNoGameWhenNoLauncherIsPresent()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var provider = new RinaorcGameProvider([Path.Combine(root, "missing.exe")]);

            Assert.Empty(await provider.ScanInstalledAsync(CancellationToken.None));
            Assert.False((await provider.GetStatusAsync(CancellationToken.None)).IsAvailable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PcGameScannerOnlyProposesExecutablesWithGameMarkers()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var gameDirectory = Path.Combine(root, "ExampleGame");
            var unrelatedDirectory = Path.Combine(root, "Unrelated");
            Directory.CreateDirectory(gameDirectory);
            Directory.CreateDirectory(unrelatedDirectory);
            await File.WriteAllTextAsync(Path.Combine(gameDirectory, "UnityPlayer.dll"), "fixture");
            await File.WriteAllTextAsync(Path.Combine(gameDirectory, "ExampleGame.exe"), "fixture");
            await File.WriteAllTextAsync(Path.Combine(gameDirectory, "Setup.exe"), "fixture");
            await File.WriteAllTextAsync(Path.Combine(unrelatedDirectory, "Tool.exe"), "fixture");
            var result = await PcGameScanner.ScanAsync(root, CancellationToken.None);

            var candidate = Assert.Single(result.Candidates);
            Assert.Equal("ExampleGame", candidate.Title);
            Assert.EndsWith("ExampleGame.exe", candidate.ExecutablePath, StringComparison.OrdinalIgnoreCase);
            Assert.False(result.WasTruncated);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arkadia-provider-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
