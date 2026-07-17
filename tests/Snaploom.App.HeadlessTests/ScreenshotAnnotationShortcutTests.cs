using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;
using Snaploom.Core;
using Snaploom.Platform.Abstractions;

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

        Drag(window, new Point(105, 125), new Point(205, 155));

        Assert.False(window.TextEditorVisible);
        Assert.Null(window.TextEdit);
        var text = Assert.IsType<ScreenshotTextAnnotation>(Assert.Single(window.Annotations));
        Assert.Equal(new LogicalPoint(150, 100), text.Origin);
        Assert.Equal("可拖拽文字", text.Text);
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

        window.KeyTextInput("一段会让输入框横向增长的文字");

        Assert.True(
            window.TextEditorVisualWidth > initialWidth,
            $"initial={initialWidth}, current={window.TextEditorVisualWidth}, text={window.TextEdit?.Text}");
        Assert.InRange(window.TextEditorVisualWidth, 20, 400);
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
            new Point(100, 296),
            MouseButton.Left,
            RawInputModifiers.LeftMouseButton);
        window.MouseUp(
            new Point(100, 296),
            MouseButton.Left,
            RawInputModifiers.None);

        Assert.True(window.TextEditorVisible);
        Assert.InRange(window.TextEditorVisualHeight, 1, 4);
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
