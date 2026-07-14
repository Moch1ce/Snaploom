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

public sealed class App : Application
{
    private TrayIcon? _trayIcon;
    private IDisposable? _desktopPlatform;
    private ScreenshotController? _screenshotController;

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
            var viewModel = new TrayMenuViewModel(
                () => _screenshotController?.StartAsync() ?? Task.CompletedTask,
                () => desktop.Shutdown());
            _trayIcon = CreateTrayIcon(viewModel, launchPlan);
            TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });

            if (_screenshotController is not null && platform is IGlobalScreenshotHotKeyService hotKeyService)
            {
                hotKeyService.TryRegisterScreenshotHotKey(
                    () => Dispatcher.UIThread.Post(
                        () => _ = _screenshotController.StartAsync()));
            }
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

    private void HandleDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        TrayIcon.SetIcons(this, trayIcons: null);
        _trayIcon?.Dispose();
        _trayIcon = null;
        _screenshotController?.Dispose();
        _screenshotController = null;
        _desktopPlatform?.Dispose();
        _desktopPlatform = null;
    }
}
