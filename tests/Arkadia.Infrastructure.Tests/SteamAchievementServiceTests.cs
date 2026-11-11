using System.Net;
using System.Text;
using Arkadia.Core.Models;
using Arkadia.Infrastructure.Providers;

namespace Arkadia.Infrastructure.Tests;

public sealed class SteamAchievementServiceTests
{
    [Fact]
    public async Task LoadsAndSortsAchievementsFromTheMostRecentSteamProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"arkadia-steam-tests-{Guid.NewGuid():N}");
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
            """
            {
              "game": {
                "availableGameStats": {
                  "achievements": [
                    {
                      "name": "locked",
                      "displayName": "Locked",
                      "description": "Still locked",
                      "icon": "earned-locked.png",
                      "icongray": "gray.png"
                    },
                    {
                      "name": "unlocked",
                      "displayName": "Unlocked",
                      "description": "Already earned",
                      "icon": "earned.png",
                      "icongray": "gray-earned.png"
                    },
                    {
                      "name": "not-returned-by-player-endpoint",
                      "displayName": "Hidden achievement",
                      "description": "Secret",
                      "icon": "hidden.png",
                      "icongray": "hidden-gray.png"
                    }
                  ]
                }
              }
            }
            """,
            """
            {
              "playerstats": {
                "success": true,
                "achievements": [
                  {
                    "apiname": "locked",
                    "achieved": 0,
                    "name": "Locked",
                    "description": "Still locked",
                    "icongray": "gray.png",
                    "unlocktime": 0
                  },
                  {
                    "apiname": "unlocked",
                    "achieved": 1,
                    "name": "Unlocked",
                    "description": "Already earned",
                    "icon": "earned.png",
                    "unlocktime": 1700000000
                  }
                ]
              }
            }
            """);
        using var httpClient = new HttpClient(handler);
        var service = new SteamAchievementService(httpClient, root);

        try
        {
            var result = await service.GetAchievementsAsync(
                new GameEntry("steam", "480", "Test game", null, true, true),
                "api&key",
                CancellationToken.None);

            Assert.Equal(
                ["Unlocked", "Hidden achievement", "Locked"],
                result.Select(achievement => achievement.Name));
            Assert.True(result[0].IsUnlocked);
            Assert.Equal(
                "https://steamcdn-a.akamaihd.net/steamcommunity/public/images/apps/480/earned.png",
                result[0].IconUri);
            Assert.False(result[1].IsUnlocked);
            Assert.Equal(
                "https://steamcdn-a.akamaihd.net/steamcommunity/public/images/apps/480/hidden-gray.png",
                result[1].IconUri);
            Assert.False(result[2].IsUnlocked);
            Assert.Equal(
                "https://steamcdn-a.akamaihd.net/steamcommunity/public/images/apps/480/gray.png",
                result[2].IconUri);
            Assert.Equal(2, handler.RequestUris.Count);
            Assert.Contains("key=api%26key", handler.RequestUris[0].Query, StringComparison.Ordinal);
            Assert.Contains("appid=480", handler.RequestUris[0].Query, StringComparison.Ordinal);
            Assert.Contains("key=api%26key", handler.RequestUris[1].Query, StringComparison.Ordinal);
            Assert.Contains("steamid=76561197960265729", handler.RequestUris[1].Query, StringComparison.Ordinal);
            Assert.Contains("appid=480", handler.RequestUris[1].Query, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsMissingSteamAchievementSchemaAsUnavailableInsteadOfNoAchievements()
    {
        var root = Path.Combine(Path.GetTempPath(), $"arkadia-steam-tests-{Guid.NewGuid():N}");
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

        var handler = new FakeHttpMessageHandler("""{"game":{}}""");
        using var httpClient = new HttpClient(handler);
        var service = new SteamAchievementService(httpClient, root);

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.GetAchievementsAsync(
                    new GameEntry("steam", "480", "Test game", null, true, true),
                    "api-key",
                    CancellationToken.None));

            Assert.Contains("achievement schema", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Single(handler.RequestUris);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PreservesTheSteamApiErrorWhenPlayerAchievementsAreUnavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"arkadia-steam-tests-{Guid.NewGuid():N}");
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
            """{"game":{"availableGameStats":{"achievements":[]}}}""",
            """{"playerstats":{"success":false,"error":"Profile is not public"}}""");
        using var httpClient = new HttpClient(handler);
        var service = new SteamAchievementService(httpClient, root);

        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.GetAchievementsAsync(
                    new GameEntry("steam", "480", "Test game", null, true, true),
                    "api-key",
                    CancellationToken.None));

            Assert.Equal("Profile is not public", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsNonSteamGamesWithoutMakingARequest()
    {
        using var httpClient = new HttpClient(new FakeHttpMessageHandler("{}"));
        var service = new SteamAchievementService(httpClient, Path.GetTempPath());

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAchievementsAsync(
            new GameEntry("epic", "game", "Game", null, true, true),
            "api-key",
            CancellationToken.None));
    }

    private sealed class FakeHttpMessageHandler(params string[] responseBodies) : HttpMessageHandler
    {
        private int _responseIndex;
        public List<Uri> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    responseBodies[_responseIndex++],
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
