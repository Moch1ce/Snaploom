using System.Collections.ObjectModel;

namespace Snaploom.Core;

public enum ScreenshotAnnotationTool
{
    Select,
    Rectangle,
    Arrow,
}

public enum ScreenshotAnnotationColor
{
    Red,
    Yellow,
    Green,
    Blue,
    Black,
    White,
}

public readonly record struct AnnotationRgbColor(byte Red, byte Green, byte Blue);

public static class ScreenshotAnnotationPalette
{
    public static AnnotationRgbColor GetColor(ScreenshotAnnotationColor color) =>
        color switch
        {
            ScreenshotAnnotationColor.Red => new AnnotationRgbColor(255, 77, 79),
            ScreenshotAnnotationColor.Yellow => new AnnotationRgbColor(250, 219, 20),
            ScreenshotAnnotationColor.Green => new AnnotationRgbColor(7, 201, 119),
            ScreenshotAnnotationColor.Blue => new AnnotationRgbColor(22, 119, 255),
            ScreenshotAnnotationColor.Black => new AnnotationRgbColor(32, 33, 36),
            ScreenshotAnnotationColor.White => new AnnotationRgbColor(255, 255, 255),
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, null),
        };
}

public readonly record struct ScreenshotAnnotationStyle
{
    public ScreenshotAnnotationStyle(ScreenshotAnnotationColor color, int lineWidth)
    {
        if (lineWidth is not (2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(
                nameof(lineWidth),
                lineWidth,
                "Annotation line width must be 2, 4, or 8 logical pixels.");
        }

        Color = color;
        LineWidth = lineWidth;
    }

    public static ScreenshotAnnotationStyle Default { get; } =
        new(ScreenshotAnnotationColor.Red, 4);

    public ScreenshotAnnotationColor Color { get; }

    public int LineWidth { get; }
}

public interface IScreenshotAnnotation
{
    ScreenshotAnnotationStyle Style { get; }
}

public sealed record ScreenshotRectangleAnnotation(
    LogicalPoint Start,
    LogicalPoint End,
    ScreenshotAnnotationStyle Style) : IScreenshotAnnotation;

public sealed record ScreenshotArrowAnnotation(
    LogicalPoint Start,
    LogicalPoint End,
    ScreenshotAnnotationStyle Style) : IScreenshotAnnotation;

public sealed class ScreenshotAnnotationSession
{
    private readonly List<IScreenshotAnnotation> _annotations = [];
    private readonly ReadOnlyCollection<IScreenshotAnnotation> _readOnlyAnnotations;
    private LogicalPoint _start;

    public ScreenshotAnnotationSession()
    {
        _readOnlyAnnotations = _annotations.AsReadOnly();
    }

    public ScreenshotAnnotationTool ActiveTool { get; private set; }

    public ScreenshotAnnotationStyle Style { get; private set; } =
        ScreenshotAnnotationStyle.Default;

    public IReadOnlyList<IScreenshotAnnotation> Annotations => _readOnlyAnnotations;

    public IScreenshotAnnotation? Preview { get; private set; }

    public void SetTool(ScreenshotAnnotationTool tool)
    {
        ActiveTool = tool;
        Preview = null;
    }

    public void SetStyle(ScreenshotAnnotationStyle style) => Style = style;

    public void Begin(LogicalPoint point)
    {
        if (ActiveTool == ScreenshotAnnotationTool.Select)
        {
            throw new InvalidOperationException("An annotation tool must be active before drawing.");
        }

        _start = point;
        Preview = CreateAnnotation(_start, _start);
    }

    public void Update(LogicalPoint point)
    {
        if (Preview is null)
        {
            throw new InvalidOperationException("An annotation has not been started.");
        }

        Preview = CreateAnnotation(_start, point);
    }

    public bool Complete()
    {
        if (Preview is not { } preview)
        {
            throw new InvalidOperationException("An annotation has not been started.");
        }

        Preview = null;
        if (preview switch
        {
            ScreenshotRectangleAnnotation rectangle => rectangle.Start == rectangle.End,
            ScreenshotArrowAnnotation arrow => arrow.Start == arrow.End,
            _ => true,
        })
        {
            return false;
        }

        _annotations.Add(preview);
        return true;
    }

    public bool CancelPreview()
    {
        if (Preview is null)
        {
            return false;
        }

        Preview = null;
        return true;
    }

    public void Clear()
    {
        Preview = null;
        _annotations.Clear();
    }

    public IEnumerable<IScreenshotAnnotation> EnumerateForRendering()
    {
        foreach (var annotation in _annotations)
        {
            yield return annotation;
        }

        if (Preview is not null)
        {
            yield return Preview;
        }
    }

    private IScreenshotAnnotation CreateAnnotation(LogicalPoint start, LogicalPoint end) =>
        ActiveTool switch
        {
            ScreenshotAnnotationTool.Rectangle =>
                new ScreenshotRectangleAnnotation(start, end, Style),
            ScreenshotAnnotationTool.Arrow =>
                new ScreenshotArrowAnnotation(start, end, Style),
            _ => throw new InvalidOperationException("The active tool does not create annotations."),
        };
}
