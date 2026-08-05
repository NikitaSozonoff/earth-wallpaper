# Application releases

## Release contract

Application version tags use semantic versions such as `v0.1.0-beta.1`. A tag triggers the Windows release workflow, which creates:

```text
EarthWallpaper-Setup-<version>.exe
EarthWallpaper-Portable-<version>.zip
checksums.txt
release-manifest.json
```

The installer contains the self-contained `win-x64` application and does not bundle wallpaper images. Users download their chosen collection from R2 after confirmation.

## Local verification

```powershell
.\scripts\build-release.ps1 -Version 0.1.0-beta.2 -RequireInstaller
```

The script publishes the app, removes debugging symbols, runs the packaged smoke-test, builds the ZIP and installer, and writes SHA-256 checksums under `artifacts/release/<version>/`.

Inno Setup is required only for a local installer build. GitHub Actions installs it on its Windows runner.

## Starting a GitHub release

After Stage 6 changes are committed, pushed, and CI is green, double-click `scripts/start-beta-release.cmd` or run:

```powershell
.\scripts\start-github-release.ps1 -Version 0.1.0-beta.2
```

The script requires a clean `main` branch and the explicit word `RELEASE`, then creates and pushes the version tag. GitHub Actions builds and publishes the release. Do not create the tag until the exact commit is ready to distribute.

## Update behavior

The application checks the repository's public Releases API. Drafts are ignored; beta builds accept prereleases. When a newer semantic version is found, the application presents its release notes and opens the Setup asset or release page in the default browser. It never downloads or executes an application update silently.

The installer uses a stable application ID, so later installers upgrade the existing installation. Application files live under `%LOCALAPPDATA%\Programs\Earth Wallpaper`; settings and downloaded content live separately under `%LOCALAPPDATA%\EarthWallpaper` and are not removed by upgrades or uninstall.

## Before publishing

1. Run application and content smoke-tests.
2. Run the packaged release build locally.
3. Install the Setup EXE and confirm launch, tray restore, startup toggle and content access.
4. Confirm `git status` is clean and CI is green.
5. Push the tag using the release script.
6. Download the GitHub installer on a second Windows account or machine and repeat first-run installation.
