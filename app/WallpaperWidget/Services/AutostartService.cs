using Microsoft.Win32;

namespace WallpaperWidget.Services;

public sealed class AutostartService
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EarthWallpaper";

    public bool IsSupported => OperatingSystem.IsWindows() && GetExecutablePath() is not null;

    public bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch { return false; }
    }

    public void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Startup registration is currently available on Windows only.");
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true)
            ?? throw new InvalidOperationException("The Windows startup registry key could not be opened.");
        if (!enabled)
        {
            key.DeleteValue(ValueName, false);
            return;
        }

        var executable = GetExecutablePath()
            ?? throw new InvalidOperationException("Startup can be enabled after installing or launching the packaged application.");
        key.SetValue(ValueName, $"\"{executable}\" --minimized", RegistryValueKind.String);
    }

    private static string? GetExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        return Path.GetFileName(path).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase) ? null : path;
    }
}
