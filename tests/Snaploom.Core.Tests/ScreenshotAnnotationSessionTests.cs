using Snaploom.Core;

namespace Snaploom.Core.Tests;

public sealed class ScreenshotAnnotationSessionTests
{
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
}
