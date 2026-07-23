using Snaploom.Performance;

namespace Snaploom.Performance.Tests;

public sealed class DesktopPerformanceEvaluatorTests
{
    [Fact]
    public void EvaluateAcceptsACompleteReportWithinEveryLimit()
    {
        var report = new DesktopPerformanceProbeReport(
            SchemaVersion: 1,
            IdleMemoryBytes: 90_000_000,
            IdleMemoryMetric: "physical-footprint",
            ScreenshotCycleNativeMemoryMetric: "physical-footprint",
            ActivationMilliseconds: Enumerable.Range(1, 30)
                .Select(value => (double)value)
                .ToArray(),
            ScreenshotCycles: Enumerable.Range(0, 20)
                .Select(index => new DesktopMemorySample(
                    ManagedHeapBytes: 10_000_000 + (index * 10_000),
                    NativeMemoryBytes: 60_000_000 + (index * 20_000)))
                .ToArray());

        var evaluation = DesktopPerformanceEvaluator.Evaluate(report);

        Assert.True(evaluation.Passed);
        Assert.Empty(evaluation.ValidationErrors);
        Assert.Equal(29, evaluation.Activation!.P95);
        Assert.Equal(150, evaluation.Activation.Limit);
        Assert.Equal(20, evaluation.Memory!.CycleCount);
        Assert.Equal(20, evaluation.Memory.ManagedHeapSamples.Count);
        Assert.Equal(20, evaluation.Memory.NativeMemorySamples.Count);
    }

    [Fact]
    public void EvaluateRejectsAnIncompleteProbeWithoutHidingAvailableMeasurements()
    {
        var report = new DesktopPerformanceProbeReport(
            SchemaVersion: 1,
            IdleMemoryBytes: 120_000_000,
            IdleMemoryMetric: "working-set",
            ScreenshotCycleNativeMemoryMetric: null,
            ActivationMilliseconds: [100],
            ScreenshotCycles: []);

        var evaluation = DesktopPerformanceEvaluator.Evaluate(report);

        Assert.False(evaluation.Passed);
        Assert.False(evaluation.IdleMemory!.Passed);
        Assert.Null(evaluation.Memory);
        Assert.Contains(
            evaluation.ValidationErrors,
            error => error.Contains("30 activation", StringComparison.Ordinal));
        Assert.Contains(
            evaluation.ValidationErrors,
            error => error.Contains("20 screenshot", StringComparison.Ordinal));
    }

    [Fact]
    public void EvaluateRejectsContinuingTailGrowthBeyondOnePercent()
    {
        var report = new DesktopPerformanceProbeReport(
            SchemaVersion: 1,
            IdleMemoryBytes: 90_000_000,
            IdleMemoryMetric: "physical-footprint",
            ScreenshotCycleNativeMemoryMetric: "physical-footprint",
            ActivationMilliseconds: Enumerable.Repeat(100d, 30).ToArray(),
            ScreenshotCycles: Enumerable.Range(0, 20)
                .Select(index => new DesktopMemorySample(
                    ManagedHeapBytes: 3_000_000 + (index * 100_000),
                    NativeMemoryBytes: 60_000_000 + (index * 1_000_000)))
                .ToArray());

        var evaluation = DesktopPerformanceEvaluator.Evaluate(report);

        Assert.False(evaluation.Passed);
        Assert.False(evaluation.Memory!.ManagedHeapPassed);
        Assert.False(evaluation.Memory.NativeMemoryPassed);
    }
}
