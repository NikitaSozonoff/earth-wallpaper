# GitHub repository preparation

## First push

Create an empty public repository on GitHub without generating a README, `.gitignore` or license. Then, from this repository root:

```powershell
git remote add origin https://github.com/OWNER/earth-wallpaper.git
git push -u origin main
```

The local repository and initial commit are prepared separately before adding `origin`.

## Public repository boundary

The repository contains application code, publisher code, tests, documentation and safe configuration templates. It must not contain:

- wallpaper image files;
- the master workbook or its backups;
- generated R2 catalogs/assets/manifests;
- build and installer output;
- Cloudflare Access Key ID or Secret Access Key;
- rclone configuration;
- local logs or application settings.

Before every initial or release commit, inspect:

```powershell
git status --short
git status --ignored --short
git diff --cached
```

## Releases

Stage 6 will add a Windows installer and release script. Application binaries will be attached to GitHub Releases rather than committed to the repository:

```text
EarthWallpaper-Setup-<version>.exe
EarthWallpaper-Portable-<version>.zip
checksums.txt
```

The first public test release will use a prerelease tag such as `v0.1.0-beta`.

## License

No source-code license has been selected yet. Choose and add a license before describing the repository as open source. Public repository visibility by itself does not grant reuse rights.
