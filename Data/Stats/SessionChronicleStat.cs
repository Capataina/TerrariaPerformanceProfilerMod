#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using LiteDB;
using Terraria.ModLoader;

using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;

namespace PerformanceProfiler.Data.Stats;

/// <summary>
/// Timeline T7 — single-sentence per-event chronicle of the live session.
///
/// <para>
/// Reads four DB collections (sessions, contextTransitions, segments, player
/// deaths, spike windows) and projects each notable event into a
/// <see cref="ChronicleLine"/> with one descriptive sentence.
/// </para>
///
/// <para>
/// <b>Invariant 3 (descriptive, not normative).</b> Every templated sentence
/// in this file is factual: "crossed into Jungle", "boss-start Eye of
/// Cthulhu", "died (Eye of Cthulhu)", "spike at 142.3 ms". Words like
/// "lost", "won", "clean", "messy", "great", "should" are forbidden. A
/// future vocabulary lint will scan these strings; keep them factual.
/// </para>
///
/// <para>
/// All inputs only carry tick indices, so UnixMs is derived from the
/// session's <see cref="SessionRow.StartedUtc"/> + <c>tick × (1000/60)</c>.
/// </para>
/// </summary>
public sealed class SessionChronicleStat : IDataStat<SessionChronicleSnapshot>
{
    /// <summary>Cap on lines emitted per session. The live tab does not need more.</summary>
    public const int MaxLines = 100;

    /// <summary>Spike-frame threshold (ms) above which a spike rates a chronicle line.</summary>
    public const double SpikeMsThreshold = 100d;

    public string Name => RolloutStreamNames.SessionChronicle;
    public DataStreamCadence Cadence => DataStreamCadence.OnDemand;
    public DataStage Stage => DataStage.Stat;

    public void Initialise(SessionContext session) { }
    public void Reset() { }
    public void Dispose() { }

