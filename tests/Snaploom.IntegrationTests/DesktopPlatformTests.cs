using Snaploom.Core;
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
    public void WindowsPlatformProvidesTheScreenshotVerticalSlice()
    {
        using var platform = new WindowsDesktopPlatform();

        Assert.IsAssignableFrom<IScreenCapturePermissionService>(platform);
        Assert.IsAssignableFrom<IGlobalScreenshotHotKeyService>(platform);
        Assert.IsAssignableFrom<IAutoStartService>(platform);
        Assert.IsAssignableFrom<ISystemResumeService>(platform);
        Assert.IsAssignableFrom<ISystemNotificationService>(platform);
        Assert.IsAssignableFrom<IFolderLauncher>(platform);
        Assert.IsAssignableFrom<IScreenCaptureService>(platform);
        Assert.IsAssignableFrom<IPngSaveDialogService>(platform);
        Assert.IsAssignableFrom<IScreenshotClipboardService>(platform);
        Assert.IsAssignableFrom<IScreenshotOverlayConfigurator>(platform);
    }

    [Fact]
    public void WindowsDesktopCaptureDoesNotRequireElevatedPermission()
    {
        using var platform = new WindowsDesktopPlatform();
        var permissionService = Assert.IsAssignableFrom<IScreenCapturePermissionService>(platform);

        Assert.Equal(ScreenCapturePermissionStatus.Granted, permissionService.GetPermissionStatus());
        Assert.True(permissionService.RequestPermission());
    }

    [Fact]
    public void WindowsLongRunningServicesCanBeStartedAndStopped()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var platform = new WindowsDesktopPlatform();
        var autoStartService = Assert.IsAssignableFrom<IAutoStartService>(platform);
        var resumeService = Assert.IsAssignableFrom<ISystemResumeService>(platform);

        _ = autoStartService.IsAutoStartEnabled();
        resumeService.StartMonitoring(() => { });
        resumeService.StopMonitoring();
    }

    [Fact]
    public async Task WindowsNativeBridgeReturnsAUnifiedFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var platform = new WindowsDesktopPlatform();
        using var capturedScreen = await platform.CaptureCurrentDisplayAsync(
            TestContext.Current.CancellationToken);

        Assert.True(capturedScreen.Frame.PhysicalSize.Width > 0);
        Assert.True(capturedScreen.Frame.PhysicalSize.Height > 0);
        Assert.Equal(
            capturedScreen.Frame.Stride * capturedScreen.Frame.PhysicalSize.Height,
            capturedScreen.Frame.Pixels.Length);
        Assert.Equal(CapturedPixelFormat.Bgra8888PremultipliedSrgb, capturedScreen.Frame.PixelFormat);
        Assert.InRange(
            capturedScreen.CursorPosition.X,
            0,
            capturedScreen.Frame.PhysicalSize.Width);
        Assert.InRange(
            capturedScreen.CursorPosition.Y,
            0,
            capturedScreen.Frame.PhysicalSize.Height);
        Assert.All(capturedScreen.WindowCandidates, candidate =>
        {
            Assert.Equal(ScreenshotWindowExclusion.None, candidate.Exclusion);
            Assert.True(candidate.Bounds.Width > 0);
            Assert.True(candidate.Bounds.Height > 0);
            Assert.InRange(candidate.Bounds.X, 0, capturedScreen.Frame.PhysicalSize.Width - 1);
            Assert.InRange(candidate.Bounds.Y, 0, capturedScreen.Frame.PhysicalSize.Height - 1);
        });
    }

    [Fact]
    public void MacOSPlatformPublishesTheMacOSApplicationIdentity()
    {
        var platform = ReadIdentity(new MacOSDesktopPlatform());

        Assert.Equal(DesktopPlatformKind.MacOS, platform.Kind);
        Assert.Equal("com.snaploom.app", platform.ApplicationId);
    }

    [Fact]
    public void MacOSPlatformProvidesTheLongRunningApplicationServices()
    {
        using var platform = new MacOSDesktopPlatform();

        Assert.IsAssignableFrom<IGlobalScreenshotHotKeyService>(platform);
        Assert.IsAssignableFrom<IAutoStartService>(platform);
        Assert.IsAssignableFrom<ISystemResumeService>(platform);
        Assert.IsAssignableFrom<ISystemNotificationService>(platform);
        Assert.IsAssignableFrom<IFolderLauncher>(platform);
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
