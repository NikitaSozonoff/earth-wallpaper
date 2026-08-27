using System.Diagnostics;
using Avalonia.Controls;
using WallpaperWidget.Models;
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
        if (sender is not Button { DataContext: SourceLink source } || !Uri.TryCreate(source.Url, UriKind.Absolute, out _)) return;
        Process.Start(new ProcessStartInfo(source.Url) { UseShellExecute = true });
    }

    private void OpenLocation_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_viewModel.HasLocationLink) return;
        Process.Start(new ProcessStartInfo(_viewModel.LocationLinkUrl) { UseShellExecute = true });
    }

    private void Close_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