    public SessionChronicleSnapshot CurrentSnapshot()
    {
        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        ProfilerDatabase? db = PerformanceProfiler.Database;
        ObjectId? sid = sys?.LiveRecorderSessionId;
        if (db == null || sid == null)
        {
            return SessionChronicleSnapshot.Empty;
        }

        // Anchor wall-clock to SessionRow.StartedUtc.
        long sessionStartUnixMs;
        SessionRow? sessionRow = null;
        try
        {
            sessionRow = db.Sessions.FindById(sid);
            sessionStartUnixMs = (sessionRow != null && sessionRow.StartedUtc.Year > 2000)
                ? new DateTimeOffset(DateTime.SpecifyKind(sessionRow.StartedUtc, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
                : Time.UnixMsNow();
        }
        catch
        {
            sessionStartUnixMs = Time.UnixMsNow();
        }

        List<ChronicleLine> lines = new(128);

        // Join line — descriptive: "joined session at tick 0".
        if (sessionRow != null)
        {
            lines.Add(new ChronicleLine(
                UnixMs: sessionStartUnixMs,
                TickIndex: 0L,
                Kind: "join",
                Text: "joined session"));
        }

        // Context transitions — one per row, templated by Type.
        try
        {
            foreach (ContextTransitionRow t in db.ContextTransitions.Find(x => x.SessionId == sid))
            {
                long unixMs = sessionStartUnixMs + (t.Tick * 1000L) / 60L;
                string text = FormatTransition(t);
                if (string.IsNullOrEmpty(text)) continue;
                string kind = ClassifyTransitionKind(t.Type ?? string.Empty);
                lines.Add(new ChronicleLine(
                    UnixMs: unixMs,
                    TickIndex: t.Tick,
                    Kind: kind,
                    Text: text));
            }
        }
        catch { }

        // Player deaths.
        try
        {
            foreach (PlayerDeathRow d in db.PlayerDeaths.Find(x => x.SessionId == sid))
            {
                string cause = "(unknown)";
                if (d.DamageWeighting != null && d.DamageWeighting.Count > 0)
                {
                    DeathDamageContributor top = d.DamageWeighting[0];
                    cause = string.IsNullOrEmpty(top.SourceName) ? top.SourceKind : top.SourceName;
                }
                else if (!string.IsNullOrEmpty(d.PrimaryBoss) && d.PrimaryBoss != "(no boss)")
                {
                    cause = d.PrimaryBoss;
                }
                lines.Add(new ChronicleLine(
                    UnixMs: d.UnixMs,
                    TickIndex: d.Tick,
                    Kind: "death",
                    Text: $"died ({cause})"));
            }
        }
        catch { }

        // Spike windows above threshold.
        try
        {
            foreach (SpikeWindowRow sp in db.SpikeWindows.Find(x => x.SessionId == sid))
            {
                if (sp.WorstFrameMs < SpikeMsThreshold) continue;
                long unixMs = sessionStartUnixMs + (sp.WorstTick * 1000L) / 60L;
                lines.Add(new ChronicleLine(
                    UnixMs: unixMs,
                    TickIndex: sp.WorstTick,
                    Kind: "spike",
                    Text: $"spike at {sp.WorstFrameMs:F1} ms"));
            }
        }
        catch { }

        // Boss segments — open / close.
        try
        {
            foreach (SegmentRow seg in db.Segments.Find(x => x.SessionId == sid))
            {
                if (seg.Family != (byte)Aggregators.Segments.SegmentFamily.Boss) continue;
                string label = string.IsNullOrEmpty(seg.Name) ? ("npc-" + seg.Key) : seg.Name;
                lines.Add(new ChronicleLine(
                    UnixMs: sessionStartUnixMs + (seg.StartTick * 1000L) / 60L,
                    TickIndex: seg.StartTick,
                    Kind: "boss-start",
                    Text: $"boss-start {label}"));
                lines.Add(new ChronicleLine(
                    UnixMs: sessionStartUnixMs + (seg.EndTick * 1000L) / 60L,
                    TickIndex: seg.EndTick,
                    Kind: "boss-end",
                    Text: $"boss-end {label}"));
            }
        }
        catch { }

        // Sort by UnixMs ascending; cap at MaxLines (keep earliest entries —
        // the join line stays first, and the truncation drops the long tail).
        lines.Sort(static (a, b) => a.UnixMs.CompareTo(b.UnixMs));
        if (lines.Count > MaxLines)
        {
            // Keep the most recent MaxLines so the live tab always shows the
            // latest activity; the join line is preserved at index 0 if it
            // is still within the window.
            lines = lines.GetRange(lines.Count - MaxLines, MaxLines);
        }

        return new SessionChronicleSnapshot(worldLoaded: sys?.Collector != null, lines);
    }

    public object CurrentSnapshotBoxed() => CurrentSnapshot();

    /// <summary>
    /// Map a <see cref="ContextTransitionRow.Type"/> to a Chronicle kind.
    /// Descriptive only; no normative coupling.
    /// </summary>
    private static string ClassifyTransitionKind(string type)
    {
        if (type.StartsWith("weather:", StringComparison.Ordinal)) return "weather";
        return type switch
        {
            "biome"     => "transition",
            "hardmode"  => "transition",
            "invasion"  => "transition",
            "bossStart" => "boss-start",
            "bossEnd"   => "boss-end",
            "bossSwap"  => "transition",
            "timeOfDay" => "transition",
            "subworld"  => "transition",
            "gameMode"  => "transition",
            "session"   => "join",
            _ => "transition",
        };
    }

    /// <summary>
    /// Descriptive sentence template for a context transition. Returns empty
    /// when the row should be skipped.
    /// </summary>
    private static string FormatTransition(ContextTransitionRow t)
    {
        string type = t.Type ?? string.Empty;
        string from = t.From ?? string.Empty;
        string to = t.To ?? string.Empty;

        // Weather flag flips encode flag identity in the Type ("weather:Rain").
        if (type.StartsWith("weather:", StringComparison.Ordinal))
        {
            string flag = type.Substring("weather:".Length);
            // To is "on" / "off" — descriptive: "BloodMoon turned on".
            return $"{flag} turned {to}";
        }

        switch (type)
        {
            case "biome":
                // "(off)" is the placeholder the watcher emits for the empty side.
                if (to == "(off)") return $"left {from}";
                return $"crossed into {to}";
            case "hardmode":
                return to == "true" ? "hardmode flipped on" : "hardmode flipped off";
            case "invasion":
                if (to == "None" || string.IsNullOrEmpty(to)) return $"invasion {from} ended";
                return $"invasion {to} started";
            case "bossStart":
                return $"boss-start {to}";
            case "bossEnd":
                return $"boss-end {from}";
            case "bossSwap":
                return $"boss swapped {from} → {to}";
            case "timeOfDay":
                return $"time-of-day {to}";
            case "subworld":
                return $"subworld {from} → {to}";
            case "gameMode":
                return $"game-mode {from} → {to}";
            case "session":
                return $"session opened: {to}";
            default:
                return $"{type} {from} → {to}";
        }
    }
}
