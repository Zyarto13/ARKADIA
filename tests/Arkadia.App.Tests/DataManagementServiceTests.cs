using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Arkadia.App.Services;
using Arkadia.Core.Models;
using Arkadia.Infrastructure.Storage;
using Xunit;

namespace Arkadia.App.Tests;

public sealed class DataManagementServiceTests
{
    [Fact]
    public async Task ExportAsyncCreatesAnArchiveWithSettingsAndDatabaseSnapshot()
    {
        using var directory = new TemporaryDirectory();
        var database = new ArkadiaDatabase(Path.Combine(directory.Path, "source.db"));
        await database.InitializeAsync(CancellationToken.None);
        var service = new DataManagementService(database, Path.Combine(directory.Path, "user-data"));
        var settings = AppSettings.Default with
        {
            Telemetry = new TelemetrySettings(UsageStatistics: true, InstallationId: "anonymous-id")
        };
        var exportPath = Path.Combine(directory.Path, "export.zip");

        await service.ExportAsync(exportPath, settings, CancellationToken.None);

        using var archive = ZipFile.OpenRead(exportPath);
        Assert.Contains(archive.Entries, entry => entry.FullName == "settings.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "arkadia.db");
        Assert.Contains(archive.Entries, entry => entry.FullName == "README.txt");
        using var settingsStream = archive.GetEntry("settings.json")!.Open();
        var exportedSettings = await JsonSerializer.DeserializeAsync<AppSettings>(settingsStream);
        Assert.Equal("anonymous-id", exportedSettings?.Telemetry?.InstallationId);
        Assert.DoesNotContain(archive.Entries, entry =>
            entry.FullName.Contains("log", StringComparison.OrdinalIgnoreCase)
            || entry.FullName.Contains("key", StringComparison.OrdinalIgnoreCase));
        var snapshotPath = Path.Combine(directory.Path, "snapshot.db");
        await using (var snapshot = File.Create(snapshotPath))
        await using (var databaseEntry = archive.GetEntry("arkadia.db")!.Open())
        {
            await databaseEntry.CopyToAsync(snapshot);
        }

        Assert.True(await new ArkadiaDatabase(snapshotPath).QuickCheckAsync(CancellationToken.None));
    }

    [Fact]
    public void DeleteAllUserDataRefusesToDeleteOutsideTheApplicationDataFolder()
    {
        using var directory = new TemporaryDirectory();
        var dataDirectory = Path.Combine(directory.Path, "not-arkadia-data");
        Directory.CreateDirectory(dataDirectory);
        var markerPath = Path.Combine(dataDirectory, "keep.txt");
        File.WriteAllText(markerPath, "preserve");
        var service = new DataManagementService(
            new ArkadiaDatabase(Path.Combine(directory.Path, "database.db")),
            dataDirectory);

        Assert.Throws<InvalidOperationException>(service.DeleteAllUserData);
        Assert.True(File.Exists(markerPath));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"arkadia-data-tests-{Guid.NewGuid():N}");
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
