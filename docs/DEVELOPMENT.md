# Development setup

## Prerequisites

- Windows 10 or 11 for the Windows application and publisher workflow
- macOS 13 or newer with Xcode command-line tools for `.app`/DMG packaging
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

The managed application can be cross-published for Apple Silicon from Windows:

```powershell
dotnet publish app\WallpaperWidget\WallpaperWidget.csproj -c Release -r osx-arm64 --self-contained true -p:IncludeNativeLibrariesForSelfExtract=false
```

The final bundle needs the native Swift helper, code signing and `hdiutil`, so run the release packaging step on macOS:

```bash
./scripts/build-macos-release.sh 0.1.0-beta.3 arm64
```

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

## Content deployment commands

From `publisher/`:

```powershell
.\deploy.ps1 -DryRun       # validate, build and preview without R2 writes
.\deploy.ps1               # confirm interactively, publish, then verify public files
.\deploy.ps1 -Yes          # non-interactive publish for trusted local automation
.\deploy.ps1 -SkipBuild    # deploy the already generated release
```

The double-clickable `publish-to-r2.cmd` uses the interactive safe path. Deployment adds immutable assets/catalogs and replaces manifests last; it never deletes remote objects.

## Logs and local application data

The packaged application uses the operating system's local application-data directory under `EarthWallpaper/`. On Windows this is `%LOCALAPPDATA%/EarthWallpaper/`. On first launch it moves the former `EarthWallpaperPrototype/` directory when possible, preserving settings and cached assets without another content download.
