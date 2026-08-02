namespace WallpaperWidget.Services;

public static class AppPaths
{
    private const string CurrentDirectoryName = "EarthWallpaper";
    private const string LegacyDirectoryName = "EarthWallpaperPrototype";

    static AppPaths()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var current = Path.Combine(localData, CurrentDirectoryName);
        var legacy = Path.Combine(localData, LegacyDirectoryName);
        RootPath = ResolveRoot(current, legacy);
    }

    public static string RootPath { get; }
    public static string ContentPath => Path.Combine(RootPath, "content");
    public static string LogsPath => Path.Combine(RootPath, "logs");
    public static string SettingsPath => Path.Combine(RootPath, "settings.json");
    public static string? MigrationMessage { get; private set; }

    private static string ResolveRoot(string current, string legacy)
    {
        if (!Directory.Exists(legacy)) return current;

        try
        {
            if (!Directory.Exists(current))
            {
                Directory.Move(legacy, current);
                MigrationMessage = $"Application data moved from '{LegacyDirectoryName}' to '{CurrentDirectoryName}'.";
                return current;
            }

            MergeMissingFiles(legacy, current);
            MigrationMessage = $"Missing application data was recovered from '{LegacyDirectoryName}'.";
            return current;
        }
        catch
        {
            // Continuing from the legacy location is safer than starting with empty settings/content.
            MigrationMessage = $"Application data migration is pending; using '{LegacyDirectoryName}'.";
            return legacy;
        }
    }

    private static void MergeMissingFiles(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            if (File.Exists(target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            try { File.Move(file, target); }
            catch { File.Copy(file, target, false); }
        }
    }
}
