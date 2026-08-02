namespace WallpaperWidget.Models;

public static class ContentPacks
{
    public const string All = "all";
    public const string Aesthetic = "aesthetic";

    public static bool IsValid(string? packId) => packId is All or Aesthetic;

    public static string ManifestFile(string packId) => packId switch
    {
        All => "manifest.json",
        Aesthetic => "manifest-aesthetic.json",
        _ => throw new ArgumentOutOfRangeException(nameof(packId)),
    };

    public static string DisplayName(string? packId) => packId switch
    {
        Aesthetic => "Visual highlights",
        All => "All places",
        _ => "Not selected",
    };
}
