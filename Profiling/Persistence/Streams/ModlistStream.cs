#nullable enable

using System;
using System.Collections.Generic;
using PerformanceProfiler.Profiling.Persistence.Records;

namespace PerformanceProfiler.Profiling.Persistence.Streams;

/// <summary>
/// Stream owning the <c>modlists</c>, <c>mods</c>, and <c>worlds</c>
/// collections. Three kinds share one stream because they all represent
/// "stable identity tables" the recorder upserts at session start.
/// </summary>
internal sealed class ModlistStream : IPersistenceStream
{
    public string Name => "identity";

    public IReadOnlyList<DbOpKind> Kinds { get; } = new[]
    {
        DbOpKind.UpsertWorld,
        DbOpKind.UpsertModlist,
        DbOpKind.UpsertMod,
    };

    public void Apply(in DbWriteOp op, ProfilerDatabase db)
    {
        switch (op.Kind)
        {
            case DbOpKind.UpsertWorld:
            {
                var row = (WorldRow)op.Payload;
                var existing = db.Worlds.FindOne(x =>
                    x.Name == row.Name && x.UniqueId == row.UniqueId);
                if (existing == null) db.Worlds.Insert(row);
                else { row.Id = existing.Id; db.Worlds.Update(row); }
                break;
            }
            case DbOpKind.UpsertModlist:
            {
                var row = (ModlistRow)op.Payload;
                var existing = db.Modlists.FindOne(x => x.Fingerprint == row.Fingerprint);
                if (existing == null) db.Modlists.Insert(row);
                else
                {
                    row.Id = existing.Id;
                    row.SessionCount = existing.SessionCount + 1;
                    if (existing.FirstSeenUtc != default) row.FirstSeenUtc = existing.FirstSeenUtc;
                    db.Modlists.Update(row);
                }
                break;
            }
            case DbOpKind.UpsertMod:
            {
                var row = (ModRow)op.Payload;
                var existing = db.Mods.FindOne(x =>
                    x.ModlistFingerprint == row.ModlistFingerprint && x.InternalName == row.InternalName);
                if (existing == null) db.Mods.Insert(row);
                else
                {
                    row.Id = existing.Id;
                    if (existing.FirstSeenUtc != default) row.FirstSeenUtc = existing.FirstSeenUtc;
                    if (existing.VersionSeen != row.VersionSeen)
                    {
                        row.VersionHistory = new List<ModVersionEntry>(existing.VersionHistory)
                        {
                            new ModVersionEntry
                            {
                                Version = row.VersionSeen,
                                FirstUtc = DateTime.UtcNow,
                                LastUtc = DateTime.UtcNow,
                            }
                        };
                    }
                    else
                    {
                        row.VersionHistory = existing.VersionHistory;
                    }
                    db.Mods.Update(row);
                }
                break;
            }
        }
    }

    public DbWriteOp? Reconstruct(JournalLine line)
    {
        return line.Kind switch
        {
            nameof(DbOpKind.UpsertWorld)
                => DbWriteOp.UpsertWorld(StreamJson.Deserialize<WorldRow>(line.Payload)),
            nameof(DbOpKind.UpsertModlist)
                => DbWriteOp.UpsertModlist(StreamJson.Deserialize<ModlistRow>(line.Payload)),
            nameof(DbOpKind.UpsertMod)
                => DbWriteOp.UpsertMod(StreamJson.Deserialize<ModRow>(line.Payload)),
            _ => null,
        };
    }

    public void EnsureIndexes(ProfilerDatabase db)
    {
        db.Modlists.EnsureIndex(x => x.Fingerprint, unique: true);
        db.Mods.EnsureIndex(x => x.ModlistFingerprint);
        db.Mods.EnsureIndex(x => x.InternalName);
    }
}
