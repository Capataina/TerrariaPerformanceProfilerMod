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
        long buffs = 0, long biomeTicks = 0, long invasions = 0, long accTicks = 0)
        => new ModUsageEntry(1, items, npcs, npcsKilled, bosses, buffs, biomeTicks, invasions, accTicks);

    // ---- UsageWeight ----------------------------------------------------

    [Fact]
    public void UsageWeight_Default_IncludesInvasions()
    {
        // items 3 + npcs 5 + bosses 2 + buffs 4 + invasions 7 = 21.
        ModUsageEntry u = Usage(items: 3, npcs: 5, bosses: 2, buffs: 4, invasions: 7);
        Assert.Equal(21L, ModMetrics.UsageWeight(u));
    }

    [Fact]
    public void UsageWeight_ExcludeInvasions_MatchesDormantLegacyFormula()
    {
        // The I2 dormant surface historically dropped invasions; the flag must
        // reproduce exactly items + npcs + bosses + buffs = 14 (no +7).
        ModUsageEntry u = Usage(items: 3, npcs: 5, bosses: 2, buffs: 4, invasions: 7);
        Assert.Equal(14L, ModMetrics.UsageWeight(u, includeInvasions: false));
    }

    [Fact]
    public void UsageWeight_IgnoresNonEngagementCounters()
    {
        // NpcsKilled, TicksInOwnedBiomes, AccessoryEquippedTicks are NOT part of
        // the weight (they are separate axes); only the five engagement counts are.
        ModUsageEntry u = Usage(npcsKilled: 99, biomeTicks: 1234, accTicks: 5678);
        Assert.Equal(0L, ModMetrics.UsageWeight(u));
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
