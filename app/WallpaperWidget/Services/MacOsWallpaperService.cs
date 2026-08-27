using System.Diagnostics;

namespace WallpaperWidget.Services;

public sealed class MacOsWallpaperService : IWallpaperService
{
    private const string HelperFileName = "EarthWallpaperMacHelper";
    private static readonly TimeSpan HelperTimeout = TimeSpan.FromSeconds(20);

    public bool TrySet(string imagePath, out string message)
    {
        if (!OperatingSystem.IsMacOS())
        {
            message = "The macOS wallpaper service is unavailable on this operating system.";
            return false;
        }

        if (!File.Exists(imagePath))
        {
            message = "The source image could not be found.";
            return false;
        }

        var helperPath = Path.Combine(AppContext.BaseDirectory, HelperFileName);
        if (!File.Exists(helperPath))
        {
            message = "The macOS wallpaper helper is missing. Reinstall the application bundle.";
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo(helperPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("set-wallpaper");
            startInfo.ArgumentList.Add(Path.GetFullPath(imagePath));

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                message = "The macOS wallpaper helper could not be started.";
                return false;
            }

            if (!process.WaitForExit((int)HelperTimeout.TotalMilliseconds))
            {
                try { process.Kill(true); } catch { }
                message = "macOS did not finish changing the wallpaper in time.";
                return false;
            }

            var standardOutput = process.StandardOutput.ReadToEnd().Trim();
            var standardError = process.StandardError.ReadToEnd().Trim();
            if (process.ExitCode != 0)
            {
                message = string.IsNullOrWhiteSpace(standardError)
                    ? "macOS rejected the wallpaper change."
                    : standardError;
                return false;
            }

            message = string.IsNullOrWhiteSpace(standardOutput)
                ? "Wallpaper updated on every connected display."
                : standardOutput;
            return true;
        }
        catch (Exception exception)
        {
            message = $"The macOS wallpaper helper failed: {exception.Message}";
            return false;
        }
    }
}
