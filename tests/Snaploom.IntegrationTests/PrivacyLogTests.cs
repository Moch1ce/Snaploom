using Snaploom.App;

namespace Snaploom.IntegrationTests;

public sealed class PrivacyLogTests
{
    [Fact]
    public void ProductionLimitsAreFiveFilesOfTwoMegabytes()
    {
        Assert.Equal(5, PrivacyLog.MaxFileCount);
        Assert.Equal(2 * 1024 * 1024, PrivacyLog.MaxFileBytes);
    }

    [Fact]
    public void SensitiveExceptionContentIsNeverWritten()
    {
        var directory = CreateTemporaryDirectory("private-folder-name");
        try
        {
            var log = new PrivacyLog(directory);
            var exception = new InvalidOperationException(
                "pixels=DEADBEEF text=top-secret clipboard=secret " +
                "path=/Users/alice/Secret/screenshot.png");

            log.Error(AppLogEvent.CaptureFailed, exception);

            var content = ReadAllLogs(directory);
            Assert.Contains(nameof(AppLogEvent.CaptureFailed), content);
            Assert.Contains(nameof(InvalidOperationException), content);
            Assert.DoesNotContain("DEADBEEF", content);
            Assert.DoesNotContain("top-secret", content);
            Assert.DoesNotContain("clipboard", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/Users/alice", content);
            Assert.DoesNotContain("private-folder-name", content);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RollingLogKeepsOnlyFiveBoundedFiles()
    {
        var directory = CreateTemporaryDirectory("rolling");
        try
        {
            var log = new PrivacyLog(directory, maxFileBytes: 512);

            for (var index = 0; index < 200; index++)
            {
                log.Info(AppLogEvent.ApplicationStarted);
            }

            File.WriteAllBytes(
                Path.Combine(directory, "snaploom.4.log"),
                new byte[700]);
            log.Info(AppLogEvent.ApplicationStopped);

            var files = Directory.EnumerateFiles(directory, "snaploom*.log").ToArray();
            Assert.InRange(files.Length, 1, 5);
            Assert.All(files, file => Assert.InRange(new FileInfo(file).Length, 1, 512));

            Assert.True(log.Clear());
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory(string suffix)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"snaploom-logs-{Guid.NewGuid():N}-{suffix}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string ReadAllLogs(string directory) => string.Concat(
        Directory.EnumerateFiles(directory, "snaploom*.log").Select(File.ReadAllText));
}
