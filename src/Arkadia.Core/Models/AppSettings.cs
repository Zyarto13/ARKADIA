using System.Globalization;

namespace Arkadia.Core.Models;

public sealed record AppSettings(
    string Theme,
    string Language,
    bool RunAtStartup = false,
    bool StartMinimized = false,
    bool CloseToTray = true,
    bool SingleInstance = true,
    string MusicService = "Spotify",
    IReadOnlyList<AppShortcut>? Shortcuts = null,
    IReadOnlyList<AudioVolumeProfile>? AudioProfiles = null,
    IReadOnlyList<HomeTileLayoutEntry>? HomeTiles = null,
    IReadOnlyList<ManualGameDefinition>? ManualGames = null,
    ThemePalette? CustomThemePalette = null,
    TelemetrySettings? Telemetry = null)
{
    public static AppSettings Default { get; } = new(
        "Blue",
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "fr" or "en"
            ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            : "en");
}
