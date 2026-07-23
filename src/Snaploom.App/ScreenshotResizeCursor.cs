using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Snaploom.Platform.MacOS;

namespace Snaploom.App;

internal static class ScreenshotResizeCursor
{
    internal static Cursor Create(StandardCursorType cursorType)
    {
        if (!OperatingSystem.IsMacOS() ||
            !TryGetMacOSPosition(cursorType, out var position))
        {
            return new Cursor(cursorType);
        }

        if (MacOSFrameResizeCursorImageProvider.TryCreate(position, out var image))
        {
            using var stream = new MemoryStream(image.Png, writable: false);
            using var bitmap = new Bitmap(stream);
            return new Cursor(bitmap, new PixelPoint(image.HotSpotX, image.HotSpotY));
        }

        return CreateFallback(cursorType);
    }

    private static Cursor CreateFallback(StandardCursorType cursorType)
    {
        var geometry = GetFallbackGeometry(cursorType);
        using var bitmap = new RenderTargetBitmap(
            new PixelSize(
                ScreenshotUiTheme.ResizeCursorFallbackSize,
                ScreenshotUiTheme.ResizeCursorFallbackSize));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.DrawGeometry(
                ScreenshotUiTheme.ResizeCursorFallbackFillBrush,
                new Pen(
                    ScreenshotUiTheme.ResizeCursorFallbackOutlineBrush,
                    ScreenshotUiTheme.ResizeCursorFallbackOutlineThickness),
                geometry);
        }

        return new Cursor(
            bitmap,
            new PixelPoint(
                ScreenshotUiTheme.ResizeCursorFallbackSize / 2,
                ScreenshotUiTheme.ResizeCursorFallbackSize / 2));
    }

    internal static StreamGeometry GetFallbackGeometry(
        StandardCursorType cursorType) =>
        cursorType switch
        {
            StandardCursorType.TopLeftCorner =>
                ScreenshotUiTheme.ResizeCursorTopLeftFallbackGeometry,
            StandardCursorType.TopRightCorner =>
                ScreenshotUiTheme.ResizeCursorTopRightFallbackGeometry,
            StandardCursorType.BottomRightCorner =>
                ScreenshotUiTheme.ResizeCursorBottomRightFallbackGeometry,
            StandardCursorType.BottomLeftCorner =>
                ScreenshotUiTheme.ResizeCursorBottomLeftFallbackGeometry,
            _ => throw new ArgumentOutOfRangeException(nameof(cursorType)),
        };

    private static bool TryGetMacOSPosition(
        StandardCursorType cursorType,
        out MacOSFrameResizeCursorPosition position)
    {
        position = cursorType switch
        {
            StandardCursorType.TopLeftCorner =>
                MacOSFrameResizeCursorPosition.TopLeft,
            StandardCursorType.TopRightCorner =>
                MacOSFrameResizeCursorPosition.TopRight,
            StandardCursorType.BottomRightCorner =>
                MacOSFrameResizeCursorPosition.BottomRight,
            StandardCursorType.BottomLeftCorner =>
                MacOSFrameResizeCursorPosition.BottomLeft,
            _ => default,
        };
        return cursorType is
            StandardCursorType.TopLeftCorner or
            StandardCursorType.TopRightCorner or
            StandardCursorType.BottomRightCorner or
            StandardCursorType.BottomLeftCorner;
    }
}
