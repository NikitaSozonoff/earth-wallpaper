# Wallpaper Publisher

Publisher converts the `Export` worksheet and the original images into a static, verifiable content release suitable for Cloudflare R2 or Cloudflare Pages.

## Everyday workflow

1. Replace or update `../data/Wallpaper catalog.xlsx`.
2. Add new original JPEG files to `../content/source-images/`.
3. Keep citation markers out of `Short description` and `Full description`. Store URLs separately in `Sources`, one per line. Use `Short label | URL` when a clear label is known; legacy `[1] URL` lines are also accepted.
4. Run a validation without creating a release:

   ```powershell
   .\publish.ps1 -ValidateOnly
   ```

5. Read `state/reports/latest-validation.json`. Errors stop the build; warnings are listed but currently allowed.
6. Build the release:

   ```powershell
   .\publish.ps1
   ```

7. Preview the R2 deployment:

   ```powershell
   .\deploy.ps1 -DryRun
   ```

8. Publish after reviewing the plan:

   ```powershell
   .\deploy.ps1
   ```

   Alternatively, double-click `publish-to-r2.cmd`. Both options require typing `PUBLISH` before R2 is changed.

The first run restores the `ClosedXML` NuGet dependency. Excel itself is not required.

## Configuration

All local paths and validation policy are in `publisher.config.json`. Paths are resolved relative to that file.

- `workbookPath`: master `.xlsx` workbook.
- `worksheet`: input sheet; normally `Export`.
- `sourceImagesPath`: original image directory.
- `outputPath`: Cloudflare-ready static directory.
- `bundledCatalogPath`: optional tracked fallback catalog copied into the application from the generated `all` pack.
- `statePath`: local logs and reports; never upload this directory.
- `requireReadyValidation`: when `true`, only rows with `Validation = Ready` are published.
- `shortDescriptionMaxLength`: maximum length of a short description derived from the full description.

`requireReadyValidation` is enabled for release builds: only rows marked `Ready` are allowed into either content pack.

## R2 deployment configuration

Copy `deploy.config.example.json` to the ignored local file `deploy.config.json` and set:

- `remoteName`: the local rclone remote, normally `earth-wallpaper-r2`;
- `bucket`: the R2 bucket name;
- `publicBaseUrl`: the public `r2.dev` or custom-domain URL, ending in `/`.

Cloudflare credentials remain in rclone's per-user configuration. They are never read from project files and must never be committed.

`deploy.ps1` always builds and validates the release before upload unless `-SkipBuild` is explicitly supplied. It uses `rclone copy`, never `sync`: no remote files are deleted. Assets and catalogs are uploaded first; mutable manifests are uploaded last. After a real deployment, both public manifests and their referenced catalogs are downloaded and verified. `-DryRun` performs the complete local validation and asks rclone for an upload plan without changing R2.

## Output contract

```text
content/publish/
├── manifest.json
├── manifest-aesthetic.json
├── catalogs/
│   └── catalog-<content-version>.json
└── assets/
    └── <first-24-characters-of-sha256>.jpg
```

- `manifest.json` points to the complete `All places` collection.
- `manifest-aesthetic.json` points to `Visual highlights`, containing only rows with `Aesthetics = Cool`.
- Both manifest files are mutable public pointers. Upload them after catalogs and assets.
- Catalogs and assets are immutable and content-addressed. Existing files do not need to be uploaded again.
- `contentVersion` is derived from normalized metadata and image hashes. Rebuilding identical content produces the same version.
- Do not delete an old catalog or asset immediately: a user may still be downloading the previous manifest.
- Shared images use the same hash-based asset path, so changing collections does not download the same image twice.

## Validation policy

Build-stopping errors include duplicate IDs, missing titles/countries/images, unsafe filenames, missing/empty files and invalid coordinates.

Warnings include missing descriptions, non-`Ready` validation status and non-standard imagery dates. Every warning records the Excel row and Place ID.

## Logs and reports

- `state/logs/publisher-YYYY-MM-DD.jsonl`: append-only operational log, one JSON object per event.
- `state/logs/deploy-YYYY-MM-DD.jsonl`: append-only deployment events.
- `state/logs/rclone-<run-id>.log`: detailed transfer output without credentials.
- `state/reports/latest-validation.json`: overwritten on every run for quick review.
- `state/reports/run-YYYYMMDD-HHMMSS.json`: immutable audit report for an individual run.
- `state/reports/latest-deploy.json`: latest deployment result and published content versions.
- `state/reports/deploy-<run-id>.json`: immutable deployment history.

Logs contain event names, row numbers, IDs and error types. They do not contain Cloudflare credentials, authorization headers or file contents.
