namespace Snaploom.Core;

public enum ScreenshotSessionState
{
    Ready,
    Selecting,
    MovingSelection,
    ResizingSelection,
    Selected,
    Saving,
}

public enum SelectionResizeHandle
{
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

public enum ScreenshotCancelResult
{
    ActionCanceled,
    SelectionCleared,
    ExitRequested,
}

public sealed class ScreenshotSession
{
    public const int MinimumSelectionSize = 8;

    private readonly PhysicalSize _frameSize;
    private PhysicalPoint _selectionStart;
    private PhysicalPoint _selectionEnd;
    private PhysicalPoint _moveStart;
    private PhysicalRect _selectionBeforeMove;
    private PhysicalRect _selectionBeforeResize;
    private SelectionResizeHandle _resizeHandle;

    public ScreenshotSession(PhysicalSize frameSize)
    {
        _frameSize = frameSize;
    }

    public ScreenshotSessionState State { get; private set; } = ScreenshotSessionState.Ready;

    public PhysicalRect? Selection { get; private set; }

    public void BeginSelection(PhysicalPoint point)
    {
        _selectionStart = Clamp(point);
        _selectionEnd = _selectionStart;
        Selection = null;
        State = ScreenshotSessionState.Selecting;
    }

    public void UpdateSelection(PhysicalPoint point)
    {
        if (State != ScreenshotSessionState.Selecting)
        {
            throw new InvalidOperationException("A selection has not been started.");
        }

        _selectionEnd = Clamp(point);
        Selection = PhysicalRect.FromPoints(_selectionStart, _selectionEnd);
    }

    public bool CompleteSelection()
    {
        if (State != ScreenshotSessionState.Selecting)
        {
            throw new InvalidOperationException("A selection has not been started.");
        }

        var selection = PhysicalRect.FromPoints(_selectionStart, _selectionEnd);
        if (selection.Width < MinimumSelectionSize || selection.Height < MinimumSelectionSize)
        {
            Selection = null;
            State = ScreenshotSessionState.Ready;
            return false;
        }

        Selection = selection;
        State = ScreenshotSessionState.Selected;
        return true;
    }

    public bool SelectionContains(PhysicalPoint point) =>
        Selection is { } selection &&
        point.X >= selection.X &&
        point.X <= selection.X + selection.Width &&
        point.Y >= selection.Y &&
        point.Y <= selection.Y + selection.Height;

    public void BeginMoveSelection(PhysicalPoint point)
    {
        if (State != ScreenshotSessionState.Selected ||
            Selection is not { } selection ||
            !SelectionContains(point))
        {
            throw new InvalidOperationException("A completed selection must be dragged from inside its bounds.");
        }

        _moveStart = Clamp(point);
        _selectionBeforeMove = selection;
        State = ScreenshotSessionState.MovingSelection;
    }

    public void UpdateMoveSelection(PhysicalPoint point)
    {
        if (State != ScreenshotSessionState.MovingSelection)
        {
            throw new InvalidOperationException("The selection is not being moved.");
        }

        var current = Clamp(point);
        var x = Math.Clamp(
            _selectionBeforeMove.X + current.X - _moveStart.X,
            0,
            _frameSize.Width - _selectionBeforeMove.Width);
        var y = Math.Clamp(
            _selectionBeforeMove.Y + current.Y - _moveStart.Y,
            0,
            _frameSize.Height - _selectionBeforeMove.Height);
        Selection = _selectionBeforeMove with { X = x, Y = y };
    }

    public void CompleteMoveSelection()
    {
        if (State != ScreenshotSessionState.MovingSelection)
        {
            throw new InvalidOperationException("The selection is not being moved.");
        }

        State = ScreenshotSessionState.Selected;
    }

