#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Profiling.Persistence.Records;
using PerformanceProfiler.Profiling.Pools;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;

namespace PerformanceProfiler.Profiling.Persistence.Interactions;

/// <summary>
/// Hooks every player-side interaction the profiler captures into one
/// <see cref="ModPlayer"/>. Each override translates the vanilla event
/// into a row enqueued via the live <see cref="ProfilerSystem"/>'s
/// recorder.
///
/// <para>
/// Every hook uses a generic vanilla / tML surface — <see cref="PlayerDeathReason"/>,
/// <see cref="Player.HurtInfo"/>, <see cref="NPC.HitInfo"/>, the buff
/// arrays, the equipment slots — per Invariant 5. There is no
/// mod-specific switch anywhere in this file.
/// </para>
///
/// <para>
/// The hook bodies are deliberately small: build the row, hand it to the
/// recorder (which queues to the writer thread), return. Per Invariant 2
/// every per-event path is allocation-bounded (the row itself is the
/// only managed allocation; payload lists are stack-built into the row's
/// final list).
/// </para>
/// </summary>
internal sealed class InteractionPlayer : ModPlayer
{
    /// <summary>State the buff-edge detector carries across ticks. One bitset
    /// per-buff-type would be huge; tML uses a flat int[BuffSlotCount] holding
    /// active buff type ids — we copy the previous-tick snapshot and diff.</summary>
    private int[] _prevBuffTypes = new int[Player.MaxBuffs];
    private int _prevBuffCount;

    /// <summary>Latch flipped by the first <see cref="PostUpdateBuffs"/> call
    /// for which <c>Player.whoAmI == Main.myPlayer</c>. v0.5 had a race where
    /// PostUpdateBuffs fired before the gate cleared, the snapshot stayed
    /// uninitialised, and the first buff-edge tick after the gate cleared
    /// got no "on" emission because the diff loop saw _prevBuffCount = 0 but
    /// then the snapshot copy ran <em>after</em> the early return, so the
    /// next tick's prev-state was still empty (loop never executed). v0.6
    /// snapshots before the early return and emits all active buffs as "on"
    /// on the first valid tick.</summary>
    private bool _firstValidBuffTickSeen;

    /// <summary>
    /// Cheap hash of the buff-type array for per-tick fast-skip in
    /// <see cref="PostUpdateBuffs"/>. v0.5/v0.6 ran the full diff loop
    /// every tick even when no edges fired (which is 99%+ of ticks).
    /// v0.6.1 hashes the array first; matching hashes skip the whole diff.
    /// </summary>
    private ulong _lastBuffHash;

    /// <summary>Last loadout fingerprint, so we only enqueue on actual change.</summary>
    private string _lastLoadoutFingerprint = "";

    /// <summary>30-second cadence for periodic loadout anchors.</summary>
    private long _lastPeriodicLoadoutTick;
    private const int PeriodicLoadoutIntervalTicks = 30 * 60;

    /// <summary>
    /// Reused fingerprint builder. v0.5 allocated a fresh <c>StringBuilder</c>
    /// every <c>PostUpdateEquips</c> tick — multiple times per second under
    /// heavy combat. v0.6 keeps one per <c>ModPlayer</c> instance, cleared at
    /// start of each capture. Phase γ.
    /// </summary>
    private readonly System.Text.StringBuilder _fpBuilder = new System.Text.StringBuilder(256);

    /// <summary>
    /// Cheap hash of (held-item-type + armor-slot-types) for the per-tick
    /// fast-skip in <see cref="PostUpdateEquips"/>. v0.5 called
    /// <see cref="CaptureLoadout"/> every tick — that's one
    /// <c>List&lt;EquipmentSlotEntry&gt;</c>, N <c>EquipmentSlotEntry</c>
    /// elements, one fingerprint <c>StringBuilder.ToString()</c>, and one
    /// <c>LoadoutSnapshotRow</c> allocated on the game thread every single
    /// tick even when nothing changed (which is 99%+ of ticks). v0.6 hashes
    /// the loadout shape into a ulong, compares against the last tick's
    /// hash, and only calls CaptureLoadout when the hash actually changes.
    /// </summary>
    private ulong _lastLoadoutHash;

