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
    string? ShowSaveDialog(string suggestedFileName);
}

public interface IScreenshotOverlayConfigurator
{
    void ConfigureScreenshotOverlay(nint nativeWindowHandle);
}

public sealed class CapturedScreen : IDisposable
{
    public CapturedScreen(CapturedFrame frame, PhysicalPoint cursorPosition)
    {
        ArgumentNullException.ThrowIfNull(frame);

        Frame = frame;
        CursorPosition = cursorPosition;
    }

    public CapturedFrame Frame { get; }

    public PhysicalPoint CursorPosition { get; }

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
