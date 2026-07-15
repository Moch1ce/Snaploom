using System.ComponentModel;
using System.Runtime.InteropServices;
using Snaploom.Core;
using Snaploom.Platform.Abstractions;

namespace Snaploom.Platform.Windows;

internal static partial class WindowsDisplayCapture
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint DibRgbColors = 0;
    private const uint Srccopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000;
    private const int EffectiveDpi = 0;

    internal static CapturedScreen CaptureCurrentDisplay(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = GetCurrentDisplayTarget();
        var pixels = CapturePixels(target.X, target.Y, target.Width, target.Height, cancellationToken);
        var frame = new CapturedFrame(
            new PhysicalSize(target.Width, target.Height),
            target.LogicalSize,
            checked(target.Width * 4),
            pixels);
        return CreateCapturedScreen(frame, target);
    }

    internal static CapturedScreen CreateCapturedScreen(
        CapturedFrame frame,
        WindowsCaptureTarget target) =>
        new(
            frame,
            target.RelativeCursor,
            new PhysicalPoint(target.X, target.Y),
            WindowsWindowEnumerator.Enumerate(target));

    internal static WindowsCaptureTarget GetCurrentDisplayTarget()
    {
        if (GetCursorPos(out var cursor) == 0)
        {
            throw CreateCaptureException("Windows could not determine the display under the pointer.");
        }

        var monitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        if (monitor == 0)
        {
            throw CreateCaptureException("Windows could not find a display under the pointer.");
        }

        var monitorInfo = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfo(monitor, ref monitorInfo) == 0)
        {
            throw CreateCaptureException("Windows could not read the current display bounds.");
        }

        var bounds = monitorInfo.Monitor;
        var width = checked(bounds.Right - bounds.Left);
        var height = checked(bounds.Bottom - bounds.Top);
        if (width <= 0 || height <= 0)
        {
            throw new ScreenCaptureException("Windows returned invalid display bounds.");
        }

        var (dpiX, dpiY) = GetMonitorDpi(monitor);
        return new WindowsCaptureTarget(
            monitor,
            bounds.Left,
            bounds.Top,
            width,
            height,
            new LogicalSize(width * 96d / dpiX, height * 96d / dpiY),
            new PhysicalPoint(cursor.X - bounds.Left, cursor.Y - bounds.Top));
    }

    private static byte[] CapturePixels(
        int sourceX,
        int sourceY,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        var desktopContext = GetDC(0);
        if (desktopContext == 0)
        {
            throw CreateCaptureException("Windows could not access the interactive desktop.");
        }

        nint memoryContext = 0;
        nint bitmap = 0;
        nint previousObject = 0;
        try
        {
            memoryContext = CreateCompatibleDC(desktopContext);
            if (memoryContext == 0)
            {
                throw CreateCaptureException("Windows could not create a capture device context.");
            }

            var bitmapInfo = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                    SizeImage = checked((uint)(width * height * 4)),
                },
            };
            bitmap = CreateDIBSection(
                desktopContext,
                ref bitmapInfo,
                DibRgbColors,
                out var pixelPointer,
                0,
                0);
            if (bitmap == 0 || pixelPointer == 0)
            {
                throw CreateCaptureException("Windows could not allocate the screen capture buffer.");
            }

            previousObject = SelectObject(memoryContext, bitmap);
            if (previousObject == 0 || previousObject == new nint(-1))
            {
                throw CreateCaptureException("Windows could not select the screen capture buffer.");
            }

            if (BitBlt(
                    memoryContext,
                    0,
                    0,
                    width,
                    height,
                    desktopContext,
                    sourceX,
                    sourceY,
                    Srccopy | CaptureBlt) == 0)
            {
                throw CreateCaptureException(
                    "Windows blocked access to the current desktop. Secure desktop, lock screen, or protected content cannot be captured.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var pixels = new byte[checked(width * height * 4)];
            Marshal.Copy(pixelPointer, pixels, 0, pixels.Length);
            for (var alphaOffset = 3; alphaOffset < pixels.Length; alphaOffset += 4)
            {
                pixels[alphaOffset] = byte.MaxValue;
            }

            return pixels;
        }
        finally
        {
            if (previousObject != 0 && memoryContext != 0)
            {
                _ = SelectObject(memoryContext, previousObject);
            }

            if (bitmap != 0)
            {
                _ = DeleteObject(bitmap);
            }

            if (memoryContext != 0)
            {
                _ = DeleteDC(memoryContext);
            }

            _ = ReleaseDC(0, desktopContext);
        }
    }

    private static (uint X, uint Y) GetMonitorDpi(nint monitor)
    {
        try
        {
            return GetDpiForMonitor(monitor, EffectiveDpi, out var dpiX, out var dpiY) >= 0 &&
                   dpiX > 0 && dpiY > 0
                ? (dpiX, dpiY)
                : (96u, 96u);
        }
        catch (DllNotFoundException)
        {
            return (96, 96);
        }
        catch (EntryPointNotFoundException)
        {
            return (96, 96);
        }
    }

    private static ScreenCaptureException CreateCaptureException(string message) =>
        new($"{message} Win32 error: {new Win32Exception(Marshal.GetLastPInvokeError()).Message}");

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        internal uint Size;
        internal Rect Monitor;
        internal Rect WorkArea;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        internal uint Size;
        internal int Width;
        internal int Height;
        internal ushort Planes;
        internal ushort BitCount;
        internal uint Compression;
        internal uint SizeImage;
        internal int XPelsPerMeter;
        internal int YPelsPerMeter;
        internal uint ClrUsed;
        internal uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        internal BitmapInfoHeader Header;
        internal uint Colors;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(Point point, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    private static partial int GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetDC(nint windowHandle);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(nint windowHandle, nint deviceContext);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateCompatibleDC(nint deviceContext);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint CreateDIBSection(
        nint deviceContext,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out nint pixelPointer,
        nint section,
        uint offset);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial nint SelectObject(nint deviceContext, nint graphicsObject);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    private static partial int BitBlt(
        nint destination,
        int x,
        int y,
        int width,
        int height,
        nint source,
        int sourceX,
        int sourceY,
        uint rasterOperation);

    [LibraryImport("gdi32.dll")]
    private static partial int DeleteObject(nint graphicsObject);

    [LibraryImport("gdi32.dll")]
    private static partial int DeleteDC(nint deviceContext);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(
        nint monitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);
}

internal readonly record struct WindowsCaptureTarget(
    nint Monitor,
    int X,
    int Y,
    int Width,
    int Height,
    LogicalSize LogicalSize,
    PhysicalPoint RelativeCursor);
