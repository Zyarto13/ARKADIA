using Arkadia.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Arkadia.App.ViewModels;

public sealed class AudioDeviceViewModel : ObservableObject
{
    private readonly Action<AudioDeviceViewModel> _onVolumeChanged;
    private readonly Action<AudioDeviceViewModel> _onMuteChanged;
    private int _volumePercent;
    private bool _isMuted;
    private bool _isApplyingSnapshot;

    public AudioDeviceViewModel(
        AudioDeviceSnapshot snapshot,
        Action<AudioDeviceViewModel> onVolumeChanged,
        Action<AudioDeviceViewModel> onMuteChanged)
    {
        Id = snapshot.Id;
        Name = snapshot.Name;
        IsInput = snapshot.IsInput;
        _onVolumeChanged = onVolumeChanged;
        _onMuteChanged = onMuteChanged;
        ApplySnapshot(snapshot);
    }

    public string Id { get; }
    public string Name { get; }
    public bool IsInput { get; }
    public bool IsDefault { get; private set; }

    public int VolumePercent
    {
        get => _volumePercent;
        set
        {
            if (SetProperty(ref _volumePercent, Math.Clamp(value, 0, 100)) && !_isApplyingSnapshot)
            {
                _onVolumeChanged(this);
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value) && !_isApplyingSnapshot)
            {
                _onMuteChanged(this);
            }
        }
    }

    public void ApplySnapshot(AudioDeviceSnapshot snapshot)
    {
        _isApplyingSnapshot = true;
        try
        {
            IsDefault = snapshot.IsDefault;
            OnPropertyChanged(nameof(IsDefault));
            VolumePercent = (int)Math.Round(snapshot.Volume * 100, MidpointRounding.AwayFromZero);
            IsMuted = snapshot.IsMuted;
        }
        finally
        {
            _isApplyingSnapshot = false;
        }
    }
}
