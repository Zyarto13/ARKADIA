using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using Arkadia.App.Services;
using Arkadia.Core.Models;
using Xunit;

namespace Arkadia.App.Tests;

public sealed class GameArtworkServiceTests
{
    private static readonly byte[] TestPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADUlEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC");

    [Fact]
    public async Task CachedArtworkLoadsWithoutNetworkWhenOffline()
    {
        var cacheDirectory = CreateCacheDirectory();
        try
        {
            var game = new GameEntry("steam", "440", "Team Fortress 2", null, true, true);
            using var onlineClient = CreateClient((_, _) =>
                Task.FromResult(CreateImageResponse()));
            var onlineService = new GameArtworkService(
                onlineClient,
                cacheDirectory,
                networkAvailable: () => true);

            var downloadedArtwork = await onlineService.LoadArtworkAsync(game);
            Assert.NotNull(downloadedArtwork);

            using var offlineClient = CreateClient((_, _) =>
                throw new InvalidOperationException("Offline cache access must not issue network requests."));
            var offlineService = new GameArtworkService(
                offlineClient,
                cacheDirectory,
                networkAvailable: () => false);

            var cachedArtwork = await offlineService.LoadArtworkAsync(game);

            Assert.NotNull(cachedArtwork);
        }
        finally
        {
            Directory.Delete(cacheDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CachedArtworkIsReturnedBeforeBackgroundRefreshCompletes()
    {
        var cacheDirectory = CreateCacheDirectory();
        var refreshStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var continueRefresh = new TaskCompletionSource<HttpResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var artworkRefreshed = new TaskCompletionSource<GameArtworkUpdatedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var game = new GameEntry("steam", "440", "Team Fortress 2", null, true, true);
            var requestCount = 0;
            using var client = CreateClient(async (_, cancellationToken) =>
            {
                if (Interlocked.Increment(ref requestCount) == 1)
                {
                    return CreateImageResponse();
                }

                refreshStarted.TrySetResult();
                return await continueRefresh.Task.WaitAsync(cancellationToken);
            });
            var service = new GameArtworkService(
                client,
                cacheDirectory,
                networkAvailable: () => true);
            Assert.NotNull(await service.LoadArtworkAsync(game));

            var metadataPath = Assert.Single(Directory.GetFiles(cacheDirectory, "*.meta.json"));
            await File.WriteAllTextAsync(
                metadataPath,
                $$"""{"FetchedUtc":"{{DateTimeOffset.MinValue:O}}","LastAttemptUtc":"{{DateTimeOffset.MinValue:O}}"}""");
            service.ArtworkUpdated += (_, args) => artworkRefreshed.TrySetResult(args);

            var cachedArtwork = await service.LoadArtworkAsync(game);

            Assert.NotNull(cachedArtwork);
            await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(continueRefresh.Task.IsCompleted);
            CompleteRefresh(continueRefresh);
            var update = await artworkRefreshed.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(game, update.Game);
            Assert.NotNull(update.Artwork);
            Assert.Equal(2, Volatile.Read(ref requestCount));
        }
        finally
        {
            CompleteRefresh(continueRefresh);
            await Task.WhenAny(artworkRefreshed.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Directory.Delete(cacheDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ArtworkCacheEvictsLeastRecentlyUsedImagesWhenTheSizeLimitIsExceeded()
    {
        var cacheDirectory = CreateCacheDirectory();
        try
        {
            var requestCount = 0;
            using var client = CreateClient((_, _) =>
            {
                Interlocked.Increment(ref requestCount);
                return Task.FromResult(CreateImageResponse());
            });
            var service = new GameArtworkService(
                client,
                cacheDirectory,
                networkAvailable: () => true,
                maximumCacheBytes: TestPng.Length + 1);
            var firstGame = new GameEntry("steam", "440", "Team Fortress 2", null, true, true);
            var secondGame = new GameEntry("steam", "570", "Dota 2", null, true, true);

            Assert.NotNull(await service.LoadArtworkAsync(firstGame));
            var firstArtworkPath = Assert.Single(Directory.GetFiles(cacheDirectory, "*.img"));
            File.SetLastAccessTimeUtc(firstArtworkPath, DateTime.UtcNow.AddDays(-1));

            Assert.NotNull(await service.LoadArtworkAsync(secondGame));

            var remainingArtworkPath = Assert.Single(Directory.GetFiles(cacheDirectory, "*.img"));
            Assert.NotEqual(firstArtworkPath, remainingArtworkPath);
            Assert.NotNull(await service.LoadArtworkAsync(secondGame));
            Assert.Equal(2, Volatile.Read(ref requestCount));
        }
        finally
        {
            Directory.Delete(cacheDirectory, recursive: true);
        }
    }

    private static HttpResponseMessage CreateImageResponse() =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(TestPng)
        };

    private static string CreateCacheDirectory()
    {
        var cacheDirectory = Path.Combine(
            Path.GetTempPath(),
            $"arkadia-artwork-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cacheDirectory);
        return cacheDirectory;
    }

    private static void CompleteRefresh(
        TaskCompletionSource<HttpResponseMessage> continueRefresh)
    {
        var response = CreateImageResponse();
        if (!continueRefresh.TrySetResult(response))
        {
            response.Dispose();
        }
    }

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) =>
        new(new StubHttpMessageHandler(responseFactory));

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responseFactory(request, cancellationToken);
    }
}
