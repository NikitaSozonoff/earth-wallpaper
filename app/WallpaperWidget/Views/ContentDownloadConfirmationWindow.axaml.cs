using Avalonia.Controls;
using WallpaperWidget.Models;

namespace WallpaperWidget.Views;

public partial class ContentDownloadConfirmationWindow : Window
{
    public ContentDownloadConfirmationWindow()
    {
        InitializeComponent();
    }

    public ContentDownloadConfirmationWindow(ContentUpdatePlan plan) : this()
    {
        var collection = ContentPacks.DisplayName(plan.PackId);
        HeadingText.Text = plan.MissingAssetCount > 0 && plan.ChangedPlaceCount == 0
            ? "Restore missing wallpaper content?"
            : plan.PreviousVersion is null ? $"Download {collection}?" : $"Update {collection}?";
        SizeText.Text = plan.DownloadBytes > 0
            ? $"{ContentSizeFormatter.Format(plan.DownloadBytes)} additional download"
            : "No additional image download";
        PlacesText.Text = BuildPlacesText(plan);
        ExplanationText.Text = plan.DownloadBytes > 0
            ? $"About {ContentSizeFormatter.Format(plan.DownloadBytes)} will be transferred and stored on this computer. This uses your internet connection and disk space."
            : "All required images are already in the local cache. Only the selected catalog will be activated.";
        ConfirmButton.Content = plan.DownloadBytes > 0
            ? $"Download {ContentSizeFormatter.Format(plan.DownloadBytes)}"
            : "Use collection";
    }

    private static string BuildPlacesText(ContentUpdatePlan plan)
    {
        if (plan.MissingAssetCount > 0 && plan.ChangedPlaceCount == 0)
        {
            var noun = plan.MissingAssetCount == 1 ? "image" : "images";
            return $"{plan.MissingAssetCount} missing {noun} will be restored · {plan.Manifest.EntryCount} places total";
        }
        if (plan.PreviousVersion is null) return $"{plan.Manifest.EntryCount} places in this collection";
        var changes = new List<string>();
        if (plan.AddedCount > 0) changes.Add($"{plan.AddedCount} new");
        if (plan.UpdatedCount > 0) changes.Add($"{plan.UpdatedCount} updated");
        if (plan.RemovedCount > 0) changes.Add($"{plan.RemovedCount} removed");
        return changes.Count == 0
            ? $"{plan.Manifest.EntryCount} places in this collection"
            : string.Join(" · ", changes) + $" · {plan.Manifest.EntryCount} total";
    }

    private void Confirm_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(true);
    private void Cancel_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(false);
}
