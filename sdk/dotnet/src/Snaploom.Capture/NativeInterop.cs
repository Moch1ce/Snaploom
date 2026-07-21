using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Snaploom.Capture;

internal static class NativeConstants
{
    internal const uint AbiMajor = 1;
    internal const uint PackageSemverMajor = 0;
    internal const uint StatusOk = 0;
    internal const uint StatusInvalidArgument = 1;
    internal const uint StatusInvalidStructSize = 2;
    internal const uint StatusClientClosed = 3;
    internal const uint StatusOutOfMemory = 6;
    internal const uint CompletionCompleted = 1;
    internal const uint CompletionCanceled = 2;
    internal const uint CompletionFailed = 3;
    internal const uint DisableClipboard = 1;
    internal const uint ClipboardWritten = 1;
    internal const uint Retryable = 2;
    internal const ulong MaximumPngBytes = 128UL * 1024UL * 1024UL;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeUtf8View
{
    internal byte* Data;
    internal ulong Length;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeVersionInfo
{
    internal uint StructSize;
    internal uint AbiMajor;
    internal NativeUtf8View SdkSemver;
    internal ulong Reserved0;
    internal ulong Reserved1;
    internal ulong Reserved2;
    internal ulong Reserved3;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeClientConfig
{
    internal uint StructSize;
    internal uint Flags;
    internal NativeUtf8View HostExecutableOverride;
    internal uint LaunchTimeoutMilliseconds;
    internal uint HandshakeTimeoutMilliseconds;
    internal ulong Reserved0;
    internal ulong Reserved1;
    internal ulong Reserved2;
    internal ulong Reserved3;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeCaptureOptions
{
    internal uint StructSize;
    internal uint Flags;
    internal ulong InteractionTimeoutMilliseconds;
    internal ulong Reserved0;
    internal ulong Reserved1;
    internal ulong Reserved2;
    internal ulong Reserved3;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeCompletion
{
    internal uint StructSize;
    internal uint Kind;
    internal uint ErrorCode;
    internal uint Flags;
    internal ulong RequestId;
    internal nint PngData;
    internal ulong PngSize;
    internal uint PixelWidth;
    internal uint PixelHeight;
    internal ulong Reserved0;
    internal ulong Reserved1;
    internal ulong Reserved2;
    internal ulong Reserved3;
}

internal sealed class SafeCaptureClientHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly INativeApi _native;

    internal SafeCaptureClientHandle(nint value, INativeApi native)
        : base(ownsHandle: true)
    {
        _native = native;
        SetHandle(value);
    }

    protected override bool ReleaseHandle()
    {
        return _native.Destroy(handle) == NativeConstants.StatusOk;
    }
}

internal unsafe interface INativeApi
{
    uint Version(ref NativeVersionInfo version);

    uint Create(NativeClientConfig* config, out nint client);

    uint Start(
        SafeCaptureClientHandle client,
        NativeCaptureOptions* options,
        delegate* unmanaged[Cdecl]<nint, nint, nint, void> callback,
        nint userData,
        ulong* requestId);

    uint Cancel(SafeCaptureClientHandle client, ulong requestId);

    uint Destroy(nint client);

    void FreeCompletion(nint completion);

    nint ErrorName(uint errorCode);
}

internal sealed unsafe class NativeApi : INativeApi
{
    internal static NativeApi Instance { get; } = new();

    private NativeApi()
    {
    }

    public uint Version(ref NativeVersionInfo version) => NativeMethods.Version(ref version);

    public uint Create(NativeClientConfig* config, out nint client) => NativeMethods.Create(config, out client);

    public uint Start(
        SafeCaptureClientHandle client,
        NativeCaptureOptions* options,
        delegate* unmanaged[Cdecl]<nint, nint, nint, void> callback,
        nint userData,
        ulong* requestId) => NativeMethods.Start(client, options, callback, userData, requestId);

    public uint Cancel(SafeCaptureClientHandle client, ulong requestId) => NativeMethods.Cancel(client, requestId);

    public uint Destroy(nint client) => NativeMethods.Destroy(client);

    public void FreeCompletion(nint completion) => NativeMethods.FreeCompletion(completion);

    public nint ErrorName(uint errorCode) => NativeMethods.ErrorName(errorCode);
}

internal static unsafe partial class NativeMethods
{
    private const string LibraryName = "snaploom_capture";

    [LibraryImport(LibraryName, EntryPoint = "snaploom_capture_version_v1")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint Version(ref NativeVersionInfo version);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_capture_client_create_v1")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint Create(NativeClientConfig* config, out nint client);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_capture_start_v1")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint Start(
        SafeCaptureClientHandle client,
        NativeCaptureOptions* options,
        delegate* unmanaged[Cdecl]<nint, nint, nint, void> callback,
        nint userData,
        ulong* requestId);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_capture_cancel_v1")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint Cancel(SafeCaptureClientHandle client, ulong requestId);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_capture_client_destroy_v1")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint Destroy(nint client);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_capture_completion_free_v1")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void FreeCompletion(nint completion);

    [LibraryImport(LibraryName, EntryPoint = "snaploom_capture_error_name_v1")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint ErrorName(uint errorCode);
}
