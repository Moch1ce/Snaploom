using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Snaploom.Core;
using Snaploom.Platform.Abstractions;
using SkiaSharp;

namespace Snaploom.App.HeadlessTests;

public sealed class ScreenshotCompletionWorkflowTests
{
    [AvaloniaFact]
    public async Task CopyKeepsEditingStateAndSaveWritesTheIdenticalPng()
    {
        var previousDirectory = ScreenshotOverlayWindow.RememberedSaveDirectory;
        var directory = Path.Combine(Path.GetTempPath(), $"snaploom-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        ScreenshotOverlayWindow.RememberedSaveDirectory = null;
        var requestedPath = Path.Combine(directory, "saved-as.jpeg");
        var finalPath = Path.ChangeExtension(requestedPath, ".png");
        var saveDialog = new RecordingSaveDialog(requestedPath);
        var clipboard = new CapturingClipboard();
        var settings = AppSettingsService.CreateTransient(
            AppSettings.CreateDefault(
                OperatingSystem.IsMacOS()
                    ? DesktopPlatformKind.MacOS
                    : DesktopPlatformKind.Windows));
        var frame = CreateFrame(600, 400);
        try
        {
            using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(100, 100));
            using var window = new ScreenshotOverlayWindow(
                capturedScreen,
                saveDialog,
                clipboard,
                new NullOverlayConfigurator(),
                settings);
            window.Show();
            Drag(window, new Point(50, 50), new Point(500, 300));
            window.KeyPress(Key.R, RawInputModifiers.None, PhysicalKey.R, "r");
            Drag(window, new Point(100, 100), new Point(220, 180));

            window.KeyPress(
                Key.C,
                CommandModifier,
                PhysicalKey.C,
                "c");

            Assert.True(window.IsVisible);
            Assert.NotEmpty(clipboard.Png);
            Assert.Single(window.Annotations);
            Assert.True(window.CanUndo);
            Assert.Empty(Directory.EnumerateFiles(directory));

            window.KeyPress(
                Key.S,
                CommandModifier,
                PhysicalKey.S,
                "s");
            await WaitForAsync(() =>
                File.Exists(finalPath) && !window.IsVisible && window.OutputCompleted);

            Assert.Matches(
                @"^Snaploom_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}\.png$",
                saveDialog.SuggestedFileName);
            Assert.Null(saveDialog.InitialDirectory);
            Assert.Equal(directory, ScreenshotOverlayWindow.RememberedSaveDirectory);
            Assert.Equal(directory, settings.Current.LastSaveDirectory);
            Assert.Equal(clipboard.Png, await File.ReadAllBytesAsync(finalPath));
            Assert.Throws<ObjectDisposedException>(() => _ = frame.Pixels);
        }
        finally
        {
            ScreenshotOverlayWindow.RememberedSaveDirectory = previousDirectory;
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaFact]
    public void CancelingSavePreservesSelectionAnnotationsAndHistory()
    {
        var previousDirectory = ScreenshotOverlayWindow.RememberedSaveDirectory;
        ScreenshotOverlayWindow.RememberedSaveDirectory = "/remembered/screenshots";
        var saveDialog = new RecordingSaveDialog(path: null);
        var frame = CreateFrame(600, 400);
        try
        {
            using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(100, 100));
            using var window = new ScreenshotOverlayWindow(
                capturedScreen,
                saveDialog,
                new CapturingClipboard(),
                new NullOverlayConfigurator());
            window.Show();
            Drag(window, new Point(50, 50), new Point(500, 300));
            window.KeyPress(Key.R, RawInputModifiers.None, PhysicalKey.R, "r");
            Drag(window, new Point(100, 100), new Point(220, 180));

            window.KeyPress(Key.S, CommandModifier, PhysicalKey.S, "s");

            Assert.True(window.IsVisible);
            Assert.Single(window.Annotations);
            Assert.True(window.CanUndo);
            Assert.Equal("/remembered/screenshots", saveDialog.InitialDirectory);
        }
        finally
        {
            ScreenshotOverlayWindow.RememberedSaveDirectory = previousDirectory;
        }
    }

