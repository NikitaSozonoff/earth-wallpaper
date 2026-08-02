using System.Text.Json;

namespace WallpaperWidget.Services;

public static class AppLog
{
    private static readonly object Sync = new();
    private static readonly string DirectoryPath = AppPaths.LogsPath;
    private static bool _initialized;

    public static void Initialize()
    {
        lock (Sync)
        {
            if (_initialized) return;
            Directory.CreateDirectory(DirectoryPath);
            foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.jsonl"))
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-14))
                {
                    try { File.Delete(file); } catch { }
                }
            }
            _initialized = true;
        }
        Info("app_started", "Application started.");
        if (AppPaths.MigrationMessage is not null) Info("app_data_migration", AppPaths.MigrationMessage);
    }

    public static void Info(string eventName, string message, object? data = null) => Write("app", "info", eventName, message, data);
    public static void Warning(string eventName, string message, object? data = null) => Write("app", "warning", eventName, message, data);
    public static void Error(string eventName, string message, object? data = null) => Write("app", "error", eventName, message, data);
    public static void ContentInfo(string eventName, string message, object? data = null) => Write("content-update", "info", eventName, message, data);
    public static void ContentWarning(string eventName, string message, object? data = null) => Write("content-update", "warning", eventName, message, data);
    public static void ContentError(string eventName, string message, object? data = null) => Write("content-update", "error", eventName, message, data);

    private static void Write(string channel, string level, string eventName, string message, object? data)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                var record = new
                {
                    timestampUtc = DateTimeOffset.UtcNow,
                    level,
                    eventName,
                    message,
                    data,
                };
                var line = JsonSerializer.Serialize(record);
                File.AppendAllText(Path.Combine(DirectoryPath, $"{channel}-{DateTime.UtcNow:yyyy-MM-dd}.jsonl"), line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never prevent the widget from starting or changing wallpaper.
        }
    }
}
