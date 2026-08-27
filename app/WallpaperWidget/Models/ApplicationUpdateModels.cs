namespace WallpaperWidget.Models;

public enum ApplicationPackagePlatform
{
    Windows,
    MacOS,
}

public sealed record ApplicationUpdatePackage(
    ApplicationPackagePlatform Platform,
    string DisplayName,
    string FileName,
    string DownloadUrl);

public sealed record ApplicationUpdateInfo(
    string Version,
    string ReleaseName,
    string ReleasePageUrl,
    IReadOnlyList<ApplicationUpdatePackage> Packages,
    string ReleaseNotes,
    bool IsPrerelease)
{
    public ApplicationUpdatePackage? PackageFor(ApplicationPackagePlatform platform) =>
        Packages.FirstOrDefault(package => package.Platform == platform);
}

public sealed record ApplicationUpdateCheckResult(
    string CurrentVersion,
    ApplicationUpdateInfo? AvailableUpdate)
{
    public bool IsUpdateAvailable => AvailableUpdate is not null;
}
