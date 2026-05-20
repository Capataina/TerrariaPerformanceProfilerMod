#nullable enable

using PerformanceProfiler.Profiling.Events;

namespace PerformanceProfiler.Profiling.Persistence;

/// <summary>
/// Diff one tick's <see cref="EventContext"/> against the previous and emit
/// transition rows for hardmode flips, primary biome changes, and boss
/// presence shifts. Lighter than the full per-dimension transition log
/// described in §10.3 of the migration plan — that lives in a future
/// iteration once the Events tab demands it. For now, this captures the
/// three transitions the session-retrospective story cares about most.
///
/// Stateful: owns the previous-tick snapshot. Caller invokes
/// <see cref="OnSnapshot"/> after every <c>ContextTagger.Snapshot</c>.
/// </summary>
internal sealed class ContextTransitionWatcher
{
    private bool _haveLastSnapshot;
    private bool _lastHardmode;
    private int _lastPrimaryBiome;
    private bool _lastBossesPresent;
    private string _lastBossName = "";

    public void OnSnapshot(in EventContext ctx, double frameMs, SessionRecorder recorder)
    {
        bool currentBossesPresent = ctx.Bosses.Count > 0;
        string currentBossName = currentBossesPresent
            ? Terraria.Lang.GetNPCNameValue(ctx.Bosses[0]) ?? ("npc-" + ctx.Bosses[0])
            : "";
        int currentPrimaryBiome = ctx.Biomes.PrimaryBitIndex();

        if (!_haveLastSnapshot)
        {
            _lastHardmode = ctx.Hardmode;
            _lastPrimaryBiome = currentPrimaryBiome;
            _lastBossesPresent = currentBossesPresent;
            _lastBossName = currentBossName;
            _haveLastSnapshot = true;
            return;
        }

        if (ctx.Hardmode != _lastHardmode)
        {
            recorder.OnContextTransition(
                type: "hardmode",
                from: _lastHardmode ? "true" : "false",
                to: ctx.Hardmode ? "true" : "false",
                tick: ctx.TickIndex,
                tickFrameMs: frameMs);
            _lastHardmode = ctx.Hardmode;
        }

        if (currentPrimaryBiome != _lastPrimaryBiome)
        {
            recorder.OnContextTransition(
                type: "biome",
                from: BiomeRegistry.NameOrIndex(_lastPrimaryBiome),
                to: BiomeRegistry.NameOrIndex(currentPrimaryBiome),
                tick: ctx.TickIndex,
                tickFrameMs: frameMs);
            _lastPrimaryBiome = currentPrimaryBiome;
        }

        if (currentBossesPresent != _lastBossesPresent || currentBossName != _lastBossName)
        {
            recorder.OnContextTransition(
                type: "boss",
                from: _lastBossesPresent ? _lastBossName : "(none)",
                to: currentBossesPresent ? currentBossName : "(none)",
                tick: ctx.TickIndex,
                tickFrameMs: frameMs);
            _lastBossesPresent = currentBossesPresent;
            _lastBossName = currentBossName;
        }
    }
}
