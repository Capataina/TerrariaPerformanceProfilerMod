#nullable enable

using System;
using System.Collections.Generic;
using PerformanceProfiler.Profiling.Persistence.Streams;

namespace PerformanceProfiler.Profiling.Persistence;

/// <summary>
/// Central registration point for every <see cref="IPersistenceStream"/>.
/// Adding a new stream is a one-line change in <see cref="Default"/>; the
/// facade discovers everything through this registry.
///
/// The registry builds a kind → stream map at construction so dispatch is
/// O(1). Multiple kinds can map to the same stream (e.g. session-start
/// and session-end both live on <see cref="SessionStream"/>).
/// </summary>
public sealed class StreamRegistry
{
    private readonly IReadOnlyList<IPersistenceStream> _streams;
    private readonly IPersistenceStream?[] _byKind;

    public IReadOnlyList<IPersistenceStream> Streams => _streams;

    public StreamRegistry(IReadOnlyList<IPersistenceStream> streams)
    {
        _streams = streams;
        int max = 0;
        foreach (var s in streams)
            foreach (var k in s.Kinds)
                if ((int)k > max) max = (int)k;
        _byKind = new IPersistenceStream?[max + 1];

        foreach (var s in streams)
        {
            foreach (var k in s.Kinds)
            {
                if (_byKind[(int)k] != null)
                {
                    throw new InvalidOperationException(
                        $"Stream registration collision: {k} is claimed by both '{_byKind[(int)k]!.Name}' and '{s.Name}'.");
                }
                _byKind[(int)k] = s;
            }
        }
    }

    public IPersistenceStream? Lookup(DbOpKind kind)
    {
        int idx = (int)kind;
        if (idx < 0 || idx >= _byKind.Length) return null;
        return _byKind[idx];
    }

    /// <summary>
    /// The default registry used by <see cref="ProfilerDatabase"/>. Each
    /// stream is a single file under <c>Streams/</c>. Order in this list
    /// does not affect dispatch (kind→stream is a map); it does affect the
    /// order <see cref="IPersistenceStream.EnsureIndexes"/> runs in.
    /// </summary>
    public static StreamRegistry Default()
    {
        return new StreamRegistry(new IPersistenceStream[]
        {
            new SessionStream(),
            new SpikeStream(),
            new StallStream(),
            new StallClusterStream(),
            new ContextTransitionStream(),
            new TickAggregateStream(),
            new PerSessionAggregateStream(),
            new ModlistStream(),
            new InsightStream(),
            new PlayerDeathStream(),
            new WorldSnapshotStream(),
            new DamageTakenStream(),
            new DamageDealtStream(),
            new NpcSpawnStream(),
            new ItemCreatedStream(),
            new LoadoutSnapshotStream(),
            new BuffEventStream(),
        });
    }
}
