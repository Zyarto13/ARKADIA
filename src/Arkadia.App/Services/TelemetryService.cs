using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Arkadia.Core.Models;
using Arkadia.Infrastructure.Storage;
using Dapper;

namespace Arkadia.App.Services;

public sealed class TelemetryEventEntry
{
    public long Id { get; set; }
    public string CreatedUtc { get; set; } = string.Empty;
    public string? SentUtc { get; set; }
    public string Event { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
}

public sealed class TelemetryService : IDisposable
{
    private const int MaximumQueuedEvents = 1000;
    private readonly ArkadiaDatabase _database;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public TelemetryService(ArkadiaDatabase database)
        : this(
            database,
            new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }),
            ownsHttpClient: true)
    {
    }

    public TelemetryService(ArkadiaDatabase database, HttpClient httpClient)
        : this(database, httpClient, ownsHttpClient: false)
    {
    }

    private TelemetryService(ArkadiaDatabase database, HttpClient httpClient, bool ownsHttpClient)
    {
        _database = database;
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    public void Dispose()
    {
        _sendLock.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    public async Task RecordAsync(
        string eventName,
        object payload,
        TelemetrySettings settings,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled(eventName, settings))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.InstallationId))
        {
            throw new InvalidOperationException("The anonymous telemetry installation identifier is missing.");
        }

        var payloadJson = JsonSerializer.Serialize(payload);
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO telemetry_queue(created_utc, event, payload_json)
            VALUES (@CreatedUtc, @Event, @PayloadJson);
            DELETE FROM telemetry_queue
            WHERE id NOT IN (
                SELECT id FROM telemetry_queue ORDER BY id DESC LIMIT @MaximumQueuedEvents
            );
            """,
            new
            {
                CreatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Event = eventName,
                PayloadJson = payloadJson,
                MaximumQueuedEvents
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<TelemetryEventEntry>> GetRecentEventsAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<TelemetryEventEntry>(new CommandDefinition(
            """
            SELECT id, created_utc AS CreatedUtc, NULL AS SentUtc, event AS Event, payload_json AS PayloadJson
            FROM telemetry_queue
            UNION ALL
            SELECT -id, created_utc AS CreatedUtc, sent_utc AS SentUtc, event AS Event, payload_json AS PayloadJson
            FROM telemetry_history
            ORDER BY CreatedUtc DESC
            LIMIT 50;
            """,
            cancellationToken: cancellationToken));
        return rows.ToArray();
    }

    public async Task PurgeDisabledCategoriesAsync(
        TelemetrySettings settings,
        CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM telemetry_queue
            WHERE (event LIKE 'crash.%' AND @CrashReports = 0)
               OR (event LIKE 'usage.%' AND @UsageStatistics = 0)
               OR (event LIKE 'performance.%' AND @Performance = 0);
            """,
            new
            {
                settings.CrashReports,
                settings.UsageStatistics,
                settings.Performance
            },
            cancellationToken: cancellationToken));
    }

    public async Task<int> SendPendingAsync(
        TelemetrySettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.CrashReports && !settings.UsageStatistics && !settings.Performance)
        {
            await PurgeDisabledCategoriesAsync(settings, cancellationToken);
            return 0;
        }

        if (string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            return 0;
        }

        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new InvalidOperationException("The telemetry endpoint must be a valid HTTPS URL.");
        }

        await PurgeDisabledCategoriesAsync(settings, cancellationToken);
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await _database.OpenConnectionAsync(cancellationToken);
            var pending = (await connection.QueryAsync<PendingTelemetryEvent>(new CommandDefinition(
                """
                SELECT id, created_utc AS CreatedUtc, event AS Event, payload_json AS PayloadJson
                FROM telemetry_queue
                ORDER BY id
                LIMIT 100;
                """,
                cancellationToken: cancellationToken))).ToArray();
            if (pending.Length == 0)
            {
                return 0;
            }

            var payloadDocuments = pending
                .Select(item => JsonDocument.Parse(item.PayloadJson))
                .ToArray();
            try
            {
                var requestBody = JsonSerializer.Serialize(new
                {
                    installationId = settings.InstallationId,
                    events = pending.Select((item, index) => new
                    {
                        item.Event,
                        item.CreatedUtc,
                        payload = payloadDocuments[index].RootElement
                    })
                });
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
                };
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
            }
            finally
            {
                foreach (var document in payloadDocuments)
                {
                    document.Dispose();
                }
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var sentUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            foreach (var item in pending)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO telemetry_history(created_utc, sent_utc, event, payload_json)
                    VALUES (@CreatedUtc, @SentUtc, @Event, @PayloadJson);
                    DELETE FROM telemetry_queue WHERE id = @Id;
                    """,
                    new
                    {
                        item.Id,
                        item.CreatedUtc,
                        SentUtc = sentUtc,
                        item.Event,
                        item.PayloadJson
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                DELETE FROM telemetry_history
                WHERE id NOT IN (
                    SELECT id FROM telemetry_history ORDER BY id DESC LIMIT @MaximumQueuedEvents
                );
                """,
                new { MaximumQueuedEvents },
                transaction,
                cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return pending.Length;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task ClearEventsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM telemetry_queue; DELETE FROM telemetry_history;",
            cancellationToken: cancellationToken));
    }

    private static bool IsEnabled(string eventName, TelemetrySettings settings) =>
        eventName.StartsWith("crash.", StringComparison.Ordinal)
            ? settings.CrashReports
            : eventName.StartsWith("usage.", StringComparison.Ordinal)
                ? settings.UsageStatistics
                : eventName.StartsWith("performance.", StringComparison.Ordinal)
                    && settings.Performance;

    private sealed record PendingTelemetryEvent(
        long Id,
        string CreatedUtc,
        string Event,
        string PayloadJson);
}
