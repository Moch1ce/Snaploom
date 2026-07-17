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
    private static readonly Pen SelectionPen = new(ScreenshotUiTheme.AccentBrush, 2);

    private readonly CapturedFrame _frame;
    private readonly ScreenshotSession _session;
    private readonly ScreenshotAnnotationSession _annotationSession = new();
    private readonly IReadOnlyList<ScreenshotWindowCandidate> _windowCandidates;
    private readonly WriteableBitmap _bitmap;
    private readonly ScreenshotPixelInspector _pixelInspector;
    private readonly Cursor _crosshairCursor = new(StandardCursorType.Cross);
    private readonly Cursor _moveCursor = new(StandardCursorType.SizeAll);
    private readonly Cursor _textCursor = new(StandardCursorType.Ibeam);
    private readonly Cursor _defaultCursor = new(StandardCursorType.Arrow);
    private readonly Cursor _horizontalResizeCursor = new(StandardCursorType.SizeWestEast);
    private readonly Cursor _verticalResizeCursor = new(StandardCursorType.SizeNorthSouth);
    private readonly Cursor _northWestSouthEastResizeCursor =
        new(StandardCursorType.TopLeftCorner);
    private readonly Cursor _northEastSouthWestResizeCursor =
        new(StandardCursorType.TopRightCorner);
    private readonly Cursor _arrowEndpointCursor = new(StandardCursorType.DragMove);
    private Point? _pendingSelectionStart;
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
        _horizontalResizeCursor.Dispose();
        _verticalResizeCursor.Dispose();
        _northWestSouthEastResizeCursor.Dispose();
        _northEastSouthWestResizeCursor.Dispose();
        _arrowEndpointCursor.Dispose();
        _annotationBitmap?.Dispose();
        ResetMosaicCache();
        _annotationSession.Clear();
        _bitmap.Dispose();
    }

    public void SelectAnnotationTool(ScreenshotAnnotationTool tool)
    {
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
            if (_session.SelectionContains(physicalPoint) && e.ClickCount >= 2)
            {
                if (TryBeginTextAnnotationEditing(position))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (_annotationSession.ActiveTool == ScreenshotAnnotationTool.Select &&
                _session.Selection is { } editableSelection &&
                HitTestResizeHandle(position, ToLogicalRect(editableSelection)) is { } selectionHandle)
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

            if (_annotationSession.ActiveTool is ScreenshotAnnotationTool.Rectangle or
                    ScreenshotAnnotationTool.Arrow &&
                _session.SelectionContains(physicalPoint) &&
                _annotationSession.HitTest(ToSelectionLogicalPoint(position)) is { } existingIndex &&
                _annotationSession.Annotations[existingIndex] is
                    ScreenshotRectangleAnnotation or ScreenshotArrowAnnotation)
            {
                _annotationSession.SetTool(ScreenshotAnnotationTool.Select);
                _annotationSession.Select(existingIndex);
                _annotationBitmapDirty = true;
                _mosaicCacheDirty = true;
                UpdateSelectedPointerFeedback(position);
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
                    if (TryBeginTextAnnotationEditing(position))
                    {
                        e.Handled = true;
                        return;
                    }

                    var origin = ToSelectionLogicalPoint(position);
                    _annotationSession.BeginText(
                        origin,
                        Math.Max(1, textSelection.Width - origin.X));
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

                if (_annotationSession.HitTest(relativePoint) is { } hitIndex)
                {
                    _annotationSession.Select(hitIndex);
                    _annotationSession.BeginMoveSelected(relativePoint);
                    CapturePointer(e.Pointer);
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
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }

        position = _pixelInspector.UpdatePointer(position, Bounds.Size);
        _pendingSelectionStart = position;
        CapturePointer(e.Pointer);
        InvalidateVisual();
        e.Handled = true;
    }

    private bool TryBeginTextAnnotationEditing(Point position)
    {
        var annotationIndex = _annotationSession.HitTest(
            ToSelectionLogicalPoint(position));
        if (annotationIndex is not { } textIndex ||
            _annotationSession.Annotations[textIndex] is not ScreenshotTextAnnotation ||
            !_annotationSession.Select(textIndex) ||
            !_annotationSession.BeginTextEdit(textIndex))
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
        if (_annotationSession.IsTransforming)
        {
            _annotationSession.UpdateSelectedTransform(ToSelectionLogicalPoint(rawPosition));
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

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_annotationSession.IsTransforming)
        {
            _annotationSession.UpdateSelectedTransform(
                ToSelectionLogicalPoint(e.GetPosition(this)));
            var changed = _annotationSession.CompleteSelectedTransform();
            ReleasePointerCapture();
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
        _session.CompleteSelection();
        ReleasePointerCapture();
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

    private void ResetAnnotationsForNewSelection()
    {
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
        if (_session.Selection is not { } selection)
        {
            SetPointerCursor(_crosshairCursor, ScreenshotPointerFeedback.Crosshair);
            return;
        }

        var logicalSelection = ToLogicalRect(selection);
        if (HitTestResizeHandle(point, logicalSelection) is { } selectionHandle)
        {
            SetResizePointerFeedback(selectionHandle);
            return;
        }

        if (!_session.SelectionContains(ToPhysicalPoint(point)))
        {
            SetPointerCursor(_crosshairCursor, ScreenshotPointerFeedback.Crosshair);
            return;
        }

        var relativePoint = ToSelectionLogicalPoint(point);
        if (_annotationSession.SelectedAnnotation is { } selectedAnnotation &&
            HitTestAnnotationResizeHandle(selectedAnnotation, relativePoint) is { } annotationHandle)
        {
            SetResizePointerFeedback(selectedAnnotation, annotationHandle);
            return;
        }

        if (_annotationSession.HitTest(relativePoint) is not null)
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

    private void SetResizePointerFeedback(SelectionResizeHandle handle)
    {
        if (handle is SelectionResizeHandle.Left or SelectionResizeHandle.Right)
        {
            SetPointerCursor(_horizontalResizeCursor, ScreenshotPointerFeedback.ResizeHorizontal);
        }
        else if (handle is SelectionResizeHandle.Top or SelectionResizeHandle.Bottom)
        {
            SetPointerCursor(_verticalResizeCursor, ScreenshotPointerFeedback.ResizeVertical);
        }
        else
        {
            SetPointerCursor(
                handle is SelectionResizeHandle.TopLeft or SelectionResizeHandle.BottomRight
                    ? _northWestSouthEastResizeCursor
                    : _northEastSouthWestResizeCursor,
                ScreenshotPointerFeedback.ResizeDiagonal);
        }
    }

    private void SetResizePointerFeedback(
        IScreenshotAnnotation annotation,
        AnnotationResizeHandle handle)
    {
        if (annotation is ScreenshotArrowAnnotation)
        {
            SetPointerCursor(_arrowEndpointCursor, ScreenshotPointerFeedback.ResizeArrow);
        }
        else if (handle is AnnotationResizeHandle.Left or AnnotationResizeHandle.Right)
        {
            SetPointerCursor(_horizontalResizeCursor, ScreenshotPointerFeedback.ResizeHorizontal);
        }
        else if (handle is AnnotationResizeHandle.Top or AnnotationResizeHandle.Bottom)
        {
            SetPointerCursor(_verticalResizeCursor, ScreenshotPointerFeedback.ResizeVertical);
        }
        else
        {
            SetPointerCursor(
                handle is AnnotationResizeHandle.TopLeft or AnnotationResizeHandle.BottomRight
                    ? _northWestSouthEastResizeCursor
                    : _northEastSouthWestResizeCursor,
                ScreenshotPointerFeedback.ResizeDiagonal);
        }
    }

    private void SetPointerCursor(Cursor cursor, ScreenshotPointerFeedback feedback)
    {
        Cursor = cursor;
        PointerFeedback = feedback;
    }

    private static SelectionResizeHandle? HitTestResizeHandle(Point point, Rect selection)
    {
        var hitRadius = Math.Max(6, ScreenshotUiTheme.SelectionHandleSize / 2 + 3);
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

        return null;
    }

    private static AnnotationResizeHandle? HitTestAnnotationResizeHandle(
        IScreenshotAnnotation? annotation,
        LogicalPoint point)
    {
        const double radius = 8;
        if (annotation is ScreenshotRectangleAnnotation rectangle)
        {
            foreach (var controlPoint in GetRectangleControlPoints(rectangle))
            {
                if (Distance(controlPoint.Center, point) <= radius)
                {
                    return controlPoint.Handle;
                }
            }

            return null;
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

    private void DrawSelectedAnnotationControls(DrawingContext context, Rect selection)
    {
        if (_annotationSession.SelectedAnnotation is not { } annotation)
        {
            return;
        }

        var controlPen = new Pen(ScreenshotUiTheme.AccentBrush, 1);
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
                var textBounds = ScreenshotAnnotationRenderer.MeasureText(text);
                context.DrawRectangle(
                    brush: null,
                    controlPen,
                    new Rect(
                        selection.X + textBounds.X,
                        selection.Y + textBounds.Y,
                        textBounds.Width,
                        textBounds.Height));
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
        const double size = 8;
        var center = ToCanvasPoint(selection, point);
        context.DrawRectangle(
            Brushes.White,
            new Pen(ScreenshotUiTheme.AccentBrush, 1),
            new Rect(center.X - (size / 2), center.Y - (size / 2), size, size),
            1,
            1);
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
        context.DrawRectangle(ScreenshotUiTheme.AccentBrush, pen: null, handle, 1, 1);
    }

    private Rect ToLogicalRect(PhysicalRect rect) =>
        new(
            rect.X / _frame.ScaleX,
            rect.Y / _frame.ScaleY,
            rect.Width / _frame.ScaleX,
            rect.Height / _frame.ScaleY);
}
