using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Arkadia.Core.Models;
using Arkadia.App.Services;
using Xunit;

namespace Arkadia.App.Tests;

public sealed class ThemeServiceTests
{
    private static readonly string[] ThemeNames = ["Blue", "Light", "Black", "Aurora", "Emerald"];

    [Fact]
    public void EmeraldThemeLoadsItsPalette()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = new Application();
                var themeService = new ThemeService();

                themeService.Apply("Emerald");

                Assert.Equal("Emerald", themeService.ActiveTheme);
                Assert.Equal(Color.FromArgb(0xFF, 0x45, 0xD6, 0xA0), (Color)application.Resources["AccentColor"]);
                var accentBrush = Assert.IsType<LinearGradientBrush>(application.Resources["AccentBrush"]);
                Assert.Equal(Color.FromArgb(0xFF, 0x45, 0xD6, 0xA0), accentBrush.GradientStops[0].Color);
                Assert.Equal(2, accentBrush.GradientStops.Count);
                foreach (var theme in ThemeNames)
                {
                    themeService.Apply(theme);

                    Assert.IsType<LinearGradientBrush>(application.Resources["WindowBackgroundBrush"]);
                    Assert.IsType<LinearGradientBrush>(application.Resources["CardBackgroundBrush"]);
                    Assert.IsType<LinearGradientBrush>(application.Resources["CardHoverBrush"]);
                    Assert.IsType<LinearGradientBrush>(application.Resources["AccentBrush"]);
                }

                ThemeService.ApplyCustomPalette(
                    new ThemePalette("#00D2FF", "#3A86FF", "#12141D", "#1E202D", "#F2F5FA"));
                Assert.IsType<LinearGradientBrush>(application.Resources["WindowBackgroundBrush"]);
                Assert.IsType<LinearGradientBrush>(application.Resources["CardBackgroundBrush"]);
                Assert.IsType<LinearGradientBrush>(application.Resources["AccentBrush"]);
                application.Shutdown();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new AggregateException(failure);
        }
    }

    [Fact]
    public void CustomPaletteContrastUsesTheWorstTextContrastAcrossBackgroundAndSurface()
    {
        var highContrast = new ThemePalette("#00D2FF", "#3A86FF", "#000000", "#000000", "#FFFFFF");
        var lowContrast = highContrast with { Surface = "#FFFFFF" };

        Assert.Equal(21, ThemeService.GetTextContrastRatio(highContrast), precision: 10);
        Assert.Equal(1, ThemeService.GetTextContrastRatio(lowContrast), precision: 10);
        Assert.False(ThemeService.TryParseHexColor("#12FG00", out _));
    }
}
