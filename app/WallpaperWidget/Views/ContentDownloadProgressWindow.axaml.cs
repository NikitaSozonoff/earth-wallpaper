using Avalonia.Controls;
using WallpaperWidget.Models;

namespace WallpaperWidget.Views;

public partial class ContentDownloadProgressWindow : Window
{
    private bool _allowClose;

    public ContentDownloadProgressWindow()
    {
        InitializeComponent();
        Closing += (_, eventArgs) =>
        {
            if (!_allowClose) eventArgs.Cancel = true;
        };
    }

    public ContentDownloadProgressWindow(ContentUpdatePlan plan) : this()
    {
        HeadingText.Text = ContentPacks.DisplayName(plan.PackId);
        AssetsText.Text = $"Preparing {plan.MissingAssetCount} images…";
        PercentText.Text = "0%";
        BytesText.Text = plan.DownloadBytes > 0
            ? $"0 B of {ContentSizeFormatter.Format(plan.DownloadBytes)}"
            : "Preparing local content…";
    }

    public void Report(ContentDownloadProgress progress)
    {
        var ratio = progress.TotalBytes > 0
            ? Math.Clamp(progress.DownloadedBytes / (double)progress.TotalBytes, 0, 1)
            : progress.TotalAssets > 0
                ? Math.Clamp(progress.CompletedAssets / (double)progress.TotalAssets, 0, 1)
                : 0;
        DownloadProgressBar.Value = ratio * 100;
        AssetsText.Text = $"{progress.CompletedAssets} of {progress.TotalAssets} images";
        PercentText.Text = $"{ratio:P0}";
        BytesText.Text = progress.TotalBytes > 0
            ? $"{ContentSizeFormatter.Format(progress.DownloadedBytes)} of {ContentSizeFormatter.Format(progress.TotalBytes)}"
            : "Verifying downloaded content…";
    }

    public void ShowFinishing()
    {
        DownloadProgressBar.Value = 100;
        PercentText.Text = "100%";
        BytesText.Text = "Verifying and activating the collection…";
    }

    public void FinishAndClose()
    {
        _allowClose = true;
        Close();
    }
}
