#nullable enable

using Terraria.ModLoader.Config;

namespace PerformanceProfiler;

/// <summary>
/// Player-facing client-side configuration.
///
/// <para>
/// v0.9.0: the mod no longer has any in-game UI of its own. The browser
/// dashboard (F9 → <c>http://127.0.0.1:7777/</c>) is the only surface. All
/// previous overlay-related config knobs were removed alongside the
/// archived in-game overlay code. Nothing here today; this class is
/// preserved as a hook for future browser-side preferences if any ever
/// need to live in the standard Mod Config menu.
/// </para>
/// </summary>
[BackgroundColor(13, 17, 23)]
public sealed class ProfilerConfig : ModConfig
{
    public override ConfigScope Mode => ConfigScope.ClientSide;
}
