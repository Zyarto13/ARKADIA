using System.IO;
using Xunit;

namespace Arkadia.App.Tests;

public sealed class XamlColorLiteralTests
{
    [Fact]
    public void XamlColorLiteralsUseSupportedWpfHexLengths()
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

        foreach (var filePath in Directory.EnumerateFiles(
                     applicationDirectory,
                     "*.xaml",
                     SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(filePath);
            for (var index = 0; index < content.Length; index++)
            {
                if (content[index] != '#')
                {
                    continue;
                }

                var digitCount = 0;
                while (index + 1 + digitCount < content.Length
                    && Uri.IsHexDigit(content[index + 1 + digitCount]))
                {
                    digitCount++;
                }

                if (digitCount > 0 && digitCount is not (3 or 4 or 6 or 8))
                {
                    Assert.Fail(
                        $"Invalid WPF color literal in {filePath} at character {index}: "
                        + content.AsSpan(index, digitCount + 1).ToString());
                }
            }
        }
    }
}
