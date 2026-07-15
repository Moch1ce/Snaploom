using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
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

            _screenshotController = ScreenshotController.TryCreate(platform);
            var launchPlan = ApplicationLaunchPlan.Create(_screenshotController is not null);
            if (_screenshotController is not null &&
                platform is IGlobalScreenshotHotKeyService hotKeyService &&
                platform is ISystemResumeService resumeService)
            {
                _hotKeyManager = new ScreenshotHotKeyManager(
                    hotKeyService,
                    resumeService,
                    ScreenshotHotKeyDefaults.For(platform.Kind),
                    () => Dispatcher.UIThread.Post(
                        () => _ = _screenshotController.StartAsync()));
                _hotKeyManager.Start();
            }

            var viewModel = new TrayMenuViewModel(
                () => _screenshotController?.StartAsync() ?? Task.CompletedTask,
                ShowShortcutSettings,
                platform as IAutoStartService,
                () => desktop.Shutdown());
            _trayIcon = CreateTrayIcon(viewModel, launchPlan);
            TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });

            Program.PrimaryInstance?.StartListening(
                () => Dispatcher.UIThread.Post(
                    () => _ = _screenshotController?.StartAsync()));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static TrayIcon CreateTrayIcon(
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
                        new NativeMenuItem("开始截图")
                        {
                            Command = viewModel.StartScreenshotCommand,
                            IsEnabled = item.IsEnabled,
                        });
                    break;

                case TrayAction.ShortcutSettings:
                    menu.Add(
                        new NativeMenuItem("快捷键设置…")
                        {
                            Command = viewModel.OpenShortcutSettingsCommand,
                            IsEnabled = item.IsEnabled,
                        });
                    break;

                case TrayAction.AutoStart:
                    var autoStartItem = new NativeMenuItem("开机启动")
                    {
                        Command = viewModel.ToggleAutoStartCommand,
                        IsEnabled = item.IsEnabled,
                        ToggleType = MenuItemToggleType.CheckBox,
                        IsChecked = viewModel.IsAutoStartEnabled,
                    };
                    viewModel.PropertyChanged += (_, args) =>
                    {
                        if (args.PropertyName == nameof(viewModel.IsAutoStartEnabled))
                        {
                            autoStartItem.IsChecked = viewModel.IsAutoStartEnabled;
                        }
                    };
                    menu.Add(autoStartItem);
                    break;

                case TrayAction.Exit:
                    menu.Add(
                        new NativeMenuItem("退出")
                        {
                            Command = viewModel.ExitCommand,
                            IsEnabled = item.IsEnabled,
                        });
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported tray action: {item.Action}.");
            }
        }

        using var iconStream = new MemoryStream(TrayIconRenderer.RenderPng(size: 32), writable: false);
        return new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            IsVisible = launchPlan.ShowTrayIcon,
            Menu = menu,
            ToolTipText = ProductIdentity.Name,
        };
    }

    private void ShowShortcutSettings()
    {
        if (_hotKeyManager is null)
        {
            return;
        }

        if (_shortcutSettingsWindow is not null)
        {
            _shortcutSettingsWindow.Activate();
            return;
        }

        _shortcutSettingsWindow = new ShortcutSettingsWindow(_hotKeyManager);
        _shortcutSettingsWindow.Closed += (_, _) => _shortcutSettingsWindow = null;
        _shortcutSettingsWindow.Show();
    }

    private void HandleDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        Dispose();
    }

    public void Dispose()
    {
        TrayIcon.SetIcons(this, trayIcons: null);
        _trayIcon?.Dispose();
        _trayIcon = null;
        _screenshotController?.Dispose();
        _screenshotController = null;
        _shortcutSettingsWindow?.Close();
        _shortcutSettingsWindow = null;
        _hotKeyManager?.Dispose();
        _hotKeyManager = null;
        _desktopPlatform?.Dispose();
        _desktopPlatform = null;
    }
}
