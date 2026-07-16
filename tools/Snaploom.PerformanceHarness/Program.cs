using System.Text.Json;
using Snaploom.Performance;

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
};

if (args is ["--evaluate-desktop", var inputArgument, "--output", var desktopOutputArgument])
{
    var inputPath = Path.GetFullPath(inputArgument);
    var desktopOutputPath = Path.GetFullPath(desktopOutputArgument);
    var probe = JsonSerializer.Deserialize<DesktopPerformanceProbeReport>(
        File.ReadAllText(inputPath),
        jsonOptions) ?? throw new InvalidDataException("Desktop probe report is empty.");
    var evaluation = DesktopPerformanceEvaluator.Evaluate(probe);
    WriteJson(desktopOutputPath, evaluation, jsonOptions);

    foreach (var error in evaluation.ValidationErrors)
    {
        Console.Error.WriteLine(error);
    }

    WriteMetric(evaluation.IdleMemory);
    WriteMetric(evaluation.Activation);
    if (evaluation.Memory is { } desktopMemory)
    {
        WriteMemory(desktopMemory);
    }

    return evaluation.Passed ? 0 : 1;
}

if (args is not ["--output", _])
{
    Console.Error.WriteLine(
        "Usage:\n" +
        "  Snaploom.PerformanceHarness --output <rendering-report.json>\n" +
        "  Snaploom.PerformanceHarness --evaluate-desktop <probe.json> --output <evaluation.json>");
    return 2;
}

var outputPath = Path.GetFullPath(args[1]);
var report = PerformanceBenchmarkRunner.Run();
WriteJson(outputPath, report, jsonOptions);

foreach (var metric in report.Metrics)
{
    WriteMetric(metric);
}

WriteMemory(report.Memory);
return report.Passed ? 0 : 1;

static void WriteJson<T>(string outputPath, T value, JsonSerializerOptions options)
{
    var outputDirectory = Path.GetDirectoryName(outputPath);
    if (!string.IsNullOrEmpty(outputDirectory))
    {
        Directory.CreateDirectory(outputDirectory);
    }

    File.WriteAllText(
        outputPath,
        JsonSerializer.Serialize(value, options) + Environment.NewLine);
}

static void WriteMetric(PerformanceMetric? metric)
{
    if (metric is null)
    {
        return;
    }

    Console.WriteLine(
        $"{metric.Name}: P95 {metric.P95:F3} {metric.Unit} " +
        $"(limit {metric.Limit:F3}, {(metric.Passed ? "PASS" : "FAIL")})");
}

static void WriteMemory(MemoryStabilityResult memory)
{
    Console.WriteLine(
        $"{memory.CycleCount}-cycle managed heap tail growth: " +
        $"{memory.ManagedHeapGrowthBytes} bytes ({memory.ManagedHeapGrowthRatio:P2}) " +
        $"({(memory.ManagedHeapPassed ? "PASS" : "FAIL")})");
    Console.WriteLine(
        $"{memory.CycleCount}-cycle {memory.NativeMemoryMetric} tail growth: " +
        $"{memory.NativeMemoryGrowthBytes} bytes ({memory.NativeMemoryGrowthRatio:P2}) " +
        $"({(memory.NativeMemoryPassed ? "PASS" : "FAIL")})");
}
