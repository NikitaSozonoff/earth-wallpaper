using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using WallpaperWidget.Models;
using WallpaperWidget.Services;

if (!args.Contains("--resume-only", StringComparer.OrdinalIgnoreCase))
    await CheckPublishedCatalogsAsync();
await CheckResumableInstallAsync();

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
