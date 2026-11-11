using NAudio.CoreAudioApi;
using System.ComponentModel;

namespace Arkadia.App.Services;

public sealed class WindowsAudioSessionService
{
    public static float? GetVolume(string musicService)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var sessions = device.AudioSessionManager.Sessions;

        for (var index = 0; index < sessions.Count; index++)
        {
            using var session = sessions[index];
            if (MatchesMusicService(session, musicService))
            {
                return session.SimpleAudioVolume.Volume;
            }
        }

        return null;
    }

    public static bool TrySetVolume(string musicService, float volume)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var sessions = device.AudioSessionManager.Sessions;

        for (var index = 0; index < sessions.Count; index++)
        {
            using var session = sessions[index];
            if (MatchesMusicService(session, musicService))
            {
                session.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f);
                return true;
            }
        }

        return false;
    }

    private static bool MatchesMusicService(AudioSessionControl session, string musicService)
    {
        var identifiers = $"{session.DisplayName} {session.GetSessionIdentifier}";
        if (ContainsMusicServiceIdentifier(identifiers, musicService))
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

        return ContainsMusicServiceIdentifier($"{identifiers} {processName}", musicService);
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
