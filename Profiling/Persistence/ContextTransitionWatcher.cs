#nullable enable

using System;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence.Records;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
namespace PerformanceProfiler.Profiling.Persistence;

/// <summary>
/// Diffs each tick's <see cref="EventContext"/> against the previous and
/// emits a <c>contextTransitions</c> row for every meaningful change.
///
/// <para>
/// Rich version (post-CheatSheet-menu playtest). The minimal v0.3 watcher
/// only diffed hardmode, the primary biome bit, and boss presence — too
/// coarse to reconstruct what the player was doing. This rewrite tracks:
/// </para>
///
/// <list type="bullet">
/// <item><b>Every biome bit</b> (vanilla + modded) — on/off transitions.
/// Lets a query reconstruct "the player walked through Forest → Underground
/// → Caverns → Hallow" not just the primary bit.</item>
/// <item><b>Every weather flag bit</b> — DayTime, BloodMoon, Eclipse,
/// SlimeRain, PumpkinMoon, SnowMoon, Sandstorm, BirthdayParty, Raining,
/// etc. Each flag flip is a row.</item>
/// <item><b>Vanilla invasion id</b> — Goblins, FrostLegion, Pirates,
/// Martians, OldOnesArmy.</item>
/// <item><b>Hardmode flip</b>.</item>
/// <item><b>Game mode</b> (stable per session, but recorded once).</item>
/// <item><b>Subworld key</b> — when SubworldLibrary is active.</item>
/// <item><b>Boss presence + outcome</b> — on the boss-gone edge, check
/// <see cref="Terraria.Player.dead"/> to classify the outcome
/// (killed-player / defeated-or-fled).</item>
/// <item><b>Time-of-day day/night</b> band changes.</item>
/// </list>
///
/// <para>
/// Allocation behaviour: the watcher caches the previous tick's snapshot
/// by value. The BiomeBitset is copied with <see cref="BiomeBitset.CopyFrom"/>
/// (allocation-free against the pre-sized buffer). The only allocation
/// per transition is the <see cref="ContextTransitionRow"/> being enqueued
/// to the writer — bounded to actual change events, not per-tick.
/// </para>
/// </summary>
internal sealed class ContextTransitionWatcher
{
    private bool _haveLastSnapshot;
    private bool _lastHardmode;
    private bool _lastIsDayTime;
    private bool _lastBossesPresent;
    private string _lastBossName = "";
    private BiomeBitset _lastBiomes;
    private WeatherFlags _lastWeather;
    private InvasionId _lastInvasion;
    private GameMode _lastMode;
    private int _lastSubworldKey;

