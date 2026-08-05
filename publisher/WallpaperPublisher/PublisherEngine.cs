using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WallpaperPublisher;

public sealed partial class PublisherEngine
{
    private readonly ResolvedPublisherConfig _config;
    private readonly PublisherLog _log;

    public PublisherEngine(ResolvedPublisherConfig config, PublisherLog log)
    {
        _config = config;
        _log = log;
    }

    public PublisherResult Run(string command)
    {
        var report = new PublisherReport { Command = command, StartedAtUtc = DateTimeOffset.UtcNow };
        try
        {
            ValidateInputs(report);
            if (report.Issues.Any(issue => issue.Severity == "error")) return Finish(report, false, null);

            var rows = new ExcelCatalogReader().Read(_config.WorkbookPath, _config.Worksheet);
            report.SourceRows = rows.Count;
            var selectedRows = _config.RequireReadyValidation
                ? rows.Where(row => row.Validation.Equals("Ready", StringComparison.OrdinalIgnoreCase)).ToArray()
                : rows.ToArray();

            ValidateRows(selectedRows, report);
            if (_config.RequireReadyValidation && selectedRows.Length == 0)
            {
                Add(report, "error", "no_ready_rows", "No rows have Validation = Ready.");
            }
            if (report.Issues.Any(issue => issue.Severity == "error")) return Finish(report, false, null);

            var entries = BuildEntries(selectedRows, report);
            report.PublishedEntries = entries.Count;
            if (report.Issues.Any(issue => issue.Severity == "error")) return Finish(report, false, null);

            if (command == "validate") return Finish(report, true, null);

            WriteAssets(entries, selectedRows);
            var allManifestPath = BuildPack("all", "manifest.json", entries, report);
            BuildPack(
                "aesthetic",
                "manifest-aesthetic.json",
                entries.Where(entry => entry.Aesthetics?.Equals("Cool", StringComparison.OrdinalIgnoreCase) == true).ToList(),
                report);

            report.ContentVersion = report.Packs.First(pack => pack.PackId == "all").ContentVersion;
            return Finish(report, true, allManifestPath);
        }
        catch (Exception exception)
        {
            Add(report, "error", "publisher_exception", exception.Message);
            _log.Error("publisher_exception", exception.Message, new { exception = exception.GetType().Name });
            return Finish(report, false, null);
        }
    }

