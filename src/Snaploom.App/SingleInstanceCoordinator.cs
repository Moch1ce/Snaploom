using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Snaploom.App;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private const string CaptureMessage = "capture";
    private readonly FileStream? _instanceLock;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _listenerTask;
    private bool _disposed;

    private SingleInstanceCoordinator(FileStream? instanceLock, string pipeName, bool isPrimary)
    {
        _instanceLock = instanceLock;
        _pipeName = pipeName;
        IsPrimary = isPrimary;
    }

    public bool IsPrimary { get; }

    public static SingleInstanceCoordinator Acquire(string applicationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        var identifier = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(applicationId)))[..24];
        var localDataDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var lockDirectory = Path.Combine(
            string.IsNullOrWhiteSpace(localDataDirectory)
                ? Path.GetTempPath()
                : localDataDirectory,
            "Snaploom",
            "Runtime");
        Directory.CreateDirectory(lockDirectory);
        FileStream? instanceLock = null;
        try
        {
            instanceLock = new FileStream(
                Path.Combine(lockDirectory, $"{identifier}.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException)
        {
        }

        return new SingleInstanceCoordinator(
            instanceLock,
            $"snaploom-{identifier}",
            isPrimary: instanceLock is not null);
    }

    public void StartListening(Action captureRequested)
    {
        ArgumentNullException.ThrowIfNull(captureRequested);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsPrimary)
        {
            throw new InvalidOperationException("Only the primary instance can listen for commands.");
        }

        if (_listenerTask is not null)
        {
            throw new InvalidOperationException("The instance command listener is already running.");
        }

        _listenerTask = ListenAsync(captureRequested, _cancellation.Token);
    }

    public async Task<bool> SignalCaptureAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsPrimary)
        {
            return false;
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var pipe = new NamedPipeClientStream(
                    serverName: ".",
                    _pipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);
                await pipe.ConnectAsync(150, cancellationToken);
                await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true)
                {
                    AutoFlush = true,
                };
                await writer.WriteLineAsync(CaptureMessage.AsMemory(), cancellationToken);
                return true;
            }
            catch (TimeoutException) when (attempt < 19)
            {
                await Task.Delay(50, cancellationToken);
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(50, cancellationToken);
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        try
        {
            _listenerTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException exception) when (
            exception.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
        }

        _cancellation.Dispose();
        _instanceLock?.Dispose();
    }

    private async Task ListenAsync(Action captureRequested, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.In,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                var message = await reader.ReadLineAsync(cancellationToken);
                if (string.Equals(message, CaptureMessage, StringComparison.Ordinal))
                {
                    captureRequested();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }
    }
}
