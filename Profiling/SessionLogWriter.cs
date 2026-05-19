#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PerformanceProfiler.Profiling;

/// <summary>
/// Writes one agent-readable JSON report for the current play session.
///
/// This is a compact export, not long-term analytics storage. It keeps a coarse
/// timeline, ranked spike summaries, and a full final mod table. The file lives
/// under the platform app-data folder so Steam Workshop installs keep reports
/// outside the mod source/build folder.
/// </summary>
public sealed class SessionLogWriter : IDisposable
{
    // Schema 3: spike rows now come from MetricCollector.Spikes (median-based
    // coalesced windows) instead of an in-writer hardcoded-threshold list.
    private const int SchemaVersion = 3;
    private const int TimelineIntervalTicks = 60 * 60;
    private const int TimelineTopMods = 10;
    private const int SpikeTopMods = 10;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };

    private readonly string _identity;
    private readonly string _currentPath;
    private readonly string _finalPath;
    private readonly DateTimeOffset _startedUtc;
    private readonly List<object> _timeline = new List<object>();

    private long _lastTimelineTick = -TimelineIntervalTicks;
    private bool _disposed;

    private SessionLogWriter(string identity, string currentPath, string finalPath, DateTimeOffset startedUtc)
    {
        _identity = identity;
        _currentPath = currentPath;
        _finalPath = finalPath;
        _startedUtc = startedUtc;
    }

    /// <summary>Creates a session report and prunes incompatible historical reports.</summary>
    public static SessionLogWriter Create()
    {
        string directory = SessionDirectory();
        Directory.CreateDirectory(directory);

        string identity = ComputeIdentity();
        PruneIncompatibleLogs(directory, identity);

        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
        string stamp = startedUtc.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        string currentPath = Path.Combine(directory, "current-session.json");
        string finalPath = Path.Combine(directory, $"{identity}-{stamp}.json");
        SessionLogWriter session = new SessionLogWriter(identity, currentPath, finalPath, startedUtc);
        session.WriteReport(final: false, collector: null);
        return session;
    }

    public void Tick(MetricCollector collector)
    {
        if (_disposed || collector.History.Count == 0)
        {
            return;
        }

        TickFrame latest = collector.History.Newest;
        if (latest.TickIndex - _lastTimelineTick >= TimelineIntervalTicks)
        {
            _timeline.Add(TimelineRow(collector, latest));
            _lastTimelineTick = latest.TickIndex;
            WriteReport(final: false, collector);
        }

        // Spikes are owned by MetricCollector now (median-based detector with
        // coalesced windows). The schema-3 SpikeObjects() reads from
        // collector.Spikes at report-write time; nothing to do per tick here.
    }

    public void End(MetricCollector collector)
    {
        if (_disposed)
        {
            return;
        }

        WriteReport(final: true, collector);
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private void WriteReport(bool final, MetricCollector? collector)
    {
        object report = new
        {
            schema = SchemaVersion,
            identity = _identity,
            state = final ? "final" : "current",
            session = new
            {
                startedUtc = _startedUtc,
                updatedUtc = DateTimeOffset.UtcNow,
                profilerVersion = ProfilerVersion(),
                modFingerprint = ModFingerprint(),
            },
            mods = Mods(),
            coverage = Coverage(),
            timeline = _timeline,
            spikes = SpikeObjects(collector),
            final = final && collector != null ? FinalSummary(collector) : null,
        };

        string json = JsonSerializer.Serialize(report, JsonOptions);
        File.WriteAllText(_currentPath, json, new UTF8Encoding(false));
        if (final)
        {
            File.WriteAllText(_finalPath, json, new UTF8Encoding(false));
        }
    }

    private static object TimelineRow(MetricCollector collector, TickFrame frame)
    {
        return new
        {
            tick = frame.TickIndex,
            timestampUnixMs = frame.TimestampUnixMs,
            frameMs = frame.FrameTimeMs,
            gcMs = frame.GcTimeMs,
            npcCount = frame.NpcCount,
            projectileCount = frame.ProjectileCount,
            dustCount = frame.DustCount,
            topMods = TopMods(collector, TimelineTopMods, averages: true),
        };
    }

    /// <summary>
    /// Renders the live spike windows from <see cref="MetricCollector.Spikes"/>
    /// into JSON-friendly objects. The per-mod breakdown comes from the
    /// SpikeWindow's frozen snapshot of the worst tick, not from the current
    /// (smoothed) collector state -- the whole point of the spike record is
    /// "what did Mod X look like AT that tick", which the live values can't
    /// answer once the moment has passed.
    /// </summary>
    private static object[] SpikeWindowsJson(MetricCollector collector)
    {
        IReadOnlyList<SpikeWindow> windows = collector.Spikes;
        object[] rows = new object[windows.Count];
        for (int i = 0; i < windows.Count; i++)
        {
            SpikeWindow w = windows[i];
            rows[i] = new
            {
                startTick = w.StartTick,
                endTick = w.EndTick,
                worstTick = w.WorstTick,
                worstFrameMs = w.WorstFrameMs,
                baselineMs = w.BaselineMs,
                madMs = w.MadMs,
                warming = w.Warming,
                context = w.ContextSummary,
                topMods = TopModsForSnapshot(w, count: SpikeTopMods),
            };
        }
        return rows;
    }

    /// <summary>
    /// Builds a "top mods at the worst tick of this spike" array from the
    /// SpikeWindow's frozen per-mod-per-category snapshot. Mirrors the shape
    /// of <see cref="TopMods"/> so consumers see a consistent structure.
    /// </summary>
    private static object[] TopModsForSnapshot(SpikeWindow window, int count)
    {
        string[] names = HookInterceptor.ProfiledModNames;
        int catCount = PerModAttribution.CategoryCount;
        int modCount = names.Length;

        // (modId, totalMs) ranking. count is small (10) so an insertion sort
        // across the worst entry holds is fine.
        int n = window.PerModCatMs.Length / catCount;
        if (n > modCount) n = modCount;

        // Build the totals array on the stack-equivalent path (small N).
        double[] totals = new double[n];
        for (int mod = 0; mod < n; mod++)
        {
            double sum = 0d;
            int baseIdx = mod * catCount;
            for (int c = 0; c < catCount; c++)
            {
                sum += window.PerModCatMs[baseIdx + c];
            }
            totals[mod] = sum;
        }

        // Pick the top `count` by totals.
        int take = count < n ? count : n;
        int[] order = new int[take];
        for (int i = 0; i < take; i++) order[i] = -1;
        for (int mod = 0; mod < n; mod++)
        {
            double v = totals[mod];
            for (int slot = 0; slot < take; slot++)
            {
                int o = order[slot];
                if (o < 0 || v > totals[o])
                {
                    for (int shift = take - 1; shift > slot; shift--) order[shift] = order[shift - 1];
                    order[slot] = mod;
                    break;
                }
            }
        }

        int written = 0;
        for (int i = 0; i < take; i++) if (order[i] >= 0) written++;

        object[] rows = new object[written];
        for (int i = 0; i < written; i++)
        {
            int mod = order[i];
            rows[i] = new
            {
                modId = mod,
                name = names[mod],
                ms = totals[mod],
            };
        }
        return rows;
    }

    private static object FinalSummary(MetricCollector collector)
    {
        return new
        {
            allMods = ModCosts(collector, topHooksPerMod: 3),
            topAverageMods = TopMods(collector, count: 10, averages: true),
            topCurrentMods = TopMods(collector, count: 10, averages: false),
            zeroCostMods = ZeroCostMods(collector),
        };
    }

    private static object Coverage()
    {
        CoverageTotals(out int total, out int measured, out int fullMods, out int partialMods);
        return new
        {
            installedHooks = PerModAttribution.HookCount,
            unsupportedHookSignatures = HookInterceptor.UnsupportedHookSignatures,
            discoveredHookOverrides = total,
            measuredHookOverrides = measured,
            coveragePercent = total == 0 ? 1d : measured / (double)total,
            fullyCoveredMods = fullMods,
            partiallyCoveredMods = partialMods,
            unsupportedSignatureFrequency = SortedSignatureFrequency(),
        };
    }

    private static Dictionary<string, int> SortedSignatureFrequency()
    {
        IReadOnlyDictionary<string, int> raw = HookInterceptor.UnsupportedSignatureFrequency;
        Dictionary<string, int> sorted = new Dictionary<string, int>(raw.Count);
        foreach (KeyValuePair<string, int> pair in raw.OrderByDescending(p => p.Value))
        {
            sorted[pair.Key] = pair.Value;
        }

        return sorted;
    }

    private static object[] SpikeObjects(MetricCollector? collector)
    {
        if (collector == null) return Array.Empty<object>();
        return SpikeWindowsJson(collector);
    }

    private static object[] Mods()
    {
        string[] names = HookInterceptor.ProfiledModNames;
        string[] versions = HookInterceptor.ProfiledModVersions;
        object[] rows = new object[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            rows[i] = new
            {
                id = i,
                name = names[i],
                version = i < versions.Length ? versions[i] : "unknown",
                coverage = CoverageForMod(i),
            };
        }

        return rows;
    }

    private static object[] ModCosts(MetricCollector collector, int topHooksPerMod)
    {
        string[] names = HookInterceptor.ProfiledModNames;
        string[] versions = HookInterceptor.ProfiledModVersions;
        object[] rows = new object[names.Length];
        for (int modId = 0; modId < names.Length; modId++)
        {
            rows[modId] = ModCost(modId, collector, versions, topHooksPerMod);
        }

        return rows;
    }

    private static object[] TopMods(MetricCollector collector, int count, bool averages)
    {
        string[] versions = HookInterceptor.ProfiledModVersions;
        int modCount = HookInterceptor.ProfiledModNames.Length;
        int[] topIds = new int[count];
        double[] topMs = new double[count];
        for (int i = 0; i < topIds.Length; i++)
        {
            topIds[i] = -1;
        }

        for (int modId = 0; modId < modCount; modId++)
        {
            double ms = ModTotal(collector, modId, averages);
            for (int slot = 0; slot < topMs.Length; slot++)
            {
                if (ms <= topMs[slot])
                {
                    continue;
                }

                for (int move = topMs.Length - 1; move > slot; move--)
                {
                    topMs[move] = topMs[move - 1];
                    topIds[move] = topIds[move - 1];
                }

                topMs[slot] = ms;
                topIds[slot] = modId;
                break;
            }
        }

        int actual = 0;
        while (actual < topIds.Length && topIds[actual] >= 0)
        {
            actual++;
        }

        object[] rows = new object[actual];
        for (int i = 0; i < actual; i++)
        {
            rows[i] = ModCost(topIds[i], collector, versions, topHooksPerMod: 3);
        }

        return rows;
    }

    private static object ModCost(int modId, MetricCollector collector, string[] versions, int topHooksPerMod)
    {
        CategoryTotals(collector, modId, out double now, out double average, out double[] categoriesNow, out double[] categoriesAverage);
        return new
        {
            id = modId,
            name = HookInterceptor.ProfiledModNames[modId],
            version = modId < versions.Length ? versions[modId] : "unknown",
            nowMs = now,
            avg30sMs = average,
            categories = CategoryRows(categoriesNow, categoriesAverage),
            topHooks = TopHooks(modId, collector, topHooksPerMod),
            coverage = CoverageForMod(modId),
        };
    }

    private static object CoverageForMod(int modId)
    {
        int measured = modId < HookInterceptor.MeasuredHookCounts.Count ? HookInterceptor.MeasuredHookCounts[modId] : 0;
        int total = modId < HookInterceptor.TotalHookCounts.Count ? HookInterceptor.TotalHookCounts[modId] : 0;
        IReadOnlyList<string> unsupported = modId < HookInterceptor.UnsupportedHookSamples.Count
            ? HookInterceptor.UnsupportedHookSamples[modId]
            : Array.Empty<string>();

        return new
        {
            measuredHooks = measured,
            totalHooks = total,
            unsupportedHooks = total - measured,
            coveragePercent = total == 0 ? 1d : measured / (double)total,
            badge = total == measured ? "full" : measured == 0 ? "unmeasured" : "partial",
            unsupportedSamples = unsupported,
        };
    }

    private static void CoverageTotals(out int total, out int measured, out int fullMods, out int partialMods)
    {
        total = 0;
        measured = 0;
        fullMods = 0;
        partialMods = 0;

        int mods = HookInterceptor.ProfiledModNames.Length;
        for (int i = 0; i < mods; i++)
        {
            int modTotal = i < HookInterceptor.TotalHookCounts.Count ? HookInterceptor.TotalHookCounts[i] : 0;
            int modMeasured = i < HookInterceptor.MeasuredHookCounts.Count ? HookInterceptor.MeasuredHookCounts[i] : 0;
            total += modTotal;
            measured += modMeasured;
            if (modTotal == modMeasured)
            {
                fullMods++;
            }
            else
            {
                partialMods++;
            }
        }
    }

    private static void CategoryTotals(MetricCollector collector, int modId,
        out double now, out double average, out double[] categoriesNow, out double[] categoriesAverage)
    {
        int catCount = PerModAttribution.CategoryCount;
        now = 0d;
        average = 0d;
        categoriesNow = new double[catCount];
        categoriesAverage = new double[catCount];
        for (int c = 0; c < catCount; c++)
        {
            int cell = modId * catCount + c;
            if (cell < collector.PerModCategoryMs.Count)
            {
                categoriesNow[c] = collector.PerModCategoryMs[cell];
                now += categoriesNow[c];
            }

            if (cell < collector.PerModCategoryAverageMs.Count)
            {
                categoriesAverage[c] = collector.PerModCategoryAverageMs[cell];
                average += categoriesAverage[c];
            }
        }
    }

    private static double ModTotal(MetricCollector collector, int modId, bool averages)
    {
        IReadOnlyList<double> values = averages ? collector.PerModCategoryAverageMs : collector.PerModCategoryMs;
        int catCount = PerModAttribution.CategoryCount;
        double total = 0d;
        for (int c = 0; c < catCount; c++)
        {
            int cell = modId * catCount + c;
            if (cell < values.Count)
            {
                total += values[cell];
            }
        }

        return total;
    }

    private static object[] CategoryRows(double[] now, double[] average)
    {
        object[] rows = new object[PerModAttribution.CategoryCount];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new
            {
                name = PerModAttribution.CategoryNames[i],
                nowMs = now[i],
                avg30sMs = average[i],
            };
        }

        return rows;
    }

    private static object[] TopHooks(int modId, MetricCollector collector, int count)
    {
        int[] topIds = new int[count];
        double[] topMs = new double[count];
        for (int i = 0; i < topIds.Length; i++)
        {
            topIds[i] = -1;
        }

        var hooks = PerModAttribution.Hooks;
        int n = hooks.Count < collector.PerHookAverageMs.Count ? hooks.Count : collector.PerHookAverageMs.Count;
        for (int i = 0; i < n; i++)
        {
            if (hooks[i].ModId != modId)
            {
                continue;
            }

            double ms = collector.PerHookAverageMs[i];
            for (int slot = 0; slot < topMs.Length; slot++)
            {
                if (ms <= topMs[slot])
                {
                    continue;
                }

                for (int move = topMs.Length - 1; move > slot; move--)
                {
                    topMs[move] = topMs[move - 1];
                    topIds[move] = topIds[move - 1];
                }

                topMs[slot] = ms;
                topIds[slot] = i;
                break;
            }
        }

        int actual = 0;
        while (actual < topIds.Length && topIds[actual] >= 0)
        {
            actual++;
        }

        object[] rows = new object[actual];
        for (int i = 0; i < actual; i++)
        {
            rows[i] = HookRow(topIds[i], topMs[i], collector);
        }

        return rows;
    }

    private static object HookRow(int hookId, double averageMs, MetricCollector collector)
    {
        HookDescriptor hook = PerModAttribution.Hooks[hookId];
        double nowMs = hookId < collector.PerHookMs.Count ? collector.PerHookMs[hookId] : 0d;
        return new
        {
            id = hookId,
            name = hook.DisplayName,
            category = PerModAttribution.CategoryNames[hook.CategoryId],
            nowMs,
            avg30sMs = averageMs,
        };
    }

    private static object[] ZeroCostMods(MetricCollector collector)
    {
        int count = 0;
        for (int modId = 0; modId < HookInterceptor.ProfiledModNames.Length; modId++)
        {
            if (ModTotal(collector, modId, averages: true) <= 0d)
            {
                count++;
            }
        }

        object[] rows = new object[count];
        int row = 0;
        string[] versions = HookInterceptor.ProfiledModVersions;
        for (int modId = 0; modId < HookInterceptor.ProfiledModNames.Length; modId++)
        {
            if (ModTotal(collector, modId, averages: true) > 0d)
            {
                continue;
            }

            rows[row++] = new
            {
                id = modId,
                name = HookInterceptor.ProfiledModNames[modId],
                version = modId < versions.Length ? versions[modId] : "unknown",
            };
        }

        return rows;
    }

    private static string SessionDirectory()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = AppContext.BaseDirectory;
        }

        return Path.Combine(root, "Terraria", "tModLoader", "PerformanceProfiler", "Sessions");
    }

    private static void PruneIncompatibleLogs(string directory, string identity)
    {
        foreach (string file in Directory.GetFiles(directory, "*.json*"))
        {
            string name = Path.GetFileName(file);
            if (name == "current-session.json")
            {
                continue;
            }

            if (!name.StartsWith(identity + "-", StringComparison.Ordinal))
            {
                File.Delete(file);
            }
        }
    }

    private static string ComputeIdentity()
    {
        return Hash($"schema={SchemaVersion};coverage={HookInterceptor.HookCoverageVersion};mods={ModFingerprint()}");
    }

    private static string ModFingerprint()
    {
        StringBuilder builder = new StringBuilder();
        string[] names = HookInterceptor.ProfiledModNames;
        string[] versions = HookInterceptor.ProfiledModVersions;
        for (int i = 0; i < names.Length; i++)
        {
            builder.Append(i).Append(':').Append(names[i]).Append('@');
            builder.Append(i < versions.Length ? versions[i] : "unknown").Append(';');
        }

        return Hash(builder.ToString());
    }

    private static string ProfilerVersion()
    {
        return typeof(SessionLogWriter).Assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private static string Hash(string text)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }
}
