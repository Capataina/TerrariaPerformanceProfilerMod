#nullable enable

using PerformanceProfiler.Data.Contracts;
using PerformanceProfiler.Insights.Shared;
using Xunit;

namespace PerformanceProfiler.Tests.Insights;

/// <summary>
/// Pins the Wave 1 Shared primitives that the duplication census collapsed into
/// one home. These are the byte-identical contract the later waves rely on: if a
/// future change drifts UsageWeight, RosterSize, SafeShare, or the fold, these
/// fail before the dashboard ever sees the wrong number.
/// </summary>
public sealed class SharedPrimitivesTests
{
    private static ModUsageEntry Usage(
        long items = 0, long npcs = 0, long npcsKilled = 0, long bosses = 0,
        long buffs = 0, long biomeTicks = 0, long invasions = 0, long accTicks = 0,
        long heldTicks = 0, long armorTicks = 0)
        => new ModUsageEntry(1, items, npcs, npcsKilled, bosses, buffs, biomeTicks,
            invasions, accTicks, heldTicks, armorTicks);

    // ---- UsageWeight (Wave 4: active-use, not creation) -----------------

    [Fact]
    public void UsageWeight_IsTheSumOfActiveUseTicks()
    {
        // held 100 + accessory 50 + armour 30 + biome 20 = 200.
        ModUsageEntry u = Usage(heldTicks: 100, accTicks: 50, armorTicks: 30, biomeTicks: 20);
        Assert.Equal(200L, ModMetrics.UsageWeight(u));
    }

    [Fact]
    public void UsageWeight_HeldItemAloneCounts_TheFluteFix()
    {
        // The Flute case: a weapon wielded but never crafted. ItemsCreated is 0,
        // but the item is held — the old creation-weighted formula read 0 (the bug);
        // the active-use weight reads the held ticks.
        ModUsageEntry flute = Usage(items: 0, heldTicks: 36000);
        Assert.Equal(36000L, ModMetrics.UsageWeight(flute));
        Assert.True(ModMetrics.UsageWeight(flute) > 0, "a held-but-not-crafted item must register as used");
    }

    [Fact]
    public void UsageWeight_IgnoresCreationAndEncounterCounters()
    {
        // ItemsCreated / NpcsSpawned / BossesFought / BuffsApplied / Invasions are
        // the *creation* axis (CreationWeight), never the use axis. None contribute.
        ModUsageEntry u = Usage(items: 9, npcs: 9, bosses: 9, buffs: 9, invasions: 9, npcsKilled: 9);
        Assert.Equal(0L, ModMetrics.UsageWeight(u));
    }

    [Fact]
    public void CreationWeight_SumsTheCreationEncounterAxis()
    {
        // items 3 + npcs 5 + bosses 2 + buffs 4 + invasions 7 = 21; held/worn ignored.
        ModUsageEntry u = Usage(items: 3, npcs: 5, bosses: 2, buffs: 4, invasions: 7,
            heldTicks: 999, accTicks: 999);
        Assert.Equal(21L, ModMetrics.CreationWeight(u));
    }

    // ---- RosterSize -----------------------------------------------------

    [Fact]
    public void RosterSize_SumsEightFields_ExcludingBiomes()
    {
        // Items..Bosses = 1+2+3+4+5+6+7+8 with Biomes(=100) deliberately excluded.
        var r = new ModRosterEntry(1, "X",
            Items: 1, NPCs: 2, Buffs: 3, Projectiles: 4, Mounts: 5,
            Accessories: 6, Biomes: 100, Invasions: 7, Bosses: 8);
        Assert.Equal(1 + 2 + 3 + 4 + 5 + 6 + 7 + 8, ModMetrics.RosterSize(r));
    }

    // ---- SumModCategories ----------------------------------------------

    [Fact]
    public void SumModCategories_FoldsOneModsRow()
    {
        // Row-major [mod0: 1,2,3][mod1: 10,20,30], catCount 3.
        double[] rowMajor = { 1, 2, 3, 10, 20, 30 };
        Assert.Equal(6d, ModMetrics.SumModCategories(rowMajor, 0, 3));
        Assert.Equal(60d, ModMetrics.SumModCategories(rowMajor, 1, 3));
    }

    [Fact]
    public void SumModCategories_ToleratesShortArray()
    {
        // The inline folds guarded against a per-mod array wider than the source;
        // the helper stops at the array bound rather than throwing.
        double[] rowMajor = { 1, 2 }; // only 2 of the implied 3 cats present.
        Assert.Equal(3d, ModMetrics.SumModCategories(rowMajor, 0, 3));
    }

    // ---- Shares ---------------------------------------------------------

    [Theory]
    [InlineData(5d, 20d, 0.25d)]
    [InlineData(1d, 0d, 0d)]     // zero total -> 0, never a divide-by-zero.
    [InlineData(3d, -2d, 0d)]    // non-positive total -> 0.
    public void SafeShare_Double(double value, double total, double expected)
        => Assert.Equal(expected, Shares.SafeShare(value, total));

    [Fact]
    public void SafeShare_Long_CastsNumeratorBeforeDivide()
        => Assert.Equal(0.5d, Shares.SafeShare(3L, 6L));

    [Fact]
    public void Percentage_ScalesByHundred()
        => Assert.Equal(25d, Shares.Percentage(1d, 4d));

    [Fact]
    public void TopN_SortsThenTruncates()
    {
        var list = new System.Collections.Generic.List<int> { 3, 1, 4, 1, 5, 9, 2 };
        Shares.TopN(list, 3, (a, b) => b.CompareTo(a)); // descending
        Assert.Equal(new[] { 9, 5, 4 }, list);
    }

    [Theory]
    // Two of {50,30,10,5,5}=100 reach 70% (50+30=80 >= 70).
    [InlineData(new double[] { 50, 30, 10, 5, 5 }, 0.70, 2)]
    // The single 90 already covers 70% of 100.
    [InlineData(new double[] { 90, 4, 3, 2, 1 }, 0.70, 1)]
    // Perfectly even: need 4 of 5 (each 20%) to clear 70%.
    [InlineData(new double[] { 20, 20, 20, 20, 20 }, 0.70, 4)]
    public void ParetoCount_FindsTheLeverSet(double[] sortedDesc, double fraction, int expected)
    {
        double total = 0;
        foreach (double v in sortedDesc) total += v;
        Assert.Equal(expected, Shares.ParetoCount(sortedDesc, total, fraction));
    }

    [Fact]
    public void ParetoCount_ZeroTotal_IsZero()
        => Assert.Equal(0, Shares.ParetoCount(new double[] { 0, 0 }, 0d, 0.7));

    // ---- ModNames -------------------------------------------------------

    [Fact]
    public void SafeName_InRange_ReturnsName()
        => Assert.Equal("Calamity", ModNames.SafeName(1, new[] { "Terraria", "Calamity" }));

    [Fact]
    public void SafeName_OutOfRange_DefaultFallback()
        => Assert.Equal("mod-5", ModNames.SafeName(5, new[] { "Terraria" }));

    [Fact]
    public void SafeName_OutOfRange_CustomFallback()
        => Assert.Equal("—", ModNames.SafeName(-1, new[] { "Terraria" }, "—"));
}
