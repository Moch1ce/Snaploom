namespace Snaploom.Core;

public enum ScreenshotSessionState
{
    Ready,
    Selecting,
    Selected,
    Saving,
}

public sealed class ScreenshotSession
{
    public const int MinimumSelectionSize = 8;

    private readonly PhysicalSize _frameSize;
    private PhysicalPoint _selectionStart;
    private PhysicalPoint _selectionEnd;

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
