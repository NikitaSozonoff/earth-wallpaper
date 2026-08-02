using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace WallpaperWidget.Views;

public partial class ContentNoticeWindow : Window
{
    private readonly DispatcherTimer _closeTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private PixelRect? _targetWorkingArea;
    private double _targetScaling = 1;

    public ContentNoticeWindow()
    {
        InitializeComponent();
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            Close();
        };
    }

    public ContentNoticeWindow(string title, string message, PixelRect? targetWorkingArea = null, double targetScaling = 1) : this()
    {
        TitleText.Text = title;
        MessageText.Text = message;
        _targetWorkingArea = targetWorkingArea;
        _targetScaling = Math.Max(0.5, targetScaling);
        Opened += (_, _) =>
        {
            var area = _targetWorkingArea ?? Screens.Primary?.WorkingArea;
            if (area is not null)
            {
                var width = (int)Math.Ceiling(ClientSize.Width * _targetScaling);
                var height = (int)Math.Ceiling(ClientSize.Height * _targetScaling);
                var margin = (int)Math.Ceiling(18 * _targetScaling);
                Position = new PixelPoint(
                    Math.Max(area.Value.X + margin, area.Value.Right - width - margin),
                    Math.Max(area.Value.Y + margin, area.Value.Bottom - height - margin));
            }
            _closeTimer.Start();
        };
    }

    private void Close_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _closeTimer.Stop();
        Close();
    }
}
