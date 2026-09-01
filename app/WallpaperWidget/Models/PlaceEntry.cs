namespace WallpaperWidget.Models;

public sealed class PlaceEntry
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Country { get; init; }
    public string? Region { get; init; }
    public string? ShortDescription { get; init; }
    public string? Description { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public int? Zoom { get; init; }
    public string? SourceUrl { get; init; }
    public List<SourceLink> Sources { get; init; } = [];
    public string ImageFile { get; init; } = string.Empty;
    public string? ImageSha256 { get; init; }
    public long? ImageBytes { get; init; }
    public string? ImageryDate { get; init; }
    public string? DateStatus { get; init; }
    public string? Attribution { get; init; }
    public string? Aesthetics { get; init; }
    public string? Story { get; init; }
    public string? Tags { get; init; }
    public int Revision { get; init; } = 1;
}

public sealed class SourceLink
{
    public string Label { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}

public sealed class SourceDisplayLink
{
    public string DisplayUrl { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Separator { get; init; } = string.Empty;
}
