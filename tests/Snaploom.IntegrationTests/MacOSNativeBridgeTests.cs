using Snaploom.Platform.Abstractions;
using Snaploom.Platform.MacOS;

namespace Snaploom.IntegrationTests;

public sealed class MacOSNativeBridgeTests
{
    [Fact]
    public void NativeBridgeReportsScreenCapturePermissionWithoutPrompting()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var service = new MacOSDesktopPlatform();

        var status = service.GetPermissionStatus();

        Assert.True(
            status is ScreenCapturePermissionStatus.Granted or
            ScreenCapturePermissionStatus.NotGranted);
    }

    [Fact]
    public async Task NativeBridgeReturnsAUnifiedFrameWhenPermissionIsAlreadyGranted()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var service = new MacOSDesktopPlatform();
        if (service.GetPermissionStatus() != ScreenCapturePermissionStatus.Granted)
        {
            return;
        }

        using var capturedScreen = await service.CaptureCurrentDisplayAsync(
            TestContext.Current.CancellationToken);

        Assert.True(capturedScreen.Frame.PhysicalSize.Width > 0);
        Assert.True(capturedScreen.Frame.PhysicalSize.Height > 0);
        Assert.Equal(
            capturedScreen.Frame.Stride * capturedScreen.Frame.PhysicalSize.Height,
            capturedScreen.Frame.Pixels.Length);
        Assert.Equal(
            capturedScreen.Frame.PhysicalSize.Width / capturedScreen.Frame.LogicalSize.Width,
            capturedScreen.Frame.ScaleX,
            precision: 6);
        Assert.InRange(
            capturedScreen.CursorPosition.X,
            0,
            capturedScreen.Frame.PhysicalSize.Width);
        Assert.InRange(
            capturedScreen.CursorPosition.Y,
            0,
            capturedScreen.Frame.PhysicalSize.Height);
        Assert.NotEmpty(capturedScreen.WindowCandidates);
        Assert.All(capturedScreen.WindowCandidates, candidate =>
        {
            Assert.Equal(Snaploom.Core.ScreenshotWindowExclusion.None, candidate.Exclusion);
            Assert.True(candidate.Bounds.Width > 0);
            Assert.True(candidate.Bounds.Height > 0);
            Assert.InRange(candidate.Bounds.X, 0, capturedScreen.Frame.PhysicalSize.Width - 1);
            Assert.InRange(candidate.Bounds.Y, 0, capturedScreen.Frame.PhysicalSize.Height - 1);
        });
    }
}
