#nullable enable

using System.Linq;
using LiteDB;
using PerformanceProfiler.Profiling.Persistence.Records;
using Terraria.ModLoader;

namespace PerformanceProfiler.Profiling.Persistence.Commands;

/// <summary>
/// <c>/profiler-summary</c> — one-paragraph session summary in chat.
/// Defaults to the live session if a world is loaded, otherwise the most
/// recent session row.
/// </summary>
public class ProfilerSummaryCommand : ModCommand
{
    public override CommandType Type => CommandType.Chat;
    public override string Command => "profiler-summary";
    public override string Usage => "/profiler-summary";
    public override string Description => "Print the current (or most recent) session's totals.";

    public override void Action(CommandCaller caller, string input, string[] args) =>
        QueryCommandBase.SafeRun(caller, Command, () =>
    {
        var db = QueryCommandBase.GetDb(caller); if (db == null) return;
        var sid = QueryCommandBase.CurrentOrLatestSessionId(db);
        if (sid == null) { caller.Reply("No sessions recorded yet."); return; }

        var session = db.Sessions.FindById(sid);
        if (session == null) { caller.Reply("Session row missing."); return; }

        int spikes = db.SpikeWindows.Find(x => x.SessionId == sid).Count();
        int stalls = db.Stalls.Find(x => x.SessionId == sid).Count();
        int clusters = db.StallClusters.Find(x => x.SessionId == sid).Count();
        int deaths = db.PlayerDeaths.Find(x => x.SessionId == sid).Count();
        var archive = db.TickAggregatesArchive.FindOne(x => x.SessionId == sid);

        caller.Reply($"Session {sid}: ticks={session.TicksObserved}, dur={QueryCommandBase.FormatDuration(session.DurationMs)}, mode={session.Mode}, end={session.EndReason}");
        if (archive != null)
        {
            caller.Reply($"  frames  avg={archive.AvgFrameMs:F2}ms  max={archive.MaxFrameMs:F1}ms");
        }
        caller.Reply($"  events  spikes={spikes}  stalls={stalls}  clusters={clusters}  deaths={deaths}");

        var topMods = db.PerSessionMods.Find(x => x.SessionId == sid)
            .OrderByDescending(x => x.TotalMs).Take(3).ToList();
        if (topMods.Count > 0)
        {
            string line = "  top mods:  " + string.Join("  ·  ",
                topMods.Select(m => $"{m.ModInternalName} {m.AvgMs:F2}ms/t ({m.TotalMs:F0}ms total)"));
            caller.Reply(line);
        }
    });
}

/// <summary>
/// <c>/profiler-stalls [N]</c> — print recent stall clusters in chat.
/// Default N = 5. Each cluster shows duration, span, dominant cause,
/// dominant contributor — what the player would care about.
/// </summary>
public class ProfilerStallsCommand : ModCommand
{
    public override CommandType Type => CommandType.Chat;
    public override string Command => "profiler-stalls";
    public override string Usage => "/profiler-stalls [count]";
    public override string Description => "List recent stall clusters with attribution.";

    public override void Action(CommandCaller caller, string input, string[] args) =>
        QueryCommandBase.SafeRun(caller, Command, () =>
    {
        var db = QueryCommandBase.GetDb(caller); if (db == null) return;
        int n = QueryCommandBase.ParseCountArg(args, 5);
        var sid = QueryCommandBase.CurrentOrLatestSessionId(db);
        if (sid == null) { caller.Reply("No sessions recorded yet."); return; }

        var clusters = db.StallClusters.Find(x => x.SessionId == sid)
            .OrderByDescending(x => x.WorstDurationMs).Take(n).ToList();

        if (clusters.Count == 0) { caller.Reply("No stall clusters in this session."); return; }
        caller.Reply($"Top {clusters.Count} stall clusters by worst hitch:");
        foreach (var c in clusters)
        {
            caller.Reply(
                $"  tick {c.StartTick}-{c.EndTick}  worst={c.WorstDurationMs:F0}ms  total={c.TotalDurationMs:F0}ms over {c.SpanMs:F0}ms  " +
                $"{c.StallCount}× {c.DominantCause}  contributor: {c.DominantContributorName}");
        }
    });
}

/// <summary>
/// <c>/profiler-mods [N]</c> — top mods in the current session by total
/// CPU. Default N = 8.
/// </summary>
public class ProfilerModsCommand : ModCommand
{
    public override CommandType Type => CommandType.Chat;
    public override string Command => "profiler-mods";
    public override string Usage => "/profiler-mods [count]";
    public override string Description => "Top mods by total CPU in the current session.";

