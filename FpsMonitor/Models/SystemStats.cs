namespace FpsMonitor.Models;

public sealed class SystemStats
{
    public int Fps { get; init; }
    public int AvgFps { get; init; }
    public int MinFps { get; init; }
    public int MaxFps { get; init; }
    public int OnePercentLowFps { get; init; }
    public double FrametimeMs { get; init; }

    public double CpuUsage { get; init; }
    public double? CpuTemp { get; init; }

    public double RamUsage { get; init; }
    public double RamUsedGb { get; init; }
    public double RamTotalGb { get; init; }

    public double GpuUsage { get; init; }
    public double? GpuTemp { get; init; }
    public double VramUsedMb { get; init; }
    public double VramTotalMb { get; init; }
}
