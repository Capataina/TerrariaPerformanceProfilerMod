#nullable enable

namespace PerformanceProfiler.Profiling.Events;

/// <summary>
/// Mirror of Terraria's four difficulty modes. Read from
/// <c>Main.GameModeInfo</c> by <see cref="ContextTagger"/>; stable for the
/// lifetime of a world.
/// </summary>
internal enum GameMode : byte
{
    Classic = 0,
    Expert  = 1,
    Master  = 2,
    Journey = 3,
}
