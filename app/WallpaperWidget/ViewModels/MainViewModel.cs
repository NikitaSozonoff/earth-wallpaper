using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System.Globalization;
using WallpaperWidget.Models;
using WallpaperWidget.Services;

namespace WallpaperWidget.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private IReadOnlyList<PlaceEntry> _entries;
    private readonly CatalogService _catalogService;
    private readonly SettingsService _settingsService;
    private readonly IWallpaperService _wallpaperService;
    private readonly AutostartService _autostartService;
    private readonly WidgetSettings _settings;
    private readonly DispatcherTimer _rotationTimer;
    private static readonly int[] RotationMinuteValues = [1, 15, 30, 60, 360, 1440];
    private int _currentIndex;
    private string _statusMessage = "Use the arrows to set a wallpaper.";
    private string _contentUpdateStatusText = "Content has not been checked yet.";
    private bool _hasPendingContentUpdate;
    private bool _isContentUpdateBusy;
    private string _applicationUpdateStatusText = $"Version {ApplicationVersion.Display}";
    private bool _isApplicationUpdateBusy;

    public MainViewModel(
        IReadOnlyList<PlaceEntry> entries,
        CatalogService catalogService,
        SettingsService settingsService,
        IWallpaperService wallpaperService,
        AutostartService autostartService,
        WidgetSettings settings)
    {
        if (entries.Count == 0) throw new InvalidOperationException("The catalog contains no usable places.");
        _catalogService = catalogService;
        _settingsService = settingsService;
        _wallpaperService = wallpaperService;
        _autostartService = autostartService;
        _settings = settings;
        _settings.LaunchAtStartup = autostartService.IsEnabled();
        var previousPlaceId = !string.IsNullOrWhiteSpace(settings.CurrentPlaceId)
            ? settings.CurrentPlaceId
            : settings.CurrentIndex > 0
                ? entries[Math.Clamp(settings.CurrentIndex, 0, entries.Count - 1)].Id
                : null;
        _entries = ShuffleEntries(entries, preferredFirstId: previousPlaceId);
        _currentIndex = 0;
        _settings.CurrentIndex = 0;
        _settings.CurrentPlaceId = CurrentPlace.Id;
        _rotationTimer = new DispatcherTimer();
        _rotationTimer.Tick += (_, _) => Next();
        UpdateRotationTimer();
        if (settings.LastContentCheckUtc is not null)
        {
            _contentUpdateStatusText = $"Last checked {settings.LastContentCheckUtc.Value.ToLocalTime():g}";
        }
        Save();
    }

    public PlaceEntry CurrentPlace => _entries[_currentIndex];
    public string CurrentTitle => CurrentPlace.Title;
    public string LocationLine => string.Join(" · ", new[] { CurrentPlace.Country, CurrentPlace.Region }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public bool HasShortDescription => !string.IsNullOrWhiteSpace(CurrentPlace.ShortDescription);
    public string CurrentShortDescription => HasShortDescription
        ? CurrentPlace.ShortDescription!.Trim()
        : !string.IsNullOrWhiteSpace(CurrentPlace.Description)
            ? CurrentPlace.Description.Trim()
            : "Explore the landscape and story behind this place.";
    public string CurrentDescription => CurrentPlace.Description ?? "A detailed description has not been added yet.";
    public string CoordinateLine => $"{CurrentPlace.Latitude:0.000000}, {CurrentPlace.Longitude:0.000000}";
    public string ImageryLine => string.IsNullOrWhiteSpace(CurrentPlace.ImageryDate)
        ? "Imagery date not recorded"
        : $"Imagery: {CurrentPlace.ImageryDate}";
    public IReadOnlyList<SourceLink> CurrentSources
    {
        get
        {
            var sources = CurrentPlace.Sources?
                .Where(source => !string.IsNullOrWhiteSpace(source.Label) && Uri.TryCreate(source.Url, UriKind.Absolute, out _))
                .ToArray() ?? [];
            if (sources.Length > 0) return sources;
            return Uri.TryCreate(CurrentPlace.SourceUrl, UriKind.Absolute, out _)
                ? [new SourceLink { Label = "Source", Url = CurrentPlace.SourceUrl! }]
                : [];
        }
    }
    public bool HasSources => CurrentSources.Count > 0;
    public string LocationLinkUrl =>
        $"https://www.google.com/maps/search/?api=1&query={Uri.EscapeDataString($"{CurrentPlace.Latitude.ToString("R", CultureInfo.InvariantCulture)},{CurrentPlace.Longitude.ToString("R", CultureInfo.InvariantCulture)}")}";
    public bool HasLocationLink =>
        CurrentPlace.Latitude is >= -90 and <= 90 && CurrentPlace.Longitude is >= -180 and <= 180;
    public bool HasDescription => !string.IsNullOrWhiteSpace(CurrentPlace.Description);
    public string CounterText => $"{_currentIndex + 1} / {_entries.Count}";

    public bool ShowLocation
    {
        get => _settings.ShowLocation;
        set
        {
            if (_settings.ShowLocation == value) return;
            _settings.ShowLocation = value;
            OnPropertyChanged();
            NotifyLayoutChanged();
            Save();
        }
    }

    public bool ShowTitle
    {
        get => _settings.ShowTitle;
        set
        {
            if (_settings.ShowTitle == value) return;
            _settings.ShowTitle = value;
            OnPropertyChanged();
            NotifyLayoutChanged();
            Save();
        }
    }

    public bool ShowShortDescription
    {
        get => _settings.ShowShortDescription;
        set
        {
            if (_settings.ShowShortDescription == value) return;
            _settings.ShowShortDescription = value;
            OnPropertyChanged();
            NotifyLayoutChanged();
            Save();
        }
    }

    public bool ShowNavigationControls
    {
        get => _settings.ShowNavigationControls;
        set
        {
            if (_settings.ShowNavigationControls == value) return;
            _settings.ShowNavigationControls = value;
            OnPropertyChanged();
            NotifyLayoutChanged();
            Save();
        }
    }

    public bool PositionLocked
    {
        get => _settings.PositionLocked;
        set
        {
            if (_settings.PositionLocked == value) return;
            _settings.PositionLocked = value;
            OnPropertyChanged();
            Save();
        }
    }

    public bool AutoRotateEnabled
    {
        get => _settings.AutoRotateEnabled;
        set
        {
            if (_settings.AutoRotateEnabled == value) return;
            _settings.AutoRotateEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RotationStatusText));
            UpdateRotationTimer();
            Save();
        }
    }

    public bool LaunchAtStartup
    {
        get => _settings.LaunchAtStartup;
        set
        {
            if (_settings.LaunchAtStartup == value) return;
            try
            {
                _autostartService.SetEnabled(value);
                _settings.LaunchAtStartup = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AutostartStatusText));
                Save();
                AppLog.Info("autostart_changed", value ? "Start at login enabled." : "Start at login disabled.");
            }
            catch (Exception exception)
            {
                AppLog.Warning("autostart_change_failed", "The startup setting could not be changed.", new { exception = exception.GetType().Name });
                OnPropertyChanged();
                OnPropertyChanged(nameof(AutostartStatusText));
            }
        }
    }

    public int RotationIntervalIndex
    {
        get
        {
            var index = Array.IndexOf(RotationMinuteValues, _settings.RotationMinutes);
            return index < 0 ? RotationMinuteValues.Length - 1 : index;
        }
        set
        {
            var normalized = Math.Clamp(value, 0, RotationMinuteValues.Length - 1);
            var minutes = RotationMinuteValues[normalized];
            if (_settings.RotationMinutes == minutes) return;
            _settings.RotationMinutes = minutes;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RotationStatusText));
            UpdateRotationTimer();
            Save();
        }
    }

    public double WidgetScale
    {
        get => _settings.WidgetScale;
        set
        {
            var normalized = Math.Round(Math.Clamp(value, 0.75, 1.55), 2);
            if (Math.Abs(_settings.WidgetScale - normalized) < 0.001) return;
            _settings.WidgetScale = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WidgetWidth));
            OnPropertyChanged(nameof(ScaleLabel));
            Save();
        }
    }

    public double PanelOpacity
    {
        get => _settings.PanelOpacity;
        set
        {
            var normalized = Math.Round(Math.Clamp(value, 0.62, 0.96), 2);
            if (Math.Abs(_settings.PanelOpacity - normalized) < 0.001) return;
            _settings.PanelOpacity = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PanelBrush));
            OnPropertyChanged(nameof(OpacityLabel));
            Save();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ContentUpdateStatusText
    {
        get => _contentUpdateStatusText;
        private set => SetProperty(ref _contentUpdateStatusText, value);
    }

    public bool HasPendingContentUpdate
    {
        get => _hasPendingContentUpdate;
        private set
        {
            if (SetProperty(ref _hasPendingContentUpdate, value))
            {
                OnPropertyChanged(nameof(PendingUpdateButtonVisible));
            }
        }
    }

    public bool IsContentUpdateBusy
    {
        get => _isContentUpdateBusy;
        private set
        {
            if (SetProperty(ref _isContentUpdateBusy, value))
            {
                OnPropertyChanged(nameof(ContentUpdateControlsEnabled));
            }
        }
    }

    public string ApplicationUpdateStatusText
    {
        get => _applicationUpdateStatusText;
        private set => SetProperty(ref _applicationUpdateStatusText, value);
    }

    public bool IsApplicationUpdateBusy
    {
        get => _isApplicationUpdateBusy;
        private set
        {
            if (SetProperty(ref _isApplicationUpdateBusy, value))
                OnPropertyChanged(nameof(ApplicationUpdateControlsEnabled));
        }
    }

    public bool ControlsOnly => !ShowLocation && !ShowTitle && !ShowShortDescription;
    public bool LocationVisible => ShowLocation && !string.IsNullOrWhiteSpace(LocationLine);
    public bool TitleVisible => ShowTitle;
    public bool DescriptionVisible => ShowShortDescription;
    public bool CuratedShortDescriptionVisible => DescriptionVisible && HasShortDescription;
    public bool DescriptionFallbackVisible => DescriptionVisible && !HasShortDescription;
    public bool NavigationVisible => ShowNavigationControls;
    public HorizontalAlignment ControlsAlignment => ControlsOnly
        ? HorizontalAlignment.Center
        : HorizontalAlignment.Right;
    public double BasePanelWidth
    {
        get
        {
            const double controlsWidth = 110;
            if (ControlsOnly) return controlsWidth;
            if (DescriptionVisible) return 420;
            if (TitleVisible) return Math.Max(360, controlsWidth);
            if (LocationVisible) return Math.Max(320, controlsWidth);
            return controlsWidth;
        }
    }
    public double WidgetWidth => BasePanelWidth * WidgetScale;
    public string ScaleLabel => $"{WidgetScale:P0}";
    public string OpacityLabel => $"{PanelOpacity:P0}";
    public IReadOnlyList<string> RotationIntervalOptions { get; } = ["1 minute (test)", "15 minutes", "30 minutes", "1 hour", "6 hours", "Every day"];
    public string RotationStatusText => AutoRotateEnabled
        ? $"Automatic · {RotationIntervalOptions[RotationIntervalIndex]}"
        : "Automatic rotation is off";
    public string ContentPackLabel => ContentPacks.DisplayName(_settings.ContentPackId);
    public IReadOnlyList<string> ContentUpdateModeOptions { get; } =
    [
        "Notify automatically",
        "Download automatically",
        "Manual only",
    ];
    public int ContentUpdateModeIndex => (int)_settings.ContentUpdateMode;
    public bool PendingUpdateButtonVisible => HasPendingContentUpdate;
    public bool ContentUpdateControlsEnabled => !IsContentUpdateBusy;
    public bool ApplicationUpdateControlsEnabled => !IsApplicationUpdateBusy;
    public bool AutostartAvailable => _autostartService.IsSupported;
    public string AutostartStatusText => AutostartAvailable
        ? LaunchAtStartup ? "Starts hidden in the notification area." : "Disabled."
        : OperatingSystem.IsMacOS()
            ? "Startup registration is not included in the first macOS beta."
            : "Available in the installed Windows application.";
    public IBrush PanelBrush => new SolidColorBrush(Color.FromArgb((byte)(PanelOpacity * 255), 22, 25, 31));
    public IBrush PanelBorderBrush => new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
    public Thickness PanelBorderThickness => new(1);
    public WidgetSettings Settings => _settings;

    public void Next()
    {
        if (_currentIndex >= _entries.Count - 1)
        {
            var previousId = CurrentPlace.Id;
            _entries = ShuffleEntries(_entries, avoidFirstId: previousId);
            _currentIndex = 0;
        }
        else
        {
            _currentIndex++;
        }
        ChangeCurrentPlace();
        ApplyCurrentWallpaper();
    }

    public void Previous()
    {
        _currentIndex = (_currentIndex - 1 + _entries.Count) % _entries.Count;
        ChangeCurrentPlace();
        ApplyCurrentWallpaper();
    }

    public void ApplyCurrentWallpaper()
    {
        var imagePath = _catalogService.ResolveImagePath(CurrentPlace);
        StatusMessage = _wallpaperService.TrySet(imagePath, out var message) ? message : $"Could not update wallpaper: {message}";
    }

    public void UseFullLayout()
    {
        ShowLocation = true;
        ShowTitle = true;
        ShowShortDescription = true;
    }

    public void UseTitleLayout()
    {
        ShowLocation = true;
        ShowTitle = true;
        ShowShortDescription = false;
    }

    public void UseControlsLayout()
    {
        ShowLocation = false;
        ShowTitle = false;
        ShowShortDescription = false;
    }

    public void SetWindowPosition(int x, int y)
    {
        _settings.WindowX = x;
        _settings.WindowY = y;
        Save();
    }

    public void SetContentPack(string packId)
    {
        if (!ContentPacks.IsValid(packId)) throw new ArgumentOutOfRangeException(nameof(packId));
        if (_settings.ContentPackId == packId) return;
        _settings.ContentPackId = packId;
        _settings.CurrentIndex = 0;
        _settings.CurrentPlaceId = null;
        OnPropertyChanged(nameof(ContentPackLabel));
        Save();
    }

    public void SetContentUpdateMode(ContentUpdateMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (_settings.ContentUpdateMode == mode) return;
        _settings.ContentUpdateMode = mode;
        OnPropertyChanged(nameof(ContentUpdateModeIndex));
        Save();
    }

    public void MarkContentCheckAttempt()
    {
        _settings.LastContentCheckAttemptUtc = DateTimeOffset.UtcNow;
        Save();
    }

    public void MarkContentCheckSucceeded()
    {
        _settings.LastContentCheckUtc = DateTimeOffset.UtcNow;
        _settings.LastContentCheckAttemptUtc = _settings.LastContentCheckUtc;
        Save();
    }

    public void MarkApplicationUpdateCheckSucceeded()
    {
        _settings.LastApplicationUpdateCheckUtc = DateTimeOffset.UtcNow;
        Save();
    }

    public void SetApplicationUpdateState(string message, bool isBusy = false)
    {
        ApplicationUpdateStatusText = message;
        IsApplicationUpdateBusy = isBusy;
    }

    public void SetContentUpdateState(string message, bool hasPendingUpdate, bool isBusy = false)
    {
        ContentUpdateStatusText = message;
        HasPendingContentUpdate = hasPendingUpdate;
        IsContentUpdateBusy = isBusy;
    }

    public void SetContentUpdateProgress(ContentDownloadProgress progress)
    {
        IsContentUpdateBusy = true;
        HasPendingContentUpdate = false;
        ContentUpdateStatusText = progress.TotalAssets > 0
            ? $"Downloading content… {progress.CompletedAssets} of {progress.TotalAssets}"
            : "Downloading content…";
    }

    public void ReplaceEntries(IReadOnlyList<PlaceEntry> entries, string statusMessage)
    {
        if (entries.Count == 0) return;
        var currentId = CurrentPlace.Id;
        var preferredFirstId = entries.Any(entry => entry.Id.Equals(currentId, StringComparison.OrdinalIgnoreCase))
            ? currentId
            : null;
        _entries = ShuffleEntries(entries, preferredFirstId: preferredFirstId);
        _currentIndex = 0;
        _settings.CurrentIndex = 0;
        _settings.CurrentPlaceId = CurrentPlace.Id;
        NotifyCurrentPlaceChanged();
        StatusMessage = statusMessage;
        Save();
    }

    private void ChangeCurrentPlace()
    {
        _settings.CurrentIndex = _currentIndex;
        _settings.CurrentPlaceId = CurrentPlace.Id;
        NotifyCurrentPlaceChanged();
        Save();
    }

    private static IReadOnlyList<PlaceEntry> ShuffleEntries(
        IReadOnlyList<PlaceEntry> entries,
        string? preferredFirstId = null,
        string? avoidFirstId = null)
    {
        var shuffled = entries.ToArray();
        Random.Shared.Shuffle(shuffled);

        if (!string.IsNullOrWhiteSpace(preferredFirstId))
        {
            var preferredIndex = Array.FindIndex(shuffled,
                entry => entry.Id.Equals(preferredFirstId, StringComparison.OrdinalIgnoreCase));
            if (preferredIndex > 0)
                (shuffled[0], shuffled[preferredIndex]) = (shuffled[preferredIndex], shuffled[0]);
        }

        if (shuffled.Length > 1 && !string.IsNullOrWhiteSpace(avoidFirstId) &&
            shuffled[0].Id.Equals(avoidFirstId, StringComparison.OrdinalIgnoreCase))
        {
            var swapIndex = Random.Shared.Next(1, shuffled.Length);
            (shuffled[0], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[0]);
        }

        return shuffled;
    }

    private void NotifyCurrentPlaceChanged()
    {
        foreach (var property in new[]
        {
            nameof(CurrentPlace), nameof(CurrentTitle), nameof(LocationLine), nameof(CurrentShortDescription),
            nameof(CurrentDescription), nameof(CoordinateLine), nameof(ImageryLine), nameof(CurrentSources),
            nameof(HasSources), nameof(HasLocationLink), nameof(LocationLinkUrl),
            nameof(HasDescription), nameof(HasShortDescription), nameof(CounterText),
            nameof(LocationVisible), nameof(CuratedShortDescriptionVisible), nameof(DescriptionFallbackVisible),
        })
        {
            OnPropertyChanged(property);
        }
    }

    private void NotifyLayoutChanged()
    {
        foreach (var property in new[]
        {
            nameof(ControlsOnly), nameof(LocationVisible), nameof(TitleVisible), nameof(DescriptionVisible),
            nameof(CuratedShortDescriptionVisible), nameof(DescriptionFallbackVisible),
            nameof(NavigationVisible), nameof(ControlsAlignment), nameof(BasePanelWidth), nameof(WidgetWidth),
        })
        {
            OnPropertyChanged(property);
        }
    }

    private void Save() => _settingsService.Save(_settings);

    private void UpdateRotationTimer()
    {
        _rotationTimer.Stop();
        _rotationTimer.Interval = TimeSpan.FromMinutes(_settings.RotationMinutes);
        if (_settings.AutoRotateEnabled)
        {
            _rotationTimer.Start();
        }
    }
}