    private string BuildPack(string packId, string manifestFileName, List<PublishedPlace> entries, PublisherReport report)
    {
        if (entries.Count == 0) throw new InvalidDataException($"Content pack '{packId}' contains no entries.");

        // Catalog order is user-visible, so reordering rows must produce a new content version.
        var canonicalEntries = JsonSerializer.SerializeToUtf8Bytes(new { PackId = packId, Entries = entries }, JsonDefaults.Canonical);
        var version = Convert.ToHexString(SHA256.HashData(canonicalEntries)).ToLowerInvariant()[..16];
        var catalog = new PublishedCatalog { PackId = packId, ContentVersion = version, Entries = entries };
        var catalogBytes = JsonSerializer.SerializeToUtf8Bytes(catalog, JsonDefaults.Write);
        var catalogHash = Convert.ToHexString(SHA256.HashData(catalogBytes)).ToLowerInvariant();
        var catalogRelativePath = $"catalogs/catalog-{version}.json";
        WriteAtomic(Path.Combine(_config.OutputPath, catalogRelativePath), catalogBytes);
        if (packId == "all" && !string.IsNullOrWhiteSpace(_config.BundledCatalogPath))
        {
            WriteAtomic(_config.BundledCatalogPath, catalogBytes);
            _log.Info("bundled_catalog_updated", $"Bundled application catalog updated: {_config.BundledCatalogPath}");
        }

        var manifest = new ContentManifest
        {
            PackId = packId,
            ContentVersion = version,
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            EntryCount = entries.Count,
            AssetCount = entries.Select(entry => entry.ImageFile).Distinct(StringComparer.Ordinal).Count(),
            DownloadBytes = catalogBytes.LongLength + entries.GroupBy(entry => entry.ImageFile).Sum(group => group.First().ImageBytes),
            Catalog = new ManifestFile
            {
                Path = catalogRelativePath.Replace('\\', '/'),
                Sha256 = catalogHash,
                Bytes = catalogBytes.LongLength,
            },
        };
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonDefaults.Write);
        var manifestPath = Path.Combine(_config.OutputPath, manifestFileName);
        WriteAtomic(manifestPath, manifestBytes);
        report.Packs.Add(new PublishedPackSummary(packId, entries.Count, manifest.DownloadBytes, version, manifestFileName));
        _log.Info("pack_built", $"Pack '{packId}' {version} is ready: {entries.Count} places, {manifest.DownloadBytes:N0} bytes.");
        return manifestPath;
    }

    private void ValidateInputs(PublisherReport report)
    {
        if (!File.Exists(_config.WorkbookPath)) Add(report, "error", "workbook_missing", $"Workbook not found: {_config.WorkbookPath}");
        if (!Directory.Exists(_config.SourceImagesPath)) Add(report, "error", "images_directory_missing", $"Images directory not found: {_config.SourceImagesPath}");
    }

    private void ValidateRows(IReadOnlyList<SourcePlace> rows, PublisherReport report)
    {
        foreach (var duplicate in rows.GroupBy(row => row.Id, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            Add(report, "error", "duplicate_id", $"Duplicate Place ID: {duplicate.Key}", duplicate.First());

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Title)) Add(report, "error", "title_missing", "Place title is required.", row);
            if (string.IsNullOrWhiteSpace(row.Country)) Add(report, "error", "country_missing", "Country is required.", row);
            if (row.Latitude is < -90 or > 90) Add(report, "error", "latitude_invalid", $"Latitude is outside -90..90: {row.Latitude}", row);
            if (row.Longitude is < -180 or > 180) Add(report, "error", "longitude_invalid", $"Longitude is outside -180..180: {row.Longitude}", row);
            if (string.IsNullOrWhiteSpace(row.ImageFile))
            {
                Add(report, "error", "image_filename_missing", "Image filename is required.", row);
                continue;
            }
            if (!string.Equals(Path.GetFileName(row.ImageFile), row.ImageFile, StringComparison.Ordinal))
                Add(report, "error", "image_path_invalid", "Image filename must not contain a directory path.", row);

            var imagePath = Path.Combine(_config.SourceImagesPath, row.ImageFile);
            if (!File.Exists(imagePath)) Add(report, "error", "image_missing", $"Image not found: {row.ImageFile}", row);
            else if (new FileInfo(imagePath).Length == 0) Add(report, "error", "image_empty", $"Image is empty: {row.ImageFile}", row);

            if (string.IsNullOrWhiteSpace(row.Description)) Add(report, "warning", "description_missing", "Full description is missing.", row);
            if (string.IsNullOrWhiteSpace(row.ShortDescription) && string.IsNullOrWhiteSpace(row.Description))
                Add(report, "warning", "short_description_missing", "Short description cannot be derived because the full description is also missing.", row);
            if (!string.IsNullOrWhiteSpace(row.ImageryDate) && !ImageryDatePattern().IsMatch(row.ImageryDate))
                Add(report, "warning", "imagery_date_format", $"Imagery date should use YYYY-MM: {row.ImageryDate}", row);
            if (!row.Validation.Equals("Ready", StringComparison.OrdinalIgnoreCase))
                Add(report, "warning", "row_not_ready", $"Validation is '{row.Validation}', not 'Ready'.", row);
        }
    }

    private List<PublishedPlace> BuildEntries(IReadOnlyList<SourcePlace> rows, PublisherReport report)
    {
        var entries = new List<PublishedPlace>(rows.Count);
        foreach (var row in rows)
        {
            var sourcePath = Path.Combine(_config.SourceImagesPath, row.ImageFile);
            if (!File.Exists(sourcePath)) continue;
            var hash = HashFile(sourcePath);
            var extension = Path.GetExtension(row.ImageFile).ToLowerInvariant();
            var assetPath = $"assets/{hash[..24]}{extension}";
            var shortDescription = string.IsNullOrWhiteSpace(row.ShortDescription)
                ? DeriveShortDescription(row.Description, _config.ShortDescriptionMaxLength)
                : row.ShortDescription;

            entries.Add(new PublishedPlace
            {
                Id = row.Id,
                Title = row.Title,
                Country = NullIfEmpty(row.Country),
                Region = NullIfEmpty(row.Region),
                ShortDescription = NullIfEmpty(shortDescription),
                Description = NullIfEmpty(row.Description),
                Latitude = row.Latitude,
                Longitude = row.Longitude,
                Zoom = row.Zoom,
                SourceUrl = NullIfEmpty(row.SourceUrl),
                ImageFile = assetPath,
                ImageSha256 = hash,
                ImageBytes = new FileInfo(sourcePath).Length,
                ImageryDate = NullIfEmpty(row.ImageryDate),
                DateStatus = NullIfEmpty(row.DateStatus),
                Attribution = NullIfEmpty(row.Attribution),
                Tags = NullIfEmpty(row.Tags),
                Aesthetics = NullIfEmpty(row.Aesthetics),
                Story = NullIfEmpty(row.Story),
                Revision = row.Revision,
            });
        }
        return entries;
    }

    private void WriteAssets(IReadOnlyList<PublishedPlace> entries, IReadOnlyList<SourcePlace> rows)
    {
        var sourceById = rows.ToDictionary(row => row.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.GroupBy(entry => entry.ImageFile).Select(group => group.First()))
        {
            var source = Path.Combine(_config.SourceImagesPath, sourceById[entry.Id].ImageFile);
            var destination = Path.Combine(_config.OutputPath, entry.ImageFile.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(destination) && HashFile(destination).Equals(entry.ImageSha256, StringComparison.OrdinalIgnoreCase)) continue;
            CopyAtomic(source, destination);
        }
    }

    private PublisherResult Finish(PublisherReport report, bool success, string? manifestPath)
    {
        report.Success = success;
        report.FinishedAtUtc = DateTimeOffset.UtcNow;
        WriteReports(report);
        var errors = report.Issues.Count(issue => issue.Severity == "error");
        var warnings = report.Issues.Count(issue => issue.Severity == "warning");
        _log.Info("run_finished", $"Publisher finished: {errors} errors, {warnings} warnings.", new { report.Success, report.ContentVersion });
        return new PublisherResult(success, report, manifestPath);
    }

    private void WriteReports(PublisherReport report)
    {
        var reportsPath = Path.Combine(_config.StatePath, "reports");
        Directory.CreateDirectory(reportsPath);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report, JsonDefaults.Write);
        WriteAtomic(Path.Combine(reportsPath, "latest-validation.json"), bytes);
        WriteAtomic(Path.Combine(reportsPath, $"run-{report.StartedAtUtc:yyyyMMdd-HHmmss-fff}.json"), bytes);
    }

    private void Add(PublisherReport report, string severity, string code, string message, SourcePlace? row = null)
    {
        report.Issues.Add(new ValidationIssue(severity, code, message, row?.RowNumber, row?.Id));
        if (severity == "error") _log.Error(code, message, row is null ? null : new { row.RowNumber, row.Id });
        else _log.Warning(code, message, row is null ? null : new { row.RowNumber, row.Id });
    }

    private static string DeriveShortDescription(string description, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(description)) return string.Empty;
        var singleLine = WhitespacePattern().Replace(description, " ").Trim();
        if (singleLine.Length <= maxLength) return singleLine;
        var cut = singleLine.LastIndexOf(' ', maxLength);
        if (cut < maxLength / 2) cut = maxLength;
        return singleLine[..cut].TrimEnd(' ', '.', ',', ';', ':') + "…";
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void CopyAtomic(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        File.Copy(source, temporary, true);
        File.Move(temporary, destination, true);
    }

    private static void WriteAtomic(string destination, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(temporary, bytes);
        File.Move(temporary, destination, true);
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    [GeneratedRegex("^\\d{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex ImageryDatePattern();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespacePattern();
}
