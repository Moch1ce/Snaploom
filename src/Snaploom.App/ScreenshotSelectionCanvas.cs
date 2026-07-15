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
    private readonly WriteableBitmap _bitmap;
    private readonly ScreenshotPixelInspector _pixelInspector;
    private bool _disposed;

    public ScreenshotSelectionCanvas(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frame = frame;
        _session = new ScreenshotSession(frame.PhysicalSize);
        _bitmap = CreateBitmap(frame);
        _pixelInspector = new ScreenshotPixelInspector(frame, _bitmap);
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        Focusable = true;
    }

    public event EventHandler? SelectionChanged;

    public ScreenshotSession Session => _session;

    internal Rect? LogicalSelection =>
        _session.Selection is { } selection ? ToLogicalRect(selection) : null;

    public CapturedColor? SampledColor =>
        _session.State is ScreenshotSessionState.Ready or ScreenshotSessionState.Selecting
            ? _pixelInspector.SampledColor
            : null;

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
            _pixelInspector.Render(context, Bounds.Size);
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
        Cursor?.Dispose();
        _bitmap.Dispose();
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
        var position = _pixelInspector.UpdatePointer(e.GetPosition(this), Bounds.Size);
        _session.BeginSelection(ToPhysicalPoint(position));
        e.Pointer.Capture(this);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_session.State is ScreenshotSessionState.Selected or ScreenshotSessionState.Saving)
        {
            return;
        }

        var position = _pixelInspector.UpdatePointer(e.GetPosition(this), Bounds.Size);
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
        if (_session.State != ScreenshotSessionState.Selecting)
        {
            return;
        }

        var position = _pixelInspector.UpdatePointer(e.GetPosition(this), Bounds.Size);
        _session.UpdateSelection(ToPhysicalPoint(position));
        _session.CompleteSelection();
        e.Pointer.Capture(control: null);
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

    private PhysicalPoint ToPhysicalPoint(Point point) =>
        new(
            checked((int)Math.Round(point.X * _frame.ScaleX)),
            checked((int)Math.Round(point.Y * _frame.ScaleY)));

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
