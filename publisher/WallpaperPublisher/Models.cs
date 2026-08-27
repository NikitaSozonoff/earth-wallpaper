namespace WallpaperPublisher;

public sealed record SourcePlace(
    int RowNumber,
    string Id,
    string Country,
    string Region,
    string Title,
    string ShortDescription,
    string Description,
    double Latitude,
    double Longitude,
    int? Zoom,
    string Sources,
    string ImageFile,
    string ImageStatus,
    string ImageryDate,
    string DateStatus,
    string Attribution,
    string Tags,
    string Aesthetics,
    string Story,
    int Revision,
    string Validation);

public sealed class PublishedCatalog
{
    public int SchemaVersion { get; init; } = 2;
    public string PackId { get; init; } = string.Empty;
    public string ContentVersion { get; init; } = string.Empty;
    public List<PublishedPlace> Entries { get; init; } = [];
}

public sealed class PublishedPlace
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
    public List<PublishedSource> Sources { get; init; } = [];
    public string ImageFile { get; init; } = string.Empty;
    public string ImageSha256 { get; init; } = string.Empty;
    public long ImageBytes { get; init; }
    public string? ImageryDate { get; init; }
    public string? DateStatus { get; init; }
    public string? Attribution { get; init; }
    public string? Tags { get; init; }
    public string? Aesthetics { get; init; }
    public string? Story { get; init; }
    public int Revision { get; init; }
}

public sealed class PublishedSource
{
    public string Label { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}

public sealed class ContentManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string PackId { get; init; } = string.Empty;
    public string ContentVersion { get; init; } = string.Empty;
    public DateTimeOffset GeneratedAtUtc { get; init; }
    public int EntryCount { get; init; }
    public int AssetCount { get; init; }
    public long DownloadBytes { get; init; }
    public ManifestFile Catalog { get; init; } = new();
}

public sealed class ManifestFile
{
    public string Path { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long Bytes { get; init; }
}

public sealed record ValidationIssue(string Severity, string Code, string Message, int? Row = null, string? PlaceId = null);

public sealed record PublishedPackSummary(string PackId, int EntryCount, long DownloadBytes, string ContentVersion, string ManifestPath);

public sealed class PublisherReport
{
    public string Command { get; init; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset FinishedAtUtc { get; set; }
    public bool Success { get; set; }
    public int SourceRows { get; set; }
    public int PublishedEntries { get; set; }
    public string? ContentVersion { get; set; }
    public List<PublishedPackSummary> Packs { get; } = [];
    public List<ValidationIssue> Issues { get; } = [];
}

public sealed record PublisherResult(bool Success, PublisherReport Report, string? ManifestPath);
