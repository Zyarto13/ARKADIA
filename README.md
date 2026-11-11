# ARKADIA

ARKADIA is a Windows 11 desktop hub for games, achievements, sound, media, and shortcuts.
The application targets .NET 8 and WPF and is designed to work locally first.

## Requirements

- Windows 11 (build 22621 or later), x64
- .NET 8 SDK
- Visual Studio Code with C# Dev Kit and XAML extensions, or Visual Studio

## Build and run

```powershell
dotnet restore Arkadia.sln
dotnet build Arkadia.sln
dotnet run --project src\Arkadia.App\Arkadia.App.csproj
```

## Build the Windows installer

Install [Inno Setup 6 or later](https://jrsoftware.org/isdl.php), then run:

```powershell
.\scripts\build-installer.ps1
```

The script publishes a self-contained x64 release and creates `dist\ARKADIA-Setup-<version>-x64.exe`. The per-user installer offers a desktop shortcut (selected by default) and creates a Start Menu shortcut. Uninstall it through Windows Installed apps.

## Publish a release

The Settings page can check GitHub Releases and install updates without requiring the user to run the setup manually. This works for copies installed with the ARKADIA setup. To publish a new version:

1. Update `<Version>` in `src\Arkadia.App\Arkadia.App.csproj`.
2. Push the changes to `main`.
3. Create and push a matching version tag, for example `v1.0.4`.

The `Publish release` workflow builds the x64 installer, creates a SHA-256 checksum, and publishes both as a GitHub Release. The app verifies the checksum before running the installer and restarting. GitHub Releases must be public for update checks to work without authentication.

The current working slice includes the WPF window, labeled navigation, localized dashboard, theme and language preferences, SQLite schema initialization, allowlisted launcher URIs, startup behavior, single-instance activation, and a notification-area menu. Windows startup uses Task Scheduler with a per-user Run-key fallback. The Games page scans installed Steam libraries and Epic Games manifests locally, launches games through their registered clients, and supports title search, platform filtering, and playtime sorting. Game cards clip their artwork to rounded corners; Steam playtime is read from the most recently used local profile when available, while unavailable values are labeled rather than guessed. The home music tile can be configured to launch Spotify, Deezer, or YouTube Music. Steam achievements can be loaded for installed games with a Steam Web API key, which ARKADIA encrypts for the current Windows user using DPAPI. The Shortcuts page saves up to 30 personal HTTPS or supported app links locally. A self-contained Windows installer creates Start Menu and optional desktop shortcuts. Cloud library sync, audio/media control, telemetry, and Discord Rich Presence remain in progress.

The dashboard uses brand marks from Simple Icons where available, with sources and trademark notes in [assets/brand/README.md](assets/brand/README.md).

## Data and logs

Application data is stored under `%AppData%\Arkadia`. Logs are written to `%AppData%\Arkadia\logs`.

## License

ARKADIA is licensed under the MIT License. Third-party dependencies retain their respective licenses.
