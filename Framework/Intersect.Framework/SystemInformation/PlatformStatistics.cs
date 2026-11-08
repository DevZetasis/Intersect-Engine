using Hardware.Info;
using Microsoft.Extensions.Logging;

namespace Intersect.Framework.SystemInformation;

public class PlatformStatistics
{
    private static HardwareInfo? HardwareInfo;
    private static ILogger? _logger;
    private static Exception? _hardwareInfoFailure;

    public static IGPUStatisticsProvider? GPUStatisticsProvider { get; set; }

    public static ILogger? Logger
    {
        get => _logger;
        set
        {
            _logger = value;
            LogHardwareInfoFailure();
        }
    }

    public static long AvailablePhysicalMemory => HardwareInfo is { } hardwareInfo
        ? (long)hardwareInfo.MemoryStatus.AvailablePhysical
        : FallbackAvailableMemory;

    public static long TotalPhysicalMemory => HardwareInfo is { } hardwareInfo
        ? (long)hardwareInfo.MemoryStatus.TotalPhysical
        : FallbackTotalMemory;

    public static long AvailableGPUMemory => GPUStatisticsProvider?.AvailableMemory ?? AvailableSystemMemory;

    public static long TotalGPUMemory => GPUStatisticsProvider?.TotalMemory ?? TotalSystemMemory;

    public static long AvailableSystemMemory => HardwareInfo is { } hardwareInfo
        ? (long)hardwareInfo.MemoryStatus.AvailableVirtual
        : FallbackAvailableMemory;

    public static long TotalSystemMemory => HardwareInfo is { } hardwareInfo
        ? (long)hardwareInfo.MemoryStatus.TotalVirtual
        : FallbackTotalMemory;

    // This is a runtime memory budget, not a measurement of physical RAM or VRAM.
    // Respect runtime/container limits and subtract memory pressure rather than reporting
    // the entire budget as free memory to the renderer.
    private static long FallbackTotalMemory => Math.Max(0, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);

    private static long FallbackAvailableMemory
    {
        get
        {
            var memoryInfo = GC.GetGCMemoryInfo();
            var totalMemory = Math.Max(0, memoryInfo.TotalAvailableMemoryBytes);
            var usedMemory = Math.Max(0, Math.Max(memoryInfo.MemoryLoadBytes, Environment.WorkingSet));
            return totalMemory - Math.Min(totalMemory, usedMemory);
        }
    }

    public static void Refresh()
    {
        try
        {
            HardwareInfo?.RefreshMemoryStatus();
        }
        catch (Exception exception)
        {
            DisableHardwareInfo(exception);
        }
    }

    static PlatformStatistics()
    {
        try
        {
            HardwareInfo = new HardwareInfo();
            Refresh();
        }
        catch (Exception exception)
        {
            DisableHardwareInfo(exception);
        }
    }

    private static void DisableHardwareInfo(Exception exception)
    {
        HardwareInfo = null;
        _hardwareInfoFailure = exception;
        LogHardwareInfoFailure();
    }

    private static void LogHardwareInfoFailure()
    {
        // Initialization runs before the bootstrappers can assign Logger. Keep the
        // exception until a logger is available, and report it only once.
        if (_logger is not { } logger || _hardwareInfoFailure is not { } exception)
        {
            return;
        }

        _hardwareInfoFailure = null;
        logger.LogWarning(
            exception,
            "Hardware memory statistics are unavailable. Using runtime memory budget estimates instead."
        );
    }
}
