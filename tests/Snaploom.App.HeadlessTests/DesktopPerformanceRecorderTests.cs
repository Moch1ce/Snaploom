using System.Text.Json;

namespace Snaploom.App.HeadlessTests;

public sealed class DesktopPerformanceRecorderTests
{
    [Fact]
    public void ProbeReportContainsOnlyMeasurementsAndNoOutputPath()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"snaploom-performance-{Guid.NewGuid():N}");
        var reportPath = Path.Combine(directory, "desktop-report.json");
        Directory.CreateDirectory(directory);
        try
        {
            using (var recorder = new DesktopPerformanceRecorder(
                reportPath,
                idleSampleDelay: Timeout.InfiniteTimeSpan))
            {
                recorder.RecordIdleMemory(90_000_000, "test-memory");
                recorder.RecordActivation(TimeSpan.FromMilliseconds(120));
                recorder.RecordScreenshotCycle(40_000_000, 95_000_000, "test-native");
            }

            using var report = JsonDocument.Parse(File.ReadAllBytes(reportPath));
            var root = report.RootElement;
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(90_000_000, root.GetProperty("idleMemoryBytes").GetInt64());
            Assert.Equal("test-memory", root.GetProperty("idleMemoryMetric").GetString());
            Assert.Equal(
                "test-native",
                root.GetProperty("screenshotCycleNativeMemoryMetric").GetString());
            Assert.Equal(
                120,
                Assert.Single(root.GetProperty("activationMilliseconds").EnumerateArray())
                    .GetDouble());
            Assert.False(root.TryGetProperty("outputPath", out _));
            Assert.DoesNotContain(directory, root.GetRawText(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
