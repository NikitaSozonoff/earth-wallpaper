namespace WallpaperWidget.Services;

public static class WallpaperServiceFactory
{
    public static IWallpaperService Create()
    {
        if (OperatingSystem.IsWindows()) return new WindowsWallpaperService();
        if (OperatingSystem.IsMacOS()) return new MacOsWallpaperService();
        return new UnsupportedWallpaperService();
    }

    private sealed class UnsupportedWallpaperService : IWallpaperService
    {
        public bool TrySet(string imagePath, out string message)
        {
            message = "Wallpaper integration is not implemented for this operating system yet.";
            return false;
        }
    }
}
