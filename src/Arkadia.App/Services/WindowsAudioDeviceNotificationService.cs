using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Arkadia.App.Services;

public sealed class WindowsAudioDeviceNotificationService : IDisposable
{
    private MMDeviceEnumerator? _enumerator;
    private EndpointNotificationClient? _notificationClient;

    public event EventHandler? DevicesChanged;

    public void Start()
    {
        if (_enumerator is not null)
        {
            return;
        }

        var enumerator = new MMDeviceEnumerator();
        var notificationClient = new EndpointNotificationClient(OnDeviceChanged);
        try
        {
            enumerator.RegisterEndpointNotificationCallback(notificationClient);
            _enumerator = enumerator;
            _notificationClient = notificationClient;
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            enumerator.Dispose();
            throw;
        }
    }

    public void Stop()
    {
        var enumerator = _enumerator;
        var notificationClient = _notificationClient;
        _enumerator = null;
        _notificationClient = null;

        if (enumerator is null)
        {
            return;
        }

        try
        {
            if (notificationClient is not null)
            {
                enumerator.UnregisterEndpointNotificationCallback(notificationClient);
            }
        }
        finally
        {
            enumerator.Dispose();
        }
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private void OnDeviceChanged() => DevicesChanged?.Invoke(this, EventArgs.Empty);

    private sealed class EndpointNotificationClient(Action onDeviceChanged) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => onDeviceChanged();

        public void OnDeviceAdded(string pwstrDeviceId) => onDeviceChanged();

        public void OnDeviceRemoved(string deviceId) => onDeviceChanged();

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) =>
            onDeviceChanged();

        public void OnPropertyValueChanged(string deviceId, PropertyKey key) => onDeviceChanged();
    }
}
