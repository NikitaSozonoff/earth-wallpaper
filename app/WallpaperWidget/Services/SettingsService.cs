using System.Text.Json;
using WallpaperWidget.Models;

namespace WallpaperWidget.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath = AppPaths.SettingsPath;

    public WidgetSettings Load()
    {
        try
        {
            return File.Exists(_settingsPath)
                ? JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(_settingsPath)) ?? new WidgetSettings()
                : new WidgetSettings();
        }
        catch
        {
            return new WidgetSettings();
        }
    }

    public void Save(WidgetSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // A read-only profile should not make the widget unusable.
        }
    }
}
