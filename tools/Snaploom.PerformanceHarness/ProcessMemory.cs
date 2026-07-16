using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Snaploom.Performance;

internal sealed record NativeMemoryMeasurement(long Bytes, string Metric);

internal static partial class ProcessMemory
{
    private const int RusageInfoV4 = 4;
    private const int RusageInfoV4BufferBytes = 512;
    private const int PhysicalFootprintOffset = 72;

    public static NativeMemoryMeasurement MeasureNativeMemory(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        process.Refresh();
        return OperatingSystem.IsMacOS()
            ? new NativeMemoryMeasurement(
                checked((long)GetMacOSPhysicalFootprint(process.Id)),
                "physical-footprint")
            : new NativeMemoryMeasurement(process.PrivateMemorySize64, "private-memory");
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
