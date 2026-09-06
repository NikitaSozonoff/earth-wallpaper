namespace WallpaperWidget.Models;

public enum ApplicationPackagePlatform
{
    Windows,
    MacOS,
}

public enum ApplicationPackageArchitecture
{
    Any,
    Arm64,
    X64,
}

public sealed record ApplicationUpdatePackage(
    ApplicationPackagePlatform Platform,
    ApplicationPackageArchitecture Architecture,
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
    public ApplicationUpdatePackage? PackageFor(
        ApplicationPackagePlatform platform,
        ApplicationPackageArchitecture architecture = ApplicationPackageArchitecture.Any)
    {
        var platformPackages = Packages.Where(package => package.Platform == platform);
        if (architecture == ApplicationPackageArchitecture.Any)
            return platformPackages.FirstOrDefault();

        return platformPackages.FirstOrDefault(package => package.Architecture == architecture) ??
               platformPackages.FirstOrDefault(package => package.Architecture == ApplicationPackageArchitecture.Any);
    }
}

public sealed record ApplicationUpdateCheckResult(
    string CurrentVersion,
    ApplicationUpdateInfo? AvailableUpdate)
{
    public bool IsUpdateAvailable => AvailableUpdate is not null;
}
