using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using System.Runtime.InteropServices;
using Snaploom.Core;
using Snaploom.Platform.Abstractions;
using Snaploom.Rendering;

namespace Snaploom.App.HeadlessTests;

public sealed class ScreenshotAnnotationShortcutTests
{
    [AvaloniaFact]
    public void RAndAAndTSelectAnnotationToolsAndVReturnsToSelection()
    {
        var frame = CreateFrame();
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();

        window.MouseMove(new Point(10, 10), RawInputModifiers.None);
        window.MouseDown(
            new Point(10, 10),
            MouseButton.Left,
            RawInputModifiers.LeftMouseButton);
        window.MouseMove(new Point(70, 70), RawInputModifiers.LeftMouseButton);
        window.MouseUp(
            new Point(70, 70),
            MouseButton.Left,
            RawInputModifiers.None);

        window.KeyPress(Key.R, RawInputModifiers.None, PhysicalKey.R, "r");
        Assert.Equal(ScreenshotAnnotationTool.Rectangle, window.ActiveAnnotationTool);
        Assert.False(window.AnnotationOptionsFlyoutOpen);

        window.KeyPress(Key.V, RawInputModifiers.None, PhysicalKey.V, "v");
        Assert.Equal(ScreenshotAnnotationTool.Select, window.ActiveAnnotationTool);

        window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
        Assert.Equal(ScreenshotAnnotationTool.Arrow, window.ActiveAnnotationTool);

        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        Assert.Equal(ScreenshotAnnotationTool.Text, window.ActiveAnnotationTool);

        window.KeyPress(Key.M, RawInputModifiers.None, PhysicalKey.M, "m");
        Assert.Equal(ScreenshotAnnotationTool.Mosaic, window.ActiveAnnotationTool);

        window.KeyPress(Key.V, RawInputModifiers.None, PhysicalKey.V, "v");
        Assert.Equal(ScreenshotAnnotationTool.Select, window.ActiveAnnotationTool);
    }

