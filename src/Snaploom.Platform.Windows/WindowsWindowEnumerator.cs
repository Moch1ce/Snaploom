using System.Runtime.InteropServices;
using Snaploom.Core;

namespace Snaploom.Platform.Windows;

internal static unsafe partial class WindowsWindowEnumerator
{
    private const int GwlStyle = -16;
    private const int GwlExtendedStyle = -20;
    private const int DwmwaExtendedFrameBounds = 9;
    private const int DwmwaCloaked = 14;
    private const uint GwOwner = 4;
    private const long WsChild = 0x40000000L;
    private const long WsCaption = 0x00C00000L;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExLayered = 0x00080000L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint LayeredAlpha = 0x00000002;

    private static readonly HashSet<string> SystemWindowClasses = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "#32768",
        "DV2ControlHost",
        "MultitaskingViewFrame",
        "Progman",
        "Shell_SecondaryTrayWnd",
        "Shell_TrayWnd",
        "SysShadow",
        "TaskListThumbnailWnd",
        "tooltips_class32",
        "WorkerW",
        "XamlExplorerHostIslandWindow",
    };

    internal static IReadOnlyList<ScreenshotWindowCandidate> Enumerate(
        WindowsCaptureTarget target)
    {
        var candidates = new List<ScreenshotWindowCandidate>();
        var zOrder = 0;
        EnumWindows((windowHandle, _) =>
        {
            candidates.Add(CreateCandidate(windowHandle, zOrder++, target));
            return true;
        }, 0);
        return candidates;
    }

    private static ScreenshotWindowCandidate CreateCandidate(
        nint windowHandle,
        int zOrder,
        WindowsCaptureTarget target)
    {
        var exclusion = ScreenshotWindowExclusion.None;
        if (IsWindowVisible(windowHandle) == 0)
        {
            exclusion |= ScreenshotWindowExclusion.Hidden;
        }

        if (IsIconic(windowHandle) != 0)
        {
            exclusion |= ScreenshotWindowExclusion.Minimized;
        }

        _ = GetWindowThreadProcessId(windowHandle, out var processId);
        if (processId == Environment.ProcessId)
        {
            exclusion |= ScreenshotWindowExclusion.OwnApplication;
        }

        var style = GetWindowLongPtr(windowHandle, GwlStyle).ToInt64();
        var extendedStyle = GetWindowLongPtr(windowHandle, GwlExtendedStyle).ToInt64();
        var owner = GetWindow(windowHandle, GwOwner);
        if ((style & WsChild) != 0 ||
            IsCloaked(windowHandle) ||
            (owner != 0 && (style & WsCaption) == 0))
        {
            exclusion |= ScreenshotWindowExclusion.NonNormal;
        }

        var className = GetClassName(windowHandle);
        if (SystemWindowClasses.Contains(className))
        {
            exclusion |= ScreenshotWindowExclusion.SystemUi;
        }

        if ((extendedStyle & WsExToolWindow) != 0 ||
            ((extendedStyle & WsExNoActivate) != 0 && (style & WsCaption) == 0))
        {
            exclusion |= ScreenshotWindowExclusion.ToolWindow;
        }

        if ((extendedStyle & WsExTransparent) != 0)
        {
            exclusion |= ScreenshotWindowExclusion.ClickThrough;
        }

        if ((extendedStyle & WsExLayered) != 0 &&
            GetLayeredWindowAttributes(windowHandle, out _, out var alpha, out var flags) != 0 &&
            (flags & LayeredAlpha) != 0 &&
            alpha == 0)
        {
            exclusion |= ScreenshotWindowExclusion.Transparent;
        }

        var bounds = GetVisibleBounds(windowHandle);
        return new ScreenshotWindowCandidate(
            windowHandle.ToInt64(),
            new PhysicalRect(
                bounds.Left - target.X,
                bounds.Top - target.Y,
                bounds.Right - bounds.Left,
                bounds.Bottom - bounds.Top),
            zOrder,
            exclusion);
    }

    private static NativeRect GetVisibleBounds(nint windowHandle)
    {
        if (DwmGetWindowAttribute(
                windowHandle,
                DwmwaExtendedFrameBounds,
                out NativeRect bounds,
                Marshal.SizeOf<NativeRect>()) == 0)
        {
            return bounds;
        }

        return GetWindowRect(windowHandle, out bounds) != 0 ? bounds : default;
    }

    private static bool IsCloaked(nint windowHandle) =>
        DwmGetWindowAttribute(
            windowHandle,
            DwmwaCloaked,
            out int cloaked,
            sizeof(int)) == 0 &&
        cloaked != 0;

    private static string GetClassName(nint windowHandle)
    {
        Span<char> buffer = stackalloc char[256];
        fixed (char* bufferPointer = buffer)
        {
            var length = GetClassName(windowHandle, bufferPointer, buffer.Length);
            return length > 0 ? new string(buffer[..length]) : string.Empty;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    private delegate bool EnumWindowsCallback(nint windowHandle, nint parameter);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [LibraryImport("user32.dll")]
    private static partial int IsWindowVisible(nint windowHandle);

    [LibraryImport("user32.dll")]
    private static partial int IsIconic(nint windowHandle);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint windowHandle, int index);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int GetClassName(nint windowHandle, char* className, int maximumCount);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint windowHandle, uint command);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowRect(nint windowHandle, out NativeRect bounds);

    [LibraryImport("user32.dll")]
    private static partial int GetLayeredWindowAttributes(
        nint windowHandle,
        out uint colorKey,
        out byte alpha,
        out uint flags);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(
        nint windowHandle,
        int attribute,
        out NativeRect value,
        int valueSize);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(
        nint windowHandle,
        int attribute,
        out int value,
        int valueSize);
}
