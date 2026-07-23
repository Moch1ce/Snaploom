using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Snaploom.Core;
using Snaploom.Platform.Abstractions;

namespace Snaploom.App.HeadlessTests;

public sealed class ScreenshotPreferenceTests
{
    [AvaloniaFact]
    public void RecentAnnotationStyleBecomesTheNextOverlayDefault()
    {
        var settings = AppSettingsService.CreateTransient(
            AppSettings.CreateDefault(DesktopPlatformKind.Windows) with
            {
                AnnotationColor = ScreenshotAnnotationColor.Blue,
                AnnotationLineWidth = 8,
            });
        var frame = CreateFrame();
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(20, 20));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new NullSaveDialog(),
            new NullClipboard(),
            new NullOverlayConfigurator(),
            settings);
        window.Show();
        Drag(window, new Point(20, 20), new Point(380, 260));
        window.KeyPress(Key.R, RawInputModifiers.None, PhysicalKey.R, "r");
        Drag(window, new Point(80, 80), new Point(180, 150));

        var rectangle = Assert.IsType<ScreenshotRectangleAnnotation>(Assert.Single(window.Annotations));
        Assert.Equal(ScreenshotAnnotationColor.Blue, rectangle.Style.Color);
        Assert.Equal(8, rectangle.Style.LineWidth);
    }

    private static CapturedFrame CreateFrame()
    {
        var pixels = new byte[400 * 300 * 4];
        for (var index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = byte.MaxValue;
        }

        return new CapturedFrame(
            new PhysicalSize(400, 300),
            new LogicalSize(400, 300),
            1600,
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
