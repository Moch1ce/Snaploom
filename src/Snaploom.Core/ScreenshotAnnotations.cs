using System.Collections.ObjectModel;

namespace Snaploom.Core;

public enum ScreenshotAnnotationTool
{
    Select,
    Rectangle,
    Arrow,
    Text,
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

public readonly record struct ScreenshotTextStyle
{
    public ScreenshotTextStyle(ScreenshotAnnotationColor color, int fontSize)
    {
        if (fontSize is not (16 or 24 or 32))
        {
            throw new ArgumentOutOfRangeException(
                nameof(fontSize),
                fontSize,
                "Text font size must be 16, 24, or 32 logical pixels.");
        }

        Color = color;
        FontSize = fontSize;
    }

    public static ScreenshotTextStyle Default { get; } =
        new(ScreenshotAnnotationColor.Red, 24);

    public ScreenshotAnnotationColor Color { get; }

    public int FontSize { get; }
}

public interface IScreenshotAnnotation;

public sealed record ScreenshotRectangleAnnotation(
    LogicalPoint Start,
    LogicalPoint End,
    ScreenshotAnnotationStyle Style) : IScreenshotAnnotation;

public sealed record ScreenshotArrowAnnotation(
    LogicalPoint Start,
    LogicalPoint End,
    ScreenshotAnnotationStyle Style) : IScreenshotAnnotation;

public sealed record ScreenshotTextAnnotation(
    LogicalPoint Origin,
    string Text,
    double MaxWidth,
    ScreenshotTextStyle Style) : IScreenshotAnnotation;

public sealed record ScreenshotTextEdit(
    LogicalPoint Origin,
    string Text,
    double MaxWidth,
    ScreenshotTextStyle Style,
    int? AnnotationIndex,
    bool IsComposing);

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

    public ScreenshotTextStyle TextStyle { get; private set; } =
        ScreenshotTextStyle.Default;

    public IReadOnlyList<IScreenshotAnnotation> Annotations => _readOnlyAnnotations;

    public IScreenshotAnnotation? Preview { get; private set; }

    public ScreenshotTextEdit? TextEdit { get; private set; }

    public void SetTool(ScreenshotAnnotationTool tool)
    {
        ActiveTool = tool;
        Preview = null;
        TextEdit = null;
    }

    public void SetStyle(ScreenshotAnnotationStyle style) => Style = style;

    public void SetTextStyle(ScreenshotTextStyle style) => TextStyle = style;

    public void Begin(LogicalPoint point)
    {
        if (ActiveTool is ScreenshotAnnotationTool.Select or ScreenshotAnnotationTool.Text)
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

    public void BeginText(LogicalPoint origin, double maxWidth)
    {
        if (ActiveTool != ScreenshotAnnotationTool.Text)
        {
            throw new InvalidOperationException("The text annotation tool must be active.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWidth);
        Preview = null;
        TextEdit = new ScreenshotTextEdit(
            origin,
            string.Empty,
            maxWidth,
            TextStyle,
            AnnotationIndex: null,
            IsComposing: false);
    }

    public bool BeginTextEdit(int annotationIndex)
    {
        if (annotationIndex < 0 ||
            annotationIndex >= _annotations.Count ||
            _annotations[annotationIndex] is not ScreenshotTextAnnotation annotation)
        {
            return false;
        }

        Preview = null;
        TextEdit = new ScreenshotTextEdit(
            annotation.Origin,
            annotation.Text,
            annotation.MaxWidth,
            annotation.Style,
            annotationIndex,
            IsComposing: false);
        return true;
    }

    public void UpdateText(string text, bool isComposing)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (TextEdit is not { } edit)
        {
            throw new InvalidOperationException("A text annotation is not being edited.");
        }

        TextEdit = edit with
        {
            Text = NormalizeLineEndings(text),
            IsComposing = isComposing,
        };
    }

    public bool CommitText()
    {
        if (TextEdit is not { } edit || edit.IsComposing)
        {
            return false;
        }

        TextEdit = null;
        if (string.IsNullOrWhiteSpace(edit.Text))
        {
            return false;
        }

        var annotation = new ScreenshotTextAnnotation(
            edit.Origin,
            NormalizeLineEndings(edit.Text),
            edit.MaxWidth,
            edit.Style);
        if (edit.AnnotationIndex is { } annotationIndex)
        {
            _annotations[annotationIndex] = annotation;
        }
        else
        {
            _annotations.Add(annotation);
        }

        return true;
    }

    public bool CancelTextEdit()
    {
        if (TextEdit is null)
        {
            return false;
        }

        TextEdit = null;
        return true;
    }

    public void Clear()
    {
        Preview = null;
        TextEdit = null;
        _annotations.Clear();
    }

    public IEnumerable<IScreenshotAnnotation> EnumerateForRendering()
    {
        for (var index = 0; index < _annotations.Count; index++)
        {
            if (TextEdit?.AnnotationIndex != index)
            {
                yield return _annotations[index];
            }
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

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
}
