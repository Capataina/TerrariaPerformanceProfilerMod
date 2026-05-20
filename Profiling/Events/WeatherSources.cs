#nullable enable

using System;
using Terraria;
using Terraria.GameContent.Events;

namespace PerformanceProfiler.Profiling.Events;

/// <summary>
/// Declarative table mapping each <see cref="WeatherFlags"/> bit to the
/// vanilla boolean that drives it. This is the one honest hardcode in the
/// Events design (plan §4.2): tModLoader 1.4.4 has no central enumeration of
/// weather/event flags, and the flags live on five different types
/// (<see cref="Main"/>, <see cref="Sandstorm"/>, <see cref="DD2Event"/>,
/// <see cref="LanternNight"/>, <see cref="BirthdayParty"/>). Adding a new
/// vanilla flag is one row here and one bit in <see cref="WeatherFlags"/>.
/// </summary>
internal static class WeatherSources
{
    /// <summary>Pairs of <c>(bit, current-value-reader)</c>. The reader is invoked from <see cref="ContextTagger"/> at the weather cadence (10 Hz).</summary>
    public static readonly (WeatherFlags Flag, Func<bool> Read)[] All = new (WeatherFlags, Func<bool>)[]
    {
        (WeatherFlags.DayTime,       () => Main.dayTime),
        (WeatherFlags.BloodMoon,     () => Main.bloodMoon),
        (WeatherFlags.Eclipse,       () => Main.eclipse),
        (WeatherFlags.SlimeRain,     () => Main.slimeRain),
        (WeatherFlags.PumpkinMoon,   () => Main.pumpkinMoon),
        (WeatherFlags.SnowMoon,      () => Main.snowMoon),
        (WeatherFlags.Sandstorm,     () => Sandstorm.Happening),
        (WeatherFlags.BirthdayParty, () => BirthdayParty.GenuineParty || BirthdayParty.ManualParty),
        (WeatherFlags.LanternNight,  () => LanternNight.GenuineLanterns || LanternNight.ManualLanterns),
        (WeatherFlags.Raining,       () => Main.raining),
        (WeatherFlags.Halloween,     () => Main.halloween),
        (WeatherFlags.Christmas,     () => Main.xMas),
    };

    /// <summary>Human-readable label for each flag; index matches the bit position.</summary>
    public static string DisplayName(WeatherFlags flag) => flag switch
    {
        WeatherFlags.DayTime       => "Day",
        WeatherFlags.BloodMoon     => "Blood Moon",
        WeatherFlags.Eclipse       => "Eclipse",
        WeatherFlags.SlimeRain     => "Slime Rain",
        WeatherFlags.PumpkinMoon   => "Pumpkin Moon",
        WeatherFlags.SnowMoon      => "Frost Moon",
        WeatherFlags.Sandstorm     => "Sandstorm",
        WeatherFlags.BirthdayParty => "Party",
        WeatherFlags.LanternNight  => "Lantern Night",
        WeatherFlags.Raining       => "Rain",
        WeatherFlags.Halloween     => "Halloween",
        WeatherFlags.Christmas     => "Christmas",
        _ => flag.ToString(),
    };
}
