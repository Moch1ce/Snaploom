using SkiaSharp;
using Snaploom.Core;

namespace Snaploom.Rendering;

public sealed record AnnotationRaster(
    int Width,
    int Height,
    int Stride,
    byte[] Pixels);

public static class ScreenshotAnnotationRenderer
{
    private const double ArrowHeadLength = 14;
    private const double ArrowHeadHalfAngleDegrees = 28;

    public static AnnotationRaster RenderBgra(
        int width,
        int height,
        double scaleX,
        double scaleY,
        IEnumerable<IScreenshotAnnotation> annotations)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scaleX);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scaleY);
        ArgumentNullException.ThrowIfNull(annotations);

        var imageInfo = new SKImageInfo(
            width,
            height,
            SKColorType.Bgra8888,
            SKAlphaType.Premul);
        using var bitmap = new SKBitmap(imageInfo);
        bitmap.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(bitmap))
        {
            Draw(canvas, scaleX, scaleY, annotations);
        }

        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        var source = bitmap.GetPixelSpan();
        for (var row = 0; row < height; row++)
        {
            source.Slice(row * bitmap.RowBytes, stride)
                .CopyTo(pixels.AsSpan(row * stride, stride));
        }

        return new AnnotationRaster(width, height, stride, pixels);
    }

    internal static void Draw(
        SKCanvas canvas,
        double scaleX,
        double scaleY,
        IEnumerable<IScreenshotAnnotation> annotations)
    {
        foreach (var annotation in annotations)
        {
            using var paint = CreatePaint(annotation.Style, scaleX, scaleY);
            switch (annotation)
            {
                case ScreenshotRectangleAnnotation rectangle:
                    DrawRectangle(canvas, paint, rectangle, scaleX, scaleY);
                    break;

                case ScreenshotArrowAnnotation arrow:
                    DrawArrow(canvas, paint, arrow, scaleX, scaleY);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported screenshot annotation: {annotation.GetType().Name}.");
            }
        }
    }

    private static SKPaint CreatePaint(
        ScreenshotAnnotationStyle style,
        double scaleX,
        double scaleY)
    {
        var color = ScreenshotAnnotationPalette.GetColor(style.Color);
        return new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(color.Red, color.Green, color.Blue),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)(style.LineWidth * ((scaleX + scaleY) / 2)),
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
    }

    private static void DrawRectangle(
        SKCanvas canvas,
        SKPaint paint,
        ScreenshotRectangleAnnotation rectangle,
        double scaleX,
        double scaleY)
    {
        var left = Math.Min(rectangle.Start.X, rectangle.End.X) * scaleX;
        var top = Math.Min(rectangle.Start.Y, rectangle.End.Y) * scaleY;
        var right = Math.Max(rectangle.Start.X, rectangle.End.X) * scaleX;
        var bottom = Math.Max(rectangle.Start.Y, rectangle.End.Y) * scaleY;
        canvas.DrawRect(
            new SKRect((float)left, (float)top, (float)right, (float)bottom),
            paint);
    }

    private static void DrawArrow(
        SKCanvas canvas,
        SKPaint paint,
        ScreenshotArrowAnnotation arrow,
        double scaleX,
        double scaleY)
    {
        var start = ToSkPoint(arrow.Start, scaleX, scaleY);
        var end = ToSkPoint(arrow.End, scaleX, scaleY);
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (length <= 0)
        {
            return;
        }

        canvas.DrawLine(start, end, paint);

        var scale = (scaleX + scaleY) / 2;
        var headLength = Math.Min(ArrowHeadLength * scale, length * 0.45);
        var direction = Math.Atan2(deltaY, deltaX);
        var angle = ArrowHeadHalfAngleDegrees * Math.PI / 180;
        var firstWing = new SKPoint(
            (float)(end.X - (headLength * Math.Cos(direction - angle))),
            (float)(end.Y - (headLength * Math.Sin(direction - angle))));
        var secondWing = new SKPoint(
            (float)(end.X - (headLength * Math.Cos(direction + angle))),
            (float)(end.Y - (headLength * Math.Sin(direction + angle))));
        using var path = new SKPath();
        path.MoveTo(firstWing);
        path.LineTo(end);
        path.LineTo(secondWing);
        canvas.DrawPath(path, paint);
    }

    private static SKPoint ToSkPoint(LogicalPoint point, double scaleX, double scaleY) =>
        new((float)(point.X * scaleX), (float)(point.Y * scaleY));
}
