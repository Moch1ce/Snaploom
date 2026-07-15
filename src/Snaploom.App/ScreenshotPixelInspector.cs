using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Snaploom.Core;

namespace Snaploom.App;

internal sealed class ScreenshotPixelInspector
{
    private static readonly Pen BorderPen = new(ScreenshotUiTheme.FloatingBorderBrush, 1);
    private static readonly Pen CrosshairPen = new(ScreenshotUiTheme.AccentBrush, 1);
    private static readonly Typeface NormalTypeface = new(
        FontFamily.Default,
        FontStyle.Normal,
        FontWeight.Normal,
        FontStretch.Normal);
    private static readonly Typeface ValueTypeface = new(
        FontFamily.Default,
        FontStyle.Normal,
        FontWeight.SemiBold,
        FontStretch.Normal);
    private const double Width = 132;
    private const double PreviewHeight = 132;
    private const double DetailsHeight = 76;
    private const double PointerGap = 16;
    private const double ScreenMargin = 8;
    private const double CornerRadius = 8;
    private const int SampleDiameter = 55;

    private readonly CapturedFrame _frame;
    private readonly IImage _bitmap;
    private Point? _pointerPosition;
    private PhysicalPoint? _samplePoint;

    internal static double Magnification => Width / SampleDiameter;

    public ScreenshotPixelInspector(CapturedFrame frame, IImage bitmap)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(bitmap);
        _frame = frame;
        _bitmap = bitmap;
    }

    public CapturedColor? SampledColor =>
        _samplePoint is { } point ? _frame.SamplePixel(point) : null;

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
        var preview = new Rect(card.X, card.Y, card.Width, PreviewHeight);
        var details = new Rect(card.X, preview.Bottom, card.Width, DetailsHeight);

        context.DrawRectangle(
            ScreenshotUiTheme.FloatingSurfaceBrush,
            BorderPen,
            card,
            CornerRadius,
            CornerRadius,
            ScreenshotUiTheme.FloatingShadow);

        using (context.PushClip(new RoundedRect(card, CornerRadius)))
        {
            DrawMagnifiedPixels(context, preview, samplePoint);
            context.DrawRectangle(ScreenshotUiTheme.FloatingSurfaceBrush, pen: null, details);
        }

        context.DrawLine(
            BorderPen,
            new Point(details.Left, details.Top),
            new Point(details.Right, details.Top));

        var color = _frame.SamplePixel(samplePoint);
        DrawText(
            context,
            ScreenshotUiText.CoordinateLabel,
            details.X + 10,
            details.Y + 8,
            ScreenshotUiTheme.PrimaryTextBrush);
        DrawText(
            context,
            $"{samplePoint.X}, {samplePoint.Y}",
            details.X + 58,
            details.Y + 8,
            ScreenshotUiTheme.PrimaryTextBrush,
            ValueTypeface);
        DrawText(
            context,
            ScreenshotUiText.ColorLabel,
            details.X + 10,
            details.Y + 29,
            ScreenshotUiTheme.PrimaryTextBrush);
        DrawText(
            context,
            color.Hex,
            details.X + 58,
            details.Y + 29,
            ScreenshotUiTheme.PrimaryTextBrush,
            ValueTypeface);
        DrawText(
            context,
            ScreenshotUiText.CopyColorHint,
            details.X + 10,
            details.Y + 52,
            ScreenshotUiTheme.MutedTextBrush,
            fontSize: 11);
    }

    private void DrawMagnifiedPixels(
        DrawingContext context,
        Rect preview,
        PhysicalPoint samplePoint)
    {
        var sourceWidth = Math.Min(SampleDiameter, _frame.PhysicalSize.Width);
        var sourceHeight = Math.Min(SampleDiameter, _frame.PhysicalSize.Height);
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
        var totalHeight = PreviewHeight + DetailsHeight;
        var x = pointer.X + PointerGap;
        var y = pointer.Y + PointerGap;

        if (x + Width + ScreenMargin > bounds.Width)
        {
            x = pointer.X - PointerGap - Width;
        }

        if (y + totalHeight + ScreenMargin > bounds.Height)
        {
            y = pointer.Y - PointerGap - totalHeight;
        }

        return new Rect(
            Math.Clamp(
                x,
                ScreenMargin,
                Math.Max(ScreenMargin, bounds.Width - Width - ScreenMargin)),
            Math.Clamp(
                y,
                ScreenMargin,
                Math.Max(ScreenMargin, bounds.Height - totalHeight - ScreenMargin)),
            Width,
            totalHeight);
    }

    private static void DrawText(
        DrawingContext context,
        string text,
        double x,
        double y,
        IBrush brush,
        Typeface? typeface = null,
        double fontSize = 12)
    {
        var formattedText = new FormattedText(
            text,
            CultureInfo.GetCultureInfo("zh-CN"),
            FlowDirection.LeftToRight,
            typeface ?? NormalTypeface,
            fontSize,
            brush);
        context.DrawText(formattedText, new Point(x, y));
    }
}
