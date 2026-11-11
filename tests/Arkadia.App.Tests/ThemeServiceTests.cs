using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Arkadia.App.Services;
using Xunit;

namespace Arkadia.App.Tests;

public sealed class ThemeServiceTests
{
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
                Assert.Equal(
                    Color.FromArgb(0xFF, 0x45, 0xD6, 0xA0),
                    ((SolidColorBrush)application.Resources["AccentBrush"]).Color);
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
