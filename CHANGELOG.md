# Changelog

## 1.0.25

- Restored an always-visible Settings shortcut in the title bar and added the `Ctrl+6` keyboard shortcut.

## 1.0.24

- Replaced the native title bar with a resizable custom chrome and branded window controls.
- Added automatic update checks at startup and every six hours, with a top-bar update badge and one-click installation.
- Migrated updates and new installations to Velopack while preserving the Inno Setup update path for existing users.
- Kept cached game artwork available offline and added size-limited cache cleanup and background refresh.
- Added local Minecraft Launcher, CurseForge modpack, and Rinaorc launcher discovery.
- Added an opt-in PC folder scan with game-marker detection and an explicit candidate review step.
- Prepared optional Authenticode signing in the GitHub release workflow and documented certificate setup.

## 1.0.22

- Fixed a startup crash caused by a malformed hexadecimal brush color in the home-tile template.

## 1.0.21

- Added editable custom color palettes with a color wheel, contrast warnings, and JSON import/export.
- Added opt-in crash, usage, and performance events that stay local until explicitly sent to a configured HTTPS endpoint.
- Added telemetry consent controls, event history, clearing, and anonymous installation ID reset.
- Added personal-data ZIP export and confirmed deletion of local ARKADIA data.
- Added CPU-core, GPU 3D usage, dedicated GPU memory, and a live 60-second system metrics view.

## 1.0.20

- Made the home dashboard and game library adapt to narrower window widths without forcing overflowing columns.
- Kept the complete album cover visible in the music tile instead of cropping it to fill the card.
- Added live game-art previews and localized library, achievement, and shortcut summaries to relevant home tiles.
- Improved game-card badge and playtime truncation to prevent collisions at smaller widths.

## 1.0.19

- Restored music controls to the home music tile, with playback-aware play/pause icon and tooltip.
- Filled and clipped the music tile with high-quality album artwork while keeping controls available.
- Added manual game entries, offline network status, live CPU/RAM statistics, and tray-aware refresh suspension.
- Added daily SQLite backups with seven-day rotation and automatic recovery from the latest valid backup.
- Updated GitHub Actions to current Node.js-compatible action versions.

## 1.0.18

- Added drag-and-drop tile reordering to the home layout editor.
- Added independent tile width and height controls with automatic collision-free placement.
- Persisted tile dimensions with backward-compatible settings defaults.

## 1.0.17

- Added a persistent sidebar music player with album artwork, previous/play/next controls, volume adjustment, numeric volume entry, and mute.
- Open the Deezer app directly from its home tile.
- Added home-tile visibility and ordering customization with saved settings.
- Applied gradient backgrounds and surfaces across all themes.

## 1.0.16

- Refreshed the default dark theme with a graphite, high-contrast palette inspired by the new visual direction.
- Added the deep Emerald theme to the appearance settings in English and French.
- Reworked the ARKADIA mark as a sharp, minimal inverted V and updated the Windows application icon.
- Added interaction-only hover glow, press response, and pointer-origin bubble ripples to buttons; respects Windows reduced-animation settings.

## 1.0.15

- Fixed game selection in the Steam achievements list by restoring normal keyboard focus and selection behavior.
- Made game collection filters wrap cleanly instead of colliding at narrow window widths.
- Updated the Deezer tile to show the full album artwork with high-quality scaling.

## 1.0.14

- Added a library filter for owned games that are not installed yet, so they can be launched through their platform to install.
- Restored the Steam achievement game chooser as a scrollable, virtualized list in its existing column.
- Includes the Sounds-page crash fix from 1.0.13 for users updating directly from 1.0.12.

## 1.0.13

- Fixed a crash when opening the Sounds page: audio-level progress bars now use one-way bindings for read-only meter values.

## 1.0.12

- Fixed a startup crash caused by selecting the home page before the audio meter timer was initialized.
- Reworked achievement game selection into a compact Steam-only dropdown and virtualized the achievement list for smoother scrolling.
- Cancelled outdated achievement requests when changing games so late responses cannot overwrite the current selection.