    public void OnSnapshot(in EventContext ctx, double frameMs, SessionRecorder recorder)
    {
        bool isDayTime = (ctx.Weather & WeatherFlags.DayTime) != 0;
        bool currentBossesPresent = ctx.Bosses.Count > 0;
        string currentBossName = currentBossesPresent
            ? Terraria.Lang.GetNPCNameValue(ctx.Bosses[0]) ?? ("npc-" + ctx.Bosses[0])
            : "";

        if (!_haveLastSnapshot)
        {
            _lastHardmode = ctx.Hardmode;
            _lastIsDayTime = isDayTime;
            _lastBossesPresent = currentBossesPresent;
            _lastBossName = currentBossName;
            _lastBiomes = new BiomeBitset(ctx.Biomes.BitLength);
            _lastBiomes.CopyFrom(ctx.Biomes);
            _lastWeather = ctx.Weather;
            _lastInvasion = ctx.VanillaInvasion;
            _lastMode = ctx.Mode;
            _lastSubworldKey = ctx.SubworldKey;
            _haveLastSnapshot = true;

            // Emit a "session-start state" transition so the timeline always
            // has an anchor point — useful for "what was the world like when
            // the player loaded in" queries.
            recorder.OnContextTransition("session", "(open)", DescribeContext(in ctx, isDayTime),
                ctx.TickIndex, frameMs);
            return;
        }

        // Hardmode flip — once per world lifetime, but huge if it happens.
        if (ctx.Hardmode != _lastHardmode)
        {
            recorder.OnContextTransition("hardmode",
                _lastHardmode ? "true" : "false",
                ctx.Hardmode ? "true" : "false",
                ctx.TickIndex, frameMs);
            _lastHardmode = ctx.Hardmode;
        }

        // Game mode — stable per session, so usually no edge here. Recorded
        // defensively in case a mod (Journey-mode toggles, etc) flips it.
        if (ctx.Mode != _lastMode)
        {
            recorder.OnContextTransition("gameMode",
                _lastMode.ToString(), ctx.Mode.ToString(),
                ctx.TickIndex, frameMs);
            _lastMode = ctx.Mode;
        }

        // Time-of-day band: just Day / Night. Dawn/Dusk are transient single-
        // tick events and don't need their own transitions; the player thinks
        // in "is it day or night".
        if (isDayTime != _lastIsDayTime)
        {
            recorder.OnContextTransition("timeOfDay",
                _lastIsDayTime ? "Day" : "Night",
                isDayTime ? "Day" : "Night",
                ctx.TickIndex, frameMs);
            _lastIsDayTime = isDayTime;
        }

        // Subworld — SubworldLibrary key changes when entering/leaving a
        // subworld. Most sessions are 0; modded subworld entrances flip it.
        if (ctx.SubworldKey != _lastSubworldKey)
        {
            recorder.OnContextTransition("subworld",
                _lastSubworldKey.ToString(),
                ctx.SubworldKey.ToString(),
                ctx.TickIndex, frameMs);
            _lastSubworldKey = ctx.SubworldKey;
        }

        // Invasion id — None / Goblins / FrostLegion / Pirates / Martians /
        // OldOnesArmy.
        if (ctx.VanillaInvasion != _lastInvasion)
        {
            recorder.OnContextTransition("invasion",
                _lastInvasion.ToString(), ctx.VanillaInvasion.ToString(),
                ctx.TickIndex, frameMs);
            _lastInvasion = ctx.VanillaInvasion;
        }

        // Weather flags — diff bit by bit so each individual flag flip
        // (BloodMoon on, Rain off, etc.) gets its own row instead of a
        // single combined value.
        WeatherFlags weatherChanged = ctx.Weather ^ _lastWeather;
        if (weatherChanged != 0)
        {
            for (int bit = 0; bit < 16; bit++)
            {
                WeatherFlags flag = (WeatherFlags)(1 << bit);
                if ((weatherChanged & flag) == 0) continue;
                bool nowOn = (ctx.Weather & flag) != 0;
                string label = WeatherSources.DisplayName(flag);
                if (string.IsNullOrEmpty(label) || label.StartsWith("?")) continue;
                // Encode the flag identity in the transition Type so the
                // row remains queryable per-flag (Type = "weather:BloodMoon"
                // not just "weather"); pre-this-fix, every weather change
                // collapsed into an indistinguishable "(weather) on -> off"
                // row with no way to tell BloodMoon from Rain.
                recorder.OnContextTransition("weather:" + label,
                    nowOn ? "off" : "on",
                    nowOn ? "on" : "off",
                    ctx.TickIndex, frameMs);
            }
            _lastWeather = ctx.Weather;
        }

        // Biome bits — diff each bit of the bitset. Vanilla bits are biomes
        // like Forest/Snow/Underground; modded bits are at indices ≥ vanilla
        // count.
        DiffBiomeBits(in ctx.Biomes, ref _lastBiomes, recorder, ctx.TickIndex, frameMs);

        // Boss presence + outcome. When the presence flips false, classify
        // the outcome by reading the local player's death state.
        if (currentBossesPresent != _lastBossesPresent || currentBossName != _lastBossName)
        {
            if (!currentBossesPresent && _lastBossesPresent)
            {
                string outcome = ClassifyBossOutcome();
                recorder.OnContextTransition("bossEnd",
                    _lastBossName + " (" + outcome + ")",
                    "(none)",
                    ctx.TickIndex, frameMs);
            }
            else if (currentBossesPresent && !_lastBossesPresent)
            {
                recorder.OnContextTransition("bossStart",
                    "(none)", currentBossName,
                    ctx.TickIndex, frameMs);
            }
            else
            {
                // Boss swapped — one despawned, another spawned in same tick window.
                recorder.OnContextTransition("bossSwap",
                    _lastBossName, currentBossName,
                    ctx.TickIndex, frameMs);
            }
            _lastBossesPresent = currentBossesPresent;
            _lastBossName = currentBossName;
        }
    }

