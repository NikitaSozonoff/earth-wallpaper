Earth Wallpaper is currently beta software. Application packages are self-contained; users do not need to install .NET.

## Windows installation

Download and run `EarthWallpaper-Setup-<version>.exe`. Before updating an existing installation, exit Earth Wallpaper from its notification-area menu so the installer can replace the running executable. Settings and downloaded wallpapers are preserved.

## macOS installation

Separate DMGs are provided for Apple Silicon (`arm64`, M1 or newer) and Intel (`x64`) Macs with macOS 13 or newer. Download the package matching the Mac's processor. This beta is ad-hoc signed but is not notarized by Apple.

1. Open the DMG and drag **Earth Wallpaper.app** to **Applications**.
2. In Applications, Control-click Earth Wallpaper and choose **Open**.
3. If offered, use **System Settings → Privacy & Security → Open Anyway**.
4. If macOS still does nothing, open Terminal and run:

```bash
xattr -dr com.apple.quarantine "/Applications/Earth Wallpaper.app"
open "/Applications/Earth Wallpaper.app"
```

This command removes the internet-download quarantine attribute from Earth Wallpaper only. It does not disable Gatekeeper globally. The same instructions are included inside the DMG as `READ ME FIRST.txt`.

Earth Wallpaper runs as a menu-bar application and may not appear in the Dock.

## Content

Wallpaper collections are downloaded separately after the user chooses a collection and confirms its size. Application updates never install silently.
