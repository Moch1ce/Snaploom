using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using System.Diagnostics;
using System.Globalization;
using System.ComponentModel;
using Snaploom.Core;
using Snaploom.Platform.Abstractions;
using Snaploom.Rendering;

namespace Snaploom.App;

public sealed class App : Application, IDisposable
{
    private TrayIcon? _trayIcon;
    private IDisposable? _desktopPlatform;
    private ScreenshotController? _screenshotController;
    private ScreenshotHotKeyManager? _hotKeyManager;
    private ShortcutSettingsWindow? _shortcutSettingsWindow;
    private AppSettingsService? _settings;
    private TrayMenuViewModel? _trayViewModel;
    private ApplicationLaunchPlan? _launchPlan;
    private NativeMenuItem? _autoStartMenuItem;
    private PrivacyLog? _log;
    private ISystemNotificationService? _notificationService;
    private IFolderLauncher? _folderLauncher;
    private IExternalUriLauncher? _uriLauncher;
    private IUpdateCheckService? _updateCheckService;
    private DesktopPerformanceRecorder? _performanceRecorder;
    private readonly CultureInfo _systemCulture = CultureInfo.CurrentUICulture;

    internal static Func<DesktopPlatformKind, AppSettingsService>? SettingsServiceFactory { get; set; }
    internal static Func<PrivacyLog>? PrivacyLogFactory { get; set; }

