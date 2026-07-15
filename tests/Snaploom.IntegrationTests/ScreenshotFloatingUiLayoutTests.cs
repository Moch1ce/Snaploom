using Avalonia;

namespace Snaploom.IntegrationTests;

public sealed class ScreenshotFloatingUiLayoutTests
{
    [Fact]
    public void PlacesBadgeAboveLeftAndToolbarBelowRightOfSelection()
    {
        var placement = Snaploom.App.ScreenshotFloatingUiLayout.Place(
            new Rect(200, 100, 500, 300),
            new Rect(0, 0, 1000, 700),
            new Size(80, 24),
            new Size(380, 48));

        Assert.Equal(new Point(200, 72), placement.BadgeOrigin);
        Assert.Equal(new Point(320, 412), placement.ToolbarOrigin);
    }

    [Fact]
    public void MovesToolbarAboveSelectionWhenBottomSpaceIsInsufficient()
    {
        var placement = Snaploom.App.ScreenshotFloatingUiLayout.Place(
            new Rect(100, 620, 300, 60),
            new Rect(0, 0, 800, 700),
            new Size(80, 24),
            new Size(380, 48));

        Assert.Equal(new Point(100, 592), placement.BadgeOrigin);
        Assert.Equal(new Point(20, 560), placement.ToolbarOrigin);
    }

    [Fact]
    public void KeepsFloatingUiInsideTheDisplayWorkingArea()
    {
        var placement = Snaploom.App.ScreenshotFloatingUiLayout.Place(
            new Rect(20, 600, 500, 80),
            new Rect(0, 24, 1000, 626),
            new Size(80, 24),
            new Size(406, 48));

        Assert.Equal(new Point(20, 572), placement.BadgeOrigin);
        Assert.Equal(new Point(114, 540), placement.ToolbarOrigin);
    }
}
