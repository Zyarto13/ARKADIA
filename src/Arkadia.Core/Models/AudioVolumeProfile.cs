namespace Arkadia.Core.Models;

public sealed record AudioVolumeProfile(
    string Name,
    IReadOnlyList<AudioVolumeSetting> Sessions,
    IReadOnlyList<AudioDeviceVolumeSetting>? Devices = null);

public sealed record AudioVolumeSetting(
    string ProcessName,
    float Volume,
    bool IsMuted);

public sealed record AudioDeviceVolumeSetting(
    string DeviceId,
    bool IsInput,
    float Volume,
    bool IsMuted);
