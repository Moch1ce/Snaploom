using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Snaploom.Core;

namespace Snaploom.App.HeadlessTests;

public sealed class ScreenshotSelectionCanvasTests
{
    [AvaloniaFact]
    public void ResizeHandlesUseTheirCorrespondingSystemCursorTypes()
    {
        Assert.Equal(
            StandardCursorType.SizeWestEast,
            ScreenshotSelectionCanvas.GetResizeCursorType(SelectionResizeHandle.Left));
        Assert.Equal(
            StandardCursorType.SizeNorthSouth,
            ScreenshotSelectionCanvas.GetResizeCursorType(SelectionResizeHandle.Bottom));
        Assert.Equal(
            StandardCursorType.TopLeftCorner,
            ScreenshotSelectionCanvas.GetResizeCursorType(SelectionResizeHandle.TopLeft));
        Assert.Equal(
            StandardCursorType.TopRightCorner,
            ScreenshotSelectionCanvas.GetResizeCursorType(SelectionResizeHandle.TopRight));
        Assert.Equal(
            StandardCursorType.BottomRightCorner,
            ScreenshotSelectionCanvas.GetResizeCursorType(SelectionResizeHandle.BottomRight));
        Assert.Equal(
            StandardCursorType.BottomLeftCorner,
            ScreenshotSelectionCanvas.GetResizeCursorType(SelectionResizeHandle.BottomLeft));

        Assert.Equal(
            StandardCursorType.TopLeftCorner,
            ScreenshotSelectionCanvas.GetResizeCursorType(AnnotationResizeHandle.TopLeft));
        Assert.Equal(
            StandardCursorType.TopRightCorner,
            ScreenshotSelectionCanvas.GetResizeCursorType(AnnotationResizeHandle.TopRight));
        Assert.Equal(
            StandardCursorType.BottomRightCorner,
            ScreenshotSelectionCanvas.GetResizeCursorType(AnnotationResizeHandle.BottomRight));
        Assert.Equal(
            StandardCursorType.BottomLeftCorner,
            ScreenshotSelectionCanvas.GetResizeCursorType(AnnotationResizeHandle.BottomLeft));
        Assert.Equal(
            StandardCursorType.SizeWestEast,
            ScreenshotSelectionCanvas.GetResizeCursorType(AnnotationResizeHandle.Left));
        Assert.Equal(
            StandardCursorType.SizeNorthSouth,
            ScreenshotSelectionCanvas.GetResizeCursorType(AnnotationResizeHandle.Bottom));
        Assert.Equal(
            StandardCursorType.DragMove,
            ScreenshotSelectionCanvas.GetResizeCursorType(AnnotationResizeHandle.End));
    }

