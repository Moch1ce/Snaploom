using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Snaploom.App;

internal sealed record DesktopIdleMemoryMeasurement(
    long Bytes,
    string Metric);

internal static partial class DesktopProcessMemory
{
    private const int RusageInfoV4 = 4;
    private const int RusageInfoV4BufferBytes = 512;
    private const int PhysicalFootprintOffset = 72;

    public static DesktopIdleMemoryMeasurement MeasureIdleMemory(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        process.Refresh();
        return OperatingSystem.IsMacOS()
            ? new DesktopIdleMemoryMeasurement(
                checked((long)GetMacOSPhysicalFootprint(process.Id)),
                "physical-footprint")
            : new DesktopIdleMemoryMeasurement(process.WorkingSet64, "working-set");
    }

    public static DesktopIdleMemoryMeasurement MeasureCycleMemory(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        process.Refresh();
        return OperatingSystem.IsMacOS()
            ? new DesktopIdleMemoryMeasurement(
                checked((long)GetMacOSPhysicalFootprint(process.Id)),
                "physical-footprint")
            : new DesktopIdleMemoryMeasurement(process.PrivateMemorySize64, "private-memory");
    }

    [SupportedOSPlatform("macos")]
    private static unsafe ulong GetMacOSPhysicalFootprint(int processId)
    {
        var buffer = stackalloc byte[RusageInfoV4BufferBytes];
        NativeMemory.Clear(buffer, RusageInfoV4BufferBytes);
        if (ProcPidRusage(processId, RusageInfoV4, buffer) != 0)
        {
            throw new InvalidOperationException("Unable to read the macOS process footprint.");
        }

        return *(ulong*)(buffer + PhysicalFootprintOffset);
    }

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "proc_pid_rusage")]
    private static unsafe partial int ProcPidRusage(
        int processId,
        int flavor,
        byte* buffer);
}
