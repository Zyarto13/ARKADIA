using System.Globalization;
using System.Windows.Media;
using Arkadia.Core.Models;
using Color = System.Windows.Media.Color;

namespace Arkadia.App.Services;

public sealed class ThemeService
{
    private static readonly string[] CustomResourceKeys =
    [
        "AccentColor",
        "AccentSecondaryColor",
        "AccentBrush",
        "WindowBackgroundBrush",
        "CardBackgroundBrush",
        "ComboBoxPopupBackgroundBrush",
        "CardHoverBrush",
        "CardBorderBrush",
        "TextPrimaryBrush",
        "TextSecondaryBrush"
    ];

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
        ClearCustomPalette();
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

    public static void ApplyCustomPalette(ThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        if (!TryParseHexColor(palette.Accent, out var accent)
            || !TryParseHexColor(palette.AccentSecondary, out var accentSecondary)
            || !TryParseHexColor(palette.Background, out var background)
            || !TryParseHexColor(palette.Surface, out var surface)
            || !TryParseHexColor(palette.Text, out var text))
        {
            throw new ArgumentException("All custom theme colors must use #RRGGBB hexadecimal values.", nameof(palette));
        }

        var resources = System.Windows.Application.Current.Resources;
        resources["AccentColor"] = accent;
        resources["AccentSecondaryColor"] = accentSecondary;
        resources["AccentBrush"] = CreateAccentBrush(accent, accentSecondary);
        resources["WindowBackgroundBrush"] = CreateGradientBrush(
            background,
            Blend(background, surface, 0.22));
        resources["CardBackgroundBrush"] = CreateGradientBrush(
            surface,
            Blend(surface, background, 0.28));
        resources["ComboBoxPopupBackgroundBrush"] = CreateGradientBrush(
            Blend(surface, background, 0.3),
            Blend(surface, background, 0.12));
        resources["CardHoverBrush"] = CreateGradientBrush(
            Blend(surface, accent, 0.16),
            Blend(surface, accentSecondary, 0.16));
        resources["CardBorderBrush"] = CreateBrush(Blend(surface, text, 0.24));
        resources["TextPrimaryBrush"] = CreateBrush(text);
        resources["TextSecondaryBrush"] = CreateBrush(Blend(text, background, 0.42));
    }

    public static void ClearCustomPalette()
    {
        if (System.Windows.Application.Current is not { } application)
        {
            return;
        }

        foreach (var key in CustomResourceKeys)
        {
            application.Resources.Remove(key);
        }
    }

    public static bool TryParseHexColor(string? value, out Color color)
    {
        color = default;
        if (value is null)
        {
            return false;
        }

        var hex = value.Trim();
        if (hex.Length != 7
            || hex[0] != '#'
            || !uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        color = Color.FromRgb(
            (byte)(rgb >> 16),
            (byte)(rgb >> 8),
            (byte)rgb);
        return true;
    }

    public static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static double GetTextContrastRatio(ThemePalette palette)
    {
        if (!TryParseHexColor(palette.Text, out var text)
            || !TryParseHexColor(palette.Background, out var background)
            || !TryParseHexColor(palette.Surface, out var surface))
        {
            return 0;
        }

        var textLuminance = GetRelativeLuminance(text);
        static double Contrast(double first, double second) =>
            (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);

        return Math.Min(
            Contrast(textLuminance, GetRelativeLuminance(background)),
            Contrast(textLuminance, GetRelativeLuminance(surface)));
    }

    private static LinearGradientBrush CreateAccentBrush(Color start, Color end)
        => CreateGradientBrush(start, end);

    private static LinearGradientBrush CreateGradientBrush(Color start, Color end)
    {
        var brush = new LinearGradientBrush(start, end, 45);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Color Blend(Color first, Color second, double amount) =>
        Color.FromRgb(
            (byte)Math.Round(first.R + (second.R - first.R) * amount),
            (byte)Math.Round(first.G + (second.G - first.G) * amount),
            (byte)Math.Round(first.B + (second.B - first.B) * amount));

    private static double GetRelativeLuminance(Color color)
    {
        static double Linearize(byte value)
        {
            var channel = value / 255d;
            return channel <= 0.04045
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linearize(color.R)
            + 0.7152 * Linearize(color.G)
            + 0.0722 * Linearize(color.B);
    }
}
