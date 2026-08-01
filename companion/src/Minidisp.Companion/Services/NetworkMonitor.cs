using System.Net.NetworkInformation;
using System.Net.Sockets;
using Minidisp.Companion.Models;

namespace Minidisp.Companion.Services;

/// <summary>
/// Enumerates active network interfaces with their IPv4 address and computes
/// up/down rates (Mbit/s) from byte-counter deltas between calls.
/// </summary>
public sealed class NetworkMonitor
{
    private readonly Dictionary<string, (long Sent, long Received, DateTime At)> _last = new();

    public List<NetStats> Sample()
    {
        var result = new List<NetStats>();
        var now = DateTime.UtcNow;

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                or NetworkInterfaceType.Tunnel) continue;

            var ipv4 = nic.GetIPProperties().UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                ?.Address.ToString();
            if (ipv4 is null) continue;

            var stats = nic.GetIPStatistics();
            float upMbps = 0, downMbps = 0;
            if (_last.TryGetValue(nic.Id, out var prev))
            {
                var seconds = (now - prev.At).TotalSeconds;
                if (seconds > 0.05)
                {
                    upMbps = (float)((stats.BytesSent - prev.Sent) * 8 / seconds / 1_000_000);
                    downMbps = (float)((stats.BytesReceived - prev.Received) * 8 / seconds / 1_000_000);
                }
            }
            _last[nic.Id] = (stats.BytesSent, stats.BytesReceived, now);

            result.Add(new NetStats
            {
                Interface = nic.Name,
                Ip = ipv4,
                Up = Math.Max(0, (float)Math.Round(upMbps, 2)),
                Down = Math.Max(0, (float)Math.Round(downMbps, 2)),
            });
        }

        // Physical ethernet/wifi first so "net" (index 0) is the interesting one.
        return result
            .OrderByDescending(n => n.Interface is "Ethernet" or "Wi-Fi")
            .ThenBy(n => n.Interface, StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
    }
}
