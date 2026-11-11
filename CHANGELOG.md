# Changelog

## Unreleased

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
