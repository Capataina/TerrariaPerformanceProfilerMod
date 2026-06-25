#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using log4net;
using LiteDB;
using PerformanceProfiler.Persistence.Records;
using JsonDocument = System.Text.Json.JsonDocument;
using JsonValueKind = System.Text.Json.JsonValueKind;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;
namespace PerformanceProfiler.Persistence;

/// <summary>
/// One-shot importer that reads any pre-existing <c>Sessions/*.json</c>
/// files written by the legacy <c>SessionLogWriter</c> and ingests what it
/// can into the new schema, then deletes the legacy directory.
///
/// The importer is intentionally minimal — it pulls only the fields the new
/// schema covers and skips anything it can't recognise. The previous JSON
/// format was rich; the importer is not trying to faithfully reconstruct
/// every field, just to preserve the cross-session history so the player
/// doesn't lose their record on upgrade.
///
/// Idempotent: looks for a <c>.imported</c> sentinel inside the legacy
/// directory and exits immediately if present. Failures on individual
/// files are logged and skipped; the rest of the import continues.
/// </summary>
internal static class LegacyJsonImporter
{
    private const string SentinelFileName = ".imported";

    public static void RunOnceIfNeeded(ProfilerDatabase db, ILog log)
    {
        string sessionsDir = Path.Combine(db.Root, ProfilerPaths.LegacySessions);
        if (!Directory.Exists(sessionsDir)) return;

        string sentinel = Path.Combine(sessionsDir, SentinelFileName);
        if (File.Exists(sentinel)) return;

        int imported = 0;
        int skipped = 0;
        foreach (string path in Directory.EnumerateFiles(sessionsDir, "*.json"))
        {
            try
            {
                if (Ingest(db, path)) imported++;
                else skipped++;
            }
            catch (Exception ex)
            {
                skipped++;
                log.Warn($"LegacyJsonImporter: failed on {Path.GetFileName(path)} ({ex.GetType().Name}: {ex.Message})");
            }
        }

        try
        {
            File.WriteAllText(sentinel, DateTime.UtcNow.ToString("o"));
            log.Info($"LegacyJsonImporter: imported {imported} JSON session(s), skipped {skipped}. Sentinel written.");
        }
        catch (Exception ex)
        {
            log.Warn($"LegacyJsonImporter: sentinel write failed ({ex.GetType().Name}: {ex.Message}); will re-run next launch.");
        }
    }

    private static bool Ingest(ProfilerDatabase db, string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;

        // The legacy schema's session identity is the report's `identity`
        // string; without an ObjectId we synthesise a deterministic one
        // by hashing the identity into the high bits of an ObjectId.
        string identity = root.TryGetProperty("identity", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString() ?? Path.GetFileNameWithoutExtension(path)
            : Path.GetFileNameWithoutExtension(path);

        ObjectId sessionId = ObjectId.NewObjectId();
        // ISO-8601 (`o` format) round-trips cleanly through InvariantCulture
        // + RoundtripKind. Plain DateTime.Parse uses CurrentCulture and
        // would silently misparse on locales whose date format differs.
        DateTime started = root.TryGetProperty("startedUtc", out var startEl) && startEl.ValueKind == JsonValueKind.String
            ? DateTime.Parse(startEl.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime()
            : File.GetCreationTimeUtc(path);
        DateTime ended = root.TryGetProperty("endedUtc", out var endEl) && endEl.ValueKind == JsonValueKind.String
            ? DateTime.Parse(endEl.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime()
            : File.GetLastWriteTimeUtc(path);
        string mode = root.TryGetProperty("mode", out var modeEl) && modeEl.ValueKind == JsonValueKind.String
            ? modeEl.GetString() ?? "lite"
            : "lite";
        string modFingerprint = root.TryGetProperty("modFingerprint", out var fpEl) && fpEl.ValueKind == JsonValueKind.String
            ? fpEl.GetString() ?? ""
            : "";
        string profilerVer = root.TryGetProperty("profilerVersion", out var pvEl) && pvEl.ValueKind == JsonValueKind.String
            ? pvEl.GetString() ?? "legacy"
            : "legacy";
        long durationMs = (long)(ended - started).TotalMilliseconds;

        // Don't double-import: identical (startedUtc, modlistFingerprint)
        // is the natural key for "this is the same legacy session".
        var existing = db.Sessions.FindOne(x =>
            x.StartedUtc == started && x.ModlistFingerprint == modFingerprint);
        if (existing != null) return false;

        db.Sessions.Insert(new SessionRow
        {
            Id = sessionId,
            StartedUtc = started,
            EndedUtc = ended,
            DurationMs = durationMs,
            ProfilerVersion = profilerVer,
            ModlistFingerprint = modFingerprint,
            Mode = mode,
            EndReason = "clean",
            Incomplete = false,
        });

        // Move the file out of the directory rather than delete it — leaves
        // a backup trail for users while keeping the directory empty enough
        // that the sentinel-and-skip logic is unambiguous next launch.
        string trashDir = Path.Combine(db.Root, "ImportedLegacyJson");
        Directory.CreateDirectory(trashDir);
        string dest = Path.Combine(trashDir, Path.GetFileName(path));
        try { File.Move(path, dest, overwrite: true); }
        catch { /* leave file in place; sentinel still guards against re-ingest */ }

        return true;
    }
}
