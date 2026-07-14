using Snaploom.Platform.Abstractions;
using Snaploom.Platform.MacOS;
using Snaploom.Platform.Windows;

namespace Snaploom.IntegrationTests;

public sealed class DesktopPlatformTests
{
    [Fact]
    public void WindowsPlatformPublishesTheWindowsApplicationIdentity()
    {
        var platform = Observe(new WindowsDesktopPlatform());

        Assert.Equal(DesktopPlatformKind.Windows, platform.Kind);
        Assert.Equal("Snaploom.Desktop", platform.ApplicationId);
    }

    [Fact]
    public void MacOSPlatformPublishesTheMacOSApplicationIdentity()
    {
        var platform = Observe(new MacOSDesktopPlatform());

        Assert.Equal(DesktopPlatformKind.MacOS, platform.Kind);
        Assert.Equal("com.snaploom.app", platform.ApplicationId);
    }

    private static (DesktopPlatformKind Kind, string ApplicationId) Observe(IDesktopPlatform platform) =>
        (platform.Kind, platform.ApplicationId);
}
