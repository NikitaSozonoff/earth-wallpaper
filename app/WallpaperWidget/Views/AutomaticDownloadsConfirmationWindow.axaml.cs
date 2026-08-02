using Avalonia.Controls;

namespace WallpaperWidget.Views;

public partial class AutomaticDownloadsConfirmationWindow : Window
{
    public AutomaticDownloadsConfirmationWindow()
    {
        InitializeComponent();
    }

    private void Confirm_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(true);
    private void Cancel_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(false);
}
