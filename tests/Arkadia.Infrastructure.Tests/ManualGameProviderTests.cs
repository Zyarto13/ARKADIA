using Arkadia.Core.Models;
using Arkadia.Core.Services;
using Arkadia.Infrastructure.Providers;

namespace Arkadia.Infrastructure.Tests;

public sealed class ManualGameProviderTests
{
    [Fact]
    public async Task ScanInstalledAsyncReturnsValidManualEntriesAndMarksMissingFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"arkadia-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var existingPath = Path.Combine(directory, "ExistingGame.exe");
        var missingPath = Path.Combine(directory, "MissingGame.exe");
        var invalidPath = Path.Combine(directory, "Readme.txt");
        await File.WriteAllTextAsync(existingPath, "test fixture");
        await File.WriteAllTextAsync(invalidPath, "not a game");
        var existingId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var settings = new MemorySettingsService(new AppSettings(
            "Blue",
            "en",
            ManualGames:
            [
                new ManualGameDefinition(existingId, "Existing Game", existingPath),
                new ManualGameDefinition(missingId, "Missing Game", missingPath),
                new ManualGameDefinition(Guid.NewGuid(), "Unsupported", invalidPath)
            ]));

        try
        {
            var provider = new ManualGameProvider(settings);

            var games = await provider.ScanInstalledAsync(CancellationToken.None);

            Assert.Collection(
                games,
                game =>
                {
                    Assert.Equal("manual", game.ProviderId);
                    Assert.Equal(existingId.ToString("N"), game.ExternalId);
                    Assert.Equal("Existing Game", game.Title);
                    Assert.True(game.IsInstalled);
                },
                game =>
                {
                    Assert.Equal(missingId.ToString("N"), game.ExternalId);
                    Assert.False(game.IsInstalled);
                });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LaunchAsyncRejectsAnUnrecognizedGameIdentifier()
    {
        var provider = new ManualGameProvider(
            new MemorySettingsService(new AppSettings("Blue", "en")));

        var result = await provider.LaunchAsync(
            new GameEntry("manual", "not-a-guid", "Unknown", null, true, false),
            CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    private sealed class MemorySettingsService(AppSettings settings) : ISettingsService
    {
        private AppSettings _settings = settings;

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_settings);
        }

        public Task SaveAsync(AppSettings updatedSettings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _settings = updatedSettings;
            return Task.CompletedTask;
        }
    }
}
