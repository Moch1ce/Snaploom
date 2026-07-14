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
    }
}
