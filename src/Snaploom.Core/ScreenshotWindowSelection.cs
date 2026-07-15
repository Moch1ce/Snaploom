namespace Snaploom.Core;

[Flags]
public enum ScreenshotWindowExclusion
{
    None = 0,
    Hidden = 1 << 0,
    Minimized = 1 << 1,
    NonNormal = 1 << 2,
    OwnApplication = 1 << 3,
    SystemUi = 1 << 4,
    ToolWindow = 1 << 5,
    Transparent = 1 << 6,
    ClickThrough = 1 << 7,
}

public readonly record struct ScreenshotWindowCandidate(
    long Id,
    PhysicalRect Bounds,
    int ZOrder,
    ScreenshotWindowExclusion Exclusion);

public enum ScreenshotSnapTargetKind
{
    Window,
    Display,
}

public readonly record struct ScreenshotSnapTarget(
    ScreenshotSnapTargetKind Kind,
    PhysicalRect Bounds,
    long? WindowId);

public static class ScreenshotWindowSelector
{
    public static IReadOnlyList<ScreenshotWindowCandidate> GetEligibleWindows(
        IEnumerable<ScreenshotWindowCandidate> candidates,
        PhysicalSize displaySize)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var displayBounds = new PhysicalRect(0, 0, displaySize.Width, displaySize.Height);
        return candidates
            .Where(candidate =>
                candidate.Exclusion == ScreenshotWindowExclusion.None &&
                candidate.Bounds.Width > 0 &&
                candidate.Bounds.Height > 0)
            .Select(candidate => candidate with
            {
                Bounds = Intersect(candidate.Bounds, displayBounds),
            })
            .Where(candidate => candidate.Bounds.Width > 0 && candidate.Bounds.Height > 0)
            .OrderBy(candidate => candidate.ZOrder)
            .ThenBy(candidate => candidate.Id)
            .ToArray();
    }

    public static ScreenshotSnapTarget HitTest(
        IEnumerable<ScreenshotWindowCandidate> candidates,
        PhysicalPoint point,
        PhysicalSize displaySize)
    {
        foreach (var candidate in GetEligibleWindows(candidates, displaySize))
        {
            if (Contains(candidate.Bounds, point))
            {
                return new ScreenshotSnapTarget(
                    ScreenshotSnapTargetKind.Window,
                    candidate.Bounds,
                    candidate.Id);
            }
        }

        return new ScreenshotSnapTarget(
            ScreenshotSnapTargetKind.Display,
            new PhysicalRect(0, 0, displaySize.Width, displaySize.Height),
            WindowId: null);
    }

    private static bool Contains(PhysicalRect rect, PhysicalPoint point) =>
        point.X >= rect.X &&
        point.X < rect.X + rect.Width &&
        point.Y >= rect.Y &&
        point.Y < rect.Y + rect.Height;

    private static PhysicalRect Intersect(PhysicalRect first, PhysicalRect second)
    {
        var left = Math.Max(first.X, second.X);
        var top = Math.Max(first.Y, second.Y);
        var right = Math.Min(first.X + first.Width, second.X + second.Width);
        var bottom = Math.Min(first.Y + first.Height, second.Y + second.Height);
        return new PhysicalRect(
            left,
            top,
            Math.Max(0, right - left),
            Math.Max(0, bottom - top));
    }
}
