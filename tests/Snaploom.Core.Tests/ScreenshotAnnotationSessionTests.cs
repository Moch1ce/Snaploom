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
}
