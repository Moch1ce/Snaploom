using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Snaploom.Core;

namespace Snaploom.App;

internal sealed class ScreenshotPixelInspector
{
    private static readonly Pen BorderPen = new(
        ScreenshotUiTheme.FloatingBorderBrush,
        ScreenshotUiTheme.FloatingBorderThickness);
    private static readonly Pen CrosshairPen = new(
        ScreenshotUiTheme.AccentBrush,
        ScreenshotUiTheme.FloatingBorderThickness);

    private readonly CapturedFrame _frame;
    private readonly IImage _bitmap;
    private Point? _pointerPosition;
    private PhysicalPoint? _samplePoint;

    internal static double Magnification =>
        ScreenshotUiTheme.PixelInspectorWidth /
        ScreenshotUiTheme.PixelInspectorSampleDiameter;

    public ScreenshotPixelInspector(CapturedFrame frame, IImage bitmap)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(bitmap);
        _frame = frame;
        _bitmap = bitmap;
    }

    public Point UpdatePointer(Point point, Size bounds)
    {
        var clamped = new Point(
            Math.Clamp(point.X, 0, bounds.Width),
            Math.Clamp(point.Y, 0, bounds.Height));
        _pointerPosition = clamped;
        _samplePoint = new PhysicalPoint(
            Math.Clamp(
                checked((int)Math.Floor(clamped.X * _frame.ScaleX)),
                0,
                _frame.PhysicalSize.Width - 1),
            Math.Clamp(
                checked((int)Math.Floor(clamped.Y * _frame.ScaleY)),
                0,
                _frame.PhysicalSize.Height - 1));
        return clamped;
    }

    public void Render(DrawingContext context, Size bounds)
    {
        if (_pointerPosition is not { } pointer ||
            _samplePoint is not { } samplePoint)
        {
            return;
        }

        var card = PlaceCard(pointer, bounds);

        context.DrawRectangle(
            ScreenshotUiTheme.FloatingSurfaceBrush,
            BorderPen,
            card,
            ScreenshotUiTheme.PixelInspectorCornerRadius,
            ScreenshotUiTheme.PixelInspectorCornerRadius,
            ScreenshotUiTheme.FloatingShadow);

        using (context.PushClip(
                   new RoundedRect(card, ScreenshotUiTheme.PixelInspectorCornerRadius)))
        {
            DrawMagnifiedPixels(context, card, samplePoint);
        }
    }

    private void DrawMagnifiedPixels(
        DrawingContext context,
        Rect preview,
        PhysicalPoint samplePoint)
    {
        var sourceWidth = Math.Min(
            ScreenshotUiTheme.PixelInspectorSampleDiameter,
            _frame.PhysicalSize.Width);
        var sourceHeight = Math.Min(
            ScreenshotUiTheme.PixelInspectorSampleDiameter,
            _frame.PhysicalSize.Height);
        var sourceX = Math.Clamp(
            samplePoint.X - (sourceWidth / 2),
            0,
            _frame.PhysicalSize.Width - sourceWidth);
        var sourceY = Math.Clamp(
            samplePoint.Y - (sourceHeight / 2),
            0,
            _frame.PhysicalSize.Height - sourceHeight);
        var source = new Rect(sourceX, sourceY, sourceWidth, sourceHeight);

        using (context.PushRenderOptions(
                   new RenderOptions
                   {
                       BitmapInterpolationMode = BitmapInterpolationMode.None,
                   }))
        {
            context.DrawImage(_bitmap, source, preview);
        }

        var crosshairX = preview.X +
            (((samplePoint.X + 0.5) - source.X) / source.Width * preview.Width);
        var crosshairY = preview.Y +
            (((samplePoint.Y + 0.5) - source.Y) / source.Height * preview.Height);
        context.DrawLine(
            CrosshairPen,
            new Point(crosshairX, preview.Top),
            new Point(crosshairX, preview.Bottom));
        context.DrawLine(
            CrosshairPen,
            new Point(preview.Left, crosshairY),
            new Point(preview.Right, crosshairY));
    }

    private static Rect PlaceCard(Point pointer, Size bounds)
    {
        const double totalHeight = ScreenshotUiTheme.PixelInspectorHeight;
        var x = pointer.X + ScreenshotUiTheme.PixelInspectorPointerGap;
        var y = pointer.Y + ScreenshotUiTheme.PixelInspectorPointerGap;

        if (x + ScreenshotUiTheme.PixelInspectorWidth +
            ScreenshotUiTheme.PixelInspectorScreenMargin > bounds.Width)
        {
            x = pointer.X - ScreenshotUiTheme.PixelInspectorPointerGap -
                ScreenshotUiTheme.PixelInspectorWidth;
        }

        if (y + totalHeight + ScreenshotUiTheme.PixelInspectorScreenMargin > bounds.Height)
        {
            y = pointer.Y - ScreenshotUiTheme.PixelInspectorPointerGap - totalHeight;
        }

        return new Rect(
            Math.Clamp(
                x,
                ScreenshotUiTheme.PixelInspectorScreenMargin,
                Math.Max(
                    ScreenshotUiTheme.PixelInspectorScreenMargin,
                    bounds.Width - ScreenshotUiTheme.PixelInspectorWidth -
                    ScreenshotUiTheme.PixelInspectorScreenMargin)),
            Math.Clamp(
                y,
                ScreenshotUiTheme.PixelInspectorScreenMargin,
                Math.Max(
                    ScreenshotUiTheme.PixelInspectorScreenMargin,
                    bounds.Height - totalHeight -
                    ScreenshotUiTheme.PixelInspectorScreenMargin)),
            ScreenshotUiTheme.PixelInspectorWidth,
            totalHeight);
    }
}