    public void BeginResizeSelection(SelectionResizeHandle handle)
    {
        if (State != ScreenshotSessionState.Selected || Selection is not { } selection)
        {
            throw new InvalidOperationException("A completed selection is required before resizing.");
        }

        _selectionBeforeResize = selection;
        _resizeHandle = handle;
        State = ScreenshotSessionState.ResizingSelection;
    }

    public void UpdateResizeSelection(PhysicalPoint point)
    {
        if (State != ScreenshotSessionState.ResizingSelection)
        {
            throw new InvalidOperationException("The selection is not being resized.");
        }

        var current = Clamp(point);
        var left = _selectionBeforeResize.X;
        var top = _selectionBeforeResize.Y;
        var right = left + _selectionBeforeResize.Width;
        var bottom = top + _selectionBeforeResize.Height;

        if (_resizeHandle is SelectionResizeHandle.TopLeft or
            SelectionResizeHandle.BottomLeft or
            SelectionResizeHandle.Left)
        {
            left = Math.Clamp(current.X, 0, right - MinimumSelectionSize);
        }

        if (_resizeHandle is SelectionResizeHandle.TopRight or
            SelectionResizeHandle.Right or
            SelectionResizeHandle.BottomRight)
        {
            right = Math.Clamp(current.X, left + MinimumSelectionSize, _frameSize.Width);
        }

        if (_resizeHandle is SelectionResizeHandle.TopLeft or
            SelectionResizeHandle.Top or
            SelectionResizeHandle.TopRight)
        {
            top = Math.Clamp(current.Y, 0, bottom - MinimumSelectionSize);
        }

        if (_resizeHandle is SelectionResizeHandle.BottomLeft or
            SelectionResizeHandle.Bottom or
            SelectionResizeHandle.BottomRight)
        {
            bottom = Math.Clamp(current.Y, top + MinimumSelectionSize, _frameSize.Height);
        }

        Selection = new PhysicalRect(left, top, right - left, bottom - top);
    }

    public void CompleteResizeSelection()
    {
        if (State != ScreenshotSessionState.ResizingSelection)
        {
            throw new InvalidOperationException("The selection is not being resized.");
        }

        State = ScreenshotSessionState.Selected;
    }

    public void BeginSave()
    {
        if (State != ScreenshotSessionState.Selected)
        {
            throw new InvalidOperationException("A completed selection is required before saving.");
        }

        State = ScreenshotSessionState.Saving;
    }

    public void CancelSave()
    {
        if (State != ScreenshotSessionState.Saving)
        {
            throw new InvalidOperationException("The screenshot session is not saving.");
        }

        State = ScreenshotSessionState.Selected;
    }

    public ScreenshotCancelResult Cancel()
    {
        switch (State)
        {
            case ScreenshotSessionState.Selecting:
                Selection = null;
                State = ScreenshotSessionState.Ready;
                return ScreenshotCancelResult.ActionCanceled;

            case ScreenshotSessionState.MovingSelection:
                Selection = _selectionBeforeMove;
                State = ScreenshotSessionState.Selected;
                return ScreenshotCancelResult.ActionCanceled;

            case ScreenshotSessionState.ResizingSelection:
                Selection = _selectionBeforeResize;
                State = ScreenshotSessionState.Selected;
                return ScreenshotCancelResult.ActionCanceled;

            case ScreenshotSessionState.Saving:
                State = ScreenshotSessionState.Selected;
                return ScreenshotCancelResult.ActionCanceled;

            case ScreenshotSessionState.Selected:
                Selection = null;
                State = ScreenshotSessionState.Ready;
                return ScreenshotCancelResult.SelectionCleared;

            case ScreenshotSessionState.Ready:
                return ScreenshotCancelResult.ExitRequested;

            default:
                throw new InvalidOperationException("The screenshot session is in an unknown state.");
        }
    }

    private PhysicalPoint Clamp(PhysicalPoint point) =>
        new(
            Math.Clamp(point.X, 0, _frameSize.Width),
            Math.Clamp(point.Y, 0, _frameSize.Height));
}
