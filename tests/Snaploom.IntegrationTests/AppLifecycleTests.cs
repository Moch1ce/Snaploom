using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;

namespace Snaploom.IntegrationTests;

public sealed class AppLifecycleTests
{
    [Fact]
    public void AppStartsHeadlesslyWithoutDesktopWindows()
    {
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        AppBuilder
            .Configure<Snaploom.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithLifetime(lifetime);

        try
        {
            Assert.Null(lifetime.MainWindow);
            Assert.Empty(lifetime.Windows);
            Assert.Equal(ShutdownMode.OnExplicitShutdown, lifetime.ShutdownMode);
        }
        finally
        {
            lifetime.Shutdown();
        }
    }
}
