using CommunityToolkit.Mvvm.ComponentModel;
using Arkadia.App.Services;
using System.Windows.Media.Imaging;

namespace Arkadia.App.ViewModels;

public sealed class AudioSessionViewModel : ObservableObject
{
    private readonly Action<AudioSessionViewModel> _onVolumeChanged;
    private readonly Action<AudioSessionViewModel> _onMuteChanged;
    private int _volumePercent;
    private bool _isMuted;
    private double _peakLevel;
    private bool _isApplyingSnapshot;

    public AudioSessionViewModel(
        AudioSessionSnapshot snapshot,
        Action<AudioSessionViewModel> onVolumeChanged,
        Action<AudioSessionViewModel> onMuteChanged)
    {
        Id = snapshot.Id;
        DeviceId = snapshot.DeviceId;
        DeviceName = snapshot.DeviceName;
        Name = snapshot.Name;
        ProcessName = snapshot.ProcessName;
        ProcessId = snapshot.ProcessId;
        Icon = snapshot.Icon;
        _onVolumeChanged = onVolumeChanged;
        _onMuteChanged = onMuteChanged;
        ApplySnapshot(snapshot);
    }

    public string Id { get; }
    public string DeviceId { get; }
    public string DeviceName { get; }
    public string Name { get; }
    public string ProcessName { get; }
    public uint ProcessId { get; }
    public BitmapSource? Icon { get; }

    public double PeakLevel
    {
        get => _peakLevel;
        private set => SetProperty(ref _peakLevel, Math.Clamp(value, 0d, 1d));
    }

    public int VolumePercent
    {
        get => _volumePercent;
        set
        {
            if (SetProperty(ref _volumePercent, Math.Clamp(value, 0, 100)))
            {
                if (!_isApplyingSnapshot)
                {
                    _onVolumeChanged(this);
                }
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                if (!_isApplyingSnapshot)
                {
                    _onMuteChanged(this);
                }
            }
        }
    }

    public void ApplySnapshot(AudioSessionSnapshot snapshot)
    {
        _isApplyingSnapshot = true;
        try
        {
            VolumePercent = (int)Math.Round(snapshot.Volume * 100, MidpointRounding.AwayFromZero);
            IsMuted = snapshot.IsMuted;
            PeakLevel = snapshot.PeakLevel;
        }
        finally
        {
            _isApplyingSnapshot = false;
        }
    }
}
