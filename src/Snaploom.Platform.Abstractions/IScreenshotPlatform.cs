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
    bool TryRegisterScreenshotHotKey(ScreenshotHotKey hotKey, Action callback);

    void UnregisterScreenshotHotKey();
}

[Flags]
public enum ScreenshotHotKeyModifiers
{
    None = 0,
    Alt = 1 << 0,
    Control = 1 << 1,
    Shift = 1 << 2,
    Command = 1 << 3,
}

public enum ScreenshotHotKeyKey
{
    A,
    B,
    C,
    D,
    E,
    F,
    G,
    H,
    I,
    J,
    K,
    L,
    M,
    N,
    O,
    P,
    Q,
    R,
    S,
    T,
    U,
    V,
    W,
    X,
    Y,
    Z,
}

public readonly record struct ScreenshotHotKey
{
    public ScreenshotHotKey(ScreenshotHotKeyModifiers modifiers, ScreenshotHotKeyKey key)
    {
        if (modifiers == ScreenshotHotKeyModifiers.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(modifiers),
                "A global screenshot hot key requires at least one modifier.");
        }

        Modifiers = modifiers;
        Key = key;
    }

    public ScreenshotHotKeyModifiers Modifiers { get; }

    public ScreenshotHotKeyKey Key { get; }

    public override string ToString()
    {
        List<string> parts = [];
        if (Modifiers.HasFlag(ScreenshotHotKeyModifiers.Command))
        {
            parts.Add("Command");
        }

        if (Modifiers.HasFlag(ScreenshotHotKeyModifiers.Control))
        {
            parts.Add("Control");
        }

        if (Modifiers.HasFlag(ScreenshotHotKeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(ScreenshotHotKeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        parts.Add(Key.ToString());
        return string.Join('+', parts);
    }
}

public static class ScreenshotHotKeyDefaults
{
    public static ScreenshotHotKey For(DesktopPlatformKind platform) => platform switch
    {
        DesktopPlatformKind.Windows => new ScreenshotHotKey(
            ScreenshotHotKeyModifiers.Alt | ScreenshotHotKeyModifiers.Shift,
            ScreenshotHotKeyKey.A),
        DesktopPlatformKind.MacOS => new ScreenshotHotKey(
            ScreenshotHotKeyModifiers.Command | ScreenshotHotKeyModifiers.Shift,
            ScreenshotHotKeyKey.A),
        _ => throw new ArgumentOutOfRangeException(nameof(platform)),
    };
}

public interface IAutoStartService
{
    bool IsAutoStartEnabled();

    void SetAutoStartEnabled(bool enabled);
}

public interface ISystemResumeService
{
    void StartMonitoring(Action callback);

    void StopMonitoring();
}

public interface ISystemNotificationService
{
    void ShowNotification(string title, string message);
}

public interface IFolderLauncher
{
    void OpenFolder(string path);
}

public interface IExternalUriLauncher
{
    void OpenUri(Uri uri);
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
