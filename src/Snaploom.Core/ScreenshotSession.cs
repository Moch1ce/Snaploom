namespace Snaploom.Core;

public enum ScreenshotSessionState
{
    Ready,
    Selecting,
    MovingSelection,
    Selected,
    Saving,
}

public sealed class ScreenshotSession
{
    public const int MinimumSelectionSize = 8;

    private readonly PhysicalSize _frameSize;
    private PhysicalPoint _selectionStart;
    private PhysicalPoint _selectionEnd;
    private PhysicalPoint _moveStart;
    private PhysicalRect _selectionBeforeMove;

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

    private PhysicalPoint Clamp(PhysicalPoint point) =>
        new(
            Math.Clamp(point.X, 0, _frameSize.Width),
            Math.Clamp(point.Y, 0, _frameSize.Height));
}
