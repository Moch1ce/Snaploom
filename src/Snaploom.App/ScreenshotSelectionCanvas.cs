using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Snaploom.Core;

namespace Snaploom.App;

public sealed class ScreenshotSelectionCanvas : Control, IDisposable
{
    private static readonly Pen SelectionPen = new(ScreenshotUiTheme.AccentBrush, 2);

    private readonly CapturedFrame _frame;
    private readonly ScreenshotSession _session;
    private readonly IReadOnlyList<ScreenshotWindowCandidate> _windowCandidates;
    private readonly WriteableBitmap _bitmap;
    private readonly ScreenshotPixelInspector _pixelInspector;
    private readonly Cursor _crosshairCursor = new(StandardCursorType.Cross);
    private readonly Cursor _moveCursor = new(StandardCursorType.SizeAll);
    private Point? _pendingSelectionStart;
    private IPointer? _capturedPointer;
    private ScreenshotSnapTarget? _hoveredSnapTarget;
    private bool _disposed;

    public ScreenshotSelectionCanvas(
        CapturedFrame frame,
        IEnumerable<ScreenshotWindowCandidate>? windowCandidates = null)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frame = frame;
        _session = new ScreenshotSession(frame.PhysicalSize);
        _windowCandidates = ScreenshotWindowSelector.GetEligibleWindows(
            windowCandidates ?? Array.Empty<ScreenshotWindowCandidate>(),
            frame.PhysicalSize);
        _bitmap = CreateBitmap(frame);
        _pixelInspector = new ScreenshotPixelInspector(frame, _bitmap);
        ClipToBounds = true;
        Cursor = _crosshairCursor;
        Focusable = true;
    }

    public event EventHandler? SelectionChanged;

    public event EventHandler? SelectionDoubleClicked;

    public ScreenshotSession Session => _session;

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
            context.DrawRectangle(brush: null, SelectionPen, selectedDestination);
            DrawSelectionHandles(context, selectedDestination);
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
        _bitmap.Dispose();
    }

    public ScreenshotCancelResult CancelCurrentLayer()
    {
        ScreenshotCancelResult result;
        if (_pendingSelectionStart is not null)
        {
            _pendingSelectionStart = null;
            result = ScreenshotCancelResult.ActionCanceled;
        }
        else
        {
            result = _session.Cancel();
        }

        if (result != ScreenshotCancelResult.ExitRequested)
        {
            ReleasePointerCapture();
            Cursor = _session.State == ScreenshotSessionState.Selected
                ? _moveCursor
                : _crosshairCursor;
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
                SelectionDoubleClicked?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }

            if (_session.Selection is { } selection &&
                HitTestResizeHandle(position, ToLogicalRect(selection)) is { } handle)
            {
                _session.BeginResizeSelection(handle);
                CapturePointer(e.Pointer);
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (_session.SelectionContains(physicalPoint))
            {
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

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_session.State == ScreenshotSessionState.Saving)
        {
            return;
        }

        var rawPosition = e.GetPosition(this);
        if (_pendingSelectionStart is { } start)
        {
            var clampedPosition = _pixelInspector.UpdatePointer(rawPosition, Bounds.Size);
            if (ScreenshotPointerGesture.HasExceededDragThreshold(
                    new LogicalPoint(start.X, start.Y),
                    new LogicalPoint(clampedPosition.X, clampedPosition.Y)))
            {
                _pendingSelectionStart = null;
                _hoveredSnapTarget = null;
                _session.BeginSelection(ToPhysicalPoint(start));
                _session.UpdateSelection(ToPhysicalPoint(clampedPosition));
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_session.State == ScreenshotSessionState.Selected)
        {
            Cursor = _session.SelectionContains(ToPhysicalPoint(rawPosition))
                ? _moveCursor
                : _crosshairCursor;
            return;
        }

        if (_session.State == ScreenshotSessionState.ResizingSelection)
        {
            _session.UpdateResizeSelection(ToPhysicalPoint(rawPosition));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_session.State == ScreenshotSessionState.MovingSelection)
        {
            _session.UpdateMoveSelection(ToPhysicalPoint(rawPosition));
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_session.State == ScreenshotSessionState.Ready)
        {
            var hoverPosition = _pixelInspector.UpdatePointer(rawPosition, Bounds.Size);
            _hoveredSnapTarget = ScreenshotWindowSelector.HitTest(
                _windowCandidates,
                ToPhysicalPoint(hoverPosition),
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
        if (_pendingSelectionStart is not null)
        {
            _pendingSelectionStart = null;
            var snapPosition = _pixelInspector.UpdatePointer(e.GetPosition(this), Bounds.Size);
            var snapTarget = ScreenshotWindowSelector.HitTest(
                _windowCandidates,
                ToPhysicalPoint(snapPosition),
                _frame.PhysicalSize);
            _session.Select(snapTarget.Bounds);
            _hoveredSnapTarget = null;
            ReleasePointerCapture();
            Cursor = _moveCursor;
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
            Cursor = _moveCursor;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_session.State == ScreenshotSessionState.ResizingSelection)
        {
            _session.UpdateResizeSelection(ToPhysicalPoint(e.GetPosition(this)));
            _session.CompleteResizeSelection();
            ReleasePointerCapture();
            Cursor = _moveCursor;
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
