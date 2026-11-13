using System.IO;
using System.Net;
using System.Net.Http;
using Arkadia.App.Services;
using Arkadia.Core.Models;
using Arkadia.Infrastructure.Storage;
using Xunit;

namespace Arkadia.App.Tests;

public sealed class TelemetryServiceTests
{
    [Fact]
    public async Task RecordAsyncDoesNotQueueEventsWithoutCategoryConsent()
    {
        using var directory = new TemporaryDirectory();
        var database = new ArkadiaDatabase(Path.Combine(directory.Path, "arkadia.db"));
        await database.InitializeAsync(CancellationToken.None);
        using var httpClient = new HttpClient(new StubHttpMessageHandler());
        using var service = new TelemetryService(database, httpClient);

        await service.RecordAsync(
            "usage.application_started",
            new { version = "1.0.0" },
            new TelemetrySettings(InstallationId: "test-installation"),
            CancellationToken.None);

        Assert.Empty(await service.GetRecentEventsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SendPendingAsyncSendsOnlyConsentedEventsAndMovesThemToHistory()
    {
        using var directory = new TemporaryDirectory();
        var database = new ArkadiaDatabase(Path.Combine(directory.Path, "arkadia.db"));
        await database.InitializeAsync(CancellationToken.None);
        var handler = new StubHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        using var service = new TelemetryService(database, httpClient);
        var settings = new TelemetrySettings(
            UsageStatistics: true,
            Endpoint: "https://telemetry.example/events",
            InstallationId: "test-installation");

        await service.RecordAsync(
            "usage.application_started",
            new { version = "1.0.0" },
            settings,
            CancellationToken.None);

        var sentCount = await service.SendPendingAsync(settings, CancellationToken.None);
        var events = await service.GetRecentEventsAsync(CancellationToken.None);

        Assert.Equal(1, sentCount);
        Assert.Equal(1, handler.RequestCount);
        Assert.Contains("test-installation", handler.LastRequestBody);
        var sentEvent = Assert.Single(events);
        Assert.Equal("usage.application_started", sentEvent.Event);
        Assert.False(string.IsNullOrWhiteSpace(sentEvent.SentUtc));
        Assert.Equal(0, await service.SendPendingAsync(settings, CancellationToken.None));
    }

    [Fact]
    public async Task SendPendingAsyncPurgesEventsAfterConsentIsWithdrawn()
    {
        using var directory = new TemporaryDirectory();
        var database = new ArkadiaDatabase(Path.Combine(directory.Path, "arkadia.db"));
        await database.InitializeAsync(CancellationToken.None);
        var handler = new StubHttpMessageHandler();
        using var httpClient = new HttpClient(handler);
        using var service = new TelemetryService(database, httpClient);
        var optedIn = new TelemetrySettings(
            UsageStatistics: true,
            Endpoint: "https://telemetry.example/events",
            InstallationId: "test-installation");

        await service.RecordAsync(
            "usage.application_started",
            new { version = "1.0.0" },
            optedIn,
            CancellationToken.None);

        var sentCount = await service.SendPendingAsync(
            optedIn with { UsageStatistics = false },
            CancellationToken.None);

        Assert.Equal(0, sentCount);
        Assert.Equal(0, handler.RequestCount);
        Assert.Empty(await service.GetRecentEventsAsync(CancellationToken.None));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"arkadia-telemetry-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
