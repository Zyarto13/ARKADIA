using System.Globalization;
using System.Resources;
using Arkadia.App.Localization;
using Xunit;

namespace Arkadia.App.Tests;

public sealed class LocalizationResourceTests
{
    [Fact]
    public void EnglishAndFrenchResourcesHaveMatchingKeys()
    {
        var resources = new ResourceManager(
            "Arkadia.App.Strings",
            typeof(LocalizationManager).Assembly);
        using var english = resources.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, true);
        using var french = resources.GetResourceSet(CultureInfo.GetCultureInfo("fr"), true, false);

        Assert.NotNull(english);
        Assert.NotNull(french);
        var englishKeys = GetKeys(english!);
        var frenchKeys = GetKeys(french!);

        Assert.True(
            englishKeys.SetEquals(frenchKeys),
            $"English-only keys: {string.Join(", ", englishKeys.Except(frenchKeys))}; " +
            $"French-only keys: {string.Join(", ", frenchKeys.Except(englishKeys))}");
    }

    private static HashSet<string> GetKeys(ResourceSet resources)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var enumerator = resources.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (enumerator.Key is string key)
            {
                keys.Add(key);
            }
        }

        return keys;
    }
}
