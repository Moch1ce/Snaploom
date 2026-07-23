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
    IAutoStartService,
    ISystemResumeService,
    ISystemNotificationService,
    IFolderLauncher,
    IExternalUriLauncher,
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
    private readonly WindowsSystemResume _systemResume = new();
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

    public bool TryRegisterScreenshotHotKey(ScreenshotHotKey hotKey, Action callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureWindows();
        return _globalHotKey.TryRegister(hotKey, callback);
    }

    public void UnregisterScreenshotHotKey() => _globalHotKey.Unregister();

    public bool IsAutoStartEnabled()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows auto-start is only available on Windows.");
        }

        return WindowsAutoStart.IsEnabled();
    }

    public void SetAutoStartEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows auto-start is only available on Windows.");
        }

        WindowsAutoStart.SetEnabled(enabled);
    }

    public void StartMonitoring(Action callback)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureWindows();
        _systemResume.Start(callback);
    }

    public void StopMonitoring() => _systemResume.Stop();

    public void ShowNotification(string title, string message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureWindows();
        WindowsSystemNotification.Show(title, message);
    }

    public void OpenFolder(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureWindows();
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(path);
        _ = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
    }

    public void OpenUri(Uri uri)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureWindows();
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Only absolute HTTPS links can be opened.", nameof(uri));
        }

        _ = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true,
            });
    }

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

    public string? ShowSaveDialog(string suggestedFileName, string? initialDirectory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureWindows();
        return WindowsPngSaveDialog.Show(suggestedFileName, initialDirectory);
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
        _systemResume.Dispose();
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
