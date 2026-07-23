using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Snaploom.Platform.Windows;

internal sealed unsafe partial class WindowsSystemResume : IDisposable
{
    private const uint DeviceNotifyCallback = 2;
    private const uint ResumeAutomatic = 0x0012;

    private GCHandle _selfHandle;
    private nint _registrationHandle;
    private Action? _callback;

    internal void Start(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Stop();
        _callback = callback;
        _selfHandle = GCHandle.Alloc(this);
        var parameters = new DeviceNotifySubscribeParameters
        {
            Callback = &HandlePowerNotification,
            Context = (void*)GCHandle.ToIntPtr(_selfHandle),
        };
        var result = PowerRegisterSuspendResumeNotification(
            DeviceNotifyCallback,
            (nint)(&parameters),
            out _registrationHandle);
        if (result != 0)
        {
            _selfHandle.Free();
            _callback = null;
            Marshal.ThrowExceptionForHR(unchecked((int)(0x80070000U | result)));
        }
    }

    internal void Stop()
    {
        if (_registrationHandle != 0)
        {
            _ = PowerUnregisterSuspendResumeNotification(_registrationHandle);
            _registrationHandle = 0;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        _callback = null;
    }

    public void Dispose() => Stop();

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint HandlePowerNotification(void* context, uint notificationType, void* setting)
    {
        _ = setting;
        if (notificationType == ResumeAutomatic)
        {
            var handle = GCHandle.FromIntPtr((nint)context);
            if (handle.Target is WindowsSystemResume notification)
            {
                notification._callback?.Invoke();
            }
        }

        return 0;
    }

    private struct DeviceNotifySubscribeParameters
    {
        internal delegate* unmanaged[Stdcall]<void*, uint, void*, uint> Callback;
        internal void* Context;
    }

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerRegisterSuspendResumeNotification(
        uint flags,
        nint recipient,
        out nint registrationHandle);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerUnregisterSuspendResumeNotification(nint registrationHandle);
}
