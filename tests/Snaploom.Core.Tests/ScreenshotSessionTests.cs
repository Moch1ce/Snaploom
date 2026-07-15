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

    [Fact]
    public void ACompletedSelectionCanBeResizedFromTheBottomRightHandle()
    {
        var session = new ScreenshotSession(new PhysicalSize(100, 80));
        session.BeginSelection(new PhysicalPoint(10, 10));
        session.UpdateSelection(new PhysicalPoint(40, 30));
        Assert.True(session.CompleteSelection());

        session.BeginResizeSelection(SelectionResizeHandle.BottomRight);
        session.UpdateResizeSelection(new PhysicalPoint(60, 50));
        session.CompleteResizeSelection();

        Assert.Equal(ScreenshotSessionState.Selected, session.State);
        Assert.Equal(new PhysicalRect(10, 10, 50, 40), session.Selection);
    }

    [Theory]
    [InlineData(SelectionResizeHandle.TopLeft, 5, 6, 5, 6, 35, 24)]
    [InlineData(SelectionResizeHandle.Top, 25, 6, 10, 6, 30, 24)]
    [InlineData(SelectionResizeHandle.TopRight, 50, 6, 10, 6, 40, 24)]
    [InlineData(SelectionResizeHandle.Right, 50, 20, 10, 10, 40, 20)]
    [InlineData(SelectionResizeHandle.Bottom, 25, 40, 10, 10, 30, 30)]
    [InlineData(SelectionResizeHandle.BottomLeft, 5, 40, 5, 10, 35, 30)]
    [InlineData(SelectionResizeHandle.Left, 5, 20, 5, 10, 35, 20)]
    [InlineData(SelectionResizeHandle.TopLeft, 39, 29, 32, 22, 8, 8)]
    public void EverySelectionHandleResizesTheExpectedEdges(
        SelectionResizeHandle handle,
        int pointerX,
        int pointerY,
        int expectedX,
        int expectedY,
        int expectedWidth,
        int expectedHeight)
    {
        var session = new ScreenshotSession(new PhysicalSize(100, 80));
        session.BeginSelection(new PhysicalPoint(10, 10));
        session.UpdateSelection(new PhysicalPoint(40, 30));
        Assert.True(session.CompleteSelection());

        session.BeginResizeSelection(handle);
        session.UpdateResizeSelection(new PhysicalPoint(pointerX, pointerY));
        session.CompleteResizeSelection();

        Assert.Equal(
            new PhysicalRect(expectedX, expectedY, expectedWidth, expectedHeight),
            session.Selection);
    }

    [Fact]
    public void EscapeClearsACompletedSelectionBeforeRequestingExit()
    {
        var session = new ScreenshotSession(new PhysicalSize(100, 80));
        session.BeginSelection(new PhysicalPoint(10, 10));
        session.UpdateSelection(new PhysicalPoint(40, 30));
        Assert.True(session.CompleteSelection());

        Assert.Equal(ScreenshotCancelResult.SelectionCleared, session.Cancel());
        Assert.Equal(ScreenshotSessionState.Ready, session.State);
        Assert.Null(session.Selection);

        Assert.Equal(ScreenshotCancelResult.ExitRequested, session.Cancel());
    }

    [Fact]
    public void EscapeRestoresTheSelectionWhenMovingIt()
    {
        var session = CreateSelectedSession();
        session.BeginMoveSelection(new PhysicalPoint(20, 20));
        session.UpdateMoveSelection(new PhysicalPoint(50, 50));

        Assert.Equal(ScreenshotCancelResult.ActionCanceled, session.Cancel());
        Assert.Equal(ScreenshotSessionState.Selected, session.State);
        Assert.Equal(new PhysicalRect(10, 10, 30, 20), session.Selection);
    }

    [Fact]
    public void EscapeRestoresTheSelectionWhenResizingIt()
    {
        var session = CreateSelectedSession();
        session.BeginResizeSelection(SelectionResizeHandle.BottomRight);
        session.UpdateResizeSelection(new PhysicalPoint(70, 60));

        Assert.Equal(ScreenshotCancelResult.ActionCanceled, session.Cancel());
        Assert.Equal(ScreenshotSessionState.Selected, session.State);
        Assert.Equal(new PhysicalRect(10, 10, 30, 20), session.Selection);
    }

    [Fact]
    public void EscapeCancelsAnIncompleteSelection()
    {
        var session = new ScreenshotSession(new PhysicalSize(100, 80));
        session.BeginSelection(new PhysicalPoint(10, 10));
        session.UpdateSelection(new PhysicalPoint(40, 30));

        Assert.Equal(ScreenshotCancelResult.ActionCanceled, session.Cancel());
        Assert.Equal(ScreenshotSessionState.Ready, session.State);
        Assert.Null(session.Selection);
    }

    private static ScreenshotSession CreateSelectedSession()
    {
        var session = new ScreenshotSession(new PhysicalSize(100, 80));
        session.BeginSelection(new PhysicalPoint(10, 10));
        session.UpdateSelection(new PhysicalPoint(40, 30));
        Assert.True(session.CompleteSelection());
        return session;
    }
}
