using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Snaploom.App;

internal sealed class ScreenshotMoveCursor : IDisposable
{
    internal const int BitmapSize = ScreenshotUiTheme.MoveCursorBitmapSize;
    internal static readonly PixelPoint HotSpot = new(BitmapSize / 2, BitmapSize / 2);

    private readonly RenderTargetBitmap _bitmap;

    internal ScreenshotMoveCursor()
    {
        _bitmap = RenderBitmap();
        Cursor = new Cursor(_bitmap, HotSpot);
    }

    internal Cursor Cursor { get; }

    public void Dispose()
    {
        Cursor.Dispose();
        _bitmap.Dispose();
    }

    internal static RenderTargetBitmap RenderBitmap()
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(BitmapSize, BitmapSize));
        using var context = bitmap.CreateDrawingContext();
        context.DrawGeometry(
            brush: null,
            new Pen(
                ScreenshotUiTheme.MoveCursorOutlineBrush,
                ScreenshotUiTheme.MoveCursorOutlineWidth),
            ScreenshotUiTheme.MoveCursorGeometry);
        context.DrawGeometry(
            brush: null,
            new Pen(
                ScreenshotUiTheme.PrimaryTextBrush,
                ScreenshotUiTheme.MoveCursorStrokeWidth),
            ScreenshotUiTheme.MoveCursorGeometry);
        return bitmap;
    }
}
