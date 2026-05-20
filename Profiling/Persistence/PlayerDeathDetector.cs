#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence.Records;
using Terraria;
using Terraria.ModLoader;

namespace PerformanceProfiler.Profiling.Persistence;

/// <summary>
/// Detects local-player death events by diffing
/// <see cref="Player.dead"/> across consecutive ticks. On the false→true
/// edge, captures the player's position, HP, and which bosses were active
/// — enough to reconstruct "killed by Eye of Cthulhu in the Forest at
/// (3500, 240)" from the row alone.
/// </summary>
internal sealed class PlayerDeathDetector
{
    private bool _wasDeadLastTick;

    public void OnTick(SessionRecorder recorder, in EventContext ctx)
    {
        var player = Main.LocalPlayer;
        if (player == null) return;

        bool dead = player.dead;
        if (dead && !_wasDeadLastTick)
        {
            // false → true edge.
            try
            {
                var row = Capture(in ctx, player);
                recorder.OnPlayerDeath(row);
            }
            catch (Exception ex)
            {
                PerformanceProfiler.LoggerOrNull?.Warn(
                    $"PlayerDeathDetector.Capture failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
        _wasDeadLastTick = dead;
    }

    private static PlayerDeathRow Capture(in EventContext ctx, Player player)
    {
        var bosses = new List<int>();
        for (int i = 0; i < ctx.Bosses.Count; i++) bosses.Add(ctx.Bosses[i]);

        string primaryBoss = bosses.Count > 0
            ? (Lang.GetNPCNameValue((short)bosses[0]) ?? ("npc-" + bosses[0]))
            : "(no boss)";

        string primaryBiome = BiomeRegistry.NameOrIndex(ctx.Biomes.PrimaryBitIndex());
        float tx = player.position.X / 16f;
        float ty = player.position.Y / 16f;

        // Look back at the last damage-taken row in the DB — that's the
        // killer, by definition (the last hit before dead=true). The
        // recorder's writer thread may not have drained it yet, but
        // LiteDB will have whatever's already committed. Fail gracefully
        // if the lookup throws or returns nothing.
        string killer = "";
        try
        {
            var db = PerformanceProfiler.Database;
            var system = ModContent.GetInstance<ProfilerSystem>();
            if (db != null && system?.LiveRecorderSessionId is { } sid)
            {
                var last = db.DamageTaken
                    .Query()
                    .Where(x => x.SessionId == sid)
                    .OrderByDescending(x => x.UnixMs)
                    .Limit(1)
                    .FirstOrDefault();
                if (last != null && !string.IsNullOrEmpty(last.SourceName))
                    killer = last.SourceName;
            }
        }
        catch { /* best-effort attribution */ }

        string summary;
        if (!string.IsNullOrEmpty(killer))
            summary = $"killed by {killer} in {primaryBiome} at ({tx:F0}, {ty:F0})";
        else if (bosses.Count > 0)
            summary = $"died fighting {primaryBoss} in {primaryBiome} at ({tx:F0}, {ty:F0})";
        else
            summary = $"died in {primaryBiome} at ({tx:F0}, {ty:F0}) (no boss active)";

        return new PlayerDeathRow
        {
            Tick = ctx.TickIndex,
            UnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TileX = tx,
            TileY = ty,
            LastHp = player.statLife,
            MaxHp = player.statLifeMax2,
            ActiveBossNpcTypes = bosses,
            PrimaryBoss = primaryBoss,
            Summary = summary,
        };
    }
}
