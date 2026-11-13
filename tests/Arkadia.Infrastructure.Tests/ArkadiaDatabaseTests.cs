using System.IO;
using System.Globalization;
using Arkadia.Infrastructure.Storage;
using Dapper;

namespace Arkadia.Infrastructure.Tests;

public sealed class ArkadiaDatabaseTests
{
    [Fact]
    public async Task InitializeCreatesSchemaAndPassesQuickCheck()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"arkadia-db-tests-{Guid.NewGuid():N}");
        var database = new ArkadiaDatabase(Path.Combine(directory, "arkadia.db"));

        try
        {
            await database.InitializeAsync(CancellationToken.None);
            await using var connection = await database.OpenConnectionAsync(CancellationToken.None);

            await using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "SELECT MAX(version) FROM schema_version;";
            var schemaVersion = await versionCommand.ExecuteScalarAsync();

            await using var gamesCommand = connection.CreateCommand();
            gamesCommand.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'games';";
            var gamesTableCount = await gamesCommand.ExecuteScalarAsync();

            Assert.Equal(2L, Convert.ToInt64(schemaVersion, CultureInfo.InvariantCulture));
            Assert.Equal(1L, Convert.ToInt64(gamesTableCount, CultureInfo.InvariantCulture));
            Assert.True(await database.QuickCheckAsync(CancellationToken.None));
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
    public async Task InitializeMigratesVersionOneTelemetryQueue()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"arkadia-db-tests-{Guid.NewGuid():N}");
        var database = new ArkadiaDatabase(Path.Combine(directory, "arkadia.db"));

        try
        {
            await database.InitializeAsync(CancellationToken.None);
            await using (var connection = await database.OpenConnectionAsync(CancellationToken.None))
            {
                await connection.ExecuteAsync(
                    "DROP TABLE telemetry_history; UPDATE schema_version SET version = 1;");
            }

            await database.InitializeAsync(CancellationToken.None);
            await using var migratedConnection = await database.OpenConnectionAsync(CancellationToken.None);
            var schemaVersion = await migratedConnection.ExecuteScalarAsync<int>(
                "SELECT MAX(version) FROM schema_version;");
            var historyTableCount = await migratedConnection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'telemetry_history';");

            Assert.Equal(2, schemaVersion);
            Assert.Equal(1, historyTableCount);
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
    public async Task InitializeIsIdempotent()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"arkadia-db-tests-{Guid.NewGuid():N}");
        var database = new ArkadiaDatabase(Path.Combine(directory, "arkadia.db"));

        try
        {
            await database.InitializeAsync(CancellationToken.None);
            await database.InitializeAsync(CancellationToken.None);
            await using var connection = await database.OpenConnectionAsync(CancellationToken.None);

            await using var versionCommand = connection.CreateCommand();
            versionCommand.CommandText = "SELECT COUNT(*) FROM schema_version;";
            var versionRows = await versionCommand.ExecuteScalarAsync();

            Assert.Equal(1L, Convert.ToInt64(versionRows, CultureInfo.InvariantCulture));
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
    public async Task DailyBackupCanRestoreCorruptedDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"arkadia-db-tests-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(directory, "arkadia.db");
        var database = new ArkadiaDatabase(databasePath);

        try
        {
            await database.InitializeAsync(CancellationToken.None);
            await using (var connection = await database.OpenConnectionAsync(CancellationToken.None))
            {
                await connection.ExecuteAsync(
                    "INSERT INTO accounts(provider, status) VALUES ('steam', 'connected');");
            }

            await database.CreateDailyBackupAsync(CancellationToken.None);
            Assert.Single(Directory.GetFiles(Path.Combine(directory, "backups"), "arkadia-*.db"));

            File.WriteAllBytes(databasePath, [0x00, 0x01, 0x02, 0x03]);
            File.Delete($"{databasePath}-wal");
            File.Delete($"{databasePath}-shm");

            Assert.True(await database.RestoreLatestBackupIfCorruptedAsync(CancellationToken.None));
            Assert.True(await database.QuickCheckAsync(CancellationToken.None));
            await using (var restoredConnection = await database.OpenConnectionAsync(CancellationToken.None))
            {
                var status = await restoredConnection.ExecuteScalarAsync<string>(
                    "SELECT status FROM accounts WHERE provider = 'steam';");
                Assert.Equal("connected", status);
            }
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
