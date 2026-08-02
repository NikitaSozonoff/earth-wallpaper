using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WallpaperWidget.Services;

public sealed class WindowsWallpaperService : IWallpaperService
{
    private const uint SpiSetDesktopWallpaper = 0x0014;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    public bool TrySet(string imagePath, out string message)
    {
        if (!File.Exists(imagePath))
        {
            message = "The source image could not be found.";
            return false;
        }

        if (!SystemParametersInfo(SpiSetDesktopWallpaper, 0, imagePath, SpifUpdateIniFile | SpifSendChange))
        {
            message = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        message = "Wallpaper updated from the original image file.";
        return true;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, string value, uint flags);
}