    public override void OnHurt(Player.HurtInfo info)
    {
        var recorder = ResolveRecorder();
        if (recorder == null || Player.whoAmI != Main.myPlayer) return;

        var (kind, id, name) = ClassifyDeathReason(info.DamageSource);
        // v0.6.1: Rent pooled row instead of `new DamageTakenRow`. The
        // writer thread's DamageTakenStream.Apply returns it to the pool
        // after Upsert; in steady state the same handful of instances cycle
        // forever and zero heap allocation per damage event.
        var row = RowPool<DamageTakenRow>.Rent();
        row.Tick = (long)Main.GameUpdateCount;
        row.UnixMs = Time.UnixMsNow();
        row.SourceKind = kind;
        row.SourceId = id;
        row.SourceName = name;
        row.DamageRaw = info.SourceDamage;
        row.DamageDealt = info.Damage;
        row.Pvp = info.PvP;
        row.Crit = false;   // Player.HurtInfo doesn't expose a Crit flag in tML 1.4.4
        row.HpBefore = Player.statLife + info.Damage;
        row.HpAfter = Player.statLife;
        row.MaxHp = Player.statLifeMax2;
        row.ActiveBuffs = SnapshotActiveBuffTypes();
        recorder.OnDamageTaken(row);
    }

    public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
    {
        var recorder = ResolveRecorder();
        if (recorder == null || Player.whoAmI != Main.myPlayer) return;

        // v0.6.1: pooled row. See OnHurt for the cycle explanation.
        var row = RowPool<DamageDealtRow>.Rent();
        row.Tick = (long)Main.GameUpdateCount;
        row.UnixMs = Time.UnixMsNow();
        row.Path = "melee";
        row.ItemId = 0;
        row.ProjectileId = 0;
        row.NpcType = target.type;
        row.NpcName = LangNameCache.Npc(target.type);
        row.DamageDealt = damageDone;
        row.Crit = hit.Crit;
        row.LoadoutFingerprint = _lastLoadoutFingerprint;
        recorder.OnDamageDealt(row);
    }

    public override void OnHitNPCWithItem(Item item, NPC target, NPC.HitInfo hit, int damageDone)
    {
        var recorder = ResolveRecorder();
        if (recorder == null || Player.whoAmI != Main.myPlayer) return;

        var row = RowPool<DamageDealtRow>.Rent();
        row.Tick = (long)Main.GameUpdateCount;
        row.UnixMs = Time.UnixMsNow();
        row.Path = "item";
        row.ItemId = item?.type ?? 0;
        row.ProjectileId = 0;
        row.NpcType = target.type;
        row.NpcName = LangNameCache.Npc(target.type);
        row.DamageDealt = damageDone;
        row.Crit = hit.Crit;
        row.LoadoutFingerprint = _lastLoadoutFingerprint;
        recorder.OnDamageDealt(row);
    }

    public override void OnHitNPCWithProj(Projectile proj, NPC target, NPC.HitInfo hit, int damageDone)
    {
        var recorder = ResolveRecorder();
        if (recorder == null || Player.whoAmI != Main.myPlayer) return;

        // The projectile's source item is the weapon that fired it, when
        // known. Vanilla exposes it on Projectile.originalDamage's neighbour
        // fields; the simplest universal handle is the held item at fire
        // time (we don't track that yet) — for now we record the projectile
        // and leave the originating item to be cross-referenced from the
        // loadout snapshot via the LoadoutFingerprint.
        var row = RowPool<DamageDealtRow>.Rent();
        row.Tick = (long)Main.GameUpdateCount;
        row.UnixMs = Time.UnixMsNow();
        row.Path = "projectile";
        row.ItemId = 0;
        row.ProjectileId = proj?.type ?? 0;
        row.NpcType = target.type;
        row.NpcName = LangNameCache.Npc(target.type);
        row.DamageDealt = damageDone;
        row.Crit = hit.Crit;
        row.LoadoutFingerprint = _lastLoadoutFingerprint;
        recorder.OnDamageDealt(row);
    }

