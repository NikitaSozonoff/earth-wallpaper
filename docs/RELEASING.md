# Application releases

## Release contract

Application version tags use semantic versions such as `v0.1.0-beta.1`. A tag triggers the Windows and macOS release jobs, which create:

```text
EarthWallpaper-Setup-<version>.exe
EarthWallpaper-Portable-<version>.zip
EarthWallpaper-macOS-arm64-<version>.dmg
checksums.txt
release-manifest.json
```

The Windows installer contains the self-contained `win-x64` application. The macOS disk image contains a self-contained Apple Silicon `.app` bundle and a native AppKit wallpaper helper. Neither package bundles wallpaper images; users download their chosen collection from R2 after confirmation.

## Local verification

```powershell
.\scripts\build-release.ps1 -Version 0.1.0-beta.3 -RequireInstaller
```

The script publishes the app, removes debugging symbols, runs the packaged smoke-test, builds the ZIP and installer, and writes SHA-256 checksums under `artifacts/release/<version>/`.

Inno Setup is required only for a local installer build. GitHub Actions installs it on its Windows runner.

The macOS artifact must be built on macOS:

```bash
./scripts/build-macos-release.sh 0.1.0-beta.3 arm64
```

Without signing environment variables, the script applies an ad-hoc signature suitable for beta testing through Gatekeeper's one-time **Open Anyway** flow. Supplying `MACOS_SIGNING_IDENTITY`, `APPLE_ID`, `APPLE_TEAM_ID`, and `APPLE_APP_PASSWORD` enables Developer ID signing and notarization after the certificate has been installed in the build keychain.

If no local Mac is available, open **Actions → Release → Run workflow** in GitHub, enter the version, and run it manually. This builds the Windows and macOS packages without creating a public GitHub Release. Download `macos-release` from the workflow run's **Artifacts** section and send the contained DMG to the tester.

## Starting a GitHub release

After Stage 6 changes are committed, pushed, and CI is green, double-click `scripts/start-beta-release.cmd` or run:

```powershell
.\scripts\start-github-release.ps1 -Version 0.1.0-beta.3
```

The script requires a clean `main` branch and the explicit word `RELEASE`, then creates and pushes the version tag. GitHub Actions builds and publishes the release. Do not create the tag until the exact commit is ready to distribute.

## Update behavior

The application checks the repository's public Releases API. Drafts are ignored; beta builds accept prereleases. When a newer semantic version is found, the application presents its release notes and explicit **Windows** and **macOS** package buttons. It never downloads or executes an application update silently.

The installer uses a stable application ID, so later installers upgrade the existing installation. Application files live under `%LOCALAPPDATA%\Programs\Earth Wallpaper`; settings and downloaded content live separately under `%LOCALAPPDATA%\EarthWallpaper` and are not removed by upgrades or uninstall.

## Before publishing

1. Run application and content smoke-tests.
2. Run the packaged Windows release build locally.
3. Install the Setup EXE and confirm launch, tray restore, startup toggle and content access.
4. Let the macOS job build the ad-hoc signed Apple Silicon DMG.
5. Install the DMG on a real Apple Silicon Mac and verify Gatekeeper override, menu-bar restore, wallpaper changes on every display and content access.
6. Confirm `git status` is clean and CI is green.
7. Push the tag using the release script.
8. Download both GitHub packages on clean test accounts and repeat first-run installation.
