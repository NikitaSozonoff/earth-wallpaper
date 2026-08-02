# Content updates

## Runtime flow

Content updates use three separate operations:

1. `ContentUpdateService.CheckAsync(packId)` downloads the small manifest and catalog, compares stable place IDs and revisions, and returns a `ContentUpdatePlan`.
2. The UI displays the number of changed places and the remaining network/disk requirement from `ContentUpdatePlan.DownloadBytes`.
3. `ContentUpdateService.InstallAsync(plan)` runs only after confirmation, or when the user explicitly enabled automatic downloads.

Checking never activates a catalog and never starts image downloads.

## User modes

- **Notify automatically** (default): check at most once per 24 hours, notify through the widget/tray, require confirmation before downloading.
- **Download automatically**: check at most once per 24 hours and download in the background. Enabling this mode requires a separate `Yes, I understand` confirmation.
- **Manual only**: network checks happen only through `Check now` or the tray command.

Failed automatic checks are throttled for 30 minutes before another attempt.

## Dynamic size calculation

The application totals only assets that are not already present in the shared local cache. Complete assets shared by `All places` and `Visual highlights` are not counted twice. Bytes already stored in a resumable `.partial` file are also subtracted.

The confirmation button includes the calculated amount, for example `Download 166.9 MiB`.

## Resume and recovery

- Incomplete assets are stored in `content/staging/*.partial`.
- The next attempt sends an HTTP `Range` request starting after the saved bytes.
- Size and SHA-256 are verified before a partial file becomes an asset.
- `content/staging/pending-update.json` records the interrupted release.
- The active pointer changes only after every required asset and the catalog have passed validation.
- On an error, the previous active catalog and wallpaper files remain usable.
- A check also scans the selected catalog for missing or incorrectly sized local assets even when `contentVersion` has not changed. Missing files produce a repair plan and are downloaded again after confirmation.

## Local state and logs

Current prototype paths:

- settings: `%LOCALAPPDATA%/EarthWallpaperPrototype/settings.json`
- content: `%LOCALAPPDATA%/EarthWallpaperPrototype/content/`
- general log: `%LOCALAPPDATA%/EarthWallpaperPrototype/logs/app-YYYY-MM-DD.jsonl`
- update log: `%LOCALAPPDATA%/EarthWallpaperPrototype/logs/content-update-YYYY-MM-DD.jsonl`

Logs older than 14 days are removed at application startup. Logs contain versions, counts, byte totals and exception types, but not downloaded file contents or credentials.

## Verification

`tests/ContentUpdateSmoke` performs two checks:

- reads both real public R2 manifests and builds plans for the current and an empty cache;
- creates a small fake release, preloads a partial asset, verifies that the remaining HTTP byte range is requested, checks SHA-256, and confirms atomic activation.
- removes an asset from the active fake release and verifies that a normal check detects and restores it.

Run from the repository root:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' run -c Debug --project tests\ContentUpdateSmoke\ContentUpdateSmoke.csproj
```

Use `-- --resume-only` to skip the public R2 check.
