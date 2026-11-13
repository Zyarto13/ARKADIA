# ARKADIA

ARKADIA is a Windows 11 desktop hub for games, achievements, sound, media, and shortcuts.
The application targets .NET 8 and WPF and is designed to work locally first.

The product requirements are maintained in [CAHIER_DES_CHARGES.md](CAHIER_DES_CHARGES.md).

## Requirements

- Windows 11 (build 22621 or later), x64
- .NET 10 SDK (the application targets .NET 8)
- Visual Studio Code with C# Dev Kit and XAML extensions, or Visual Studio

## Build and run

```powershell
dotnet restore Arkadia.sln
dotnet build Arkadia.sln
dotnet run --project src\Arkadia.App\Arkadia.App.csproj
```

## Build the Windows installer

Install [Inno Setup 6 or later](https://jrsoftware.org/isdl.php), restore the local .NET tools, then run:

```powershell
dotnet tool restore
.\scripts\build-installer.ps1
```

The script publishes a self-contained x64 release, creates the Velopack setup and update feed in `dist\velopack`, and also builds an Inno Setup compatibility installer at `dist\ARKADIA-Setup-<version>-x64.exe`. New installations should use `dist\velopack\ARKADIA-win-Setup.exe`; it installs per-user without elevation. The versioned Inno Setup installer remains available to let existing installations migrate through the app's update button.

Game artwork is cached under `%AppData%\Arkadia\artwork` with a 500 MB LRU limit. Cached artwork remains available offline; stale covers appear immediately and refresh in the background when a network is available.

## Publish a release

The app checks GitHub Releases and installs Velopack updates, including deltas when a previous package is available. The release must include `releases.win.json`, the Velopack packages, the Velopack setup, and its checksum. A versioned Inno Setup installer is also published as a transition path for existing installations; its first update launches the Velopack setup and preserves the user's `%AppData%\Arkadia` data. To publish a new version:

1. Update `<Version>` in `src\Arkadia.App\Arkadia.App.csproj`.
2. Push the changes to `main`.
3. Create and push a matching version tag, for example `v1.0.10`.

The `Publish release` workflow builds and publishes both installers, the Velopack feed/packages, and SHA-256 checksums. It uses the newest prior Velopack full package to generate deltas when one exists. GitHub Releases must be public for update checks to work without authentication.

ARKADIA checks for updates when the window opens and every six hours. The update button in the top bar shows a badge when a new release is available; click it to download and install the update. The window uses a custom, resizable title bar with minimize, maximize, and close controls.

Installers are unsigned unless a code-signing certificate is configured in the GitHub repository secrets. An unsigned installer may trigger the Windows SmartScreen “Windows protected your PC” warning; this is a publisher-reputation warning, not by itself a malware detection. See [docs/release.md](docs/release.md) for certificate setup.

The current working slice includes the WPF window, labeled navigation, localized dashboard, theme and language preferences, SQLite schema initialization, allowlisted launcher URIs, startup behavior, single-instance activation, and a notification-area menu. Windows startup uses Task Scheduler with a per-user Run-key fallback. The Games page scans installed Steam libraries, Epic manifests, Minecraft Launcher locations, CurseForge instance manifests, and common/registered Rinaorc launcher locations. CurseForge and Minecraft work locally without API keys or online authentication; CurseForge opens its launcher rather than selecting a specific modpack. The optional PC folder scan proposes executables only when game markers are present and requires explicit user selection before adding them as manual games; scans are limited to the selected folder, depth, and candidate count. The library supports title search, platform filtering, playtime sorting, and a Steam-owned-games collection for public profiles. The owned collection uses the configured Steam Web API key; private profiles may not expose their games. Steam API playtime is merged with the local cache so installed-game entries retain the reported time. Game artwork is loaded from Steam's public store CDN, with extra Steam library-cover sources and local Steam artwork caches; Epic titles are matched against the public Steam store catalog when possible, and Epic's installed executable icon is used when no store image is available. Downloaded artwork is cached locally. The home music tile can be configured to launch Spotify, Deezer, or YouTube Music. Deezer has play/pause and next-track media-key controls for the active Windows media player, displays the active Windows media session's title, artist, and artwork, and has an optional per-app volume slider with direct numeric entry when Windows exposes a matching audio session. Its favorites shortcut opens Deezer's liked tracks when no media track is active. Steam achievements can be loaded for installed games with a Steam Web API key, which ARKADIA encrypts for the current Windows user using DPAPI. Steam's “Game Details” privacy setting is separate from profile visibility and must allow stats access for achievements. The Shortcuts page saves up to 30 personal HTTPS or supported app links locally. A self-contained Windows installer creates Start Menu and optional desktop shortcuts. Cloud library sync, telemetry, and Discord Rich Presence remain in progress.

The Sound page lists active playback and recording devices, their default status, master volume and mute controls, and active app audio sessions with per-app volume, mute, and live level meters while the page is visible. It can save and restore per-app volume profiles locally; changing the Windows default device is not supported.

The dashboard uses brand marks from Simple Icons where available, with sources and trademark notes in [assets/brand/README.md](assets/brand/README.md).

## Data and logs

Application data is stored under `%AppData%\Arkadia`. Logs are written to `%AppData%\Arkadia\logs`.

## License

ARKADIA is licensed under the MIT License. Third-party dependencies retain their respective licenses.
