using System.IO;
using System.Xml.Linq;
using Xunit;

namespace Arkadia.App.Tests;

public sealed class WindowChromeConfigurationTests
{
    [Fact]
    public void MainWindowUsesCustomResizableChromeAndProvidesWindowActions()
    {
        var applicationDirectory = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "Arkadia.App"));
        var document = XDocument.Load(Path.Combine(applicationDirectory, "MainWindow.xaml"));
        var window = document.Root;
        Assert.NotNull(window);

        XNamespace shell = "clr-namespace:System.Windows.Shell;assembly=PresentationFramework";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var chrome = window!.Element(shell + "WindowChrome.WindowChrome")?.Element(shell + "WindowChrome");

        Assert.Equal("None", (string?)window.Attribute("WindowStyle"));
        Assert.Equal("CanResize", (string?)window.Attribute("ResizeMode"));
        Assert.Equal("64", (string?)chrome?.Attribute("CaptionHeight"));
        Assert.Equal("6", (string?)chrome?.Attribute("ResizeBorderThickness"));

        var namedElements = window.Descendants()
            .Select(element => (string?)element.Attribute(xaml + "Name"))
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("MinimizeWindowButton", namedElements);
        Assert.Contains("MaximizeWindowButton", namedElements);
        Assert.Contains("CloseWindowButton", namedElements);
        Assert.Contains("SettingsQuickAccessButton", namedElements);
        Assert.Contains("UpdateActionButton", namedElements);

        var settingsButton = window.Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Name") == "SettingsQuickAccessButton");
        Assert.Equal("Settings", (string?)settingsButton.Attribute("CommandParameter"));
    }
}