    public override void PostUpdateBuffs()
    {
        // Hard gate: only the local player. Note we do NOT early-return here
        // before the diff (the v0.5 bug); for non-local-player ticks we still
        // skip the diff but we deliberately do not even touch state, so the
        // local-player branch's snapshot stays clean.
        if (Player.whoAmI != Main.myPlayer) return;

        // v0.6.1 dirty-flag fast path: hash the buff array first. If it
        // matches the previous tick's hash, NOTHING changed — skip the
        // diff entirely. Steady-state 99%+ ticks skip.
        ulong hash = ComputeBuffHash();
        if (hash == _lastBuffHash && _firstValidBuffTickSeen) return;
        _lastBuffHash = hash;

        var recorder = ResolveRecorder();

        // Diff against last tick's snapshot. Buffs leaving = old types not
        // present in current, buffs entering = current types not present in
        // old. tML's buff array holds active type ids contiguously up to
        // Player.buffType.Length-1 with 0 = empty.
        //
        // First valid tick (v0.6 fix): emit every currently-active buff as
        // "on" so the timeline doesn't lose state for buffs that were already
        // active when the gate first cleared. Same shape as the post-respawn
        // re-initialisation case.
        if (!_firstValidBuffTickSeen)
        {
            _firstValidBuffTickSeen = true;
            if (recorder != null)
            {
                for (int i = 0; i < Player.buffType.Length; i++)
                {
                    int t = Player.buffType[i];
                    if (t > 0) EmitBuffEdge(recorder, t, "on");
                }
            }
        }
        else if (recorder != null)
        {
            // Removed: types in prev but not in current.
            for (int i = 0; i < _prevBuffCount; i++)
            {
                int t = _prevBuffTypes[i];
                if (t <= 0) continue;
                if (System.Array.IndexOf(Player.buffType, t) < 0)
                    EmitBuffEdge(recorder, t, "off");
            }
            // Added: types in current but not in prev.
            for (int i = 0; i < Player.buffType.Length; i++)
            {
                int t = Player.buffType[i];
                if (t <= 0) continue;
                if (System.Array.IndexOf(_prevBuffTypes, t, 0, _prevBuffCount) < 0)
                    EmitBuffEdge(recorder, t, "on");
            }
        }

        // Snapshot for next tick — always, regardless of recorder availability,
        // so that a recorder appearing mid-session still gets a coherent diff
        // on its first tick (which then emits any new on/off vs the snapshot
        // we kept while the recorder was null).
        if (_prevBuffTypes.Length < Player.buffType.Length)
            _prevBuffTypes = new int[Player.buffType.Length];
        System.Array.Copy(Player.buffType, _prevBuffTypes, Player.buffType.Length);
        _prevBuffCount = Player.buffType.Length;
    }

    public override void PostUpdateEquips()
    {
        if (Player.whoAmI != Main.myPlayer) return;

        // v0.6.1 dirty-flag fast path: compute a cheap hash of held-item +
        // armor slot types FIRST, before any allocation. If it matches the
        // previous tick's hash AND no periodic anchor is due, we can skip
        // the entire CaptureLoadout path (List + N entries + StringBuilder
        // + LoadoutSnapshotRow alloc). Steady-state: 99%+ of ticks skip.
        ulong hash = ComputeLoadoutHash();
        long tickNow = (long)Main.GameUpdateCount;
        bool periodicDue = tickNow - _lastPeriodicLoadoutTick >= PeriodicLoadoutIntervalTicks;

        if (hash == _lastLoadoutHash && !periodicDue) return;

        var recorder = ResolveRecorder();
        if (recorder == null) { _lastLoadoutHash = hash; return; }

        bool isChange = hash != _lastLoadoutHash;
        _lastLoadoutHash = hash;

        var row = CaptureLoadout(isChange ? "change" : "periodic");
        if (isChange)
        {
            _lastLoadoutFingerprint = row.Fingerprint;
            recorder.OnLoadoutSnapshot(row);
        }
        else
        {
            // Hash matched but periodic-anchor cadence elapsed; emit anchor
            // so insight queries can find at least one snapshot per time
            // window without depending on equipment change.
            _lastPeriodicLoadoutTick = tickNow;
            recorder.OnLoadoutSnapshot(row);
        }

        // Periodic anchor tick book-keeping: also reset the cadence on
        // genuine changes so we don't double-fire an anchor right after a
        // change.
        if (isChange) _lastPeriodicLoadoutTick = tickNow;
    }

