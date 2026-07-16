namespace Snaploom.Performance;

public sealed record DesktopMemorySample(
    long ManagedHeapBytes,
    long NativeMemoryBytes);

public sealed record DesktopPerformanceProbeReport(
    int SchemaVersion,
    long? IdleMemoryBytes,
    string? IdleMemoryMetric,
    string? ScreenshotCycleNativeMemoryMetric,
    IReadOnlyList<double> ActivationMilliseconds,
    IReadOnlyList<DesktopMemorySample> ScreenshotCycles);

public sealed record DesktopPerformanceEvaluation(
    int SchemaVersion,
    PerformanceMetric? IdleMemory,
    PerformanceMetric? Activation,
    MemoryStabilityResult? Memory,
    IReadOnlyList<string> ValidationErrors,
    bool Passed);

public static class DesktopPerformanceEvaluator
{
    private const int RequiredActivationSamples = 30;
    private const int RequiredScreenshotCycles = 20;
    private const double IdleMemoryLimitBytes = 100_000_000;
    private const double ActivationLimitMilliseconds = 150;

    public static DesktopPerformanceEvaluation Evaluate(DesktopPerformanceProbeReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var validationErrors = new List<string>();
        if (report.SchemaVersion != 1)
        {
            validationErrors.Add($"Unsupported probe schema version: {report.SchemaVersion}.");
        }

        PerformanceMetric? idleMemory = null;
        if (report.IdleMemoryBytes is { } idleMemoryBytes &&
            !string.IsNullOrWhiteSpace(report.IdleMemoryMetric))
        {
            idleMemory = CreateMetric(
                $"tray-idle-{report.IdleMemoryMetric}",
                [idleMemoryBytes],
                IdleMemoryLimitBytes,
                "bytes");
        }
        else
        {
            validationErrors.Add("At least one idle working-set measurement is required.");
        }

        PerformanceMetric? activation = null;
        if (report.ActivationMilliseconds.Count >= RequiredActivationSamples)
        {
            activation = CreateMetric(
                "shortcut-to-interactive",
                report.ActivationMilliseconds,
                ActivationLimitMilliseconds,
                "ms");
        }
        else
        {
            validationErrors.Add(
                $"At least {RequiredActivationSamples} activation measurements are required; " +
                $"found {report.ActivationMilliseconds.Count}.");
        }

        MemoryStabilityResult? memory = null;
        if (report.ScreenshotCycles.Count >= RequiredScreenshotCycles &&
            !string.IsNullOrWhiteSpace(report.ScreenshotCycleNativeMemoryMetric))
        {
            var managedSamples = report.ScreenshotCycles
                .Select(sample => sample.ManagedHeapBytes)
                .ToArray();
            var nativeSamples = report.ScreenshotCycles
                .Select(sample => sample.NativeMemoryBytes)
                .ToArray();
            memory = MemoryStabilityEvaluator.Evaluate(
                managedSamples,
                nativeSamples,
                report.ScreenshotCycleNativeMemoryMetric);
        }
        else
        {
            if (report.ScreenshotCycles.Count < RequiredScreenshotCycles)
            {
                validationErrors.Add(
                    $"At least {RequiredScreenshotCycles} screenshot cycle measurements are required; " +
                    $"found {report.ScreenshotCycles.Count}.");
            }

            if (string.IsNullOrWhiteSpace(report.ScreenshotCycleNativeMemoryMetric))
            {
                validationErrors.Add("A screenshot cycle native-memory metric is required.");
            }
        }

        var passed = validationErrors.Count == 0 &&
                     idleMemory is { Passed: true } &&
                     activation is { Passed: true } &&
                     memory is { ManagedHeapPassed: true, NativeMemoryPassed: true };
        return new DesktopPerformanceEvaluation(
            SchemaVersion: 1,
            idleMemory,
            activation,
            memory,
            validationErrors,
            passed);
    }

    private static PerformanceMetric CreateMetric(
        string name,
        IEnumerable<double> samples,
        double limit,
        string unit)
    {
        var materializedSamples = samples.ToArray();
        return new PerformanceMetric(
            name,
            materializedSamples.Length,
            materializedSamples,
            PerformanceStatistics.Percentile(materializedSamples, 50),
            PerformanceStatistics.Percentile(materializedSamples, 95),
            materializedSamples.Max(),
            limit,
            unit,
            PerformanceStatistics.Percentile(materializedSamples, 95) <= limit);
    }

}
