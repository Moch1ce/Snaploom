using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Snaploom.Core;
using Snaploom.Rendering;

namespace Snaploom.App;

internal enum ScreenshotPointerFeedback
{
    Default,
    Disabled,
    Crosshair,
    Text,
    MoveSelection,
    MoveAnnotation,
    ResizeHorizontal,
    ResizeVertical,
    ResizeDiagonal,
    ResizeArrow,
}

public sealed class ScreenshotSelectionCanvas : Control, IDisposable
{
    private static readonly Pen SelectionPen = new(
        ScreenshotUiTheme.AccentBrush,
        ScreenshotUiTheme.SelectionBorderThickness);

    private readonly record struct PendingTextAnnotationInteraction(
        int AnnotationIndex,
        Point Start);

    private enum ResizeBorder
    {
        Top,
        Right,
        Bottom,
        Left,
    }

    private readonly CapturedFrame _frame;
    private readonly ScreenshotSession _session;
    private readonly ScreenshotAnnotationSession _annotationSession = new();
    private readonly IReadOnlyList<ScreenshotWindowCandidate> _windowCandidates;
    private readonly WriteableBitmap _bitmap;
    private readonly ScreenshotPixelInspector _pixelInspector;
    private readonly Cursor _crosshairCursor = new(StandardCursorType.Cross);
    private readonly Cursor _moveCursor = ScreenshotMoveCursor.Create();
    private readonly Cursor _textCursor = new(StandardCursorType.Ibeam);
    private readonly Cursor _defaultCursor = new(StandardCursorType.Arrow);
    private readonly Cursor _disabledCursor = new(GetMaskCursorType());
    private readonly Dictionary<StandardCursorType, Cursor> _resizeCursors = new()
    {
        [StandardCursorType.SizeWestEast] = new(StandardCursorType.SizeWestEast),
        [StandardCursorType.SizeNorthSouth] = new(StandardCursorType.SizeNorthSouth),
        [StandardCursorType.TopLeftCorner] =
            ScreenshotResizeCursor.Create(StandardCursorType.TopLeftCorner),
        [StandardCursorType.TopRightCorner] =
            ScreenshotResizeCursor.Create(StandardCursorType.TopRightCorner),
        [StandardCursorType.BottomRightCorner] =
            ScreenshotResizeCursor.Create(StandardCursorType.BottomRightCorner),
        [StandardCursorType.BottomLeftCorner] =
            ScreenshotResizeCursor.Create(StandardCursorType.BottomLeftCorner),
        [StandardCursorType.DragMove] = new(StandardCursorType.DragMove),
    };
    private Point? _pendingSelectionStart;
    private PendingTextAnnotationInteraction? _pendingTextAnnotationInteraction;
    private PhysicalPoint? _snapHoverOrigin;
    private IPointer? _capturedPointer;
    private ScreenshotSnapTarget? _hoveredSnapTarget;
    private WriteableBitmap? _annotationBitmap;
    private ScreenshotMosaicTileCache? _mosaicTileCache;
    private readonly Dictionary<MosaicTileKey, WriteableBitmap> _mosaicTileBitmaps = [];
    private PhysicalRect? _mosaicSelection;
    private bool _annotationBitmapDirty = true;
    private bool _mosaicCacheDirty = true;
    private bool _selectionHasBeenEdited;
    private bool _isAnnotationMoveTransform;
    private bool _disposed;

    public ScreenshotSelectionCanvas(
        CapturedFrame frame,
        IEnumerable<ScreenshotWindowCandidate>? windowCandidates = null,
        PhysicalPoint? snapHoverOrigin = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frame = frame;
        _session = new ScreenshotSession(frame.PhysicalSize);
        _windowCandidates = ScreenshotWindowSelector.GetEligibleWindows(
            windowCandidates ?? Array.Empty<ScreenshotWindowCandidate>(),
            frame.PhysicalSize);
        _snapHoverOrigin = snapHoverOrigin;
        _bitmap = CreateBitmap(frame);
        _pixelInspector = new ScreenshotPixelInspector(frame, _bitmap);
        ClipToBounds = true;
        Cursor = _crosshairCursor;
        Focusable = true;
    }

    public event EventHandler? SelectionChanged;

    public event EventHandler? SelectionDoubleClicked;

    public event EventHandler? AnnotationStarted;

    public event EventHandler? TextEditingStarted;

    public event EventHandler? AnnotationSelectionChanged;

    public event EventHandler? AnnotationHistoryChanged;

    public event EventHandler? SelectionReplaced;

    public ScreenshotSession Session => _session;

    public IReadOnlyList<IScreenshotAnnotation> Annotations => _annotationSession.Annotations;

    public ScreenshotAnnotationTool ActiveAnnotationTool => _annotationSession.ActiveTool;

    public ScreenshotTextEdit? TextEdit => _annotationSession.TextEdit;

    public int MosaicTileCount => _mosaicTileBitmaps.Count;

    public IScreenshotAnnotation? SelectedAnnotation => _annotationSession.SelectedAnnotation;

    public bool CanUndo => _annotationSession.CanUndo;

    public bool CanRedo => _annotationSession.CanRedo;

    internal ScreenshotPointerFeedback PointerFeedback { get; private set; } =
        ScreenshotPointerFeedback.Crosshair;

    internal int SelectedAnnotationControlPointCount =>
        _annotationSession.SelectedAnnotation switch
        {
            ScreenshotRectangleAnnotation => 8,
            ScreenshotArrowAnnotation => 2,
            _ => 0,
        };

    internal ScreenshotSnapTarget? HoveredSnapTarget => _hoveredSnapTarget;

    internal Rect? LogicalSelection =>
        _session.Selection is { } selection ? ToLogicalRect(selection) : null;

    internal bool IsMaskInteractionArea(Point point) =>
        _session.State == ScreenshotSessionState.Selected &&
        HitTestSelectionResizeHandle(point) is null &&
        !_session.SelectionContains(ToPhysicalPoint(point));

    internal static StandardCursorType GetMaskCursorType() => StandardCursorType.No;

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var destination = new Rect(Bounds.Size);
        var fullSource = new Rect(
            0,
            0,
            _frame.PhysicalSize.Width,
            _frame.PhysicalSize.Height);
        context.DrawImage(_bitmap, fullSource, destination);
        context.DrawRectangle(ScreenshotUiTheme.DimBrush, pen: null, destination);

        if (_session.Selection is not { } selection)
        {
            if (_hoveredSnapTarget is { } snapTarget)
            {
                DrawSnapTarget(context, snapTarget.Bounds);
            }
            else
            {
                _pixelInspector.Render(context, Bounds.Size);
            }

            return;
        }

        if (selection.Width > 0 && selection.Height > 0)
        {
            var source = new Rect(selection.X, selection.Y, selection.Width, selection.Height);
            var selectedDestination = ToLogicalRect(selection);
            context.DrawImage(_bitmap, source, selectedDestination);
            DrawAnnotations(context, selection, selectedDestination);
            context.DrawRectangle(brush: null, SelectionPen, selectedDestination);
            DrawSelectionHandles(context, selectedDestination);
            DrawSelectedAnnotationControls(context, selectedDestination);
        }

