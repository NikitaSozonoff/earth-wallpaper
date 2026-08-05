namespace WallpaperWidget.Models;

public sealed class WidgetSettings
{
    public string? ContentPackId { get; set; }
    public int CurrentIndex { get; set; }
    public string? CurrentPlaceId { get; set; }
    public bool ShowLocation { get; set; } = true;
    public bool ShowTitle { get; set; } = true;
    public bool ShowShortDescription { get; set; } = true;
    public bool ShowNavigationControls { get; set; } = true;
    public bool PositionLocked { get; set; }
    public bool AutoRotateEnabled { get; set; }
    public int RotationMinutes { get; set; } = 1440;
    public ContentUpdateMode ContentUpdateMode { get; set; } = ContentUpdateMode.NotifyAutomatically;
    public DateTimeOffset? LastContentCheckUtc { get; set; }
    public DateTimeOffset? LastContentCheckAttemptUtc { get; set; }
    public DateTimeOffset? LastApplicationUpdateCheckUtc { get; set; }
    public bool LaunchAtStartup { get; set; }
    public double WidgetScale { get; set; } = 1.0;
    public double PanelOpacity { get; set; } = 0.84;
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
}

public enum ContentUpdateMode
{
    NotifyAutomatically = 0,
    DownloadAutomatically = 1,
    ManualOnly = 2,
}
