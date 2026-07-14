using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Snaploom.Core;
using Snaploom.Rendering;

namespace Snaploom.App;

public sealed class App : Application
{
    private TrayIcon? _trayIcon;

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
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += HandleDesktopExit;

            var viewModel = new TrayMenuViewModel(() => desktop.Shutdown());
            _trayIcon = CreateTrayIcon(viewModel);
            TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static TrayIcon CreateTrayIcon(TrayMenuViewModel viewModel)
    {
        var menu = new NativeMenu();
        foreach (var item in ApplicationLaunchPlan.Default.TrayItems)
        {
            switch (item.Action)
            {
                case TrayAction.StartScreenshot:
                    menu.Add(
                        new NativeMenuItem("开始截图")
                        {
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
            IsVisible = ApplicationLaunchPlan.Default.ShowTrayIcon,
            Menu = menu,
            ToolTipText = ProductIdentity.Name,
        };
    }

    private void HandleDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        TrayIcon.SetIcons(this, trayIcons: null);
        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
