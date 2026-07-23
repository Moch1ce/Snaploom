namespace Snaploom.Performance;

public sealed record MemoryStabilityResult(
    int CycleCount,
    int TailWindowSize,
    double MaximumTailGrowthRatio,
    long ManagedHeapBaselineBytes,
    long ManagedHeapGrowthBytes,
    double ManagedHeapGrowthRatio,
    bool ManagedHeapPassed,
    long NativeMemoryBaselineBytes,
    long NativeMemoryGrowthBytes,
    double NativeMemoryGrowthRatio,
    string NativeMemoryMetric,
    bool NativeMemoryPassed,
    IReadOnlyList<long> ManagedHeapSamples,
    IReadOnlyList<long> NativeMemorySamples);

public static class MemoryStabilityEvaluator
{
    public const int TailWindowSize = 5;
    public const double MaximumTailGrowthRatio = 0.01;

    public static MemoryStabilityResult Evaluate(
        IReadOnlyList<long> managedSamples,
        IReadOnlyList<long> nativeSamples,
        string nativeMemoryMetric)
    {
        ArgumentNullException.ThrowIfNull(managedSamples);
        ArgumentNullException.ThrowIfNull(nativeSamples);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeMemoryMetric);
        if (managedSamples.Count != nativeSamples.Count)
        {
            throw new ArgumentException("Managed and native sample counts must match.");
        }

        if (managedSamples.Count < TailWindowSize * 2)
        {
            throw new ArgumentException(
                $"At least {TailWindowSize * 2} memory samples are required.");
        }

        var baselineStart = managedSamples.Count - (TailWindowSize * 2);
        var finalStart = managedSamples.Count - TailWindowSize;
        var managedBaseline = Median(managedSamples, baselineStart, TailWindowSize);
        var managedFinal = Median(managedSamples, finalStart, TailWindowSize);
        var nativeBaseline = Median(nativeSamples, baselineStart, TailWindowSize);
        var nativeFinal = Median(nativeSamples, finalStart, TailWindowSize);
        var managedGrowth = managedFinal - managedBaseline;
        var nativeGrowth = nativeFinal - nativeBaseline;
        var managedGrowthRatio = GrowthRatio(managedBaseline, managedGrowth);
        var nativeGrowthRatio = GrowthRatio(nativeBaseline, nativeGrowth);
        return new MemoryStabilityResult(
            managedSamples.Count,
            TailWindowSize,
            MaximumTailGrowthRatio,
            managedBaseline,
            managedGrowth,
            managedGrowthRatio,
            managedGrowthRatio <= MaximumTailGrowthRatio,
            nativeBaseline,
            nativeGrowth,
            nativeGrowthRatio,
            nativeMemoryMetric,
            nativeGrowthRatio <= MaximumTailGrowthRatio,
            managedSamples.ToArray(),
            nativeSamples.ToArray());
    }

    private static double GrowthRatio(long baseline, long growth) =>
        growth <= 0 ? 0 : growth / (double)Math.Max(1, baseline);

    private static long Median(
        IReadOnlyList<long> samples,
        int start,
        int count)
    {
        var ordered = samples.Skip(start).Take(count).Order().ToArray();
        return ordered[ordered.Length / 2];
    }
}
