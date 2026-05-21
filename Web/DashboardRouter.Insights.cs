#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Detectors.Insights;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Web.Server;

namespace PerformanceProfiler.Web;

internal static partial class DashboardRouter
{
    // ----------------------------------------------------------------------
    // /api/insights — live insight records from InsightsEngine.
    // ----------------------------------------------------------------------
    private static string BuildInsights()
    {
        // Migration step 11 — insights via registry.
        var snap = Data.DataRegistry.Shared
            .Lookup<Data.Stats.InsightsSnapshot>(Data.Stats.InsightsStat.StreamName)?
            .CurrentSnapshot() ?? Data.Stats.InsightsSnapshot.Empty;
        if (!snap.WorldLoaded || snap.Live == null)
        {
            return JsonSerializer.Serialize(new { worldLoaded = false, records = Array.Empty<object>() }, JsonOpts);
        }

        string[] modNames = HookInterceptor.ProfiledModNames;
        var records = new List<object>();
        foreach (var rec in snap.Live)
        {
            string subjectName = rec.Subject.ModId >= 0 && rec.Subject.ModId < modNames.Length
                ? modNames[rec.Subject.ModId]
                : null!;
            records.Add(new
            {
                pattern = rec.Pattern.ToString(),
                confidence = rec.Confidence.ToString(),
                scope = rec.Scope.ToString(),
                audience = rec.Audience.ToString(),
                shortText = InsightRenderer.Render(rec, Audience.Player, Density.Short),
                mediumText = InsightRenderer.Render(rec, Audience.Player, Density.Medium),
                subjectModId = rec.Subject.ModId,
                subjectModName = subjectName,
                observedMs = rec.Magnitude.ObservedMs,
                baselineMs = rec.Magnitude.BaselineMs,
                ratioOrDelta = rec.Magnitude.RatioOrDelta,
                firstSeenTick = rec.FirstSeenTick,
                lastSeenTick = rec.LastSeenTick,
                confirmationCount = rec.ConfirmationCount,
            });
        }

        return JsonSerializer.Serialize(new
        {
            worldLoaded = true,
            records,
        }, JsonOpts);
    }
}
