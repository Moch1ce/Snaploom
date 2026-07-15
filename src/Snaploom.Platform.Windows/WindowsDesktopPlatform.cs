using System.ComponentModel;
using System.Runtime.InteropServices;
using Snaploom.Core;
using Snaploom.Platform.Abstractions;

namespace Snaploom.Platform.Windows;

public sealed partial class WindowsDesktopPlatform :
    IDesktopPlatform,
    IPlatformProcessInitializer,
    IScreenCapturePermissionService,
    IGlobalScreenshotHotKeyService,
    IScreenCaptureService,
    IPngSaveDialogService,
    IScreenshotClipboardService,
    IScreenshotOverlayConfigurator,
    IDisposable
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private static readonly nint HwndTopmost = new(-1);

    private readonly WindowsGlobalHotKey _globalHotKey = new();
    private bool _disposed;

    public DesktopPlatformKind Kind => DesktopPlatformKind.Windows;

    public string ApplicationId => ProductIdentity.WindowsAppId;

    public void InitializeProcess()
    {
        EnsureWindows();
        var result = SetCurrentProcessExplicitAppUserModelId(ApplicationId);
        Marshal.ThrowExceptionForHR(result);
    }

    public ScreenCapturePermissionStatus GetPermissionStatus() => ScreenCapturePermissionStatus.Granted;

    public bool RequestPermission() => true;

    public void OpenPermissionSettings()
    {
    }

    public bool TryRegisterScreenshotHotKey(Action callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureWindows();
        return _globalHotKey.TryRegister(callback);
    }

    public void UnregisterScreenshotHotKey() => _globalHotKey.Unregister();

    public Task<CapturedScreen> CaptureCurrentDisplayAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureWindows();
#if WINDOWS
        return Task.Run(
            () => WindowsGraphicsCapture.CaptureCurrentDisplayAsync(cancellationToken),
            cancellationToken);
#else
        return Task.Run(
            () => WindowsDisplayCapture.CaptureCurrentDisplay(cancellationToken),
            cancellationToken);
#endif
    }

    public string? ShowSaveDialog(string suggestedFileName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureWindows();
        return WindowsPngSaveDialog.Show(suggestedFileName);
    }

    public void CopyPng(ReadOnlySpan<byte> png)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureWindows();
        WindowsClipboard.CopyPng(png);
    }

    public void CopyText(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureWindows();
        WindowsClipboard.CopyText(text);
    }

    public void ConfigureScreenshotOverlay(nint nativeWindowHandle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureWindows();
        ArgumentOutOfRangeException.ThrowIfEqual(nativeWindowHandle, 0);

        if (SetWindowPos(
                nativeWindowHandle,
                HwndTopmost,
                0,
                0,
                0,
                0,
                SwpNoSize | SwpNoMove | SwpNoActivate) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _globalHotKey.Dispose();
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The Windows desktop adapter can only run on Windows.");
        }
    }

    [LibraryImport(
        "shell32.dll",
        EntryPoint = "SetCurrentProcessExplicitAppUserModelID",
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelId(string applicationId);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
