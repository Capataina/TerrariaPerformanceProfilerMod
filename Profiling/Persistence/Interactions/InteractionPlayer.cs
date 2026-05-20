#nullable enable

using System.Collections.Generic;
using PerformanceProfiler.Profiling.Persistence.Records;
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

    /// <summary>Last loadout fingerprint, so we only enqueue on actual change.</summary>
    private string _lastLoadoutFingerprint = "";

    /// <summary>30-second cadence for periodic loadout anchors.</summary>
    private long _lastPeriodicLoadoutTick;
    private const int PeriodicLoadoutIntervalTicks = 30 * 60;

    public override void OnHurt(Player.HurtInfo info)
    {
        var recorder = ResolveRecorder();
        if (recorder == null || Player.whoAmI != Main.myPlayer) return;

        var (kind, id, name) = ClassifyDeathReason(info.DamageSource);
        var row = new DamageTakenRow
        {
            Tick = (long)Main.GameUpdateCount,
            UnixMs = Time.UnixMsNow(),
            SourceKind = kind,
            SourceId = id,
            SourceName = name,
            DamageRaw = info.SourceDamage,
            DamageDealt = info.Damage,
            Pvp = info.PvP,
            Crit = false,                                  // Player.HurtInfo doesn't expose a Crit flag in tML 1.4.4
            HpBefore = Player.statLife + info.Damage,
            HpAfter = Player.statLife,
            MaxHp = Player.statLifeMax2,
            ActiveBuffs = SnapshotActiveBuffTypes(),
        };
        recorder.OnDamageTaken(row);
    }

    public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
    {
        var recorder = ResolveRecorder();
        if (recorder == null || Player.whoAmI != Main.myPlayer) return;

        recorder.OnDamageDealt(new DamageDealtRow
        {
            Tick = (long)Main.GameUpdateCount,
            UnixMs = Time.UnixMsNow(),
            Path = "melee",
            ItemId = 0,
            ProjectileId = 0,
            NpcType = target.type,
            NpcName = target.TypeName ?? "",
            DamageDealt = damageDone,
            Crit = hit.Crit,
            LoadoutFingerprint = _lastLoadoutFingerprint,
        });
    }

    public override void OnHitNPCWithItem(Item item, NPC target, NPC.HitInfo hit, int damageDone)
    {
        var recorder = ResolveRecorder();
        if (recorder == null || Player.whoAmI != Main.myPlayer) return;

        recorder.OnDamageDealt(new DamageDealtRow
        {
            Tick = (long)Main.GameUpdateCount,
            UnixMs = Time.UnixMsNow(),
            Path = "item",
            ItemId = item?.type ?? 0,
            ProjectileId = 0,
            NpcType = target.type,
            NpcName = target.TypeName ?? "",
            DamageDealt = damageDone,
            Crit = hit.Crit,
            LoadoutFingerprint = _lastLoadoutFingerprint,
        });
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
        recorder.OnDamageDealt(new DamageDealtRow
        {
            Tick = (long)Main.GameUpdateCount,
            UnixMs = Time.UnixMsNow(),
            Path = "projectile",
            ItemId = 0,
            ProjectileId = proj?.type ?? 0,
            NpcType = target.type,
            NpcName = target.TypeName ?? "",
            DamageDealt = damageDone,
            Crit = hit.Crit,
            LoadoutFingerprint = _lastLoadoutFingerprint,
        });
    }

    public override void PostUpdateBuffs()
    {
        // Hard gate: only the local player. Note we do NOT early-return here
        // before the diff (the v0.5 bug); for non-local-player ticks we still
        // skip the diff but we deliberately do not even touch state, so the
        // local-player branch's snapshot stays clean.
        if (Player.whoAmI != Main.myPlayer) return;

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
        var recorder = ResolveRecorder();
        if (recorder == null || Player.whoAmI != Main.myPlayer) return;

        var row = CaptureLoadout("change");
        if (row.Fingerprint != _lastLoadoutFingerprint)
        {
            _lastLoadoutFingerprint = row.Fingerprint;
            recorder.OnLoadoutSnapshot(row);
        }
        else
        {
            // Periodic anchor every 30s so insight queries can find at
            // least one snapshot in every time window.
            long tick = (long)Main.GameUpdateCount;
            if (tick - _lastPeriodicLoadoutTick >= PeriodicLoadoutIntervalTicks)
            {
                _lastPeriodicLoadoutTick = tick;
                row.Reason = "periodic";
                recorder.OnLoadoutSnapshot(row);
            }
        }
    }

    // ---- helpers --------------------------------------------------------

    private void EmitBuffEdge(SessionRecorder recorder, int buffType, string edge)
    {
        string name;
        try { name = Lang.GetBuffName(buffType) ?? ("buff-" + buffType); }
        catch { name = "buff-" + buffType; }
        string owner = OwningModName(BuffLoader.GetBuff(buffType));
        recorder.OnBuffEvent(new BuffEventRow
        {
            Tick = (long)Main.GameUpdateCount,
            UnixMs = Time.UnixMsNow(),
            Edge = edge,
            BuffType = buffType,
            BuffName = name,
            OwningMod = owner,
            DurationTicks = -1,
        });
    }

    private LoadoutSnapshotRow CaptureLoadout(string reason)
    {
        var slots = new List<EquipmentSlotEntry>(Player.armor.Length);
        int armorEnd = 3; // vanilla armor slots 0..2
        for (int i = 0; i < Player.armor.Length; i++)
        {
            var it = Player.armor[i];
            if (it == null || it.type == 0) continue;

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
        // held item type. Stable + cheap to compare.
        var sb = new System.Text.StringBuilder(slots.Count * 12 + 16);
        sb.Append('h').Append(Player.HeldItem?.type ?? 0).Append('|');
        foreach (var s in slots) sb.Append(s.Kind[0]).Append(s.Index).Append(':').Append(s.ItemType).Append('|');
        string fp = sb.ToString();

        return new LoadoutSnapshotRow
        {
            Tick = (long)Main.GameUpdateCount,
            UnixMs = Time.UnixMsNow(),
            Reason = reason,
            HeldItemType = Player.HeldItem?.type ?? 0,
            HeldItemName = Player.HeldItem?.Name ?? "",
            Slots = slots,
            Fingerprint = fp,
        };
    }

    private List<int> SnapshotActiveBuffTypes()
    {
        var list = new List<int>();
        for (int i = 0; i < Player.buffType.Length; i++)
            if (Player.buffType[i] > 0) list.Add(Player.buffType[i]);
        return list;
    }

    private static (string kind, int id, string name) ClassifyDeathReason(PlayerDeathReason reason)
    {
        // PlayerDeathReason exposes mutually-exclusive sources via int fields.
        // -1 = "not this source". Take the first that's set.
        if (reason.SourceProjectileLocalIndex >= 0 && reason.SourceProjectileType >= 0)
        {
            int type = reason.SourceProjectileType;
            string name;
            try { name = Lang.GetProjectileName(type)?.Value ?? ("proj-" + type); }
            catch
            {
                // Lang.GetProjectileName returns LocalizedText in some tML versions, plain string in others.
                // Fallback already covers both via .Value? (string returns null on .Value access in newer
                // builds, suppressed by the ?). Safe default below.
                name = "proj-" + type;
            }
            return ("projectile", type, name);
        }
        if (reason.SourceNPCIndex >= 0 && reason.SourceNPCIndex < Main.npc.Length)
        {
            int type = Main.npc[reason.SourceNPCIndex].type;
            return ("npc", type, Main.npc[reason.SourceNPCIndex].TypeName ?? ("npc-" + type));
        }
        if (reason.SourceOtherIndex >= 0)
        {
            return ("other", reason.SourceOtherIndex, OtherIndexName(reason.SourceOtherIndex));
        }
        if (reason.SourcePlayerIndex >= 0)
        {
            return ("player", reason.SourcePlayerIndex, "player");
        }
        if (!string.IsNullOrEmpty(reason.SourceCustomReason))
        {
            return ("custom", -1, reason.SourceCustomReason);
        }
        return ("unknown", -1, "(unknown)");
    }

    /// <summary>Vanilla "Other" source indices (lava, drowning, fall, etc.). See PlayerDeathReason.LegacyDefault constants.</summary>
    private static string OtherIndexName(int idx) => idx switch
    {
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
