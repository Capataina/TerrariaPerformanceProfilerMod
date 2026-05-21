#nullable enable

namespace PerformanceProfiler.Data;

/// <summary>
/// How often a <see cref="IDataStream"/> is driven.
///
/// <list type="bullet">
///   <item><b>PerTick</b> — <see cref="IHasPerTickCallback.PerTickCallback"/>
///         fires on every game tick. Hot-path discipline applies: zero
///         allocation, no virtual dispatch beyond the static delegate
///         call site.</item>
///   <item><b>OneHz</b> — driven by an external 1 Hz scheduler. Allocation
///         allowed.</item>
///   <item><b>OnEvent</b> — emits when an upstream condition fires; no
///         scheduled capture.</item>
///   <item><b>OnDemand</b> — pure-pull. <c>CurrentSnapshot()</c> is the
///         only entry point. The default for <see cref="DataStage.Stat"/>.</item>
/// </list>
/// </summary>
public enum DataStreamCadence : byte
{
    PerTick,
    OneHz,
    OnEvent,
    OnDemand,
}

/// <summary>
/// Per-tick callback. Static delegate so the call site is non-virtual.
/// </summary>
public delegate void TickCapture(in TickContext ctx);

/// <summary>
/// Non-generic base for the data pipeline registry. Every stream
/// (collector, aggregator, stat, detector, exporter) implements this
/// indirectly via <see cref="IDataStream{TSnapshot}"/>.
///
/// <para>
/// See <c>context/plans/unified-data-pipeline.md</c> for the design
/// rationale. The principle: <b>if it produces a number, it lives in
/// Data/. If it consumes a number, it asks the registry.</b>
/// </para>
/// </summary>
public interface IDataStream
{
    /// <summary>Stable registry key. Unique across the process.</summary>
    string Name { get; }

    /// <summary>How often this stream is driven; see <see cref="DataStreamCadence"/>.</summary>
    DataStreamCadence Cadence { get; }

    /// <summary>Which pipeline stage this stream belongs to.</summary>
    DataStage Stage { get; }

    /// <summary>Called once at world-load (or at registry-bringup if registered later).</summary>
    void Initialise(SessionContext session);

    /// <summary>Discards all per-session state. Called at world-unload.</summary>
    void Reset();

    /// <summary>Called at mod-unload. Releases any unmanaged or cross-process state.</summary>
    void Dispose();

    /// <summary>
    /// Boxed-snapshot accessor used by exporters that iterate
    /// <see cref="DataRegistry.All"/> without knowing the concrete
    /// <c>TSnapshot</c> type. Typed callers should prefer
    /// <see cref="IDataStream{TSnapshot}.CurrentSnapshot"/>.
    /// </summary>
    object CurrentSnapshotBoxed();
}

/// <summary>
/// Typed view onto a data stream. <typeparamref name="TSnapshot"/> is the
/// immutable struct (or readonly record class) the stream produces when
/// asked for current state.
/// </summary>
public interface IDataStream<TSnapshot> : IDataStream
{
    /// <summary>Fresh immutable snapshot of the stream's current state.</summary>
    TSnapshot CurrentSnapshot();
}

/// <summary>Marker for any <see cref="IDataStream"/> that has a per-tick capture callback.</summary>
public interface IHasPerTickCallback
{
    /// <summary>The static-delegate callback the registry invokes per tick. Null when this stream is not <see cref="DataStreamCadence.PerTick"/>.</summary>
    TickCapture? PerTickCallback { get; }
}

/// <summary>
/// Per-stage specialisation markers. They carry no extra contract today
/// beyond <see cref="IDataStream{TSnapshot}"/>; the marker presence lets
/// future audit tooling reject obvious mistakes (e.g. an exporter
/// registering a per-tick callback).
/// </summary>
public interface IDataCollector<TSnapshot> : IDataStream<TSnapshot>, IHasPerTickCallback { }
public interface IDataAggregator<TSnapshot> : IDataStream<TSnapshot> { }
public interface IDataStat<TSnapshot> : IDataStream<TSnapshot> { }
public interface IDataDetector<TSnapshot> : IDataStream<TSnapshot>
{
    /// <summary>Run the detection pass. Invoked off-thread; the caller owns the latch.</summary>
    void Evaluate(long nowTick, long sessionLengthTicks);
}
public interface IDataExporter : IDataStream { }
