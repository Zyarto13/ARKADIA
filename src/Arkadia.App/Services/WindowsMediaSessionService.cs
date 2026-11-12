using System.IO;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Arkadia.App.Services;

public sealed record MusicSessionSnapshot(
    string Title,
    string Artist,
    string SourceAppUserModelId,
    bool IsPlaying,
    BitmapImage? Artwork);

public enum MediaTransportCommand
{
    TogglePlayPause,
    PlayNext,
    PlayPrevious
}

public static class WindowsMediaSessionService
{
    private const ulong MaximumArtworkSizeBytes = 10 * 1024 * 1024;

    public static async Task<MusicSessionSnapshot?> GetCurrentSessionAsync(
        string musicService,
        CancellationToken cancellationToken)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var serviceIdentifier = GetServiceIdentifier(musicService);
        var session = manager.GetSessions().FirstOrDefault(candidate =>
            candidate.SourceAppUserModelId.Contains(serviceIdentifier, StringComparison.OrdinalIgnoreCase));
        if (session is null)
        {
            return null;
        }

        var properties = await session.TryGetMediaPropertiesAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var artwork = await LoadArtworkAsync(properties.Thumbnail, cancellationToken);

        return string.IsNullOrWhiteSpace(properties.Title)
            ? null
            : new MusicSessionSnapshot(
                properties.Title,
                properties.Artist,
                session.SourceAppUserModelId,
                session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                artwork);
    }

    public static async Task<bool> SendCommandAsync(
        string musicService,
        string? preferredAppUserModelId,
        MediaTransportCommand command,
        CancellationToken cancellationToken)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var sessions = manager.GetSessions();
        var serviceIdentifier = GetServiceIdentifier(musicService);
        var matchingSessions = sessions
            .Where(session => session.SourceAppUserModelId.Contains(
                serviceIdentifier,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var target = !string.IsNullOrWhiteSpace(preferredAppUserModelId)
            ? matchingSessions.FirstOrDefault(session => string.Equals(
                session.SourceAppUserModelId,
                preferredAppUserModelId,
                StringComparison.OrdinalIgnoreCase))
            : null;
        target ??= matchingSessions.FirstOrDefault();

        if (target is null)
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return command switch
        {
            MediaTransportCommand.TogglePlayPause => await target.TryTogglePlayPauseAsync(),
            MediaTransportCommand.PlayNext => await target.TrySkipNextAsync(),
            MediaTransportCommand.PlayPrevious => await target.TrySkipPreviousAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown media transport command.")
        };
    }

    private static string GetServiceIdentifier(string musicService) =>
        musicService switch
        {
            "Deezer" => "deezer",
            "Spotify" => "spotify",
            "YouTubeMusic" => "youtube",
            _ => musicService
        };

    private static async Task<BitmapImage?> LoadArtworkAsync(
        IRandomAccessStreamReference? artworkReference,
        CancellationToken cancellationToken)
    {
        if (artworkReference is null)
        {
            return null;
        }

        using var randomAccessStream = await artworkReference.OpenReadAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (randomAccessStream.Size is 0 or > MaximumArtworkSizeBytes)
        {
            return null;
        }

        using var reader = new DataReader(randomAccessStream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)randomAccessStream.Size);
        cancellationToken.ThrowIfCancellationRequested();

        var bytes = new byte[(int)randomAccessStream.Size];
        reader.ReadBytes(bytes);

        using var imageStream = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = imageStream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
