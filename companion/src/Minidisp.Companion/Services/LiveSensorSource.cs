using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;
using Minidisp.Companion.Models;

namespace Minidisp.Companion.Services;

/// <summary>
/// Live sensor readings via LibreHardwareMonitor (CPU/GPU/memory) plus
/// NetworkMonitor and DriveInfo. Temperature sensors need admin elevation;
/// without it the fields are simply omitted.
/// </summary>
public sealed class LiveSensorSource : ISensorSource
{
    private readonly ILogger _log;
    private readonly Computer _computer;
    private readonly NetworkMonitor _network = new();
    private readonly object _gate = new();
    private bool _disposed;

    public string Name => "Live sensors";

    public LiveSensorSource(ILogger<LiveSensorSource> log)
    {
        _log = log;
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
        };
        _computer.Open();
        _log.LogInformation("LibreHardwareMonitor opened (admin: {IsAdmin})", IsElevated());
    }

    private static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    public StatsSnapshot? GetSnapshot()
    {
        lock (_gate)
        {
            if (_disposed) return null;
            foreach (var hw in _computer.Hardware) hw.Update();

            var snap = new StatsSnapshot
            {
                Host = Environment.MachineName,
                Uptime = Environment.TickCount64 / 1000,
                Net = _network.Sample(),
                Disk = SampleDisks(),
            };

            foreach (var hw in _computer.Hardware)
            {
                switch (hw.HardwareType)
                {
                    case HardwareType.Cpu:
                        snap.Cpu = ReadCpu(hw);
                        break;
                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        // Prefer a discrete GPU over an iGPU if both exist.
                        if (snap.Gpu is null || hw.HardwareType != HardwareType.GpuIntel)
                            snap.Gpu = ReadGpu(hw);
                        break;
                    case HardwareType.Memory:
                        snap.Mem = ReadMemory(hw);
                        break;
                }
            }
            return snap;
        }
    }

    private static float? Sensor(IHardware hw, SensorType type, params string[] names)
    {
        foreach (var name in names)
        {
            var s = hw.Sensors.FirstOrDefault(s => s.SensorType == type &&
                s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (s?.Value is float v && !float.IsNaN(v)) return v;
        }
        return null;
    }

    private static CpuStats ReadCpu(IHardware hw)
    {
        var cores = hw.Sensors
            .Where(s => s.SensorType == SensorType.Load &&
                        s.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Name.Length).ThenBy(s => s.Name)
            .Select(s => (float)Math.Round(s.Value ?? 0, 1))
            .Take(32)
            .ToList();

        var clock = hw.Sensors
            .Where(s => s.SensorType == SensorType.Clock &&
                        s.Name.StartsWith("CPU Core", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Value)
            .FirstOrDefault(v => v > 0);

        return new CpuStats
        {
            Name = hw.Name,
            Load = Round1(Sensor(hw, SensorType.Load, "CPU Total")),
            Temp = Round1(Sensor(hw, SensorType.Temperature,
                "Core (Tctl/Tdie)", "CPU Package", "Core Average", "Core Max")),
            Freq = clock is float mhz ? (float)Math.Round(mhz / 1000, 2) : null,
            Cores = cores.Count > 0 ? cores : null,
        };
    }

    private static GpuStats ReadGpu(IHardware hw) => new()
    {
        Name = hw.Name,
        Load = Round1(Sensor(hw, SensorType.Load, "GPU Core", "D3D 3D")),
        Temp = Round1(Sensor(hw, SensorType.Temperature, "GPU Core", "GPU Hot Spot")),
    };

    private static MemStats ReadMemory(IHardware hw)
    {
        var used = Sensor(hw, SensorType.Data, "Memory Used");
        var available = Sensor(hw, SensorType.Data, "Memory Available");
        float? total = used + available;
        return new MemStats
        {
            Pct = Round1(Sensor(hw, SensorType.Load, "Memory")),
            Used = Round1(used),
            Total = Round1(total),
        };
    }

    private static List<DiskStats> SampleDisks() =>
        DriveInfo.GetDrives()
            .Where(d => d is { IsReady: true, DriveType: DriveType.Fixed })
            .Select(d => new DiskStats
            {
                Name = d.Name.TrimEnd('\\'),
                Pct = (float)Math.Round(100.0 * (d.TotalSize - d.TotalFreeSpace) / d.TotalSize, 1),
                Free = (float)Math.Round(d.TotalFreeSpace / 1_073_741_824.0, 1),
            })
            .Take(4)
            .ToList();

    private static float? Round1(float? v) => v is float f ? (float)Math.Round(f, 1) : null;

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _computer.Close();
        }
    }
}
