using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace FpsMonitor.Services;

public sealed record HardwareSnapshot(
    double CpuUsage,
    double? CpuTemp,
    double RamUsage,
    double RamUsedGb,
    double RamTotalGb,
    double GpuUsage,
    double? GpuTemp,
    double VramUsedMb,
    double VramTotalMb);

public sealed class HardwareMonitorService : IDisposable
{
    private readonly Computer _computer;
    private readonly object _lock = new();
    private bool _disposed;

    public HardwareMonitorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true
        };
        _computer.Open();
    }

    public HardwareSnapshot Collect()
    {
        lock (_lock)
        {
            if (_disposed)
                return new HardwareSnapshot(0, null, 0, 0, 0, 0, null, 0, 0);

            double cpuUsage = 0;
            double? cpuTemp = null;
            double ramUsage = 0;
            double ramUsedGb = 0;
            double ramTotalGb = 0;
            double gpuUsage = 0;
            double? gpuTemp = null;
            double vramUsedMb = 0;
            double vramTotalMb = 0;

            foreach (var hardware in _computer.Hardware)
            {
                hardware.Update();

                switch (hardware.HardwareType)
                {
                    case HardwareType.Cpu:
                        foreach (var sensor in hardware.Sensors)
                        {
                            if (sensor.SensorType == SensorType.Load &&
                                sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase) &&
                                sensor.Value.HasValue)
                            {
                                cpuUsage = Math.Round(sensor.Value.Value, 1);
                            }
                            else if (sensor.SensorType == SensorType.Temperature &&
                                     sensor.Value.HasValue &&
                                     (cpuTemp is null ||
                                      sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                                      sensor.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)))
                            {
                                cpuTemp = Math.Round(sensor.Value.Value, 0);
                            }
                        }
                        break;

                    case HardwareType.Memory:
                        foreach (var sensor in hardware.Sensors)
                        {
                            if (!sensor.Value.HasValue)
                                continue;

                            if (sensor.SensorType == SensorType.Load &&
                                sensor.Name.Equals("Memory", StringComparison.OrdinalIgnoreCase))
                            {
                                ramUsage = Math.Round(sensor.Value.Value, 1);
                            }
                            else if (sensor.SensorType == SensorType.Data)
                            {
                                if (sensor.Name.Equals("Memory Used", StringComparison.OrdinalIgnoreCase))
                                    ramUsedGb = Math.Round(sensor.Value.Value, 1);
                                else if (sensor.Name.Equals("Memory Available", StringComparison.OrdinalIgnoreCase))
                                    ramTotalGb = Math.Round(ramUsedGb + sensor.Value.Value, 1);
                            }
                        }
                        break;

                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        foreach (var sensor in hardware.Sensors)
                        {
                            if (!sensor.Value.HasValue)
                                continue;

                            if (sensor.SensorType == SensorType.Load &&
                                sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                            {
                                gpuUsage = Math.Round(sensor.Value.Value, 1);
                            }
                            else if (sensor.SensorType == SensorType.Temperature &&
                                     (gpuTemp is null || sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)))
                            {
                                gpuTemp = Math.Round(sensor.Value.Value, 0);
                            }
                            else if (sensor.SensorType == SensorType.SmallData)
                            {
                                if (sensor.Name.Contains("Memory Used", StringComparison.OrdinalIgnoreCase))
                                    vramUsedMb = Math.Round(sensor.Value.Value, 0);
                                else if (sensor.Name.Contains("Memory Total", StringComparison.OrdinalIgnoreCase))
                                    vramTotalMb = Math.Round(sensor.Value.Value, 0);
                            }
                        }
                        break;
                }
            }

            // Fallback для RAM через WinAPI, если датчики памяти не вернули данные.
            if (ramTotalGb <= 0)
            {
                var mem = GetMemoryStatus();
                ramUsage = mem.UsagePercent;
                ramUsedGb = mem.UsedGb;
                ramTotalGb = mem.TotalGb;
            }

            return new HardwareSnapshot(
                cpuUsage, cpuTemp,
                ramUsage, ramUsedGb, ramTotalGb,
                gpuUsage, gpuTemp, vramUsedMb, vramTotalMb);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static (double UsagePercent, double UsedGb, double TotalGb) GetMemoryStatus()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
            return (0, 0, 0);

        const double gb = 1024.0 * 1024.0 * 1024.0;
        var totalGb = Math.Round(status.ullTotalPhys / gb, 1);
        var usedGb = Math.Round((status.ullTotalPhys - status.ullAvailPhys) / gb, 1);
        return (status.dwMemoryLoad, usedGb, totalGb);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _computer.Close();
        }
    }
}