    [AvaloniaFact]
    public void EscapeClosesTheOverlayImmediatelyFromAnActiveAnnotationTool()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 200));
        window.KeyPress(Key.R, RawInputModifiers.None, PhysicalKey.R, "r");

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

        Assert.False(window.IsVisible);
        Assert.False(window.OutputCompleted);
    }

    [AvaloniaFact]
    public void EscapeClosesTheOverlayImmediatelyWhileEditingText()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 200));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        Click(window, new Point(100, 120));
        Assert.True(window.TextEditorVisible);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

        Assert.False(window.IsVisible);
        Assert.False(window.OutputCompleted);
    }

    [AvaloniaFact]
    public void ClickingCommittedTextWithTheTextToolReopensAndUpdatesIt()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 200));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        Click(window, new Point(100, 120));
        window.TextEditor.Text = "原文";
        var commandModifier = OperatingSystem.IsMacOS()
            ? RawInputModifiers.Meta
            : RawInputModifiers.Control;
        window.KeyPress(Key.Enter, commandModifier, PhysicalKey.Enter, "\r");

        Click(window, new Point(105, 125));

        Assert.True(window.TextEditorVisible);
        Assert.Equal(0, window.TextEdit?.AnnotationIndex);
        Assert.Equal("原文", window.TextEditor.Text);

        window.TextEditor.Text = "修改后的文字";
        window.KeyPress(Key.Enter, commandModifier, PhysicalKey.Enter, "\r");

        var text = Assert.IsType<ScreenshotTextAnnotation>(Assert.Single(window.Annotations));
        Assert.Equal("修改后的文字", text.Text);
    }

    [AvaloniaFact]
    public void DraggingCommittedTextWithTheTextToolMovesItWithoutOpeningTheEditor()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 200));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        Click(window, new Point(100, 120));
        window.TextEditor.Text = "可拖拽文字";
        var commandModifier = OperatingSystem.IsMacOS()
            ? RawInputModifiers.Meta
            : RawInputModifiers.Control;
        window.KeyPress(Key.Enter, commandModifier, PhysicalKey.Enter, "\r");

        var canvas = Assert.Single(
            window.GetVisualDescendants().OfType<ScreenshotSelectionCanvas>());
        window.MouseMove(new Point(105, 125), RawInputModifiers.None);
        Assert.Equal(ScreenshotPointerFeedback.MoveAnnotation, canvas.PointerFeedback);
        Assert.NotSame(AppCursorStyles.PointerCursor, canvas.Cursor);

        window.MouseDown(
            new Point(105, 125),
            MouseButton.Left,
            RawInputModifiers.LeftMouseButton);
        Assert.Equal(ScreenshotPointerFeedback.MoveAnnotation, canvas.PointerFeedback);
        Assert.NotSame(AppCursorStyles.PointerCursor, canvas.Cursor);

        window.MouseMove(new Point(550, 250), RawInputModifiers.LeftMouseButton);
        Assert.Equal(ScreenshotPointerFeedback.MoveAnnotation, canvas.PointerFeedback);
        window.MouseUp(
            new Point(550, 250),
            MouseButton.Left,
            RawInputModifiers.None);

        Assert.False(window.TextEditorVisible);
        Assert.Null(window.TextEdit);
        var text = Assert.IsType<ScreenshotTextAnnotation>(Assert.Single(window.Annotations));
        Assert.Equal("可拖拽文字", text.Text);
        var textBounds = ScreenshotAnnotationRenderer.MeasureVisualBounds(text);
        var editorBounds = ScreenshotTextEditorLayout.Measure(
            text,
            new Rect(0, 0, 450, 150));
        Assert.InRange(textBounds.Left, 0, 450);
        Assert.InRange(textBounds.Top, 0, 150);
        Assert.Equal(450, editorBounds.Right, precision: 5);
        Assert.Equal(150, editorBounds.Bottom, precision: 5);
    }

    [AvaloniaFact]
    public void EditingTextAtTheSelectionEdgeKeepsTheEditorChromeInside()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        Click(window, new Point(100, 120));
        window.TextEditor.Text = "边缘文字";
        var commandModifier = OperatingSystem.IsMacOS()
            ? RawInputModifiers.Meta
            : RawInputModifiers.Control;
        window.KeyPress(Key.Enter, commandModifier, PhysicalKey.Enter, "\r");

        Drag(window, new Point(105, 125), new Point(550, 350));
        var text = Assert.IsType<ScreenshotTextAnnotation>(Assert.Single(window.Annotations));
        var bounds = ScreenshotAnnotationRenderer.MeasureText(text);
        Click(
            window,
            new Point(
                50 + bounds.X + (bounds.Width / 2),
                50 + bounds.Y + (bounds.Height / 2)));

        Assert.True(window.TextEditorVisible);
        var editorTopLeft = Assert.IsType<Point>(
            window.TextEditor.TranslatePoint(
                new Point(
                    -ScreenshotUiTheme.FloatingBorderThickness,
                    -ScreenshotUiTheme.FloatingBorderThickness),
                window));
        var editorBottomRight = Assert.IsType<Point>(
            window.TextEditor.TranslatePoint(
                new Point(
                    window.TextEditor.Bounds.Width +
                        ScreenshotUiTheme.FloatingBorderThickness,
                    window.TextEditor.Bounds.Height +
                        ScreenshotUiTheme.FloatingBorderThickness),
                window));

        Assert.InRange(editorTopLeft.X, 50, 500);
        Assert.InRange(editorTopLeft.Y, 50, 300);
        Assert.InRange(editorBottomRight.X, 50, 500);
        Assert.InRange(editorBottomRight.Y, 50, 300);
    }

    [AvaloniaFact]
    public void TextEditingDoesNotStartWhenTheSelectionCannotFitTheEditorChrome()
    {
        var frame = CreateFrame(width: 200, height: 160);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(80, 80));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");

        Click(window, new Point(65, 65));

        Assert.False(window.TextEditorVisible);
        Assert.Null(window.TextEdit);
        Assert.Empty(window.Annotations);
    }

    [AvaloniaFact]
    public void DraggingTheLastWrappedLineMovesTheWholeTextAnnotation()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        Click(window, new Point(100, 120));
        window.TextEditor.Text =
            "这是一段很长的文字用来验证自动换行后的最后几个字也可以按住并拖拽移动";
        var commandModifier = OperatingSystem.IsMacOS()
            ? RawInputModifiers.Meta
            : RawInputModifiers.Control;
        window.KeyPress(Key.Enter, commandModifier, PhysicalKey.Enter, "\r");
        var annotation = Assert.IsType<ScreenshotTextAnnotation>(
            Assert.Single(window.Annotations));
        var bounds = Snaploom.Rendering.ScreenshotAnnotationRenderer.MeasureText(annotation);
        var editorBounds = ScreenshotTextEditorLayout.Measure(
            annotation,
            new Rect(0, 0, 450, 250));
        Assert.True(bounds.Height > annotation.Style.FontSize * 1.25);
        var tailLinePoint = new Point(
            50 + bounds.X + (annotation.Style.FontSize / 2),
            50 + bounds.Y + bounds.Height - (annotation.Style.FontSize / 2));

        Drag(
            window,
            tailLinePoint,
            new Point(tailLinePoint.X + 50, tailLinePoint.Y + 20));

        Assert.False(window.TextEditorVisible);
        Assert.Null(window.TextEdit);
        var moved = Assert.IsType<ScreenshotTextAnnotation>(Assert.Single(window.Annotations));
        var movedBounds = ScreenshotAnnotationRenderer.MeasureVisualBounds(moved);
        var movedEditorBounds = ScreenshotTextEditorLayout.Measure(
            moved,
            new Rect(0, 0, 450, 250));
        Assert.Equal(
            new LogicalPoint(
                annotation.Origin.X + Math.Min(50, 450 - editorBounds.Right),
                annotation.Origin.Y + Math.Min(20, 250 - editorBounds.Bottom)),
            moved.Origin);
        Assert.InRange(movedBounds.Right, 0, 450);
        Assert.InRange(movedBounds.Bottom, 0, 250);
        Assert.InRange(movedEditorBounds.Right, 0, 450);
        Assert.InRange(movedEditorBounds.Bottom, 0, 250);
    }

    [AvaloniaFact]
    public void FirstBlankClickCommitsCurrentTextAndSecondClickStartsAnotherEditor()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        Click(window, new Point(100, 120));
        window.TextEditor.Text = "第一段文字";

        Click(window, new Point(400, 240));

        Assert.False(window.TextEditorVisible);
        Assert.Null(window.TextEdit);
        var committed = Assert.IsType<ScreenshotTextAnnotation>(
            Assert.Single(window.Annotations));
        Assert.Equal("第一段文字", committed.Text);

        Click(window, new Point(400, 240));

        Assert.True(window.TextEditorVisible);
        Assert.Equal(new LogicalPoint(350, 190), window.TextEdit?.Origin);
        Assert.Single(window.Annotations);
    }

    [AvaloniaFact]
    public void ClickingTheMaskDoesNotCommitAnActiveTextEditor()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        Click(window, new Point(100, 120));
        window.TextEditor.Text = "保持编辑";

        Click(window, new Point(20, 20));

        Assert.True(window.TextEditorVisible);
        Assert.Equal("保持编辑", window.TextEdit?.Text);
        Assert.Empty(window.Annotations);
    }

    [AvaloniaFact]
    public void FirstSelectionCornerClickCommitsTextWithoutResizing()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        Click(window, new Point(100, 120));
        window.TextEditor.Text = "提交后再缩放";
        var canvas = Assert.Single(
            window.GetVisualDescendants().OfType<ScreenshotSelectionCanvas>());
        var selectionBeforeClick = canvas.Session.Selection;

        Click(window, new Point(50, 50));

        Assert.False(window.TextEditorVisible);
        Assert.Null(window.TextEdit);
        Assert.Equal(selectionBeforeClick, canvas.Session.Selection);
        Assert.Equal(ScreenshotSessionState.Selected, canvas.Session.State);
        Assert.Equal(
            "提交后再缩放",
            Assert.IsType<ScreenshotTextAnnotation>(Assert.Single(window.Annotations)).Text);
    }

    [AvaloniaFact]
    public void ToolbarExposesAllStyleSelectionsForActiveAnnotationTools()
    {
        var toolbar = new ScreenshotToolbar();
        toolbar.SetSelectionActionsEnabled(isEnabled: true);

        toolbar.SelectTool(ScreenshotAnnotationTool.Rectangle);
        toolbar.SelectAnnotationStyle(
            new ScreenshotAnnotationStyle(ScreenshotAnnotationColor.White, 8));

        Assert.Equal(ScreenshotAnnotationTool.Rectangle, toolbar.ActiveTool);
        Assert.True(toolbar.AnnotationOptionsVisible);
        Assert.Equal(ScreenshotAnnotationColor.White, toolbar.AnnotationStyle.Color);
        Assert.Equal(8, toolbar.AnnotationStyle.LineWidth);

        toolbar.SelectTool(ScreenshotAnnotationTool.Select);
        Assert.False(toolbar.AnnotationOptionsVisible);

        toolbar.SelectTool(ScreenshotAnnotationTool.Text);
        toolbar.SelectTextStyle(
            new ScreenshotTextStyle(ScreenshotAnnotationColor.Green, 32));
        Assert.True(toolbar.AnnotationOptionsVisible);
        Assert.False(toolbar.LineWidthOptionsVisible);
        Assert.True(toolbar.FontSizeOptionsVisible);
        Assert.Equal(ScreenshotAnnotationColor.Green, toolbar.TextStyle.Color);
        Assert.Equal(32, toolbar.TextStyle.FontSize);

        toolbar.SelectTool(ScreenshotAnnotationTool.Mosaic);
        toolbar.SelectMosaicStyle(new ScreenshotMosaicStyle(64, 16));
        Assert.True(toolbar.AnnotationOptionsVisible);
        Assert.False(toolbar.ColorOptionsVisible);
        Assert.False(toolbar.LineWidthOptionsVisible);
        Assert.False(toolbar.FontSizeOptionsVisible);
        Assert.True(toolbar.MosaicBrushOptionsVisible);
        Assert.Equal(64, toolbar.MosaicStyle.BrushSize);
        Assert.Equal(16, toolbar.MosaicStyle.PixelSize);

        toolbar.SelectTool(ScreenshotAnnotationTool.Select);
        toolbar.SetSelectedAnnotation(new ScreenshotTextAnnotation(
            new LogicalPoint(4, 4),
            "selected",
            100,
            new ScreenshotTextStyle(ScreenshotAnnotationColor.Blue, 16)));
        Assert.True(toolbar.AnnotationOptionsVisible);
        Assert.True(toolbar.ColorOptionsVisible);
        Assert.True(toolbar.FontSizeOptionsVisible);
        Assert.Equal(ScreenshotAnnotationColor.Blue, toolbar.TextStyle.Color);
        Assert.Equal(16, toolbar.TextStyle.FontSize);
        toolbar.SetSelectedAnnotation(annotation: null);
        Assert.False(toolbar.AnnotationOptionsVisible);
    }

    [AvaloniaFact]
    public void ToolSettingsOpenWithoutChangingTheMainToolbarWidth()
    {
        var toolbar = new ScreenshotToolbar
        {
            IsVisible = true,
        };
        toolbar.SetSelectionActionsEnabled(isEnabled: true);
        toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var widthBefore = toolbar.DesiredSize.Width;

        toolbar.SelectTool(ScreenshotAnnotationTool.Rectangle);
        toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.True(toolbar.AnnotationOptionsVisible);
        Assert.Equal(widthBefore, toolbar.DesiredSize.Width);
    }

    [AvaloniaFact]
    public void RectangleSettingsFlyoutOpensWhenTheToolbarIsAttached()
    {
        var toolbar = new ScreenshotToolbar
        {
            IsVisible = true,
        };
        toolbar.SetSelectionActionsEnabled(isEnabled: true);
        var window = new Avalonia.Controls.Window
        {
            Width = 800,
            Height = 200,
            Content = toolbar,
        };
        try
        {
            window.Show();

            toolbar.SelectTool(ScreenshotAnnotationTool.Rectangle);

            Assert.False(toolbar.AnnotationOptionsFlyoutSuspended);
            Assert.True(toolbar.AnnotationOptionsFlyoutOpen);
            Assert.True(toolbar.ColorOptionsVisible);
            Assert.True(toolbar.LineWidthOptionsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RectangleSettingsFlyoutAppliesClickedColorAndLineWidth()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 200));
        var toolbarOrigin = window.ToolbarOrigin;
        Click(window, new Point(
            toolbarOrigin.X + ScreenshotUiTheme.FloatingBorderThickness +
            ScreenshotUiTheme.ToolbarHorizontalPadding +
            (ScreenshotUiTheme.ToolbarButtonSize / 2),
            toolbarOrigin.Y + (ScreenshotUiTheme.ToolbarHeight / 2)));

        Assert.Equal(ScreenshotAnnotationTool.Rectangle, window.ActiveAnnotationTool);
        Assert.True(window.AnnotationOptionsFlyoutOpen);

        var flyoutOrigin = window.AnnotationOptionsFlyoutOrigin;
        var optionCenterY = ScreenshotUiTheme.AnnotationOptionsPointerHeight -
            ScreenshotUiTheme.AnnotationOptionsPointerOverlap +
            (ScreenshotUiTheme.ToolbarHeight / 2);
        Click(window, new Point(
            flyoutOrigin.X + ScreenshotUiTheme.AnnotationOptionsSurfaceHorizontalPadding +
            (3.5 * ScreenshotUiTheme.ToolbarButtonSize),
            flyoutOrigin.Y + optionCenterY));
        Click(window, new Point(
            flyoutOrigin.X + ScreenshotUiTheme.AnnotationOptionsSurfaceHorizontalPadding +
            (6 * ScreenshotUiTheme.ToolbarButtonSize) +
            ScreenshotUiTheme.ToolbarSeparatorWidth +
            (2 * ScreenshotUiTheme.ToolbarSeparatorMargin) +
            (2.5 * ScreenshotUiTheme.ToolbarButtonSize),
            flyoutOrigin.Y + optionCenterY));

        Drag(window, new Point(100, 100), new Point(220, 160));

        var rectangle = Assert.IsType<ScreenshotRectangleAnnotation>(
            Assert.Single(window.Annotations));
        Assert.Equal(ScreenshotAnnotationColor.Blue, rectangle.Style.Color);
        Assert.Equal(8, rectangle.Style.LineWidth);
    }

    [AvaloniaFact]
    public void ClickingADrawnRectangleSelectsItAndAppliesStyleChanges()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 200));
        var toolbarOrigin = window.ToolbarOrigin;
        Click(window, new Point(
            toolbarOrigin.X + ScreenshotUiTheme.FloatingBorderThickness +
            ScreenshotUiTheme.ToolbarHorizontalPadding +
            (ScreenshotUiTheme.ToolbarButtonSize / 2),
            toolbarOrigin.Y + (ScreenshotUiTheme.ToolbarHeight / 2)));
        Drag(window, new Point(100, 100), new Point(220, 160));

        Click(window, new Point(100, 130));

        Assert.Equal(ScreenshotAnnotationTool.Select, window.ActiveAnnotationTool);
        Assert.IsType<ScreenshotRectangleAnnotation>(window.SelectedAnnotation);
        Assert.True(window.AnnotationOptionsFlyoutOpen);

        var flyoutOrigin = window.AnnotationOptionsFlyoutOrigin;
        var optionCenterY = ScreenshotUiTheme.AnnotationOptionsPointerHeight -
            ScreenshotUiTheme.AnnotationOptionsPointerOverlap +
            (ScreenshotUiTheme.ToolbarHeight / 2);
        Click(window, new Point(
            flyoutOrigin.X + ScreenshotUiTheme.AnnotationOptionsSurfaceHorizontalPadding +
            (3.5 * ScreenshotUiTheme.ToolbarButtonSize),
            flyoutOrigin.Y + optionCenterY));
        Click(window, new Point(
            flyoutOrigin.X + ScreenshotUiTheme.AnnotationOptionsSurfaceHorizontalPadding +
            (6 * ScreenshotUiTheme.ToolbarButtonSize) +
            ScreenshotUiTheme.ToolbarSeparatorWidth +
            (2 * ScreenshotUiTheme.ToolbarSeparatorMargin) +
            (2.5 * ScreenshotUiTheme.ToolbarButtonSize),
            flyoutOrigin.Y + optionCenterY));

        var rectangle = Assert.IsType<ScreenshotRectangleAnnotation>(
            Assert.Single(window.Annotations));
        Assert.Equal(ScreenshotAnnotationColor.Blue, rectangle.Style.Color);
        Assert.Equal(8, rectangle.Style.LineWidth);
    }

    [AvaloniaFact]
    public void RectangleToolDoesNotSelectAnExistingMosaicStroke()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 200));
        window.KeyPress(Key.M, RawInputModifiers.None, PhysicalKey.M, "m");
        Drag(window, new Point(100, 100), new Point(150, 100));
        window.KeyPress(Key.R, RawInputModifiers.None, PhysicalKey.R, "r");

        Click(window, new Point(125, 100));

        Assert.Equal(ScreenshotAnnotationTool.Rectangle, window.ActiveAnnotationTool);
        Assert.Null(window.SelectedAnnotation);
        Assert.IsType<ScreenshotMosaicAnnotation>(Assert.Single(window.Annotations));
    }

    [AvaloniaFact]
    public void OverlayTextEditorCommitsWithThePlatformModifierAndEnter()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();

        Drag(window, new Point(50, 50), new Point(500, 300));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        window.MouseDown(
            new Point(100, 120),
            MouseButton.Left,
            RawInputModifiers.LeftMouseButton);
        window.MouseUp(
            new Point(100, 120),
            MouseButton.Left,
            RawInputModifiers.None);

        Assert.True(
            window.TextEditorVisible,
            $"tool={window.ActiveAnnotationTool}, edit={window.TextEdit is not null}, annotations={window.Annotations.Count}");
        window.TextEditor.Text = "输入法 中文\nEnglish 123";
        var modifier = OperatingSystem.IsMacOS()
            ? RawInputModifiers.Meta
            : RawInputModifiers.Control;
        window.KeyPress(Key.Enter, modifier, PhysicalKey.Enter, "\r");

        Assert.False(window.TextEditorVisible);
        var text = Assert.IsType<ScreenshotTextAnnotation>(
            Assert.Single(window.Annotations));
        Assert.Equal("输入法 中文\nEnglish 123", text.Text);
    }

    [AvaloniaFact]
    public void TextEditorStartsCompactAndGrowsInsideItsSelection()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        window.MouseDown(
            new Point(100, 120),
            MouseButton.Left,
            RawInputModifiers.LeftMouseButton);
        window.MouseUp(
            new Point(100, 120),
            MouseButton.Left,
            RawInputModifiers.None);

        var initialWidth = window.TextEditorVisualWidth;
        Assert.InRange(initialWidth, 20, 48);
        Assert.Equal(4, window.TextEditorControlPointCount);
        Assert.Equal(0, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(window.TextEditor.Background).Color.A);
        Assert.Equal(new Thickness(8), window.TextEditor.Padding);
        Assert.Equal(VerticalAlignment.Top, window.TextEditor.VerticalContentAlignment);
        Assert.Equal(
            ScreenshotTextStyle.Default.FontSize *
            ScreenshotTextMetrics.LineHeightMultiplier,
            window.TextEditor.LineHeight);
        Assert.Equal(
            ScrollBarVisibility.Disabled,
            ScrollViewer.GetHorizontalScrollBarVisibility(window.TextEditor));
        Assert.Equal(
            ScrollBarVisibility.Disabled,
            ScrollViewer.GetVerticalScrollBarVisibility(window.TextEditor));
        Assert.Equal(
            new Point(100, 120),
            window.TextEditor.TranslatePoint(
                new Point(
                    window.TextEditor.Padding.Left,
                    window.TextEditor.Padding.Top),
                window));

        window.KeyTextInput("一段会让输入框横向增长的文字");

        Assert.True(
            window.TextEditorVisualWidth > initialWidth,
            $"initial={initialWidth}, current={window.TextEditorVisualWidth}, text={window.TextEdit?.Text}");
        Assert.InRange(window.TextEditorVisualWidth, 20, 400);
    }

    [AvaloniaFact]
    public void TextEditorDoesNotShowOrRespondToInternalScrolling()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        Click(window, new Point(100, 120));
        window.TextEditor.Text = string.Join(
            '\n',
            Enumerable.Range(1, 20).Select(index => $"第 {index} 行"));
        window.UpdateLayout();

        var scrollViewer = Assert.Single(
            window.TextEditor.GetVisualDescendants().OfType<ScrollViewer>());
        Assert.DoesNotContain(
            window.TextEditor.GetVisualDescendants().OfType<ScrollBar>(),
            scrollBar => scrollBar.IsVisible);
        var offset = scrollViewer.Offset;

        window.MouseWheel(
            new Point(105, 125),
            new Vector(0, -5),
            RawInputModifiers.None);
        window.UpdateLayout();

        Assert.Equal(offset, scrollViewer.Offset);
    }

    [AvaloniaFact]
    public void TextEditorKeepsItsFullContentWidthWhenTextReachesTheSelectionEdge()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        Click(window, new Point(100, 120));
        window.KeyTextInput(new string('W', 80));
        window.UpdateLayout();

        var edit = Assert.IsType<ScreenshotTextEdit>(window.TextEdit);
        var contentWidth = window.TextEditor.Bounds.Width -
            window.TextEditor.Padding.Left -
            window.TextEditor.Padding.Right;
        var measured = ScreenshotAnnotationRenderer.MeasureText(
            new ScreenshotTextAnnotation(
                edit.Origin,
                edit.Text,
                edit.MaxWidth,
                edit.Style));

        Assert.InRange(contentWidth, measured.Width, measured.Width + 1);
        Assert.Equal(
            new Point(100, 120),
            window.TextEditor.TranslatePoint(
                new Point(
                    window.TextEditor.Padding.Left,
                    window.TextEditor.Padding.Top),
                window));
    }

    [AvaloniaFact]
    public void TextEditorGrowsWhileTheInputMethodIsStillComposing()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        Click(window, new Point(100, 120));
        window.TextEditor.Text = "问";
        window.TextEditor.CaretIndex = window.TextEditor.Text.Length;
        var initialWidth = window.TextEditorVisualWidth;
        var request = new Avalonia.Input.TextInput.TextInputMethodClientRequestedEventArgs
        {
            RoutedEvent = InputElement.TextInputMethodClientRequestedEvent,
        };
        window.TextEditor.RaiseEvent(request);
        Assert.NotNull(request.Client);
        Assert.True(request.Client.SupportsPreedit);

        request.Client.SetPreeditText("js'j's");
        window.UpdateLayout();

        Assert.True(
            window.TextEditorVisualWidth > initialWidth,
            $"initial={initialWidth}, current={window.TextEditorVisualWidth}");
        Assert.Equal("问js'j's", request.Client.SurroundingText);
    }

    [AvaloniaFact]
    public void ConfirmingPinyinKeepsTheCaretAfterTheCommittedText()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        Click(window, new Point(100, 120));
        var request = new Avalonia.Input.TextInput.TextInputMethodClientRequestedEventArgs
        {
            RoutedEvent = InputElement.TextInputMethodClientRequestedEvent,
        };
        window.TextEditor.RaiseEvent(request);
        var client = Assert.IsAssignableFrom<
            Avalonia.Input.TextInput.TextInputMethodClient>(request.Client);

        client.SetPreeditText("wen", cursorPos: 3);
        window.UpdateLayout();
        client.SetPreeditText(string.Empty);
        window.KeyTextInput("文");
        window.UpdateLayout();

        client.SetPreeditText("zi", cursorPos: 2);
        window.UpdateLayout();
        var preeditWidth = window.TextEditorVisualWidth;
        client.SetPreeditText(string.Empty);
        window.KeyTextInput("字");
        window.UpdateLayout();

        Assert.Equal("文字", window.TextEditor.Text);
        Assert.Equal("文字".Length, window.TextEditor.CaretIndex);
        var scrollViewer = Assert.Single(
            window.TextEditor.GetVisualDescendants().OfType<ScrollViewer>());
        Assert.Equal(Vector.Zero, scrollViewer.Offset);
        Assert.True(
            client.CursorRectangle.X > window.TextEditor.Padding.Left,
            $"caret={window.TextEditor.CaretIndex}, cursor={client.CursorRectangle}");
        Assert.True(window.TextEditorVisualWidth >= preeditWidth);
    }

    [AvaloniaFact]
    public void FocusedTextEditorDoesNotRenderAThemeBlueBorder()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        Click(window, new Point(100, 120));

        using var renderedFrame = window.CaptureRenderedFrame();
        Assert.NotNull(renderedFrame);

        AssertNoStrongBluePixels(
            renderedFrame,
            new PixelRect(92, 112, 48, 48));
    }

    [AvaloniaFact]
    public void EditingCommittedTextDoesNotRenderAGreenSelectionBorder()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        Click(window, new Point(100, 120));
        window.TextEditor.Text = "已有文字";
        var commandModifier = OperatingSystem.IsMacOS()
            ? RawInputModifiers.Meta
            : RawInputModifiers.Control;
        window.KeyPress(Key.Enter, commandModifier, PhysicalKey.Enter, "\r");

        Click(window, new Point(105, 125));
        using var renderedFrame = window.CaptureRenderedFrame();
        Assert.NotNull(renderedFrame);

        AssertNoAccentPixels(
            renderedFrame,
            new PixelRect(98, 118, 110, 4));
    }

    [AvaloniaFact]
    public void CommittedTextShowsItsGreenBorderOnlyWhileBeingDragged()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        Click(window, new Point(100, 120));
        window.TextEditor.Text = "拖拽文字";
        var commandModifier = OperatingSystem.IsMacOS()
            ? RawInputModifiers.Meta
            : RawInputModifiers.Control;
        window.KeyPress(Key.Enter, commandModifier, PhysicalKey.Enter, "\r");

        window.MouseDown(
            new Point(105, 125),
            MouseButton.Left,
            RawInputModifiers.LeftMouseButton);
        window.MouseMove(
            new Point(205, 205),
            RawInputModifiers.LeftMouseButton);
        using var draggingFrame = window.CaptureRenderedFrame();
        Assert.NotNull(draggingFrame);
        AssertHasAccentPixels(
            draggingFrame,
            new PixelRect(60, 60, 430, 230));
        var draggingBorder = GetAccentPixelBounds(
            draggingFrame,
            new PixelRect(60, 60, 430, 230));

        window.MouseUp(
            new Point(205, 205),
            MouseButton.Left,
            RawInputModifiers.None);
        using var releasedFrame = window.CaptureRenderedFrame();
        Assert.NotNull(releasedFrame);
        AssertNoAccentPixels(
            releasedFrame,
            new PixelRect(60, 60, 430, 230));

        Click(window, new Point(205, 205));
        Assert.True(window.TextEditorVisible);
        var editorTopLeft = Assert.IsType<Point>(
            window.TextEditor.TranslatePoint(
                new Point(
                    -ScreenshotUiTheme.FloatingBorderThickness,
                    -ScreenshotUiTheme.FloatingBorderThickness),
                window));
        var editorRight = editorTopLeft.X + window.TextEditorVisualWidth;
        var editorBottom = editorTopLeft.Y + window.TextEditorVisualHeight;

        Assert.InRange(
            draggingBorder.X,
            (int)Math.Floor(editorTopLeft.X) - 1,
            (int)Math.Ceiling(editorTopLeft.X) + 1);
        Assert.InRange(
            draggingBorder.Y,
            (int)Math.Floor(editorTopLeft.Y) - 1,
            (int)Math.Ceiling(editorTopLeft.Y) + 1);
        Assert.InRange(
            draggingBorder.Right,
            (int)Math.Floor(editorRight) - 1,
            (int)Math.Ceiling(editorRight) + 1);
        Assert.InRange(
            draggingBorder.Bottom,
            (int)Math.Floor(editorBottom) - 1,
            (int)Math.Ceiling(editorBottom) + 1);
    }

    [AvaloniaFact]
    public void FocusedTextEditorCaretRendersInsideItsBorder()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        Click(window, new Point(100, 120));

        Assert.True(
            window.TextEditor.Bounds.Width <= window.TextEditorVisualWidth,
            $"editor={window.TextEditor.Bounds}, hostWidth={window.TextEditorVisualWidth}");
        using var renderedFrame = window.CaptureRenderedFrame();
        Assert.NotNull(renderedFrame);

        AssertAccentPixelsStayInside(
            renderedFrame,
            searchRegion: new PixelRect(70, 110, 70, 60),
            expectedRegion: new PixelRect(
                100,
                120,
                (int)Math.Ceiling(window.TextEditorVisualWidth),
                (int)Math.Ceiling(window.TextEditorVisualHeight)));
    }

    [AvaloniaFact]
    public void TextEditorStaysInsideTheSelectionNearItsBottomEdge()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        window.MouseDown(
            new Point(100, 292),
            MouseButton.Left,
            RawInputModifiers.LeftMouseButton);
        window.MouseUp(
            new Point(100, 292),
            MouseButton.Left,
            RawInputModifiers.None);

        Assert.True(window.TextEditorVisible);
        Assert.Equal(
            ScreenshotTextStyle.Default.FontSize *
                ScreenshotTextMetrics.LineHeightMultiplier +
                ScreenshotUiTheme.TextEditorMeasuredHeightPadding,
            window.TextEditorVisualHeight,
            precision: 3);
        var editorBottom = Assert.IsType<Point>(
            window.TextEditor.TranslatePoint(
                new Point(
                    0,
                    window.TextEditor.Bounds.Height +
                        ScreenshotUiTheme.FloatingBorderThickness),
                window));
        Assert.Equal(300, editorBottom.Y, precision: 3);
    }

    [AvaloniaFact]
    public void TextEditorHeightShrinksAfterDeletingANewline()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(10, 10));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        Click(window, new Point(100, 120));

        window.KeyTextInput("第一行");
        var singleLineHeight = window.TextEditorVisualHeight;
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        window.UpdateLayout();
        Assert.Equal("第一行\n", window.TextEditor.Text);
        Assert.True(window.TextEditorVisualHeight > singleLineHeight);

        window.KeyPress(
            Key.Back,
            RawInputModifiers.None,
            PhysicalKey.Backspace,
            null);
        window.UpdateLayout();

        Assert.Equal("第一行", window.TextEditor.Text);
        var edit = Assert.IsType<ScreenshotTextEdit>(window.TextEdit);
        Assert.False(edit.IsComposing);
        var measuredBounds = ScreenshotTextEditorLayout.Measure(
            edit,
            new Rect(50, 50, 450, 250));
        Assert.Equal(singleLineHeight, measuredBounds.Height, precision: 3);
        Assert.Equal(singleLineHeight, window.TextEditorVisualHeight, precision: 3);
    }

    [AvaloniaFact]
    public void ToolbarPositionFreezesAfterAnnotationStarts()
    {
        var frame = CreateFrame(width: 1000, height: 700);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(200, 200));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();

        Drag(window, new Point(100, 100), new Point(600, 400));
        window.KeyPress(Key.R, RawInputModifiers.None, PhysicalKey.R, "r");
        var annotationToolbarOrigin = window.ToolbarOrigin;

        Drag(window, new Point(150, 150), new Point(300, 250));
        window.KeyPress(Key.V, RawInputModifiers.None, PhysicalKey.V, "v");
        Drag(window, new Point(400, 300), new Point(500, 350));

        Assert.Equal(annotationToolbarOrigin, window.ToolbarOrigin);
    }

    [AvaloniaFact]
    public void DeleteUndoAndRedoEditTheSelectedAnnotation()
    {
        var frame = CreateFrame(width: 600, height: 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(100, 100));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator());
        window.Show();

        Drag(window, new Point(50, 50), new Point(500, 300));
        window.KeyPress(Key.R, RawInputModifiers.None, PhysicalKey.R, "r");
        Drag(window, new Point(100, 100), new Point(220, 180));
        window.KeyPress(Key.V, RawInputModifiers.None, PhysicalKey.V, "v");
        Drag(window, new Point(100, 140), new Point(110, 145));
        Assert.NotNull(window.SelectedAnnotation);

        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        Assert.Empty(window.Annotations);

        var commandModifier = OperatingSystem.IsMacOS()
            ? RawInputModifiers.Meta
            : RawInputModifiers.Control;
        window.KeyPress(Key.Z, commandModifier, PhysicalKey.Z, "z");
        Assert.Single(window.Annotations);
        window.KeyPress(
            Key.Z,
            commandModifier | RawInputModifiers.Shift,
            PhysicalKey.Z,
            "z");
        Assert.Empty(window.Annotations);
    }

    private static CapturedFrame CreateFrame(int width = 100, int height = 100)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = byte.MaxValue;
        }

        return new CapturedFrame(
            new PhysicalSize(width, height),
            new LogicalSize(width, height),
            stride: width * 4,
            pixels);
    }

    private static void Drag(ScreenshotOverlayWindow window, Point start, Point end)
    {
        window.MouseMove(start, RawInputModifiers.None);
        window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left, RawInputModifiers.None);
    }

    private static void Click(ScreenshotOverlayWindow window, Point point)
    {
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
    }

    private static void AssertNoStrongBluePixels(Bitmap bitmap, PixelRect region)
    {
        using var pixels = new WriteableBitmap(
            bitmap.PixelSize,
            bitmap.Dpi,
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using var framebuffer = pixels.Lock();
        bitmap.CopyPixels(framebuffer);
        var bytes = new byte[framebuffer.RowBytes * bitmap.PixelSize.Height];
        Marshal.Copy(framebuffer.Address, bytes, 0, bytes.Length);

        var bluePixelCount = 0;
        for (var y = region.Y; y < region.Bottom; y++)
        {
            for (var x = region.X; x < region.Right; x++)
            {
                var offset = (y * framebuffer.RowBytes) + (x * 4);
                var blue = bytes[offset];
                var green = bytes[offset + 1];
                var red = bytes[offset + 2];
                if (blue > 140 && blue > green + 20 && blue > red + 40)
                {
                    bluePixelCount++;
                }
            }
        }

        Assert.Equal(0, bluePixelCount);
    }

    private static void AssertNoAccentPixels(Bitmap bitmap, PixelRect region)
        => Assert.Equal(0, CountAccentPixels(bitmap, region));

    private static void AssertHasAccentPixels(Bitmap bitmap, PixelRect region) =>
        Assert.True(CountAccentPixels(bitmap, region) > 0);

    private static PixelRect GetAccentPixelBounds(Bitmap bitmap, PixelRect region)
    {
        var minimumX = int.MaxValue;
        var minimumY = int.MaxValue;
        var maximumX = int.MinValue;
        var maximumY = int.MinValue;
        VisitMatchingPixels(
            bitmap,
            region,
            static (blue, green, red) =>
                green > 100 && green > red + 20 && green > blue + 20,
            (x, y) =>
            {
                minimumX = Math.Min(minimumX, x);
                minimumY = Math.Min(minimumY, y);
                maximumX = Math.Max(maximumX, x);
                maximumY = Math.Max(maximumY, y);
            });

        Assert.True(minimumX <= maximumX && minimumY <= maximumY);
        return new PixelRect(
            minimumX,
            minimumY,
            maximumX - minimumX + 1,
            maximumY - minimumY + 1);
    }

    private static int CountAccentPixels(Bitmap bitmap, PixelRect region)
    {
        var accentPixelCount = 0;
        VisitMatchingPixels(
            bitmap,
            region,
            static (blue, green, red) =>
                green > 150 && green > red + 60 && green > blue + 40,
            (_, _) => accentPixelCount++);
        return accentPixelCount;
    }

    private static void VisitMatchingPixels(
        Bitmap bitmap,
        PixelRect region,
        Func<byte, byte, byte, bool> matches,
        Action<int, int> visit)
    {
        using var pixels = new WriteableBitmap(
            bitmap.PixelSize,
            bitmap.Dpi,
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using var framebuffer = pixels.Lock();
        bitmap.CopyPixels(framebuffer);
        var bytes = new byte[framebuffer.RowBytes * bitmap.PixelSize.Height];
        Marshal.Copy(framebuffer.Address, bytes, 0, bytes.Length);

        for (var y = region.Y; y < region.Bottom; y++)
        {
            for (var x = region.X; x < region.Right; x++)
            {
                var offset = (y * framebuffer.RowBytes) + (x * 4);
                var blue = bytes[offset];
                var green = bytes[offset + 1];
                var red = bytes[offset + 2];
                if (matches(blue, green, red))
                {
                    visit(x, y);
                }
            }
        }
    }

    private static void AssertAccentPixelsStayInside(
        Bitmap bitmap,
        PixelRect searchRegion,
        PixelRect expectedRegion)
    {
        using var pixels = new WriteableBitmap(
            bitmap.PixelSize,
            bitmap.Dpi,
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using var framebuffer = pixels.Lock();
        bitmap.CopyPixels(framebuffer);
        var bytes = new byte[framebuffer.RowBytes * bitmap.PixelSize.Height];
        Marshal.Copy(framebuffer.Address, bytes, 0, bytes.Length);

        var accentPoints = new List<PixelPoint>();
        for (var y = searchRegion.Y; y < searchRegion.Bottom; y++)
        {
            for (var x = searchRegion.X; x < searchRegion.Right; x++)
            {
                var offset = (y * framebuffer.RowBytes) + (x * 4);
                var blue = bytes[offset];
                var green = bytes[offset + 1];
                var red = bytes[offset + 2];
                if (green > 150 && green > red + 60 && green > blue + 40)
                {
                    accentPoints.Add(new PixelPoint(x, y));
                }
            }
        }

        Assert.NotEmpty(accentPoints);
        Assert.All(accentPoints, point =>
        {
            Assert.InRange(point.X, expectedRegion.X, expectedRegion.Right - 1);
            Assert.InRange(point.Y, expectedRegion.Y, expectedRegion.Bottom - 1);
        });
    }

    private sealed class NullSaveDialog : IPngSaveDialogService
    {
        public string? ShowSaveDialog(string suggestedFileName, string? initialDirectory) => null;
    }

    private sealed class NullClipboard : IScreenshotClipboardService
    {
        public void CopyPng(ReadOnlySpan<byte> png)
        {
        }

        public void CopyText(string text)
        {
        }
    }

    private sealed class NullOverlayConfigurator : IScreenshotOverlayConfigurator
    {
        public void ConfigureScreenshotOverlay(nint nativeWindowHandle)
        {
        }
    }
}
