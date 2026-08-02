using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using WallpaperWidget.Models;

namespace WallpaperWidget.Services;

public sealed class ContentUpdateService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly ContentStorage _storage;
    private readonly HttpClient _httpClient;
    private readonly RemoteContentOptions? _options;

    public ContentUpdateService(
        ContentStorage storage,
        RemoteContentOptions? options = null,
        HttpMessageHandler? httpMessageHandler = null)
    {
        _storage = storage;
        _options = options;
        _httpClient = httpMessageHandler is null ? new HttpClient() : new HttpClient(httpMessageHandler);
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EarthWallpaper", "0.1"));
    }

    public async Task<ContentUpdatePlan> CheckAsync(string packId, CancellationToken cancellationToken = default)
    {
        if (!ContentPacks.IsValid(packId)) throw new ArgumentOutOfRangeException(nameof(packId));
        var options = LoadOptions();
        if (!TryGetBaseUri(options.BaseUrl, out var baseUri))
            throw new InvalidOperationException("Remote content URL is not configured.");

        AppLog.ContentInfo("content_check_started", "Checking the remote content catalog.", new { packId });
        var manifestName = ContentPacks.ManifestFile(packId);
        var manifestBytes = await DownloadBytesAsync(
            new Uri(baseUri, $"{manifestName}?nocache={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"),
            cancellationToken);
        var manifest = JsonSerializer.Deserialize<ContentManifest>(manifestBytes, JsonOptions)
            ?? throw new InvalidDataException("Remote manifest is invalid.");
        ValidateManifest(manifest, packId);

        var activeCatalog = _storage.GetActiveCatalog();
        var activeVersion = _storage.GetActiveVersion();
        var activeReleaseMatches = manifest.ContentVersion.Equals(activeVersion, StringComparison.OrdinalIgnoreCase)
            && packId.Equals(_storage.GetActivePackId(), StringComparison.OrdinalIgnoreCase)
            && activeCatalog is not null
            && manifest.ContentVersion.Equals(activeCatalog.ContentVersion, StringComparison.OrdinalIgnoreCase);

        if (activeReleaseMatches)
        {
            var requirements = CalculateAssetRequirements(activeCatalog!, cancellationToken);
            if (requirements.MissingAssetCount == 0)
            {
                AppLog.ContentInfo("content_up_to_date", "Content is already up to date and all assets are present.", new { packId, manifest.ContentVersion });
                return new ContentUpdatePlan
                {
                    PackId = packId,
                    Manifest = manifest,
                    Catalog = activeCatalog!,
                    CatalogBytes = [],
                    PreviousVersion = activeVersion,
                    TotalPackBytes = requirements.TotalPackBytes,
                    DownloadBytes = 0,
                    IsUpToDate = true,
                };
            }

            var activeCatalogPath = _storage.GetActiveCatalogPath()
                ?? throw new InvalidDataException("The active catalog path is unavailable.");
            var repairPlan = new ContentUpdatePlan
            {
                PackId = packId,
                Manifest = manifest,
                Catalog = activeCatalog!,
                CatalogBytes = await File.ReadAllBytesAsync(activeCatalogPath, cancellationToken),
                PreviousVersion = activeVersion,
                MissingAssetCount = requirements.MissingAssetCount,
                TotalPackBytes = requirements.TotalPackBytes,
                DownloadBytes = requirements.DownloadBytes,
                IsUpToDate = false,
            };
            AppLog.ContentWarning("content_repair_needed", "The active catalog is current but local assets are missing.", new
            {
                packId,
                manifest.ContentVersion,
                repairPlan.MissingAssetCount,
                repairPlan.DownloadBytes,
            });
            return repairPlan;
        }

        var catalogBytes = await DownloadBytesAsync(ResolveRelativeUri(baseUri, manifest.Catalog.Path), cancellationToken);
        VerifyBytes(catalogBytes, manifest.Catalog.Bytes, manifest.Catalog.Sha256, "catalog");
        var catalog = JsonSerializer.Deserialize<CatalogDocument>(catalogBytes, JsonOptions)
            ?? throw new InvalidDataException("Downloaded catalog is invalid.");
        ValidateCatalog(catalog, manifest, packId);

        var previousEntries = activeCatalog?.Entries.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, PlaceEntry>(StringComparer.OrdinalIgnoreCase);
        var nextEntries = catalog.Entries.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
        var added = nextEntries.Keys.Count(id => !previousEntries.ContainsKey(id));
        var updated = nextEntries.Count(pair => previousEntries.TryGetValue(pair.Key, out var oldEntry) && EntryChanged(oldEntry, pair.Value));
        var removed = previousEntries.Keys.Count(id => !nextEntries.ContainsKey(id));

        var assetRequirements = CalculateAssetRequirements(catalog, cancellationToken);

        var plan = new ContentUpdatePlan
        {
            PackId = packId,
            Manifest = manifest,
            Catalog = catalog,
            CatalogBytes = catalogBytes,
            PreviousVersion = activeVersion,
            AddedCount = added,
            UpdatedCount = updated,
            RemovedCount = removed,
            MissingAssetCount = assetRequirements.MissingAssetCount,
            TotalPackBytes = assetRequirements.TotalPackBytes,
            DownloadBytes = assetRequirements.DownloadBytes,
            IsUpToDate = false,
        };
        AppLog.ContentInfo("content_plan_ready", "Content update plan is ready.", new
        {
            packId,
            manifest.ContentVersion,
            added,
            updated,
            removed,
            assetRequirements.MissingAssetCount,
            downloadBytes = assetRequirements.DownloadBytes,
            totalPackBytes = assetRequirements.TotalPackBytes,
        });
        return plan;
    }

    public async Task<ContentUpdateResult> InstallAsync(
        ContentUpdatePlan plan,
        IProgress<ContentDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (plan.IsUpToDate)
        {
            return new ContentUpdateResult(false, plan.Manifest.ContentVersion, plan.Manifest.EntryCount, 0, 0, 0, "Content is up to date.");
        }

        var options = LoadOptions();
        if (!TryGetBaseUri(options.BaseUrl, out var baseUri))
            throw new InvalidOperationException("Remote content URL is not configured.");

        var assets = DistinctAssets(plan.Catalog).ToArray();
        _storage.SavePendingUpdate(new PendingContentUpdate
        {
            PackId = plan.PackId,
            ContentVersion = plan.Manifest.ContentVersion,
            StartedAtUtc = DateTimeOffset.UtcNow,
            AssetCount = assets.Length,
            DownloadBytes = plan.DownloadBytes,
        });
        AppLog.ContentInfo("content_install_started", "Installing content update.", new
        {
            plan.PackId,
            plan.Manifest.ContentVersion,
            assetCount = assets.Length,
            plan.DownloadBytes,
        });

        var completedAssets = 0;
        long downloadedBytes = 0;
        try
        {
            foreach (var entry in assets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateAsset(entry);
                var expectedBytes = entry.ImageBytes!.Value;
                var localPath = _storage.GetContentPath(entry.ImageFile);
                if (!IsCompleteLocalAsset(localPath, expectedBytes))
                {
                    await DownloadAssetAsync(
                        ResolveRelativeUri(baseUri, entry.ImageFile),
                        entry,
                        localPath,
                        bytesTransferred =>
                        {
                            downloadedBytes += bytesTransferred;
                            progress?.Report(new ContentDownloadProgress(
                                completedAssets,
                                assets.Length,
                                downloadedBytes,
                                plan.DownloadBytes));
                        },
                        cancellationToken);
                }

                completedAssets++;
                progress?.Report(new ContentDownloadProgress(
                    completedAssets,
                    assets.Length,
                    downloadedBytes,
                    plan.DownloadBytes));
            }

            var releasePath = Path.Combine(_storage.ReleasesPath, plan.Manifest.ContentVersion);
            Directory.CreateDirectory(releasePath);
            ContentStorage.WriteAtomic(Path.Combine(releasePath, "catalog.json"), plan.CatalogBytes);
            _storage.Activate(plan.Manifest.ContentVersion, plan.PackId);
            _storage.ClearPendingUpdate();
            AppLog.ContentInfo("content_installed", "Content update installed and activated.", new
            {
                plan.PackId,
                plan.Manifest.ContentVersion,
                plan.Manifest.EntryCount,
                plan.AddedCount,
                plan.UpdatedCount,
                plan.RemovedCount,
                downloadedBytes,
            });
            return new ContentUpdateResult(
                true,
                plan.Manifest.ContentVersion,
                plan.Manifest.EntryCount,
                plan.AddedCount,
                plan.UpdatedCount,
                plan.RemovedCount,
                BuildInstalledMessage(plan));
        }
        catch (OperationCanceledException)
        {
            AppLog.ContentWarning("content_install_paused", "Content installation was interrupted and can be resumed.", new
            {
                plan.PackId,
                plan.Manifest.ContentVersion,
                completedAssets,
                downloadedBytes,
            });
            throw;
        }
        catch (Exception exception)
        {
            AppLog.ContentError("content_install_failed", "Content installation failed; the active catalog was not changed.", new
            {
                plan.PackId,
                plan.Manifest.ContentVersion,
                completedAssets,
                downloadedBytes,
                exception = exception.GetType().Name,
                exception.Message,
            });
            throw;
        }
    }

    private async Task DownloadAssetAsync(
        Uri uri,
        PlaceEntry entry,
        string destination,
        Action<long> reportTransferred,
        CancellationToken cancellationToken)
    {
        var expectedBytes = entry.ImageBytes!.Value;
        var partialPath = _storage.GetPartialAssetPath(entry.ImageFile);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);

        if (File.Exists(destination) && new FileInfo(destination).Length != expectedBytes)
            File.Delete(destination);

        var offset = GetUsablePartialLength(partialPath, expectedBytes);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var canAppend = offset > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!canAppend) offset = 0;
        await using (var target = new FileStream(
            partialPath,
            canAppend ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            var buffer = new byte[128 * 1024];
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                reportTransferred(read);
            }
            await target.FlushAsync(cancellationToken);
        }

        var actualBytes = new FileInfo(partialPath).Length;
        if (actualBytes != expectedBytes)
            throw new InvalidDataException($"Downloaded {entry.ImageFile} has an unexpected size ({actualBytes} of {expectedBytes} bytes).");

        string actualHash;
        await using (var verificationStream = File.OpenRead(partialPath))
        {
            actualHash = Convert.ToHexString(await SHA256.HashDataAsync(verificationStream, cancellationToken)).ToLowerInvariant();
        }
        if (!actualHash.Equals(entry.ImageSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partialPath);
            throw new InvalidDataException($"Downloaded {entry.ImageFile} failed SHA-256 verification.");
        }

        File.Move(partialPath, destination, true);
    }

    private RemoteContentOptions LoadOptions()
    {
        if (_options is not null) return _options;
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "remote-content.json");
        if (!File.Exists(path)) return new RemoteContentOptions();
        return JsonSerializer.Deserialize<RemoteContentOptions>(File.ReadAllText(path), JsonOptions) ?? new RemoteContentOptions();
    }

    private async Task<byte[]> DownloadBytesAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static void ValidateManifest(ContentManifest manifest, string expectedPackId)
    {
        if (manifest.SchemaVersion != 1) throw new InvalidDataException($"Unsupported manifest schema: {manifest.SchemaVersion}");
        if (!string.IsNullOrWhiteSpace(manifest.PackId) && !expectedPackId.Equals(manifest.PackId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Remote manifest belongs to a different content pack.");
        if (!ContentStorage.IsSafeVersion(manifest.ContentVersion)) throw new InvalidDataException("Manifest content version is invalid.");
        ValidateRelativePath(manifest.Catalog.Path);
        if (manifest.EntryCount <= 0 || manifest.DownloadBytes <= 0)
            throw new InvalidDataException("Manifest content totals are invalid.");
        if (manifest.Catalog.Bytes <= 0 || manifest.Catalog.Sha256.Length != 64)
            throw new InvalidDataException("Manifest catalog metadata is invalid.");
    }

    private static void ValidateCatalog(CatalogDocument catalog, ContentManifest manifest, string packId)
    {
        if (catalog.SchemaVersion != 2) throw new InvalidDataException($"Unsupported catalog schema: {catalog.SchemaVersion}");
        if (!string.IsNullOrWhiteSpace(catalog.PackId) && !packId.Equals(catalog.PackId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Downloaded catalog belongs to a different content pack.");
        if (!manifest.ContentVersion.Equals(catalog.ContentVersion, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Manifest and catalog content versions do not match.");
        if (catalog.Entries.Count != manifest.EntryCount)
            throw new InvalidDataException("Manifest and catalog entry counts do not match.");
        if (catalog.Entries.Any(entry => string.IsNullOrWhiteSpace(entry.Id)) ||
            catalog.Entries.Select(entry => entry.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != catalog.Entries.Count)
            throw new InvalidDataException("Catalog contains missing or duplicate place IDs.");
    }

    private static void ValidateAsset(PlaceEntry entry)
    {
        ValidateRelativePath(entry.ImageFile);
        if (entry.ImageSha256 is null || entry.ImageSha256.Length != 64 || entry.ImageBytes is null or <= 0)
            throw new InvalidDataException($"Catalog image metadata is invalid for {entry.Id}.");
    }

    private static IEnumerable<PlaceEntry> DistinctAssets(CatalogDocument catalog) =>
        catalog.Entries
            .GroupBy(entry => entry.ImageFile, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());

    private (long DownloadBytes, long TotalPackBytes, int MissingAssetCount) CalculateAssetRequirements(
        CatalogDocument catalog,
        CancellationToken cancellationToken)
    {
        long downloadBytes = 0;
        long totalPackBytes = 0;
        var missingAssetCount = 0;
        foreach (var entry in DistinctAssets(catalog))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateAsset(entry);
            var expectedBytes = entry.ImageBytes!.Value;
            totalPackBytes += expectedBytes;
            var localPath = _storage.GetContentPath(entry.ImageFile);
            if (IsCompleteLocalAsset(localPath, expectedBytes)) continue;

            missingAssetCount++;
            var partialPath = _storage.GetPartialAssetPath(entry.ImageFile);
            var partialBytes = GetUsablePartialLength(partialPath, expectedBytes);
            downloadBytes += expectedBytes - partialBytes;
        }
        return (downloadBytes, totalPackBytes, missingAssetCount);
    }

    private static bool EntryChanged(PlaceEntry previous, PlaceEntry next) =>
        previous.Revision != next.Revision ||
        !string.Equals(previous.ImageSha256, next.ImageSha256, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(previous.Title, next.Title, StringComparison.Ordinal) ||
        !string.Equals(previous.ShortDescription, next.ShortDescription, StringComparison.Ordinal) ||
        !string.Equals(previous.Description, next.Description, StringComparison.Ordinal) ||
        previous.Latitude != next.Latitude || previous.Longitude != next.Longitude;

    private static bool IsCompleteLocalAsset(string path, long expectedBytes) =>
        File.Exists(path) && new FileInfo(path).Length == expectedBytes;

    private static long GetUsablePartialLength(string path, long expectedBytes)
    {
        if (!File.Exists(path)) return 0;
        var length = new FileInfo(path).Length;
        if (length >= 0 && length < expectedBytes) return length;
        try { File.Delete(path); } catch { }
        return 0;
    }

    private static void VerifyBytes(byte[] bytes, long? expectedBytes, string? expectedHash, string label)
    {
        if (expectedBytes is > 0 && bytes.LongLength != expectedBytes)
            throw new InvalidDataException($"Downloaded {label} has an unexpected size.");
        if (!string.IsNullOrWhiteSpace(expectedHash))
        {
            var actualHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Downloaded {label} failed SHA-256 verification.");
        }
    }

    private static Uri ResolveRelativeUri(Uri baseUri, string relativePath)
    {
        ValidateRelativePath(relativePath);
        return new Uri(baseUri, relativePath.Replace('\\', '/'));
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsafe content path: {path}");
    }

    private static bool TryGetBaseUri(string value, out Uri baseUri)
    {
        if (Uri.TryCreate(value.TrimEnd('/') + "/", UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
        {
            baseUri = uri;
            return true;
        }
        baseUri = null!;
        return false;
    }

    private static string BuildInstalledMessage(ContentUpdatePlan plan)
    {
        if (plan.AddedCount > 0 && plan.UpdatedCount > 0)
            return $"Content updated: {plan.AddedCount} new and {plan.UpdatedCount} updated places.";
        if (plan.AddedCount > 0) return $"Content updated: {plan.AddedCount} new places.";
        if (plan.UpdatedCount > 0) return $"Content updated: {plan.UpdatedCount} places refreshed.";
        return $"{ContentPacks.DisplayName(plan.PackId)} is ready.";
    }
}
