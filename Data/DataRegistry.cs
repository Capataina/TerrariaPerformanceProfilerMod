#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Data;

/// <summary>
/// The unified data pipeline registry. Every collector, aggregator, stat,
/// detector, and exporter is registered here at construction; consumers
/// (the dashboard router, the future session-report exporter, a future
/// <c>Mod.Call</c> API) iterate <see cref="All"/> or use <see cref="Lookup"/>
/// instead of reaching into named subsystems.
///
/// <para>
/// <see cref="Shared"/> is the process-wide instance. The registry is
/// populated by <c>PerformanceProfiler.RegisterDataPipeline</c> (mod-load
/// time, before any world exists) and re-Initialised at every world-load
/// via <see cref="InitialiseAll"/>.
/// </para>
///
/// <para>
/// Per-tick callbacks are captured into a frozen <see cref="PerTickCallbacks"/>
/// array after <see cref="Freeze"/> runs. The per-tick driver iterates the
/// array with a <c>for</c> loop and a static-delegate invocation — no
/// virtual dispatch, no allocator pressure.
/// </para>
/// </summary>
public sealed class DataRegistry
{
    /// <summary>Process-wide registry instance.</summary>
    public static DataRegistry Shared { get; } = new DataRegistry();

    private readonly ConcurrentDictionary<string, IDataStream> _byName
        = new(StringComparer.Ordinal);
    private readonly List<IDataStream> _ordered = new();
    private readonly object _gate = new();

    /// <summary>
    /// Frozen array of per-tick callbacks. Populated by <see cref="Freeze"/>
    /// after every stream has been registered + Initialised. The per-tick
    /// driver iterates this array directly; callers MUST NOT mutate the
    /// array.
    /// </summary>
    public TickCapture[] PerTickCallbacks { get; private set; } = Array.Empty<TickCapture>();

    /// <summary>
    /// Register a stream. Collisions on <see cref="IDataStream.Name"/> throw.
    /// The dictionary insert and the ordered-list append run under the same
    /// lock so a concurrent <see cref="DisposeAll"/> can never observe one
    /// without the other.
    /// </summary>
    public void Register(IDataStream stream)
    {
        lock (_gate)
        {
            if (!_byName.TryAdd(stream.Name, stream))
            {
                throw new InvalidOperationException(
                    $"Data stream collision: '{stream.Name}' already registered.");
            }
            _ordered.Add(stream);
        }
    }

    /// <summary>Untyped lookup by registry key. <c>null</c> when not registered.</summary>
    public IDataStream? Lookup(string name)
        => _byName.TryGetValue(name, out var s) ? s : null;

    /// <summary>Typed lookup. <c>null</c> when not registered OR registered with a different <typeparamref name="TSnapshot"/>.</summary>
    public IDataStream<TSnapshot>? Lookup<TSnapshot>(string name)
        => _byName.TryGetValue(name, out var s) ? s as IDataStream<TSnapshot> : null;

    /// <summary>Snapshot of every registered stream in registration order. Safe to iterate concurrently with <see cref="Register"/>.</summary>
    public IReadOnlyList<IDataStream> All
    {
        get { lock (_gate) return _ordered.ToArray(); }
    }

    /// <summary>
    /// Recompute the <see cref="PerTickCallbacks"/> array from the current
    /// registry. Called by <see cref="InitialiseAll"/>; can be called manually
    /// after late registrations.
    /// </summary>
    public void Freeze()
    {
        var ordered = All;
        var arr = new List<TickCapture>(ordered.Count);
        foreach (var s in ordered)
        {
            if (s.Cadence != DataStreamCadence.PerTick) continue;
            if (s is IHasPerTickCallback h && h.PerTickCallback is { } cb)
            {
                arr.Add(cb);
            }
        }
        PerTickCallbacks = arr.ToArray();
    }

    /// <summary>
    /// Call every stream's <see cref="IDataStream.Initialise"/> and then
    /// re-freeze the per-tick callback array. Invoked at world-load.
    /// </summary>
    public void InitialiseAll(SessionContext session)
    {
        foreach (var s in All) s.Initialise(session);
        Freeze();
    }

    /// <summary>Discard every stream's per-session state. Invoked at world-unload.</summary>
    public void ResetAll()
    {
        foreach (var s in All) s.Reset();
    }

    /// <summary>
    /// Dispose every stream and empty the registry. Invoked at mod-unload.
    /// Both the name map and the ordered list are cleared under the same
    /// lock to keep the two views consistent under concurrent
    /// <see cref="Register"/>.
    /// </summary>
    public void DisposeAll()
    {
        foreach (var s in All) s.Dispose();
        lock (_gate)
        {
            _byName.Clear();
            _ordered.Clear();
        }
        PerTickCallbacks = Array.Empty<TickCapture>();
    }
}
