using System.Text.Json;
using System.Globalization;
using Arkadia.Core.Models;
using Arkadia.Core.Services;
using Arkadia.Infrastructure.Providers;

namespace Arkadia.Infrastructure.Tests;

public sealed class GameProviderTests
{
    [Fact]
    public async Task SteamProviderFindsInstalledGamesAcrossLibraries()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var secondaryLibrary = Path.Combine(root, "secondary");
            WriteSteamManifest(
                Path.Combine(root, "steamapps"),
                "480",
                "Spacewar",
                "Spacewar");
            WriteSteamManifest(
                Path.Combine(secondaryLibrary, "steamapps"),
                "1234",
                "Example Game",
                "ExampleGame");
            WriteSteamManifest(
                Path.Combine(root, "steamapps"),
                "228980",
                "Steamworks Common Redistributables",
                "Steamworks Shared");
            await File.WriteAllTextAsync(
                Path.Combine(root, "steamapps", "libraryfolders.vdf"),
                $$"""
                "libraryfolders"
                {
                    "0"
                    {
                        "path" "{{root.Replace("\\", "\\\\", StringComparison.Ordinal)}}"
                    }
                    "1"
                    {
                        "path" "{{secondaryLibrary.Replace("\\", "\\\\", StringComparison.Ordinal)}}"
                    }
                }
                """);

            var provider = new SteamGameProvider(new FakeLauncher(), root);
            var games = await provider.ScanInstalledAsync(CancellationToken.None);

            Assert.Equal(["Example Game", "Spacewar"], games.Select(game => game.Title));
            Assert.All(games, game => Assert.Equal("steam", game.ProviderId));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SteamProviderUsesLauncherProtocolForInstalledGame()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            WriteSteamManifest(Path.Combine(root, "steamapps"), "1234", "Example Game", "ExampleGame");
            var launcher = new FakeLauncher();
            var provider = new SteamGameProvider(launcher, root);
            var game = Assert.Single(await provider.ScanInstalledAsync(CancellationToken.None));

