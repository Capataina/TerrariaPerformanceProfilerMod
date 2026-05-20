#nullable enable

using System;
using System.Collections.Generic;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence.Records;
using Terraria;

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
        string summary = bosses.Count > 0
            ? $"killed by {primaryBoss} in {primaryBiome} at ({tx:F0}, {ty:F0})"
            : $"died in {primaryBiome} at ({tx:F0}, {ty:F0}) (no boss active)";

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
