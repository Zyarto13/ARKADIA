using System.IO;
using Arkadia.App.Services;
using Xunit;

namespace Arkadia.App.Tests;

public sealed class SteamApiKeyStoreTests
{
    [Fact]
    public void ProtectsKeyForCurrentWindowsUserAndCanReadItBack()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"arkadia-tests-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "steam-api-key.dat");
        const string apiKey = "sample-steam-api-key";

        try
        {
            var store = new SteamApiKeyStore(path);
            store.Save(apiKey);

            Assert.Equal(apiKey, store.Load());
            Assert.DoesNotContain(
                apiKey,
                System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)));

            store.Delete();
            Assert.Null(store.Load());
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
