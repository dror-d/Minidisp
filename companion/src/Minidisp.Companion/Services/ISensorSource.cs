using Minidisp.Companion.Models;

namespace Minidisp.Companion.Services;

public interface ISensorSource : IDisposable
{
    /// <summary>Human-readable source name for the tray/log.</summary>
    string Name { get; }

    /// <summary>Current snapshot, or null if no data is available yet.</summary>
    StatsSnapshot? GetSnapshot();
}
