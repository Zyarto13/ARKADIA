using System.IO;
using System.Text.Json;
using Arkadia.Core.Models;
using Arkadia.Core.Services;

namespace Arkadia.App.Services;

public sealed class JsonSettingsService : ISettingsService, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public JsonSettingsService()
        : this(null)
    {
    }

    public JsonSettingsService(string? settingsPath)
    {
        _settingsPath = Path.GetFullPath(settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Arkadia",
            "settings.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_settingsPath))
        {
            return AppSettings.Default;
        }

        await using var stream = new FileStream(
            _settingsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        return await JsonSerializer.DeserializeAsync<AppSettings>(
                   stream,
                   SerializerOptions,
                   cancellationToken)
               ?? throw new InvalidDataException("The ARKADIA settings file contains no settings.");
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await _saveLock.WaitAsync(cancellationToken);
        var temporaryPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_settingsPath))
            {
                File.Replace(temporaryPath, _settingsPath, null);
            }
            else
            {
                File.Move(temporaryPath, _settingsPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            _saveLock.Release();
        }
    }

    public void Dispose() => _saveLock.Dispose();
}
