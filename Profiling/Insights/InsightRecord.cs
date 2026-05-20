#nullable enable

namespace PerformanceProfiler.Profiling.Insights;

/// <summary>
/// Catalog of diagnostic patterns the engine can emit. One detector class
/// per value; the renderer selects its template family by this key. Stable
/// numeric values so the JSONL export stays self-describing across schema
/// bumps — never reorder. See <c>context/notes/insights-engine-plan.md §3.1</c>
/// for the full catalog and the rationale.
/// </summary>
public enum PatternKey : byte
{
    ContextCorrelatedSpike = 1,
    ContextConditionalCost = 2,
    HotHookDominance       = 3,
    AllocationBurst        = 4,
    GcPauseCulprit         = 5,
    SustainedCostShift     = 6,
    FreeRemovalCandidate   = 7,
    NewContributor         = 8,
    PeakContributorToSpike = 9,
    HookFrequencyTail      = 10,
}

/// <summary>
/// Strength of an insight. Drives badge colour and template hedging.
/// Preliminary is the first-fire-only state; promotion needs repeat
/// confirmations within the store's TTL window.
/// </summary>
public enum Confidence : byte { Preliminary = 0, Low = 1, Medium = 2, High = 3 }

/// <summary>
/// Who the rendered output is aimed at. Some patterns are modder-only
/// (HookFrequencyTail); some make sense to both audiences with different
/// densities. Selected at render time, never at detect time.
/// </summary>
public enum Audience : byte { Player = 0, Modder = 1, Both = 2 }

/// <summary>
/// Names what comparison the detector ran against. Required by the honesty
/// contract: every rendered insight declares its baseline so a reader can
/// argue with the comparison itself, not just the number. See plan §5.5.
/// </summary>
public enum BaselineKind : byte
{
    SessionMean         = 0,
    RollingFiveMinute   = 1,
    PreContext          = 2,
    ComparableContexts  = 3,
    SessionFirstHalf    = 4,
    PerModRollingMean   = 5,
    None                = 255,
}

/// <summary>
/// Identifies the subject of an insight. <see cref="ModId"/> is the offset
/// into <c>HookInterceptor.ProfiledModNames</c>; <see cref="HookId"/> is
/// the offset into <c>PerModAttribution.Hooks</c> (or -1 if the insight is
/// mod-level). Context fields default to (-1, 0) when the insight is not
/// context-scoped, which is the only shape currently emitted because the
/// Events tab plan has not landed yet.
/// </summary>
public readonly struct SubjectRef
{
    public readonly int ModId;
    public readonly int HookId;
    public readonly int ContextKey;
    public readonly byte ContextDim;

    public SubjectRef(int modId, int hookId, int contextKey, byte contextDim)
    {
        ModId = modId;
        HookId = hookId;
        ContextKey = contextKey;
        ContextDim = contextDim;
    }

    public static SubjectRef ForMod(int modId) => new SubjectRef(modId, -1, -1, 0);
    public static SubjectRef ForHook(int modId, int hookId) => new SubjectRef(modId, hookId, -1, 0);
}

/// <summary>
/// The numeric heart of an insight: baseline value, observed value, the
/// derived ratio or delta, and the sample count that produced them.
/// AllocBytes is non-zero only for allocation-flavoured patterns.
/// </summary>
public struct Magnitude
{
    public double BaselineMs;
    public double ObservedMs;
    public double RatioOrDelta;
    public long   AllocBytes;
    public int    Count;
}

/// <summary>
/// Statistical justification for the claim. <see cref="PValue"/> = 1.0
/// means no hypothesis test was run (binomial-only patterns or pure
/// attribution patterns like PEAK_CONTRIBUTOR_TO_SPIKE). The supporting-
/// evidence panel renders this verbatim so the reader can replicate.
/// </summary>
public struct Evidence
{
    public int    SampleN;
    public int    BaselineN;
    public double PValue;
    public double EffectSize;
    public double PValueAdjusted;
    public long   FirstTickIndex;
    public long   LastTickIndex;
    public BaselineKind Baseline;
}

/// <summary>
/// A single diagnostic statement. Produced by an <see cref="IInsightDetector"/>,
/// deduplicated and tracked by the <see cref="InsightStore"/>, ranked by
/// the scorer, and rendered by the template engine. Cached renderings live
/// on the record to avoid re-formatting per draw; they are populated lazily
/// and never serialised.
/// </summary>
public sealed class InsightRecord
{
    public PatternKey Pattern;
    public SubjectRef Subject;
    public Magnitude  Magnitude;
    public Evidence   Evidence;
    public Confidence Confidence;
    public Audience   Audience;

    public long FirstSeenTick;
    public long LastSeenTick;
    public int  ConfirmationCount;

    /// <summary>Cached short-form string for the Player audience; cleared when ranking mutates state.</summary>
    public string? CachedShortPlayer;
    /// <summary>Cached medium-form string for the Player audience; cleared when ranking mutates state.</summary>
    public string? CachedMediumPlayer;
    /// <summary>Cached long-form string for the Modder audience; cleared when ranking mutates state.</summary>
    public string? CachedLongModder;

    /// <summary>Drops cached rendered strings so the next Render rebuilds them from current state.</summary>
    public void InvalidateRenderingCache()
    {
        CachedShortPlayer = null;
        CachedMediumPlayer = null;
        CachedLongModder = null;
    }
}
