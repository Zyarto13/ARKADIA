using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Arkadia.Core.Models;
using Arkadia.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace Arkadia.App.Services;

public sealed class DataManagementService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly ArkadiaDatabase _database;
    private readonly string _dataDirectory;

    public DataManagementService(ArkadiaDatabase database, string? dataDirectory = null)
    {
        _database = database;
        _dataDirectory = Path.GetFullPath(dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Arkadia"));
    }

    public async Task ExportAsync(
        string destinationPath,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var exportPath = Path.GetFullPath(destinationPath);
        var exportDirectory = Path.GetDirectoryName(exportPath)
            ?? throw new InvalidOperationException("The export file has no parent directory.");
        Directory.CreateDirectory(exportDirectory);

        var temporaryArchive = $"{exportPath}.{Guid.NewGuid():N}.tmp";
        var temporaryDatabase = Path.Combine(
            Path.GetTempPath(),
            $"arkadia-export-{Guid.NewGuid():N}.db");
        try
        {
            await CreateDatabaseSnapshotAsync(temporaryDatabase, cancellationToken);
            await using (var file = new FileStream(
                             temporaryArchive,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             bufferSize: 64 * 1024,
                             useAsync: true))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
            {
                var settingsEntry = archive.CreateEntry("settings.json", CompressionLevel.Optimal);
                await using (var settingsStream = settingsEntry.Open())
                {
                    await JsonSerializer.SerializeAsync(
                        settingsStream,
                        settings,
                        JsonOptions,
                        cancellationToken);
                }

                var databaseEntry = archive.CreateEntry("arkadia.db", CompressionLevel.Optimal);
                await using (var databaseEntryStream = databaseEntry.Open())
                await using (var databaseFile = new FileStream(
                                 temporaryDatabase,
                                 FileMode.Open,
                                 FileAccess.Read,
                                 FileShare.Read,
                                 bufferSize: 64 * 1024,
                                 useAsync: true))
                {
                    await databaseFile.CopyToAsync(databaseEntryStream, cancellationToken);
                }

                var readmeEntry = archive.CreateEntry("README.txt", CompressionLevel.Optimal);
                await using var readmeStream = readmeEntry.Open();
                var readme = Encoding.UTF8.GetBytes(
                    "ARKADIA personal data export.\r\n"
                    + "Contains the local database snapshot and application settings.\r\n"
                    + "Encrypted API keys, logs, and cached artwork are intentionally excluded.\r\n");
                await readmeStream.WriteAsync(readme, cancellationToken);
            }

            File.Move(temporaryArchive, exportPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryArchive))
            {
                File.Delete(temporaryArchive);
            }

            if (File.Exists(temporaryDatabase))
            {
                File.Delete(temporaryDatabase);
            }
        }
    }

    public void DeleteAllUserData()
    {
        var applicationDataDirectory = Path.GetFullPath(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        var expectedDirectory = Path.Combine(applicationDataDirectory, "Arkadia");
        if (!string.Equals(
                Path.TrimEndingDirectorySeparator(_dataDirectory),
                Path.TrimEndingDirectorySeparator(expectedDirectory),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to delete data outside the ARKADIA application folder.");
        }

        if (Directory.Exists(_dataDirectory))
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private async Task CreateDatabaseSnapshotAsync(
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = await _database.OpenConnectionAsync(cancellationToken);
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        await destination.OpenAsync(cancellationToken);
        await Task.Run(() => source.BackupDatabase(destination), cancellationToken);
    }
}