    public override void Action(CommandCaller caller, string input, string[] args) =>
        QueryCommandBase.SafeRun(caller, Command, () =>
    {
        var db = QueryCommandBase.GetDb(caller); if (db == null) return;
        int n = QueryCommandBase.ParseCountArg(args, 8);
        var sid = QueryCommandBase.CurrentOrLatestSessionId(db);
        if (sid == null) { caller.Reply("No sessions recorded yet."); return; }

        var rows = db.PerSessionMods.Find(x => x.SessionId == sid)
            .OrderByDescending(x => x.TotalMs).Take(n).ToList();
        if (rows.Count == 0) { caller.Reply("No per-mod aggregates yet."); return; }
        caller.Reply($"Top {rows.Count} mods by total CPU:");
        foreach (var m in rows)
        {
            caller.Reply($"  {m.ModInternalName,-26} avg={m.AvgMs:F2}ms/t  peak={m.PeakMs:F1}ms  total={m.TotalMs:F0}ms");
        }
    });
}

/// <summary>
/// <c>/profiler-deaths</c> — list player deaths in the current session
/// with location + boss attribution.
/// </summary>
public class ProfilerDeathsCommand : ModCommand
{
    public override CommandType Type => CommandType.Chat;
    public override string Command => "profiler-deaths";
    public override string Usage => "/profiler-deaths";
    public override string Description => "List player deaths recorded in the current session.";

    public override void Action(CommandCaller caller, string input, string[] args) =>
        QueryCommandBase.SafeRun(caller, Command, () =>
    {
        var db = QueryCommandBase.GetDb(caller); if (db == null) return;
        var sid = QueryCommandBase.CurrentOrLatestSessionId(db);
        if (sid == null) { caller.Reply("No sessions recorded yet."); return; }

        var deaths = db.PlayerDeaths.Find(x => x.SessionId == sid)
            .OrderBy(x => x.Tick).ToList();
        if (deaths.Count == 0) { caller.Reply("No deaths recorded — well played."); return; }
        caller.Reply($"{deaths.Count} death(s) this session:");
        foreach (var d in deaths)
            caller.Reply($"  tick {d.Tick}  {d.Summary}");
    });
}

/// <summary>
/// <c>/profiler-tail [N]</c> — interleaved most-recent events (stall
/// clusters, top spikes, transitions, deaths). For the "what was the
/// last weird thing that happened" use case.
/// </summary>
public class ProfilerTailCommand : ModCommand
{
    public override CommandType Type => CommandType.Chat;
    public override string Command => "profiler-tail";
    public override string Usage => "/profiler-tail [count]";
    public override string Description => "Recent events (stalls, spikes, transitions, deaths).";

    public override void Action(CommandCaller caller, string input, string[] args) =>
        QueryCommandBase.SafeRun(caller, Command, () =>
    {
        var db = QueryCommandBase.GetDb(caller); if (db == null) return;
        int n = QueryCommandBase.ParseCountArg(args, 8);
        var sid = QueryCommandBase.CurrentOrLatestSessionId(db);
        if (sid == null) { caller.Reply("No sessions recorded yet."); return; }

        // Recent of each kind, merged + sorted by tick.
        var clusters = db.StallClusters.Find(x => x.SessionId == sid)
            .OrderByDescending(x => x.EndUnixMs).Take(n).ToList();
        var spikes = db.SpikeWindows.Find(x => x.SessionId == sid)
            .OrderByDescending(x => x.WorstTick).Take(n).ToList();
        var transitions = db.ContextTransitions.Find(x => x.SessionId == sid)
            .OrderByDescending(x => x.Tick).Take(n).ToList();
        var deaths = db.PlayerDeaths.Find(x => x.SessionId == sid)
            .OrderByDescending(x => x.Tick).Take(n).ToList();

        var lines = new System.Collections.Generic.List<(long tick, string text)>();
        foreach (var c in clusters)
            lines.Add((c.StartTick,
                $"stall × {c.StallCount}  worst={c.WorstDurationMs:F0}ms  cause={c.DominantCause}  {c.DominantContributorName}"));
        foreach (var s in spikes)
        {
            string top = (s.TopContributors != null && s.TopContributors.Count > 0)
                ? s.TopContributors[0].Name + " " + s.TopContributors[0].Ms.ToString("F1") + "ms"
                : "(no attribution)";
            lines.Add((s.WorstTick, $"spike  worst={s.WorstFrameMs:F1}ms  top={top}"));
        }
        foreach (var t in transitions)
            lines.Add((t.Tick, $"ctx[{t.Type}]: {t.From} -> {t.To}"));
        foreach (var d in deaths)
            lines.Add((d.Tick, $"death  {d.Summary}"));

        lines.Sort((a, b) => b.tick.CompareTo(a.tick));
        if (lines.Count > n) lines = lines.GetRange(0, n);

        if (lines.Count == 0) { caller.Reply("No events yet."); return; }
        caller.Reply($"Recent {lines.Count} events:");
        foreach (var (tick, text) in lines)
            caller.Reply($"  tick {tick,6}  {text}");
    });
}
