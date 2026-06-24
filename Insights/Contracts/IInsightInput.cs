#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Data.Stats;

namespace PerformanceProfiler.Insights;

/// <summary>
/// The processed-pipeline view a detector, reference frame, or driver may read.
/// Formalises the input contract the engine already honours by convention: the
/// interpretation layer depends on smoothed / aggregated pipeline outputs and the
/// session baseline, never on collection internals (no raw rings, no hook-dispatch
/// state). <see cref="PerformanceProfiler.Profiling.MetricCollector"/> is the
/// production implementation (wired in Wave 3); defining the surface as an
/// interface keeps reference frames and drivers unit-testable against synthetic
/// input — the L1 axis of context/plans/extensive-testing-infrastructure.md —
/// without a running game.
///
/// <para>
/// This is intentionally a small, additive surface: it carries exactly what the
/// substrate needs today and grows as later waves add frames and drivers, never
/// the whole MetricCollector. Index convention matches the pipeline:
/// per-(mod,category) arrays are row-major <c>[modId * CategoryCount + categoryId]</c>;
/// per-hook arrays are indexed by hookId.
/// </para>
/// </summary>
public interface IInsightInput
{
    /// <summary>Rolling ~30 s per-mod-by-category CPU cost (ms), row-major.</summary>
    IReadOnlyList<double> PerModCategoryAverageMs { get; }

    /// <summary>Rolling ~30 s per-hook CPU cost (ms), indexed by hookId.</summary>
    IReadOnlyList<double> PerHookAverageMs { get; }

    /// <summary>Rolling ~30 s per-mod-by-category allocation (bytes); null when allocation tracking is off.</summary>
    IReadOnlyList<double>? PerModCategoryAverageBytes { get; }

    /// <summary>True when this session is tracking allocations (Standard / Deep modes).</summary>
    bool TracksAllocations { get; }

    /// <summary>The single-session frame / period / allocation baseline (histogram median + MAD).</summary>
    Baseline Baseline { get; }
}
