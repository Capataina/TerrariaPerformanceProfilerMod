#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PerformanceProfiler.Profiling.Persistence.Records;
using JsonSerializer = System.Text.Json.JsonSerializer;
using JsonSerializerOptions = System.Text.Json.JsonSerializerOptions;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
namespace PerformanceProfiler.Profiling.Persistence;

/// <summary>
/// Append-only NDJSON redo log living alongside the LiteDB main file. Layer 2
/// of the crash-safety stack per §5 of the migration plan: every batch the
/// writer thread is about to commit to LiteDB is journaled first, so an
/// unflushed batch lost to a crash can be replayed on next launch.
///
/// File semantics:
/// <list type="bullet">
/// <item>Opened in <c>FileMode.Append</c> with <c>FileShare.Read</c> so a
/// process inspecting the journal (e.g. a recovery probe) can read without
/// closing our writer.</item>
/// <item>Line-framed JSON. A torn partial line on the trailing edge is
/// detected by the deserializer throwing on the truncated payload — the
/// replay loop catches and skips it.</item>
/// <item>Truncated to zero bytes after a clean session end + checkpoint —
/// see <see cref="TruncateOnCleanShutdown"/>. The journal is a redo log,
/// not an archive.</item>
/// </list>
/// </summary>
public sealed class EventJournal : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
    {
        WriteIndented = false,
        IncludeFields = false,
    };

    private readonly string _path;
    private FileStream? _stream;
    private bool _disposed;

    public string Path => _path;

    /// <summary>
    /// Number of bytes appended since the last <see cref="Flush"/>. Used by
    /// the writer thread to decide whether to fsync mid-session.
    /// </summary>
    public long UnflushedBytes { get; private set; }

    public EventJournal(string path)
    {
        _path = path;
        _stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
    }

    /// <summary>
    /// Append every op in the batch as one line. The line shape mirrors
    /// <see cref="DbWriteOp"/>'s discriminated structure so replay can
    /// reconstruct the op without ambiguity.
    /// </summary>
    public void AppendBatch(IReadOnlyList<DbWriteOp> batch)
    {
        if (_stream == null || batch.Count == 0) return;

        StringBuilder buf = new StringBuilder(batch.Count * 256);
        for (int i = 0; i < batch.Count; i++)
        {
            DbWriteOp op = batch[i];
            JournalLine line = new JournalLine
            {
                Kind = op.Kind.ToString(),
                SessionId = op.SessionId.ToString(),
                EndReason = op.EndReason,
                DurationMs = op.DurationMs,
                TicksObserved = op.TicksObserved,
                Payload = SerializePayload(op.Payload),
            };
            buf.Append(JsonSerializer.Serialize(line, JsonOpts));
            buf.Append('\n');
        }

        byte[] bytes = Encoding.UTF8.GetBytes(buf.ToString());
        _stream.Write(bytes, 0, bytes.Length);
        UnflushedBytes += bytes.Length;
    }

    /// <summary>Fsync the underlying file. Cheap if no writes have happened since the last call.</summary>
    public void Flush()
    {
        if (_stream == null) return;
        _stream.Flush(flushToDisk: true);
        UnflushedBytes = 0;
    }

    /// <summary>
    /// Replay the entire journal as a sequence of opaque <see cref="JournalLine"/>s.
    /// Idempotency is the caller's responsibility — the replay path checks
    /// the natural key on each kind and upserts.
    /// </summary>
    public IEnumerable<JournalLine> Replay()
    {
        if (!File.Exists(_path)) yield break;

        foreach (string line in File.ReadLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JournalLine? jl;
            try { jl = JsonSerializer.Deserialize<JournalLine>(line, JsonOpts); }
            catch { continue; }  // torn last line — drop and continue
            if (jl != null) yield return jl;
        }
    }

    /// <summary>
    /// Zero the file. Called only after a clean session-end has been
    /// committed to LiteDB and the engine has been checkpointed.
    /// </summary>
    public void TruncateOnCleanShutdown()
    {
        if (_stream != null)
        {
            _stream.Dispose();
            _stream = null;
        }
        File.WriteAllBytes(_path, Array.Empty<byte>());
        _stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        UnflushedBytes = 0;
    }

    public long FileSize => File.Exists(_path) ? new FileInfo(_path).Length : 0L;

    public void Dispose()
    {
        if (_disposed) return;
        _stream?.Dispose();
        _stream = null;
        _disposed = true;
    }

    private static string SerializePayload(object payload)
    {
        // Records carry their own field shape; serialise verbatim so replay
        // can reconstruct them via System.Text.Json without a custom mapper.
        // Lists (mod/hook batches) are serialised as arrays.
        return JsonSerializer.Serialize(payload, payload.GetType(), JsonOpts);
    }
}

/// <summary>Shape of one NDJSON line. Public so the replay path can deserialise it.</summary>
public sealed class JournalLine
{
    public string Kind { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string EndReason { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public long TicksObserved { get; set; }
    public string Payload { get; set; } = string.Empty;
}
