using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using WallpaperWidget.Models;

namespace WallpaperWidget.Services;

public sealed class ApplicationUpdateService
{
    private const string ReleasesUrl = "https://api.github.com/repos/NikitaSozonoff/earth-wallpaper/releases?per_page=20";
    private readonly HttpClient _httpClient;
    private readonly string _currentVersionDisplay;
    private readonly SemanticVersion _currentVersion;

    public ApplicationUpdateService(HttpMessageHandler? handler = null, string? currentVersion = null)
    {
        _currentVersionDisplay = currentVersion ?? ApplicationVersion.Display;
        _currentVersion = SemanticVersion.TryParse(_currentVersionDisplay, out var parsedCurrent)
            ? parsedCurrent
            : new SemanticVersion(0, 0, 0, null);
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler, true);
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EarthWallpaper", SanitizeProductVersion(ApplicationVersion.Display)));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<ApplicationUpdateCheckResult> CheckAsync(bool includePrereleases = true, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(ReleasesUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var releases = await JsonSerializer.DeserializeAsync<List<GitHubRelease>>(stream, cancellationToken: cancellationToken) ?? [];

        ApplicationUpdateInfo? newest = null;
        SemanticVersion? newestVersion = null;
        foreach (var release in releases.Where(item => !item.Draft && (includePrereleases || !item.Prerelease)))
        {
            if (!SemanticVersion.TryParse(release.TagName, out var parsed) || parsed.CompareTo(_currentVersion) <= 0) continue;
            if (newestVersion is not null && parsed.CompareTo(newestVersion.Value) <= 0) continue;

            var setup = release.Assets.FirstOrDefault(asset =>
                asset.Name.StartsWith("EarthWallpaper-Setup-", StringComparison.OrdinalIgnoreCase) &&
                asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            newestVersion = parsed;
            newest = new ApplicationUpdateInfo(
                parsed.ToString(),
                string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
                release.HtmlUrl,
                setup?.BrowserDownloadUrl,
                release.Body ?? string.Empty,
                release.Prerelease);
        }

        return new ApplicationUpdateCheckResult(_currentVersionDisplay, newest);
    }

    private static string SanitizeProductVersion(string value)
    {
        var sanitized = new string(value.Select(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' ? character : '-').ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "0.0.0" : sanitized;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; init; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
        [JsonPropertyName("html_url")] public string HtmlUrl { get; init; } = string.Empty;
        [JsonPropertyName("body")] public string? Body { get; init; }
        [JsonPropertyName("draft")] public bool Draft { get; init; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; init; }
        [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; init; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; init; } = string.Empty;
    }
}
