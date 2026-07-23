using System.Diagnostics;
using System.Text.Json;

namespace Snaploom.App;

internal sealed record DesktopMemorySample(
    long ManagedHeapBytes,
    long NativeMemoryBytes);

internal sealed record DesktopPerformanceProbeReport(
    int SchemaVersion,
    long? IdleMemoryBytes,
    string? IdleMemoryMetric,
    string? ScreenshotCycleNativeMemoryMetric,
    IReadOnlyList<double> ActivationMilliseconds,
    IReadOnlyList<DesktopMemorySample> ScreenshotCycles);

internal sealed class DesktopPerformanceRecorder : IDisposable
{
    public const string ReportPathEnvironmentVariable = "SNAPLOOM_PERFORMANCE_REPORT";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _sync = new();
    private readonly string _reportPath;
    private readonly Timer _idleSampleTimer;
    private readonly List<double> _activationMilliseconds = [];
    private readonly List<DesktopMemorySample> _screenshotCycles = [];
    private long? _idleMemoryBytes;
    private string? _idleMemoryMetric;
    private string? _screenshotCycleNativeMemoryMetric;
    private bool _disposed;

    public DesktopPerformanceRecorder(string reportPath, TimeSpan idleSampleDelay)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportPath);
        if (idleSampleDelay < TimeSpan.Zero && idleSampleDelay != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(idleSampleDelay));
        }

        _reportPath = Path.GetFullPath(reportPath);
        _idleSampleTimer = new Timer(
            _ => SampleIdleWorkingSet(),
            state: null,
            idleSampleDelay,
            Timeout.InfiniteTimeSpan);
    }

    public static DesktopPerformanceRecorder? TryCreateFromEnvironment()
    {
        var reportPath = Environment.GetEnvironmentVariable(ReportPathEnvironmentVariable);
        return string.IsNullOrWhiteSpace(reportPath)
            ? null
            : new DesktopPerformanceRecorder(reportPath, TimeSpan.FromSeconds(10));
    }

    public void RecordIdleMemory(long memoryBytes, string metric)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(memoryBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(metric);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _idleMemoryBytes = memoryBytes;
            _idleMemoryMetric = metric;
            WriteReport();
        }
    }

    public void RecordActivation(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activationMilliseconds.Add(elapsed.TotalMilliseconds);
            WriteReport();
        }
    }

    public void RecordScreenshotCycle(
        long managedHeapBytes,
        long nativeMemoryBytes,
        string nativeMemoryMetric)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(managedHeapBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(nativeMemoryBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeMemoryMetric);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _screenshotCycleNativeMemoryMetric ??= nativeMemoryMetric;
            if (!string.Equals(
                    _screenshotCycleNativeMemoryMetric,
                    nativeMemoryMetric,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "All screenshot cycles must use the same native-memory metric.",
                    nameof(nativeMemoryMetric));
            }

            _screenshotCycles.Add(new DesktopMemorySample(managedHeapBytes, nativeMemoryBytes));
            WriteReport();
        }
    }

    public void RecordScreenshotCycle()
    {
        ForceCollection();
        using var process = Process.GetCurrentProcess();
        var nativeMemory = DesktopProcessMemory.MeasureCycleMemory(process);
        RecordScreenshotCycle(
            GC.GetTotalMemory(forceFullCollection: false),
            nativeMemory.Bytes,
            nativeMemory.Metric);
    }

    public void Dispose()
    {
        _idleSampleTimer.Dispose();
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            WriteReport();
            _disposed = true;
        }
    }

    private void SampleIdleWorkingSet()
    {
        ForceCollection();
        using var process = Process.GetCurrentProcess();
        var measurement = DesktopProcessMemory.MeasureIdleMemory(process);
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _idleMemoryBytes = measurement.Bytes;
            _idleMemoryMetric = measurement.Metric;
            WriteReport();
        }
    }

    private static void ForceCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private void WriteReport()
    {
        var directory = Path.GetDirectoryName(_reportPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var report = new DesktopPerformanceProbeReport(
            SchemaVersion: 1,
            _idleMemoryBytes,
            _idleMemoryMetric,
            _screenshotCycleNativeMemoryMetric,
            _activationMilliseconds.ToArray(),
            _screenshotCycles.ToArray());
        var temporaryPath = _reportPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(report, JsonOptions) + Environment.NewLine);
        File.Move(temporaryPath, _reportPath, overwrite: true);
    }
}
