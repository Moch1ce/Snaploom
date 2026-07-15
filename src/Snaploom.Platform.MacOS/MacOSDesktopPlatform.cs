using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Snaploom.Core;
using Snaploom.Platform.Abstractions;

namespace Snaploom.Platform.MacOS;

public sealed class MacOSDesktopPlatform :
    IDesktopPlatform,
    IScreenCapturePermissionService,
    IGlobalScreenshotHotKeyService,
    IAutoStartService,
    ISystemResumeService,
    ISystemNotificationService,
    IFolderLauncher,
    IScreenCaptureService,
    IPngSaveDialogService,
    IScreenshotClipboardService,
    IScreenshotOverlayConfigurator,
    IDisposable
{
    private const uint CommandModifier = 1 << 8;
    private const uint ShiftModifier = 1 << 9;
    private const uint OptionModifier = 1 << 11;
    private const uint ControlModifier = 1 << 12;

    private static Action? s_hotKeyCallback;
    private static Action? s_resumeCallback;
    private bool _hotKeyRegistered;
    private bool _resumeMonitoring;
    private bool _disposed;

    public DesktopPlatformKind Kind => DesktopPlatformKind.MacOS;

    public string ApplicationId => ProductIdentity.MacOSBundleId;

    public ScreenCapturePermissionStatus GetPermissionStatus() =>
        MacOSNative.GetScreenCapturePermission() == 1
            ? ScreenCapturePermissionStatus.Granted
            : ScreenCapturePermissionStatus.NotGranted;

    public bool RequestPermission() => MacOSNative.RequestScreenCapturePermission() == 1;

    public void OpenPermissionSettings() => MacOSNative.OpenScreenCaptureSettings();

    public void ConfigureScreenshotOverlay(nint nativeWindowHandle)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(nativeWindowHandle, 0);
        MacOSNative.ConfigureCaptureOverlay(nativeWindowHandle);
    }

    public unsafe bool TryRegisterScreenshotHotKey(ScreenshotHotKey hotKey, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ObjectDisposedException.ThrowIf(_disposed, this);

        UnregisterScreenshotHotKey();
        s_hotKeyCallback = callback;
        _hotKeyRegistered = MacOSNative.RegisterScreenshotHotKey(
            GetKeyCode(hotKey.Key),
            GetModifiers(hotKey.Modifiers),
            &HandleHotKey) == 0;
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

    public bool IsAutoStartEnabled()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return MacOSNative.IsAutoStartEnabled() == 1;
    }

    public void SetAutoStartEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (MacOSNative.SetAutoStartEnabled(enabled ? 1 : 0) != 1)
        {
            throw new InvalidOperationException("macOS rejected the login item update.");
        }
    }

    public unsafe void StartMonitoring(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ObjectDisposedException.ThrowIf(_disposed, this);
        StopMonitoring();
        s_resumeCallback = callback;
        MacOSNative.StartResumeMonitoring(&HandleResume);
        _resumeMonitoring = true;
    }

    public void StopMonitoring()
    {
        if (_resumeMonitoring)
        {
            MacOSNative.StopResumeMonitoring();
            _resumeMonitoring = false;
        }

        s_resumeCallback = null;
    }

    public void ShowNotification(string title, string message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        MacOSNative.ShowSystemNotification(title, message);
    }

    public void OpenFolder(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(path);
        MacOSNative.OpenFolder(path);
    }

    public Task<CapturedScreen> CaptureCurrentDisplayAsync(CancellationToken cancellationToken = default) =>
        Task.Run(CaptureCurrentDisplay, cancellationToken);

    public string? ShowSaveDialog(string suggestedFileName, string? initialDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);

        var pathPointer = MacOSNative.ShowPngSavePanel(suggestedFileName, initialDirectory);
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

    public unsafe void CopyPng(ReadOnlySpan<byte> png)
    {
        ArgumentOutOfRangeException.ThrowIfZero(png.Length);
        fixed (byte* pngPointer = png)
        {
            if (MacOSNative.CopyPngToClipboard(pngPointer, (nuint)png.Length) != 1)
            {
                throw new InvalidOperationException("macOS rejected the PNG clipboard data.");
            }
        }
    }

    public void CopyText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (MacOSNative.CopyTextToClipboard(text) != 1)
        {
            throw new InvalidOperationException("macOS rejected the text clipboard data.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        UnregisterScreenshotHotKey();
        StopMonitoring();
        _disposed = true;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void HandleHotKey() => s_hotKeyCallback?.Invoke();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void HandleResume() => s_resumeCallback?.Invoke();

    private static uint GetModifiers(ScreenshotHotKeyModifiers modifiers)
    {
        var result = 0U;
        if (modifiers.HasFlag(ScreenshotHotKeyModifiers.Command))
        {
            result |= CommandModifier;
        }

        if (modifiers.HasFlag(ScreenshotHotKeyModifiers.Shift))
        {
            result |= ShiftModifier;
        }

        if (modifiers.HasFlag(ScreenshotHotKeyModifiers.Alt))
        {
            result |= OptionModifier;
        }

        if (modifiers.HasFlag(ScreenshotHotKeyModifiers.Control))
        {
            result |= ControlModifier;
        }

        return result;
    }

    private static uint GetKeyCode(ScreenshotHotKeyKey key) => key switch
    {
        ScreenshotHotKeyKey.A => 0,
        ScreenshotHotKeyKey.B => 11,
        ScreenshotHotKeyKey.C => 8,
        ScreenshotHotKeyKey.D => 2,
        ScreenshotHotKeyKey.E => 14,
        ScreenshotHotKeyKey.F => 3,
        ScreenshotHotKeyKey.G => 5,
        ScreenshotHotKeyKey.H => 4,
        ScreenshotHotKeyKey.I => 34,
        ScreenshotHotKeyKey.J => 38,
        ScreenshotHotKeyKey.K => 40,
        ScreenshotHotKeyKey.L => 37,
        ScreenshotHotKeyKey.M => 46,
        ScreenshotHotKeyKey.N => 45,
        ScreenshotHotKeyKey.O => 31,
        ScreenshotHotKeyKey.P => 35,
        ScreenshotHotKeyKey.Q => 12,
        ScreenshotHotKeyKey.R => 15,
        ScreenshotHotKeyKey.S => 1,
        ScreenshotHotKeyKey.T => 17,
        ScreenshotHotKeyKey.U => 32,
        ScreenshotHotKeyKey.V => 9,
        ScreenshotHotKeyKey.W => 13,
        ScreenshotHotKeyKey.X => 7,
        ScreenshotHotKeyKey.Y => 16,
        ScreenshotHotKeyKey.Z => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

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
            var windowCount = MacOSNative.GetFrameWindowCount(handle);
            var windowCandidates = new ScreenshotWindowCandidate[windowCount];
            for (var index = 0; index < windowCount; index++)
            {
                windowCandidates[index] = new ScreenshotWindowCandidate(
                    MacOSNative.GetFrameWindowId(handle, index),
                    new PhysicalRect(
                        MacOSNative.GetFrameWindowX(handle, index),
                        MacOSNative.GetFrameWindowY(handle, index),
                        MacOSNative.GetFrameWindowWidth(handle, index),
                        MacOSNative.GetFrameWindowHeight(handle, index)),
                    MacOSNative.GetFrameWindowZOrder(handle, index),
                    (ScreenshotWindowExclusion)MacOSNative.GetFrameWindowExclusion(handle, index));
            }

            return new CapturedScreen(
                frame,
                cursorPosition,
                new PhysicalPoint(
                    MacOSNative.GetFrameDisplayOriginX(handle),
                    MacOSNative.GetFrameDisplayOriginY(handle)),
                windowCandidates);
        }
        finally
        {
            MacOSNative.ReleaseFrame(handle);
        }
    }
}
