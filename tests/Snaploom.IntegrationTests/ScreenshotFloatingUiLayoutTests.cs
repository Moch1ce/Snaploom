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
        Assert.Equal(new Point(320, 408), placement.ToolbarOrigin);
    }

    [Fact]
    public void MovesToolbarAboveSelectionWhenNeitherBottomNorSelectionCanContainIt()
    {
        var placement = Snaploom.App.ScreenshotFloatingUiLayout.Place(
            new Rect(100, 620, 300, 60),
            new Rect(0, 0, 800, 700),
            new Size(80, 24),
            new Size(380, 48));

        Assert.Equal(new Point(100, 592), placement.BadgeOrigin);
        Assert.Equal(new Point(20, 564), placement.ToolbarOrigin);
    }

    [Fact]
    public void PlacesToolbarInsideTheBottomRightOfALargeSelection()
    {
        var placement = Snaploom.App.ScreenshotFloatingUiLayout.Place(
            new Rect(100, 300, 600, 350),
            new Rect(0, 0, 1000, 650),
            new Size(80, 24),
            new Size(392, 44));

        Assert.Equal(new Point(100, 272), placement.BadgeOrigin);
        Assert.Equal(new Point(300, 598), placement.ToolbarOrigin);
    }

    [Fact]
    public void KeepsFloatingUiInsideTheDisplayWorkingArea()
    {
        var placement = Snaploom.App.ScreenshotFloatingUiLayout.Place(
            new Rect(20, 600, 500, 80),
            new Rect(0, 24, 1000, 626),
            new Size(80, 24),
            new Size(392, 44));

        Assert.Equal(new Point(20, 572), placement.BadgeOrigin);
        Assert.Equal(new Point(120, 548), placement.ToolbarOrigin);
    }

    [Fact]
    public void DoesNotPlaceAnInsideToolbarAgainstTheWorkingAreaEdge()
    {
        var placement = Snaploom.App.ScreenshotFloatingUiLayout.Place(
            new Rect(0, 300, 400, 350),
            new Rect(0, 0, 1000, 650),
            new Size(80, 24),
            new Size(392, 44));

        Assert.Equal(new Point(8, 248), placement.ToolbarOrigin);
    }
}
