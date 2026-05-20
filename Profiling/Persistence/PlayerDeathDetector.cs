#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence.Records;
using Terraria;

namespace PerformanceProfiler.Profiling.Persistence;

/// <summary>
/// Detects local-player death events by diffing <see cref="Player.dead"/>
/// across consecutive ticks. On the false→true edge, captures position, HP,
/// active bosses, and a damage-weighted attribution of the killer.
///
/// v0.6 replaces the v0.5 last-hit-credit attribution. The v0.5 path read
/// the most recent <c>DamageTakenRow</c> from LiteDB at the death edge —
/// that was the only game-thread DB read in the lifecycle, and it gave
/// wrong answers when a vulture swarm softened the player and a slime
/// stole the final blow (the 16:09-16:14 playtest's death #1 case: 93/100
/// dmg from vultures, 21 dmg slime kill, row read "killed by Blue Slime").
///
/// The v0.6 path aggregates damage from the recorder's in-RAM
/// <see cref="SessionRecorder.AggregateRecentDamage"/> over a 10-second
/// window and reports the largest contributor as the killer. The full
/// breakdown is persisted in <see cref="PlayerDeathRow.DamageWeighting"/>
/// so the user can see the chain, not just the verdict (Honesty contract,
/// Invariant 3).
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
                var row = Capture(recorder, in ctx, player);
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

    private static PlayerDeathRow Capture(SessionRecorder recorder, in EventContext ctx, Player player)
    {
        var bosses = new List<int>();
        for (int i = 0; i < ctx.Bosses.Count; i++) bosses.Add(ctx.Bosses[i]);

        string primaryBoss = bosses.Count > 0
            ? (Lang.GetNPCNameValue((short)bosses[0]) ?? ("npc-" + bosses[0]))
            : "(no boss)";

        string primaryBiome = BiomeRegistry.NameOrIndex(ctx.Biomes.PrimaryBitIndex());
        float tx = player.position.X / 16f;
        float ty = player.position.Y / 16f;

        long deathUnixMs = Time.UnixMsNow();

        // v0.6: damage-weighted attribution over the last 10 seconds.
        // The recorder's in-RAM ring replaces the v0.5 LiteDB query.
        var weighting = recorder.AggregateRecentDamage(deathUnixMs);

        string summary = BuildSummary(weighting, primaryBoss, bosses.Count, primaryBiome, tx, ty);

        return new PlayerDeathRow
        {
            Tick = ctx.TickIndex,
            UnixMs = deathUnixMs,
            TileX = tx,
            TileY = ty,
            LastHp = player.statLife,
            MaxHp = player.statLifeMax2,
            ActiveBossNpcTypes = bosses,
            PrimaryBoss = primaryBoss,
            DamageWeighting = weighting,
            DamageAttributionWindowSeconds = SessionRecorder.DamageAttributionWindowSeconds,
            Summary = summary,
        };
    }

    /// <summary>
    /// Build a human-readable summary. When the damage breakdown shows a
    /// clear dominant contributor (≥ 60%), the summary names that source.
    /// When two or three sources are close, the summary names them with
    /// their share — that's the honesty contract: don't pretend a single
    /// killer when the data shows a swarm.
    /// </summary>
    private static string BuildSummary(List<DeathDamageContributor> weighting, string primaryBoss, int bossCount, string primaryBiome, float tx, float ty)
    {
        if (weighting.Count == 0)
        {
            return bossCount > 0
                ? $"died fighting {primaryBoss} in {primaryBiome} at ({tx:F0}, {ty:F0})"
                : $"died in {primaryBiome} at ({tx:F0}, {ty:F0}) (no boss active, no damage in window)";
        }

        var sb = new StringBuilder("killed by ");
        // Dominant case: top contributor ≥ 60% of window damage.
        if (weighting[0].Fraction >= 0.6f)
        {
            sb.Append(weighting[0].SourceName);
            if (weighting[0].Fraction < 0.95f)
            {
                sb.Append(" (").Append((int)(weighting[0].Fraction * 100)).Append("%)");
            }
        }
        else
        {
            // Swarm case: list top contributors by share.
            int shown = 0;
            for (int i = 0; i < weighting.Count && shown < 3; i++)
            {
                if (weighting[i].Fraction < 0.10f) break;   // 10 %+ to be named
                if (shown > 0) sb.Append(" + ");
                sb.Append(weighting[i].SourceName)
                  .Append(" (").Append((int)(weighting[i].Fraction * 100)).Append("%)");
                shown++;
            }
        }
        sb.Append(" in ").Append(primaryBiome).Append(" at (").Append(tx.ToString("F0")).Append(", ").Append(ty.ToString("F0")).Append(')');
        return sb.ToString();
    }
}
