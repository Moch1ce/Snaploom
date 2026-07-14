using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Snaploom.Core;
using Snaploom.Platform.Abstractions;

namespace Snaploom.Platform.MacOS;

public sealed class MacOSDesktopPlatform :
    IDesktopPlatform,
    IScreenCapturePermissionService,
    IGlobalScreenshotHotKeyService,
    IScreenCaptureService,
    IPngSaveDialogService,
    IDisposable
{
    private static Action? s_hotKeyCallback;
    private bool _hotKeyRegistered;

    public DesktopPlatformKind Kind => DesktopPlatformKind.MacOS;

    public string ApplicationId => ProductIdentity.MacOSBundleId;

    public ScreenCapturePermissionStatus GetPermissionStatus() =>
        MacOSNative.GetScreenCapturePermission() == 1
            ? ScreenCapturePermissionStatus.Granted
            : ScreenCapturePermissionStatus.NotGranted;

    public bool RequestPermission() => MacOSNative.RequestScreenCapturePermission() == 1;

    public void OpenPermissionSettings() => MacOSNative.OpenScreenCaptureSettings();

    public unsafe bool TryRegisterScreenshotHotKey(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        UnregisterScreenshotHotKey();
        s_hotKeyCallback = callback;
        _hotKeyRegistered = MacOSNative.RegisterScreenshotHotKey(&HandleHotKey) == 0;
        if (!_hotKeyRegistered)
        {
            s_hotKeyCallback = null;
        }

        return _hotKeyRegistered;
    }

    public void UnregisterScreenshotHotKey()
    {
        if (_hotKeyRegistered)
        {
            MacOSNative.UnregisterScreenshotHotKey();
            _hotKeyRegistered = false;
        }

        s_hotKeyCallback = null;
    }

    public Task<CapturedScreen> CaptureCurrentDisplayAsync(CancellationToken cancellationToken = default) =>
        Task.Run(CaptureCurrentDisplay, cancellationToken);

    public string? ShowSaveDialog(string suggestedFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);

        var pathPointer = MacOSNative.ShowPngSavePanel(suggestedFileName);
        if (pathPointer == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(pathPointer);
        }
        finally
        {
            MacOSNative.ReleaseString(pathPointer);
        }
    }

    public void Dispose() => UnregisterScreenshotHotKey();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void HandleHotKey() => s_hotKeyCallback?.Invoke();

    private static CapturedScreen CaptureCurrentDisplay()
    {
        var handle = MacOSNative.CaptureCurrentDisplay();
        if (handle == 0)
        {
            throw new ScreenCaptureException("The macOS capture bridge returned no result.");
        }

        try
        {
            var status = MacOSNative.GetFrameStatus(handle);
            if (status != 0)
            {
                var messagePointer = MacOSNative.GetFrameErrorMessage(handle);
                var message = messagePointer == 0
                    ? "macOS could not capture the current display."
                    : Marshal.PtrToStringUTF8(messagePointer) ?? "macOS could not capture the current display.";
                throw new ScreenCaptureException(message, permissionDenied: status == 1);
            }

            var width = MacOSNative.GetFrameWidth(handle);
            var height = MacOSNative.GetFrameHeight(handle);
            var stride = MacOSNative.GetFrameStride(handle);
            var pixelLength = checked((int)MacOSNative.GetFramePixelLength(handle));
            var pixels = new byte[pixelLength];
            Marshal.Copy(MacOSNative.GetFramePixelData(handle), pixels, startIndex: 0, pixelLength);

            var frame = new CapturedFrame(
                new PhysicalSize(width, height),
                new LogicalSize(
                    MacOSNative.GetFrameLogicalWidth(handle),
                    MacOSNative.GetFrameLogicalHeight(handle)),
                stride,
                pixels);
            var cursorPosition = new PhysicalPoint(
                checked((int)Math.Round(MacOSNative.GetFrameCursorX(handle))),
                checked((int)Math.Round(MacOSNative.GetFrameCursorY(handle))));
            return new CapturedScreen(frame, cursorPosition);
        }
        finally
        {
            MacOSNative.ReleaseFrame(handle);
        }
    }
}