    /// <summary>
    /// Cheap loadout-shape hash. Combines held-item type with every armor
    /// slot's <c>type</c> via FNV-1a-ish XOR/rotate. No allocations, ~20
    /// ulong ops per tick. Stable across ticks when nothing equipped
    /// changes; flips on any slot type difference. Collision-tolerant —
    /// false negatives just trigger a CaptureLoadout call on the next
    /// tick where the hash diverges, no correctness loss.
    /// </summary>
    private ulong ComputeLoadoutHash()
    {
        ulong h = 14695981039346656037UL;   // FNV offset
        int held = Player.HeldItem?.type ?? 0;
        h = (h ^ (ulong)held) * 1099511628211UL;
        var armor = Player.armor;
        for (int i = 0; i < armor.Length; i++)
        {
            var it = armor[i];
            int t = (it == null) ? 0 : it.type;
            h = (h ^ (ulong)t) * 1099511628211UL;
        }
        return h;
    }

    /// <summary>
    /// Cheap buff-array hash. Same FNV-1a shape as
    /// <see cref="ComputeLoadoutHash"/>. Position-sensitive so a buff
    /// changing slot still flips the hash (and the diff catches the
    /// edge correctly). No allocations.
    /// </summary>
    private ulong ComputeBuffHash()
    {
        ulong h = 14695981039346656037UL;
        var buffs = Player.buffType;
        for (int i = 0; i < buffs.Length; i++)
        {
            h = (h ^ (ulong)(uint)buffs[i]) * 1099511628211UL;
        }
        return h;
    }

    // ---- helpers --------------------------------------------------------

    private void EmitBuffEdge(SessionRecorder recorder, int buffType, string edge)
    {
        // v0.6: LangNameCache + ModOwnerCache (Phase α infrastructure).
        // Was: try { Lang.GetBuffName } catch + OwningModName(BuffLoader.GetBuff).
        // Both lookups are now id-keyed array reads, populated once at
        // PostSetupContent. Allocation + lookup cost dropped to near-zero.
        // v0.6.1: pooled BuffEventRow.
        var row = RowPool<BuffEventRow>.Rent();
        row.Tick = (long)Main.GameUpdateCount;
        row.UnixMs = Time.UnixMsNow();
        row.Edge = edge;
        row.BuffType = buffType;
        row.BuffName = LangNameCache.Buff(buffType);
        row.OwningMod = ModOwnerCache.ForBuff(buffType);
        row.DurationTicks = -1;
        recorder.OnBuffEvent(row);
    }

    private LoadoutSnapshotRow CaptureLoadout(string reason)
    {
        // v0.6.1: pooled row. The row's Slots list is also pooled (cleared
        // on Return via IPoolReset.Reset, keeping its backing array).
        var row = RowPool<LoadoutSnapshotRow>.Rent();
        var slots = row.Slots;  // reuse the pooled list (already cleared by Reset)
        int armorEnd = 3; // vanilla armor slots 0..2
        for (int i = 0; i < Player.armor.Length; i++)
        {
            var it = Player.armor[i];
            if (it == null || it.type == Terraria.ID.ItemID.None) continue;

            string kind = i < armorEnd ? "armor"
                        : i < armorEnd + 7 ? "accessory"
                        : i < (armorEnd + 7) * 2 ? "vanity"
                        : "dye";
            slots.Add(new EquipmentSlotEntry
            {
                Kind = kind,
                Index = i,
                ItemType = it.type,
                ItemName = it.Name ?? "",
            });
        }

        // Fingerprint = concatenation of (kind:index:type) sorted by index +
        // held item type. Stable + cheap to compare. v0.6 reuses the
        // _fpBuilder field (cleared per call) so the per-tick allocation
        // is just the final ToString().
        var sb = _fpBuilder;
        sb.Clear();
        sb.Append('h').Append(Player.HeldItem?.type ?? 0).Append('|');
        foreach (var s in slots) sb.Append(s.Kind[0]).Append(s.Index).Append(':').Append(s.ItemType).Append('|');
        string fp = sb.ToString();

        row.Tick = (long)Main.GameUpdateCount;
        row.UnixMs = Time.UnixMsNow();
        row.Reason = reason;
        row.HeldItemType = Player.HeldItem?.type ?? 0;
        row.HeldItemName = Player.HeldItem?.Name ?? "";
        row.Fingerprint = fp;
        return row;
    }