            var result = await provider.LaunchAsync(game, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal("steam://rungameid/1234", launcher.LastUri);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SteamProviderReadsPlaytimeFromTheMostRecentLocalProfile()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            WriteSteamManifest(Path.Combine(root, "steamapps"), "1234", "Example Game", "ExampleGame");
            Directory.CreateDirectory(Path.Combine(root, "config"));
            await File.WriteAllTextAsync(
                Path.Combine(root, "config", "loginusers.vdf"),
                """
                "users"
                {
                    "76561197960265729"
                    {
                        "MostRecent" "1"
                    }
                }
                """);
            var userConfig = Path.Combine(root, "userdata", "1", "config");
            Directory.CreateDirectory(userConfig);
            await File.WriteAllTextAsync(
                Path.Combine(userConfig, "localconfig.vdf"),
                """
                "UserLocalConfigStore"
                {
                    "Software"
                    {
                        "Valve"
                        {
                            "Steam"
                            {
                                "apps"
                                {
                                    "1234"
                                    {
                                        "Playtime" "125"
                                    }
                                }
                            }
                        }
                    }
                }
                """);

            var provider = new SteamGameProvider(new FakeLauncher(), root);
            var game = Assert.Single(await provider.ScanInstalledAsync(CancellationToken.None));

            Assert.Equal(125, game.PlaytimeMinutes);
            Assert.Equal(new Uri("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1234/header.jpg"), game.ArtworkUri);
            Assert.Equal(
                string.Format(CultureInfo.CurrentCulture, "{0:0.#} h", 125 / 60d),
                game.PlaytimeText);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SteamProviderLoadsOwnedGamesForTheMostRecentPublicProfile()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "config"));
            await File.WriteAllTextAsync(
                Path.Combine(root, "config", "loginusers.vdf"),
                """
                "users"
                {
                    "76561197960265729"
                    {
                        "MostRecent" "1"
                    }
                }
                """);

            var handler = new FakeHttpMessageHandler(
                """{"response":{"game_count":2,"games":[{"appid":1234,"name":"Example Game","playtime_forever":125},{"appid":5678,"name":"Untouched Game","playtime_forever":0}]}}""");
            using var httpClient = new HttpClient(handler);
            var provider = new SteamGameProvider(new FakeLauncher(), httpClient, root);

            var games = await provider.ScanOwnedAsync("test-api-key", CancellationToken.None);

            Assert.Equal(["Example Game", "Untouched Game"], games.Select(game => game.Title));
            Assert.All(games, game =>
            {
                Assert.Equal("steam", game.ProviderId);
                Assert.False(game.IsInstalled);
                Assert.True(game.IsOwned);
                Assert.Null(game.InstallPath);
            });
            Assert.Equal(125, games[0].PlaytimeMinutes);
            Assert.Equal(0, games[1].PlaytimeMinutes);
            Assert.Contains("steamid=76561197960265729", handler.LastRequestUri!.Query);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SteamProviderReportsPrivateOwnedGameListsInsteadOfReturningAnEmptyLibrary()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "config"));
            await File.WriteAllTextAsync(
                Path.Combine(root, "config", "loginusers.vdf"),
                """
                "users"
                {
                    "76561197960265729"
                    {
                        "MostRecent" "1"
                    }
                }
                """);
            using var httpClient = new HttpClient(new FakeHttpMessageHandler("""{"response":{}}"""));
            var provider = new SteamGameProvider(new FakeLauncher(), httpClient, root);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.ScanOwnedAsync("test-api-key", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EpicProviderFindsCompleteManifestsAndLaunchesByAppName()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var installPath = Path.Combine(root, "installed");
            Directory.CreateDirectory(installPath);
            var executablePath = Path.Combine(installPath, "ExampleGame.exe");
            await File.WriteAllBytesAsync(executablePath, [0]);
            await WriteEpicManifestAsync(root, "installed.item", new
            {
                AppName = "ExampleGame",
                DisplayName = "Example Game",
                InstallLocation = installPath,
                LaunchExecutable = "ExampleGame.exe",
                bIsIncomplete = false
            });
            await WriteEpicManifestAsync(root, "incomplete.item", new
            {
                AppName = "PendingGame",
                DisplayName = "Pending Game",
                InstallLocation = installPath,
                bIsIncomplete = true
            });

            var launcher = new FakeLauncher();
            var provider = new EpicGamesProvider(launcher, root);
            var game = Assert.Single(await provider.ScanInstalledAsync(CancellationToken.None));

            Assert.Equal("Example Game", game.Title);
            Assert.Equal(executablePath, game.ArtworkFilePath);
            var result = await provider.LaunchAsync(game, CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Equal(
                "com.epicgames.launcher://apps/ExampleGame?action=launch&silent=true",
                launcher.LastUri);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EpicProviderRejectsUnsafeAppNamesBeforeLaunching()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var launcher = new FakeLauncher();
            var provider = new EpicGamesProvider(launcher, root);
            var result = await provider.LaunchAsync(
                new GameEntry("epic", "../unsafe", "Unsafe", null, true, true),
                CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Null(launcher.LastUri);
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

    private static void WriteSteamManifest(string steamAppsDirectory, string appId, string title, string installDirectory)
    {
        var commonDirectory = Path.Combine(steamAppsDirectory, "common", installDirectory);
        Directory.CreateDirectory(commonDirectory);
        Directory.CreateDirectory(steamAppsDirectory);
        File.WriteAllText(
            Path.Combine(steamAppsDirectory, $"appmanifest_{appId}.acf"),
            $$"""
            "AppState"
            {
                "appid" "{{appId}}"
                "name" "{{title}}"
                "installdir" "{{installDirectory}}"
            }
            """);
    }

    private static async Task WriteEpicManifestAsync<T>(string directory, string fileName, T manifest)
    {
        await File.WriteAllTextAsync(
            Path.Combine(directory, fileName),
            JsonSerializer.Serialize(manifest));
    }

    private sealed class FakeLauncher : IExternalLauncher
    {
        public string? LastUri { get; private set; }

        public Task<LaunchResult> OpenUriAsync(string uri, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastUri = uri;
            return Task.FromResult(new LaunchResult(true));
        }
    }

    private sealed class FakeHttpMessageHandler(string responseBody) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            });
        }
    }
}
