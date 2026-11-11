using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Arkadia.Core.Models;
using Serilog;

namespace Arkadia.App.Services;

public sealed class GameArtworkService
{
    private const int MaximumArtworkBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private static readonly SemaphoreSlim RequestLimit = new(4);
    private readonly ConcurrentDictionary<string, Lazy<Task<BitmapImage?>>> _artworkCache = new(StringComparer.OrdinalIgnoreCase);

    public GameArtworkService(HttpClient httpClient, string? cacheDirectory = null)
    {
        _httpClient = httpClient;
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Arkadia",
            "artwork");
    }

    public Task<BitmapImage?> LoadArtworkAsync(GameEntry game)
    {
        var key = $"{game.ProviderId}:{game.ExternalId}:{game.Title}:{game.ArtworkFilePath}";
        var artworkTask = _artworkCache.GetOrAdd(
            key,
            _ => new Lazy<Task<BitmapImage?>>(
                () => LoadArtworkUncachedAsync(game),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return LoadArtworkAndAllowRetryAsync(key, artworkTask);
    }

    private async Task<BitmapImage?> LoadArtworkAndAllowRetryAsync(
        string key,
        Lazy<Task<BitmapImage?>> artworkTask)
    {
        var task = artworkTask.Value;
        try
        {
            return await task;
        }
        finally
        {
            if (task.IsFaulted || task.IsCanceled || (task.IsCompletedSuccessfully && task.Result is null))
            {
                ((ICollection<KeyValuePair<string, Lazy<Task<BitmapImage?>>>>)_artworkCache)
                    .Remove(new KeyValuePair<string, Lazy<Task<BitmapImage?>>>(key, artworkTask));
            }
        }
    }

    private async Task<BitmapImage?> LoadArtworkUncachedAsync(GameEntry game)
    {
        var cachePath = GetCachePath(game);
        var cachedArtwork = TryLoadCachedArtwork(cachePath);
        if (cachedArtwork is not null)
        {
            return cachedArtwork;
        }

        await RequestLimit.WaitAsync();
        try
        {
            using var timeout = new CancellationTokenSource(RequestTimeout);
            IReadOnlyList<Uri> artworkUris = game.ArtworkUris;
            if (artworkUris.Count == 0)
            {
                artworkUris = await FindSteamArtworkUrisAsync(game.Title, timeout.Token);
            }

            foreach (var artworkUri in artworkUris)
            {
                try
                {
                    var bytes = await DownloadArtworkAsync(artworkUri, timeout.Token);
                    if (bytes is null)
                    {
                        continue;
                    }

                    var image = DecodeArtwork(bytes);
                    TryCacheArtwork(cachePath, bytes);
                    return image;
                }
                catch (HttpRequestException exception)
                {
                    Log.Debug(exception, "Could not load game artwork from {ArtworkUri}.", artworkUri);
                }
                catch (IOException exception)
                {
                    Log.Debug(exception, "Could not read game artwork from {ArtworkUri}.", artworkUri);
                }
                catch (System.IO.FileFormatException exception)
                {
                    Log.Debug(exception, "The image at {ArtworkUri} was not valid artwork.", artworkUri);
                }
            }

            var executableIcon = TryLoadExecutableIcon(game.ArtworkFilePath);
            if (executableIcon is not null)
            {
                TryCacheArtwork(cachePath, executableIcon.Value.Bytes);
                return executableIcon.Value.Image;
            }
        }
        catch (OperationCanceledException)
        {
            Log.Debug("Timed out while loading artwork for {GameTitle}.", game.Title);
        }
        finally
        {
            RequestLimit.Release();
        }

        return null;
    }

    private async Task<IReadOnlyList<Uri>> FindSteamArtworkUrisAsync(
        string title,
        CancellationToken cancellationToken)
    {
        var searchUri = new Uri(
            "https://store.steampowered.com/api/storesearch/?term="
            + Uri.EscapeDataString(title)
            + "&l=en&cc=us");

        try
        {
            var responseBody = await DownloadTextAsync(searchUri, cancellationToken);
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var matches = items.EnumerateArray()
                .Select(item => new
                {
                    Name = ReadString(item, "name"),
                    Id = ReadString(item, "id"),
                    TinyImage = ReadString(item, "tiny_image")
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Name)
                    && uint.TryParse(item.Id, out _))
                .Select(item => new
                {
                    item.Id,
                    item.TinyImage,
                    Similarity = GetTitleSimilarity(title, item.Name!)
                })
                .Where(item => item.Similarity >= 0.72)
                .OrderByDescending(item => item.Similarity)
                .ToArray();

            if (matches.Length == 0)
            {
                return [];
            }

            var bestMatch = matches[0];
            var appId = bestMatch.Id;
            var artworkUris = new List<Uri>
            {
                new($"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg"),
                new($"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/capsule_616x353.jpg")
            };

            if (Uri.TryCreate(bestMatch.TinyImage, UriKind.Absolute, out var tinyImageUri)
                && tinyImageUri.Scheme == Uri.UriSchemeHttps
                && tinyImageUri.Host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase))
            {
                artworkUris.Add(tinyImageUri);
            }

            return artworkUris;
        }
        catch (HttpRequestException exception)
        {
            Log.Debug(exception, "Could not search Steam for artwork for {GameTitle}.", title);
        }
        catch (JsonException exception)
        {
            Log.Debug(exception, "Steam returned invalid artwork search results for {GameTitle}.", title);
        }

        return [];
    }

    private async Task<string> DownloadTextAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(uri);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private async Task<byte[]?> DownloadArtworkAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(uri);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumArtworkBytes)
        {
            Log.Debug("Skipped oversized artwork from {ArtworkUri}.", uri);
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return bytes.Length <= MaximumArtworkBytes ? bytes : null;
    }

    private static HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("ARKADIA/1.0");
        return request;
    }

    private static BitmapImage DecodeArtwork(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static (BitmapImage Image, byte[] Bytes)? TryLoadExecutableIcon(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return null;
        }

        try
        {
            using var icon = Icon.ExtractAssociatedIcon(executablePath);
            if (icon is null)
            {
                return null;
            }

            using var iconBitmap = icon.ToBitmap();
            using var bitmap = new Bitmap(920, 430);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            using var background = new LinearGradientBrush(
                bounds,
                Color.FromArgb(255, 24, 30, 58),
                Color.FromArgb(255, 14, 65, 68),
                40);
            graphics.FillRectangle(background, bounds);
            using var ringPen = new Pen(Color.FromArgb(65, 89, 225, 210), 3);
            graphics.DrawEllipse(ringPen, 590, -150, 440, 440);
            graphics.DrawEllipse(ringPen, 640, -100, 340, 340);
            graphics.DrawImage(iconBitmap, new Rectangle(365, 85, 190, 190));
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            var bytes = stream.ToArray();
            return (DecodeArtwork(bytes), bytes);
        }
        catch (ArgumentException exception)
        {
            Log.Debug(exception, "Could not extract an icon from the Epic game executable.");
        }
        catch (ExternalException exception)
        {
            Log.Debug(exception, "Could not read the Epic game executable icon.");
        }
        catch (System.IO.FileFormatException exception)
        {
            Log.Debug(exception, "The executable icon was not a supported image format.");
        }

        return null;
    }

    private static BitmapImage? TryLoadCachedArtwork(string cachePath)
    {
        if (!File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            return DecodeArtwork(File.ReadAllBytes(cachePath));
        }
        catch (IOException exception)
        {
            Log.Debug(exception, "Could not read cached game artwork.");
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Warning(exception, "Access was denied while reading cached game artwork.");
        }
        catch (System.IO.FileFormatException exception)
        {
            Log.Debug(exception, "Removing invalid cached game artwork.");
            try
            {
                File.Delete(cachePath);
            }
            catch (IOException deleteException)
            {
                Log.Debug(deleteException, "Could not remove invalid cached game artwork.");
            }
            catch (UnauthorizedAccessException deleteException)
            {
                Log.Debug(deleteException, "Access was denied while removing invalid cached game artwork.");
            }
        }

        return null;
    }

    private void TryCacheArtwork(string cachePath, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            File.WriteAllBytes(cachePath, bytes);
        }
        catch (IOException exception)
        {
            Log.Warning(exception, "Could not cache game artwork.");
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Warning(exception, "Access was denied while caching game artwork.");
        }
    }

    private string GetCachePath(GameEntry game)
    {
        var key = Encoding.UTF8.GetBytes($"{game.ProviderId}:{game.ExternalId}:{game.Title}:{game.ArtworkFilePath}");
        var fileName = Convert.ToHexString(SHA256.HashData(key));
        return Path.Combine(_cacheDirectory, $"{fileName}.img");
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private static double GetTitleSimilarity(string firstTitle, string secondTitle)
    {
        var first = NormalizeTitle(firstTitle);
        var second = NormalizeTitle(secondTitle);
        if (first.Length == 0 || second.Length == 0)
        {
            return 0;
        }

        if (string.Equals(first, second, StringComparison.Ordinal))
        {
            return 1;
        }

        if (first.StartsWith(second, StringComparison.Ordinal)
            || second.StartsWith(first, StringComparison.Ordinal))
        {
            return (double)Math.Min(first.Length, second.Length) / Math.Max(first.Length, second.Length);
        }

        var previousRow = new int[second.Length + 1];
        var currentRow = new int[second.Length + 1];
        for (var column = 0; column <= second.Length; column++)
        {
            previousRow[column] = column;
        }

        for (var row = 1; row <= first.Length; row++)
        {
            currentRow[0] = row;
            for (var column = 1; column <= second.Length; column++)
            {
                var substitutionCost = first[row - 1] == second[column - 1] ? 0 : 1;
                currentRow[column] = Math.Min(
                    Math.Min(currentRow[column - 1] + 1, previousRow[column] + 1),
                    previousRow[column - 1] + substitutionCost);
            }

            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return 1d - (double)previousRow[second.Length] / Math.Max(first.Length, second.Length);
    }

    private static string NormalizeTitle(string title)
    {
        var normalized = title.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark
                && char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}
