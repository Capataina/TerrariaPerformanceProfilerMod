#nullable enable

using System.Collections.Concurrent;

namespace PerformanceProfiler.Profiling.Pools;

/// <summary>
/// Thread-safe object pool for row types. The persistence layer used to
/// allocate every <c>DamageTakenRow</c>, <c>DamageDealtRow</c>,
/// <c>BuffEventRow</c>, etc. fresh on the game thread for every emit —
/// the dominant contributor to the 441→570 ns/op enqueue regression per
/// the persistence dossier §3 and cross-allocations §3.3.
///
/// <para>
/// The pool API is the classic Rent/Return shape: callers borrow a row,
/// fill it, hand it to the recorder, and the writer thread returns it
/// after the LiteDB upsert completes. The instance's <see cref="IPoolReset"/>
/// implementation clears every field on Return so the next renter starts
/// clean.
/// </para>
///
/// <para>
/// Backed by a <see cref="ConcurrentBag{T}"/> for simplicity and lock-free
/// LIFO semantics. The bag grows naturally to the steady-state working
/// set (typically 1–4 instances per row type for a single-player session).
/// No size cap is enforced; if the pool grows unexpectedly, that itself is
/// a signal worth flagging during a perf regression review.
/// </para>
/// </summary>
public static class RowPool<T> where T : class, IPoolReset, new()
{
    private static readonly ConcurrentBag<T> _bag = new();

    /// <summary>
    /// Return a row from the pool, or allocate a fresh one if the pool is
    /// empty. The returned instance is already-reset (Return clears state).
    /// </summary>
    public static T Rent()
    {
        if (_bag.TryTake(out var item)) return item;
        return new T();
    }

    /// <summary>
    /// Return a row to the pool. Calls <see cref="IPoolReset.Reset"/> first
    /// so the next renter is guaranteed clean state. Null-safe.
    /// </summary>
    public static void Return(T? item)
    {
        if (item == null) return;
        item.Reset();
        _bag.Add(item);
    }
}
