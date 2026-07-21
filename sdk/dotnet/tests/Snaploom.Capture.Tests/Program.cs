using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Snaploom.Capture;

return await ContractTests.RunAsync();

internal static class ContractTests
{
    internal static async Task<int> RunAsync()
    {
        var tests = new (string Name, Func<Task> Run)[]
        {
            ("ABI layout and seven imports", AbiLayoutAndImportsAsync),
            ("GC compaction preserves callback state and frees once", GcCompactionAsync),
            ("Cancellation before request ID is not lost", CancelBeforeRequestIdAsync),
            ("Dispose races terminal callback without leaking", DisposeRaceAsync),
            ("Start failure releases GCHandle without callback", StartFailureAsync),
            ("Unknown asynchronous errors preserve raw values", UnknownErrorAsync),
            ("One thousand callbacks return handles to baseline", StressAsync),
        };

        try
        {
            foreach (var test in tests)
            {
                await test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }

            Equal(0, CaptureClient.ActiveRequestHandleCount, "request GCHandle baseline");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static Task AbiLayoutAndImportsAsync()
    {
        Equal(16, Marshal.SizeOf<NativeUtf8View>(), nameof(NativeUtf8View));
        Equal(56, Marshal.SizeOf<NativeVersionInfo>(), nameof(NativeVersionInfo));
        Equal(64, Marshal.SizeOf<NativeClientConfig>(), nameof(NativeClientConfig));
        Equal(48, Marshal.SizeOf<NativeCaptureOptions>(), nameof(NativeCaptureOptions));
        Equal(80, Marshal.SizeOf<NativeCompletion>(), nameof(NativeCompletion));

        var imports = typeof(NativeMethods)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(method => method.GetCustomAttribute<LibraryImportAttribute>() is not null)
            .Select(method => method.GetCustomAttribute<LibraryImportAttribute>()!.EntryPoint)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expected = new[]
        {
            "snaploom_capture_cancel_v1",
            "snaploom_capture_client_create_v1",
            "snaploom_capture_client_destroy_v1",
            "snaploom_capture_completion_free_v1",
            "snaploom_capture_error_name_v1",
            "snaploom_capture_start_v1",
            "snaploom_capture_version_v1",
        };
        SequenceEqual(expected, imports, "LibraryImport allowlist");
        return Task.CompletedTask;
    }

    private static async Task GcCompactionAsync()
    {
        using var native = new FakeNativeApi();
        using var client = new CaptureClient(native);
        var capture = client.CaptureAsync(new CaptureOptions { DisableClipboard = true });

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        native.CompleteSuccess(new byte[] { 137, 80, 78, 71 }, 2, 2, clipboardWritten: false);

        var result = await capture;
        SequenceEqual(new byte[] { 137, 80, 78, 71 }, result.Png.ToArray(), "owned PNG");
        Equal((uint)2, result.PixelWidth, "pixel width");
        Equal(1, native.CompletionFreeCount, "completion free count");
        Equal(0, CaptureClient.ActiveRequestHandleCount, "GCHandle count");
    }

    private static async Task CancelBeforeRequestIdAsync()
    {
        using var native = new FakeNativeApi { BlockStart = true };
        using var client = new CaptureClient(native);
        using var cancellation = new CancellationTokenSource();

        var outer = Task.Factory.StartNew(
            () => client.CaptureAsync(cancellationToken: cancellation.Token),
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);
        True(native.StartEntered.Wait(TimeSpan.FromSeconds(5)), "start entered");
        cancellation.Cancel();
        native.ReleaseStart.Set();
        var capture = await outer;

        await ThrowsAsync<OperationCanceledException>(async () => await capture);
        SequenceEqual(new ulong[] { 42 }, native.CanceledRequestIds, "cancel request ID");
        Equal(1, native.CompletionFreeCount, "canceled completion free count");
        Equal(0, CaptureClient.ActiveRequestHandleCount, "cancel GCHandle count");
    }

    private static async Task DisposeRaceAsync()
    {
        using var native = new FakeNativeApi();
        var client = new CaptureClient(native);
        var capture = client.CaptureAsync();

        await Task.Run(client.Dispose);
        await ThrowsAsync<CaptureCanceledException>(async () => await capture);
        Equal(1, native.DestroyCount, "destroy count");
        Equal(1, native.CompletionFreeCount, "dispose completion free count");
        Equal(0, CaptureClient.ActiveRequestHandleCount, "dispose GCHandle count");
    }

    private static Task StartFailureAsync()
    {
        using var native = new FakeNativeApi { StartStatus = NativeConstants.StatusInvalidArgument };
        using var client = new CaptureClient(native);
        Throws<CaptureArgumentException>(() => client.CaptureAsync());
        Equal(0, native.CompletionFreeCount, "failed start completion count");
        Equal(0, CaptureClient.ActiveRequestHandleCount, "failed start GCHandle count");
        return Task.CompletedTask;
    }

    private static async Task UnknownErrorAsync()
    {
        using var native = new FakeNativeApi();
        using var client = new CaptureClient(native);
        var capture = client.CaptureAsync();
        native.CompleteFailure(7_777, retryable: true);
        var error = await ThrowsAsync<CaptureException>(async () => await capture);
        Equal((uint)7_777, (uint)error.ErrorCode, "raw error value");
        True(error.IsRetryable, "retryable flag");
        Equal(1, native.CompletionFreeCount, "failed completion free count");
    }

    private static async Task StressAsync()
    {
        using var native = new FakeNativeApi();
        using var client = new CaptureClient(native);
        for (var index = 0; index < 1_000; index++)
        {
            var capture = client.CaptureAsync();
            native.CompleteSuccess(new byte[] { 137, 80, 78, 71 }, 1, 1, clipboardWritten: true);
            _ = await capture;
        }

        Equal(1_000, native.CompletionFreeCount, "stress completion free count");
        Equal(0, CaptureClient.ActiveRequestHandleCount, "stress GCHandle count");
    }

    private static void Equal<T>(T expected, T actual, string message)
        where T : IEquatable<T>
    {
        if (!expected.Equals(actual))
        {
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
        }
    }

    private static void True(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidOperationException($"{message}: expected true");
        }
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string message)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException($"{message}: sequences differ");
        }
    }

    private static TException Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException error)
        {
            return error;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}");
    }

    private static async Task<TException> ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException error)
        {
            return error;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}");
    }
}

