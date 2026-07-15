using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Snaploom.App;

internal enum ScreenshotToolbarIconKind
{
    Rectangle,
    Arrow,
    Text,
    Mosaic,
    Undo,
    Save,
    Cancel,
    Confirm,
}

internal sealed class ScreenshotToolbarIcon : Control
{
    private readonly ScreenshotToolbarIconKind _kind;
    private readonly IBrush _brush;

    internal ScreenshotToolbarIcon(ScreenshotToolbarIconKind kind, IBrush brush)
    {
        ArgumentNullException.ThrowIfNull(brush);
        _kind = kind;
        _brush = brush;
        Width = ScreenshotUiTheme.IconSize;
        Height = ScreenshotUiTheme.IconSize;
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var pen = new Pen(_brush, 1.5);

        switch (_kind)
        {
            case ScreenshotToolbarIconKind.Rectangle:
                context.DrawRectangle(brush: null, pen, new Rect(2.5, 2.5, 15, 15));
                break;

            case ScreenshotToolbarIconKind.Arrow:
                context.DrawLine(pen, new Point(3, 17), new Point(17, 3));
                context.DrawLine(pen, new Point(10, 3), new Point(17, 3));
                context.DrawLine(pen, new Point(17, 3), new Point(17, 10));
                break;

            case ScreenshotToolbarIconKind.Text:
                context.DrawRectangle(brush: null, pen, new Rect(2.5, 2.5, 15, 15));
                context.DrawLine(pen, new Point(6, 6), new Point(14, 6));
                context.DrawLine(pen, new Point(10, 6), new Point(10, 15));
                break;

            case ScreenshotToolbarIconKind.Mosaic:
                DrawMosaic(context);
                break;

            case ScreenshotToolbarIconKind.Undo:
                context.DrawLine(pen, new Point(5, 4), new Point(2, 8));
                context.DrawLine(pen, new Point(2, 8), new Point(6, 11));
                context.DrawLine(pen, new Point(2, 8), new Point(11, 8));
                context.DrawLine(pen, new Point(11, 8), new Point(16, 12));
                context.DrawLine(pen, new Point(16, 12), new Point(14, 17));
                break;

            case ScreenshotToolbarIconKind.Save:
                context.DrawLine(pen, new Point(10, 2), new Point(10, 13));
                context.DrawLine(pen, new Point(6, 9), new Point(10, 13));
                context.DrawLine(pen, new Point(10, 13), new Point(14, 9));
                context.DrawLine(pen, new Point(3, 13), new Point(3, 18));
                context.DrawLine(pen, new Point(3, 18), new Point(17, 18));
                context.DrawLine(pen, new Point(17, 18), new Point(17, 13));
                break;

            case ScreenshotToolbarIconKind.Cancel:
                context.DrawLine(pen, new Point(3, 3), new Point(17, 17));
                context.DrawLine(pen, new Point(17, 3), new Point(3, 17));
                break;

            case ScreenshotToolbarIconKind.Confirm:
                context.DrawLine(pen, new Point(2, 10), new Point(8, 16));
                context.DrawLine(pen, new Point(8, 16), new Point(18, 4));
                break;

            default:
                throw new InvalidOperationException($"Unsupported toolbar icon: {_kind}.");
        }
    }

    private void DrawMosaic(DrawingContext context)
    {
        const double cell = 4;
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                if ((row + column) % 2 != 0)
                {
                    continue;
                }

                context.DrawRectangle(
                    _brush,
                    pen: null,
                    new Rect(3 + (column * 5.5), 3 + (row * 5.5), cell, cell));
            }
        }
    }
}
