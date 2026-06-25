#nullable enable

using System.Collections.Generic;
using LiteDB;

using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Collectors;
namespace PerformanceProfiler.Persistence.Streams;

/// <summary>
/// One stream = one logical group of related <see cref="DbWriteOp"/> kinds
/// that share a journal-replay shape, an index set, and an apply path.
///
/// The persistence layer is composed of independent streams plugged into the
/// <see cref="StreamRegistry"/> at construction. Adding a new collection
/// later is a single-file change: implement <see cref="IPersistenceStream"/>,
/// register the stream in <see cref="StreamRegistry.Default"/>. The facade
/// (<see cref="ProfilerDatabase"/>) is read-only with respect to stream
/// additions — no central switch, no per-kind branching in the facade.
///
/// Streams MUST be:
/// <list type="bullet">
/// <item>idempotent on apply — replay re-runs ops that already landed and
/// must produce the same DB state.</item>
/// <item>self-contained — a stream's <see cref="EnsureIndexes"/> declares
/// every index its queries need; the facade does not own a global index
/// list.</item>
/// <item>tolerant of partial payloads on reconstruct — a torn journal line
/// is detected at the facade level and skipped, but if a payload is
/// well-formed and contains an unknown field, the stream should ignore the
/// field (forward-compatible).</item>
/// </list>
/// </summary>
public interface IPersistenceStream
{
    /// <summary>Human-readable name used in logs.</summary>
    string Name { get; }

    /// <summary>
    /// The <see cref="DbOpKind"/>s this stream handles. The registry uses
    /// this list to dispatch ops to the right stream.
    /// </summary>
    IReadOnlyList<DbOpKind> Kinds { get; }

    /// <summary>
    /// Apply one op to the DB. Called from the writer thread. Must be
    /// idempotent (journal replay re-applies committed ops).
    /// </summary>
    void Apply(in DbWriteOp op, ProfilerDatabase db);

    /// <summary>
    /// Reconstruct a <see cref="DbWriteOp"/> from a journal line. Return
    /// <c>null</c> if the line's kind isn't one this stream handles (the
    /// registry only calls the right stream, but defensive nulls are fine).
    /// Throw on malformed payloads; the facade catches and skips.
    /// </summary>
    DbWriteOp? Reconstruct(JournalLine line);

    /// <summary>
    /// Create whatever LiteDB indexes this stream's queries need. Called
    /// once at <see cref="ProfilerDatabase"/> open time, after the schema
    /// version is reconciled. Must be idempotent (<c>EnsureIndex</c> is).
    /// </summary>
    void EnsureIndexes(ProfilerDatabase db);
}