internal sealed unsafe class FakeNativeApi : INativeApi, IDisposable
{
    private readonly object _gate = new();
    private readonly List<PendingRequest> _pending = [];
    private readonly Dictionary<nint, nint> _completionPng = [];
    private readonly nint _version = Marshal.StringToCoTaskMemUTF8("0.1.9");
    private readonly nint _unknown = Marshal.StringToCoTaskMemUTF8("UNKNOWN");
    private bool _disposed;

    internal bool BlockStart { get; init; }

    internal uint StartStatus { get; init; }

    internal ManualResetEventSlim StartEntered { get; } = new(initialState: false);

    internal ManualResetEventSlim ReleaseStart { get; } = new(initialState: false);

    internal List<ulong> CanceledRequestIds { get; } = [];

    internal int CompletionFreeCount { get; private set; }

    internal int DestroyCount { get; private set; }

    public uint Version(ref NativeVersionInfo version)
    {
        version = new NativeVersionInfo
        {
            StructSize = (uint)sizeof(NativeVersionInfo),
            AbiMajor = NativeConstants.AbiMajor,
            SdkSemver = new NativeUtf8View { Data = (byte*)_version, Length = 5 },
        };
        return NativeConstants.StatusOk;
    }

    public uint Create(NativeClientConfig* config, out nint client)
    {
        if (config is null || config->StructSize != sizeof(NativeClientConfig))
        {
            client = 0;
            return NativeConstants.StatusInvalidStructSize;
        }

        client = (nint)0x1234;
        return NativeConstants.StatusOk;
    }

