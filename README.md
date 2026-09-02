# Earth Wallpaper

Earth Wallpaper is a lightweight desktop widget for Windows and macOS that changes the wallpaper and presents a short educational story about the current place. The overlay can show a location, title and short description, or collapse to minimal controls.

> The project is currently in beta. Windows installer/portable packages and an Apple Silicon macOS disk image are produced through GitHub Releases.

## Features

- two content collections: **All places** and the aesthetics-focused **Visual highlights**;
- independently configurable widget layout, position, scale and opacity;
- optional automatic wallpaper rotation;
- detailed place information in a separate window;
- manual, notification-only and automatic content update modes;
- dynamic download-size calculation before confirmation;
- resumable HTTP Range downloads with size and SHA-256 validation;
- atomic catalog activation and recovery through the previous working release;
- shared content-addressed cache between collections;
- tray controls and local structured logs;
- optional Windows startup registration (macOS login-item support is planned after beta validation);
- daily GitHub Releases checks for application updates;
- per-user installer, clean uninstall and settings/content preservation across upgrades.

## Architecture

```text
Cloudflare R2
  ├─ manifest.json / manifest-aesthetic.json
  ├─ versioned catalogs
  └─ content-addressed image assets
             ↓
Earth Wallpaper (Avalonia / .NET 10)

Local workbook + source images
             ↓
Wallpaper Publisher
             ↓
Cloudflare-ready content release
```

Cloudflare R2 carries wallpaper content. GitHub carries source code and application installers through GitHub Releases. The source photographs, master workbook, generated R2 payload and deployment credentials are intentionally excluded from this repository.

## Installation

Download the package for your operating system from [GitHub Releases](https://github.com/NikitaSozonoff/earth-wallpaper/releases):

- Windows: `EarthWallpaper-Setup-<version>.exe`;
- macOS on Apple Silicon: `EarthWallpaper-macOS-arm64-<version>.dmg`.

The Windows installation is per-user and does not require administrator privileges. Close a running Earth Wallpaper instance from its tray menu before installing an update. The portable ZIP is provided for Windows testing without installation.

The macOS beta is built for Apple Silicon and is ad-hoc signed, but it is not notarized by Apple. After dragging **Earth Wallpaper.app** from the DMG to **Applications**, try **Control-click → Open** and then **System Settings → Privacy & Security → Open Anyway**. If macOS still does nothing, run this one-time command in Terminal:

```bash
xattr -dr com.apple.quarantine "/Applications/Earth Wallpaper.app"
open "/Applications/Earth Wallpaper.app"
```

This removes the internet-download quarantine attribute from Earth Wallpaper only; it does not disable Gatekeeper globally. These instructions are also included inside the DMG.

Release packages are self-contained: users do not need to install the .NET SDK or Runtime. The SDK listed below is required only for building the project from source.

Application updates are checked against public GitHub Releases once per day and can also be checked manually in widget settings or from the tray menu. The update window offers explicit Windows and macOS package buttons; installation remains a visible user-confirmed action.

Downloaded wallpapers and settings live in the operating system's local application-data directory under `EarthWallpaper` and are preserved when the application is upgraded. On Windows this is `%LOCALAPPDATA%\EarthWallpaper`. The first packaged launch migrates the previous `EarthWallpaperPrototype` directory without downloading the collection again.

## Repository layout

- `app/WallpaperWidget/` — Avalonia desktop application.
- `publisher/WallpaperPublisher/` — catalog validator and deterministic content builder.
- `tests/ContentUpdateSmoke/` — public-catalog and local recovery smoke tests.
- `docs/` — content-update, publishing and development documentation.
- `content/` — local-only source and generated content locations; media is not committed.
- `data/` — local-only master workbook location.

## Build

Requirements:

- Windows 10/11 for Windows development, or macOS 13+ for macOS packaging;
- .NET SDK 10.0.302 or a compatible 10.0 patch;
- Xcode command-line tools for the native macOS helper and DMG.

```powershell
dotnet build app\WallpaperWidget\WallpaperWidget.csproj -c Release
dotnet build publisher\WallpaperPublisher\WallpaperPublisher.csproj -c Release
dotnet run --project tests\ContentUpdateSmoke\ContentUpdateSmoke.csproj -c Release -- --resume-only
```

On macOS, build an Apple Silicon beta package with:

```bash
./scripts/build-macos-release.sh 0.1.0-beta.3 arm64
```

See [development setup](docs/DEVELOPMENT.md), [content publishing](docs/CONTENT-PUBLISHING.md), and [content update internals](docs/CONTENT-UPDATES.md).
Release creation is documented in [application releases](docs/RELEASING.md).

## Content and privacy

Wallpaper media is not stored in this repository. The application downloads a selected collection from its configured public content endpoint. It does not contain analytics, advertising or user accounts. See [privacy and network behavior](docs/PRIVACY.md).

## License status

No open-source license has been selected yet. Public visibility alone does not grant permission to redistribute the code or separately hosted media.
