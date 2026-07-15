using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Snaploom.App;

namespace Snaploom.IntegrationTests;

public sealed class AppLifecycleTests
{
    [Fact]
    public void AppStartsInTheTrayAndTheExitMenuShutsItDown()
    {
        var logDirectory = Path.Combine(
            Path.GetTempPath(),
            $"snaploom-app-test-{Guid.NewGuid():N}");
        Snaploom.App.App.SettingsServiceFactory = platform => AppSettingsService.CreateTransient(
            AppSettings.CreateDefault(platform));
        Snaploom.App.App.PrivacyLogFactory = () => new PrivacyLog(logDirectory);
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
            var shortcutSettingsItem = Assert.IsType<NativeMenuItem>(menu.Items[1]);
            var autoStartItem = Assert.IsType<NativeMenuItem>(menu.Items[2]);
            var exitItem = Assert.IsType<NativeMenuItem>(menu.Items[3]);

            Assert.True(startScreenshotItem.IsEnabled);
            Assert.Equal(AppUiText.StartScreenshot, startScreenshotItem.Header);
            Assert.NotNull(startScreenshotItem.Command);
            Assert.True(shortcutSettingsItem.IsEnabled);
            Assert.NotNull(shortcutSettingsItem.Command);
            Assert.True(autoStartItem.IsEnabled);
            Assert.NotNull(autoStartItem.Command);
            Assert.Equal(MenuItemToggleType.CheckBox, autoStartItem.ToggleType);
            Assert.True(exitItem.IsEnabled);
            Assert.NotNull(exitItem.Command);

            exitItem.Command.Execute(parameter: null);

            Assert.True(exitRaised);
            Assert.Null(TrayIcon.GetIcons(app));
            var logContent = string.Concat(
                Directory.EnumerateFiles(logDirectory).Select(File.ReadAllText));
            Assert.Contains(nameof(AppLogEvent.ApplicationStarted), logContent);
            Assert.Contains(nameof(AppLogEvent.ApplicationStopped), logContent);
        }
        finally
        {
            Snaploom.App.App.SettingsServiceFactory = null;
            Snaploom.App.App.PrivacyLogFactory = null;
            if (!exitRaised)
            {
                lifetime.Shutdown();
            }

            if (Directory.Exists(logDirectory))
            {
                Directory.Delete(logDirectory, recursive: true);
            }
        }
    }
}