    /// <summary>
    /// Diff two biome bitsets and emit a transition per changed bit.
    /// v0.5 walked bit-by-bit, calling <see cref="BiomeBitset.IsSet"/> twice
    /// per index — ~680 ns per tick across 68 typical bits, 99%+ of which
    /// were no-change ticks paying for the full scan.
    ///
    /// v0.6 (events-and-context dossier R1) walks 64-bit words, XOR-diffs
    /// them, and uses <see cref="System.Numerics.BitOperations.TrailingZeroCount"/>
    /// to iterate only the changed bits. No-change ticks short-circuit on
    /// the diff==0 check; only edges pay the loop body cost.
    /// </summary>
    private static void DiffBiomeBits(in BiomeBitset current, ref BiomeBitset last,
        SessionRecorder recorder, long tick, double frameMs)
    {
        int wc = current.WordCount;
        if (last.WordCount < wc) wc = last.WordCount;
        for (int w = 0; w < wc; w++)
        {
            ulong cur = current.WordAt(w);
            ulong was = last.WordAt(w);
            ulong diff = cur ^ was;
            while (diff != 0UL)
            {
                int b = System.Numerics.BitOperations.TrailingZeroCount(diff);
                int bit = (w << 6) + b;
                ulong mask = 1UL << b;
                bool nowSet = (cur & mask) != 0UL;
                string name = BiomeRegistry.NameOrIndex(bit);
                recorder.OnContextTransition(
                    "biome",
                    nowSet ? "(off)" : name,
                    nowSet ? name : "(off)",
                    tick, frameMs);
                diff &= diff - 1UL;   // clear lowest set bit, move to next
            }
        }
        last.CopyFrom(current);
    }

    /// <summary>
    /// Decide whether a boss-end transition means the player won, lost, or
    /// the boss escaped. Reading <see cref="Terraria.Main.LocalPlayer"/> at
    /// the flip moment gives us the player's life state at that moment.
    /// </summary>
    private static string ClassifyBossOutcome()
    {
        try
        {
            var player = Terraria.Main.LocalPlayer;
            if (player == null) return "unknown";
            if (player.dead) return "killed-player";
            if (player.statLife <= 0) return "killed-player";
            // Distinguishing "defeated" from "fled" without per-NPC HP tracking
            // is unreliable — we don't have the NPC reference here. Caller can
            // tighten this by passing in the last-seen boss NPC and reading
            // its life field at the moment of despawn.
            return "boss-gone";
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>One-line summary of the context. Used as the "to" value of the session-open transition.</summary>
    private static string DescribeContext(in EventContext ctx, bool isDayTime)
    {
        string primaryBiome = BiomeRegistry.NameOrIndex(ctx.Biomes.PrimaryBitIndex());
        string mode = ctx.Mode.ToString();
        string hardmode = ctx.Hardmode ? "Hardmode" : "Pre-Hardmode";
        string time = isDayTime ? "Day" : "Night";
        return $"{primaryBiome} · {mode} · {hardmode} · {time}";
    }
}
