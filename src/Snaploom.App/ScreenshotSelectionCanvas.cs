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
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.FromArgb(115, 0, 0, 0));
    private static readonly Pen SelectionPen = new(Brushes.White, 1);

    private readonly CapturedFrame _frame;
    private readonly ScreenshotSession _session;
    private readonly WriteableBitmap _bitmap;
    private bool _disposed;

    public ScreenshotSelectionCanvas(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frame = frame;
        _session = new ScreenshotSession(frame.PhysicalSize);
        _bitmap = CreateBitmap(frame);
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        Focusable = true;
    }

    public event EventHandler? SelectionChanged;

    public ScreenshotSession Session => _session;

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
        context.DrawRectangle(DimBrush, pen: null, destination);

        if (_session.Selection is not { } selection)
        {
            return;
        }

        var source = new Rect(selection.X, selection.Y, selection.Width, selection.Height);
        var selectedDestination = ToLogicalRect(selection);
        context.DrawImage(_bitmap, source, selectedDestination);
        context.DrawRectangle(brush: null, SelectionPen, selectedDestination);
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
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        _session.BeginSelection(ToPhysicalPoint(e.GetPosition(this)));
        e.Pointer.Capture(this);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_session.State != ScreenshotSessionState.Selecting)
        {
            return;
        }

        _session.UpdateSelection(ToPhysicalPoint(e.GetPosition(this)));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_session.State != ScreenshotSessionState.Selecting)
        {
            return;
        }

        _session.UpdateSelection(ToPhysicalPoint(e.GetPosition(this)));
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

    private Rect ToLogicalRect(PhysicalRect rect) =>
        new(
            rect.X / _frame.ScaleX,
            rect.Y / _frame.ScaleY,
            rect.Width / _frame.ScaleX,
            rect.Height / _frame.ScaleY);
}
