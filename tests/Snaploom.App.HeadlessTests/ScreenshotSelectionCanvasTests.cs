using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Snaploom.Core;

namespace Snaploom.App.HeadlessTests;

public sealed class ScreenshotSelectionCanvasTests
{
    [AvaloniaFact]
    public void DragThresholdAndMinimumSizeUseLogicalAndPhysicalCoordinates()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            window.MouseMove(new Point(10, 10), RawInputModifiers.None);
            window.MouseDown(
                new Point(10, 10),
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);
            window.MouseMove(
                new Point(12.5, 12.5),
                RawInputModifiers.LeftMouseButton);

            Assert.Equal(ScreenshotSessionState.Ready, canvas.Session.State);

            window.MouseMove(
                new Point(13, 13),
                RawInputModifiers.LeftMouseButton);
            Assert.Equal(ScreenshotSessionState.Selecting, canvas.Session.State);

            window.MouseMove(
                new Point(14, 14),
                RawInputModifiers.LeftMouseButton);
            window.MouseUp(
                new Point(14, 14),
                MouseButton.Left,
                RawInputModifiers.None);

            Assert.Equal(ScreenshotSessionState.Selected, canvas.Session.State);
            Assert.Equal(new PhysicalRect(20, 20, 8, 8), canvas.Session.Selection);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void BottomRightHandleResizesACompletedSelection()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(30, 30));
            Assert.Equal(new PhysicalRect(20, 20, 40, 40), canvas.Session.Selection);

            Drag(window, new Point(30, 30), new Point(40, 45));

            Assert.Equal(ScreenshotSessionState.Selected, canvas.Session.State);
            Assert.Equal(new PhysicalRect(20, 20, 60, 70), canvas.Session.Selection);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HoverAndClickSelectTheTopmostWindowCandidate()
    {
        using var frame = CreateHighDpiFrame();
        var candidates = new[]
        {
            new ScreenshotWindowCandidate(
                20,
                new PhysicalRect(30, 30, 100, 80),
                1,
                ScreenshotWindowExclusion.None),
            new ScreenshotWindowCandidate(
                10,
                new PhysicalRect(20, 20, 100, 80),
                0,
                ScreenshotWindowExclusion.None),
        };
        using var canvas = new ScreenshotSelectionCanvas(frame, candidates);
        var window = ShowCanvas(canvas);
        try
        {
            window.MouseMove(new Point(20, 20), RawInputModifiers.None);

            Assert.Equal(ScreenshotSnapTargetKind.Window, canvas.HoveredSnapTarget?.Kind);
            Assert.Equal(10, canvas.HoveredSnapTarget?.WindowId);

            window.MouseDown(
                new Point(20, 20),
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);
            window.MouseUp(
                new Point(20, 20),
                MouseButton.Left,
                RawInputModifiers.None);

            Assert.Equal(ScreenshotSessionState.Selected, canvas.Session.State);
            Assert.Equal(new PhysicalRect(20, 20, 100, 80), canvas.Session.Selection);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ClickingDesktopSpaceSelectsTheWholeCurrentDisplay()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(
            frame,
            Array.Empty<ScreenshotWindowCandidate>());
        var window = ShowCanvas(canvas);
        try
        {
            window.MouseMove(new Point(90, 90), RawInputModifiers.None);
            window.MouseDown(
                new Point(90, 90),
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);
            window.MouseUp(
                new Point(90, 90),
                MouseButton.Left,
                RawInputModifiers.None);

            Assert.Equal(new PhysicalRect(0, 0, 200, 200), canvas.Session.Selection);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RectangleAndArrowGesturesAppendAnnotationsInVisualOrder()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(80, 80));

            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Rectangle);
            Drag(window, new Point(20, 20), new Point(50, 45));

            var rectangle = Assert.IsType<ScreenshotRectangleAnnotation>(
                Assert.Single(canvas.Annotations));
            Assert.Equal(new LogicalPoint(10, 10), rectangle.Start);
            Assert.Equal(new LogicalPoint(40, 35), rectangle.End);

            canvas.SetAnnotationStyle(
                new ScreenshotAnnotationStyle(ScreenshotAnnotationColor.Yellow, 8));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Arrow);
            Drag(window, new Point(25, 60), new Point(65, 30));

            Assert.Equal(2, canvas.Annotations.Count);
            var arrow = Assert.IsType<ScreenshotArrowAnnotation>(canvas.Annotations[1]);
            Assert.Equal(ScreenshotAnnotationColor.Yellow, arrow.Style.Color);
            Assert.Equal(8, arrow.Style.LineWidth);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EscapeCancelsThePreviewThenReturnsToSelectionTool()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(80, 80));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Rectangle);
            window.MouseMove(new Point(20, 20), RawInputModifiers.None);
            window.MouseDown(
                new Point(20, 20),
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);
            window.MouseMove(
                new Point(50, 45),
                RawInputModifiers.LeftMouseButton);

            Assert.Equal(ScreenshotCancelResult.ActionCanceled, canvas.CancelCurrentLayer());
            Assert.Empty(canvas.Annotations);
            Assert.Equal(ScreenshotAnnotationTool.Rectangle, canvas.ActiveAnnotationTool);

            Assert.Equal(ScreenshotCancelResult.ActionCanceled, canvas.CancelCurrentLayer());
            Assert.Equal(ScreenshotAnnotationTool.Select, canvas.ActiveAnnotationTool);
            Assert.NotNull(canvas.Session.Selection);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TextClickStartsAnEditorAndCommitsMultilineText()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(80, 80));
            canvas.SetTextStyle(
                new ScreenshotTextStyle(ScreenshotAnnotationColor.Blue, 32));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Text);

            window.MouseDown(
                new Point(20, 25),
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);
            window.MouseUp(
                new Point(20, 25),
                MouseButton.Left,
                RawInputModifiers.None);

            Assert.NotNull(canvas.TextEdit);
            canvas.UpdateTextDraft("中文 English 123\n第二行", isComposing: false);
            Assert.True(canvas.CommitTextEdit());

            var text = Assert.IsType<ScreenshotTextAnnotation>(
                Assert.Single(canvas.Annotations));
            Assert.Equal(new LogicalPoint(10, 15), text.Origin);
            Assert.Equal("中文 English 123\n第二行", text.Text);
            Assert.Equal(new ScreenshotTextStyle(ScreenshotAnnotationColor.Blue, 32), text.Style);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EscapeCancelsTextEditingBeforeItCancelsTheTextTool()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(80, 80));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Text);
            window.MouseDown(
                new Point(20, 25),
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);
            window.MouseUp(
                new Point(20, 25),
                MouseButton.Left,
                RawInputModifiers.None);
            canvas.UpdateTextDraft("不应保存", isComposing: false);

            Assert.Equal(ScreenshotCancelResult.ActionCanceled, canvas.CancelCurrentLayer());
            Assert.Null(canvas.TextEdit);
            Assert.Empty(canvas.Annotations);
            Assert.Equal(ScreenshotAnnotationTool.Text, canvas.ActiveAnnotationTool);

            Assert.Equal(ScreenshotCancelResult.ActionCanceled, canvas.CancelCurrentLayer());
            Assert.Equal(ScreenshotAnnotationTool.Select, canvas.ActiveAnnotationTool);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DoubleClickingCommittedTextReopensItForEditing()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(80, 80));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Text);
            window.MouseDown(
                new Point(20, 25),
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);
            window.MouseUp(
                new Point(20, 25),
                MouseButton.Left,
                RawInputModifiers.None);
            canvas.UpdateTextDraft("可编辑文字", isComposing: false);
            Assert.True(canvas.CommitTextEdit());
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Select);

            Click(window, new Point(22, 28));
            Click(window, new Point(22, 28));

            Assert.Equal("可编辑文字", canvas.TextEdit?.Text);
            canvas.UpdateTextDraft("修改完成", isComposing: false);
            Assert.True(canvas.CommitTextEdit());
            Assert.Equal(
                "修改完成",
                Assert.IsType<ScreenshotTextAnnotation>(Assert.Single(canvas.Annotations)).Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MosaicGestureCommitsAContinuousStroke()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(80, 80));
            canvas.SetMosaicStyle(new ScreenshotMosaicStyle(64, 16));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Mosaic);

            Drag(window, new Point(20, 25), new Point(65, 55));

            var mosaic = Assert.IsType<ScreenshotMosaicAnnotation>(
                Assert.Single(canvas.Annotations));
            Assert.True(mosaic.Points.Count > 2);
            Assert.Equal(new ScreenshotMosaicStyle(64, 16), mosaic.Style);
            Assert.True(canvas.MosaicTileCount > 0);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SelectedAnnotationMovesInsteadOfTheSelectionAndCanBeUndone()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(80, 80));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Rectangle);
            Drag(window, new Point(20, 20), new Point(50, 45));
            var originalSelection = canvas.Session.Selection;
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Select);

            Drag(window, new Point(20, 30), new Point(30, 35));

            Assert.Equal(originalSelection, canvas.Session.Selection);
            var moved = Assert.IsType<ScreenshotRectangleAnnotation>(
                Assert.Single(canvas.Annotations));
            Assert.Equal(new LogicalPoint(20, 15), moved.Start);
            Assert.Equal(new LogicalPoint(50, 40), moved.End);
            Assert.NotNull(canvas.SelectedAnnotation);
            Assert.Equal(
                ScreenshotCancelResult.ActionCanceled,
                canvas.CancelCurrentLayer());
            Assert.Null(canvas.SelectedAnnotation);
            Assert.NotNull(canvas.Session.Selection);
            Assert.True(canvas.UndoAnnotation());
            var restored = Assert.IsType<ScreenshotRectangleAnnotation>(
                Assert.Single(canvas.Annotations));
            Assert.Equal(new LogicalPoint(10, 10), restored.Start);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ResizingSelectionFromTopLeftKeepsAnnotationAtItsScreenPosition()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(80, 80));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Rectangle);
            Drag(window, new Point(30, 30), new Point(50, 50));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Select);

            Drag(window, new Point(10, 10), new Point(15, 15));

            var selection = Assert.IsType<PhysicalRect>(canvas.Session.Selection);
            var logicalSelection = Assert.IsType<Rect>(canvas.LogicalSelection);
            var rectangle = Assert.IsType<ScreenshotRectangleAnnotation>(
                Assert.Single(canvas.Annotations));
            Assert.Equal(new LogicalPoint(15, 15), rectangle.Start);
            Assert.Equal(30, logicalSelection.X + rectangle.Start.X);
            Assert.Equal(30, logicalSelection.Y + rectangle.Start.Y);
            Assert.Equal(new PhysicalRect(30, 30, 130, 130), selection);
        }
        finally
        {
            window.Close();
        }
    }

    private static Window ShowCanvas(ScreenshotSelectionCanvas canvas)
    {
        var window = new Window
        {
            Width = 100,
            Height = 100,
            Content = canvas,
        };
        window.Show();
        return window;
    }

    private static void Drag(Window window, Point start, Point end)
    {
        window.MouseMove(start, RawInputModifiers.None);
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left, RawInputModifiers.None);
    }

    private static void Click(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }

    private static CapturedFrame CreateHighDpiFrame()
    {
        var pixels = new byte[200 * 200 * 4];
        for (var index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = byte.MaxValue;
        }

        return new CapturedFrame(
            new PhysicalSize(200, 200),
            new LogicalSize(100, 100),
            stride: 800,
            pixels);
    }
}
