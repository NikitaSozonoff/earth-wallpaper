namespace WallpaperWidget.Models;

public sealed class RemoteContentOptions
{
    public string BaseUrl { get; init; } = string.Empty;
}

public sealed class ContentManifest
{
    public int SchemaVersion { get; init; }
    public string? PackId { get; init; }
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

public sealed class ActiveContentPointer
{
    public string? PackId { get; init; }
    public string ContentVersion { get; init; } = string.Empty;
}

public sealed class ContentUpdatePlan
{
    public required string PackId { get; init; }
    public required ContentManifest Manifest { get; init; }
    public required CatalogDocument Catalog { get; init; }
    public required byte[] CatalogBytes { get; init; }
    public string? PreviousVersion { get; init; }
    public int AddedCount { get; init; }
    public int UpdatedCount { get; init; }
    public int RemovedCount { get; init; }
    public int MissingAssetCount { get; init; }
    public long TotalPackBytes { get; init; }
    public long DownloadBytes { get; init; }
    public bool IsUpToDate { get; init; }

    public int ChangedPlaceCount => AddedCount + UpdatedCount;
    public bool RequiresInstall => !IsUpToDate;
}

public sealed record ContentDownloadProgress(
    int CompletedAssets,
    int TotalAssets,
    long DownloadedBytes,
    long TotalBytes);

public sealed record ContentUpdateResult(
    bool Updated,
    string? Version,
    int EntryCount,
    int AddedCount,
    int UpdatedCount,
    int RemovedCount,
    string Message);

public sealed class PendingContentUpdate
{
    public string PackId { get; init; } = string.Empty;
    public string ContentVersion { get; init; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; init; }
    public int AssetCount { get; init; }
    public long DownloadBytes { get; init; }
}

public sealed record ContentPackSummary(string PackId, int EntryCount, long TotalBytes, long DownloadBytes)
{
    public string DetailText => DownloadBytes > 0
        ? $"{EntryCount} places · {ContentSizeFormatter.Format(DownloadBytes)} to download"
        : $"{EntryCount} places · already downloaded";
}

public static class ContentSizeFormatter
{
    public static string Format(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        var kib = bytes / 1024d;
        if (kib < 1024) return $"{kib:0.#} KiB";
        var mib = kib / 1024d;
        if (mib < 1024) return $"{mib:0.#} MiB";
        return $"{mib / 1024d:0.##} GiB";
    }
}
