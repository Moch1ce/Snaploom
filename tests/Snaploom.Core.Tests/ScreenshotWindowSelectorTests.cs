using Snaploom.Core;

namespace Snaploom.Core.Tests;

public sealed class ScreenshotWindowSelectorTests
{
    [Theory]
    [InlineData(ScreenshotWindowExclusion.Hidden)]
    [InlineData(ScreenshotWindowExclusion.Minimized)]
    [InlineData(ScreenshotWindowExclusion.NonNormal)]
    [InlineData(ScreenshotWindowExclusion.OwnApplication)]
    [InlineData(ScreenshotWindowExclusion.SystemUi)]
    [InlineData(ScreenshotWindowExclusion.ToolWindow)]
    [InlineData(ScreenshotWindowExclusion.Transparent)]
    [InlineData(ScreenshotWindowExclusion.ClickThrough)]
    public void ExcludesNonCapturableWindows(ScreenshotWindowExclusion exclusion)
    {
        var candidates = new[]
        {
            new ScreenshotWindowCandidate(
                1,
                new PhysicalRect(10, 10, 50, 40),
                ZOrder: 0,
                exclusion),
        };

        Assert.Empty(
            ScreenshotWindowSelector.GetEligibleWindows(
                candidates,
                new PhysicalSize(100, 80)));
    }

    [Fact]
    public void HitTestSelectsTheTopmostEligibleWindowDeterministically()
    {
        var candidates = new[]
        {
            new ScreenshotWindowCandidate(
                30,
                new PhysicalRect(20, 20, 50, 40),
                ZOrder: 2,
                ScreenshotWindowExclusion.None),
            new ScreenshotWindowCandidate(
                10,
                new PhysicalRect(10, 10, 50, 40),
                ZOrder: 0,
                ScreenshotWindowExclusion.OwnApplication),
            new ScreenshotWindowCandidate(
                20,
                new PhysicalRect(15, 15, 50, 40),
                ZOrder: 1,
                ScreenshotWindowExclusion.None),
        };

        var target = ScreenshotWindowSelector.HitTest(
            candidates,
            new PhysicalPoint(25, 25),
            new PhysicalSize(100, 80));

        Assert.Equal(ScreenshotSnapTargetKind.Window, target.Kind);
        Assert.Equal(20, target.WindowId);
        Assert.Equal(new PhysicalRect(15, 15, 50, 40), target.Bounds);
    }

    [Fact]
    public void HitTestFallsBackToTheCurrentDisplayOnDesktopSpace()
    {
        var target = ScreenshotWindowSelector.HitTest(
            Array.Empty<ScreenshotWindowCandidate>(),
            new PhysicalPoint(90, 70),
            new PhysicalSize(100, 80));

        Assert.Equal(ScreenshotSnapTargetKind.Display, target.Kind);
        Assert.Null(target.WindowId);
        Assert.Equal(new PhysicalRect(0, 0, 100, 80), target.Bounds);
    }

    [Fact]
    public void ClipsPartiallyVisibleWindowsToTheCurrentDisplay()
    {
        var candidates = new[]
        {
            new ScreenshotWindowCandidate(
                1,
                new PhysicalRect(-20, 10, 50, 40),
                ZOrder: 0,
                ScreenshotWindowExclusion.None),
        };

        var eligible = ScreenshotWindowSelector.GetEligibleWindows(
            candidates,
            new PhysicalSize(100, 80));

        Assert.Equal(new PhysicalRect(0, 10, 30, 40), Assert.Single(eligible).Bounds);
    }
}
