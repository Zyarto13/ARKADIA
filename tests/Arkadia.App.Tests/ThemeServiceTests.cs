using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
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
}
