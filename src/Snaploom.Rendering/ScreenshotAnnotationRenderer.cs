using SkiaSharp;
using Snaploom.Core;
using System.Globalization;

namespace Snaploom.Rendering;

public sealed record AnnotationRaster(
    int Width,
    int Height,
    int Stride,
    byte[] Pixels);

public readonly record struct ScreenshotTextBounds(
    double X,
    double Y,
    double Width,
    double Height)
{
    public bool Contains(LogicalPoint point) =>
        point.X >= X && point.X <= X + Width &&
        point.Y >= Y && point.Y <= Y + Height;
}

public static class ScreenshotAnnotationRenderer
{
    private const double ArrowHeadLength = 14;
    private const double ArrowHeadHalfAngleDegrees = 28;

    public static AnnotationRaster RenderBgra(
        int width,
        int height,
        double scaleX,
        double scaleY,
        IEnumerable<IScreenshotAnnotation> annotations,
        string? preferredTextFontFamily = null)
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
            Draw(canvas, scaleX, scaleY, annotations, preferredTextFontFamily);
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
        IEnumerable<IScreenshotAnnotation> annotations,
        string? preferredTextFontFamily = null)
    {
        foreach (var annotation in annotations)
        {
            switch (annotation)
            {
                case ScreenshotRectangleAnnotation rectangle:
                    using (var paint = CreatePaint(rectangle.Style, scaleX, scaleY))
                    {
                        DrawRectangle(canvas, paint, rectangle, scaleX, scaleY);
                    }
                    break;

                case ScreenshotArrowAnnotation arrow:
                    using (var paint = CreatePaint(arrow.Style, scaleX, scaleY))
                    {
                        DrawArrow(canvas, paint, arrow, scaleX, scaleY);
                    }
                    break;

                case ScreenshotTextAnnotation text:
                    DrawText(
                        canvas,
                        text,
                        scaleX,
                        scaleY,
                        preferredTextFontFamily);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported screenshot annotation: {annotation.GetType().Name}.");
            }
        }
    }

    public static ScreenshotTextBounds MeasureText(
        ScreenshotTextAnnotation annotation,
        string? preferredTextFontFamily = null)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        using var layout = CreateTextLayout(
            annotation,
            scaleX: 1,
            scaleY: 1,
            preferredTextFontFamily);
        return new ScreenshotTextBounds(
            annotation.Origin.X,
            annotation.Origin.Y,
            Math.Min(annotation.MaxWidth, Math.Max(layout.Width, annotation.Style.FontSize / 2d)),
            layout.Height);
    }

    public static int? HitTestText(
        IReadOnlyList<IScreenshotAnnotation> annotations,
        LogicalPoint point,
        string? preferredTextFontFamily = null)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        for (var index = annotations.Count - 1; index >= 0; index--)
        {
            if (annotations[index] is ScreenshotTextAnnotation text &&
                MeasureText(text, preferredTextFontFamily).Contains(point))
            {
                return index;
            }
        }

        return null;
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

    private static void DrawText(
        SKCanvas canvas,
        ScreenshotTextAnnotation annotation,
        double scaleX,
        double scaleY,
        string? preferredTextFontFamily)
    {
        using var layout = CreateTextLayout(
            annotation,
            scaleX,
            scaleY,
            preferredTextFontFamily);
        var color = ScreenshotAnnotationPalette.GetColor(annotation.Style.Color);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(color.Red, color.Green, color.Blue),
            Style = SKPaintStyle.Fill,
        };

        var saveCount = canvas.Save();
        try
        {
            canvas.ClipRect(new SKRect(
                (float)(annotation.Origin.X * scaleX),
                (float)(annotation.Origin.Y * scaleY),
                (float)((annotation.Origin.X + annotation.MaxWidth) * scaleX),
                canvas.DeviceClipBounds.Bottom));
            foreach (var glyph in layout.Glyphs)
            {
                using var font = new SKFont(glyph.Typeface, layout.FontSize);
                canvas.DrawText(glyph.Text, glyph.X, glyph.Baseline, font, paint);
            }
        }
        finally
        {
            canvas.RestoreToCount(saveCount);
        }
    }

    private static TextLayout CreateTextLayout(
        ScreenshotTextAnnotation annotation,
        double scaleX,
        double scaleY,
        string? preferredTextFontFamily)
    {
        var fontScale = (scaleX + scaleY) / 2;
        var fontSize = (float)(annotation.Style.FontSize * fontScale);
        var lineHeight = fontSize * 1.25f;
        var maxWidth = (float)(annotation.MaxWidth * scaleX);
        var originX = (float)(annotation.Origin.X * scaleX);
        var originY = (float)(annotation.Origin.Y * scaleY);
        var layout = new TextLayout(fontSize, lineHeight);
        var baseTypeface = string.IsNullOrWhiteSpace(preferredTextFontFamily)
            ? SKTypeface.Default
            : SKTypeface.FromFamilyName(preferredTextFontFamily) ?? SKTypeface.Default;
        layout.Own(baseTypeface);

        var x = 0f;
        var line = 0;
        foreach (var element in EnumerateTextElements(annotation.Text))
        {
            if (element == "\n")
            {
                layout.Width = Math.Max(layout.Width, x);
                x = 0;
                line++;
                continue;
            }

            var typeface = ResolveTypeface(baseTypeface, element);
            layout.Own(typeface);
            using var font = new SKFont(typeface, fontSize);
            var width = Math.Max(0, font.MeasureText(element));
            if (x > 0 && x + width > maxWidth)
            {
                layout.Width = Math.Max(layout.Width, x);
                x = 0;
                line++;
            }

            layout.Glyphs.Add(new TextGlyph(
                element,
                typeface,
                originX + x,
                originY + fontSize + (line * lineHeight)));
            x += width;
        }

        layout.Width = Math.Min(maxWidth, Math.Max(layout.Width, x));
        layout.Height = Math.Max(lineHeight, (line + 1) * lineHeight);
        return layout;
    }

    private static SKTypeface ResolveTypeface(SKTypeface baseTypeface, string textElement)
    {
        if (baseTypeface.ContainsGlyphs(textElement))
        {
            return baseTypeface;
        }

        var codePoint = char.ConvertToUtf32(textElement, 0);
        return SKFontManager.Default.MatchCharacter(codePoint) ?? baseTypeface;
    }

    private static IEnumerable<string> EnumerateTextElements(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            if (element != "\r")
            {
                yield return element;
            }
        }
    }

    private static SKPoint ToSkPoint(LogicalPoint point, double scaleX, double scaleY) =>
        new((float)(point.X * scaleX), (float)(point.Y * scaleY));

    private sealed record TextGlyph(
        string Text,
        SKTypeface Typeface,
        float X,
        float Baseline);

    private sealed class TextLayout(float fontSize, float lineHeight) : IDisposable
    {
        private readonly HashSet<SKTypeface> _ownedTypefaces =
            new(ReferenceEqualityComparer.Instance);

        internal List<TextGlyph> Glyphs { get; } = [];

        internal float FontSize { get; } = fontSize;

        internal float LineHeight { get; } = lineHeight;

        internal float Width { get; set; }

        internal float Height { get; set; }

        internal void Own(SKTypeface typeface) => _ownedTypefaces.Add(typeface);

        public void Dispose()
        {
            foreach (var typeface in _ownedTypefaces)
            {
                typeface.Dispose();
            }
        }
    }
}