    public override void Initialize()
    {
        Name = ProductIdentity.Name;
        RequestedThemeVariant = ThemeVariant.Default;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var platform = DesktopPlatformFactory.CreateCurrent();
            _desktopPlatform = platform as IDisposable;
            if (platform is IPlatformProcessInitializer processInitializer)
            {
                processInitializer.InitializeProcess();
            }

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += HandleDesktopExit;

            var defaults = AppSettings.CreateDefault(platform.Kind);
            _settings = SettingsServiceFactory?.Invoke(platform.Kind) ??
                new AppSettingsService(
                    new JsonAppSettingsStore(AppSettingsService.GetDefaultPath(), defaults));
            ApplyAppearance();
            _log = PrivacyLogFactory?.Invoke() ??
                new PrivacyLog(PrivacyLog.GetDefaultDirectory());
            _notificationService = platform as ISystemNotificationService;
            _folderLauncher = platform as IFolderLauncher;
            _uriLauncher = platform as IExternalUriLauncher;
            _updateCheckService = UpdateCheckService.CreateDefault();
            _log.Info(AppLogEvent.ApplicationStarted);

            _screenshotController = ScreenshotController.TryCreate(platform, _settings, _log);
            _performanceRecorder = DesktopPerformanceRecorder.TryCreateFromEnvironment();
            if (_screenshotController is not null && _performanceRecorder is not null)
            {
                _screenshotController.ScreenshotActivated += HandleScreenshotActivated;
                _screenshotController.ScreenshotCycleCompleted += HandleScreenshotCycleCompleted;
            }
            _launchPlan = ApplicationLaunchPlan.Create(_screenshotController is not null);
            if (_screenshotController is null)
            {
                ReportBackgroundFailure(
                    AppLogEvent.PlatformUnavailable,
                    AppUiText.PlatformUnavailable);
            }
            if (_screenshotController is not null &&
                platform is IGlobalScreenshotHotKeyService hotKeyService &&
                platform is ISystemResumeService resumeService)
            {
                _hotKeyManager = new ScreenshotHotKeyManager(
                    hotKeyService,
                    resumeService,
                    _settings.Current.HotKey,
                    HandleScreenshotHotKey);
                _hotKeyManager.RegistrationFailed += HandleHotKeyReregistrationFailed;
                if (!_hotKeyManager.Start())
                {
                    ReportBackgroundFailure(
                        AppLogEvent.HotKeyConflict,
                        AppUiText.HotKeyConflictNotification);
                }
            }

            _trayViewModel = new TrayMenuViewModel(
                () => _screenshotController?.StartAsync() ?? Task.CompletedTask,
                ShowShortcutSettings,
                platform as IAutoStartService,
                () => desktop.Shutdown(),
                _settings,
                HandleAutoStartFailure);
            _trayViewModel.PropertyChanged += HandleTrayViewModelChanged;
            _trayIcon = CreateTrayIcon(_trayViewModel, _launchPlan);
            TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });

            Program.PrimaryInstance?.StartListening(
                () => Dispatcher.UIThread.Post(
                    () => _ = _screenshotController?.StartAsync()));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private TrayIcon CreateTrayIcon(
        TrayMenuViewModel viewModel,
        ApplicationLaunchPlan launchPlan)
    {
        var menu = CreateTrayMenu(viewModel, launchPlan);
        using var iconStream = new MemoryStream(TrayIconRenderer.RenderPng(size: 32), writable: false);
        return new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            IsVisible = launchPlan.ShowTrayIcon,
            Menu = menu,
            ToolTipText = ProductIdentity.Name,
        };
    }

    private NativeMenu CreateTrayMenu(
        TrayMenuViewModel viewModel,
        ApplicationLaunchPlan launchPlan)
    {
        var menu = new NativeMenu();
        foreach (var item in launchPlan.TrayItems)
        {
            switch (item.Action)
            {
                case TrayAction.StartScreenshot:
                    menu.Add(
                        new NativeMenuItem(AppUiText.StartScreenshot)
                        {
                            Command = viewModel.StartScreenshotCommand,
                            IsEnabled = item.IsEnabled,
                        });
                    break;

                case TrayAction.ShortcutSettings:
                    menu.Add(
                        new NativeMenuItem(AppUiText.SettingsMenu)
                        {
                            Command = viewModel.OpenShortcutSettingsCommand,
                            IsEnabled = item.IsEnabled,
                        });
                    break;

                case TrayAction.AutoStart:
                    _autoStartMenuItem = new NativeMenuItem(AppUiText.AutoStart)
                    {
                        Command = viewModel.ToggleAutoStartCommand,
                        IsEnabled = item.IsEnabled,
                        ToggleType = MenuItemToggleType.CheckBox,
                        IsChecked = viewModel.IsAutoStartEnabled,
                    };
                    menu.Add(_autoStartMenuItem);
                    break;

                case TrayAction.Exit:
                    menu.Add(
                        new NativeMenuItem(AppUiText.Exit)
                        {
                            Command = viewModel.ExitCommand,
                            IsEnabled = item.IsEnabled,
                        });
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported tray action: {item.Action}.");
            }
        }

        return menu;
    }

    private void ShowShortcutSettings()
    {
        if (_hotKeyManager is null ||
            _settings is null ||
            _trayViewModel is null ||
            _log is null ||
            _updateCheckService is null)
        {
            return;
        }

        if (_shortcutSettingsWindow is not null)
        {
            _shortcutSettingsWindow.Activate();
            return;
        }

        _shortcutSettingsWindow = new ShortcutSettingsWindow(
            _hotKeyManager,
            _settings,
            _trayViewModel,
            ApplyAppearance,
            _log,
            _folderLauncher,
            _updateCheckService,
            _uriLauncher);
        _shortcutSettingsWindow.Closed += (_, _) => _shortcutSettingsWindow = null;
        _shortcutSettingsWindow.Show();
    }

    private void ApplyAppearance()
    {
        if (_settings is null)
        {
            return;
        }

        var culture = AppAppearance.GetCulture(_settings.Current.Language, _systemCulture);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        RequestedThemeVariant = AppAppearance.GetThemeVariant(_settings.Current.Theme);
        if (_trayIcon is not null && _trayViewModel is not null && _launchPlan is not null)
        {
            _trayIcon.Menu = CreateTrayMenu(_trayViewModel, _launchPlan);
        }
    }

    private void HandleDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        Dispose();
    }

    private void HandleScreenshotActivated(TimeSpan elapsed) =>
        _performanceRecorder?.RecordActivation(elapsed);

    private void HandleScreenshotHotKey()
    {
        var activationStartedAt = Stopwatch.GetTimestamp();
        Dispatcher.UIThread.Post(
            () => _ = _screenshotController?.StartAsync(activationStartedAt));
    }

    private void HandleScreenshotCycleCompleted(object? sender, EventArgs e) =>
        _performanceRecorder?.RecordScreenshotCycle();

    private void HandleHotKeyReregistrationFailed(object? sender, EventArgs e) =>
        ReportBackgroundFailure(
            AppLogEvent.HotKeyReregisterFailed,
            AppUiText.HotKeyReregisterNotification);

    private void HandleAutoStartFailure(Exception exception) =>
        ReportBackgroundFailure(
            AppLogEvent.AutoStartFailed,
            AppUiText.AutoStartFailure,
            exception);

    private void ReportBackgroundFailure(
        AppLogEvent logEvent,
        string message,
        Exception? exception = null)
    {
        _log?.Error(logEvent, exception);
        try
        {
            _notificationService?.ShowNotification(AppUiText.BackgroundErrorTitle, message);
        }
        catch (InvalidOperationException notificationException)
        {
            _log?.Error(AppLogEvent.PlatformUnavailable, notificationException);
        }
        catch (PlatformNotSupportedException notificationException)
        {
            _log?.Error(AppLogEvent.PlatformUnavailable, notificationException);
        }
        catch (System.Runtime.InteropServices.COMException notificationException)
        {
            _log?.Error(AppLogEvent.PlatformUnavailable, notificationException);
        }
        catch (UnauthorizedAccessException notificationException)
        {
            _log?.Error(AppLogEvent.PlatformUnavailable, notificationException);
        }
    }

    private void HandleTrayViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrayMenuViewModel.IsAutoStartEnabled) &&
            _autoStartMenuItem is not null &&
            _trayViewModel is not null)
        {
            _autoStartMenuItem.IsChecked = _trayViewModel.IsAutoStartEnabled;
        }
    }

    public void Dispose()
    {
        TrayIcon.SetIcons(this, trayIcons: null);
        _trayIcon?.Dispose();
        _trayIcon = null;
        if (_screenshotController is not null)
        {
            _screenshotController.ScreenshotActivated -= HandleScreenshotActivated;
            _screenshotController.ScreenshotCycleCompleted -= HandleScreenshotCycleCompleted;
            _screenshotController.Dispose();
        }
        _screenshotController = null;
        _shortcutSettingsWindow?.Close();
        _shortcutSettingsWindow = null;
        if (_hotKeyManager is not null)
        {
            _hotKeyManager.RegistrationFailed -= HandleHotKeyReregistrationFailed;
            _hotKeyManager.Dispose();
        }

        _hotKeyManager = null;
        if (_trayViewModel is not null)
        {
            _trayViewModel.PropertyChanged -= HandleTrayViewModelChanged;
            _trayViewModel = null;
        }

        _autoStartMenuItem = null;
        _log?.Info(AppLogEvent.ApplicationStopped);
        _log = null;
        _notificationService = null;
        _folderLauncher = null;
        _uriLauncher = null;
        _updateCheckService = null;
        _performanceRecorder?.Dispose();
        _performanceRecorder = null;
        _launchPlan = null;
        _settings = null;
        _desktopPlatform?.Dispose();
        _desktopPlatform = null;
    }
}
