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
