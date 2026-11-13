using Microsoft.Win32;

namespace Arkadia.Infrastructure.Providers;

internal static class InstalledApplicationLocator
{
    private static readonly string[] UninstallRegistryPaths =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
        @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    public static IEnumerable<string> FindExecutablePaths(string displayNameFragment, params string[] executableNames)
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var registryPath in UninstallRegistryPaths)
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
                using var uninstallKey = baseKey.OpenSubKey(registryPath);
                if (uninstallKey is null)
                {
                    continue;
                }

                foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                {
                    using var applicationKey = uninstallKey.OpenSubKey(subKeyName);
                    var displayName = applicationKey?.GetValue("DisplayName") as string;
                    if (applicationKey is null
                        || string.IsNullOrWhiteSpace(displayName)
                        || !displayName.Contains(displayNameFragment, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var iconPath = ParseExecutablePath(applicationKey.GetValue("DisplayIcon") as string);
                    if (iconPath is not null)
                    {
                        yield return iconPath;
                    }

                    var installLocation = applicationKey.GetValue("InstallLocation") as string;
                    if (string.IsNullOrWhiteSpace(installLocation))
                    {
                        continue;
                    }

                    foreach (var executableName in executableNames)
                    {
                        var path = Path.Combine(installLocation, executableName);
                        if (File.Exists(path))
                        {
                            yield return path;
                        }
                    }
                }
            }
        }
    }

    private static string? ParseExecutablePath(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
        {
            return null;
        }

        var path = displayIcon.Trim();
        if (path.StartsWith('"'))
        {
            var closingQuote = path.IndexOf('"', 1);
            path = closingQuote > 1 ? path[1..closingQuote] : string.Empty;
        }
        else
        {
            var iconIndexSeparator = path.LastIndexOf(',');
            if (iconIndexSeparator > 0 && int.TryParse(path[(iconIndexSeparator + 1)..], out _))
            {
                path = path[..iconIndexSeparator];
            }
        }

        return File.Exists(path) ? path : null;
    }
}
