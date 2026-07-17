using System.Collections.ObjectModel;

namespace Snaploom.Core;

public enum ScreenshotAnnotationTool
{
    Select,
    Rectangle,
    Arrow,
    Text,
    Mosaic,
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

public readonly record struct ScreenshotMosaicStyle
{
    public ScreenshotMosaicStyle(int brushSize, int pixelSize)
    {
        if (brushSize is not (16 or 32 or 64))
        {
            throw new ArgumentOutOfRangeException(
                nameof(brushSize),
                brushSize,
                "Mosaic brush size must be 16, 32, or 64 logical pixels.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize);
        BrushSize = brushSize;
        PixelSize = pixelSize;
    }

    public static ScreenshotMosaicStyle Default { get; } = new(32, 12);

    public int BrushSize { get; }

    public int PixelSize { get; }
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

public sealed record ScreenshotMosaicAnnotation(
    IReadOnlyList<LogicalPoint> Points,
    ScreenshotMosaicStyle Style) : IScreenshotAnnotation;

public sealed record ScreenshotTextEdit(
    LogicalPoint Origin,
    string Text,
    double MaxWidth,
    ScreenshotTextStyle Style,
    int? AnnotationIndex,
    bool IsComposing);

public enum AnnotationResizeHandle
{
    Start,
    End,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

public sealed class ScreenshotAnnotationSession
{
    private readonly List<IScreenshotAnnotation> _annotations = [];
    private readonly ReadOnlyCollection<IScreenshotAnnotation> _readOnlyAnnotations;
    private readonly List<LogicalPoint> _mosaicPoints = [];
    private readonly Stack<AnnotationHistoryState> _undo = [];
    private readonly Stack<AnnotationHistoryState> _redo = [];
    private LogicalPoint _start;
    private AnnotationHistoryState? _transformBefore;
    private IScreenshotAnnotation? _transformOriginal;
    private LogicalPoint _transformStart;
    private AnnotationResizeHandle? _resizeHandle;

    public ScreenshotAnnotationSession()
    {
        _readOnlyAnnotations = _annotations.AsReadOnly();
    }

    public ScreenshotAnnotationTool ActiveTool { get; private set; }

    public ScreenshotAnnotationStyle Style { get; private set; } =
        ScreenshotAnnotationStyle.Default;

    public ScreenshotTextStyle TextStyle { get; private set; } =
        ScreenshotTextStyle.Default;

    public ScreenshotMosaicStyle MosaicStyle { get; private set; } =
        ScreenshotMosaicStyle.Default;

    public IReadOnlyList<IScreenshotAnnotation> Annotations => _readOnlyAnnotations;

    public IScreenshotAnnotation? Preview { get; private set; }

    public ScreenshotTextEdit? TextEdit { get; private set; }

    public int? SelectedIndex { get; private set; }

    public IScreenshotAnnotation? SelectedAnnotation =>
        SelectedIndex is { } index && index >= 0 && index < _annotations.Count
            ? _annotations[index]
            : null;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public bool IsTransforming => _transformBefore is not null;

    public void SetTool(ScreenshotAnnotationTool tool)
    {
        ActiveTool = tool;
        Preview = null;
        TextEdit = null;
        _mosaicPoints.Clear();
        SelectedIndex = null;
    }

    public void SetStyle(ScreenshotAnnotationStyle style) => Style = style;

    public void SetTextStyle(ScreenshotTextStyle style) => TextStyle = style;

    public void SetMosaicStyle(ScreenshotMosaicStyle style) => MosaicStyle = style;

    public void Begin(LogicalPoint point)
    {
        if (ActiveTool is ScreenshotAnnotationTool.Select or ScreenshotAnnotationTool.Text)
        {
            throw new InvalidOperationException("An annotation tool must be active before drawing.");
        }

        _start = point;
        if (ActiveTool == ScreenshotAnnotationTool.Mosaic)
        {
            _mosaicPoints.Clear();
            _mosaicPoints.Add(point);
            Preview = CreateMosaicAnnotation();
        }
        else
        {
            Preview = CreateAnnotation(_start, _start);
        }
    }

    public void Update(LogicalPoint point)
    {
        if (Preview is null)
        {
            throw new InvalidOperationException("An annotation has not been started.");
        }

        if (Preview is ScreenshotMosaicAnnotation)
        {
            AppendMosaicPoints(point);
            Preview = CreateMosaicAnnotation();
        }
        else
        {
            Preview = CreateAnnotation(_start, point);
        }
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
            ScreenshotMosaicAnnotation => false,
            _ => true,
        })
        {
            return false;
        }

        PushHistory(CaptureState());
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
        _mosaicPoints.Clear();
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
        SelectedIndex = annotationIndex;
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

        if (string.IsNullOrWhiteSpace(edit.Text))
        {
            TextEdit = null;
            return false;
        }

        var annotation = new ScreenshotTextAnnotation(
            edit.Origin,
            NormalizeLineEndings(edit.Text),
            edit.MaxWidth,
            edit.Style);
        var before = CaptureState();
        TextEdit = null;
        if (edit.AnnotationIndex is { } annotationIndex)
        {
            if (Equals(_annotations[annotationIndex], annotation))
            {
                return false;
            }

            PushHistory(before);
            _annotations[annotationIndex] = annotation;
            SelectedIndex = annotationIndex;
        }
        else
        {
            PushHistory(before);
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
        _mosaicPoints.Clear();
        _annotations.Clear();
        SelectedIndex = null;
        _transformBefore = null;
        _transformOriginal = null;
        _undo.Clear();
        _redo.Clear();
    }

    public int? HitTest(LogicalPoint point, double tolerance = 6)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);
        for (var index = _annotations.Count - 1; index >= 0; index--)
        {
            if (ContainsPoint(_annotations[index], point, tolerance))
            {
                return index;
            }
        }

        return null;
    }

    public bool SelectAt(LogicalPoint point, double tolerance = 6)
    {
        SelectedIndex = HitTest(point, tolerance);
        return SelectedIndex is not null;
    }

    public bool Select(int annotationIndex)
    {
        if (annotationIndex < 0 || annotationIndex >= _annotations.Count)
        {
            return false;
        }

        SelectedIndex = annotationIndex;
        return true;
    }

    public bool ClearSelection()
    {
        if (SelectedIndex is null)
        {
            return false;
        }

        SelectedIndex = null;
        return true;
    }

    public bool BeginMoveSelected(LogicalPoint point)
    {
        if (SelectedAnnotation is not { } annotation || IsTransforming)
        {
            return false;
        }

        _transformBefore = CaptureState();
        _transformOriginal = annotation;
        _transformStart = point;
        _resizeHandle = null;
        return true;
    }

    public bool BeginResizeSelected(AnnotationResizeHandle handle)
    {
        var supported = SelectedAnnotation switch
        {
            ScreenshotRectangleAnnotation => true,
            ScreenshotArrowAnnotation => handle is AnnotationResizeHandle.Start or
                AnnotationResizeHandle.End,
            _ => false,
        };
        if (!supported || IsTransforming)
        {
            return false;
        }

        _transformBefore = CaptureState();
        _transformOriginal = SelectedAnnotation;
        _resizeHandle = handle;
        return true;
    }

    public void UpdateSelectedTransform(LogicalPoint point)
    {
        if (_transformOriginal is null || SelectedIndex is not { } index)
        {
            throw new InvalidOperationException("An annotation transform has not been started.");
        }

        _annotations[index] = _resizeHandle is { } resizeHandle
            ? ResizeAnnotation(_transformOriginal, resizeHandle, point)
            : TranslateAnnotation(
                _transformOriginal,
                new LogicalPoint(
                    point.X - _transformStart.X,
                    point.Y - _transformStart.Y));
    }

    public bool CompleteSelectedTransform()
    {
        if (_transformBefore is not { } before ||
            _transformOriginal is null ||
            SelectedAnnotation is not { } current)
        {
            return false;
        }

        var changed = !Equals(_transformOriginal, current);
        ResetTransform();
        if (changed)
        {
            PushHistory(before);
        }

        return changed;
    }

    public bool CancelSelectedTransform()
    {
        if (_transformBefore is not { } before)
        {
            return false;
        }

        RestoreState(before);
        ResetTransform();
        return true;
    }

    public bool UpdateSelectedStyle(ScreenshotAnnotationStyle style)
    {
        if (SelectedIndex is not { } index)
        {
            return false;
        }

        IScreenshotAnnotation? updated = _annotations[index] switch
        {
            ScreenshotRectangleAnnotation rectangle => rectangle with { Style = style },
            ScreenshotArrowAnnotation arrow => arrow with { Style = style },
            _ => null,
        };
        return ReplaceSelectedWithHistory(updated);
    }

    public bool UpdateSelectedStyle(ScreenshotTextStyle style)
    {
        if (SelectedAnnotation is not ScreenshotTextAnnotation text)
        {
            return false;
        }

        return ReplaceSelectedWithHistory(text with { Style = style });
    }

    public bool UpdateSelectedStyle(ScreenshotMosaicStyle style)
    {
        if (SelectedAnnotation is not ScreenshotMosaicAnnotation mosaic)
        {
            return false;
        }

        return ReplaceSelectedWithHistory(mosaic with { Style = style });
    }

    public bool DeleteSelected()
    {
        if (SelectedIndex is not { } index)
        {
            return false;
        }

        PushHistory(CaptureState());
        _annotations.RemoveAt(index);
        SelectedIndex = null;
        return true;
    }

    public bool Undo()
    {
        if (_undo.Count == 0 || Preview is not null || TextEdit is not null || IsTransforming)
        {
            return false;
        }

        _redo.Push(CaptureState());
        RestoreState(_undo.Pop());
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0 || Preview is not null || TextEdit is not null || IsTransforming)
        {
            return false;
        }

        _undo.Push(CaptureState());
        RestoreState(_redo.Pop());
        return true;
    }

    public void RebaseForSelectionOriginChange(LogicalPoint offset)
    {
        if (offset == default)
        {
            return;
        }

        for (var index = 0; index < _annotations.Count; index++)
        {
            _annotations[index] = TranslateAnnotation(_annotations[index], offset);
        }

        RebaseHistory(_undo, offset);
        RebaseHistory(_redo, offset);
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

    private ScreenshotMosaicAnnotation CreateMosaicAnnotation() =>
        new(_mosaicPoints.ToArray(), MosaicStyle);

    private void AppendMosaicPoints(LogicalPoint point)
    {
        var previous = _mosaicPoints[^1];
        var deltaX = point.X - previous.X;
        var deltaY = point.Y - previous.Y;
        var distance = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (distance <= 0)
        {
            return;
        }

        var maximumSpacing = MosaicStyle.BrushSize / 4d;
        var steps = Math.Max(1, (int)Math.Ceiling(distance / maximumSpacing));
        for (var step = 1; step <= steps; step++)
        {
            var progress = step / (double)steps;
            _mosaicPoints.Add(new LogicalPoint(
                previous.X + (deltaX * progress),
                previous.Y + (deltaY * progress)));
        }
    }

    private bool ReplaceSelectedWithHistory(IScreenshotAnnotation? updated)
    {
        if (updated is null || SelectedIndex is not { } index ||
            Equals(_annotations[index], updated))
        {
            return false;
        }

        PushHistory(CaptureState());
        _annotations[index] = updated;
        return true;
    }

    private void PushHistory(AnnotationHistoryState state)
    {
        _undo.Push(state);
        _redo.Clear();
    }

    private AnnotationHistoryState CaptureState() =>
        new(_annotations.ToArray(), SelectedIndex);

    private void RestoreState(AnnotationHistoryState state)
    {
        _annotations.Clear();
        _annotations.AddRange(state.Annotations);
        SelectedIndex = state.SelectedIndex is { } index && index < _annotations.Count
            ? index
            : null;
    }

    private void ResetTransform()
    {
        _transformBefore = null;
        _transformOriginal = null;
        _resizeHandle = null;
    }

    private static IScreenshotAnnotation ResizeAnnotation(
        IScreenshotAnnotation annotation,
        AnnotationResizeHandle handle,
        LogicalPoint point) =>
        annotation switch
        {
            ScreenshotRectangleAnnotation rectangle => ResizeRectangle(rectangle, handle, point),
            ScreenshotArrowAnnotation arrow when handle == AnnotationResizeHandle.Start =>
                arrow with { Start = point },
            ScreenshotArrowAnnotation arrow when handle == AnnotationResizeHandle.End =>
                arrow with { End = point },
            _ => throw new InvalidOperationException("The selected annotation cannot be resized."),
        };

    private static ScreenshotRectangleAnnotation ResizeRectangle(
        ScreenshotRectangleAnnotation rectangle,
        AnnotationResizeHandle handle,
        LogicalPoint point)
    {
        handle = handle switch
        {
            AnnotationResizeHandle.Start => AnnotationResizeHandle.TopLeft,
            AnnotationResizeHandle.End => AnnotationResizeHandle.BottomRight,
            _ => handle,
        };
        var left = Math.Min(rectangle.Start.X, rectangle.End.X);
        var top = Math.Min(rectangle.Start.Y, rectangle.End.Y);
        var right = Math.Max(rectangle.Start.X, rectangle.End.X);
        var bottom = Math.Max(rectangle.Start.Y, rectangle.End.Y);

        if (handle is AnnotationResizeHandle.TopLeft or
            AnnotationResizeHandle.BottomLeft or
            AnnotationResizeHandle.Left)
        {
            left = Math.Min(point.X, right - 1);
        }

        if (handle is AnnotationResizeHandle.TopRight or
            AnnotationResizeHandle.Right or
            AnnotationResizeHandle.BottomRight)
        {
            right = Math.Max(point.X, left + 1);
        }

        if (handle is AnnotationResizeHandle.TopLeft or
            AnnotationResizeHandle.Top or
            AnnotationResizeHandle.TopRight)
        {
            top = Math.Min(point.Y, bottom - 1);
        }

        if (handle is AnnotationResizeHandle.BottomLeft or
            AnnotationResizeHandle.Bottom or
            AnnotationResizeHandle.BottomRight)
        {
            bottom = Math.Max(point.Y, top + 1);
        }

        return rectangle with
        {
            Start = new LogicalPoint(left, top),
            End = new LogicalPoint(right, bottom),
        };
    }

    private static IScreenshotAnnotation TranslateAnnotation(
        IScreenshotAnnotation annotation,
        LogicalPoint offset) =>
        annotation switch
        {
            ScreenshotRectangleAnnotation rectangle => rectangle with
            {
                Start = Add(rectangle.Start, offset),
                End = Add(rectangle.End, offset),
            },
            ScreenshotArrowAnnotation arrow => arrow with
            {
                Start = Add(arrow.Start, offset),
                End = Add(arrow.End, offset),
            },
            ScreenshotTextAnnotation text => text with
            {
                Origin = Add(text.Origin, offset),
            },
            ScreenshotMosaicAnnotation mosaic => mosaic with
            {
                Points = mosaic.Points.Select(point => Add(point, offset)).ToArray(),
            },
            _ => throw new InvalidOperationException(
                $"Unsupported screenshot annotation: {annotation.GetType().Name}."),
        };

    private static LogicalPoint Add(LogicalPoint point, LogicalPoint offset) =>
        new(point.X + offset.X, point.Y + offset.Y);

    private static bool ContainsPoint(
        IScreenshotAnnotation annotation,
        LogicalPoint point,
        double tolerance) =>
        annotation switch
        {
            ScreenshotRectangleAnnotation rectangle =>
                DistanceToRectangleBorder(rectangle, point) <=
                tolerance + (rectangle.Style.LineWidth / 2d),
            ScreenshotArrowAnnotation arrow =>
                DistanceToSegment(point, arrow.Start, arrow.End) <=
                tolerance + (arrow.Style.LineWidth / 2d),
            ScreenshotTextAnnotation text => ContainsText(text, point),
            ScreenshotMosaicAnnotation mosaic => ContainsMosaic(mosaic, point, tolerance),
            _ => false,
        };

    private static double DistanceToRectangleBorder(
        ScreenshotRectangleAnnotation rectangle,
        LogicalPoint point)
    {
        var left = Math.Min(rectangle.Start.X, rectangle.End.X);
        var right = Math.Max(rectangle.Start.X, rectangle.End.X);
        var top = Math.Min(rectangle.Start.Y, rectangle.End.Y);
        var bottom = Math.Max(rectangle.Start.Y, rectangle.End.Y);
        if (point.X >= left && point.X <= right && point.Y >= top && point.Y <= bottom)
        {
            return Math.Min(
                Math.Min(point.X - left, right - point.X),
                Math.Min(point.Y - top, bottom - point.Y));
        }

        var closestX = Math.Clamp(point.X, left, right);
        var closestY = Math.Clamp(point.Y, top, bottom);
        return Distance(point, new LogicalPoint(closestX, closestY));
    }

    private static bool ContainsText(ScreenshotTextAnnotation text, LogicalPoint point)
    {
        var lines = text.Text.Split('\n');
        var width = Math.Min(
            text.MaxWidth,
            Math.Max(
                text.Style.FontSize / 2d,
                lines.Max(line => line.Length) * text.Style.FontSize * 0.6));
        var height = Math.Max(1, lines.Length) * text.Style.FontSize * 1.25;
        return point.X >= text.Origin.X && point.X <= text.Origin.X + width &&
            point.Y >= text.Origin.Y && point.Y <= text.Origin.Y + height;
    }

    private static bool ContainsMosaic(
        ScreenshotMosaicAnnotation mosaic,
        LogicalPoint point,
        double tolerance)
    {
        if (mosaic.Points.Count == 0)
        {
            return false;
        }

        var radius = (mosaic.Style.BrushSize / 2d) + tolerance;
        if (mosaic.Points.Count == 1)
        {
            return Distance(point, mosaic.Points[0]) <= radius;
        }

        for (var index = 1; index < mosaic.Points.Count; index++)
        {
            if (DistanceToSegment(point, mosaic.Points[index - 1], mosaic.Points[index]) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    private static double DistanceToSegment(
        LogicalPoint point,
        LogicalPoint start,
        LogicalPoint end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var lengthSquared = (deltaX * deltaX) + (deltaY * deltaY);
        if (lengthSquared <= 0)
        {
            return Distance(point, start);
        }

        var progress = Math.Clamp(
            (((point.X - start.X) * deltaX) + ((point.Y - start.Y) * deltaY)) /
            lengthSquared,
            0,
            1);
        return Distance(
            point,
            new LogicalPoint(start.X + (progress * deltaX), start.Y + (progress * deltaY)));
    }

    private static double Distance(LogicalPoint first, LogicalPoint second)
    {
        var deltaX = second.X - first.X;
        var deltaY = second.Y - first.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    private static void RebaseHistory(
        Stack<AnnotationHistoryState> history,
        LogicalPoint offset)
    {
        var rebased = history
            .Reverse()
            .Select(state => state with
            {
                Annotations = state.Annotations
                    .Select(annotation => TranslateAnnotation(annotation, offset))
                    .ToArray(),
            })
            .ToArray();
        history.Clear();
        foreach (var state in rebased)
        {
            history.Push(state);
        }
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private sealed record AnnotationHistoryState(
        IReadOnlyList<IScreenshotAnnotation> Annotations,
        int? SelectedIndex);
}
