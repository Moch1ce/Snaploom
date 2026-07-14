using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;

namespace Snaploom.IntegrationTests;

public sealed class AppLifecycleTests
{
    [Fact]
    public void AppStartsInTheTrayAndTheExitMenuShutsItDown()
    {
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        var exitRaised = false;
        lifetime.Exit += (_, _) => exitRaised = true;

        AppBuilder
            .Configure<Snaploom.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithLifetime(lifetime);

        try
        {
            Assert.Null(lifetime.MainWindow);
            Assert.Empty(lifetime.Windows);
            Assert.Equal(ShutdownMode.OnExplicitShutdown, lifetime.ShutdownMode);

            var app = Assert.IsType<Snaploom.App.App>(Application.Current);
            var trayIcon = Assert.Single(Assert.IsType<TrayIcons>(TrayIcon.GetIcons(app)));
            Assert.True(trayIcon.IsVisible);

            var menu = Assert.IsType<NativeMenu>(trayIcon.Menu);
            var startScreenshotItem = Assert.IsType<NativeMenuItem>(menu.Items[0]);
            var exitItem = Assert.IsType<NativeMenuItem>(menu.Items[1]);

            Assert.False(startScreenshotItem.IsEnabled);
            Assert.Null(startScreenshotItem.Command);
            Assert.True(exitItem.IsEnabled);
            Assert.NotNull(exitItem.Command);

            exitItem.Command.Execute(parameter: null);

            Assert.True(exitRaised);
            Assert.Null(TrayIcon.GetIcons(app));
        }
        finally
        {
            if (!exitRaised)
            {
                lifetime.Shutdown();
            }
        }
    }
}
