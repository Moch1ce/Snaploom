using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Snaploom.App;

internal sealed class ScreenshotMoveCursor : IDisposable
{
    internal const int BitmapSize = 28;
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
        DrawFourWayArrow(context, new Pen(Brushes.White, 4));
        DrawFourWayArrow(context, new Pen(Brushes.Black, 2));
        return bitmap;
    }

    private static void DrawFourWayArrow(DrawingContext context, Pen pen)
    {
        const double center = BitmapSize / 2d;
        const double tip = 3;
        const double oppositeTip = BitmapSize - tip;
        const double arrowWing = 5;

        context.DrawLine(pen, new Point(center, tip), new Point(center, oppositeTip));
        context.DrawLine(pen, new Point(tip, center), new Point(oppositeTip, center));

        context.DrawLine(
            pen,
            new Point(center, tip),
            new Point(center - arrowWing, tip + arrowWing));
        context.DrawLine(
            pen,
            new Point(center, tip),
            new Point(center + arrowWing, tip + arrowWing));
        context.DrawLine(
            pen,
            new Point(center, oppositeTip),
            new Point(center - arrowWing, oppositeTip - arrowWing));
        context.DrawLine(
            pen,
            new Point(center, oppositeTip),
            new Point(center + arrowWing, oppositeTip - arrowWing));
        context.DrawLine(
            pen,
            new Point(tip, center),
            new Point(tip + arrowWing, center - arrowWing));
        context.DrawLine(
            pen,
            new Point(tip, center),
            new Point(tip + arrowWing, center + arrowWing));
        context.DrawLine(
            pen,
            new Point(oppositeTip, center),
            new Point(oppositeTip - arrowWing, center - arrowWing));
        context.DrawLine(
            pen,
            new Point(oppositeTip, center),
            new Point(oppositeTip - arrowWing, center + arrowWing));
    }
}
