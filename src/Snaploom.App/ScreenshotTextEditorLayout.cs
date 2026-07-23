using Avalonia;
using Snaploom.Core;
using Snaploom.Rendering;

namespace Snaploom.App;

internal static class ScreenshotTextEditorLayout
{
    internal static Rect Measure(
        ScreenshotTextAnnotation text,
        Rect selection) =>
        Measure(
            new ScreenshotTextEdit(
                text.Origin,
                text.Text,
                text.MaxWidth,
                text.Style,
                AnnotationIndex: null,
                IsComposing: false),
            selection);

    internal static Rect Measure(
        ScreenshotTextEdit edit,
        Rect selection,
        string? measurementText = null)
    {
        var draftText = measurementText ?? edit.Text;
        var text = string.IsNullOrEmpty(draftText)
            ? "I"
            : draftText;
        var measured = ScreenshotAnnotationRenderer.MeasureText(new ScreenshotTextAnnotation(
            new LogicalPoint(0, 0),
            text,
            edit.MaxWidth,
            edit.Style));
        var maximumWidth = Math.Max(
            1,
            edit.MaxWidth + ScreenshotUiTheme.TextEditorMeasuredWidthPadding);
        var minimumWidth = Math.Min(
            Math.Max(
                ScreenshotUiTheme.TextEditorMinimumWidth,
                ScreenshotTextMetrics.GetMinimumContentWidth(edit.Style) +
                    ScreenshotUiTheme.TextEditorMeasuredWidthPadding),
            maximumWidth);
        var width = Math.Clamp(
            measured.Width + ScreenshotUiTheme.TextEditorMeasuredWidthPadding,
            minimumWidth,
            maximumWidth);
        var minimumHeight = (edit.Style.FontSize *
            ScreenshotTextMetrics.LineHeightMultiplier) +
            ScreenshotUiTheme.TextEditorMeasuredHeightPadding;
        var maximumHeight = Math.Max(
            1,
            selection.Height -
                edit.Origin.Y +
                ScreenshotUiTheme.TextEditorChromeInset);
        minimumHeight = Math.Min(minimumHeight, maximumHeight);
        var height = Math.Clamp(
            measured.Height + ScreenshotUiTheme.TextEditorMeasuredHeightPadding,
            minimumHeight,
            maximumHeight);

        return new Rect(
            selection.X + edit.Origin.X - ScreenshotUiTheme.TextEditorChromeInset,
            selection.Y + edit.Origin.Y - ScreenshotUiTheme.TextEditorChromeInset,
            width,
            height);
    }
}
