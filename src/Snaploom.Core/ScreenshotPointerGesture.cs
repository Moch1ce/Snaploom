namespace Snaploom.Core;

public static class ScreenshotPointerGesture
{
    public const double MinimumDragDistance = 4;

    public static bool HasExceededDragThreshold(LogicalPoint origin, LogicalPoint current)
    {
        var deltaX = current.X - origin.X;
        var deltaY = current.Y - origin.Y;
        return (deltaX * deltaX) + (deltaY * deltaY) >=
               MinimumDragDistance * MinimumDragDistance;
    }
}
