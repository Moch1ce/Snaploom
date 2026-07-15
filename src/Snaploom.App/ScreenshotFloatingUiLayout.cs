using Avalonia;

namespace Snaploom.App;

internal readonly record struct ScreenshotFloatingUiPlacement(
    Point BadgeOrigin,
    Point ToolbarOrigin);

internal static class ScreenshotFloatingUiLayout
{
    private const double EdgeMargin = 8;
    private const double BadgeGap = 4;
    private const double ToolbarGap = 8;
    private const double ToolbarInsideInset = 16;

    internal static ScreenshotFloatingUiPlacement Place(
        Rect selection,
        Rect availableBounds,
        Size badgeSize,
        Size toolbarSize)
    {
        var badgeX = ClampToAvailableBounds(
            selection.Left,
            badgeSize.Width,
            availableBounds.Left,
            availableBounds.Right);
        var badgeY = selection.Top - badgeSize.Height - BadgeGap;
        if (badgeY < availableBounds.Top + EdgeMargin)
        {
            badgeY = Math.Min(
                selection.Top + BadgeGap,
                Math.Max(
                    availableBounds.Top + EdgeMargin,
                    availableBounds.Bottom - badgeSize.Height - EdgeMargin));
        }

        var toolbarX = ClampToAvailableBounds(
            selection.Right - toolbarSize.Width,
            toolbarSize.Width,
            availableBounds.Left,
            availableBounds.Right);
        var toolbarY = selection.Bottom + ToolbarGap;
        if (toolbarY + toolbarSize.Height + EdgeMargin > availableBounds.Bottom)
        {
            var visibleSelection = selection.Intersect(availableBounds);
            var insideX = visibleSelection.Right - toolbarSize.Width - ToolbarInsideInset;
            var insideY = visibleSelection.Bottom - toolbarSize.Height - ToolbarInsideInset;
            var canPlaceInside = visibleSelection.Width >= toolbarSize.Width + ToolbarInsideInset &&
                                 visibleSelection.Height >= toolbarSize.Height + ToolbarInsideInset &&
                                 insideX >= availableBounds.Left + EdgeMargin &&
                                 insideY >= availableBounds.Top + EdgeMargin;
            if (canPlaceInside)
            {
                toolbarX = insideX;
                toolbarY = insideY;
            }
            else
            {
                toolbarY = selection.Top - toolbarSize.Height - ToolbarGap;
            }
        }

        toolbarY = Math.Clamp(
            toolbarY,
            availableBounds.Top + EdgeMargin,
            Math.Max(
                availableBounds.Top + EdgeMargin,
                availableBounds.Bottom - toolbarSize.Height - EdgeMargin));

        return new ScreenshotFloatingUiPlacement(
            new Point(badgeX, badgeY),
            new Point(toolbarX, toolbarY));
    }

    private static double ClampToAvailableBounds(
        double value,
        double width,
        double availableLeft,
        double availableRight) =>
        Math.Clamp(
            value,
            availableLeft + EdgeMargin,
            Math.Max(availableLeft + EdgeMargin, availableRight - width - EdgeMargin));
}
