using Snaploom.Core;

namespace Snaploom.Core.Tests;

public sealed class ScreenshotSessionTests
{
    [Fact]
    public void SelectionSmallerThanEightPhysicalPixelsIsRejected()
    {
        var session = new ScreenshotSession(new PhysicalSize(100, 80));

        session.BeginSelection(new PhysicalPoint(10, 10));
        session.UpdateSelection(new PhysicalPoint(17, 40));
        var accepted = session.CompleteSelection();

        Assert.False(accepted);
        Assert.Equal(ScreenshotSessionState.Ready, session.State);
        Assert.Null(session.Selection);
    }

    [Fact]
    public void CancelingSavePreservesTheCurrentSelection()
    {
        var session = new ScreenshotSession(new PhysicalSize(100, 80));
        session.BeginSelection(new PhysicalPoint(30, 30));
        session.UpdateSelection(new PhysicalPoint(10, 10));
        Assert.True(session.CompleteSelection());

        session.BeginSave();
        session.CancelSave();

        Assert.Equal(ScreenshotSessionState.Selected, session.State);
        Assert.Equal(new PhysicalRect(10, 10, 20, 20), session.Selection);
    }

    [Fact]
    public void ACompletedSelectionCanBeReplacedByDraggingAgain()
    {
        var session = new ScreenshotSession(new PhysicalSize(100, 80));
        session.BeginSelection(new PhysicalPoint(10, 10));
        session.UpdateSelection(new PhysicalPoint(30, 30));
        Assert.True(session.CompleteSelection());

        session.BeginSelection(new PhysicalPoint(40, 50));
        session.UpdateSelection(new PhysicalPoint(70, 75));
        Assert.True(session.CompleteSelection());

        Assert.Equal(ScreenshotSessionState.Selected, session.State);
        Assert.Equal(new PhysicalRect(40, 50, 30, 25), session.Selection);
    }

    [Fact]
    public void ACompletedSelectionCanBeMovedByDraggingInsideIt()
    {
        var session = new ScreenshotSession(new PhysicalSize(100, 80));
        session.BeginSelection(new PhysicalPoint(10, 10));
        session.UpdateSelection(new PhysicalPoint(40, 35));
        Assert.True(session.CompleteSelection());

        session.BeginMoveSelection(new PhysicalPoint(20, 20));
        session.UpdateMoveSelection(new PhysicalPoint(45, 50));
        session.CompleteMoveSelection();

        Assert.Equal(ScreenshotSessionState.Selected, session.State);
        Assert.Equal(new PhysicalRect(35, 40, 30, 25), session.Selection);
    }

    [Fact]
    public void MovingASelectionKeepsItInsideTheCapturedFrame()
    {
        var session = new ScreenshotSession(new PhysicalSize(100, 80));
        session.BeginSelection(new PhysicalPoint(10, 10));
        session.UpdateSelection(new PhysicalPoint(40, 35));
        Assert.True(session.CompleteSelection());

        session.BeginMoveSelection(new PhysicalPoint(20, 20));
        session.UpdateMoveSelection(new PhysicalPoint(200, 200));
        session.CompleteMoveSelection();

        Assert.Equal(new PhysicalRect(70, 55, 30, 25), session.Selection);
    }
}
