using Snaploom.Core;

namespace Snaploom.Core.Tests;

public sealed class ScreenshotPointerGestureTests
{
    [Fact]
    public void DragStartsOnlyAfterFourLogicalPixels()
    {
        var origin = new LogicalPoint(10, 10);

        Assert.False(
            ScreenshotPointerGesture.HasExceededDragThreshold(
                origin,
                new LogicalPoint(13.99, 10)));
        Assert.True(
            ScreenshotPointerGesture.HasExceededDragThreshold(
                origin,
                new LogicalPoint(14, 10)));
    }
}
