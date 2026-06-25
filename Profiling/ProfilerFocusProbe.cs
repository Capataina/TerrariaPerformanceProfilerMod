#nullable enable

using System;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.Profiling;

/// <summary>
/// Reads <c>Main.hasFocus</c> once per tick. Wrapped in a single static
/// surface so the StallDetector classifier can distinguish:
///
/// <list type="bullet">
/// <item>Focus lost during a long gap → real OS suspension (cmd-tab,
/// sleep, app switcher).</item>
/// <item>Focus retained throughout a long gap → main-thread freeze
/// (game thread blocked on lock / sync wait / render-thread stall);
/// the OS wasn't suspending us, the game just didn't advance.</item>
/// </list>
///
/// The v0.4 playtest misnamed a 1864 ms freeze as <c>ProcessSuspended</c>
/// even though the player never alt-tabbed. The focus signal is the
/// disambiguating bit.
///
/// <para>
/// <see cref="Read"/> returns true if <see cref="Terraria.Main.hasFocus"/>
/// is true, or true defensively if Main is unavailable (test paths). The
/// defensive path means "assume we had focus" so we don't false-positive
/// ProcessSuspended in environments where the field can't be read.
/// </para>
/// </summary>
internal static class ProfilerFocusProbe
{
    public static bool Read()
    {
        try
        {
            return Terraria.Main.hasFocus;
        }
        catch (Exception)
        {
            return true;
        }
    }
}
