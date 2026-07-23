using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Snaploom.Core;

namespace Snaploom.App;

internal static class Program
{
    internal static SingleInstanceCoordinator? PrimaryInstance { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        var applicationId = OperatingSystem.IsWindows()
            ? ProductIdentity.WindowsAppId
            : ProductIdentity.MacOSBundleId;
        using var instance = SingleInstanceCoordinator.Acquire(applicationId);
        if (!instance.IsPrimary)
        {
            _ = instance.SignalCaptureAsync().GetAwaiter().GetResult();
            return;
        }

        PrimaryInstance = instance;
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(
                args,
                ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            PrimaryInstance = null;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace();
}
