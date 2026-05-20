#nullable enable

using System;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence.Records;
using Terraria;

namespace PerformanceProfiler.Profiling.Persistence;

/// <summary>
/// Emits a <see cref="WorldSnapshotRow"/> every ~30 seconds while a world
/// is loaded. The rows are cheap (one row × maybe 60 per session) but
/// transform "what was the player doing" into a queryable timeline:
/// position, HP, mana, primary biome, primary boss, entity counts, time
/// of day.
/// </summary>
internal sealed class WorldSnapshotter
{
    /// <summary>Emit cadence — 30 s × 60 ticks/s = 1800 ticks.</summary>
    public const int IntervalTicks = 30 * 60;

    private long _lastEmittedTick = long.MinValue;

    /// <summary>
    /// Called from <c>ProfilerSystem.PostUpdateEverything</c> after the
    /// frame's context has been snapshotted by <c>ContextTagger</c>. Emits
    /// at most one row per <see cref="IntervalTicks"/>.
    /// </summary>
    public void OnTick(SessionRecorder recorder, in EventContext ctx, int npcCount, int projCount, int dustCount)
    {
        long tick = ctx.TickIndex;
        if (_lastEmittedTick != long.MinValue && tick - _lastEmittedTick < IntervalTicks) return;
        _lastEmittedTick = tick;

        WorldSnapshotRow row;
        try
        {
            row = Capture(in ctx, npcCount, projCount, dustCount);
        }
        catch (Exception ex)
        {
            PerformanceProfiler.LoggerOrNull?.Warn($"WorldSnapshotter.Capture failed: {ex.GetType().Name}: {ex.Message}");
            return;
        }
        recorder.OnWorldSnapshot(row);
    }

    private static WorldSnapshotRow Capture(in EventContext ctx, int npcCount, int projCount, int dustCount)
    {
        var player = Main.LocalPlayer;
        bool isDay = (ctx.Weather & WeatherFlags.DayTime) != 0;

        string primaryBiome = BiomeRegistry.NameOrIndex(ctx.Biomes.PrimaryBitIndex());
        string primaryBoss = ctx.Bosses.Count > 0
            ? (Lang.GetNPCNameValue(ctx.Bosses[0]) ?? ("npc-" + ctx.Bosses[0]))
            : "(no boss)";

        int items = 0;
        for (int i = 0; i < Main.item.Length; i++)
            if (Main.item[i] != null && Main.item[i].active) items++;

        return new WorldSnapshotRow
        {
            Tick = ctx.TickIndex,
            UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TileX = player != null ? player.position.X / 16f : 0f,
            TileY = player != null ? player.position.Y / 16f : 0f,
            Hp = player?.statLife ?? 0,
            MaxHp = player?.statLifeMax2 ?? 0,
            Mana = player?.statMana ?? 0,
            MaxMana = player?.statManaMax2 ?? 0,
            PrimaryBiome = primaryBiome,
            IsHardmode = ctx.Hardmode,
            GameMode = ctx.Mode.ToString(),
            TimeOfDayBand = isDay ? "Day" : "Night",
            IsDayTime = isDay,
            NpcCount = npcCount,
            ProjectileCount = projCount,
            DustCount = dustCount,
            ItemCount = items,
            PrimaryBoss = primaryBoss,
        };
    }
}
