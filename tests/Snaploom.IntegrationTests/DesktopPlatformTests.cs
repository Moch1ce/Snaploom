using Snaploom.Platform.Abstractions;
using Snaploom.Platform.MacOS;
using Snaploom.Platform.Windows;
using Snaploom.App;

namespace Snaploom.IntegrationTests;

public sealed class DesktopPlatformTests
{
    [Fact]
    public void WindowsPlatformPublishesTheWindowsApplicationIdentity()
    {
        var platform = ReadIdentity(new WindowsDesktopPlatform());

        Assert.Equal(DesktopPlatformKind.Windows, platform.Kind);
        Assert.Equal("Snaploom.Desktop", platform.ApplicationId);
    }

    [Fact]
    public void MacOSPlatformPublishesTheMacOSApplicationIdentity()
    {
        var platform = ReadIdentity(new MacOSDesktopPlatform());

        Assert.Equal(DesktopPlatformKind.MacOS, platform.Kind);
        Assert.Equal("com.snaploom.app", platform.ApplicationId);
    }

    [Fact]
    public void CurrentPlatformMatchesTheRunningOperatingSystem()
    {
        var platform = DesktopPlatformFactory.CreateCurrent();

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(DesktopPlatformKind.Windows, platform.Kind);
            Assert.Equal("Snaploom.Desktop", platform.ApplicationId);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.Equal(DesktopPlatformKind.MacOS, platform.Kind);
            Assert.Equal("com.snaploom.app", platform.ApplicationId);
        }
        else
        {
            Assert.Fail("Snaploom v1 only supports Windows and macOS.");
        }
    }

    private static (DesktopPlatformKind Kind, string ApplicationId) ReadIdentity(IDesktopPlatform platform) =>
        (platform.Kind, platform.ApplicationId);
}
