using Avalonia.Controls;
using WallpaperWidget.Models;

namespace WallpaperWidget.Views;

public partial class ContentPackWindow : Window
{
    public ContentPackWindow()
    {
        InitializeComponent();
    }

    public ContentPackWindow(IEnumerable<ContentPackSummary> summaries) : this()
    {
        foreach (var summary in summaries)
        {
            if (summary.PackId == ContentPacks.All) AllSizeText.Text = summary.DetailText;
            if (summary.PackId == ContentPacks.Aesthetic) AestheticSizeText.Text = summary.DetailText;
        }
    }

    private void All_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(ContentPacks.All);

    private void Aesthetic_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(ContentPacks.Aesthetic);
}
