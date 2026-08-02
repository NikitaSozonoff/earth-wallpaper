# Development setup

## Prerequisites

- Windows 10 or 11
- .NET SDK 10.0.302 (the repository `global.json` accepts newer 10.0 patches)
- PowerShell 7 or Windows PowerShell
- Git for Windows

Excel is not required at runtime. Publisher reads `.xlsx` directly through ClosedXML.

## Build and verification

From the repository root:

```powershell
dotnet restore app\WallpaperWidget\WallpaperWidget.csproj
dotnet build app\WallpaperWidget\WallpaperWidget.csproj -c Release --no-restore

dotnet restore publisher\WallpaperPublisher\WallpaperPublisher.csproj
dotnet build publisher\WallpaperPublisher\WallpaperPublisher.csproj -c Release --no-restore

dotnet restore tests\ContentUpdateSmoke\ContentUpdateSmoke.csproj
dotnet run --project tests\ContentUpdateSmoke\ContentUpdateSmoke.csproj -c Release --no-restore -- --resume-only
```

The full smoke test also checks the configured public R2 manifests:

```powershell
dotnet run --project tests\ContentUpdateSmoke\ContentUpdateSmoke.csproj -c Release
```

The `--resume-only` test is deterministic and is the one used by GitHub Actions.

## Local-only directories

Git intentionally ignores:

- `content/source-images/`
- `content/publish/`
- `data/*.xlsx`
- `data/source-backups/`
- `publisher/state/`
- `outputs/` and `artifacts/`
- local deployment configuration and credentials

Do not force-add ignored media or workbooks to a public commit.

## Configuration

`app/WallpaperWidget/Data/remote-content.json` contains only the public read URL used by the application. It is safe to commit.

The future R2 deployment configuration is split into:

- `publisher/deploy.config.example.json` — safe tracked template;
- `publisher/deploy.config.json` — ignored local values;
- rclone's user-level configuration — credentials, never committed.

## Logs and local application data

The current prototype uses `%LOCALAPPDATA%/EarthWallpaperPrototype/`. Stage 6 will migrate this directory to `%LOCALAPPDATA%/EarthWallpaper/` while preserving settings and cached assets.
