namespace WallpaperWidget.Services;

public static class WallpaperServiceFactory
{
    public static IWallpaperService Create() => OperatingSystem.IsWindows()
        ? new WindowsWallpaperService()
        : new UnsupportedWallpaperService();

    private sealed class UnsupportedWallpaperService : IWallpaperService
    {
        public bool TrySet(string imagePath, out string message)
        {
            message = "Wallpaper integration is not implemented for this operating system yet.";
            return false;
        }
    }
}
