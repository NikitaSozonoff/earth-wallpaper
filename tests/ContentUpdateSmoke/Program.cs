using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using WallpaperWidget.Models;
using WallpaperWidget.Services;

if (!args.Contains("--resume-only", StringComparer.OrdinalIgnoreCase))
    await CheckPublishedCatalogsAsync();
CheckApplicationVersions();
await CheckApplicationUpdateDiscoveryAsync();
await CheckResumableInstallAsync();

static void CheckApplicationVersions()
{
    var ordered = new[] { "v0.1.0-beta.1", "0.1.0-beta.2", "0.1.0", "0.2.0-beta.1" }
        .Select(value => SemanticVersion.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Version could not be parsed: {value}"))
        .ToArray();
    for (var index = 1; index < ordered.Length; index++)
    {
        if (ordered[index - 1].CompareTo(ordered[index]) >= 0)
            throw new InvalidOperationException($"Version ordering failed: {ordered[index - 1]} >= {ordered[index]}");
    }
    Console.WriteLine("application versions: prerelease and stable ordering verified");
}

static async Task CheckApplicationUpdateDiscoveryAsync()
{
    const string releaseJson = """
    [
      {
        "tag_name": "v0.1.0-beta.2",
        "name": "Earth Wallpaper 0.1.0-beta.2",
        "html_url": "https://github.com/NikitaSozonoff/earth-wallpaper/releases/tag/v0.1.0-beta.2",
        "body": "Smoke-test release notes.",
        "draft": false,
        "prerelease": true,
        "assets": [
          {
            "name": "EarthWallpaper-Setup-0.1.0-beta.2.exe",
            "browser_download_url": "https://github.com/NikitaSozonoff/earth-wallpaper/releases/download/v0.1.0-beta.2/EarthWallpaper-Setup-0.1.0-beta.2.exe"
          }
        ]
      }
    ]
    """;
    var service = new ApplicationUpdateService(
        new StaticJsonHandler(releaseJson),
        currentVersion: "0.1.0-beta.1");
    var result = await service.CheckAsync();
    if (!result.IsUpdateAvailable || result.AvailableUpdate?.Version != "0.1.0-beta.2" || result.AvailableUpdate.InstallerDownloadUrl is null)
        throw new InvalidOperationException("GitHub release discovery did not select the newer installer.");
    Console.WriteLine("application update: newer GitHub prerelease and Setup asset discovered");
}

static async Task CheckPublishedCatalogsAsync()
{
    var updater = new ContentUpdateService(new ContentStorage());
    var freshUpdater = new ContentUpdateService(new ContentStorage(Path.Combine(Path.GetTempPath(), $"earth-wallpaper-smoke-{Guid.NewGuid():N}")));
    foreach (var packId in new[] { ContentPacks.All, ContentPacks.Aesthetic })
    {
        var plan = await updater.CheckAsync(packId);
        var freshPlan = await freshUpdater.CheckAsync(packId);
        Console.WriteLine(
            $"{packId}: version={plan.Manifest.ContentVersion}, entries={plan.Manifest.EntryCount}, " +
            $"download={plan.DownloadBytes}, freshDownload={freshPlan.DownloadBytes}, " +
            $"total={plan.TotalPackBytes}, upToDate={plan.IsUpToDate}");
    }
}

static async Task CheckResumableInstallAsync()
{
    var root = Path.Combine(Path.GetTempPath(), $"earth-wallpaper-resume-{Guid.NewGuid():N}");
    try
    {
        var assetBytes = Enumerable.Range(0, 32 * 1024).Select(index => (byte)(index % 251)).ToArray();
        var assetHash = Convert.ToHexString(SHA256.HashData(assetBytes)).ToLowerInvariant();
        var assetPath = $"assets/{assetHash[..24]}.jpg";
        var version = "resume-smoke-v1";
        var catalog = new CatalogDocument
        {
            SchemaVersion = 2,
            PackId = ContentPacks.Aesthetic,
            ContentVersion = version,
            Entries =
            [
                new PlaceEntry
                {
                    Id = "resume-smoke",
                    Title = "Resume smoke test",
                    ImageFile = assetPath,
                    ImageSha256 = assetHash,
                    ImageBytes = assetBytes.Length,
                },
            ],
        };
        var catalogBytes = JsonSerializer.SerializeToUtf8Bytes(catalog);
        var catalogHash = Convert.ToHexString(SHA256.HashData(catalogBytes)).ToLowerInvariant();
        var catalogPath = $"catalogs/catalog-{version}.json";
        var manifest = new ContentManifest
        {
            SchemaVersion = 1,
            PackId = ContentPacks.Aesthetic,
            ContentVersion = version,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            EntryCount = 1,
            AssetCount = 1,
            DownloadBytes = assetBytes.Length + catalogBytes.Length,
            Catalog = new ManifestFile
            {
                Path = catalogPath,
                Sha256 = catalogHash,
                Bytes = catalogBytes.Length,
            },
        };
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        var storage = new ContentStorage(root);
        var partialPath = storage.GetPartialAssetPath(assetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllBytesAsync(partialPath, assetBytes[..4096]);

        var handler = new FakeContentHandler(manifestBytes, catalogBytes, catalogPath, assetBytes, assetPath);
        var updater = new ContentUpdateService(
            storage,
            new RemoteContentOptions { BaseUrl = "https://content.test/" },
            handler);
        var plan = await updater.CheckAsync(ContentPacks.Aesthetic);
        if (plan.DownloadBytes != assetBytes.Length - 4096)
            throw new InvalidOperationException($"Resume plan is incorrect: {plan.DownloadBytes} bytes.");

        await updater.InstallAsync(plan);
        var finalPath = storage.GetContentPath(assetPath);
        if (!File.Exists(finalPath) || !File.ReadAllBytes(finalPath).SequenceEqual(assetBytes))
            throw new InvalidOperationException("Resumed asset was not installed correctly.");
        if (storage.GetActiveVersion() != version || storage.GetActivePackId() != ContentPacks.Aesthetic)
            throw new InvalidOperationException("Resumed catalog was not activated.");
        if (handler.LastRangeStart != 4096)
            throw new InvalidOperationException("The downloader did not request the remaining byte range.");

        Console.WriteLine($"resume: requested bytes {handler.LastRangeStart}-{assetBytes.Length - 1}, verified and activated");

        File.Delete(finalPath);
        var repairPlan = await updater.CheckAsync(ContentPacks.Aesthetic);
        if (repairPlan.IsUpToDate || repairPlan.MissingAssetCount != 1 || repairPlan.DownloadBytes != assetBytes.Length)
            throw new InvalidOperationException("A missing asset was not detected in the current release.");
        await updater.InstallAsync(repairPlan);
        if (!File.Exists(finalPath) || !File.ReadAllBytes(finalPath).SequenceEqual(assetBytes))
            throw new InvalidOperationException("A missing asset was not restored correctly.");
        Console.WriteLine("repair: missing active asset detected, downloaded, verified and restored");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}

sealed class FakeContentHandler(
    byte[] manifestBytes,
    byte[] catalogBytes,
    string catalogPath,
    byte[] assetBytes,
    string assetPath) : HttpMessageHandler
{
    public long? LastRangeStart { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath.TrimStart('/') ?? string.Empty;
        if (path == ContentPacks.ManifestFile(ContentPacks.Aesthetic))
            return Task.FromResult(Response(HttpStatusCode.OK, manifestBytes));
        if (path == catalogPath)
            return Task.FromResult(Response(HttpStatusCode.OK, catalogBytes));
        if (path == assetPath)
        {
            var start = request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0;
            LastRangeStart = start;
            var response = Response(start > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK, assetBytes[(int)start..]);
            if (start > 0)
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, assetBytes.Length - 1, assetBytes.Length);
            return Task.FromResult(response);
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static HttpResponseMessage Response(HttpStatusCode statusCode, byte[] bytes) => new(statusCode)
    {
        Content = new ByteArrayContent(bytes),
    };
}

sealed class StaticJsonHandler(string json) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        });
}
