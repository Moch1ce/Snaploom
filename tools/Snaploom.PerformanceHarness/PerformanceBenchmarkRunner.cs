using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Snaploom.Core;
using Snaploom.Rendering;

namespace Snaploom.Performance;

public sealed record PerformanceEnvironment(
    string OperatingSystem,
    string OperatingSystemArchitecture,
    string ProcessArchitecture,
    string Framework,
    int ProcessorCount,
    bool ServerGarbageCollection);

public sealed record PerformanceMetric(
    string Name,
    int SampleCount,
    IReadOnlyList<double> Samples,
    double P50,
    double P95,
    double Maximum,
    double Limit,
    string Unit,
    bool Passed);

public sealed record PerformanceReport(
    int SchemaVersion,
    DateTimeOffset RecordedAtUtc,
    string Workload,
    PerformanceEnvironment Environment,
    IReadOnlyList<PerformanceMetric> Metrics,
    MemoryStabilityResult Memory,
    bool Passed);

public static class PerformanceBenchmarkRunner
{
    private const int Width = 3840;
    private const int Height = 2160;
    private const int Stride = Width * 4;
    private const double FrameBudgetMilliseconds = 1000d / 60;
    private const double PngBudgetMilliseconds = 1000;

    public static PerformanceReport Run()
    {
        var sourcePixels = CreateSynthetic4KFrame();
        try
        {
            using var frame = new CapturedFrame(
                new PhysicalSize(Width, Height),
                new LogicalSize(Width, Height),
                Stride,
                sourcePixels);
            var annotations = CreateAnnotations();

            WarmUp(frame, annotations);
            var metrics = new[]
            {
                CreateMetric(
                    "selection-state-update-4k",
                    MeasureSelectionMovement(),
                    FrameBudgetMilliseconds,
                    "ms"),
                CreateMetric(
                    "object-drag-render-4k",
                    MeasureObjectDragRendering(),
                    FrameBudgetMilliseconds,
                    "ms"),
                CreateMetric(
                    "mosaic-stroke-cache-4k",
                    MeasureMosaicStroke(frame),
                    FrameBudgetMilliseconds,
                    "ms"),
                CreateMetric(
                    "mosaic-move-cache-4k",
                    MeasureMosaicMovement(frame),
                    FrameBudgetMilliseconds,
                    "ms"),
                CreateMetric(
                    "mosaic-undo-redo-cache-4k",
                    MeasureMosaicUndoRedo(frame),
                    FrameBudgetMilliseconds,
                    "ms"),
                CreateMetric(
                    "png-compose-save-4k",
                    MeasurePngComposition(frame, annotations),
                    PngBudgetMilliseconds,
                    "ms"),
            };
            var memory = MeasureMemoryStability(frame, annotations);
            return new PerformanceReport(
                SchemaVersion: 1,
                DateTimeOffset.UtcNow,
                Workload: "synthetic-4k-bgra-srgb",
                new PerformanceEnvironment(
                    RuntimeInformation.OSDescription,
                    RuntimeInformation.OSArchitecture.ToString(),
                    RuntimeInformation.ProcessArchitecture.ToString(),
                    RuntimeInformation.FrameworkDescription,
                    Environment.ProcessorCount,
                    GCSettings.IsServerGC),
                metrics,
                memory,
                metrics.All(metric => metric.Passed) &&
                memory.ManagedHeapPassed &&
                memory.NativeMemoryPassed);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sourcePixels);
        }
    }

    private static void WarmUp(
        CapturedFrame frame,
        IReadOnlyList<IScreenshotAnnotation> annotations)
    {
        var png = SelectionPngEncoder.Encode(
            frame,
            new PhysicalRect(0, 0, Width, Height),
            annotations);
        CryptographicOperations.ZeroMemory(png);
        var raster = ScreenshotAnnotationRenderer.RenderBgra(
            Width,
            Height,
            scaleX: 1,
            scaleY: 1,
            annotations.Where(annotation => annotation is not ScreenshotMosaicAnnotation));
        CryptographicOperations.ZeroMemory(raster.Pixels);
    }

    private static double[] MeasureSelectionMovement()
    {
        var session = new ScreenshotSession(new PhysicalSize(Width, Height));
        session.Select(new PhysicalRect(100, 100, 1920, 1080));
        session.BeginMoveSelection(new PhysicalPoint(200, 200));
        var samples = new double[240];
        for (var index = 0; index < samples.Length; index++)
        {
            var point = new PhysicalPoint(200 + (index % 600), 200 + (index % 300));
            samples[index] = MeasureMilliseconds(() => session.UpdateMoveSelection(point));
        }

        session.CompleteMoveSelection();
        return samples;
    }

    private static double[] MeasureObjectDragRendering()
    {
        var session = new ScreenshotAnnotationSession();
        session.SetTool(ScreenshotAnnotationTool.Rectangle);
        session.Begin(new LogicalPoint(200, 200));
        session.Update(new LogicalPoint(1200, 900));
        _ = session.Complete();
        _ = session.Select(0);
        _ = session.BeginMoveSelected(new LogicalPoint(400, 400));

        var samples = new double[30];
        for (var index = 0; index < samples.Length; index++)
        {
            var point = new LogicalPoint(401 + index, 401 + index);
            samples[index] = MeasureMilliseconds(() =>
            {
                session.UpdateSelectedTransform(point);
                var raster = ScreenshotAnnotationRenderer.RenderBgra(
                    Width,
                    Height,
                    scaleX: 1,
                    scaleY: 1,
                    session.EnumerateForRendering());
                CryptographicOperations.ZeroMemory(raster.Pixels);
            });
        }

        _ = session.CompleteSelectedTransform();
        return samples;
    }

    private static double[] MeasureMosaicStroke(CapturedFrame frame)
    {
        using var cache = new ScreenshotMosaicTileCache(
            Width,
            Height,
            Stride,
            frame.Pixels.Span,
            scaleX: 1,
            scaleY: 1);
        var points = new List<LogicalPoint>();
        var samples = new double[120];
        for (var index = 0; index < samples.Length; index++)
        {
            points.Add(new LogicalPoint(
                120 + (index * 28),
                1080 + (Math.Sin(index / 8d) * 700)));
            var annotation = new ScreenshotMosaicAnnotation(
                points.ToArray(),
                ScreenshotMosaicStyle.Default);
            samples[index] = MeasureMilliseconds(() => cache.Update([annotation]));
        }

        return samples;
    }

    private static double[] MeasurePngComposition(
        CapturedFrame frame,
        IReadOnlyList<IScreenshotAnnotation> annotations)
    {
        var samples = new double[5];
        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"snaploom-performance-{Guid.NewGuid():N}.png");
        try
        {
            for (var index = 0; index < samples.Length; index++)
            {
                samples[index] = MeasureMilliseconds(() =>
                {
                    var png = SelectionPngEncoder.Encode(
                        frame,
                        new PhysicalRect(0, 0, Width, Height),
                        annotations);
                    File.WriteAllBytes(outputPath, png);
                    CryptographicOperations.ZeroMemory(png);
                });
            }
        }
        finally
        {
            File.Delete(outputPath);
        }

        return samples;
    }

    private static MemoryStabilityResult MeasureMemoryStability(
        CapturedFrame frame,
        IReadOnlyList<IScreenshotAnnotation> annotations)
    {
        const int cycleCount = 20;
        var managedSamples = new long[cycleCount];
        var nativeSamples = new long[cycleCount];
        string? nativeMemoryMetric = null;
        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"snaploom-memory-cycle-{Guid.NewGuid():N}.png");
        using var process = Process.GetCurrentProcess();
        try
        {
            for (var index = 0; index < cycleCount; index++)
            {
                var png = SelectionPngEncoder.Encode(
                    frame,
                    new PhysicalRect(0, 0, Width, Height),
                    annotations);
                File.WriteAllBytes(outputPath, png);
                CryptographicOperations.ZeroMemory(png);
                ForceCollection();
                var nativeMemory = ProcessMemory.MeasureNativeMemory(process);
                managedSamples[index] = GC.GetTotalMemory(forceFullCollection: false);
                nativeSamples[index] = nativeMemory.Bytes;
                nativeMemoryMetric ??= nativeMemory.Metric;
            }
        }
        finally
        {
            File.Delete(outputPath);
        }

        return MemoryStabilityEvaluator.Evaluate(
            managedSamples,
            nativeSamples,
            nativeMemoryMetric ?? throw new InvalidOperationException("No native-memory metric was recorded."));
    }

    private static double[] MeasureMosaicMovement(CapturedFrame frame)
    {
        using var cache = CreateMosaicCache(frame);
        var session = CreateMosaicSession();
        cache.Update(session.Annotations.OfType<ScreenshotMosaicAnnotation>());
        var samples = new double[30];
        for (var index = 0; index < samples.Length; index++)
        {
            var selected = (ScreenshotMosaicAnnotation)session.SelectedAnnotation!;
            var origin = selected.Points[0];
            _ = session.BeginMoveSelected(origin);
            samples[index] = MeasureMilliseconds(() =>
            {
                session.UpdateSelectedTransform(new LogicalPoint(origin.X + 2, origin.Y + 1));
                cache.Update(session.Annotations.OfType<ScreenshotMosaicAnnotation>());
            });
            _ = session.CompleteSelectedTransform();
        }

        return samples;
    }

    private static double[] MeasureMosaicUndoRedo(CapturedFrame frame)
    {
        using var cache = CreateMosaicCache(frame);
        var session = CreateMosaicSession();
        cache.Update(session.Annotations.OfType<ScreenshotMosaicAnnotation>());
        var samples = new double[60];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = MeasureMilliseconds(() =>
            {
                var changed = index % 2 == 0 ? session.Undo() : session.Redo();
                if (!changed)
                {
                    throw new InvalidOperationException("The mosaic history benchmark lost its state.");
                }

                cache.Update(session.Annotations.OfType<ScreenshotMosaicAnnotation>());
            });
        }

        return samples;
    }

    private static ScreenshotAnnotationSession CreateMosaicSession()
    {
        var session = new ScreenshotAnnotationSession();
        session.SetTool(ScreenshotAnnotationTool.Mosaic);
        session.Begin(new LogicalPoint(300, 1800));
        for (var index = 1; index < 80; index++)
        {
            session.Update(new LogicalPoint(300 + (index * 35), 1800 - (index * 12)));
        }

        _ = session.Complete();
        _ = session.Select(0);
        return session;
    }

    private static ScreenshotMosaicTileCache CreateMosaicCache(CapturedFrame frame) =>
        new(
            Width,
            Height,
            Stride,
            frame.Pixels.Span,
            scaleX: 1,
            scaleY: 1);

    private static IReadOnlyList<IScreenshotAnnotation> CreateAnnotations() =>
    [
        new ScreenshotRectangleAnnotation(
            new LogicalPoint(200, 200),
            new LogicalPoint(1800, 1000),
            ScreenshotAnnotationStyle.Default),
        new ScreenshotArrowAnnotation(
            new LogicalPoint(500, 1600),
            new LogicalPoint(2500, 600),
            new ScreenshotAnnotationStyle(ScreenshotAnnotationColor.Blue, 8)),
        new ScreenshotTextAnnotation(
            new LogicalPoint(1800, 1200),
            "Snaploom 4K 性能验收",
            MaxWidth: 1200,
            ScreenshotTextStyle.Default),
        new ScreenshotMosaicAnnotation(
            Enumerable.Range(0, 80)
                .Select(index => new LogicalPoint(300 + (index * 35), 1800 - (index * 12)))
                .ToArray(),
            ScreenshotMosaicStyle.Default),
    ];

    private static byte[] CreateSynthetic4KFrame()
    {
        var pixels = GC.AllocateUninitializedArray<byte>(checked(Stride * Height));
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var offset = (y * Stride) + (x * 4);
                pixels[offset] = (byte)(x % 256);
                pixels[offset + 1] = (byte)(y % 256);
                pixels[offset + 2] = (byte)((x + y) % 256);
                pixels[offset + 3] = byte.MaxValue;
            }
        }

        return pixels;
    }

    private static PerformanceMetric CreateMetric(
        string name,
        double[] samples,
        double limit,
        string unit)
    {
        var p50 = PerformanceStatistics.Percentile(samples, 50);
        var p95 = PerformanceStatistics.Percentile(samples, 95);
        return new PerformanceMetric(
            name,
            samples.Length,
            samples,
            p50,
            p95,
            samples.Max(),
            limit,
            unit,
            p95 <= limit);
    }

    private static double MeasureMilliseconds(Action action)
    {
        var startedAt = Stopwatch.GetTimestamp();
        action();
        return Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
    }

    private static void ForceCollection()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }
}
