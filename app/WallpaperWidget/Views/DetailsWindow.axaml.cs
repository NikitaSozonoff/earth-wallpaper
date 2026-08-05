using System.Diagnostics;
using Avalonia.Controls;
using WallpaperWidget.ViewModels;

namespace WallpaperWidget.Views;

public partial class DetailsWindow : Window
{
    private readonly MainViewModel _viewModel = null!;

    public DetailsWindow()
    {
        InitializeComponent();
    }

    public DetailsWindow(MainViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OpenSource_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_viewModel.HasExternalLink) return;
        Process.Start(new ProcessStartInfo(_viewModel.ExternalLinkUrl) { UseShellExecute = true });
    }

    private void Close_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
