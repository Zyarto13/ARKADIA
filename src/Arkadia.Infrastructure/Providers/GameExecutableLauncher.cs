using System.ComponentModel;
using System.Diagnostics;
using Arkadia.Core.Models;

namespace Arkadia.Infrastructure.Providers;

internal static class GameExecutableLauncher
{
    public static LaunchResult Start(string executablePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath)!,
                UseShellExecute = true
            });
            return new LaunchResult(true);
        }
        catch (Win32Exception exception)
        {
            return new LaunchResult(false, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return new LaunchResult(false, exception.Message);
        }
    }
}
