using System.Text.Json;
using WallpaperWidget.Models;

namespace WallpaperWidget.Services;

public sealed class CatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ContentStorage _contentStorage;
    private string? _contentRoot;

    public CatalogService(ContentStorage? contentStorage = null)
    {
        _contentStorage = contentStorage ?? new ContentStorage();
    }

    public IReadOnlyList<PlaceEntry> Load()
    {
        var activeCatalogPath = _contentStorage.GetActiveCatalogPath();
        var catalogPath = activeCatalogPath ?? Path.Combine(AppContext.BaseDirectory, "Data", "catalog.json");
        _contentRoot = activeCatalogPath is null ? null : _contentStorage.RootPath;
        if (!File.Exists(catalogPath))
        {
            throw new FileNotFoundException("The local wallpaper catalog was not found.", catalogPath);
        }

        var json = File.ReadAllText(catalogPath);
        var document = JsonSerializer.Deserialize<CatalogDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("The local wallpaper catalog is invalid.");

        return document.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.ImageFile))
            .ToArray();
    }

    public string ResolveImagePath(PlaceEntry entry)
    {
        if (_contentRoot is not null)
        {
            var cachedPath = Path.GetFullPath(Path.Combine(_contentRoot, entry.ImageFile.Replace('/', Path.DirectorySeparatorChar)));
            var normalizedRoot = Path.GetFullPath(_contentRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (cachedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(cachedPath)) return cachedPath;
        }

        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "content", "source-images", entry.ImageFile);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "content", "source-images", entry.ImageFile);
    }
}