    private List<int> SnapshotActiveBuffTypes()
    {
        // The list itself escapes into ActiveBuffs on the row, so it can't
        // be pooled-and-returned here — the row owns it. Capacity hint
        // sized to typical-loadout buff count avoids the doubling-growth.
        var list = new List<int>(8);
        for (int i = 0; i < Player.buffType.Length; i++)
            if (Player.buffType[i] > 0) list.Add(Player.buffType[i]);
        return list;
    }

    private static (string kind, int id, string name) ClassifyDeathReason(PlayerDeathReason reason)
    {
        // PlayerDeathReason exposes mutually-exclusive sources via int fields.
        // -1 = "not this source". Take the first that's set.
        //
        // v0.6: Lang.GetProjectileName and NPC.TypeName both go through
        // LangNameCache — the cache resolves once at PostSetupContent into
        // a flat string[] indexed by type id. Per-call cost: one array load.
        if (reason.SourceProjectileLocalIndex >= 0 && reason.SourceProjectileType >= 0)
        {
            int type = reason.SourceProjectileType;
            return ("projectile", type, LangNameCache.Projectile(type));
        }
        if (reason.SourceNPCIndex >= 0 && reason.SourceNPCIndex < Main.npc.Length)
        {
            int type = Main.npc[reason.SourceNPCIndex].type;
            return ("npc", type, LangNameCache.Npc(type));
        }
        if (reason.SourceOtherIndex >= 0)
        {
            return ("other", reason.SourceOtherIndex, OtherIndexName(reason.SourceOtherIndex));
        }
        if (reason.SourcePlayerIndex >= 0)
        {
            return ("player", reason.SourcePlayerIndex, "player");
        }
        if (reason.CustomReason != null)
        {
            string text = reason.CustomReason.ToString();
            if (!string.IsNullOrEmpty(text)) return ("custom", -1, text);
        }
        return ("unknown", -1, "(unknown)");
    }

    /// <summary>
    /// Vanilla "Other" source indices. See <c>PlayerDeathReason.LegacyDefault</c>
    /// constants. Case 0 maps to "Fall" — vanilla's <c>PlayerDeathReason.ByOther(0)</c>
    /// is reached by the fall-damage code path that doesn't carry the
    /// <c>Fall_TooHigh = 1</c> tag (e.g. mining-tunnel jumps the player
    /// initiated), and the v0.6 playtest confirmed this with a 1500-dmg
    /// one-shot from jumping down a self-dug shaft.
    /// </summary>
    private static string OtherIndexName(int idx) => idx switch
    {
        0 => "Fall",
        1 => "Fall",
        2 => "Drown",
        3 => "Lava",
        4 => "Suffocation",
        5 => "BurningCampfire",
        6 => "Poisoned",
        7 => "Electrified",
        8 => "TriedToEscape",
        9 => "WasLicked",
        10 => "Teleport",
        11 => "FloatingIslandFall",
        12 => "DemonAltar",
        13 => "BetrayedByBunny",
        14 => "TooMuchInformation",
        15 => "Suffocated",
        _ => "other-" + idx,
    };

    private static string OwningModName(ModBuff? buff)
        => buff?.Mod?.Name ?? "Terraria";

    private static SessionRecorder? ResolveRecorder()
    {
        var system = ModContent.GetInstance<ProfilerSystem>();
        return system?.LiveRecorder;
    }
}
