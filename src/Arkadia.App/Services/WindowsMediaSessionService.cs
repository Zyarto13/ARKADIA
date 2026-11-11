using System.IO;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Arkadia.App.Services;

public sealed record MusicSessionSnapshot(string Title, string Artist, BitmapImage? Artwork);

public static class WindowsMediaSessionService
{
    private const ulong MaximumArtworkSizeBytes = 10 * 1024 * 1024;

    public static async Task<MusicSessionSnapshot?> GetCurrentSessionAsync(
        CancellationToken cancellationToken)
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var session = manager.GetCurrentSession();
        if (session is null)
        {
            return null;
        }

        var properties = await session.TryGetMediaPropertiesAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var artwork = await LoadArtworkAsync(properties.Thumbnail, cancellationToken);

        return string.IsNullOrWhiteSpace(properties.Title)
            ? null
            : new MusicSessionSnapshot(properties.Title, properties.Artist, artwork);
    }

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
