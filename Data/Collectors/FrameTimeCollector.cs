#nullable enable

using Terraria.ModLoader;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.Data.Collectors;

/// <summary>
/// Snapshot of the most recent <see cref="TickFrame"/> + a quick-look
/// reference to the full rolling-history buffer. The buffer reference
/// is read-only; callers may iterate it but MUST NOT mutate it.
/// </summary>
public readonly struct FrameTimeSnapshot
{
    public readonly bool WorldLoaded;
    public readonly long TickIndex;
    public readonly double FrameTimeMs;
    public readonly double GcTimeMs;
    public readonly int NpcCount;
    public readonly int ProjectileCount;
    public readonly int DustCount;
    public readonly RingBuffer<TickFrame>? History;

    public FrameTimeSnapshot(bool worldLoaded, long tickIndex, double frameMs, double gcMs,
        int npcCount, int projCount, int dustCount, RingBuffer<TickFrame>? history)
    {
        WorldLoaded = worldLoaded;
        TickIndex = tickIndex;
        FrameTimeMs = frameMs;
        GcTimeMs = gcMs;
        NpcCount = npcCount;
        ProjectileCount = projCount;
        DustCount = dustCount;
        History = history;
    }

    public static readonly FrameTimeSnapshot Empty
        = new FrameTimeSnapshot(false, 0L, 0d, 0d, 0, 0, 0, null);
}

/// <summary>
/// Pipeline-facing adapter over <see cref="MetricCollector"/>'s frame-
/// timing surface. The underlying collector owns the per-tick storage;
/// this class exposes it through <see cref="IDataCollector{TSnapshot}"/>.
///
/// <para>
/// <b>Per-tick callback.</b> Registered as <see cref="DataStreamCadence.PerTick"/>
/// with a no-op capture delegate. The real data flow stays through
/// <see cref="MetricCollector"/>'s already-wired <see cref="MetricCollector.EndTick"/>;
/// the registry's per-tick drive site just confirms the loop fires.
/// Snapshot reads pull live state from the collector.
/// </para>
/// </summary>
public sealed class FrameTimeCollector : IDataCollector<FrameTimeSnapshot>
{
    public const string StreamName = "frameTime";

    public string Name => StreamName;
    public DataStreamCadence Cadence => DataStreamCadence.PerTick;
    public DataStage Stage => DataStage.Collector;

    /// <summary>
    /// Static no-op delegate so the registry's per-tick loop has work to
    /// iterate. The actual frame timing is captured by MetricCollector's
    /// own EndTick path; this adapter only EXPOSES that state through
    /// the pipeline. The no-op exists so the registry can confirm the
    /// stream is per-tick driven (and so future iterations can fill it
    /// in without changing the registry contract).
    /// </summary>
    public TickCapture? PerTickCallback => Capture;
    private static readonly TickCapture Capture = static (in TickContext _) => { };

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public FrameTimeSnapshot CurrentSnapshot()
    {
        MetricCollector? c = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (c == null || c.History.Count == 0) return FrameTimeSnapshot.Empty;
        var latest = c.History[c.History.Count - 1];
        return new FrameTimeSnapshot(
            worldLoaded: true,
            tickIndex: latest.TickIndex,
            frameMs: latest.FrameTimeMs,
            gcMs: latest.GcTimeMs,
            npcCount: latest.NpcCount,
            projCount: latest.ProjectileCount,
            dustCount: latest.DustCount,
            history: c.History);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();
}