    public uint Start(
        SafeCaptureClientHandle client,
        NativeCaptureOptions* options,
        delegate* unmanaged[Cdecl]<nint, nint, nint, void> callback,
        nint userData,
        ulong* requestId)
    {
        _ = client;
        if (options is null || options->StructSize != sizeof(NativeCaptureOptions))
        {
            return NativeConstants.StatusInvalidStructSize;
        }

        StartEntered.Set();
        if (BlockStart)
        {
            if (!ReleaseStart.Wait(TimeSpan.FromSeconds(5)))
            {
                return 255;
            }
        }

        if (StartStatus != NativeConstants.StatusOk)
        {
            return StartStatus;
        }

        const ulong id = 42;
        if (requestId is not null)
        {
            *requestId = id;
        }

        lock (_gate)
        {
            _pending.Add(new PendingRequest(callback, userData, id));
        }

        return NativeConstants.StatusOk;
    }

    public uint Cancel(SafeCaptureClientHandle client, ulong requestId)
    {
        _ = client;
        lock (_gate)
        {
            CanceledRequestIds.Add(requestId);
        }

        Complete(requestId, NativeConstants.CompletionCanceled, 0, 0, null, 0, 0);
        return NativeConstants.StatusOk;
    }

    public uint Destroy(nint client)
    {
        _ = client;
        PendingRequest[] pending;
        lock (_gate)
        {
            DestroyCount++;
            pending = _pending.ToArray();
        }

        foreach (var request in pending)
        {
            Complete(request.RequestId, NativeConstants.CompletionCanceled, 0, 0, null, 0, 0);
        }

        return NativeConstants.StatusOk;
    }

    public void FreeCompletion(nint completion)
    {
        if (completion == 0)
        {
            return;
        }

        nint png;
        lock (_gate)
        {
            CompletionFreeCount++;
            png = _completionPng.Remove(completion, out var pointer) ? pointer : 0;
        }

        if (png != 0)
        {
            new Span<byte>((void*)png, 4).Clear();
            Marshal.FreeHGlobal(png);
        }

        new Span<byte>((void*)completion, sizeof(NativeCompletion)).Clear();
        Marshal.FreeHGlobal(completion);
    }

    public nint ErrorName(uint errorCode)
    {
        _ = errorCode;
        return _unknown;
    }

    internal void CompleteSuccess(byte[] png, uint width, uint height, bool clipboardWritten)
    {
        Complete(
            42,
            NativeConstants.CompletionCompleted,
            0,
            clipboardWritten ? NativeConstants.ClipboardWritten : 0,
            png,
            width,
            height);
    }

    internal void CompleteFailure(uint error, bool retryable)
    {
        Complete(
            42,
            NativeConstants.CompletionFailed,
            error,
            retryable ? NativeConstants.Retryable : 0,
            null,
            0,
            0);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StartEntered.Dispose();
        ReleaseStart.Dispose();
        Marshal.FreeCoTaskMem(_version);
        Marshal.FreeCoTaskMem(_unknown);
    }

    private void Complete(
        ulong requestId,
        uint kind,
        uint error,
        uint flags,
        byte[]? png,
        uint width,
        uint height)
    {
        PendingRequest request;
        lock (_gate)
        {
            var index = _pending.FindIndex(item => item.RequestId == requestId);
            if (index < 0)
            {
                return;
            }

            request = _pending[index];
            _pending.RemoveAt(index);
        }

        nint pngPointer = 0;
        if (png is not null)
        {
            pngPointer = Marshal.AllocHGlobal(png.Length);
            Marshal.Copy(png, 0, pngPointer, png.Length);
        }

        var completionPointer = Marshal.AllocHGlobal(sizeof(NativeCompletion));
        *(NativeCompletion*)completionPointer = new NativeCompletion
        {
            StructSize = (uint)sizeof(NativeCompletion),
            Kind = kind,
            ErrorCode = error,
            Flags = flags,
            RequestId = requestId,
            PngData = pngPointer,
            PngSize = (ulong)(png?.Length ?? 0),
            PixelWidth = width,
            PixelHeight = height,
        };
        lock (_gate)
        {
            _completionPng[completionPointer] = pngPointer;
        }

        request.Callback((nint)0x1234, completionPointer, request.UserData);
    }

    private readonly struct PendingRequest(
        delegate* unmanaged[Cdecl]<nint, nint, nint, void> callback,
        nint userData,
        ulong requestId)
    {
        internal delegate* unmanaged[Cdecl]<nint, nint, nint, void> Callback { get; } = callback;

        internal nint UserData { get; } = userData;

        internal ulong RequestId { get; } = requestId;
    }
}
