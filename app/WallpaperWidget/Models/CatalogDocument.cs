namespace WallpaperWidget.Models;

public sealed class CatalogDocument
{
    public int SchemaVersion { get; init; }
    public string? PackId { get; init; }
    public string? ContentVersion { get; init; }
    public List<PlaceEntry> Entries { get; init; } = [];
}
