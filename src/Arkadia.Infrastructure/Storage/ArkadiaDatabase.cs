using System.IO;
using Arkadia.Core.Services;
using Dapper;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Arkadia.Infrastructure.Storage;

public sealed class ArkadiaDatabase : IAppDatabase
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _connectionString;
    private readonly string _databasePath;

    public ArkadiaDatabase(string? databasePath = null)
    {
        _databasePath = Path.GetFullPath(databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Arkadia",
            "arkadia.db"));
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
    }

    public SqliteConnection CreateConnection() => new(_connectionString);

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "PRAGMA journal_mode=WAL;",
            cancellationToken: cancellationToken));
        await ConfigureConnectionAsync(connection, cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL, applied_utc TEXT NOT NULL);",
            transaction: transaction,
            cancellationToken: cancellationToken));

        var currentVersion = await connection.QuerySingleOrDefaultAsync<int?>(
            new CommandDefinition(
                "SELECT MAX(version) FROM schema_version;",
                transaction: transaction,
                cancellationToken: cancellationToken));

        if (currentVersion is null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                InitialSchema,
                transaction: transaction,
                cancellationToken: cancellationToken));
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO schema_version(version, applied_utc) VALUES (@Version, @AppliedUtc);",
                new
                {
                    Version = CurrentSchemaVersion,
                    AppliedUtc = DateTimeOffset.UtcNow.ToString("O")
                },
                transaction,
                cancellationToken: cancellationToken));
        }
        else if (currentVersion > CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Database schema version {currentVersion} is newer than supported version {CurrentSchemaVersion}.");
        }
        else if (currentVersion < CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Database schema version {currentVersion} requires a migration that is not available.");
        }

        await transaction.CommitAsync(cancellationToken);

        if (!await QuickCheckAsync(cancellationToken))
        {
            throw new InvalidDataException("The ARKADIA database failed SQLite quick_check.");
        }
    }

    public async Task<bool> QuickCheckAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var result = await connection.ExecuteScalarAsync<string>(
            new CommandDefinition("PRAGMA quick_check;", cancellationToken: cancellationToken));
        return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> RestoreLatestBackupIfCorruptedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_databasePath))
        {
            return false;
        }

        var isHealthy = false;
        try
        {
            isHealthy = await QuickCheckAsync(cancellationToken);
        }
        catch (SqliteException)
        {
        }

        if (isHealthy)
        {
            return false;
        }

        foreach (var backupPath in GetBackupPaths().OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!await new ArkadiaDatabase(backupPath).QuickCheckAsync(cancellationToken))
                {
                    continue;
                }
            }
            catch (SqliteException)
            {
                continue;
            }

            var restorePath = $"{_databasePath}.restore.tmp";
            File.Copy(backupPath, restorePath, overwrite: true);
            DeleteSidecarFiles(_databasePath);
            File.Move(restorePath, _databasePath, overwrite: true);
            return true;
        }

        throw new InvalidDataException(
            "The ARKADIA database is corrupted and no valid automatic backup could be restored.");
    }

    public async Task CreateDailyBackupAsync(CancellationToken cancellationToken)
    {
        var backupDirectory = GetBackupDirectory();
        Directory.CreateDirectory(backupDirectory);

        var date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var backupPath = Path.Combine(backupDirectory, $"arkadia-{date}.db");
        var temporaryPath = $"{backupPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var source = await OpenConnectionAsync(cancellationToken))
            await using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = temporaryPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString()))
            {
                await destination.OpenAsync(cancellationToken);
                source.BackupDatabase(destination);
            }

            if (!await new ArkadiaDatabase(temporaryPath).QuickCheckAsync(cancellationToken))
            {
                throw new InvalidDataException("The generated ARKADIA database backup failed SQLite quick_check.");
            }

            File.Move(temporaryPath, backupPath, overwrite: true);
            RotateBackups();
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken);
            await ConfigureConnectionAsync(connection, cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            "PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;",
            cancellationToken: cancellationToken));
    }

    private string[] GetBackupPaths()
    {
        var backupDirectory = GetBackupDirectory();
        if (!Directory.Exists(backupDirectory))
        {
            return [];
        }

        return Directory.GetFiles(backupDirectory, "arkadia-*.db");
    }

    private string GetBackupDirectory() =>
        Path.Combine(Path.GetDirectoryName(_databasePath)!, "backups");

    private void RotateBackups()
    {
        var backups = GetBackupPaths()
            .Select(path => new
            {
                Path = path,
                Date = ParseBackupDate(path)
            })
            .Where(item => item.Date is not null)
            .OrderByDescending(item => item.Date)
            .ToArray();

        foreach (var backup in backups.Skip(7))
        {
            File.Delete(backup.Path);
        }
    }

    private static DateTime? ParseBackupDate(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        return fileName.StartsWith("arkadia-", StringComparison.Ordinal)
            && DateTime.TryParseExact(
                fileName["arkadia-".Length..],
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date)
                ? date
                : null;
    }

    private static void DeleteSidecarFiles(string databasePath)
    {
        File.Delete($"{databasePath}-wal");
        File.Delete($"{databasePath}-shm");
    }

    private const string InitialSchema = """
        CREATE TABLE accounts (
            provider TEXT PRIMARY KEY,
            display_name TEXT,
            external_id TEXT,
            avatar_path TEXT,
            status TEXT NOT NULL,
            last_sync_utc TEXT
        );
        CREATE TABLE games (
            id INTEGER PRIMARY KEY,
            provider TEXT NOT NULL,
            external_id TEXT,
            title TEXT NOT NULL,
            sort_title TEXT,
            install_path TEXT,
            exe_path TEXT,
            launch_uri TEXT,
            is_installed INTEGER NOT NULL,
            is_owned INTEGER NOT NULL,
            is_manual INTEGER NOT NULL DEFAULT 0,
            is_hidden INTEGER NOT NULL DEFAULT 0,
            is_favorite INTEGER NOT NULL DEFAULT 0,
            cover_path TEXT,
            header_path TEXT,
            added_utc TEXT NOT NULL,
            last_seen_utc TEXT,
            last_played_utc TEXT,
            total_playtime_sec INTEGER NOT NULL DEFAULT 0,
            UNIQUE(provider, external_id)
        );
        CREATE TABLE game_sessions (
            id INTEGER PRIMARY KEY,
            game_id INTEGER NOT NULL REFERENCES games(id) ON DELETE CASCADE,
            start_utc TEXT NOT NULL,
            end_utc TEXT,
            source TEXT NOT NULL
        );
        CREATE TABLE achievement_games (
            app_id INTEGER PRIMARY KEY,
            game_id INTEGER REFERENCES games(id),
            total INTEGER,
            unlocked INTEGER,
            last_sync_utc TEXT,
            has_achievements INTEGER NOT NULL
        );
        CREATE TABLE achievements (
            app_id INTEGER NOT NULL,
            api_name TEXT NOT NULL,
            display_name TEXT,
            description TEXT,
            icon_path TEXT,
            icon_gray_path TEXT,
            hidden INTEGER,
            unlocked INTEGER NOT NULL,
            unlock_utc TEXT,
            global_percent REAL,
            PRIMARY KEY(app_id, api_name)
        );
        CREATE TABLE tiles (
            id TEXT PRIMARY KEY,
            type TEXT NOT NULL,
            col INTEGER,
            row INTEGER,
            col_span INTEGER NOT NULL DEFAULT 1,
            row_span INTEGER NOT NULL DEFAULT 1,
            is_visible INTEGER NOT NULL DEFAULT 1,
            config_json TEXT
        );
        CREATE TABLE shortcuts (
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL,
            target TEXT NOT NULL,
            arguments TEXT,
            working_dir TEXT,
            icon_path TEXT,
            sort_order INTEGER NOT NULL
        );
        CREATE TABLE settings (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        CREATE TABLE telemetry_queue (
            id INTEGER PRIMARY KEY,
            created_utc TEXT NOT NULL,
            event TEXT NOT NULL,
            payload_json TEXT NOT NULL
        );
        CREATE TABLE http_cache (
            key TEXT PRIMARY KEY,
            etag TEXT,
            fetched_utc TEXT NOT NULL,
            expires_utc TEXT,
            body BLOB
        );
        CREATE INDEX ix_games_provider ON games(provider);
        CREATE INDEX ix_games_last_played ON games(last_played_utc DESC);
        CREATE INDEX ix_games_title ON games(title COLLATE NOCASE);
        CREATE INDEX ix_achievements_app_unlocked ON achievements(app_id, unlocked);
        """;
}
