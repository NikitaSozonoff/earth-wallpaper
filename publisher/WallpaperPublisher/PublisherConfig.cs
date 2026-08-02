using System.Text.Json;

namespace WallpaperPublisher;

public sealed class PublisherConfig
{
    public string WorkbookPath { get; init; } = "../data/Wallpaper catalog.xlsx";
    public string Worksheet { get; init; } = "Export";
    public string SourceImagesPath { get; init; } = "../content/source-images";
    public string OutputPath { get; init; } = "../content/publish";
    public string StatePath { get; init; } = "state";
    public bool RequireReadyValidation { get; init; }
    public int ShortDescriptionMaxLength { get; init; } = 220;

    public static PublisherConfig Load(string configPath)
    {
        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<PublisherConfig>(json, JsonDefaults.Read)
            ?? throw new InvalidDataException($"Publisher configuration is invalid: {configPath}");
    }

    public ResolvedPublisherConfig Resolve(string configPath)
    {
        var basePath = Path.GetDirectoryName(Path.GetFullPath(configPath))
            ?? Environment.CurrentDirectory;

        return new ResolvedPublisherConfig(
            ResolvePath(basePath, WorkbookPath),
            Worksheet,
            ResolvePath(basePath, SourceImagesPath),
            ResolvePath(basePath, OutputPath),
            ResolvePath(basePath, StatePath),
            RequireReadyValidation,
            Math.Clamp(ShortDescriptionMaxLength, 100, 400));
    }

    private static string ResolvePath(string basePath, string path) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(basePath, path));
}

public sealed record ResolvedPublisherConfig(
    string WorkbookPath,
    string Worksheet,
    string SourceImagesPath,
    string OutputPath,
    string StatePath,
    bool RequireReadyValidation,
    int ShortDescriptionMaxLength);

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Read = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static readonly JsonSerializerOptions Write = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static readonly JsonSerializerOptions Canonical = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
}
