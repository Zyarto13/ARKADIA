using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Arkadia.App.Services;

public sealed class WindowsAudioSessionService
{
    private static readonly ConcurrentDictionary<
        uint,
        (DateTimeOffset UpdatedAt, string? Name, string? ExecutablePath)> ProcessInfoCache = new();
    private static readonly ConcurrentDictionary<string, Lazy<BitmapSource?>> SessionIconCache =
        new(StringComparer.Ordinal);

    public static IReadOnlyList<AudioDeviceSnapshot> GetDevices(DataFlow dataFlow)
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultDeviceId = GetDefaultDeviceId(enumerator, dataFlow);
        var devices = enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active);
        var results = new List<AudioDeviceSnapshot>(devices.Count);

        for (var index = 0; index < devices.Count; index++)
        {
            using var device = devices[index];
            results.Add(new AudioDeviceSnapshot(
                device.ID,
                device.FriendlyName,
                dataFlow == DataFlow.Capture,
                string.Equals(device.ID, defaultDeviceId, StringComparison.OrdinalIgnoreCase),
                device.AudioEndpointVolume.MasterVolumeLevelScalar,
                device.AudioEndpointVolume.Mute));
        }

        return results
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<AudioSessionSnapshot> GetActiveSessions()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        var results = new List<AudioSessionSnapshot>();

        for (var deviceIndex = 0; deviceIndex < devices.Count; deviceIndex++)
        {
            using var device = devices[deviceIndex];
            var sessions = device.AudioSessionManager.Sessions;
            for (var sessionIndex = 0; sessionIndex < sessions.Count; sessionIndex++)
            {
                using var session = sessions[sessionIndex];
                if (session.State != AudioSessionState.AudioSessionStateActive)
                {
                    continue;
                }

                var processId = session.GetProcessID;
                var (processName, executablePath) = GetProcessInfo(processId);
                var displayName = session.DisplayName;
                var name = !string.IsNullOrWhiteSpace(displayName)
                    ? displayName
                    : !string.IsNullOrWhiteSpace(processName)
                        ? processName
                        : "System sounds";
                var sessionIdentifier = session.GetSessionIdentifier;
                var id = $"{device.ID}\u001f{sessionIdentifier}\u001f{processId.ToString(CultureInfo.InvariantCulture)}";

                results.Add(new AudioSessionSnapshot(
                    id,
                    device.ID,
                    device.FriendlyName,
                    name,
                    processName ?? string.Empty,
                    processId,
                    GetSessionIcon(id, session.IconPath, executablePath),
                    session.SimpleAudioVolume.Volume,
                    session.SimpleAudioVolume.Mute,
                    Math.Clamp(session.AudioMeterInformation.MasterPeakValue, 0f, 1f)));
            }
        }

        return results
            .OrderBy(session => session.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(session => session.ProcessId)
            .ToArray();
    }

    public static bool TrySetDeviceVolume(string deviceId, DataFlow dataFlow, float volume) =>
        TryUpdateDevice(
            deviceId,
            dataFlow,
            device => device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0f, 1f));

    public static bool TrySetDeviceMuted(string deviceId, DataFlow dataFlow, bool isMuted) =>
        TryUpdateDevice(deviceId, dataFlow, device => device.AudioEndpointVolume.Mute = isMuted);

    public static bool TrySetSessionVolume(string sessionId, float volume) =>
        TryUpdateSession(sessionId, session => session.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f));

    public static bool TrySetSessionMuted(string sessionId, bool isMuted) =>
        TryUpdateSession(sessionId, session => session.SimpleAudioVolume.Mute = isMuted);

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

    private static string? GetDefaultDeviceId(MMDeviceEnumerator enumerator, DataFlow dataFlow)
    {
        try
        {
            using var device = enumerator.GetDefaultAudioEndpoint(dataFlow, Role.Multimedia);
            return device.ID;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static bool TryUpdateDevice(string deviceId, DataFlow dataFlow, Action<MMDevice> update)
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active);
        for (var index = 0; index < devices.Count; index++)
        {
            using var device = devices[index];
            if (string.Equals(device.ID, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                update(device);
                return true;
            }
        }

        return false;
    }

    private static bool TryUpdateSession(string sessionId, Action<AudioSessionControl> update)
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        for (var deviceIndex = 0; deviceIndex < devices.Count; deviceIndex++)
        {
            using var device = devices[deviceIndex];
            var sessions = device.AudioSessionManager.Sessions;
            for (var sessionIndex = 0; sessionIndex < sessions.Count; sessionIndex++)
            {
                using var session = sessions[sessionIndex];
                var processId = session.GetProcessID;
                var identifier = $"{device.ID}\u001f{session.GetSessionIdentifier}\u001f{processId.ToString(CultureInfo.InvariantCulture)}";
                if (string.Equals(identifier, sessionId, StringComparison.Ordinal))
                {
                    update(session);
                    return true;
                }
            }
        }

        return false;
    }

    private static (string? Name, string? ExecutablePath) GetProcessInfo(uint processId)
    {
        if (processId == 0)
        {
            return (null, null);
        }

        if (ProcessInfoCache.TryGetValue(processId, out var cached)
            && DateTimeOffset.UtcNow - cached.UpdatedAt < TimeSpan.FromSeconds(1))
        {
            return (cached.Name, cached.ExecutablePath);
        }

        var (name, executablePath) = ReadProcessInfo(processId);
        ProcessInfoCache[processId] = (DateTimeOffset.UtcNow, name, executablePath);
        return (name, executablePath);
    }

    private static (string? Name, string? ExecutablePath) ReadProcessInfo(uint processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            var name = process.ProcessName;
            try
            {
                return (name, process.MainModule?.FileName);
            }
            catch (Win32Exception)
            {
                return (name, null);
            }
        }
        catch (ArgumentException)
        {
            return (null, null);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return (null, null);
        }
    }

    private static BitmapSource? GetSessionIcon(string sessionId, string? iconPath, string? executablePath)
    {
        var icon = SessionIconCache.GetOrAdd(
            sessionId,
            _ => new Lazy<BitmapSource?>(
                () => LoadSessionIcon(iconPath, executablePath),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        if (SessionIconCache.Count > 256)
        {
            SessionIconCache.Clear();
        }

        return icon;
    }

    private static BitmapSource? LoadSessionIcon(string? iconPath, string? executablePath)
    {
        foreach (var candidatePath in new[] { iconPath, executablePath })
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                continue;
            }

            var expandedPath = Environment.ExpandEnvironmentVariables(candidatePath.Trim('"'));
            var separator = expandedPath.LastIndexOf(',');
            if (separator > 0 && File.Exists(expandedPath[..separator]))
            {
                expandedPath = expandedPath[..separator];
            }

            if (!File.Exists(expandedPath))
            {
                continue;
            }

            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(expandedPath);
                if (icon is null)
                {
                    continue;
                }

                var image = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(32, 32));
                image.Freeze();
                return image;
            }
            catch (ArgumentException)
            {
            }
            catch (Win32Exception)
            {
            }
            catch (ExternalException)
            {
            }
        }

        return null;
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

        var (processName, _) = GetProcessInfo(session.GetProcessID);
        if (string.IsNullOrWhiteSpace(processName))
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

public sealed record AudioDeviceSnapshot(
    string Id,
    string Name,
    bool IsInput,
    bool IsDefault,
    float Volume,
    bool IsMuted);

public sealed record AudioSessionSnapshot(
    string Id,
    string DeviceId,
    string DeviceName,
    string Name,
    string ProcessName,
    uint ProcessId,
    BitmapSource? Icon,
    float Volume,
    bool IsMuted,
    float PeakLevel);
