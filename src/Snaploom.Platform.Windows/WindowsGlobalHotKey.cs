using System.Runtime.InteropServices;

namespace Snaploom.Platform.Windows;

internal sealed partial class WindowsGlobalHotKey : IDisposable
{
    private const int HotKeyId = 1;
    private const uint ModAlt = 0x0001;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VirtualKeyA = 0x41;
    private const uint WmHotKey = 0x0312;
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0x0000;

    private readonly object _sync = new();
    private Thread? _thread;
    private ManualResetEventSlim? _ready;
    private Action? _callback;
    private uint _threadId;
    private bool _registered;

    internal bool TryRegister(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Unregister();

        lock (_sync)
        {
            _callback = callback;
            _ready = new ManualResetEventSlim();
            _thread = new Thread(RunMessageLoop)
            {
                IsBackground = true,
                Name = "Snaploom Windows hot key",
            };
            _thread.Start();
        }

        if (_ready!.Wait(TimeSpan.FromSeconds(5)))
        {
            return _registered;
        }

        Unregister();
        return false;
    }

    internal void Unregister()
    {
        Thread? thread;
        uint threadId;
        lock (_sync)
        {
            thread = _thread;
            threadId = _threadId;
            _callback = null;
        }

        if (thread is not null && thread.IsAlive && threadId != 0)
        {
            _ = PostThreadMessage(threadId, WmQuit, 0, 0);
            thread.Join(TimeSpan.FromSeconds(2));
        }

        lock (_sync)
        {
            _thread = null;
            _threadId = 0;
            _registered = false;
            _ready?.Dispose();
            _ready = null;
        }
    }

    public void Dispose() => Unregister();

    private void RunMessageLoop()
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, 0, 0, 0, PmNoRemove);
        _registered = RegisterHotKey(
            0,
            HotKeyId,
            ModAlt | ModShift | ModNoRepeat,
            VirtualKeyA) != 0;
        _ready?.Set();
        if (!_registered)
        {
            return;
        }

        try
        {
            while (GetMessage(out var message, 0, 0, 0) > 0)
            {
                if (message.Identifier == WmHotKey && message.WParam == HotKeyId)
                {
                    _callback?.Invoke();
                }
            }
        }
        finally
        {
            _ = UnregisterHotKey(0, HotKeyId);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        internal nint WindowHandle;
        internal uint Identifier;
        internal nint WParam;
        internal nint LParam;
        internal uint Time;
        internal Point Point;
        internal uint Private;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int RegisterHotKey(
        nint windowHandle,
        int id,
        uint modifiers,
        uint virtualKey);

    [LibraryImport("user32.dll")]
    private static partial int UnregisterHotKey(nint windowHandle, int id);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessage(
        out NativeMessage message,
        nint windowHandle,
        uint messageFilterMinimum,
        uint messageFilterMaximum);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    private static partial int PeekMessage(
        out NativeMessage message,
        nint windowHandle,
        uint messageFilterMinimum,
        uint messageFilterMaximum,
        uint removeMessage);

    [LibraryImport("user32.dll")]
    private static partial int PostThreadMessage(
        uint threadId,
        uint message,
        nint wParam,
        nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
