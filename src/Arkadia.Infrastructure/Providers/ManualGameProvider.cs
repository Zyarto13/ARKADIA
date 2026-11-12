using System.ComponentModel;
using System.Diagnostics;
using Arkadia.Core.Models;
using Arkadia.Core.Providers;
using Arkadia.Core.Services;

namespace Arkadia.Infrastructure.Providers;

public sealed class ManualGameProvider(ISettingsService settingsService) : IGameProvider
{
    public string Id => "manual";

    public Task<ProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ProviderStatus(true, "Manual games", null));
    }

    public async Task<IReadOnlyList<GameEntry>> ScanInstalledAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        var games = new List<GameEntry>();
        foreach (var manualGame in settings.ManualGames ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (manualGame.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(manualGame.Title)
                || !TryNormalizeExecutablePath(manualGame.ExecutablePath, out var executablePath))
            {
                continue;
            }

            games.Add(new GameEntry(
                Id,
                manualGame.Id.ToString("N"),
                manualGame.Title,
                executablePath,
                File.Exists(executablePath),
                false));
        }

        return games;
    }

    public async Task<LaunchResult> LaunchAsync(GameEntry game, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (game.ProviderId != Id || !Guid.TryParseExact(game.ExternalId, "N", out var gameId))
        {
            return new LaunchResult(false, "The manual game entry is invalid.");
        }

        var settings = await settingsService.LoadAsync(cancellationToken);
        var definition = settings.ManualGames?.FirstOrDefault(item => item.Id == gameId);
        if (definition is null || !TryNormalizeExecutablePath(definition.ExecutablePath, out var executablePath))
        {
            return new LaunchResult(false, "The manual game executable could not be found.");
        }

        if (!File.Exists(executablePath))
        {
            return new LaunchResult(false, "The manual game executable no longer exists.");
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath)!,
                UseShellExecute = true
            });
            return new LaunchResult(true);
        }
        catch (Win32Exception exception)
        {
            return new LaunchResult(false, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return new LaunchResult(false, exception.Message);
        }
    }

    private static bool TryNormalizeExecutablePath(string? path, out string executablePath)
    {
        executablePath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var normalizedPath = Path.GetFullPath(path);
            var extension = Path.GetExtension(normalizedPath);
            if (!string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            executablePath = normalizedPath;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
