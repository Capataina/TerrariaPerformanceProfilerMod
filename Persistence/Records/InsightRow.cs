#nullable enable

using System.Collections.Generic;
using LiteDB;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
namespace PerformanceProfiler.Persistence.Records;

/// <summary>One row per surfaced insight. Schema placeholder for the M4+ live store.</summary>
public sealed class InsightRow
{
    [BsonId] public ObjectId Id { get; set; } = ObjectId.NewObjectId();
    [BsonField("_schema")] public int Schema { get; set; } = 1;

    public ObjectId SessionId { get; set; } = ObjectId.Empty;
    public string PatternKey { get; set; } = string.Empty;
    public string Audience { get; set; } = "player";
    public string RenderedShort { get; set; } = string.Empty;
    public string RenderedLong { get; set; } = string.Empty;
    public string Confidence { get; set; } = "preliminary";
    public string EvidenceScope { get; set; } = "ThisSession";
    public double PValueAdjusted { get; set; } = 1.0;
    public long FirstSeenTick { get; set; }
    public long LastConfirmedTick { get; set; }

    /// <summary>For roster patterns (e.g. CostConcentration): mods loaded this session and
    /// mods that did measurable work. Lets the archive say "3 of 26 active (29 loaded, 3 idle)"
    /// instead of a bare count. 0 on rows from patterns with no roster, and on legacy rows
    /// (additive; LiteDB defaults the missing field to 0).</summary>
    public int LoadedModCount { get; set; }
    public int ActiveModCount { get; set; }

    /// <summary>The named contributors behind an aggregate insight (the top-N mods, each with
    /// its value and share), resolved to display names at session-end where mod names exist.
    /// Empty for single-subject patterns and legacy rows.</summary>
    public List<InsightContributorRow> Contributors { get; set; } = new();
}

/// <summary>One named participant of an aggregate <see cref="InsightRow"/>: the persisted,
/// name-resolved twin of <c>Insights.InsightContributor</c> (the live struct carries a
/// numeric ModId that means nothing across sessions, so the name is resolved before storage).</summary>
public sealed class InsightContributorRow
{
    public string ModName { get; set; } = string.Empty;
    /// <summary>The contributor's raw value in the aggregate's unit (e.g. ms/tick).</summary>
    public double Value { get; set; }
    /// <summary>The contributor's share of the aggregate, a fraction in [0,1].</summary>
    public double Share { get; set; }
}
