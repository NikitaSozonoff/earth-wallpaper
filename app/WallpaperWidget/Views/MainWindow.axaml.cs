using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WallpaperWidget.ViewModels;

namespace WallpaperWidget.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = null!;
    private bool _positionReady;
    private bool _adjustingPosition;
    private bool _anchoredRight;
    private double _lastPixelWidth;
    private PixelRect? _anchorWorkingArea;
    private double _anchorScaling = 1;
    private bool _syncingUpdateMode;
    public bool AllowClose { get; set; }
    public event EventHandler? WidgetVisibilityChanged;
    public event EventHandler? ContentPackChangeRequested;
    public event EventHandler? ContentUpdateCheckRequested;
    public event EventHandler? ContentUpdateInstallRequested;
    public event EventHandler? ApplicationUpdateCheckRequested;
    public event Func<int, Task>? ContentUpdateModeChangeRequested;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        SyncContentUpdateModeSelection();

        Opened += (_, _) =>
        {
            RestorePosition();
            _positionReady = true;
            UpdateHorizontalAnchor();
            _lastPixelWidth = ClientSize.Width * _anchorScaling;
            ClampToAnchorScreen();
            WidgetVisibilityChanged?.Invoke(this, EventArgs.Empty);
        };
        PositionChanged += (_, _) =>
        {
            if (!_positionReady || _adjustingPosition) return;
            UpdateHorizontalAnchor();
            _viewModel.SetWindowPosition(Position.X, Position.Y);
        };
        Resized += (_, args) => HandleResize(args.ClientSize);
        Closing += (_, args) =>
        {
            if (AllowClose) return;
            args.Cancel = true;
            HideToTray();
        };
    }

    private void RestorePosition()
    {
        if (_viewModel.Settings.WindowX is int x && _viewModel.Settings.WindowY is int y)
        {
            Position = new PixelPoint(x, y);
            return;
        }

        var workingArea = Screens.Primary?.WorkingArea;
        if (workingArea is null) return;
        Position = new PixelPoint(workingArea.Value.Right - 500, workingArea.Value.Y + 36);
    }

    private void Previous_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _viewModel.Previous();
    private void Next_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _viewModel.Next();
    private void Details_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        new DetailsWindow(_viewModel).Show();
    }

    private void FullLayout_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _viewModel.UseFullLayout();
    private void TitleLayout_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _viewModel.UseTitleLayout();
    private void ControlsLayout_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _viewModel.UseControlsLayout();
    private void HideWidget_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => HideToTray();
    private void ChangeContentPack_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ContentPackChangeRequested?.Invoke(this, EventArgs.Empty);
    private void CheckContentUpdates_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ContentUpdateCheckRequested?.Invoke(this, EventArgs.Empty);
    private void InstallContentUpdate_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ContentUpdateInstallRequested?.Invoke(this, EventArgs.Empty);
    private void CheckApplicationUpdates_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ApplicationUpdateCheckRequested?.Invoke(this, EventArgs.Empty);

    private async void ContentUpdateMode_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingUpdateMode || sender is not ComboBox comboBox || comboBox.SelectedIndex < 0) return;
        var handler = ContentUpdateModeChangeRequested;
        if (handler is not null) await handler(comboBox.SelectedIndex);
    }

    public void SyncContentUpdateModeSelection()
    {
        if (_viewModel is null) return;
        _syncingUpdateMode = true;
        ContentUpdateModeCombo.SelectedIndex = _viewModel.ContentUpdateModeIndex;
        _syncingUpdateMode = false;
    }

    private void WidgetCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel.PositionLocked || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var current = e.Source as Visual;
        while (current is not null && current != this)
        {
            if (current is Button or Slider or CheckBox or ComboBox or SelectableTextBlock) return;
            current = current.GetVisualParent();
        }

        BeginMoveDrag(e);
    }

    public void HideToTray()
    {
        Hide();
        WidgetVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ShowFromTray()
    {
        BringToFrontFromTray();
    }

    public void BringToFrontFromTray()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Topmost = true;
        Activate();
        Dispatcher.UIThread.Post(() =>
        {
            Topmost = false;
            Activate();
        }, DispatcherPriority.Background);
        WidgetVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void HandleResize(Size clientSize)
    {
        if (!_positionReady) return;

        var area = _anchorWorkingArea;
        if (area is null)
        {
            UpdateHorizontalAnchor();
            area = _anchorWorkingArea;
        }
        if (area is null) return;

        var newPixelWidth = clientSize.Width * _anchorScaling;
        var newPixelHeight = clientSize.Height * _anchorScaling;
        if (_lastPixelWidth <= 0) _lastPixelWidth = newPixelWidth;

        var x = _anchoredRight
            ? (int)Math.Round(Position.X + _lastPixelWidth - newPixelWidth)
            : Position.X;
        var y = Position.Y;
        var margin = 8;
        var minX = area.Value.X + margin;
        var maxX = Math.Max(minX, area.Value.Right - (int)Math.Ceiling(newPixelWidth) - margin);
        var minY = area.Value.Y + margin;
        var maxY = Math.Max(minY, area.Value.Bottom - (int)Math.Ceiling(newPixelHeight) - margin);

        SetPositionSafely(new PixelPoint(Math.Clamp(x, minX, maxX), Math.Clamp(y, minY, maxY)));
        _lastPixelWidth = newPixelWidth;
    }

    private void UpdateHorizontalAnchor()
    {
        var center = new PixelPoint(
            Position.X + (int)Math.Round(Math.Max(1, ClientSize.Width * _anchorScaling) / 2),
            Position.Y + (int)Math.Round(Math.Max(1, ClientSize.Height * _anchorScaling) / 2));
        var screen = Screens.ScreenFromPoint(center) ?? Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;

        _anchorWorkingArea = screen.WorkingArea;
        _anchorScaling = screen.Scaling;
        var actualCenterX = Position.X + (ClientSize.Width * _anchorScaling / 2);
        _anchoredRight = actualCenterX >= screen.WorkingArea.X + (screen.WorkingArea.Width / 2d);
    }

    private void ClampToAnchorScreen()
    {
        if (_anchorWorkingArea is not PixelRect area) return;

        var width = ClientSize.Width * _anchorScaling;
        var height = ClientSize.Height * _anchorScaling;
        var margin = 8;
        var minX = area.X + margin;
        var maxX = Math.Max(minX, area.Right - (int)Math.Ceiling(width) - margin);
        var minY = area.Y + margin;
        var maxY = Math.Max(minY, area.Bottom - (int)Math.Ceiling(height) - margin);
        SetPositionSafely(new PixelPoint(
            Math.Clamp(Position.X, minX, maxX),
            Math.Clamp(Position.Y, minY, maxY)));
    }

    private void SetPositionSafely(PixelPoint position)
    {
        if (Position == position) return;
        _adjustingPosition = true;
        Position = position;
        _adjustingPosition = false;
        _viewModel.SetWindowPosition(position.X, position.Y);
    }
}
