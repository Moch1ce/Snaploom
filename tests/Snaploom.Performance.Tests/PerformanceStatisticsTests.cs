using Snaploom.Performance;

namespace Snaploom.Performance.Tests;

public sealed class PerformanceStatisticsTests
{
    [Fact]
    public void P95UsesTheNearestRankFromOrderedSamples()
    {
        double[] samples =
        [
            20, 1, 19, 2, 18, 3, 17, 4, 16, 5,
            15, 6, 14, 7, 13, 8, 12, 9, 11, 10,
        ];

        var p95 = PerformanceStatistics.Percentile(samples, percentile: 95);

        Assert.Equal(19, p95);
    }
}
