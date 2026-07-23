using System.Buffers.Binary;
using Snaploom.Platform.Abstractions;
using Snaploom.Platform.MacOS;

namespace Snaploom.IntegrationTests;

public sealed class MacOSNativeBridgeTests
{
    [Fact]
    public void NativeBridgeProvidesCompactFrameResizeCursorImages()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(15))
        {
            return;
        }

        foreach (var position in Enum.GetValues<MacOSFrameResizeCursorPosition>())
        {
            Assert.True(
                MacOSFrameResizeCursorImageProvider.TryCreate(position, out var image));

            Assert.True(image.Png.Length >= 24);
            var width = BinaryPrimitives.ReadInt32BigEndian(image.Png.AsSpan(16, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(image.Png.AsSpan(20, 4));
            Assert.InRange(width, 16, 24);
            Assert.Equal(width, height);
            Assert.Equal(0, width % 2);
            Assert.Equal(width / 2, image.HotSpotX);
            Assert.Equal(height / 2, image.HotSpotY);
        }
    }

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

        if (Environment.GetEnvironmentVariable(
                "SNAPLOOM_RUN_SCREEN_CAPTURE_INTEGRATION") != "1")
        {
            return;
        }

        var service = new MacOSDesktopPlatform();
        if (service.GetPermissionStatus() != ScreenCapturePermissionStatus.Granted)
        {
            return;
        }

        CapturedScreen capturedScreen;
        try
        {
            capturedScreen = await service.CaptureCurrentDisplayAsync(
                TestContext.Current.CancellationToken);
        }
        catch (ScreenCaptureException exception) when (exception.PermissionDenied)
        {
            // TCC can be revoked between the non-prompting preflight check and ScreenCaptureKit.
            return;
        }

        using (capturedScreen)
        {
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

    [Fact]
    public void NativeBridgeExposesLoginItemAndWakeMonitoringServices()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        using var platform = new MacOSDesktopPlatform();
        var autoStartService = Assert.IsAssignableFrom<IAutoStartService>(platform);
        var resumeService = Assert.IsAssignableFrom<ISystemResumeService>(platform);

        _ = autoStartService.IsAutoStartEnabled();
        resumeService.StartMonitoring(() => { });
        resumeService.StopMonitoring();
    }
}
