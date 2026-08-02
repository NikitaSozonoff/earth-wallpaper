using System.Diagnostics;
using Avalonia.Controls;
using WallpaperWidget.Models;
using WallpaperWidget.Services;

namespace WallpaperWidget.Views;

public partial class ApplicationUpdateWindow : Window
{
    private readonly ApplicationUpdateInfo _update = null!;

    public ApplicationUpdateWindow()
    {
        InitializeComponent();
    }

    public ApplicationUpdateWindow(ApplicationUpdateInfo update) : this()
    {
        _update = update;
        VersionText.Text = $"Version {update.Version} is available";
        ReleaseNameText.Text = update.ReleaseName;
        ReleaseNotesText.Text = BuildReleaseNotes(update.ReleaseNotes);
        ChannelText.Text = update.IsPrerelease ? "Beta release" : "Stable release";
    }

    private void Later_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(false);

    private void OpenInstaller_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var url = _update.InstallerDownloadUrl ?? _update.ReleasePageUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            AppLog.Info("application_update_opened", "The application update download page was opened.", new { _update.Version });
            Close(true);
        }
        catch (Exception exception)
        {
            AppLog.Warning("application_update_open_failed", "The application update page could not be opened.", new { exception = exception.GetType().Name });
        }
    }

    private static string BuildReleaseNotes(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return "This release includes application improvements and fixes.";
        var normalized = notes.Trim();
        return normalized.Length <= 2400 ? normalized : normalized[..2400].TrimEnd() + "…";
    }
}
