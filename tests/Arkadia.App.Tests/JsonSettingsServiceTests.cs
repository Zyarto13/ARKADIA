using System.IO;
using Arkadia.App.Services;
using Arkadia.Core.Models;
using Xunit;

namespace Arkadia.App.Tests;

public sealed class JsonSettingsServiceTests
{
    [Fact]
    public async Task SaveAndLoadAsyncRoundTripsSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"arkadia-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        var settings = new AppSettings(
            "Black",
            "fr",
            RunAtStartup: true,
            StartMinimized: true,
            CloseToTray: false,
            SingleInstance: false,
            MusicService: "Deezer",
            Shortcuts: [new AppShortcut(Guid.Parse("6e55c49e-8aad-4e84-9d6d-11456311f3eb"), "Docs", "https://example.com")],
            AudioProfiles:
            [
                new AudioVolumeProfile(
                    "Night",
                    [new AudioVolumeSetting("spotify", 0.25f, true)],
                    [new AudioDeviceVolumeSetting("headphones-id", false, 0.6f, false)])
            ],
            HomeTiles:
            [
                new HomeTileLayoutEntry("music"),
                new HomeTileLayoutEntry("steam", false)
            ]);

        try
        {
            using var service = new JsonSettingsService(path);
            await service.SaveAsync(settings, CancellationToken.None);
            var loaded = await service.LoadAsync(CancellationToken.None);

            Assert.Equal(
                settings with { Shortcuts = null, AudioProfiles = null, HomeTiles = null },
                loaded with { Shortcuts = null, AudioProfiles = null, HomeTiles = null });
            Assert.Equal(settings.Shortcuts, loaded.Shortcuts);
            Assert.Equal(settings.HomeTiles!.ToArray(), loaded.HomeTiles!.ToArray());
            Assert.Collection(
                loaded.AudioProfiles!,
                profile =>
                {
                    Assert.Equal("Night", profile.Name);
                    Assert.Equal([new AudioVolumeSetting("spotify", 0.25f, true)], profile.Sessions);
                    Assert.Equal(
                        [new AudioDeviceVolumeSetting("headphones-id", false, 0.6f, false)],
                        profile.Devices);
                });
        }

        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadAsyncUsesSpotifyForSettingsCreatedBeforeMusicServiceSelection()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"arkadia-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");

        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, """{"Theme":"Blue","Language":"en"}""");
            using var service = new JsonSettingsService(path);

            var loaded = await service.LoadAsync(CancellationToken.None);

            Assert.Equal("Spotify", loaded.MusicService);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveAsyncAtomicallyReplacesExistingSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"arkadia-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");

        try
        {
            using var service = new JsonSettingsService(path);
            await service.SaveAsync(new AppSettings("Blue", "en"), CancellationToken.None);
            await service.SaveAsync(new AppSettings("Light", "fr"), CancellationToken.None);

            var loaded = await service.LoadAsync(CancellationToken.None);

            Assert.Equal(new AppSettings("Light", "fr"), loaded);
            Assert.False(File.Exists($"{path}.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ConcurrentSavesCompleteWithoutLeavingTemporaryFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"arkadia-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");

        try
        {
            using var service = new JsonSettingsService(path);
            var settings = Enumerable.Range(0, 20)
                .Select(index => new AppSettings(
                    index % 2 == 0 ? "Black" : "Light",
                    index % 2 == 0 ? "fr" : "en"))
                .ToArray();

            await Task.WhenAll(settings.Select(item => service.SaveAsync(item, CancellationToken.None)));

            var loaded = await service.LoadAsync(CancellationToken.None);
            Assert.Contains(loaded, settings);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