## 1.0.11

- Added a localized Windows audio mixer with input/output device volume and mute controls, active app volume and real-time meters, and saved per-app volume profiles.
- Added live audio device change notifications and per-application icons in the mixer.
- Added the project requirements document at [CAHIER_DES_CHARGES.md](CAHIER_DES_CHARGES.md).

## 1.0.10

- Fixed Steam profile detection for current `loginusers.vdf` files that use account timestamps instead of the legacy `MostRecent` marker.
- Reused the selected Steam profile for owned games, achievement requests, and local playtime cache lookup.

## 1.0.9

- Restored compact dashboard tile sizing.
- Matched music volume to the active Windows media session, including Deezer desktop sessions.
- Exposed Steam API error details for owned-game and achievement requests instead of masking them as generic privacy failures.

## 1.0.8

- Fixed Steam library merging so Steam API playtime is retained for installed games and zero-playtime games display correctly.
- Added clearer Steam achievement access errors, including the distinction between profile visibility and Game Details privacy.
- Added a redesigned music volume control with direct 0-100 numeric entry.
- Restored active Windows media-session title, artist, and artwork on the Deezer tile using the .NET 10 SDK's stable Windows SDK references.

## 1.0.7

- Added a Steam-owned-games collection alongside installed games, with public Steam library lookup using the configured Web API key.
- Improved Steam artwork fallback coverage with library-cover CDN sources and local cache images.
- Added an optional per-app volume slider for music services when Windows exposes a matching audio session; the Deezer shortcut opens liked tracks when no dedicated Deezer account integration is configured.
- Added an original ARKADIA inverted-V brand mark as the Windows application icon.
- Limited Discord to launcher behavior; call controls and participant lists are not available through the supported Discord presence interface.

## 1.0.6

- Fixed game artwork loading by switching to Steam's current CDN, matching Epic game titles against Steam's public store catalog, and using installed Epic game icons when no store image exists.
- Added Deezer tile controls for play/pause and next track using Windows media keys.
- Made theme dropdowns opaque with theme-aware text and selection contrast.

## 1.0.5

- Added the Aurora theme with a dark indigo, teal, and mint palette.
- Replaced the default checkbox and dropdown visuals with rounded, high-contrast controls.
- Added a Steam artwork fallback source and a designed game-card placeholder when artwork is unavailable.

## 1.0.4

- Fixed a startup crash on the Achievements page by making the read-only progress binding explicitly one-way.
- Added instant game-title search and platform filters to the installed library.

## 1.0.3

- Added GitHub release checks and checksum-verified in-app updates for installed copies.
- Added a GitHub Actions workflow to build and publish installer releases from version tags.

## 1.0.2

- Restyled application scrollbars with a slim, rounded, theme-aware design.
- Reworked achievements into a game library with per-game achievement progress and clearer unlock status.
- Redesigned settings into separate appearance, system, and Steam-key cards.
- Replaced shortcut tiles with horizontal rows and best-effort website favicons.

## Unreleased

- Created the .NET 8 WPF application shell and Core project.
- Added the initial navigation, localized home dashboard, theme/language preferences, and allowlisted URI launcher.
- Added SQLite WAL initialization with an idempotent initial schema and integrity check.
- Added the first Core, settings, and database tests.
- Added localized startup, start-minimized, and close-to-tray settings; added tray restore/quit actions and single-instance activation.
- Registered logon startup through Task Scheduler with a per-user Run-key fallback.
- Updated transitive SQLitePCLRaw to 2.1.12 to remediate CVE-2025-6965.
- Added offline discovery and launcher-based launching for installed Steam and Epic Games titles.
- Redesigned game-library cards with Steam cover artwork, a bottom-fade text overlay, and compact local playtime labels.
- Added Steam achievement schema loading so locked and hidden achievements are listed alongside unlocked profile progress.
- Added a self-contained Windows installer build script with Start Menu and optional desktop shortcuts.
- Added a localized shortcuts page for saving, launching, and removing up to 30 personal HTTPS or supported application links.
