namespace WallpaperWidget.Models;

public sealed record ApplicationUpdateInfo(
    string Version,
    string ReleaseName,
    string ReleasePageUrl,
    string? InstallerDownloadUrl,
    string ReleaseNotes,
    bool IsPrerelease);

public sealed record ApplicationUpdateCheckResult(
    string CurrentVersion,
    ApplicationUpdateInfo? AvailableUpdate)
{
    public bool IsUpdateAvailable => AvailableUpdate is not null;
}
