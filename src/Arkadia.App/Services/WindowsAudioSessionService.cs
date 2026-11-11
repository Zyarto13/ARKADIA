using System.IO;
using NAudio.CoreAudioApi;
using System.ComponentModel;

namespace Arkadia.App.Services;

public sealed class WindowsAudioSessionService
{
    public static float? GetVolume(string musicService, string? sourceAppUserModelId = null)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var sessions = device.AudioSessionManager.Sessions;

        for (var index = 0; index < sessions.Count; index++)
        {
            using var session = sessions[index];
            if (MatchesMusicService(session, musicService, sourceAppUserModelId))
            {
                return session.SimpleAudioVolume.Volume;
            }
        }

        return null;
    }

    public static bool TrySetVolume(
        string musicService,
        float volume,
        string? sourceAppUserModelId = null)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var sessions = device.AudioSessionManager.Sessions;

        for (var index = 0; index < sessions.Count; index++)
        {
            using var session = sessions[index];
            if (MatchesMusicService(session, musicService, sourceAppUserModelId))
            {
                session.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f);
                return true;
            }
        }

        return false;
    }

    private static bool MatchesMusicService(
        AudioSessionControl session,
        string musicService,
        string? sourceAppUserModelId)
    {
        var identifiers = $"{session.DisplayName} {session.GetSessionIdentifier}";
        if (ContainsMusicServiceIdentifier(identifiers, musicService)
            || (!string.IsNullOrWhiteSpace(sourceAppUserModelId)
                && identifiers.Contains(sourceAppUserModelId, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var processId = session.GetProcessID;
        if (processId == 0)
        {
            return false;
        }

        string processName;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            processName = process.ProcessName;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            return false;
        }

        if (ContainsMusicServiceIdentifier($"{identifiers} {processName}", musicService))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(sourceAppUserModelId))
        {
            return false;
        }

        var applicationName = GetApplicationName(sourceAppUserModelId);
        return applicationName.Length >= 3
            && (string.Equals(applicationName, processName, StringComparison.OrdinalIgnoreCase)
                || applicationName.StartsWith($"{processName}_", StringComparison.OrdinalIgnoreCase)
                || applicationName.StartsWith($"{processName}.", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetApplicationName(string appUserModelId)
    {
        var applicationName = appUserModelId.Split('!', StringSplitOptions.RemoveEmptyEntries)[0];
        var separatorIndex = Math.Max(
            applicationName.LastIndexOf('\\'),
            applicationName.LastIndexOf('/'));
        if (separatorIndex >= 0)
        {
            applicationName = applicationName[(separatorIndex + 1)..];
        }

        return Path.GetFileNameWithoutExtension(applicationName);
    }

    private static bool ContainsMusicServiceIdentifier(string identifiers, string musicService) =>
        musicService switch
        {
            "Deezer" => identifiers.Contains("deezer", StringComparison.OrdinalIgnoreCase),
            "Spotify" => identifiers.Contains("spotify", StringComparison.OrdinalIgnoreCase),
            "YouTubeMusic" => identifiers.Contains("youtube", StringComparison.OrdinalIgnoreCase)
                || identifiers.Contains("music.youtube", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
}
