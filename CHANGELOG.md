# Changelog

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
