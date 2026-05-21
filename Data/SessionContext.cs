#nullable enable

namespace PerformanceProfiler.Data;

/// <summary>
/// Per-session payload handed to every <see cref="IDataStream"/> at
/// <see cref="IDataStream.Initialise"/>. Carries the identity of the
/// current session and references to the cross-cutting services
/// (LiteDB, modlist fingerprint, allocation-tracking flag).
///
/// <para>
/// Treated as immutable for the lifetime of a session. <see cref="DataRegistry.ResetAll"/>
/// discards the current session context implicitly; the next world-load
/// constructs a fresh one and passes it through <see cref="DataRegistry.InitialiseAll"/>.
/// </para>
/// </summary>
public sealed class SessionContext
{
    public required LiteDB.ObjectId SessionId { get; init; }
    public required string ModlistFingerprint { get; init; }
    public required bool TracksAllocations { get; init; }
    public required string HookBackendMode { get; init; }

    /// <summary>
    /// The LiteDB facade. <c>null</c> when the DB failed to open at mod load —
    /// see <c>PerformanceProfiler.Profiling.Persistence.ProfilerDatabase</c>'s
    /// abort-clean contract.
    /// </summary>
    public Profiling.Persistence.ProfilerDatabase? Database { get; init; }
}
