namespace Snaploom.Performance;

public static class PerformanceStatistics
{
    public static double Percentile(IEnumerable<double> samples, double percentile)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (percentile <= 0 || percentile > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile));
        }

        var ordered = samples.Order().ToArray();
        if (ordered.Length == 0)
        {
            throw new ArgumentException("At least one sample is required.", nameof(samples));
        }

        var rank = (int)Math.Ceiling((percentile / 100) * ordered.Length);
        return ordered[rank - 1];
    }
}
