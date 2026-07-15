using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
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

    private sealed class NullSaveDialog : IPngSaveDialogService
    {
        public string? ShowSaveDialog(string suggestedFileName) => null;
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
