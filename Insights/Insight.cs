#nullable enable

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Insights;

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
    LoadoutCorrelatedCost  = 11,
    EventConditionalCost   = 12,
    LoadoutCombinationCost = 13,
    // v0.7 segment-driven patterns.
    SegmentOutlier         = 14,
    SegmentTopMod          = 15,
    SegmentDeathCorrelation = 16,
    // Wave 5 family detectors: D (headroom relative to a ceiling),
    // E (structure across signals), C (distribution shape).
    FrameHeadroom          = 17,
    CostConcentration      = 18,
    FrameJitter            = 19,
    // Wave 5 family B (behaviour over time): the runtime heap-leak signal.
    HeapLeak               = 20,
}

/// <summary>
/// Strength of an insight. Drives badge colour and template hedging.
/// Preliminary is the first-fire-only state; promotion needs repeat
/// confirmations within the store's TTL window.
/// </summary>
public enum Confidence : byte { Preliminary = 0, Low = 1, Medium = 2, High = 3 }

/// <summary>
/// Data-strength badge orthogonal to <see cref="Confidence"/>. A record can be
/// statistically tight (High confidence) within a single session and still be
/// weaker than lifetime data accumulated across many sessions; the honesty
/// contract requires the UI to make that distinction visible (README §insights).
///
/// <list type="bullet">
///   <item><b>ThisSession</b> — every signal sourced from the current world only.
///   The current default for in-scope detectors.</item>
///   <item><b>LifetimeData</b> — record draws on prior sessions retained via
///   the persistence layer (LiteDB; not yet wired).</item>
///   <item><b>NeedsPersistence</b> — the detector has a real claim it cannot
///   substantiate without persistence (e.g. NEW_CONTRIBUTOR, FREE_REMOVAL).
///   Renders with a "needs persistence" hedge until LiteDB lands.</item>
/// </list>
/// </summary>
public enum EvidenceScope : byte
{
    ThisSession      = 0,
    LifetimeData     = 1,
    NeedsPersistence = 2,
}

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
/// What kind of thing an insight is about. Mod/Hook/Context are the per-entity
/// subjects the original detectors emit; Session/Runtime/Machine are the
/// process-level subjects Family B (restart, leak, warmup) needs — a heap leak
/// is about the runtime, not a mod, and could not be expressed before. Stable
/// numeric values — never reorder. Part of the dedup key, so a Session-subject
/// and a Mod-subject with the same (now -1) ids never collide.
/// </summary>
public enum SubjectKind : byte
{
    Mod     = 0,
    Hook    = 1,
    Context = 2,
    Session = 3,
    Runtime = 4,
    Machine = 5,
}

/// <summary>
/// Identifies the subject of an insight. <see cref="ModId"/> is the offset into
/// <c>HookInterceptor.ProfiledModNames</c>; <see cref="HookId"/> is the offset
/// into <c>PerModAttribution.Hooks</c> (or -1 if mod-level); <see cref="ContextKey"/>
/// / <see cref="ContextDim"/> identify a game context (a segment, a biome bit).
/// <see cref="Kind"/> disambiguates which fields are meaningful and carries the
/// process-level subjects that have no entity id at all.
/// </summary>
public readonly struct SubjectRef
{
    public readonly SubjectKind Kind;
    public readonly int ModId;
    public readonly int HookId;
    public readonly int ContextKey;
    public readonly byte ContextDim;

    public SubjectRef(SubjectKind kind, int modId, int hookId, int contextKey, byte contextDim)
    {
        Kind = kind;
        ModId = modId;
        HookId = hookId;
        ContextKey = contextKey;
        ContextDim = contextDim;
    }

    public static SubjectRef ForMod(int modId) => new SubjectRef(SubjectKind.Mod, modId, -1, -1, 0);
    public static SubjectRef ForHook(int modId, int hookId) => new SubjectRef(SubjectKind.Hook, modId, hookId, -1, 0);
    public static SubjectRef ForContext(int contextKey, byte contextDim) => new SubjectRef(SubjectKind.Context, -1, -1, contextKey, contextDim);
    /// <summary>The whole play session (e.g. "this session ran 34% above your boss-fight baseline").</summary>
    public static SubjectRef ForSession() => new SubjectRef(SubjectKind.Session, -1, -1, -1, 0);
    /// <summary>The .NET runtime / process (heap, GC) — Family B leak/warmup.</summary>
    public static SubjectRef ForRuntime() => new SubjectRef(SubjectKind.Runtime, -1, -1, -1, 0);
    /// <summary>The machine / modlist fingerprint — cross-session durability subjects.</summary>
    public static SubjectRef ForMachine() => new SubjectRef(SubjectKind.Machine, -1, -1, -1, 0);
}

