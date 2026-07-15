using Snaploom.Core;

namespace Snaploom.Platform.Abstractions;

public enum ScreenCapturePermissionStatus
{
    NotGranted,
    Granted,
}

public interface IScreenCapturePermissionService
{
    ScreenCapturePermissionStatus GetPermissionStatus();

    bool RequestPermission();

    void OpenPermissionSettings();
}

public interface IGlobalScreenshotHotKeyService
{
    bool TryRegisterScreenshotHotKey(Action callback);

    void UnregisterScreenshotHotKey();
}

public interface IScreenCaptureService
{
    Task<CapturedScreen> CaptureCurrentDisplayAsync(CancellationToken cancellationToken = default);
}

public interface IPngSaveDialogService
{
    string? ShowSaveDialog(string suggestedFileName, string? initialDirectory);
}

public interface IScreenshotClipboardService
{
    void CopyPng(ReadOnlySpan<byte> png);

    void CopyText(string text);
}

public interface IScreenshotOverlayConfigurator
{
    void ConfigureScreenshotOverlay(nint nativeWindowHandle);
}

public sealed class CapturedScreen : IDisposable
{
    public CapturedScreen(
        CapturedFrame frame,
        PhysicalPoint cursorPosition,
        PhysicalPoint? displayOrigin = null,
        IEnumerable<ScreenshotWindowCandidate>? windowCandidates = null)
    {
        ArgumentNullException.ThrowIfNull(frame);

        Frame = frame;
        CursorPosition = cursorPosition;
        DisplayOrigin = displayOrigin ?? new PhysicalPoint(0, 0);
        WindowCandidates = ScreenshotWindowSelector.GetEligibleWindows(
            windowCandidates ?? Array.Empty<ScreenshotWindowCandidate>(),
            frame.PhysicalSize);
    }

    public CapturedFrame Frame { get; }

    public PhysicalPoint CursorPosition { get; }

    public PhysicalPoint DisplayOrigin { get; }

    public PhysicalPoint GlobalCursorPosition => new(
        checked(DisplayOrigin.X + CursorPosition.X),
        checked(DisplayOrigin.Y + CursorPosition.Y));

    public IReadOnlyList<ScreenshotWindowCandidate> WindowCandidates { get; }

    public void Dispose() => Frame.Dispose();
}

public sealed class ScreenCaptureException : Exception
{
    public ScreenCaptureException(string message, bool permissionDenied = false)
        : base(message)
    {
        PermissionDenied = permissionDenied;
    }

    public bool PermissionDenied { get; }
}
