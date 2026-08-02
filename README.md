# Earth Wallpaper

Earth Wallpaper is a lightweight desktop widget that changes the Windows wallpaper and presents a short educational story about the current place. The overlay can show a location, title and short description, or collapse to minimal controls.

> The project is currently a pre-release prototype. A Windows installer and application self-update channel are being prepared.

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
- tray controls and local structured logs.

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

Cloudflare R2 carries wallpaper content. GitHub carries source code and, once Stage 6 is complete, application installers through GitHub Releases. The source photographs, master workbook, generated R2 payload and deployment credentials are intentionally excluded from this repository.

## Repository layout

- `app/WallpaperWidget/` — Avalonia desktop application.
- `publisher/WallpaperPublisher/` — catalog validator and deterministic content builder.
- `tests/ContentUpdateSmoke/` — public-catalog and local recovery smoke tests.
- `docs/` — content-update, publishing and development documentation.
- `content/` — local-only source and generated content locations; media is not committed.
- `data/` — local-only master workbook location.

## Build

Requirements:

- Windows 10 or 11;
- .NET SDK 10.0.302 or a compatible 10.0 patch.

```powershell
dotnet build app\WallpaperWidget\WallpaperWidget.csproj -c Release
dotnet build publisher\WallpaperPublisher\WallpaperPublisher.csproj -c Release
dotnet run --project tests\ContentUpdateSmoke\ContentUpdateSmoke.csproj -c Release -- --resume-only
```

See [development setup](docs/DEVELOPMENT.md), [content publishing](docs/CONTENT-PUBLISHING.md), and [content update internals](docs/CONTENT-UPDATES.md).

## Content and privacy

Wallpaper media is not stored in this repository. The application downloads a selected collection from its configured public content endpoint. It does not contain analytics, advertising or user accounts. See [privacy and network behavior](docs/PRIVACY.md).

## License status

No open-source license has been selected yet. Public visibility alone does not grant permission to redistribute the code or separately hosted media.