/// <summary>
/// The numeric shape an insight's <see cref="Magnitude"/> carries. The five
/// families need different numbers (a deviation, a rate, a scaling law, a
/// headroom, a distribution), so the meaningful <see cref="Magnitude"/> fields
/// are keyed off this tag rather than forcing every family into the
/// baseline/observed/ratio mould. <see cref="Deviation"/> is the default and the
/// only shape the pre-rework detectors emit, so an unset shape reads as a classic
/// baseline-vs-observed record. Stable numeric values — never reorder.
/// </summary>
public enum MagnitudeShape : byte
{
    /// <summary>Family A: baseline vs observed, expressed in <see cref="Magnitude.RatioOrDelta"/>.</summary>
    Deviation    = 0,
    /// <summary>Share patterns: <see cref="Magnitude.RatioOrDelta"/> is a fraction in [0,1].</summary>
    Share        = 1,
    /// <summary>Family B: a change per unit time/work, carried in <see cref="Magnitude.Rate"/>.</summary>
    Rate         = 2,
    /// <summary>Family E: observed ≈ Slope·driver + Intercept up to <see cref="Magnitude.CliffAt"/>.</summary>
    Scaling      = 3,
    /// <summary>Family D: how much of a <see cref="Magnitude.Ceiling"/> is <see cref="Magnitude.Remaining"/>.</summary>
    Headroom     = 4,
    /// <summary>Family C: the percentile / modality / recovery shape of a distribution.</summary>
    Distribution = 5,
}

/// <summary>
/// The numeric heart of an insight. The Deviation block (baseline, observed,
/// ratio-or-delta, bytes, count) is what every Family-A and share pattern uses
/// and what the original detectors populate. The shape-specific blocks are
/// populated only when <see cref="Shape"/> selects them, so a Temporal /
/// Distribution / Headroom / Scaling insight carries its own number honestly
/// instead of being squeezed into a ratio. Adding fields is cheap: an
/// <c>Insight</c> is a class allocated off-thread at ≤1 Hz, never per tick.
/// </summary>
public struct Magnitude
{
    /// <summary>Which fields below are meaningful. Default <see cref="MagnitudeShape.Deviation"/>.</summary>
    public MagnitudeShape Shape;

    // ---- Deviation / Share (the original block) ----
    public double BaselineMs;
    public double ObservedMs;
    public double RatioOrDelta;
    public long   AllocBytes;
    public int    Count;

    // ---- Rate (Family B: drift / leak / warmup) ----
    /// <summary>Change per unit (e.g. heap MB per minute, ms per 1000 ticks).</summary>
    public double Rate;

    // ---- Scaling (Family E: cost ~ driver) ----
    /// <summary>observed ≈ <see cref="Slope"/>·driver + <see cref="Intercept"/>.</summary>
    public double Slope;
    /// <summary>The fixed cost at driver = 0.</summary>
    public double Intercept;
    /// <summary>The driver value past which the linear fit breaks (a cliff); 0 if none observed.</summary>
    public double CliffAt;

    // ---- Headroom (Family D: relative to a ceiling) ----
    /// <summary>The limit the signal is measured against (e.g. the 16.6 ms frame budget, a RAM ceiling).</summary>
    public double Ceiling;
    /// <summary>How much of <see cref="Ceiling"/> is still free.</summary>
    public double Remaining;

    // ---- Distribution (Family C: shape of the spread) ----
    public double P50;
    public double P90;
    public double P99;
    /// <summary>Number of modes (1 = unimodal, 2 = bimodal "stutter vs slowdown", …).</summary>
    public int    Modality;
    /// <summary>Median time to return to baseline after a disturbance, in ms.</summary>
    public double RecoveryMs;
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
public sealed class Insight
{
    public PatternKey Pattern;
    public SubjectRef Subject;
    public Magnitude  Magnitude;
    public Evidence   Evidence;
    public Confidence Confidence;
    public Audience   Audience;

    /// <summary>
    /// Data-strength badge — see <see cref="EvidenceScope"/>. Defaults to
    /// <see cref="EvidenceScope.ThisSession"/>; detectors that need lifetime
    /// data they don't have yet (FREE_REMOVAL, NEW_CONTRIBUTOR) set
    /// <see cref="EvidenceScope.NeedsPersistence"/> so the UI hedge is
    /// deterministic, not template-driven.
    /// </summary>
    public EvidenceScope Scope;

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
