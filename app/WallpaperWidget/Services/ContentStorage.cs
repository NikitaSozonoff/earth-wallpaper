using System.Text.Json;
using WallpaperWidget.Models;

namespace WallpaperWidget.Services;

public sealed class ContentStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ContentStorage(string? rootPath = null)
    {
        RootPath = rootPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EarthWallpaperPrototype",
            "content");
    }

    public string RootPath { get; }

    public string ReleasesPath => Path.Combine(RootPath, "releases");
    public string AssetsPath => Path.Combine(RootPath, "assets");
    public string StagingPath => Path.Combine(RootPath, "staging");
    public string ActivePointerPath => Path.Combine(RootPath, "active.json");
    public string PendingUpdatePath => Path.Combine(StagingPath, "pending-update.json");

    public string? GetActiveVersion()
    {
        try
        {
            if (!File.Exists(ActivePointerPath)) return null;
            var pointer = JsonSerializer.Deserialize<ActiveContentPointer>(File.ReadAllText(ActivePointerPath), JsonOptions);
            return IsSafeVersion(pointer?.ContentVersion) ? pointer!.ContentVersion : null;
        }
        catch
        {
            return null;
        }
    }

    public string? GetActivePackId()
    {
        try
        {
            if (!File.Exists(ActivePointerPath)) return null;
            var pointer = JsonSerializer.Deserialize<ActiveContentPointer>(File.ReadAllText(ActivePointerPath), JsonOptions);
            return ContentPacks.IsValid(pointer?.PackId) ? pointer!.PackId : null;
        }
        catch
        {
            return null;
        }
    }

    public string? GetActiveCatalogPath()
    {
        var version = GetActiveVersion();
        if (version is null) return null;
        var path = Path.Combine(ReleasesPath, version, "catalog.json");
        return File.Exists(path) ? path : null;
    }

    public CatalogDocument? GetActiveCatalog()
    {
        try
        {
            var path = GetActiveCatalogPath();
            return path is null
                ? null
                : JsonSerializer.Deserialize<CatalogDocument>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public string GetContentPath(string relativePath)
    {
        var localPath = Path.GetFullPath(Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var normalizedRoot = Path.GetFullPath(RootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!localPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Content path escapes the local content directory.");
        return localPath;
    }

    public string GetPartialAssetPath(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        if (string.IsNullOrWhiteSpace(fileName)) throw new InvalidDataException("Content asset path is invalid.");
        return Path.Combine(StagingPath, fileName + ".partial");
    }

    public void SavePendingUpdate(PendingContentUpdate pending)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(pending);
        WriteAtomic(PendingUpdatePath, bytes);
    }

    public void ClearPendingUpdate()
    {
        try
        {
            if (File.Exists(PendingUpdatePath)) File.Delete(PendingUpdatePath);
        }
        catch
        {
            // A stale marker is harmless; the next check rebuilds the plan from verified files.
        }
    }

    public void Activate(string version, string packId)
    {
        if (!IsSafeVersion(version)) throw new InvalidDataException("Content version contains invalid characters.");
        if (!ContentPacks.IsValid(packId)) throw new InvalidDataException("Content pack is invalid.");
        Directory.CreateDirectory(RootPath);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new ActiveContentPointer { ContentVersion = version, PackId = packId });
        WriteAtomic(ActivePointerPath, bytes);
    }

    public static void WriteAtomic(string destination, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, destination, true);
    }

    public static bool IsSafeVersion(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