    [AvaloniaFact]
    public void SelectionCornersApplyFourDistinctDirectionalCursors()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(80, 80));
            var selection = Assert.IsType<Rect>(canvas.LogicalSelection);
            var cursors = new List<Cursor>();
            foreach (var corner in new[]
            {
                selection.TopLeft,
                selection.TopRight,
                selection.BottomRight,
                selection.BottomLeft,
            })
            {
                window.MouseMove(corner, RawInputModifiers.None);
                Assert.Equal(ScreenshotPointerFeedback.ResizeDiagonal, canvas.PointerFeedback);
                cursors.Add(Assert.IsType<Cursor>(canvas.Cursor));
            }

            if (OperatingSystem.IsMacOS())
            {
                Assert.All(cursors, cursor => Assert.Equal("BitmapCursor", cursor.ToString()));
            }

            for (var first = 0; first < cursors.Count; first++)
            {
                for (var second = first + 1; second < cursors.Count; second++)
                {
                    Assert.NotSame(cursors[first], cursors[second]);
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SelectionBordersResizeFromAnywhereAlongEachEdge()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(80, 80));
            foreach (var (point, feedback) in new[]
            {
                (new Point(30, 10), ScreenshotPointerFeedback.ResizeVertical),
                (new Point(80, 30), ScreenshotPointerFeedback.ResizeHorizontal),
                (new Point(60, 80), ScreenshotPointerFeedback.ResizeVertical),
                (new Point(10, 60), ScreenshotPointerFeedback.ResizeHorizontal),
            })
            {
                window.MouseMove(point, RawInputModifiers.None);
                Assert.Equal(feedback, canvas.PointerFeedback);
            }

            Drag(window, new Point(30, 10), new Point(30, 20));

            Assert.Equal(new Rect(10, 20, 70, 60), canvas.LogicalSelection);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SelectionCornerKeepsResizePriorityWhileDrawingToolIsActive()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(80, 80));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Rectangle);

            window.MouseMove(new Point(80, 80), RawInputModifiers.None);
            Assert.Equal(ScreenshotPointerFeedback.ResizeDiagonal, canvas.PointerFeedback);

            Drag(window, new Point(80, 80), new Point(90, 90));

            Assert.Equal(new Rect(10, 10, 80, 80), canvas.LogicalSelection);
            Assert.Empty(canvas.Annotations);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SelectedRectangleCornersApplyFourDistinctDirectionalCursors()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(90, 90));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Rectangle);
            Drag(window, new Point(20, 20), new Point(70, 70));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Select);
            Click(window, new Point(35, 20));

            var cursors = new List<Cursor>();
            foreach (var corner in new[]
            {
                new Point(20, 20),
                new Point(70, 20),
                new Point(70, 70),
                new Point(20, 70),
            })
            {
                window.MouseMove(corner, RawInputModifiers.None);
                Assert.Equal(ScreenshotPointerFeedback.ResizeDiagonal, canvas.PointerFeedback);
                cursors.Add(Assert.IsType<Cursor>(canvas.Cursor));
            }

            for (var first = 0; first < cursors.Count; first++)
            {
                for (var second = first + 1; second < cursors.Count; second++)
                {
                    Assert.NotSame(cursors[first], cursors[second]);
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MoveCursorUsesSmallThinPureBlackBlockStyleFourDirectionalArrows()
    {
        var geometry = ScreenshotUiTheme.MoveCursorGeometry;

        Assert.Equal(24, ScreenshotUiTheme.MoveCursorSize);
        Assert.Same(Brushes.Black, ScreenshotUiTheme.MoveCursorFillBrush);
        Assert.Equal(new Rect(5, 5, 14, 14), geometry.Bounds);
        Assert.All(
            new[]
            {
                new Point(12, 5.5),
                new Point(18.5, 12),
                new Point(12, 18.5),
                new Point(5.5, 12),
                new Point(12, 12),
                new Point(11.25, 8.5),
                new Point(8.5, 11.25),
                new Point(12.75, 15.5),
                new Point(15.5, 12.75),
            },
            point => Assert.True(geometry.FillContains(point)));
        Assert.All(
            new[]
            {
                new Point(6, 6),
                new Point(18, 6),
                new Point(18, 18),
                new Point(6, 18),
                new Point(12, 4.5),
                new Point(19.5, 12),
                new Point(12, 19.5),
                new Point(4.5, 12),
                new Point(10.75, 8.5),
                new Point(8.5, 10.75),
                new Point(13.25, 15.5),
                new Point(15.5, 13.25),
            },
            point => Assert.False(geometry.FillContains(point)));
    }

    [AvaloniaFact]
    public void ResizeCursorFallbackUsesFourCompactThemeGeometries()
    {
        Assert.Equal(22, ScreenshotUiTheme.ResizeCursorFallbackSize);
        Assert.Same(Brushes.Black, ScreenshotUiTheme.ResizeCursorFallbackFillBrush);
        Assert.Same(Brushes.White, ScreenshotUiTheme.ResizeCursorFallbackOutlineBrush);
        Assert.Equal(1, ScreenshotUiTheme.ResizeCursorFallbackOutlineThickness);

        var geometries = new[]
        {
            ScreenshotResizeCursor.GetFallbackGeometry(StandardCursorType.TopLeftCorner),
            ScreenshotResizeCursor.GetFallbackGeometry(StandardCursorType.TopRightCorner),
            ScreenshotResizeCursor.GetFallbackGeometry(StandardCursorType.BottomRightCorner),
            ScreenshotResizeCursor.GetFallbackGeometry(StandardCursorType.BottomLeftCorner),
        };
        Assert.Same(ScreenshotUiTheme.ResizeCursorTopLeftFallbackGeometry, geometries[0]);
        Assert.Same(ScreenshotUiTheme.ResizeCursorTopRightFallbackGeometry, geometries[1]);
        Assert.Same(ScreenshotUiTheme.ResizeCursorBottomRightFallbackGeometry, geometries[2]);
        Assert.Same(ScreenshotUiTheme.ResizeCursorBottomLeftFallbackGeometry, geometries[3]);
        for (var first = 0; first < geometries.Length; first++)
        {
            for (var second = first + 1; second < geometries.Length; second++)
            {
                Assert.NotSame(geometries[first], geometries[second]);
            }
        }
    }

    [AvaloniaFact]
    public void CreatingSelectionUsesMoveCursorFromPointerDown()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            window.MouseDown(
                new Point(10, 10),
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);

            Assert.Equal(ScreenshotPointerFeedback.MoveSelection, canvas.PointerFeedback);

            window.MouseMove(
                new Point(13, 13),
                RawInputModifiers.LeftMouseButton);

            Assert.Equal(ScreenshotSessionState.Selecting, canvas.Session.State);
            Assert.Equal(ScreenshotPointerFeedback.MoveSelection, canvas.PointerFeedback);

            window.MouseUp(
                new Point(13, 13),
                MouseButton.Left,
                RawInputModifiers.None);

            Assert.Equal(ScreenshotSessionState.Ready, canvas.Session.State);
            Assert.Equal(ScreenshotPointerFeedback.Crosshair, canvas.PointerFeedback);
        }
        finally
        {
            window.Close();
        }
    }

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
            Assert.Equal(ScreenshotPointerFeedback.ResizeDiagonal, canvas.PointerFeedback);
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
    public void WindowSnapWaitsUntilThePointerLeavesItsCaptureOrigin()
    {
        using var frame = CreateHighDpiFrame();
        var candidates = new[]
        {
            new ScreenshotWindowCandidate(
                10,
                new PhysicalRect(20, 20, 100, 80),
                0,
                ScreenshotWindowExclusion.None),
        };
        using var canvas = new ScreenshotSelectionCanvas(
            frame,
            candidates,
            new PhysicalPoint(40, 40));
        var window = ShowCanvas(canvas);
        try
        {
            window.MouseMove(new Point(20, 20), RawInputModifiers.None);

            Assert.Null(canvas.HoveredSnapTarget);
            Click(window, new Point(20, 20));
            Assert.Equal(ScreenshotSessionState.Ready, canvas.Session.State);
            Assert.Null(canvas.Session.Selection);
            Assert.Equal(ScreenshotPointerFeedback.Crosshair, canvas.PointerFeedback);

            window.MouseMove(new Point(21, 20), RawInputModifiers.None);

            Assert.Equal(ScreenshotSnapTargetKind.Window, canvas.HoveredSnapTarget?.Kind);
            Assert.Equal(10, canvas.HoveredSnapTarget?.WindowId);
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
    public void SelectionCornerWinsOverOverlappingTextOnDoubleClick()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(90, 90));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Text);
            Click(window, new Point(30, 30));
            canvas.UpdateTextDraft("角落文字", isComposing: false);
            Assert.True(canvas.CommitTextEdit());
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Select);

            Drag(window, new Point(10, 10), new Point(30, 30));
            Assert.Equal(new Rect(30, 30, 60, 60), canvas.LogicalSelection);

            Click(window, new Point(30, 30));
            Click(window, new Point(30, 30));

            Assert.Null(canvas.TextEdit);
            Assert.Equal(ScreenshotSessionState.Selected, canvas.Session.State);
            Assert.Equal(new Rect(30, 30, 60, 60), canvas.LogicalSelection);
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
    public void PressingInsideSelectionImmediatelyShowsMoveFeedback()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(80, 80));

            window.MouseDown(
                new Point(70, 70),
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);

            Assert.Equal(ScreenshotSessionState.MovingSelection, canvas.Session.State);
            Assert.Equal(ScreenshotPointerFeedback.MoveSelection, canvas.PointerFeedback);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ClickingAnnotationUsesPointerUntilDragBegins()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(90, 90));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Rectangle);
            Drag(window, new Point(20, 20), new Point(70, 70));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Select);

            window.MouseMove(new Point(35, 20), RawInputModifiers.None);
            Assert.Equal(ScreenshotPointerFeedback.SelectAnnotation, canvas.PointerFeedback);
            Assert.Same(AppCursorStyles.PointerCursor, canvas.Cursor);

            window.MouseDown(
                new Point(35, 20),
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);

            Assert.True(canvas.SelectedAnnotation is ScreenshotRectangleAnnotation);
            Assert.Equal(ScreenshotPointerFeedback.SelectAnnotation, canvas.PointerFeedback);
            Assert.Same(AppCursorStyles.PointerCursor, canvas.Cursor);

            window.MouseMove(new Point(40, 20), RawInputModifiers.LeftMouseButton);
            Assert.Equal(ScreenshotPointerFeedback.MoveAnnotation, canvas.PointerFeedback);

            window.MouseUp(
                new Point(40, 20),
                MouseButton.Left,
                RawInputModifiers.None);
            Assert.Equal(ScreenshotPointerFeedback.ResizeVertical, canvas.PointerFeedback);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LosingPointerCaptureCancelsAnnotationMoveAndRestoresPointer()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(90, 90));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Rectangle);
            Drag(window, new Point(20, 20), new Point(70, 70));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Select);

            window.MouseDown(
                new Point(35, 20),
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(40, 20), RawInputModifiers.LeftMouseButton);

            var movingRectangle = Assert.IsType<ScreenshotRectangleAnnotation>(
                Assert.Single(canvas.Annotations));
            Assert.Equal(new LogicalPoint(15, 10), movingRectangle.Start);

            canvas.HandlePointerCaptureLost();

            var restoredRectangle = Assert.IsType<ScreenshotRectangleAnnotation>(
                Assert.Single(canvas.Annotations));
            Assert.Equal(new LogicalPoint(10, 10), restoredRectangle.Start);
            Assert.Equal(new LogicalPoint(60, 60), restoredRectangle.End);
            Assert.Equal(ScreenshotPointerFeedback.SelectAnnotation, canvas.PointerFeedback);
            Assert.Same(AppCursorStyles.PointerCursor, canvas.Cursor);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LosingPointerCaptureCancelsSelectionResizeAndRebasesAnnotations()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(90, 90));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Rectangle);
            Drag(window, new Point(30, 30), new Point(50, 50));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Select);

            window.MouseDown(
                new Point(10, 10),
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(20, 20), RawInputModifiers.LeftMouseButton);

            Assert.Equal(new Rect(20, 20, 70, 70), canvas.LogicalSelection);
            Assert.Equal(
                new LogicalPoint(10, 10),
                Assert.IsType<ScreenshotRectangleAnnotation>(
                    Assert.Single(canvas.Annotations)).Start);

            canvas.HandlePointerCaptureLost();

            Assert.Equal(ScreenshotSessionState.Selected, canvas.Session.State);
            Assert.Equal(new Rect(10, 10, 80, 80), canvas.LogicalSelection);
            Assert.Equal(
                new LogicalPoint(20, 20),
                Assert.IsType<ScreenshotRectangleAnnotation>(
                    Assert.Single(canvas.Annotations)).Start);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LosingPointerCaptureCancelsSelectionAndAnnotationDrawingGestures()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            window.MouseDown(
                new Point(10, 10),
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(60, 60), RawInputModifiers.LeftMouseButton);
            Assert.Equal(ScreenshotSessionState.Selecting, canvas.Session.State);

            canvas.HandlePointerCaptureLost();

            Assert.Equal(ScreenshotSessionState.Ready, canvas.Session.State);
            Assert.Null(canvas.Session.Selection);
            Assert.Equal(ScreenshotPointerFeedback.Crosshair, canvas.PointerFeedback);

            Drag(window, new Point(10, 10), new Point(90, 90));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Rectangle);
            window.MouseDown(
                new Point(20, 20),
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);
            window.MouseMove(new Point(60, 60), RawInputModifiers.LeftMouseButton);

            canvas.HandlePointerCaptureLost();

            Assert.Empty(canvas.Annotations);
            Assert.Equal(ScreenshotSessionState.Selected, canvas.Session.State);
            Assert.Equal(ScreenshotPointerFeedback.Crosshair, canvas.PointerFeedback);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ArrowUsesPointerWhenItCanBeClickedForSelection()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(90, 90));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Arrow);
            Drag(window, new Point(20, 30), new Point(70, 50));

            window.MouseMove(new Point(45, 40), RawInputModifiers.None);
            Assert.Equal(ScreenshotPointerFeedback.SelectAnnotation, canvas.PointerFeedback);
            Assert.Same(AppCursorStyles.PointerCursor, canvas.Cursor);

            Click(window, new Point(45, 40));
            Assert.Equal(ScreenshotAnnotationTool.Select, canvas.ActiveAnnotationTool);
            Assert.IsType<ScreenshotArrowAnnotation>(canvas.SelectedAnnotation);
            Assert.Equal(ScreenshotPointerFeedback.SelectAnnotation, canvas.PointerFeedback);
            Assert.Same(AppCursorStyles.PointerCursor, canvas.Cursor);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SelectedRectangleShowsEightControlPointsAndResizesFromAnEdge()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(80, 80));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Rectangle);
            Drag(window, new Point(20, 20), new Point(50, 45));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Select);
            Click(window, new Point(20, 30));

            Assert.Equal(8, canvas.SelectedAnnotationControlPointCount);

            Drag(window, new Point(50, 32.5), new Point(65, 32.5));

            var resized = Assert.IsType<ScreenshotRectangleAnnotation>(
                Assert.Single(canvas.Annotations));
            Assert.Equal(new LogicalPoint(10, 10), resized.Start);
            Assert.Equal(new LogicalPoint(55, 35), resized.End);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SelectedRectangleBordersResizeFromAnywhereAlongEachEdge()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(90, 90));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Rectangle);
            Drag(window, new Point(20, 20), new Point(70, 70));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Select);
            Click(window, new Point(45, 20));

            foreach (var (point, feedback) in new[]
            {
                (new Point(30, 20), ScreenshotPointerFeedback.ResizeVertical),
                (new Point(70, 30), ScreenshotPointerFeedback.ResizeHorizontal),
                (new Point(60, 70), ScreenshotPointerFeedback.ResizeVertical),
                (new Point(20, 60), ScreenshotPointerFeedback.ResizeHorizontal),
            })
            {
                window.MouseMove(point, RawInputModifiers.None);
                Assert.Equal(feedback, canvas.PointerFeedback);
            }

            Drag(window, new Point(30, 20), new Point(30, 30));

            var resized = Assert.IsType<ScreenshotRectangleAnnotation>(
                Assert.Single(canvas.Annotations));
            Assert.Equal(new LogicalPoint(10, 20), resized.Start);
            Assert.Equal(new LogicalPoint(60, 60), resized.End);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SelectedArrowEndpointChangesItsLengthAndDirection()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(80, 80));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Arrow);
            var drawingCursor = canvas.Cursor;
            Drag(window, new Point(20, 20), new Point(50, 20));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Select);
            Click(window, new Point(35, 20));

            Assert.Equal(2, canvas.SelectedAnnotationControlPointCount);
            window.MouseMove(new Point(50, 20), RawInputModifiers.None);
            Assert.Equal(ScreenshotPointerFeedback.ResizeArrow, canvas.PointerFeedback);
            Assert.NotSame(drawingCursor, canvas.Cursor);

            Drag(window, new Point(50, 20), new Point(40, 50));

            var arrow = Assert.IsType<ScreenshotArrowAnnotation>(Assert.Single(canvas.Annotations));
            Assert.Equal(new LogicalPoint(10, 10), arrow.Start);
            Assert.Equal(new LogicalPoint(30, 40), arrow.End);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HoverFeedbackDistinguishesSelectionObjectsHandlesAndEditedBlankSpace()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(80, 80));
            window.MouseMove(new Point(70, 70), RawInputModifiers.None);
            Assert.Equal(ScreenshotPointerFeedback.MoveSelection, canvas.PointerFeedback);

            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Rectangle);
            Drag(window, new Point(20, 20), new Point(50, 45));
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Select);

            window.MouseMove(new Point(70, 70), RawInputModifiers.None);
            Assert.Equal(ScreenshotPointerFeedback.Default, canvas.PointerFeedback);

            window.MouseMove(new Point(20, 30), RawInputModifiers.None);
            Assert.Equal(ScreenshotPointerFeedback.SelectAnnotation, canvas.PointerFeedback);
            Assert.Same(AppCursorStyles.PointerCursor, canvas.Cursor);
            Click(window, new Point(20, 30));

            window.MouseMove(new Point(50, 32.5), RawInputModifiers.None);
            Assert.Equal(ScreenshotPointerFeedback.ResizeHorizontal, canvas.PointerFeedback);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SelectionStaysLockedAfterItsAnnotationsAreUndone()
    {
        using var frame = CreateHighDpiFrame();
        using var canvas = new ScreenshotSelectionCanvas(frame);
        var window = ShowCanvas(canvas);
        try
        {
            Drag(window, new Point(10, 10), new Point(80, 80));
            var originalSelection = canvas.Session.Selection;
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Rectangle);
            Drag(window, new Point(20, 20), new Point(50, 45));
            Assert.True(canvas.UndoAnnotation());
            Assert.Empty(canvas.Annotations);
            canvas.SelectAnnotationTool(ScreenshotAnnotationTool.Select);

            window.MouseMove(new Point(70, 70), RawInputModifiers.None);
            Assert.Equal(ScreenshotPointerFeedback.Default, canvas.PointerFeedback);
            Drag(window, new Point(70, 70), new Point(60, 60));

            Assert.Equal(originalSelection, canvas.Session.Selection);
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
