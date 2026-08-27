using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using WallpaperWidget.Models;
using WallpaperWidget.Services;
using WallpaperWidget.ViewModels;
using WallpaperWidget.Views;

namespace WallpaperWidget;

public partial class App : Application
{
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan FailedCheckRetryInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ApplicationUpdateCheckInterval = TimeSpan.FromHours(24);
    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;
    private NativeMenuItem? _trayVisibilityItem;
    private NativeMenuItem? _trayContentUpdateItem;
    private NativeMenuItem? _trayApplicationUpdateItem;
    private TrayIcon? _trayIcon;
    private CatalogService? _catalogService;
    private ContentUpdateService? _contentUpdateService;
    private ApplicationUpdateService? _applicationUpdateService;
    private ContentUpdatePlan? _pendingPlan;
    private ApplicationUpdateInfo? _availableApplicationUpdate;
    private bool _startMinimized;
    private readonly SemaphoreSlim _contentUpdateLock = new(1, 1);
    private readonly DispatcherTimer _contentCheckTimer = new() { Interval = TimeSpan.FromMinutes(30) };

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public void ActivateFromExternalRequest()
    {
        if (_mainWindow is null) return;
        _mainWindow.BringToFrontFromTray();
        UpdateTrayVisibilityText();
        AppLog.Info("existing_instance_activated", "The existing application window was activated.");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppLog.Initialize();
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var contentStorage = new ContentStorage();
            _catalogService = new CatalogService(contentStorage);
            _contentUpdateService = new ContentUpdateService(contentStorage);
            _applicationUpdateService = new ApplicationUpdateService();
            var settingsService = new SettingsService();
            var settings = settingsService.Load();
            var entries = _catalogService.Load();
            var autostartService = new AutostartService();
            _startMinimized = desktop.Args?.Contains("--minimized", StringComparer.OrdinalIgnoreCase) == true;

            _viewModel = new MainViewModel(
                entries,
                _catalogService,
                settingsService,
                WallpaperServiceFactory.Create(),
                autostartService,
                settings);

            _mainWindow = new MainWindow(_viewModel);
            _mainWindow.WidgetVisibilityChanged += (_, _) => UpdateTrayVisibilityText();
            _mainWindow.ContentPackChangeRequested += MainWindow_ContentPackChangeRequested;
            _mainWindow.ContentUpdateCheckRequested += MainWindow_ContentUpdateCheckRequested;
            _mainWindow.ContentUpdateInstallRequested += MainWindow_ContentUpdateInstallRequested;
            _mainWindow.ContentUpdateModeChangeRequested += MainWindow_ContentUpdateModeChangeRequested;
            _mainWindow.ApplicationUpdateCheckRequested += MainWindow_ApplicationUpdateCheckRequested;
            _mainWindow.Opened += MainWindow_OnOpened;
            desktop.MainWindow = _mainWindow;

            var trayIcons = TrayIcon.GetIcons(this);
            if (trayIcons is { Count: > 0 })
            {
                _trayIcon = trayIcons[0];
                _trayIcon.Clicked += TrayIcon_OnClicked;
                var menuItems = _trayIcon.Menu?.Items.OfType<NativeMenuItem>().ToArray() ?? [];
                _trayVisibilityItem = menuItems.FirstOrDefault(item => item.Header?.ToString() == "Hide widget");
                _trayContentUpdateItem = menuItems.FirstOrDefault(item => item.Header?.ToString() == "Check for content updates");
                _trayApplicationUpdateItem = menuItems.FirstOrDefault(item => item.Header?.ToString() == "Check for application updates");
            }

            _contentCheckTimer.Tick += ContentCheckTimer_OnTick;
            _contentCheckTimer.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void TrayVisibility_OnClick(object? sender, EventArgs e)
    {
        if (_mainWindow is null) return;
        if (_mainWindow.IsVisible) _mainWindow.HideToTray();
        else _mainWindow.ShowFromTray();
        UpdateTrayVisibilityText();
    }

    private void TrayIcon_OnClicked(object? sender, EventArgs e)
    {
        if (_mainWindow is null) return;
        _mainWindow.BringToFrontFromTray();
        UpdateTrayVisibilityText();
    }

    private void TrayNext_OnClick(object? sender, EventArgs e)
    {
        _viewModel?.Next();
        if (_mainWindow is { IsVisible: false }) _mainWindow.ShowFromTray();
        UpdateTrayVisibilityText();
    }

    private async void TrayContentUpdate_OnClick(object? sender, EventArgs e)
    {
        if (_mainWindow is { IsVisible: false }) _mainWindow.ShowFromTray();
        if (_pendingPlan is not null) await ReviewPendingPlanAsync();
        else await CheckCurrentPackAsync(ContentCheckReason.Manual);
    }

    private async void TrayApplicationUpdate_OnClick(object? sender, EventArgs e)
    {
        if (_mainWindow is { IsVisible: false }) _mainWindow.ShowFromTray();
        if (_availableApplicationUpdate is not null) await ShowApplicationUpdateAsync(_availableApplicationUpdate);
        else await CheckApplicationUpdateAsync(ApplicationUpdateCheckReason.Manual);
    }

    private void UpdateTrayVisibilityText()
    {
        if (_trayVisibilityItem is not null)
            _trayVisibilityItem.Header = _mainWindow is { IsVisible: true } ? "Hide widget" : "Show widget";
    }

    private void UpdateTrayContentText(string? text = null)
    {
        var display = text ?? "Check for content updates";
        if (_trayContentUpdateItem is not null) _trayContentUpdateItem.Header = display;
        if (_trayIcon is not null) _trayIcon.ToolTipText = text is null ? "Earth Wallpaper" : $"Earth Wallpaper — {display}";
    }

    private void TrayExit_OnClick(object? sender, EventArgs e)
    {
        _contentCheckTimer.Stop();
        if (_mainWindow is not null) _mainWindow.AllowClose = true;
        AppLog.Info("app_exiting", "Application is exiting.");
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    private async void MainWindow_OnOpened(object? sender, EventArgs e)
    {
        if (_mainWindow is not null) _mainWindow.Opened -= MainWindow_OnOpened;
        try
        {
            if (_viewModel is null) return;
            if (!ContentPacks.IsValid(_viewModel.Settings.ContentPackId)) await ChooseContentPackAsync();

            if (ContentPacks.IsValid(_viewModel.Settings.ContentPackId))
            {
                if (_startMinimized && _mainWindow is not null)
                {
                    _mainWindow.HideToTray();
                    UpdateTrayVisibilityText();
                }
                if (_viewModel.Settings.ContentUpdateMode != ContentUpdateMode.ManualOnly && IsAutomaticCheckDue())
                    await CheckCurrentPackAsync(ContentCheckReason.Automatic);
            }

            if (IsApplicationUpdateCheckDue())
                await CheckApplicationUpdateAsync(ApplicationUpdateCheckReason.Automatic);
        }
        catch (Exception exception)
        {
            HandleContentError("content_initialization_failed", "Content initialization failed.", exception);
        }
    }

    private async void MainWindow_ContentPackChangeRequested(object? sender, EventArgs e)
    {
        await ChooseContentPackAsync();
    }

    private async void MainWindow_ContentUpdateCheckRequested(object? sender, EventArgs e)
    {
        await CheckCurrentPackAsync(ContentCheckReason.Manual);
    }

    private async void MainWindow_ContentUpdateInstallRequested(object? sender, EventArgs e)
    {
        if (_pendingPlan is not null) await ReviewPendingPlanAsync();
        else await CheckCurrentPackAsync(ContentCheckReason.Manual);
    }

    private async void MainWindow_ApplicationUpdateCheckRequested(object? sender, EventArgs e)
    {
        if (_availableApplicationUpdate is not null) await ShowApplicationUpdateAsync(_availableApplicationUpdate);
        else await CheckApplicationUpdateAsync(ApplicationUpdateCheckReason.Manual);
    }

    private async Task MainWindow_ContentUpdateModeChangeRequested(int requestedIndex)
    {
        if (_viewModel is null || _mainWindow is null) return;
        if (!Enum.IsDefined(typeof(ContentUpdateMode), requestedIndex))
        {
            _mainWindow.SyncContentUpdateModeSelection();
            return;
        }

        var requestedMode = (ContentUpdateMode)requestedIndex;
        if (requestedMode == ContentUpdateMode.DownloadAutomatically &&
            _viewModel.Settings.ContentUpdateMode != ContentUpdateMode.DownloadAutomatically)
        {
            var confirmed = await new AutomaticDownloadsConfirmationWindow().ShowDialog<bool>(_mainWindow);
            if (!confirmed)
            {
                _mainWindow.SyncContentUpdateModeSelection();
                return;
            }
        }

        _viewModel.SetContentUpdateMode(requestedMode);
        _mainWindow.SyncContentUpdateModeSelection();
        var message = requestedMode switch
        {
            ContentUpdateMode.NotifyAutomatically => "Updates are checked daily and require confirmation.",
            ContentUpdateMode.DownloadAutomatically => "Updates will be checked and downloaded daily.",
            _ => "Content updates will only be checked manually.",
        };
        _viewModel.SetContentUpdateState(message, _pendingPlan is not null);
        AppLog.ContentInfo("content_update_mode_changed", message, new { requestedMode });

        if (requestedMode != ContentUpdateMode.ManualOnly && IsAutomaticCheckDue())
            await CheckCurrentPackAsync(ContentCheckReason.Automatic);
    }

    private async void ContentCheckTimer_OnTick(object? sender, EventArgs e)
    {
        if (_viewModel?.Settings.ContentUpdateMode != ContentUpdateMode.ManualOnly && IsAutomaticCheckDue())
            await CheckCurrentPackAsync(ContentCheckReason.Automatic);
        if (IsApplicationUpdateCheckDue())
            await CheckApplicationUpdateAsync(ApplicationUpdateCheckReason.Automatic);
    }

    private bool IsAutomaticCheckDue()
    {
        if (_viewModel is null) return false;
        var now = DateTimeOffset.UtcNow;
        var lastSuccess = _viewModel.Settings.LastContentCheckUtc;
        if (lastSuccess is not null && now - lastSuccess.Value < AutomaticCheckInterval) return false;
        var lastAttempt = _viewModel.Settings.LastContentCheckAttemptUtc;
        return lastAttempt is null || now - lastAttempt.Value >= FailedCheckRetryInterval;
    }

    private bool IsApplicationUpdateCheckDue()
    {
        if (_viewModel is null || _viewModel.IsApplicationUpdateBusy) return false;
        var lastCheck = _viewModel.Settings.LastApplicationUpdateCheckUtc;
        return lastCheck is null || DateTimeOffset.UtcNow - lastCheck.Value >= ApplicationUpdateCheckInterval;
    }

    private async Task CheckApplicationUpdateAsync(ApplicationUpdateCheckReason reason)
    {
        if (_viewModel is null || _applicationUpdateService is null || _viewModel.IsApplicationUpdateBusy) return;
        try
        {
            _viewModel.SetApplicationUpdateState("Checking GitHub Releases…", true);
            var result = await _applicationUpdateService.CheckAsync(includePrereleases: true);
            _viewModel.MarkApplicationUpdateCheckSucceeded();
            _availableApplicationUpdate = result.AvailableUpdate;
            if (_availableApplicationUpdate is null)
            {
                _viewModel.SetApplicationUpdateState($"Version {result.CurrentVersion} is up to date.");
                UpdateTrayApplicationText();
                if (reason == ApplicationUpdateCheckReason.Manual)
                    ShowNotice("Earth Wallpaper is up to date", $"You are using version {result.CurrentVersion}.");
                return;
            }

            _viewModel.SetApplicationUpdateState($"Version {_availableApplicationUpdate.Version} is available.");
            UpdateTrayApplicationText($"Application update {_availableApplicationUpdate.Version}");
            AppLog.Info("application_update_available", "A new application release is available.", new
            {
                currentVersion = result.CurrentVersion,
                availableVersion = _availableApplicationUpdate.Version,
                _availableApplicationUpdate.IsPrerelease,
            });
            if (reason == ApplicationUpdateCheckReason.Manual)
                await ShowApplicationUpdateAsync(_availableApplicationUpdate);
            else
                ShowNotice("Earth Wallpaper update available", $"Version {_availableApplicationUpdate.Version} is ready. Open the tray menu to review it.");
        }
        catch (Exception exception)
        {
            _viewModel.SetApplicationUpdateState($"Version {ApplicationVersion.Display} · update check failed.");
            AppLog.Warning("application_update_check_failed", "GitHub Releases could not be checked.", new { exception = exception.GetType().Name, exception.Message });
            if (reason == ApplicationUpdateCheckReason.Manual)
                ShowNotice("Application update check failed", "GitHub Releases could not be reached. Try again later.");
        }
    }

    private async Task ShowApplicationUpdateAsync(ApplicationUpdateInfo update)
    {
        if (_mainWindow is null) return;
        if (!_mainWindow.IsVisible) _mainWindow.ShowFromTray();
        await new ApplicationUpdateWindow(update).ShowDialog<bool>(_mainWindow);
    }

    private void UpdateTrayApplicationText(string? text = null)
    {
        if (_trayApplicationUpdateItem is not null)
            _trayApplicationUpdateItem.Header = text ?? "Check for application updates";
    }

    private async Task ChooseContentPackAsync()
    {
        if (_mainWindow is null || _viewModel is null || _contentUpdateService is null) return;
        Dictionary<string, ContentUpdatePlan> plans;
        try
        {
            _viewModel.SetContentUpdateState("Checking collection sizes…", false, true);
            plans = await BuildPackPlansAsync();
            _viewModel.MarkContentCheckSucceeded();
        }
        catch (Exception exception)
        {
            HandleContentError("content_pack_sizes_failed", "Collection sizes could not be checked.", exception);
            plans = [];
        }

        var summaries = plans.Values.Select(plan => new ContentPackSummary(
            plan.PackId,
            plan.Manifest.EntryCount,
            plan.TotalPackBytes,
            plan.DownloadBytes));
        var selected = await new ContentPackWindow(summaries).ShowDialog<string?>(_mainWindow);
        if (!ContentPacks.IsValid(selected))
        {
            _viewModel.SetContentUpdateState("Collection selection was cancelled.", _pendingPlan is not null);
            return;
        }

        ContentUpdatePlan plan;
        try
        {
            plan = plans.TryGetValue(selected!, out var preparedPlan)
                ? preparedPlan
                : await CheckPackAsync(selected!);
        }
        catch (Exception exception)
        {
            HandleContentError("content_pack_check_failed", "The selected collection could not be checked.", exception);
            return;
        }

        if (plan.IsUpToDate)
        {
            _viewModel.SetContentPack(plan.PackId);
            _viewModel.SetContentUpdateState("Content is up to date.", false);
            return;
        }
        await ConfirmAndInstallAsync(plan);
    }

    private async Task<Dictionary<string, ContentUpdatePlan>> BuildPackPlansAsync()
    {
        if (_contentUpdateService is null || _viewModel is null) return [];
        await _contentUpdateLock.WaitAsync();
        try
        {
            _viewModel.MarkContentCheckAttempt();
            var allTask = _contentUpdateService.CheckAsync(ContentPacks.All);
            var aestheticTask = _contentUpdateService.CheckAsync(ContentPacks.Aesthetic);
            var plans = await Task.WhenAll(allTask, aestheticTask);
            return plans.ToDictionary(plan => plan.PackId, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            _contentUpdateLock.Release();
        }
    }

    private async Task CheckCurrentPackAsync(ContentCheckReason reason)
    {
        if (_viewModel is null || !ContentPacks.IsValid(_viewModel.Settings.ContentPackId)) return;
        ContentUpdatePlan plan;
        try
        {
            _viewModel.SetContentUpdateState("Checking for content updates…", false, true);
            plan = await CheckPackAsync(_viewModel.Settings.ContentPackId!);
            _viewModel.MarkContentCheckSucceeded();
        }
        catch (Exception exception)
        {
            HandleContentError("content_check_failed", "The update check failed. Existing content is still available.", exception, _pendingPlan is not null);
            return;
        }

        if (plan.IsUpToDate)
        {
            _pendingPlan = null;
            var checkedAt = DateTimeOffset.Now.ToString("g");
            _viewModel.SetContentUpdateState($"Content is up to date · checked {checkedAt}", false);
            UpdateTrayContentText();
            if (reason == ContentCheckReason.Manual) ShowNotice("Content is up to date", "No new places or image changes were found.");
            return;
        }

        _pendingPlan = plan;
        var availableMessage = BuildAvailableMessage(plan);
        _viewModel.SetContentUpdateState(availableMessage, true);
        UpdateTrayContentText(availableMessage);

        if (reason == ContentCheckReason.Manual)
        {
            await ConfirmAndInstallAsync(plan);
            return;
        }

        if (_viewModel.Settings.ContentUpdateMode == ContentUpdateMode.DownloadAutomatically)
        {
            await InstallPlanAsync(plan);
            return;
        }

        ShowNotice("New wallpaper content", availableMessage + ". Open the tray menu to review it.");
    }

    private async Task<ContentUpdatePlan> CheckPackAsync(string packId)
    {
        if (_contentUpdateService is null || _viewModel is null) throw new InvalidOperationException("Content updater is unavailable.");
        await _contentUpdateLock.WaitAsync();
        try
        {
            _viewModel.MarkContentCheckAttempt();
            return await _contentUpdateService.CheckAsync(packId);
        }
        finally
        {
            _contentUpdateLock.Release();
        }
    }

    private async Task ConfirmAndInstallAsync(ContentUpdatePlan plan)
    {
        if (_mainWindow is null || _viewModel is null) return;
        var confirmed = await new ContentDownloadConfirmationWindow(plan).ShowDialog<bool>(_mainWindow);
        if (!confirmed)
        {
            _pendingPlan = plan;
            _viewModel.SetContentUpdateState(BuildAvailableMessage(plan), true);
            UpdateTrayContentText(BuildAvailableMessage(plan));
            AppLog.ContentInfo("content_install_declined", "The user postponed the content download.", new
            {
                plan.PackId,
                plan.Manifest.ContentVersion,
                plan.DownloadBytes,
            });
            return;
        }
        await InstallPlanAsync(plan);
    }

    private async Task ReviewPendingPlanAsync()
    {
        if (_pendingPlan is null || _viewModel is null) return;
        var packId = _pendingPlan.PackId;
        try
        {
            _viewModel.SetContentUpdateState("Refreshing download size…", false, true);
            var refreshedPlan = await CheckPackAsync(packId);
            _viewModel.MarkContentCheckSucceeded();
            if (refreshedPlan.IsUpToDate)
            {
                _pendingPlan = null;
                _viewModel.SetContentUpdateState("Content is up to date.", false);
                UpdateTrayContentText();
                return;
            }
            _pendingPlan = refreshedPlan;
            await ConfirmAndInstallAsync(refreshedPlan);
        }
        catch (Exception exception)
        {
            HandleContentError("content_plan_refresh_failed", "The download size could not be refreshed. Existing content is still available.", exception, true);
        }
    }

    private async Task InstallPlanAsync(ContentUpdatePlan plan)
    {
        if (_contentUpdateService is null || _catalogService is null || _viewModel is null) return;
        ContentDownloadProgressWindow? progressWindow = null;
        await _contentUpdateLock.WaitAsync();
        try
        {
            _pendingPlan = null;
            UpdateTrayContentText("Downloading content…");
            _viewModel.SetContentUpdateState("Preparing content download…", false, true);
            if (_mainWindow is not null)
            {
                progressWindow = new ContentDownloadProgressWindow(plan);
                progressWindow.Show(_mainWindow);
                _mainWindow.IsEnabled = false;
            }
            var progress = new Progress<ContentDownloadProgress>(value =>
            {
                _viewModel.SetContentUpdateProgress(value);
                progressWindow?.Report(value);
            });
            var result = await Task.Run(() => _contentUpdateService.InstallAsync(plan, progress));
            progressWindow?.ShowFinishing();
            var entries = _catalogService.Load();
            _viewModel.SetContentPack(plan.PackId);
            _viewModel.ReplaceEntries(entries, result.Message);
            _viewModel.ApplyCurrentWallpaper();
            _viewModel.SetContentUpdateState(result.Message, false);
            UpdateTrayContentText();
            ShowNotice("Wallpaper content updated", result.Message);
        }
        catch (Exception exception)
        {
            _pendingPlan = plan;
            HandleContentError("content_install_failed", "Download paused. It will continue from the saved progress next time.", exception, true);
        }
        finally
        {
            if (_mainWindow is not null) _mainWindow.IsEnabled = true;
            progressWindow?.FinishAndClose();
            _contentUpdateLock.Release();
        }
    }

    private void HandleContentError(string eventName, string userMessage, Exception exception, bool hasPending = false)
    {
        AppLog.ContentWarning(eventName, userMessage, new { exception = exception.GetType().Name, exception.Message });
        _viewModel?.SetContentUpdateState(userMessage, hasPending);
        UpdateTrayContentText(hasPending ? "Content download paused" : "Content update check failed");
        ShowNotice("Earth Wallpaper", userMessage);
    }

    private void ShowNotice(string title, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var screen = _mainWindow?.Screens.ScreenFromWindow(_mainWindow) ?? _mainWindow?.Screens.Primary;
            var notice = new ContentNoticeWindow(title, message, screen?.WorkingArea, screen?.Scaling ?? 1);
            notice.Show();
        });
    }

    private static string BuildAvailableMessage(ContentUpdatePlan plan)
    {
        var changeText = plan.MissingAssetCount > 0 && plan.ChangedPlaceCount == 0
            ? plan.MissingAssetCount == 1
                ? "1 missing image"
                : $"{plan.MissingAssetCount} missing images"
            : plan.ChangedPlaceCount switch
        {
            > 0 => $"{plan.ChangedPlaceCount} new or updated places",
            _ => $"{plan.Manifest.EntryCount} places",
        };
        return plan.DownloadBytes > 0
            ? $"{changeText} · {ContentSizeFormatter.Format(plan.DownloadBytes)}"
            : $"{changeText} · already downloaded";
    }

    private enum ContentCheckReason
    {
        Manual,
        Automatic,
    }

    private enum ApplicationUpdateCheckReason
    {
        Manual,
        Automatic,
    }
}
