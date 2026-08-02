namespace WallpaperWidget.Services;

public interface IWallpaperService
{
    bool TrySet(string imagePath, out string message);
}
