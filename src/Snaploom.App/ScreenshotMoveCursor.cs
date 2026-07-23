using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Snaploom.App;

internal static class ScreenshotMoveCursor
{
    internal static Cursor Create()
    {
        var size = ScreenshotUiTheme.MoveCursorSize;
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.DrawGeometry(
                ScreenshotUiTheme.MoveCursorFillBrush,
                pen: null,
                ScreenshotUiTheme.MoveCursorGeometry);
        }

        return new Cursor(bitmap, new PixelPoint(size / 2, size / 2));
    }
}
