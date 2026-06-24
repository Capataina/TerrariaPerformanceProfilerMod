#nullable enable

namespace PerformanceProfiler.Insights.Drivers;

/// <summary>
/// Total active entities (NPCs + projectiles) on the latest tick. The workload
/// dimension Family B controls for: a cost or heap rise that tracks entity count is
/// progression (more map, more enemies), not a leak.
/// </summary>
public sealed class EntityCountDriver : IDriver
{
    public string Name => "entities";
    public double Sample(IInsightInput input) => input.LatestNpcCount + input.LatestProjectileCount;
}

/// <summary>
/// Minutes since the session began (ticks / 3600 at 60 tps). The time axis Family B
/// regresses against — "heap is rising per minute of play".
/// </summary>
public sealed class SessionAgeDriver : IDriver
{
    public string Name => "sessionMinutes";
    public double Sample(IInsightInput input) => input.SessionTicks / 3600d;
}

/// <summary>
/// Process managed-heap megabytes — the leak signal. Rising at constant
/// <see cref="EntityCountDriver"/> is the honest definition of a leak.
/// </summary>
public sealed class HeapDriver : IDriver
{
    public string Name => "heapMB";
    public double Sample(IInsightInput input) => input.ManagedHeapBytes / (1024d * 1024d);
}
