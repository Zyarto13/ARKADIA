namespace Arkadia.App.Services;

public sealed class ThemeService
{
    private static readonly Dictionary<string, string> ThemeFiles =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Blue"] = "Blue",
            ["Light"] = "Light",
            ["Black"] = "Black",
            ["Aurora"] = "Aurora",
            ["Emerald"] = "Emerald"
        };

    private string _activeTheme = "Blue";

    public void Apply(string theme)
    {
        if (!ThemeFiles.TryGetValue(theme, out var themeFile))
        {
            throw new ArgumentOutOfRangeException(nameof(theme), theme, "Unknown ARKADIA theme.");
        }

        var applicationResources = System.Windows.Application.Current.Resources;
        var activeTheme = applicationResources.MergedDictionaries
            .FirstOrDefault(dictionary =>
                dictionary.Source?.OriginalString.Replace('\\', '/')
                    .Contains("Themes/", StringComparison.OrdinalIgnoreCase) == true);

        if (activeTheme is not null)
        {
            applicationResources.MergedDictionaries.Remove(activeTheme);
        }

        applicationResources.MergedDictionaries.Insert(0, new System.Windows.ResourceDictionary
        {
            Source = new Uri(
                $"/Arkadia.App;component/Themes/{themeFile}.xaml",
                UriKind.Relative)
        });
        _activeTheme = theme;
    }

    public string ActiveTheme => _activeTheme;
}
