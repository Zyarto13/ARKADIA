using System.IO;
using System.Globalization;
using Arkadia.Infrastructure.Storage;

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

            Assert.Equal(1L, Convert.ToInt64(schemaVersion, CultureInfo.InvariantCulture));
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
}
