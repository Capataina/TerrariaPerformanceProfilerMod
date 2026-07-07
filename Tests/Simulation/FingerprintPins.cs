#nullable enable

using PerformanceProfiler.Persistence;
using Xunit;

namespace PerformanceProfiler.Tests.Simulation;

/// <summary>
/// X7 pins: the modlist fingerprint's v2 identity rules. The live store had
/// 10 "modlists seen" across 11 sessions because v1 hashed load order + mod
/// versions + the profiler itself; each rule here is one of the fracture
/// modes, pinned.
/// </summary>
public sealed class FingerprintPins
{
    private static readonly string[] Stack =
        { "CalamityMod", "ThoriumMod", "BossChecklist", "ImproveGame" };

    [Fact]
    public void SameSet_DifferentOrder_SameFingerprint()
    {
        string a = FingerprintCore.Compute(new[] { "CalamityMod", "ThoriumMod", "BossChecklist" });
        string b = FingerprintCore.Compute(new[] { "ThoriumMod", "BossChecklist", "CalamityMod" });
        Assert.Equal(a, b);
    }

    [Fact]
    public void ProfilerPresence_DoesNotChangeIdentity()
    {
        string without = FingerprintCore.Compute(Stack);
        var with = new string[Stack.Length + 1];
        Stack.CopyTo(with, 0);
        with[^1] = FingerprintCore.SelfName;
        Assert.Equal(without, FingerprintCore.Compute(with));
    }

    [Fact]
    public void SetChange_ChangesIdentity()
    {
        string a = FingerprintCore.Compute(Stack);
        string b = FingerprintCore.Compute(new[] { "CalamityMod", "ThoriumMod", "BossChecklist" });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void EmptyAndSelfOnly_AreTheSameIdentity()
    {
        // A vanilla-plus-profiler install and a truly empty list are the same
        // "no modlist" identity — the profiler never counts itself.
        Assert.Equal(
            FingerprintCore.Compute(System.Array.Empty<string>()),
            FingerprintCore.Compute(new[] { FingerprintCore.SelfName }));
    }
}
