#nullable enable

using System;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.Profiling.Events;

/// <summary>
/// Vanilla weather, moon, and world-event flags collapsed into one bitset so a
/// tick's full weather context fits in a single <see cref="ushort"/>. The set
/// is fixed because tModLoader 1.4.4 has no API enumerating modded weather
/// events; see <c>context/notes/events-tab-plan.md</c> §4.2 for the evidence
/// ledger and §6 for the documented gap on modded events.
///
/// <para>
/// Adding a new vanilla flag is two edits: one row here and one row in the
/// <see cref="WeatherSources"/> declarative table. The order is the bit order
/// used in the session schema; do not reorder existing entries.
/// </para>
/// </summary>
[Flags]
internal enum WeatherFlags : ushort
{
    None          = 0,
    DayTime       = 1 << 0,
    BloodMoon     = 1 << 1,
    Eclipse       = 1 << 2,
    SlimeRain     = 1 << 3,
    PumpkinMoon   = 1 << 4,
    SnowMoon      = 1 << 5,
    Sandstorm     = 1 << 6,
    BirthdayParty = 1 << 7,
    LanternNight = 1 << 8,
    Raining       = 1 << 9,
    Halloween     = 1 << 10,
    Christmas     = 1 << 11,
}