    [AvaloniaFact]
    public void SaveDialogIsNotCoveredAndCancelRestoresTheOverlay()
    {
        ScreenshotOverlayWindow? overlay = null;
        var saveDialog = new VisibilityCheckingSaveDialog(
            () => overlay?.IsVisible == true,
            () => overlay?.AnnotationOptionsFlyoutSuspended == true);
        var frame = CreateFrame(600, 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(100, 100));
        using var window = overlay = new ScreenshotOverlayWindow(
            capturedScreen,
            saveDialog,
            new CapturingClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));
        var toolbarOrigin = window.ToolbarOrigin;
        Click(window, new Point(
            toolbarOrigin.X + ScreenshotUiTheme.FloatingBorderThickness +
            ScreenshotUiTheme.ToolbarHorizontalPadding +
            (ScreenshotUiTheme.ToolbarButtonSize / 2),
            toolbarOrigin.Y + (ScreenshotUiTheme.ToolbarHeight / 2)));
        Assert.True(window.AnnotationOptionsFlyoutOpen);

        window.KeyPress(Key.S, CommandModifier, PhysicalKey.S, "s");

        Assert.False(saveDialog.OverlayWasVisible);
        Assert.True(saveDialog.AnnotationOptionsFlyoutWasSuspended);
        Assert.True(window.IsVisible);
        Assert.False(window.AnnotationOptionsFlyoutSuspended);
        Assert.True(window.AnnotationOptionsFlyoutOpen);
    }

    [AvaloniaFact]
    public void CancelingSaveRestoresTheActiveTextDraftWithoutAddingHistory()
    {
        var frame = CreateFrame(600, 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(100, 100));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new RecordingSaveDialog(path: null),
            new CapturingClipboard(),
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));
        window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
        Click(window, new Point(100, 120));
        window.KeyTextInput("未提交草稿");

        window.KeyPress(Key.S, CommandModifier, PhysicalKey.S, "s");

        Assert.True(window.IsVisible);
        Assert.True(window.TextEditorVisible);
        Assert.Equal(ScreenshotAnnotationTool.Text, window.ActiveAnnotationTool);
        Assert.Equal("未提交草稿", window.TextEdit?.Text);
        Assert.Empty(window.Annotations);
        Assert.False(window.CanUndo);
    }

    [AvaloniaFact]
    public async Task SavingCommitsAnActiveTextDraftWhileTheOverlayIsHidden()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"snaploom-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "text-draft.png");
        var frame = CreateFrame(600, 400);
        try
        {
            using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(100, 100));
            using var window = new ScreenshotOverlayWindow(
                capturedScreen,
                new RecordingSaveDialog(path),
                new CapturingClipboard(),
                new NullOverlayConfigurator());
            window.Show();
            Drag(window, new Point(50, 50), new Point(500, 300));
            window.KeyPress(Key.T, RawInputModifiers.None, PhysicalKey.T, "t");
            Click(window, new Point(100, 120));
            window.KeyTextInput("保存文字草稿");

            window.KeyPress(Key.S, CommandModifier, PhysicalKey.S, "s");
            await WaitForAsync(() =>
                File.Exists(path) && !window.IsVisible && window.OutputCompleted);

            Assert.True(window.OutputCompleted);
            using var bitmap = SKBitmap.Decode(path);
            Assert.Contains(bitmap.Pixels, pixel => pixel.Red > pixel.Green);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaFact]
    public void EnterCopiesPhysicalPixelsAndExitsAtTwoHundredPercentDpi()
    {
        var pixels = new byte[400 * 200 * 4];
        for (var index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = byte.MaxValue;
        }

        var frame = new CapturedFrame(
            new PhysicalSize(400, 200),
            new LogicalSize(200, 100),
            1600,
            pixels);
        var clipboard = new CapturingClipboard();
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(20, 20));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new RecordingSaveDialog(path: null),
            clipboard,
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(20, 20), new Point(180, 80));

        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");

        Assert.False(window.IsVisible);
        Assert.True(window.OutputCompleted);
        using var bitmap = SKBitmap.Decode(clipboard.Png);
        Assert.Equal(320, bitmap.Width);
        Assert.Equal(120, bitmap.Height);
        Assert.Empty(window.Annotations);
        Assert.Throws<ObjectDisposedException>(() => _ = frame.Pixels);
    }

    [AvaloniaFact]
    public void DoubleClickingTheSelectionCopiesAndExits()
    {
        var frame = CreateFrame(600, 400);
        var clipboard = new CapturingClipboard();
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(100, 100));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new RecordingSaveDialog(path: null),
            clipboard,
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));

        Click(window, new Point(200, 160));
        window.MouseDown(
            new Point(200, 160),
            MouseButton.Left,
            RawInputModifiers.LeftMouseButton);

        Assert.False(window.IsVisible);
        Assert.NotEmpty(clipboard.Png);
    }

    [AvaloniaFact]
    public void ShownOverlaySignalsThatItIsInteractive()
    {
        var frame = CreateFrame(600, 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(100, 100));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new RecordingSaveDialog(path: null),
            new CapturingClipboard(),
            new NullOverlayConfigurator());
        var interactiveRaised = false;
        window.Interactive += (_, _) => interactiveRaised = true;

        window.Show();

        Assert.True(interactiveRaised);
    }

    [AvaloniaFact]
    public void ClosingWithoutCopyOrSaveDoesNotCompleteAnOutputCycle()
    {
        var frame = CreateFrame(600, 400);
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(100, 100));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new RecordingSaveDialog(path: null),
            new CapturingClipboard(),
            new NullOverlayConfigurator());

        window.Show();
        window.Close();

        Assert.False(window.OutputCompleted);
    }

    [AvaloniaFact]
    public void RightClickExitsWithoutCopyingOrSaving()
    {
        var frame = CreateFrame(600, 400);
        var clipboard = new CapturingClipboard();
        using var capturedScreen = new CapturedScreen(frame, new PhysicalPoint(100, 100));
        using var window = new ScreenshotOverlayWindow(
            capturedScreen,
            new RecordingSaveDialog(path: null),
            clipboard,
            new NullOverlayConfigurator());
        window.Show();
        Drag(window, new Point(50, 50), new Point(500, 300));

        window.MouseDown(
            new Point(200, 160),
            MouseButton.Right,
            RawInputModifiers.RightMouseButton);

        Assert.False(window.IsVisible);
        Assert.False(window.OutputCompleted);
        Assert.Empty(clipboard.Png);
    }

    private static RawInputModifiers CommandModifier => OperatingSystem.IsMacOS()
        ? RawInputModifiers.Meta
        : RawInputModifiers.Control;

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("Timed out waiting for the screenshot workflow to complete.");
    }

    private static CapturedFrame CreateFrame(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = byte.MaxValue;
        }

        return new CapturedFrame(
            new PhysicalSize(width, height),
            new LogicalSize(width, height),
            width * 4,
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

    private sealed class RecordingSaveDialog(string? path) : IPngSaveDialogService
    {
        public string SuggestedFileName { get; private set; } = string.Empty;

        public string? InitialDirectory { get; private set; }

        public string? ShowSaveDialog(string suggestedFileName, string? initialDirectory)
        {
            SuggestedFileName = suggestedFileName;
            InitialDirectory = initialDirectory;
            return path;
        }
    }

    private sealed class VisibilityCheckingSaveDialog(
        Func<bool> isOverlayVisible,
        Func<bool> isAnnotationOptionsFlyoutSuspended)
        : IPngSaveDialogService
    {
        public bool OverlayWasVisible { get; private set; }

        public bool AnnotationOptionsFlyoutWasSuspended { get; private set; }

        public string? ShowSaveDialog(string suggestedFileName, string? initialDirectory)
        {
            OverlayWasVisible = isOverlayVisible();
            AnnotationOptionsFlyoutWasSuspended = isAnnotationOptionsFlyoutSuspended();
            return null;
        }
    }

    private sealed class CapturingClipboard : IScreenshotClipboardService
    {
        public byte[] Png { get; private set; } = [];

        public void CopyPng(ReadOnlySpan<byte> png) => Png = png.ToArray();

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
