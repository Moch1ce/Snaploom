using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Snaploom.Capture;

/// <summary>A disposable .NET 8 client for the separately installed Snaploom Capture Host.</summary>
public sealed unsafe class CaptureClient : IDisposable
{
    private readonly object _gate = new();
    private readonly INativeApi _native;
    private SafeCaptureClientHandle? _handle;

    /// <summary>Creates a client using the native asset selected from the current package RID.</summary>
    public CaptureClient(CaptureClientOptions? options = null)
        : this(NativeApi.Instance, options, enforcePlatform: true)
    {
    }

    internal CaptureClient(INativeApi native, CaptureClientOptions? options = null, bool enforcePlatform = false)
    {
        ArgumentNullException.ThrowIfNull(native);
        if (enforcePlatform)
        {
            PlatformSupport.EnsureSupported();
        }

        _native = native;
        RuntimeVersion = QueryRuntimeVersion(native);
        _handle = CreateHandle(native, options);
    }

    /// <summary>The ABI and semantic version reported by the loaded native SDK.</summary>
    public CaptureSdkVersion RuntimeVersion { get; }

    internal static int ActiveRequestHandleCount => RequestState.ActiveHandleCount;

    /// <summary>Starts one capture and completes only after the native terminal callback.</summary>
    public Task<CaptureResult> CaptureAsync(
        CaptureOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        SafeCaptureClientHandle handle;
        var addRef = false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_handle is null, this);
            handle = _handle;
            handle.DangerousAddRef(ref addRef);
        }

        var request = new RequestState(_native, handle, cancellationToken);
        var userData = request.AllocateHandle();
        var nativeOptions = CreateOptions(options);
        ulong requestId = 0;

        try
        {
            var status = cancellationToken.CanBeCanceled
                ? _native.Start(handle, &nativeOptions, &CompletionCallback, userData, &requestId)
                : _native.Start(handle, &nativeOptions, &CompletionCallback, userData, null);
            if (status != NativeConstants.StatusOk)
            {
                request.AbortStart();
                ThrowStatus(status, nameof(options));
            }

            request.PublishRequestId(requestId);
            return request.Task;
        }
        catch
        {
            request.AbortStart();
            throw;
        }
        finally
        {
            if (addRef)
            {
                handle.DangerousRelease();
            }
        }
    }

    /// <summary>Closes the native client. This may block while accepted callbacks finish.</summary>
    public void Dispose()
    {
        SafeCaptureClientHandle? handle;
        lock (_gate)
        {
            handle = _handle;
            _handle = null;
        }

        handle?.Dispose();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CompletionCallback(nint client, nint completion, nint userData)
    {
        _ = client;
        RequestState? request = null;
        try
        {
            var gcHandle = GCHandle.FromIntPtr(userData);
            request = gcHandle.Target as RequestState;
            if (request is null)
            {
                return;
            }

            request.Complete(completion);
        }
        catch (Exception error)
        {
            request?.FaultFromCallback(error);
        }
        finally
        {
            request?.ReleaseAfterCallback();
        }
    }

    private static CaptureSdkVersion QueryRuntimeVersion(INativeApi native)
    {
        var version = new NativeVersionInfo { StructSize = (uint)sizeof(NativeVersionInfo) };
        var status = native.Version(ref version);
        if (status != NativeConstants.StatusOk)
        {
            ThrowStatus(status, "version");
        }

        if (version.StructSize < sizeof(NativeVersionInfo)
            || version.AbiMajor != NativeConstants.AbiMajor
            || version.SdkSemver.Data is null
            || version.SdkSemver.Length is 0 or > 256)
        {
            throw new CaptureSdkException("The loaded Snaploom Capture native SDK has an incompatible ABI.");
        }

        var semver = Encoding.UTF8.GetString(version.SdkSemver.Data, checked((int)version.SdkSemver.Length));
        var separator = semver.IndexOf('.');
        if (separator <= 0
            || !uint.TryParse(semver.AsSpan(0, separator), out var semverMajor)
            || semverMajor != NativeConstants.PackageSemverMajor)
        {
            throw new CaptureSdkException("The loaded Snaploom Capture native SDK has an incompatible semantic version.");
        }

        return new CaptureSdkVersion(version.AbiMajor, semver);
    }

    private static SafeCaptureClientHandle CreateHandle(INativeApi native, CaptureClientOptions? options)
    {
        var hostPath = options?.HostExecutableOverride;
        if (!string.IsNullOrEmpty(hostPath) && !Path.IsPathFullyQualified(hostPath))
        {
            throw new ArgumentException("The Capture Host override must be an absolute path.", nameof(options));
        }

        var hostBytes = string.IsNullOrEmpty(hostPath) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(hostPath);
        fixed (byte* hostPointer = hostBytes)
        {
            var config = new NativeClientConfig
            {
                StructSize = (uint)sizeof(NativeClientConfig),
                HostExecutableOverride = new NativeUtf8View
                {
                    Data = hostBytes.Length == 0 ? null : hostPointer,
                    Length = (ulong)hostBytes.Length,
                },
                LaunchTimeoutMilliseconds = ToUInt32Milliseconds(options?.LaunchTimeout, nameof(options.LaunchTimeout)),
                HandshakeTimeoutMilliseconds = ToUInt32Milliseconds(options?.HandshakeTimeout, nameof(options.HandshakeTimeout)),
            };
            var status = native.Create(&config, out var client);
            if (status != NativeConstants.StatusOk)
            {
                ThrowStatus(status, nameof(options));
            }

            if (client == 0)
            {
                throw new CaptureSdkException("The native SDK returned a null client after a successful create.");
            }

            return new SafeCaptureClientHandle(client, native);
        }
    }

    private static NativeCaptureOptions CreateOptions(CaptureOptions? options)
    {
        return new NativeCaptureOptions
        {
            StructSize = (uint)sizeof(NativeCaptureOptions),
            Flags = options?.DisableClipboard == true ? NativeConstants.DisableClipboard : 0,
            InteractionTimeoutMilliseconds = ToUInt64Milliseconds(options?.InteractionTimeout, nameof(options.InteractionTimeout)),
        };
    }

    private static uint ToUInt32Milliseconds(TimeSpan? value, string parameter)
    {
        var milliseconds = ToUInt64Milliseconds(value, parameter);
        if (milliseconds > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameter);
        }

        return (uint)milliseconds;
    }

    private static ulong ToUInt64Milliseconds(TimeSpan? value, string parameter)
    {
        if (value is null)
        {
            return 0;
        }

        if (value <= TimeSpan.Zero || value.Value.TotalMilliseconds > ulong.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameter);
        }

        return checked((ulong)Math.Ceiling(value.Value.TotalMilliseconds));
    }

    private static void ThrowStatus(uint status, string parameter)
    {
        switch (status)
        {
            case NativeConstants.StatusInvalidArgument:
            case NativeConstants.StatusInvalidStructSize:
                throw new CaptureArgumentException(status);
            case NativeConstants.StatusClientClosed:
                throw new ObjectDisposedException("CaptureClient", $"The native client is closed (status {status}).");
            case NativeConstants.StatusOutOfMemory:
                throw new OutOfMemoryException("The native Snaploom Capture SDK could not allocate memory.");
            default:
                throw new CaptureSdkException($"Snaploom Capture native call '{parameter}' failed with status {status}.", status);
        }
    }

    private sealed class RequestState
    {
        private readonly object _gate = new();
        private readonly INativeApi _native;
        private readonly SafeCaptureClientHandle _client;
        private readonly CancellationToken _token;
        private readonly TaskCompletionSource<CaptureResult> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _registration;
        private nint _userData;
        private ulong _requestId;
        private bool _requestIdPublished;
        private bool _cancelRequested;
        private bool _terminal;

        internal RequestState(INativeApi native, SafeCaptureClientHandle client, CancellationToken token)
        {
            _native = native;
            _client = client;
            _token = token;
            if (token.CanBeCanceled)
            {
                _registration = token.Register(static state => ((RequestState)state!).RequestCancel(), this);
            }
        }

        internal static int ActiveHandleCount;

        internal Task<CaptureResult> Task => _completion.Task;

        internal nint AllocateHandle()
        {
            var handle = GCHandle.Alloc(this, GCHandleType.Normal);
            var userData = GCHandle.ToIntPtr(handle);
            _userData = userData;
            Interlocked.Increment(ref ActiveHandleCount);
            return userData;
        }

        internal void PublishRequestId(ulong requestId)
        {
            var shouldCancel = false;
            lock (_gate)
            {
                if (_terminal)
                {
                    return;
                }

                _requestId = requestId;
                _requestIdPublished = true;
                shouldCancel = _cancelRequested;
            }

            if (shouldCancel)
            {
                SendCancel(requestId);
            }
        }

        internal void Complete(nint completionPointer)
        {
            try
            {
                if (completionPointer == 0)
                {
                    throw new CaptureSdkException("The native SDK returned a null completion.");
                }

                var completion = *(NativeCompletion*)completionPointer;
                CompleteTask(CopyCompletion(completion));
            }
            catch (OperationCanceledException) when (_token.IsCancellationRequested)
            {
                _completion.TrySetCanceled(_token);
            }
            catch (Exception error)
            {
                _completion.TrySetException(error);
            }
            finally
            {
                _native.FreeCompletion(completionPointer);
            }
        }

        internal void FaultFromCallback(Exception error)
        {
            _completion.TrySetException(new CaptureSdkException("A managed callback bridge failure occurred.", innerException: error));
        }

        internal void ReleaseAfterCallback()
        {
            lock (_gate)
            {
                _terminal = true;
            }

            _registration.Unregister();
            FreeHandleOnce();
        }

        internal void AbortStart()
        {
            lock (_gate)
            {
                if (_terminal)
                {
                    return;
                }

                _terminal = true;
            }

            _registration.Unregister();
            FreeHandleOnce();
        }

        private void RequestCancel()
        {
            ulong requestId = 0;
            var shouldCancel = false;
            lock (_gate)
            {
                if (_terminal)
                {
                    return;
                }

                _cancelRequested = true;
                if (_requestIdPublished)
                {
                    requestId = _requestId;
                    shouldCancel = true;
                }
            }

            if (shouldCancel)
            {
                SendCancel(requestId);
            }
        }

        private void SendCancel(ulong requestId)
        {
            var addRef = false;
            try
            {
                _client.DangerousAddRef(ref addRef);
                _ = _native.Cancel(_client, requestId);
            }
            catch (ObjectDisposedException)
            {
                // Destroy records cancellation for every accepted request and waits for terminal callbacks.
            }
            finally
            {
                if (addRef)
                {
                    _client.DangerousRelease();
                }
            }
        }

        private CaptureResult CopyCompletion(NativeCompletion completion)
        {
            if (completion.StructSize < sizeof(NativeCompletion)
                || completion.Reserved0 != 0
                || completion.Reserved1 != 0
                || completion.Reserved2 != 0
                || completion.Reserved3 != 0
                || (completion.Flags & ~(NativeConstants.ClipboardWritten | NativeConstants.Retryable)) != 0)
            {
                throw new CaptureSdkException("The native SDK returned an invalid completion layout.");
            }

            if (completion.Kind == NativeConstants.CompletionCompleted)
            {
                if (completion.ErrorCode != 0
                    || completion.PngData == 0
                    || completion.PngSize == 0
                    || completion.PngSize > NativeConstants.MaximumPngBytes
                    || completion.PngSize > int.MaxValue
                    || completion.PixelWidth == 0
                    || completion.PixelHeight == 0
                    || (completion.Flags & NativeConstants.Retryable) != 0)
                {
                    throw new CaptureSdkException("The native SDK returned an invalid completed capture.");
                }

                var png = new byte[checked((int)completion.PngSize)];
                Marshal.Copy(completion.PngData, png, 0, png.Length);
                return new CaptureResult(
                    png,
                    completion.PixelWidth,
                    completion.PixelHeight,
                    (completion.Flags & NativeConstants.ClipboardWritten) != 0);
            }

            if (completion.PngData != 0
                || completion.PngSize != 0
                || completion.PixelWidth != 0
                || completion.PixelHeight != 0
                || (completion.Flags & NativeConstants.ClipboardWritten) != 0)
            {
                throw new CaptureSdkException("The native SDK returned image data for a non-completed terminal.");
            }

            if (completion.Kind == NativeConstants.CompletionCanceled && completion.ErrorCode == 0 && completion.Flags == 0)
            {
                if (_token.IsCancellationRequested)
                {
                    throw new OperationCanceledException(_token);
                }

                throw new CaptureCanceledException();
            }

            if (completion.Kind == NativeConstants.CompletionFailed
                && completion.ErrorCode != 0
                && (completion.Flags & ~NativeConstants.Retryable) == 0)
            {
                var namePointer = _native.ErrorName(completion.ErrorCode);
                var name = namePointer == 0 ? "UNKNOWN" : Marshal.PtrToStringUTF8(namePointer) ?? "UNKNOWN";
                throw new CaptureException(
                    (CaptureErrorCode)completion.ErrorCode,
                    (completion.Flags & NativeConstants.Retryable) != 0,
                    name);
            }

            throw new CaptureSdkException($"The native SDK returned an unknown completion kind {completion.Kind}.");
        }

        private void CompleteTask(CaptureResult result)
        {
            _completion.TrySetResult(result);
        }

        private void FreeHandleOnce()
        {
            var userData = Interlocked.Exchange(ref _userData, 0);
            if (userData == 0)
            {
                return;
            }

            GCHandle.FromIntPtr(userData).Free();
            Interlocked.Decrement(ref ActiveHandleCount);
        }
    }
}

internal static class PlatformSupport
{
    internal const string UnsupportedMessage =
        "SNAPLOOM001: Snaploom.Capture supports only win-x64 and osx-arm64.";

    internal static void EnsureSupported()
    {
        var architecture = RuntimeInformation.ProcessArchitecture;
        if ((OperatingSystem.IsWindows() && architecture == Architecture.X64)
            || (OperatingSystem.IsMacOS() && architecture == Architecture.Arm64))
        {
            return;
        }

        throw new PlatformNotSupportedException(UnsupportedMessage);
    }
}
