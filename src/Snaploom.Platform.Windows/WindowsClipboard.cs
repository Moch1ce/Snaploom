using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Snaploom.Platform.Windows;

internal static unsafe partial class WindowsClipboard
{
    private const uint GlobalMoveable = 0x0002;
    private const uint GlobalZeroInitialize = 0x0040;
    private const uint UnicodeTextFormat = 13;

    internal static void CopyPng(ReadOnlySpan<byte> png)
    {
        ArgumentOutOfRangeException.ThrowIfZero(png.Length);
        var pngFormat = RegisterClipboardFormat("PNG");
        if (pngFormat == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        CopyBytes(pngFormat, png);
    }

    internal static void CopyText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var byteLength = checked((text.Length + 1) * sizeof(char));
        var memory = GlobalAlloc(GlobalMoveable | GlobalZeroInitialize, (nuint)byteLength);
        if (memory == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        try
        {
            var destination = GlobalLock(memory);
            if (destination == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            try
            {
                fixed (char* source = text)
                {
                    Buffer.MemoryCopy(source, (void*)destination, byteLength, text.Length * sizeof(char));
                }
            }
            finally
            {
                _ = GlobalUnlock(memory);
            }

            TransferToClipboard(UnicodeTextFormat, ref memory);
        }
        finally
        {
            if (memory != 0)
            {
                GlobalFree(memory);
            }
        }
    }

    private static void CopyBytes(uint format, ReadOnlySpan<byte> bytes)
    {
        var memory = GlobalAlloc(GlobalMoveable, (nuint)bytes.Length);
        if (memory == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        try
        {
            var destination = GlobalLock(memory);
            if (destination == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            try
            {
                fixed (byte* source = bytes)
                {
                    Buffer.MemoryCopy(source, (void*)destination, bytes.Length, bytes.Length);
                }
            }
            finally
            {
                _ = GlobalUnlock(memory);
            }

            TransferToClipboard(format, ref memory);
        }
        finally
        {
            if (memory != 0)
            {
                GlobalFree(memory);
            }
        }
    }

    private static void TransferToClipboard(uint format, ref nint memory)
    {
        OpenClipboardWithRetry();
        try
        {
            if (EmptyClipboard() == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            if (SetClipboardData(format, memory) == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            memory = 0;
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    private static void OpenClipboardWithRetry()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(0) != 0)
            {
                return;
            }

            Thread.Sleep(20);
        }

        throw new Win32Exception(Marshal.GetLastPInvokeError(), "The Windows clipboard is currently in use.");
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormat(string format);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int OpenClipboard(nint owner);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetClipboardData(uint format, nint memory);

    [LibraryImport("user32.dll")]
    private static partial int CloseClipboard();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial int GlobalUnlock(nint memory);

    [LibraryImport("kernel32.dll")]
    private static partial nint GlobalFree(nint memory);
}
