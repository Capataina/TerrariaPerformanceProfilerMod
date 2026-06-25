#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using LiteDB;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
using Xunit;

namespace PerformanceProfiler.Tests;

/// <summary>
/// Audit pin for the F2 code-health change: <see cref="EventJournal.AppendBatch"/>
/// was rewritten from a StringBuilder → string → UTF-8 byte[] double-buffer to a
/// per-line <c>SerializeToUtf8Bytes</c> stream-write. The change is asserted to be
/// byte-for-byte output-neutral.
///
/// <para>
/// The oracle re-implements the <em>old</em> algorithm independently (build one
/// whole-batch string of <c>Serialize(line) + '\n'</c> per op, then a single
/// <c>Encoding.UTF8.GetBytes</c>), so it is not coupled to the production path it
/// guards. If the new streaming write ever drifts from the old framing — a missing
/// <c>'\n'</c>, a reordered field, an options change — the produced journal bytes
/// stop matching and this test fails. <see cref="JournalLine"/> is public and the
/// serializer options are reproduced verbatim (<c>WriteIndented=false,
/// IncludeFields=false</c>), so the oracle reconstructs the exact UTF-8 the old
/// code wrote.
/// </para>
/// </summary>
public class AuditPin_Web_Journal : IDisposable
{
    private readonly string _root;

    // Verbatim copy of EventJournal's private JsonOpts. The oracle must use the
    // same options as the production serialiser or the bytes will not match.
    private static readonly JsonSerializerOptions OracleOpts = new JsonSerializerOptions
    {
        WriteIndented = false,
        IncludeFields = false,
    };

    public AuditPin_Web_Journal()
    {
        _root = Path.Combine(Path.GetTempPath(), "perfprofiler-journalpin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void AppendBatch_ProducesByteIdenticalOutput_ToOldDoubleBufferForm()
    {
        var sessionId = ObjectId.NewObjectId();

        // A representative, multi-kind batch covering a record payload, the
        // synthetic session-end payload (new object()), and another record kind,
        // so the per-line framing and the payload-serialisation path are both
        // exercised more than once per batch.
        var batch = new List<DbWriteOp>
        {
            DbWriteOp.SessionStart(new SessionRow
            {
                Id = sessionId,
                StartedUtc = new DateTime(2026, 6, 25, 12, 0, 0, DateTimeKind.Utc),
                ProfilerVersion = "pin-test",
                ModlistFingerprint = "fp-abc",
                Mode = "lite",
                EndReason = "clean",
                Incomplete = true,
            }),
            DbWriteOp.SessionEnd(sessionId, endReason: "clean", durationMs: 12345, ticksObserved: 678),
            DbWriteOp.Spike(new SpikeWindowRow
            {
                SessionId = sessionId,
                StartTick = 100, EndTick = 110, WorstTick = 105,
                WorstFrameMs = 87.3, BaselineMs = 16.7, MadMs = 0.4,
                Context = "pin-context",
            }),
            DbWriteOp.Stall(new StallEventRow
            {
                SessionId = sessionId,
                TickIndex = 200,
                UnixMs = 1234567,
                DurationMs = 250,
                BaselineTickMs = 16.7,
                Cause = "GcPause",
            }),
        };

        // --- Production path: the new streaming AppendBatch. ---
        string path = Path.Combine(_root, "journal.ndjson");
        using (var journal = new EventJournal(path))
        {
            journal.AppendBatch(batch);
            journal.Flush();
        }
        byte[] actual = File.ReadAllBytes(path);

        // --- Oracle: the old double-buffer algorithm, re-implemented here. ---
        byte[] expected = OldFormBytes(batch);

        Assert.Equal(expected, actual);

        // UnflushedBytes accounting must also match the produced byte count.
        long reportedUnflushed;
        using (var journal2 = new EventJournal(Path.Combine(_root, "journal2.ndjson")))
        {
            journal2.AppendBatch(batch);
            reportedUnflushed = journal2.UnflushedBytes;
        }
        Assert.Equal(expected.Length, reportedUnflushed);
    }

    /// <summary>
    /// Re-creates the pre-change AppendBatch output: one whole-batch StringBuilder
    /// of <c>Serialize(line) + '\n'</c> per op, then a single UTF-8 encode. Mirrors
    /// the exact field-assignment and payload-serialisation logic of the production
    /// method so the bytes are the true old-form oracle.
    /// </summary>
    private static byte[] OldFormBytes(IReadOnlyList<DbWriteOp> batch)
    {
        var sb = new StringBuilder(batch.Count * 256);
        for (int i = 0; i < batch.Count; i++)
        {
            DbWriteOp op = batch[i];
            var line = new JournalLine
            {
                Kind = op.Kind.ToString(),
                SessionId = op.SessionId.ToString(),
                EndReason = op.EndReason,
                DurationMs = op.DurationMs,
                TicksObserved = op.TicksObserved,
                Payload = System.Text.Json.JsonSerializer.Serialize(op.Payload, op.Payload.GetType(), OracleOpts),
            };
            sb.Append(System.Text.Json.JsonSerializer.Serialize(line, OracleOpts));
            sb.Append('\n');
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