        if (_session.State == ScreenshotSessionState.Selecting)
        {
            _pixelInspector.Render(context, Bounds.Size);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _crosshairCursor.Dispose();
        _moveCursor.Dispose();
        _textCursor.Dispose();
        _defaultCursor.Dispose();
        foreach (var cursor in _resizeCursors.Values)
        {
            cursor.Dispose();
        }
        _annotationBitmap?.Dispose();
        ResetMosaicCache();
        _annotationSession.Clear();
        _bitmap.Dispose();
    }

    public void SelectAnnotationTool(ScreenshotAnnotationTool tool)
    {
        if (_pendingTextAnnotationInteraction is not null)
        {
            _pendingTextAnnotationInteraction = null;
            ReleasePointerCapture();
        }

        if (_annotationSession.CancelPreview())
        {
            ReleasePointerCapture();
        }

        _annotationSession.SetTool(tool);
        _annotationBitmapDirty = true;
        _mosaicCacheDirty = true;
        SetToolCursor(tool);
        AnnotationSelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    public void SetAnnotationStyle(ScreenshotAnnotationStyle style)
    {
        _annotationSession.SetStyle(style);
        if (_annotationSession.UpdateSelectedStyle(style))
        {
            AnnotationChanged();
        }
    }

    public void SetTextStyle(ScreenshotTextStyle style)
    {
        _annotationSession.SetTextStyle(style);
        if (_annotationSession.UpdateSelectedStyle(style))
        {
            AnnotationChanged();
        }
    }

    public void SetMosaicStyle(ScreenshotMosaicStyle style)
    {
        _annotationSession.SetMosaicStyle(style);
        if (_annotationSession.UpdateSelectedStyle(style))
        {
            AnnotationChanged();
        }
    }

    public void UpdateTextDraft(string text, bool isComposing)
    {
        _annotationSession.UpdateText(text, isComposing);
        _annotationBitmapDirty = true;
        InvalidateVisual();
    }

    public bool CommitTextEdit()
    {
        var committed = _annotationSession.CommitText();
        _annotationBitmapDirty = true;
        InvalidateVisual();
        if (committed)
        {
            AnnotationSelectionChanged?.Invoke(this, EventArgs.Empty);
            AnnotationHistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        return committed;
    }

    public bool CancelTextEdit()
    {
        var canceled = _annotationSession.CancelTextEdit();
        if (canceled)
        {
            _annotationBitmapDirty = true;
            InvalidateVisual();
        }

        return canceled;
    }

    public bool DeleteSelectedAnnotation()
    {
        if (!_annotationSession.DeleteSelected())
        {
            return false;
        }

        AnnotationChanged();
        return true;
    }

    public bool UndoAnnotation()
    {
        if (!_annotationSession.Undo())
        {
            return false;
        }

        AnnotationChanged();
        return true;
    }

    public bool RedoAnnotation()
    {
        if (!_annotationSession.Redo())
        {
            return false;
        }

        AnnotationChanged();
        return true;
    }

    public ScreenshotCancelResult CancelCurrentLayer()
    {
        ScreenshotCancelResult result;
        if (_annotationSession.CancelTextEdit())
        {
            _annotationBitmapDirty = true;
            result = ScreenshotCancelResult.ActionCanceled;
        }
        else if (_annotationSession.CancelSelectedTransform())
        {
            _annotationBitmapDirty = true;
            _mosaicCacheDirty = true;
            result = ScreenshotCancelResult.ActionCanceled;
        }
        else if (_annotationSession.CancelPreview())
        {
            _annotationBitmapDirty = true;
            result = ScreenshotCancelResult.ActionCanceled;
        }
        else if (_annotationSession.ClearSelection())
        {
            AnnotationSelectionChanged?.Invoke(this, EventArgs.Empty);
            result = ScreenshotCancelResult.ActionCanceled;
        }
        else if (_annotationSession.ActiveTool != ScreenshotAnnotationTool.Select)
        {
            SelectAnnotationTool(ScreenshotAnnotationTool.Select);
            result = ScreenshotCancelResult.ActionCanceled;
        }
        else if (_pendingSelectionStart is not null)
        {
            _pendingSelectionStart = null;
            result = ScreenshotCancelResult.ActionCanceled;
        }
        else
        {
            result = _session.Cancel();
            if (result == ScreenshotCancelResult.SelectionCleared)
            {
                ResetAnnotationsForNewSelection();
            }
        }

        if (result != ScreenshotCancelResult.ExitRequested)
        {
            _annotationBitmapDirty = true;
            _mosaicCacheDirty = true;
            ReleasePointerCapture();
            if (_session.State == ScreenshotSessionState.Selected)
            {
                SetToolCursor(_annotationSession.ActiveTool);
            }
            else
            {
                SetPointerCursor(_crosshairCursor, ScreenshotPointerFeedback.Crosshair);
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        return result;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            _session.State == ScreenshotSessionState.Saving)
        {
            return;
        }

        Focus();
        var position = e.GetPosition(this);
        var physicalPoint = ToPhysicalPoint(position);
        if (_session.State == ScreenshotSessionState.Selected)
        {
            if (HandleMaskPointerPressed(position, e.ClickCount))
            {
                e.Handled = true;
                return;
            }

            if (_annotationSession.TextEdit is not null)
            {
                CommitTextEdit();
                e.Handled = true;
                return;
            }

            if (HitTestSelectionResizeHandle(position) is { } selectionHandle)
            {
                _annotationSession.ClearSelection();
                _session.BeginResizeSelection(selectionHandle);
                CapturePointer(e.Pointer);
                AnnotationSelectionChanged?.Invoke(this, EventArgs.Empty);
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (_session.SelectionContains(physicalPoint) && e.ClickCount >= 2)
            {
                if (TryBeginTextAnnotationEditing(position))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (_annotationSession.ActiveTool is ScreenshotAnnotationTool.Rectangle or
                    ScreenshotAnnotationTool.Arrow &&
                HitTestSelectableShapeAnnotation(position) is { } existingIndex)
            {
                _annotationSession.SetTool(ScreenshotAnnotationTool.Select);
                _annotationSession.Select(existingIndex);
                BeginMoveSelectedAnnotation(ToSelectionLogicalPoint(position));
                _isAnnotationMoveTransform = true;
                CapturePointer(e.Pointer);
                _annotationBitmapDirty = true;
                _mosaicCacheDirty = true;
                SetPointerCursor(_moveCursor, ScreenshotPointerFeedback.MoveAnnotation);
                AnnotationSelectionChanged?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (_annotationSession.ActiveTool == ScreenshotAnnotationTool.Text)
            {
                if (_session.SelectionContains(physicalPoint) &&
                    LogicalSelection is { } textSelection)
                {
                    _annotationSession.CommitText();
                    if (TryBeginTextAnnotationInteraction(position))
                    {
                        CapturePointer(e.Pointer);
                        e.Handled = true;
                        return;
                    }

                    var requestedOrigin = ToSelectionLogicalPoint(position);
                    if (!TryConstrainTextEditorLayout(
                            requestedOrigin,
                            Math.Max(
                                1,
                                textSelection.Width -
                                    requestedOrigin.X -
                                    ScreenshotUiTheme.TextEditorChromeInset),
                            _annotationSession.TextStyle,
                            textSelection,
                            out var origin,
                            out var maxWidth))
                    {
                        e.Handled = true;
                        return;
                    }

                    _annotationSession.BeginText(
                        origin,
                        maxWidth);
                    _selectionHasBeenEdited = true;
                    _annotationBitmapDirty = true;
                    _mosaicCacheDirty = true;
                    AnnotationStarted?.Invoke(this, EventArgs.Empty);
                    TextEditingStarted?.Invoke(this, EventArgs.Empty);
                    InvalidateVisual();
                    e.Handled = true;
                }

                return;
            }

            if (_annotationSession.ActiveTool != ScreenshotAnnotationTool.Select)
            {
                if (_session.SelectionContains(physicalPoint))
                {
                    _annotationSession.Begin(ToSelectionLogicalPoint(position));
                    _selectionHasBeenEdited = true;
                    MarkActiveAnnotationDirty();
                    CapturePointer(e.Pointer);
                    AnnotationStarted?.Invoke(this, EventArgs.Empty);
                    InvalidateVisual();
                    e.Handled = true;
                }

                return;
            }

            if (_session.SelectionContains(physicalPoint))
            {
                var relativePoint = ToSelectionLogicalPoint(position);
                if (_annotationSession.SelectedAnnotation is { } selectedAnnotation &&
                    HitTestAnnotationResizeHandle(
                        selectedAnnotation,
                        relativePoint) is { } selectedHandle)
                {
                    _annotationSession.BeginResizeSelected(selectedHandle);
                    CapturePointer(e.Pointer);
                    _annotationBitmapDirty = true;
                    _mosaicCacheDirty = true;
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }

                if (HitTestAnnotation(relativePoint) is { } hitIndex)
                {
                    _annotationSession.Select(hitIndex);
                    BeginMoveSelectedAnnotation(relativePoint);
                    _isAnnotationMoveTransform = true;
                    CapturePointer(e.Pointer);
                    SetPointerCursor(_moveCursor, ScreenshotPointerFeedback.MoveAnnotation);
                    _annotationBitmapDirty = true;
                    _mosaicCacheDirty = true;
                    AnnotationSelectionChanged?.Invoke(this, EventArgs.Empty);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }
            }

            if (_session.SelectionContains(physicalPoint) && e.ClickCount >= 2)
            {
                SelectionDoubleClicked?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }

            if (_session.SelectionContains(physicalPoint))
            {
                if (_selectionHasBeenEdited)
                {
                    _annotationSession.ClearSelection();
                    AnnotationSelectionChanged?.Invoke(this, EventArgs.Empty);
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                }

                _session.BeginMoveSelection(physicalPoint);
                CapturePointer(e.Pointer);
                SetPointerCursor(_moveCursor, ScreenshotPointerFeedback.MoveSelection);
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }

        position = _pixelInspector.UpdatePointer(position, Bounds.Size);
        _pendingSelectionStart = position;
        CapturePointer(e.Pointer);
        SetPointerCursor(_moveCursor, ScreenshotPointerFeedback.MoveSelection);
        InvalidateVisual();
        e.Handled = true;
    }

    internal bool HandleMaskPointerPressed(Point point, int clickCount)
    {
        if (!IsMaskInteractionArea(point))
        {
            return false;
        }

        SetPointerCursor(_disabledCursor, ScreenshotPointerFeedback.Disabled);
        if (clickCount >= 2)
        {
            SelectionDoubleClicked?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    private bool TryBeginTextAnnotationInteraction(Point position)
    {
        if (HitTestTextAnnotation(position) is not { } textIndex ||
            !_annotationSession.Select(textIndex))
        {
            return false;
        }

        _pendingTextAnnotationInteraction = new(textIndex, position);
        _annotationBitmapDirty = true;
        _mosaicCacheDirty = true;
        SetPointerCursor(_moveCursor, ScreenshotPointerFeedback.MoveAnnotation);
        AnnotationSelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        return true;
    }

    private bool TryBeginTextAnnotationEditing(Point position)
    {
        return HitTestTextAnnotation(position) is { } textIndex &&
            TryBeginTextAnnotationEditing(textIndex);
    }

    private static bool TryConstrainTextEditorLayout(
        LogicalPoint requestedOrigin,
        double requestedMaxWidth,
        ScreenshotTextStyle style,
        Rect selection,
        out LogicalPoint origin,
        out double maxWidth)
    {
        var inset = ScreenshotUiTheme.TextEditorChromeInset;
        var minimumContentWidth = ScreenshotTextMetrics.GetMinimumContentWidth(style);
        var lineHeight = style.FontSize *
            ScreenshotTextMetrics.LineHeightMultiplier;
        if (selection.Width < (inset * 2) + minimumContentWidth ||
            selection.Height < (inset * 2) + lineHeight)
        {
            origin = default;
            maxWidth = default;
            return false;
        }

        origin = new LogicalPoint(
            Math.Clamp(
                requestedOrigin.X,
                inset,
                selection.Width - inset - minimumContentWidth),
            Math.Clamp(
                requestedOrigin.Y,
                inset,
                selection.Height - inset - lineHeight));
        maxWidth = Math.Clamp(
            requestedMaxWidth,
            minimumContentWidth,
            selection.Width - inset - origin.X);
        return true;
    }

    private int? HitTestTextAnnotation(Point position)
    {
        if (!_session.SelectionContains(ToPhysicalPoint(position)))
        {
            return null;
        }

        var annotationIndex = HitTestAnnotation(ToSelectionLogicalPoint(position));
        return annotationIndex is { } textIndex &&
            _annotationSession.Annotations[textIndex] is ScreenshotTextAnnotation
                ? textIndex
                : null;
    }

    private bool TryBeginTextAnnotationEditing(int textIndex)
    {
        if (LogicalSelection is not { } selection ||
            textIndex < 0 ||
            textIndex >= _annotationSession.Annotations.Count ||
            _annotationSession.Annotations[textIndex] is not ScreenshotTextAnnotation text ||
            !TryConstrainTextEditorLayout(
                text.Origin,
                text.MaxWidth,
                text.Style,
                selection,
                out var origin,
                out var maxWidth) ||
            !_annotationSession.Select(textIndex) ||
            !_annotationSession.BeginTextEdit(textIndex, origin, maxWidth))
        {
            return false;
        }

        _annotationBitmapDirty = true;
        _mosaicCacheDirty = true;
        AnnotationStarted?.Invoke(this, EventArgs.Empty);
        TextEditingStarted?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        return true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_session.State == ScreenshotSessionState.Saving)
        {
            return;
        }

        var rawPosition = e.GetPosition(this);
        if (_pendingTextAnnotationInteraction is { } pendingTextInteraction)
        {
            if (!ScreenshotPointerGesture.HasExceededDragThreshold(
                    new LogicalPoint(
                        pendingTextInteraction.Start.X,
                        pendingTextInteraction.Start.Y),
                    new LogicalPoint(rawPosition.X, rawPosition.Y)))
            {
                SetPointerCursor(_moveCursor, ScreenshotPointerFeedback.MoveAnnotation);
                e.Handled = true;
                return;
            }

            _pendingTextAnnotationInteraction = null;
            BeginMoveSelectedAnnotation(
                ToSelectionLogicalPoint(pendingTextInteraction.Start));
            _isAnnotationMoveTransform = true;
            SetPointerCursor(_moveCursor, ScreenshotPointerFeedback.MoveAnnotation);
            UpdateSelectedAnnotationTransform(rawPosition);
            _annotationBitmapDirty = true;
            _mosaicCacheDirty = true;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_annotationSession.IsTransforming)
        {
            if (_isAnnotationMoveTransform)
            {
                SetPointerCursor(_moveCursor, ScreenshotPointerFeedback.MoveAnnotation);
            }

            UpdateSelectedAnnotationTransform(rawPosition);
            _annotationBitmapDirty = true;
            _mosaicCacheDirty = true;
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_pendingSelectionStart is { } start)
        {
            var clampedPosition = _pixelInspector.UpdatePointer(rawPosition, Bounds.Size);
            if (ScreenshotPointerGesture.HasExceededDragThreshold(
                    new LogicalPoint(start.X, start.Y),
                    new LogicalPoint(clampedPosition.X, clampedPosition.Y)))
            {
                _pendingSelectionStart = null;
                _hoveredSnapTarget = null;
                ResetAnnotationsForNewSelection();
                _session.BeginSelection(ToPhysicalPoint(start));
                _session.UpdateSelection(ToPhysicalPoint(clampedPosition));
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_annotationSession.Preview is not null)
        {
            _annotationSession.Update(ToSelectionLogicalPoint(rawPosition));
            MarkActiveAnnotationDirty();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_session.State == ScreenshotSessionState.Selected)
        {
            if (_annotationSession.ActiveTool != ScreenshotAnnotationTool.Select)
            {
                if (HitTestSelectionResizeHandle(rawPosition) is { } selectionHandle)
                {
                    SetResizePointerFeedback(selectionHandle);
                    return;
                }

                if (IsMaskInteractionArea(rawPosition))
                {
                    SetPointerCursor(_disabledCursor, ScreenshotPointerFeedback.Disabled);
                    return;
                }

                if (_annotationSession.ActiveTool is ScreenshotAnnotationTool.Rectangle or
                        ScreenshotAnnotationTool.Arrow &&
                    HitTestSelectableShapeAnnotation(rawPosition) is not null)
                {
                    SetPointerCursor(_moveCursor, ScreenshotPointerFeedback.MoveAnnotation);
                    return;
                }

                if (_annotationSession.ActiveTool == ScreenshotAnnotationTool.Text &&
                    HitTestTextAnnotation(rawPosition) is not null)
                {
                    SetPointerCursor(_moveCursor, ScreenshotPointerFeedback.MoveAnnotation);
                    return;
                }

                SetToolCursor(_annotationSession.ActiveTool);
                return;
            }

            UpdateSelectedPointerFeedback(rawPosition);
            return;
        }

        if (_session.State == ScreenshotSessionState.ResizingSelection)
        {
            var previousSelection = LogicalSelection;
            _session.UpdateResizeSelection(ToPhysicalPoint(rawPosition));
            RebaseAnnotations(previousSelection, LogicalSelection);
            _annotationBitmapDirty = true;
            _mosaicCacheDirty = true;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_session.State == ScreenshotSessionState.MovingSelection)
        {
            _session.UpdateMoveSelection(ToPhysicalPoint(rawPosition));
            _mosaicCacheDirty = true;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_session.State == ScreenshotSessionState.Ready)
        {
            var hoverPosition = _pixelInspector.UpdatePointer(rawPosition, Bounds.Size);
            var physicalHoverPosition = ToPhysicalPoint(hoverPosition);
            if (_snapHoverOrigin is { } origin && physicalHoverPosition == origin)
            {
                _hoveredSnapTarget = null;
                InvalidateVisual();
                return;
            }

            _snapHoverOrigin = null;
            _hoveredSnapTarget = ScreenshotWindowSelector.HitTest(
                _windowCandidates,
                physicalHoverPosition,
                _frame.PhysicalSize);
            InvalidateVisual();
            return;
        }

        var position = _pixelInspector.UpdatePointer(rawPosition, Bounds.Size);
        if (_session.State == ScreenshotSessionState.Selecting)
        {
            _session.UpdateSelection(ToPhysicalPoint(position));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }

        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        HandlePointerCaptureLost();
    }

    internal void HandlePointerCaptureLost()
    {
        _capturedPointer = null;
        var hadPendingSelection = _pendingSelectionStart is not null;
        _pendingSelectionStart = null;
        var hadPendingTextInteraction = _pendingTextAnnotationInteraction is not null;
        _pendingTextAnnotationInteraction = null;
        var wasAnnotationMoveTransform = _isAnnotationMoveTransform;
        _isAnnotationMoveTransform = false;
        var annotationTransformCanceled = _annotationSession.CancelSelectedTransform();
        var annotationPreviewCanceled = _annotationSession.CancelPreview();
        var previousSelection = LogicalSelection;
        var selectionGestureCanceled = _session.State is
            ScreenshotSessionState.Selecting or
            ScreenshotSessionState.MovingSelection or
            ScreenshotSessionState.ResizingSelection;
        if (selectionGestureCanceled)
        {
            _session.Cancel();
            RebaseAnnotations(previousSelection, LogicalSelection);
        }

        if (!hadPendingSelection &&
            !hadPendingTextInteraction &&
            !annotationTransformCanceled &&
            !annotationPreviewCanceled &&
            !selectionGestureCanceled)
        {
            return;
        }

        _annotationBitmapDirty = true;
        _mosaicCacheDirty = true;
        if (wasAnnotationMoveTransform || hadPendingTextInteraction)
        {
            SetPointerCursor(_moveCursor, ScreenshotPointerFeedback.MoveAnnotation);
        }
        else if (_session.State == ScreenshotSessionState.Selected)
        {
            SetToolCursor(_annotationSession.ActiveTool);
        }
        else
        {
            SetPointerCursor(_crosshairCursor, ScreenshotPointerFeedback.Crosshair);
        }

        if (annotationTransformCanceled)
        {
            AnnotationSelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        if (selectionGestureCanceled)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_annotationSession.IsTransforming)
        {
            UpdateSelectedAnnotationTransform(e.GetPosition(this));
            var changed = _annotationSession.CompleteSelectedTransform();
            ReleasePointerCapture();
            UpdateSelectedPointerFeedback(e.GetPosition(this));
            _annotationBitmapDirty = true;
            _mosaicCacheDirty = true;
            if (changed)
            {
                AnnotationHistoryChanged?.Invoke(this, EventArgs.Empty);
            }

            AnnotationSelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_pendingTextAnnotationInteraction is { } pendingTextInteraction)
        {
            _pendingTextAnnotationInteraction = null;
            ReleasePointerCapture();
            TryBeginTextAnnotationEditing(pendingTextInteraction.AnnotationIndex);
            e.Handled = true;
            return;
        }

        if (_annotationSession.Preview is not null)
        {
            _annotationSession.Update(ToSelectionLogicalPoint(e.GetPosition(this)));
            _annotationSession.Complete();
            MarkActiveAnnotationDirty();
            ReleasePointerCapture();
            InvalidateVisual();
            AnnotationHistoryChanged?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (_pendingSelectionStart is not null)
        {
            _pendingSelectionStart = null;
            var snapPosition = _pixelInspector.UpdatePointer(e.GetPosition(this), Bounds.Size);
            if (_snapHoverOrigin is { } origin && ToPhysicalPoint(snapPosition) == origin)
            {
                ReleasePointerCapture();
                SetPointerCursor(_crosshairCursor, ScreenshotPointerFeedback.Crosshair);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            var snapTarget = ScreenshotWindowSelector.HitTest(
                _windowCandidates,
                ToPhysicalPoint(snapPosition),
                _frame.PhysicalSize);
            ResetAnnotationsForNewSelection();
            _session.Select(snapTarget.Bounds);
            _hoveredSnapTarget = null;
            ReleasePointerCapture();
            SetPointerCursor(_moveCursor, ScreenshotPointerFeedback.MoveSelection);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_session.State == ScreenshotSessionState.MovingSelection)
        {
            _session.UpdateMoveSelection(ToPhysicalPoint(e.GetPosition(this)));
            _session.CompleteMoveSelection();
            ReleasePointerCapture();
            SetPointerCursor(_moveCursor, ScreenshotPointerFeedback.MoveSelection);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_session.State == ScreenshotSessionState.ResizingSelection)
        {
            var previousSelection = LogicalSelection;
            _session.UpdateResizeSelection(ToPhysicalPoint(e.GetPosition(this)));
            RebaseAnnotations(previousSelection, LogicalSelection);
            _session.CompleteResizeSelection();
            _annotationBitmapDirty = true;
            _mosaicCacheDirty = true;
            ReleasePointerCapture();
            UpdateSelectedPointerFeedback(e.GetPosition(this));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_session.State != ScreenshotSessionState.Selecting)
        {
            return;
        }

        var position = _pixelInspector.UpdatePointer(e.GetPosition(this), Bounds.Size);
        _session.UpdateSelection(ToPhysicalPoint(position));
        var completed = _session.CompleteSelection();
        ReleasePointerCapture();
        if (completed)
        {
            UpdateSelectedPointerFeedback(position);
        }
        else
        {
            SetPointerCursor(_crosshairCursor, ScreenshotPointerFeedback.Crosshair);
        }
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        e.Handled = true;
    }

    private unsafe static WriteableBitmap CreateBitmap(CapturedFrame frame)
    {
        fixed (byte* pixelPointer = frame.Pixels.Span)
        {
            return new WriteableBitmap(
                PixelFormat.Bgra8888,
                AlphaFormat.Premul,
                (nint)pixelPointer,
                new PixelSize(frame.PhysicalSize.Width, frame.PhysicalSize.Height),
                new Vector(96, 96),
                frame.Stride);
        }
    }

    private void DrawSnapTarget(DrawingContext context, PhysicalRect target)
    {
        var source = new Rect(target.X, target.Y, target.Width, target.Height);
        var destination = ToLogicalRect(target);
        context.DrawImage(_bitmap, source, destination);
        context.DrawRectangle(brush: null, SelectionPen, destination);
    }

    private void DrawAnnotations(
        DrawingContext context,
        PhysicalRect selection,
        Rect destination)
    {
        var annotations = _annotationSession.EnumerateForRendering().ToArray();
        DrawMosaicTiles(
            context,
            selection,
            destination,
            annotations.OfType<ScreenshotMosaicAnnotation>().ToArray());

        if (_annotationBitmapDirty)
        {
            _annotationBitmap?.Dispose();
            _annotationBitmap = null;
            var vectorAnnotations = annotations
                .Where(annotation => annotation is not ScreenshotMosaicAnnotation)
                .ToArray();
            if (vectorAnnotations.Length > 0)
            {
                var raster = ScreenshotAnnotationRenderer.RenderBgra(
                    selection.Width,
                    selection.Height,
                    _frame.ScaleX,
                    _frame.ScaleY,
                    vectorAnnotations);
                _annotationBitmap = CreateBitmap(raster);
            }

            _annotationBitmapDirty = false;
        }

        if (_annotationBitmap is null)
        {
            return;
        }

        context.DrawImage(
            _annotationBitmap,
            new Rect(0, 0, selection.Width, selection.Height),
            destination);
    }

    private void DrawMosaicTiles(
        DrawingContext context,
        PhysicalRect selection,
        Rect destination,
        ScreenshotMosaicAnnotation[] mosaics)
    {
        if (mosaics.Length == 0)
        {
            ResetMosaicCache();
            return;
        }

        if (_mosaicTileCache is null || _mosaicSelection != selection)
        {
            ResetMosaicCache();
            var pixels = CopySelectionPixels(selection);
            _mosaicTileCache = new ScreenshotMosaicTileCache(
                selection.Width,
                selection.Height,
                selection.Width * 4,
                pixels,
                _frame.ScaleX,
                _frame.ScaleY);
            _mosaicSelection = selection;
            _mosaicCacheDirty = true;
        }

        if (_mosaicCacheDirty)
        {
            var update = _mosaicTileCache.Update(mosaics);
            foreach (var tile in update.ChangedTiles)
            {
                if (_mosaicTileBitmaps.Remove(tile.Key, out var previousBitmap))
                {
                    previousBitmap.Dispose();
                }

                _mosaicTileBitmaps.Add(tile.Key, CreateBitmap(tile));
            }

            _mosaicCacheDirty = false;
        }

        foreach (var tile in _mosaicTileCache.Tiles)
        {
            if (!_mosaicTileBitmaps.TryGetValue(tile.Key, out var bitmap))
            {
                continue;
            }

            context.DrawImage(
                bitmap,
                new Rect(0, 0, tile.Width, tile.Height),
                new Rect(
                    destination.X + (tile.X / _frame.ScaleX),
                    destination.Y + (tile.Y / _frame.ScaleY),
                    tile.Width / _frame.ScaleX,
                    tile.Height / _frame.ScaleY));
        }
    }

    private unsafe static WriteableBitmap CreateBitmap(AnnotationRaster raster)
    {
        fixed (byte* pixelPointer = raster.Pixels)
        {
            return new WriteableBitmap(
                PixelFormat.Bgra8888,
                AlphaFormat.Premul,
                (nint)pixelPointer,
                new PixelSize(raster.Width, raster.Height),
                new Vector(96, 96),
                raster.Stride);
        }
    }

    private unsafe static WriteableBitmap CreateBitmap(MosaicTileRaster tile)
    {
        fixed (byte* pixelPointer = tile.Pixels)
        {
            return new WriteableBitmap(
                PixelFormat.Bgra8888,
                AlphaFormat.Premul,
                (nint)pixelPointer,
                new PixelSize(tile.Width, tile.Height),
                new Vector(96, 96),
                tile.Stride);
        }
    }

    private PhysicalPoint ToPhysicalPoint(Point point) =>
        _frame.ToPhysicalPoint(new LogicalPoint(point.X, point.Y));

    private void CapturePointer(IPointer pointer)
    {
        _capturedPointer = pointer;
        pointer.Capture(this);
    }

    private void ReleasePointerCapture()
    {
        _capturedPointer?.Capture(control: null);
        _capturedPointer = null;
        _isAnnotationMoveTransform = false;
    }

    private LogicalPoint ToSelectionLogicalPoint(Point point)
    {
        if (LogicalSelection is not { } selection)
        {
            throw new InvalidOperationException("A completed selection is required for annotations.");
        }

        return new LogicalPoint(
            Math.Clamp(point.X - selection.X, 0, selection.Width),
            Math.Clamp(point.Y - selection.Y, 0, selection.Height));
    }

    private void UpdateSelectedAnnotationTransform(Point point)
    {
        _annotationSession.UpdateSelectedTransform(ToSelectionLogicalPoint(point));
    }

    private bool BeginMoveSelectedAnnotation(LogicalPoint point)
    {
        if (LogicalSelection is not { } selection ||
            _annotationSession.SelectedAnnotation is not { } annotation)
        {
            throw new InvalidOperationException(
                "A completed selection is required for annotation transforms.");
        }

        var constraint = annotation is
            ScreenshotRectangleAnnotation or
            ScreenshotArrowAnnotation or
            ScreenshotTextAnnotation
                ? CreateMovementConstraint(annotation, selection)
                : AnnotationMovementConstraint.Unbounded;
        return _annotationSession.BeginMoveSelected(point, constraint);
    }

    private static AnnotationMovementConstraint CreateMovementConstraint(
        IScreenshotAnnotation annotation,
        Rect selection)
    {
        var bounds = annotation is ScreenshotTextAnnotation text
            ? ToAnnotationBounds(
                ScreenshotTextEditorLayout.Measure(
                    text,
                    new Rect(0, 0, selection.Width, selection.Height)))
            : ScreenshotAnnotationRenderer.MeasureVisualBounds(annotation);
        return AnnotationMovementConstraint.Within(
            new LogicalSize(selection.Width, selection.Height),
            new LogicalPoint(bounds.Left, bounds.Top),
            new LogicalPoint(bounds.Right, bounds.Bottom));
    }

    private static ScreenshotAnnotationBounds ToAnnotationBounds(Rect bounds) =>
        new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);

    private void ResetAnnotationsForNewSelection()
    {
        _pendingTextAnnotationInteraction = null;
        _annotationSession.Clear();
        _annotationSession.SetTool(ScreenshotAnnotationTool.Select);
        _selectionHasBeenEdited = false;
        _annotationBitmapDirty = true;
        ResetMosaicCache();
        AnnotationSelectionChanged?.Invoke(this, EventArgs.Empty);
        AnnotationHistoryChanged?.Invoke(this, EventArgs.Empty);
        SelectionReplaced?.Invoke(this, EventArgs.Empty);
    }

    private void AnnotationChanged()
    {
        _annotationBitmapDirty = true;
        _mosaicCacheDirty = true;
        AnnotationSelectionChanged?.Invoke(this, EventArgs.Empty);
        AnnotationHistoryChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void RebaseAnnotations(Rect? previous, Rect? current)
    {
        if (previous is not { } oldSelection || current is not { } newSelection)
        {
            return;
        }

        _annotationSession.RebaseForSelectionOriginChange(new LogicalPoint(
            oldSelection.X - newSelection.X,
            oldSelection.Y - newSelection.Y));
    }

    private byte[] CopySelectionPixels(PhysicalRect selection)
    {
        var stride = checked(selection.Width * 4);
        var pixels = new byte[checked(stride * selection.Height)];
        for (var row = 0; row < selection.Height; row++)
        {
            var sourceOffset =
                ((selection.Y + row) * _frame.Stride) + (selection.X * 4);
            _frame.Pixels.Span.Slice(sourceOffset, stride)
                .CopyTo(pixels.AsSpan(row * stride, stride));
        }

        return pixels;
    }

    private void MarkActiveAnnotationDirty()
    {
        if (_annotationSession.ActiveTool == ScreenshotAnnotationTool.Mosaic ||
            _annotationSession.Preview is ScreenshotMosaicAnnotation)
        {
            _mosaicCacheDirty = true;
        }
        else
        {
            _annotationBitmapDirty = true;
        }
    }

    private void ResetMosaicCache()
    {
        _mosaicTileCache?.Dispose();
        _mosaicTileCache = null;
        _mosaicSelection = null;
        _mosaicCacheDirty = true;
        DisposeMosaicTileBitmaps();
    }

    private void DisposeMosaicTileBitmaps()
    {
        foreach (var bitmap in _mosaicTileBitmaps.Values)
        {
            bitmap.Dispose();
        }

        _mosaicTileBitmaps.Clear();
    }

    private void SetToolCursor(ScreenshotAnnotationTool tool)
    {
        switch (tool)
        {
            case ScreenshotAnnotationTool.Select when
                _session.State == ScreenshotSessionState.Selected &&
                !_selectionHasBeenEdited:
                SetPointerCursor(_moveCursor, ScreenshotPointerFeedback.MoveSelection);
                break;
            case ScreenshotAnnotationTool.Select when
                _session.State == ScreenshotSessionState.Selected:
                SetPointerCursor(_defaultCursor, ScreenshotPointerFeedback.Default);
                break;
            case ScreenshotAnnotationTool.Text:
                SetPointerCursor(_textCursor, ScreenshotPointerFeedback.Text);
                break;
            default:
                SetPointerCursor(_crosshairCursor, ScreenshotPointerFeedback.Crosshair);
                break;
        }
    }

    private void UpdateSelectedPointerFeedback(Point point)
    {
        if (_session.Selection is null)
        {
            SetPointerCursor(_crosshairCursor, ScreenshotPointerFeedback.Crosshair);
            return;
        }

        if (HitTestSelectionResizeHandle(point) is { } selectionHandle)
        {
            SetResizePointerFeedback(selectionHandle);
            return;
        }

        if (!_session.SelectionContains(ToPhysicalPoint(point)))
        {
            SetPointerCursor(_disabledCursor, ScreenshotPointerFeedback.Disabled);
            return;
        }

        var relativePoint = ToSelectionLogicalPoint(point);
        if (_annotationSession.SelectedAnnotation is { } selectedAnnotation &&
            HitTestAnnotationResizeHandle(selectedAnnotation, relativePoint) is { } annotationHandle)
        {
            SetResizePointerFeedback(selectedAnnotation, annotationHandle);
            return;
        }

        if (HitTestAnnotation(relativePoint) is not null)
        {
            SetPointerCursor(_moveCursor, ScreenshotPointerFeedback.MoveAnnotation);
            return;
        }

        if (!_selectionHasBeenEdited)
        {
            SetPointerCursor(_moveCursor, ScreenshotPointerFeedback.MoveSelection);
            return;
        }

        SetPointerCursor(_defaultCursor, ScreenshotPointerFeedback.Default);
    }

    private int? HitTestAnnotation(LogicalPoint point) =>
        _annotationSession.HitTest(
            point,
            static (text, candidate) =>
                ScreenshotAnnotationRenderer.MeasureText(text).Contains(candidate));

    private SelectionResizeHandle? HitTestSelectionResizeHandle(Point point) =>
        _session.Selection is { } selection
            ? HitTestResizeHandle(point, ToLogicalRect(selection))
            : null;

    private int? HitTestSelectableShapeAnnotation(Point point)
    {
        if (!_session.SelectionContains(ToPhysicalPoint(point)))
        {
            return null;
        }

        var annotationIndex = HitTestAnnotation(ToSelectionLogicalPoint(point));
        return annotationIndex is { } index &&
            _annotationSession.Annotations[index] is
                ScreenshotRectangleAnnotation or ScreenshotArrowAnnotation
                ? index
                : null;
    }

    private void SetResizePointerFeedback(SelectionResizeHandle handle)
    {
        var cursorType = GetResizeCursorType(handle);
        SetPointerCursor(_resizeCursors[cursorType], GetResizePointerFeedback(cursorType));
    }

    private void SetResizePointerFeedback(
        IScreenshotAnnotation annotation,
        AnnotationResizeHandle handle)
    {
        var cursorType = annotation is ScreenshotArrowAnnotation
            ? StandardCursorType.DragMove
            : GetResizeCursorType(handle);
        SetPointerCursor(_resizeCursors[cursorType], GetResizePointerFeedback(cursorType));
    }

    internal static StandardCursorType GetResizeCursorType(SelectionResizeHandle handle) =>
        handle switch
        {
            SelectionResizeHandle.Left or SelectionResizeHandle.Right =>
                StandardCursorType.SizeWestEast,
            SelectionResizeHandle.Top or SelectionResizeHandle.Bottom =>
                StandardCursorType.SizeNorthSouth,
            SelectionResizeHandle.TopLeft => StandardCursorType.TopLeftCorner,
            SelectionResizeHandle.TopRight => StandardCursorType.TopRightCorner,
            SelectionResizeHandle.BottomRight => StandardCursorType.BottomRightCorner,
            SelectionResizeHandle.BottomLeft => StandardCursorType.BottomLeftCorner,
            _ => throw new ArgumentOutOfRangeException(nameof(handle)),
        };

    internal static StandardCursorType GetResizeCursorType(AnnotationResizeHandle handle) =>
        handle switch
        {
            AnnotationResizeHandle.Start or AnnotationResizeHandle.End =>
                StandardCursorType.DragMove,
            _ => GetResizeCursorType(ToSelectionResizeHandle(handle)),
        };

    private static SelectionResizeHandle ToSelectionResizeHandle(
        AnnotationResizeHandle handle) =>
        handle switch
        {
            AnnotationResizeHandle.TopLeft => SelectionResizeHandle.TopLeft,
            AnnotationResizeHandle.Top => SelectionResizeHandle.Top,
            AnnotationResizeHandle.TopRight => SelectionResizeHandle.TopRight,
            AnnotationResizeHandle.Right => SelectionResizeHandle.Right,
            AnnotationResizeHandle.BottomRight => SelectionResizeHandle.BottomRight,
            AnnotationResizeHandle.Bottom => SelectionResizeHandle.Bottom,
            AnnotationResizeHandle.BottomLeft => SelectionResizeHandle.BottomLeft,
            AnnotationResizeHandle.Left => SelectionResizeHandle.Left,
            _ => throw new ArgumentOutOfRangeException(nameof(handle)),
        };

    private static ScreenshotPointerFeedback GetResizePointerFeedback(
        StandardCursorType cursorType) =>
        cursorType switch
        {
            StandardCursorType.SizeWestEast => ScreenshotPointerFeedback.ResizeHorizontal,
            StandardCursorType.SizeNorthSouth => ScreenshotPointerFeedback.ResizeVertical,
            StandardCursorType.DragMove => ScreenshotPointerFeedback.ResizeArrow,
            _ => ScreenshotPointerFeedback.ResizeDiagonal,
        };

    private void SetPointerCursor(Cursor cursor, ScreenshotPointerFeedback feedback)
    {
        Cursor = cursor;
        PointerFeedback = feedback;
    }

    private static SelectionResizeHandle? HitTestResizeHandle(Point point, Rect selection)
    {
        var hitRadius = Math.Max(
            ScreenshotUiTheme.SelectionHandleMinimumHitRadius,
            (ScreenshotUiTheme.SelectionHandleSize / 2) +
            ScreenshotUiTheme.SelectionHandleHitPadding);
        var handles = new (SelectionResizeHandle Handle, Point Center)[]
        {
            (SelectionResizeHandle.TopLeft, selection.TopLeft),
            (SelectionResizeHandle.Top, new Point(selection.Center.X, selection.Top)),
            (SelectionResizeHandle.TopRight, selection.TopRight),
            (SelectionResizeHandle.Right, new Point(selection.Right, selection.Center.Y)),
            (SelectionResizeHandle.BottomRight, selection.BottomRight),
            (SelectionResizeHandle.Bottom, new Point(selection.Center.X, selection.Bottom)),
            (SelectionResizeHandle.BottomLeft, selection.BottomLeft),
            (SelectionResizeHandle.Left, new Point(selection.Left, selection.Center.Y)),
        };

        foreach (var handle in handles)
        {
            var hitBounds = new Rect(
                handle.Center.X - hitRadius,
                handle.Center.Y - hitRadius,
                hitRadius * 2,
                hitRadius * 2);
            if (hitBounds.Contains(point))
            {
                return handle.Handle;
            }
        }

        return HitTestResizeBorder(point, selection, hitRadius) is { } border
            ? ToSelectionResizeHandle(border)
            : null;
    }

    private static AnnotationResizeHandle? HitTestAnnotationResizeHandle(
        IScreenshotAnnotation? annotation,
        LogicalPoint point)
    {
        const double radius = ScreenshotUiTheme.AnnotationControlHitRadius;
        if (annotation is ScreenshotRectangleAnnotation rectangle)
        {
            foreach (var controlPoint in GetRectangleControlPoints(rectangle))
            {
                if (Distance(controlPoint.Center, point) <= radius)
                {
                    return controlPoint.Handle;
                }
            }

            var left = Math.Min(rectangle.Start.X, rectangle.End.X);
            var top = Math.Min(rectangle.Start.Y, rectangle.End.Y);
            var bounds = new Rect(
                left,
                top,
                Math.Abs(rectangle.End.X - rectangle.Start.X),
                Math.Abs(rectangle.End.Y - rectangle.Start.Y));
            return HitTestResizeBorder(
                    new Point(point.X, point.Y),
                    bounds,
                    radius) is { } border
                ? ToAnnotationResizeHandle(border)
                : null;
        }

        if (annotation is not ScreenshotArrowAnnotation arrow)
        {
            return null;
        }

        if (Distance(arrow.Start, point) <= radius)
        {
            return AnnotationResizeHandle.Start;
        }

        if (Distance(arrow.End, point) <= radius)
        {
            return AnnotationResizeHandle.End;
        }

        return null;
    }

    private static ResizeBorder? HitTestResizeBorder(
        Point point,
        Rect bounds,
        double hitRadius)
    {
        ResizeBorder? nearestBorder = null;
        var nearestDistance = double.PositiveInfinity;

        void Consider(ResizeBorder border, double distance)
        {
            if (distance <= hitRadius && distance < nearestDistance)
            {
                nearestBorder = border;
                nearestDistance = distance;
            }
        }

        if (point.X >= bounds.Left && point.X <= bounds.Right)
        {
            Consider(ResizeBorder.Top, Math.Abs(point.Y - bounds.Top));
            Consider(ResizeBorder.Bottom, Math.Abs(point.Y - bounds.Bottom));
        }

        if (point.Y >= bounds.Top && point.Y <= bounds.Bottom)
        {
            Consider(ResizeBorder.Left, Math.Abs(point.X - bounds.Left));
            Consider(ResizeBorder.Right, Math.Abs(point.X - bounds.Right));
        }

        return nearestBorder;
    }

    private static AnnotationResizeHandle ToAnnotationResizeHandle(
        ResizeBorder border) =>
        border switch
        {
            ResizeBorder.Top => AnnotationResizeHandle.Top,
            ResizeBorder.Right => AnnotationResizeHandle.Right,
            ResizeBorder.Bottom => AnnotationResizeHandle.Bottom,
            ResizeBorder.Left => AnnotationResizeHandle.Left,
            _ => throw new ArgumentOutOfRangeException(nameof(border)),
        };

    private static SelectionResizeHandle ToSelectionResizeHandle(
        ResizeBorder border) =>
        border switch
        {
            ResizeBorder.Top => SelectionResizeHandle.Top,
            ResizeBorder.Right => SelectionResizeHandle.Right,
            ResizeBorder.Bottom => SelectionResizeHandle.Bottom,
            ResizeBorder.Left => SelectionResizeHandle.Left,
            _ => throw new ArgumentOutOfRangeException(nameof(border)),
        };

    private void DrawSelectedAnnotationControls(DrawingContext context, Rect selection)
    {
        if (_annotationSession.TextEdit is not null ||
            _annotationSession.SelectedAnnotation is not { } annotation)
        {
            return;
        }

        var controlPen = new Pen(
            ScreenshotUiTheme.AccentBrush,
            ScreenshotUiTheme.AnnotationControlBorderThickness);
        switch (annotation)
        {
            case ScreenshotRectangleAnnotation rectangle:
                DrawAnnotationBounds(context, selection, rectangle.Start, rectangle.End, controlPen);
                foreach (var controlPoint in GetRectangleControlPoints(rectangle))
                {
                    DrawAnnotationHandle(context, selection, controlPoint.Center);
                }
                break;

            case ScreenshotArrowAnnotation arrow:
                context.DrawLine(
                    controlPen,
                    ToCanvasPoint(selection, arrow.Start),
                    ToCanvasPoint(selection, arrow.End));
                DrawAnnotationHandle(context, selection, arrow.Start);
                DrawAnnotationHandle(context, selection, arrow.End);
                break;

            case ScreenshotTextAnnotation text:
                if (_pendingTextAnnotationInteraction is null &&
                    !_isAnnotationMoveTransform)
                {
                    break;
                }

                var textBounds = ScreenshotTextEditorLayout.Measure(text, selection);
                context.DrawRectangle(
                    brush: null,
                    controlPen,
                    textBounds);
                break;

            case ScreenshotMosaicAnnotation mosaic when mosaic.Points.Count > 0:
                var radius = mosaic.Style.BrushSize / 2d;
                var start = new LogicalPoint(
                    mosaic.Points.Min(point => point.X) - radius,
                    mosaic.Points.Min(point => point.Y) - radius);
                var end = new LogicalPoint(
                    mosaic.Points.Max(point => point.X) + radius,
                    mosaic.Points.Max(point => point.Y) + radius);
                DrawAnnotationBounds(context, selection, start, end, controlPen);
                break;
        }
    }

    private static void DrawAnnotationBounds(
        DrawingContext context,
        Rect selection,
        LogicalPoint start,
        LogicalPoint end,
        Pen pen)
    {
        var left = selection.X + Math.Min(start.X, end.X);
        var top = selection.Y + Math.Min(start.Y, end.Y);
        context.DrawRectangle(
            brush: null,
            pen,
            new Rect(
                left,
                top,
                Math.Abs(end.X - start.X),
                Math.Abs(end.Y - start.Y)));
    }

    private static void DrawAnnotationHandle(
        DrawingContext context,
        Rect selection,
        LogicalPoint point)
    {
        const double size = ScreenshotUiTheme.AnnotationControlSize;
        var center = ToCanvasPoint(selection, point);
        context.DrawRectangle(
            ScreenshotUiTheme.AnnotationControlFillBrush,
            new Pen(
                ScreenshotUiTheme.AccentBrush,
                ScreenshotUiTheme.AnnotationControlBorderThickness),
            new Rect(center.X - (size / 2), center.Y - (size / 2), size, size),
            ScreenshotUiTheme.AnnotationControlCornerRadius,
            ScreenshotUiTheme.AnnotationControlCornerRadius);
    }

    private static Point ToCanvasPoint(Rect selection, LogicalPoint point) =>
        new(selection.X + point.X, selection.Y + point.Y);

    private static (AnnotationResizeHandle Handle, LogicalPoint Center)[]
        GetRectangleControlPoints(ScreenshotRectangleAnnotation rectangle)
    {
        var left = Math.Min(rectangle.Start.X, rectangle.End.X);
        var top = Math.Min(rectangle.Start.Y, rectangle.End.Y);
        var right = Math.Max(rectangle.Start.X, rectangle.End.X);
        var bottom = Math.Max(rectangle.Start.Y, rectangle.End.Y);
        var centerX = (left + right) / 2;
        var centerY = (top + bottom) / 2;
        return
        [
            (AnnotationResizeHandle.TopLeft, new LogicalPoint(left, top)),
            (AnnotationResizeHandle.Top, new LogicalPoint(centerX, top)),
            (AnnotationResizeHandle.TopRight, new LogicalPoint(right, top)),
            (AnnotationResizeHandle.Right, new LogicalPoint(right, centerY)),
            (AnnotationResizeHandle.BottomRight, new LogicalPoint(right, bottom)),
            (AnnotationResizeHandle.Bottom, new LogicalPoint(centerX, bottom)),
            (AnnotationResizeHandle.BottomLeft, new LogicalPoint(left, bottom)),
            (AnnotationResizeHandle.Left, new LogicalPoint(left, centerY)),
        ];
    }

    private static double Distance(LogicalPoint first, LogicalPoint second)
    {
        var deltaX = second.X - first.X;
        var deltaY = second.Y - first.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    private static void DrawSelectionHandles(DrawingContext context, Rect selection)
    {
        DrawSelectionHandle(context, selection.TopLeft);
        DrawSelectionHandle(context, new Point(selection.Center.X, selection.Top));
        DrawSelectionHandle(context, selection.TopRight);
        DrawSelectionHandle(context, new Point(selection.Right, selection.Center.Y));
        DrawSelectionHandle(context, selection.BottomRight);
        DrawSelectionHandle(context, new Point(selection.Center.X, selection.Bottom));
        DrawSelectionHandle(context, selection.BottomLeft);
        DrawSelectionHandle(context, new Point(selection.Left, selection.Center.Y));
    }

    private static void DrawSelectionHandle(DrawingContext context, Point center)
    {
        var handle = new Rect(
            center.X - (ScreenshotUiTheme.SelectionHandleSize / 2),
            center.Y - (ScreenshotUiTheme.SelectionHandleSize / 2),
            ScreenshotUiTheme.SelectionHandleSize,
            ScreenshotUiTheme.SelectionHandleSize);
        context.DrawRectangle(
            ScreenshotUiTheme.AccentBrush,
            pen: null,
            handle,
            ScreenshotUiTheme.SelectionHandleCornerRadius,
            ScreenshotUiTheme.SelectionHandleCornerRadius);
    }

    private Rect ToLogicalRect(PhysicalRect rect) =>
        new(
            rect.X / _frame.ScaleX,
            rect.Y / _frame.ScaleY,
            rect.Width / _frame.ScaleX,
            rect.Height / _frame.ScaleY);
}
