#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;

namespace PerformanceProfiler.Profiling.Persistence;

/// <summary>
/// Single dedicated background thread that owns every LiteDB write.
///
/// The game thread <see cref="Enqueue"/>s a <see cref="DbWriteOp"/>; this
/// worker drains the queue in batches (up to <see cref="BatchCap"/>) and
/// hands them to <see cref="ProfilerDatabase.ApplyBatch"/>. The
/// <see cref="Channels.Channel{T}"/> implementation is lock-free on the
/// producer side, so the game thread pays a single CAS per enqueue.
///
/// Periodic checkpoint cadence keeps LiteDB's <c>-log</c> file bounded
/// (issue #1568 mitigation per §5 of the migration plan).
/// </summary>
public sealed class DbWriterThread : IDisposable
{
    /// <summary>Maximum batch size per LiteDB <c>InsertBulk</c> / loop iteration.</summary>
    public const int BatchCap = 64;

    /// <summary>Soft cap on the producer queue. Beyond this we drop warm aggregates.</summary>
    public const int QueueSoftCap = 100_000;

    /// <summary>Checkpoint cadence — every N seconds of writer-thread activity.</summary>
    public const int CheckpointIntervalSeconds = 60;

    /// <summary>Force a journal fsync after this many bytes have been appended.</summary>
    public const int JournalForcedFlushBytes = 1 << 16; // 64 KB

    private readonly ProfilerDatabase _db;
    private readonly EventJournal _journal;
    private readonly Channel<DbWriteOp> _queue;
    private readonly Thread _worker;
    private readonly CancellationTokenSource _cts = new();
    private readonly Action<string, Exception?> _log;

    private DateTime _lastCheckpointUtc = DateTime.UtcNow;
    private int _pendingSinceLastCheckpoint;
    private long _droppedWarmCount;

    public long DroppedWarmCount => Volatile.Read(ref _droppedWarmCount);

    /// <summary>Approximate queue depth — for diagnostics only.</summary>
    public int ApproxQueueDepth => _queue.Reader.Count;

    public DbWriterThread(ProfilerDatabase db, EventJournal journal, Action<string, Exception?>? log = null)
    {
        _db = db;
        _journal = journal;
        _log = log ?? ((_, _) => { });
        _queue = Channel.CreateUnbounded<DbWriteOp>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _worker = new Thread(Run)
        {
            Name = "ProfilerDbWriter",
            IsBackground = true,
        };
        _worker.Start();
    }

    /// <summary>
    /// Called from any thread (game thread included). Non-blocking; if the
    /// queue is past the soft cap we drop the lowest-value class (warm
    /// aggregates) and log once per second from the background thread.
    /// </summary>
    public void Enqueue(in DbWriteOp op)
    {
        if (_cts.IsCancellationRequested) return;

        if (_queue.Reader.Count >= QueueSoftCap && op.Kind == DbOpKind.WarmAggregate)
        {
            Interlocked.Increment(ref _droppedWarmCount);
            return;
        }
        _queue.Writer.TryWrite(op);
    }

    private void Run()
    {
        var batch = new List<DbWriteOp>(BatchCap);
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                // Wait up to 1 second; that gives us the cadence for
                // periodic checkpoints even when no work is arriving.
                bool readReady;
                try
                {
                    readReady = _queue.Reader
                        .WaitToReadAsync(_cts.Token).AsTask()
                        .Wait(1000);
                }
                catch (AggregateException ae) when (ae.InnerException is OperationCanceledException) { break; }
                catch (OperationCanceledException) { break; }

                if (!readReady)
                {
                    MaybeCheckpoint();
                    continue;
                }

                batch.Clear();
                while (batch.Count < BatchCap && _queue.Reader.TryRead(out var op))
                {
                    batch.Add(op);
                }

                if (batch.Count == 0)
                {
                    MaybeCheckpoint();
                    continue;
                }

                try
                {
                    // Journal first (durable truth), then LiteDB. Mid-batch
                    // crash: replay reconstructs the same state.
                    _journal.AppendBatch(batch);
                    if (_journal.UnflushedBytes >= JournalForcedFlushBytes)
                    {
                        _journal.Flush();
                    }
                    _db.ApplyBatch(batch);
                    _pendingSinceLastCheckpoint += batch.Count;
                }
                catch (Exception ex)
                {
                    _log("ProfilerDbWriter: batch apply failed", ex);
                }

                MaybeCheckpoint();
            }
        }
        finally
        {
            DrainAndShutdown(batch);
        }
    }

    private void MaybeCheckpoint()
    {
        if (_pendingSinceLastCheckpoint <= 0) return;
        if ((DateTime.UtcNow - _lastCheckpointUtc).TotalSeconds < CheckpointIntervalSeconds) return;

        try
        {
            _db.Checkpoint();
            _journal.Flush();
        }
        catch (Exception ex)
        {
            _log("ProfilerDbWriter: checkpoint failed", ex);
        }
        finally
        {
            _lastCheckpointUtc = DateTime.UtcNow;
            _pendingSinceLastCheckpoint = 0;
        }
    }

    private void DrainAndShutdown(List<DbWriteOp> batch)
    {
        batch.Clear();
        while (_queue.Reader.TryRead(out var op)) batch.Add(op);
        if (batch.Count > 0)
        {
            try
            {
                _journal.AppendBatch(batch);
                _journal.Flush();
                _db.ApplyBatch(batch);
            }
            catch (Exception ex) { _log("ProfilerDbWriter: drain apply failed", ex); }
        }

        try { _db.Checkpoint(); }
        catch (Exception ex) { _log("ProfilerDbWriter: final checkpoint failed", ex); }
    }

    public void Dispose()
    {
        try
        {
            _queue.Writer.TryComplete();
            _cts.Cancel();
            if (_worker.IsAlive)
            {
                _worker.Join(TimeSpan.FromSeconds(10));
            }
        }
        catch (Exception ex)
        {
            _log("ProfilerDbWriter: dispose failed", ex);
        }
        finally
        {
            _cts.Dispose();
        }
    }
}
