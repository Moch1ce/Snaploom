using Snaploom.Core;

namespace Snaploom.Core.Tests;

public sealed class ScreenshotAnnotationSessionTests
{
    [Fact]
    public void TextStyleDefaultsToRedAndOnlySupportsTheThreeLogicalSizes()
    {
        Assert.Equal(ScreenshotAnnotationColor.Red, ScreenshotTextStyle.Default.Color);
        Assert.Equal(24, ScreenshotTextStyle.Default.FontSize);

        Assert.Equal(
            16,
            new ScreenshotTextStyle(ScreenshotAnnotationColor.Blue, 16).FontSize);
        Assert.Equal(
            32,
            new ScreenshotTextStyle(ScreenshotAnnotationColor.Blue, 32).FontSize);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ScreenshotTextStyle(ScreenshotAnnotationColor.Blue, 20));
    }

    [Fact]
    public void DefaultStyleUsesRedAndOnlySupportsTheThreeLogicalWidths()
    {
        Assert.Equal(
            ScreenshotAnnotationColor.Red,
            ScreenshotAnnotationStyle.Default.Color);
        Assert.Equal(4, ScreenshotAnnotationStyle.Default.LineWidth);

        Assert.Equal(
            new AnnotationRgbColor(255, 77, 79),
            ScreenshotAnnotationPalette.GetColor(ScreenshotAnnotationColor.Red));
        Assert.Equal(
            new AnnotationRgbColor(250, 219, 20),
            ScreenshotAnnotationPalette.GetColor(ScreenshotAnnotationColor.Yellow));
        Assert.Equal(
            new AnnotationRgbColor(7, 201, 119),
            ScreenshotAnnotationPalette.GetColor(ScreenshotAnnotationColor.Green));
        Assert.Equal(
            new AnnotationRgbColor(22, 119, 255),
            ScreenshotAnnotationPalette.GetColor(ScreenshotAnnotationColor.Blue));
        Assert.Equal(
            new AnnotationRgbColor(32, 33, 36),
            ScreenshotAnnotationPalette.GetColor(ScreenshotAnnotationColor.Black));
        Assert.Equal(
            new AnnotationRgbColor(255, 255, 255),
            ScreenshotAnnotationPalette.GetColor(ScreenshotAnnotationColor.White));

        Assert.Equal(2, new ScreenshotAnnotationStyle(ScreenshotAnnotationColor.Blue, 2).LineWidth);
        Assert.Equal(8, new ScreenshotAnnotationStyle(ScreenshotAnnotationColor.Blue, 8).LineWidth);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ScreenshotAnnotationStyle(ScreenshotAnnotationColor.Blue, 3));
    }

    [Fact]
    public void PreviewBecomesTheTopmostNonDestructiveAnnotation()
    {
        var session = new ScreenshotAnnotationSession();
        session.SetTool(ScreenshotAnnotationTool.Rectangle);
        session.Begin(new LogicalPoint(2, 3));
        session.Update(new LogicalPoint(20, 30));

        var preview = Assert.IsType<ScreenshotRectangleAnnotation>(session.Preview);
        Assert.Equal(new LogicalPoint(2, 3), preview.Start);
        Assert.Equal(new LogicalPoint(20, 30), preview.End);
        Assert.Empty(session.Annotations);

        Assert.True(session.Complete());
        Assert.Same(preview, Assert.Single(session.Annotations));

        session.SetTool(ScreenshotAnnotationTool.Arrow);
        session.SetStyle(new ScreenshotAnnotationStyle(ScreenshotAnnotationColor.Blue, 8));
        session.Begin(new LogicalPoint(4, 5));
        session.Update(new LogicalPoint(40, 50));
        Assert.True(session.Complete());

        Assert.IsType<ScreenshotRectangleAnnotation>(session.Annotations[0]);
        var arrow = Assert.IsType<ScreenshotArrowAnnotation>(session.Annotations[1]);
        Assert.Equal(ScreenshotAnnotationColor.Blue, arrow.Style.Color);
        Assert.Equal(8, arrow.Style.LineWidth);
    }

    [Fact]
    public void SwitchingBackToSelectionCancelsOnlyTheCurrentPreview()
    {
        var session = new ScreenshotAnnotationSession();
        session.SetTool(ScreenshotAnnotationTool.Rectangle);
        session.Begin(new LogicalPoint(2, 3));
        session.Update(new LogicalPoint(20, 30));

        session.SetTool(ScreenshotAnnotationTool.Select);

        Assert.Null(session.Preview);
        Assert.Empty(session.Annotations);
        Assert.Equal(ScreenshotAnnotationTool.Select, session.ActiveTool);
    }

    [Fact]
    public void TextCompositionDoesNotCreateAnAnnotationUntilCommitted()
    {
        var session = new ScreenshotAnnotationSession();
        session.SetTool(ScreenshotAnnotationTool.Text);
        session.SetTextStyle(new ScreenshotTextStyle(ScreenshotAnnotationColor.Green, 32));

        session.BeginText(new LogicalPoint(8, 12), maxWidth: 180);
        session.UpdateText("zhong", isComposing: true);

        Assert.Empty(session.Annotations);
        Assert.False(session.CommitText());
        Assert.Empty(session.Annotations);

        session.UpdateText("中文 English 123 !?\n第二行", isComposing: false);

        Assert.True(session.CommitText());
        var annotation = Assert.IsType<ScreenshotTextAnnotation>(
            Assert.Single(session.Annotations));
        Assert.Equal(new LogicalPoint(8, 12), annotation.Origin);
        Assert.Equal(180, annotation.MaxWidth);
        Assert.Equal("中文 English 123 !?\n第二行", annotation.Text);
        Assert.Equal(new ScreenshotTextStyle(ScreenshotAnnotationColor.Green, 32), annotation.Style);
    }

    [Fact]
    public void ExistingTextCanBeEditedOrCanceledWithoutCreatingExtraObjects()
    {
        var session = new ScreenshotAnnotationSession();
        session.SetTool(ScreenshotAnnotationTool.Text);
        session.BeginText(new LogicalPoint(4, 6), maxWidth: 100);
        session.UpdateText("原文", isComposing: false);
        Assert.True(session.CommitText());

        Assert.True(session.BeginTextEdit(0));
        session.UpdateText("修改后", isComposing: false);
        Assert.True(session.CommitText());
        Assert.Equal("修改后", Assert.IsType<ScreenshotTextAnnotation>(session.Annotations[0]).Text);
        Assert.Single(session.Annotations);

        Assert.True(session.BeginTextEdit(0));
        session.UpdateText("不会保存", isComposing: false);
        Assert.True(session.CancelTextEdit());
        Assert.Equal("修改后", Assert.IsType<ScreenshotTextAnnotation>(session.Annotations[0]).Text);
        Assert.Single(session.Annotations);
    }

    [Fact]
    public void TextBeingEditedIsHiddenFromTheCommittedRenderLayer()
    {
        var session = new ScreenshotAnnotationSession();
        session.SetTool(ScreenshotAnnotationTool.Text);
        session.BeginText(new LogicalPoint(4, 6), maxWidth: 100);
        session.UpdateText("first", isComposing: false);
        Assert.True(session.CommitText());
        session.BeginText(new LogicalPoint(8, 10), maxWidth: 90);
        session.UpdateText("second", isComposing: false);
        Assert.True(session.CommitText());

        Assert.True(session.BeginTextEdit(0));

        var visible = session.EnumerateForRendering().ToArray();
        var text = Assert.IsType<ScreenshotTextAnnotation>(Assert.Single(visible));
        Assert.Equal("second", text.Text);
    }

    [Fact]
    public void MosaicStyleKeepsBrushSizeAndPixelIntensity()
    {
        Assert.Equal(32, ScreenshotMosaicStyle.Default.BrushSize);
        Assert.Equal(12, ScreenshotMosaicStyle.Default.PixelSize);
        Assert.Equal(16, new ScreenshotMosaicStyle(16, 8).BrushSize);
        Assert.Equal(64, new ScreenshotMosaicStyle(64, 16).BrushSize);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenshotMosaicStyle(24, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenshotMosaicStyle(32, 0));
    }

    [Fact]
    public void MosaicStrokeInterpolatesContinuousNonDestructivePoints()
    {
        var session = new ScreenshotAnnotationSession();
        session.SetTool(ScreenshotAnnotationTool.Mosaic);
        session.SetMosaicStyle(new ScreenshotMosaicStyle(16, 8));

        session.Begin(new LogicalPoint(4, 6));
        session.Update(new LogicalPoint(60, 6));

        var preview = Assert.IsType<ScreenshotMosaicAnnotation>(session.Preview);
        Assert.True(preview.Points.Count > 2);
        Assert.All(
            preview.Points.Zip(preview.Points.Skip(1)),
            pair => Assert.InRange(Distance(pair.First, pair.Second), 0, 4.001));
        Assert.Equal(new ScreenshotMosaicStyle(16, 8), preview.Style);
        Assert.Empty(session.Annotations);

        Assert.True(session.Complete());
        Assert.Same(preview, Assert.Single(session.Annotations));
    }

    [Fact]
    public void HitTestingSelectsOnlyTheTopmostOverlappingObject()
    {
        var session = new ScreenshotAnnotationSession();
        DrawRectangle(session, new LogicalPoint(5, 5), new LogicalPoint(45, 35));
        session.SetStyle(new ScreenshotAnnotationStyle(ScreenshotAnnotationColor.Blue, 8));
        DrawRectangle(session, new LogicalPoint(5, 5), new LogicalPoint(45, 35));

        Assert.Equal(1, session.HitTest(new LogicalPoint(5, 20)));
        Assert.True(session.SelectAt(new LogicalPoint(5, 20)));
        Assert.Equal(1, session.SelectedIndex);
        Assert.Equal(
            ScreenshotAnnotationColor.Blue,
            Assert.IsType<ScreenshotRectangleAnnotation>(session.SelectedAnnotation).Style.Color);

        Assert.False(session.SelectAt(new LogicalPoint(80, 80)));
        Assert.Null(session.SelectedIndex);
    }

    [Fact]
    public void CreationMoveResizeStyleAndDeleteAreUndoableAndRedoable()
    {
        var session = new ScreenshotAnnotationSession();
        DrawRectangle(session, new LogicalPoint(5, 5), new LogicalPoint(25, 20));
        Assert.True(session.CanUndo);
        Assert.True(session.Undo());
        Assert.Empty(session.Annotations);
        Assert.True(session.Redo());

        Assert.True(session.Select(0));
        Assert.True(session.BeginMoveSelected(new LogicalPoint(5, 5)));
        session.UpdateSelectedTransform(new LogicalPoint(15, 10));
        Assert.True(session.CompleteSelectedTransform());
        var moved = Assert.IsType<ScreenshotRectangleAnnotation>(session.SelectedAnnotation);
        Assert.Equal(new LogicalPoint(15, 10), moved.Start);
        Assert.Equal(new LogicalPoint(35, 25), moved.End);

        Assert.True(session.BeginResizeSelected(AnnotationResizeHandle.End));
        session.UpdateSelectedTransform(new LogicalPoint(50, 40));
        Assert.True(session.CompleteSelectedTransform());
        var resized = Assert.IsType<ScreenshotRectangleAnnotation>(session.SelectedAnnotation);
        Assert.Equal(new LogicalPoint(50, 40), resized.End);

        Assert.True(session.UpdateSelectedStyle(
            new ScreenshotAnnotationStyle(ScreenshotAnnotationColor.Green, 2)));
        Assert.Equal(
            ScreenshotAnnotationColor.Green,
            Assert.IsType<ScreenshotRectangleAnnotation>(session.SelectedAnnotation).Style.Color);
        Assert.True(session.DeleteSelected());
        Assert.Empty(session.Annotations);

        Assert.True(session.Undo());
        Assert.NotNull(session.SelectedAnnotation);
        Assert.True(session.Undo());
        Assert.Equal(
            ScreenshotAnnotationColor.Red,
            Assert.IsType<ScreenshotRectangleAnnotation>(session.SelectedAnnotation).Style.Color);
    }

    [Theory]
    [InlineData(AnnotationResizeHandle.TopLeft, 5, 6, 5, 6, 40, 30)]
    [InlineData(AnnotationResizeHandle.Top, 20, 6, 10, 6, 40, 30)]
    [InlineData(AnnotationResizeHandle.TopRight, 50, 6, 10, 6, 50, 30)]
    [InlineData(AnnotationResizeHandle.Right, 50, 20, 10, 10, 50, 30)]
    [InlineData(AnnotationResizeHandle.BottomRight, 50, 40, 10, 10, 50, 40)]
    [InlineData(AnnotationResizeHandle.Bottom, 20, 40, 10, 10, 40, 40)]
    [InlineData(AnnotationResizeHandle.BottomLeft, 5, 40, 5, 10, 40, 40)]
    [InlineData(AnnotationResizeHandle.Left, 5, 20, 5, 10, 40, 30)]
    public void RectangleCanResizeFromAllEightControlPoints(
        AnnotationResizeHandle handle,
        double pointerX,
        double pointerY,
        double expectedLeft,
        double expectedTop,
        double expectedRight,
        double expectedBottom)
    {
        var session = new ScreenshotAnnotationSession();
        DrawRectangle(session, new LogicalPoint(10, 10), new LogicalPoint(40, 30));
        Assert.True(session.Select(0));

        Assert.True(session.BeginResizeSelected(handle));
        session.UpdateSelectedTransform(new LogicalPoint(pointerX, pointerY));
        Assert.True(session.CompleteSelectedTransform());

        var rectangle = Assert.IsType<ScreenshotRectangleAnnotation>(session.SelectedAnnotation);
        Assert.Equal(new LogicalPoint(expectedLeft, expectedTop), rectangle.Start);
        Assert.Equal(new LogicalPoint(expectedRight, expectedBottom), rectangle.End);
    }

    [Fact]
    public void MovingAnArrowEndpointChangesItsLengthAndDirection()
    {
        var session = new ScreenshotAnnotationSession();
        session.SetTool(ScreenshotAnnotationTool.Arrow);
        session.Begin(new LogicalPoint(10, 10));
        session.Update(new LogicalPoint(40, 10));
        Assert.True(session.Complete());
        Assert.True(session.Select(0));

        Assert.True(session.BeginResizeSelected(AnnotationResizeHandle.End));
        session.UpdateSelectedTransform(new LogicalPoint(20, 40));
        Assert.True(session.CompleteSelectedTransform());

        var arrow = Assert.IsType<ScreenshotArrowAnnotation>(session.SelectedAnnotation);
        Assert.Equal(new LogicalPoint(10, 10), arrow.Start);
        Assert.Equal(new LogicalPoint(20, 40), arrow.End);
    }

    [Fact]
    public void TextAndMosaicCanMoveAndChangeStyleButMosaicCannotResize()
    {
        var session = new ScreenshotAnnotationSession();
        session.SetTool(ScreenshotAnnotationTool.Text);
        session.BeginText(new LogicalPoint(10, 12), 120);
        session.UpdateText("text", isComposing: false);
        Assert.True(session.CommitText());
        Assert.True(session.Select(0));
        Assert.True(session.UpdateSelectedStyle(
            new ScreenshotTextStyle(ScreenshotAnnotationColor.Yellow, 32)));
        Assert.Equal(
            32,
            Assert.IsType<ScreenshotTextAnnotation>(session.SelectedAnnotation).Style.FontSize);

        session.SetTool(ScreenshotAnnotationTool.Mosaic);
        session.Begin(new LogicalPoint(30, 30));
        session.Update(new LogicalPoint(50, 50));
        Assert.True(session.Complete());
        Assert.True(session.Select(1));
        Assert.False(session.BeginResizeSelected(AnnotationResizeHandle.End));
        Assert.True(session.UpdateSelectedStyle(new ScreenshotMosaicStyle(64, 16)));
        Assert.True(session.BeginMoveSelected(new LogicalPoint(30, 30)));
        session.UpdateSelectedTransform(new LogicalPoint(40, 35));
        Assert.True(session.CompleteSelectedTransform());
        var mosaic = Assert.IsType<ScreenshotMosaicAnnotation>(session.SelectedAnnotation);
        Assert.Equal(new LogicalPoint(40, 35), mosaic.Points[0]);
        Assert.Equal(64, mosaic.Style.BrushSize);
    }

    [Fact]
    public void RebasingAfterSelectionResizeKeepsObjectsAtTheirScreenCoordinates()
    {
        var session = new ScreenshotAnnotationSession();
        DrawRectangle(session, new LogicalPoint(20, 15), new LogicalPoint(50, 40));

        session.RebaseForSelectionOriginChange(new LogicalPoint(8, 5));

        var rectangle = Assert.IsType<ScreenshotRectangleAnnotation>(session.Annotations[0]);
        Assert.Equal(new LogicalPoint(28, 20), rectangle.Start);
        Assert.Equal(new LogicalPoint(58, 45), rectangle.End);
        Assert.True(session.CanUndo);
    }

    private static void DrawRectangle(
        ScreenshotAnnotationSession session,
        LogicalPoint start,
        LogicalPoint end)
    {
        session.SetTool(ScreenshotAnnotationTool.Rectangle);
        session.Begin(start);
        session.Update(end);
        Assert.True(session.Complete());
    }

    private static double Distance(LogicalPoint first, LogicalPoint second)
    {
        var deltaX = second.X - first.X;
        var deltaY = second.Y - first.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }
}
