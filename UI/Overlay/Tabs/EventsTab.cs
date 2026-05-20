#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Events;

namespace PerformanceProfiler.UI.Overlay.Tabs;

/// <summary>
/// One row in the Events list, ready to render. Rebuilt every Tick from the
/// aggregator's snapshot so per-frame the row list is fresh; this is the
/// only Events-specific allocation that runs at refresh cadence, and it is
/// bounded by the bucket count (typically well under 200).
/// </summary>
internal readonly struct EventRow
{
    public readonly EventDimension Dimension;
    public readonly int Key;
    public readonly string DimensionLabel;
    public readonly string DisplayName;
    public readonly double AvgMs;
    public readonly double PeakMs;
    public readonly long DwellTicks;
    public readonly int Spikes;
    public readonly bool Active;

    public EventRow(EventDimension dim, int key, string dimLabel, string name,
                    double avgMs, double peakMs, long dwellTicks, int spikes, bool active)
    {
        Dimension = dim;
        Key = key;
        DimensionLabel = dimLabel;
        DisplayName = name;
        AvgMs = avgMs;
        PeakMs = peakMs;
        DwellTicks = dwellTicks;
        Spikes = spikes;
        Active = active;
    }
}

/// <summary>
/// The Events tab: per-tick game-state context correlated with frame time.
/// Each row is one bucket in one dimension (biome, weather, boss, invasion,
/// difficulty, subworld). Rows update every frame from
/// <see cref="EventAggregator"/>'s snapshot; colour grading is against the
/// session running mean (the honest contract from plan section 9.4 — a
/// bucket is "expensive" relative to typical session behaviour, not
/// relative to itself).
///
/// <para>
/// Sort order: dimension first (biome → weather → boss → invasion →
/// difficulty → subworld), then by average ms descending within each
/// dimension. This keeps the most-expensive bucket per group at the top
/// of its group while letting the player scan the dimension column for
/// what they care about.
/// </para>
/// </summary>
internal sealed class EventsTab : IOverlayTab
{
    public string Label => "EVENTS";

    /// <summary>Pruning threshold: buckets with less than this many ticks are tracked but not rendered (plan section 9.5).</summary>
    private const int MinDwellTicksToRender = 60; // 1 second @ 60 tps

    private const float RowHeight = 16f;
    private const int VisibleRowCount = 12;

    /// <summary>Refresh cadence: rebuild rows + "now-active" summary at 1 Hz instead of 60 Hz. Inputs are 30s-smoothed so 60-frame staleness is invisible.</summary>
    private const int RefreshIntervalTicks = 60;

    /// <summary>Tab rows, rebuilt at 1 Hz from the aggregator. Sorted as documented above.</summary>
    private EventRow[] _rows = Array.Empty<EventRow>();
    private int _rowCount;
    private int _scrollOffset;

    // Throttle / cache state. _tickCounter advances per-frame; _lastRefreshTick
    // records the last counter value at which we did the heavy rebuild + summary.
    // _cachedNowSummary holds the last "Forest · Day · Hardmode" header string
    // so Draw doesn't have to rebuild it 60× per second.
    private long _tickCounter;
    private long _lastRefreshTick = -RefreshIntervalTicks;
    private string _cachedNowSummary = "(no buckets active)";

    public bool IsAvailable(MetricCollector? collector)
    {
        if (collector == null) return false;
        return ModContent.GetInstance<ProfilerSystem>()?.Events != null;
    }

    public void Tick(MetricCollector collector)
    {
        EventAggregator? agg = ModContent.GetInstance<ProfilerSystem>()?.Events;
        if (agg == null)
        {
            _rowCount = 0;
            return;
        }

        _tickCounter++;
        // 1 Hz refresh. SnapshotRows allocates a fresh List per call; doing it
        // at 60 Hz produces ~200-600 KB/s of garbage in a profiler designed
        // to surface allocation pressure. The bucket dictionaries the
        // aggregator maintains DO update every tick; the displayed table just
        // doesn't need to follow that closely.
        if (_tickCounter - _lastRefreshTick >= RefreshIntervalTicks)
        {
            _lastRefreshTick = _tickCounter;
            BuildRows(agg);
            _cachedNowSummary = ComputeNowActiveSummary(agg);
        }

        int maxOff = Math.Max(0, _rowCount - VisibleRowCount);
        if (_scrollOffset > maxOff) _scrollOffset = maxOff;
    }

    public float MeasurePanelHeight(MetricCollector collector)
    {
        float h = OverlayLayoutCurrent.ChromeHeight + 32f;
        if (_rowCount == 0)
        {
            h += 24f; // empty-state hint
            return h;
        }
        int visible = Math.Min(_rowCount - _scrollOffset, VisibleRowCount);
        h += visible * RowHeight;
        if (_rowCount > _scrollOffset + visible) h += 14f;
        h += 18f; // footer note about modded events
        return h;
    }

    public void HandleClick(float localX, float localY, MetricCollector collector)
    {
        // Drill-down (per-bucket per-mod expansion) is a v2 feature; v1 is
        // read-only.
    }

    public void HandleScroll(int delta, MetricCollector collector)
    {
        int maxOff = Math.Max(0, _rowCount - VisibleRowCount);
        _scrollOffset = Math.Clamp(_scrollOffset + delta, 0, maxOff);
    }

    // ---- Row building --------------------------------------------------------

    private void BuildRows(EventAggregator agg)
    {
        List<EventBucketRow> snapshot = agg.SnapshotRows(MinDwellTicksToRender);
        if (_rows.Length < snapshot.Count)
        {
            _rows = new EventRow[Math.Max(snapshot.Count, 16)];
        }
        _rowCount = 0;
        foreach (EventBucketRow r in snapshot)
        {
            _rows[_rowCount++] = new EventRow(
                dim:       r.Dimension,
                key:       r.Key,
                dimLabel:  DimensionLabel(r.Dimension),
                name:      r.DisplayName,
                avgMs:     r.Stats.AvgMs,
                peakMs:    r.Stats.PeakMs,
                dwellTicks: r.Stats.Ticks,
                spikes:    r.Stats.SpikeCount,
                active:    r.Active);
        }

        // Sort: dimension order first, then avg ms desc.
        // Array.Sort on a Span over the populated prefix avoids re-allocating
        // the snapshot list.
        Array.Sort(_rows, 0, _rowCount, RowOrdering.Instance);
    }

    private sealed class RowOrdering : IComparer<EventRow>
    {
        public static readonly RowOrdering Instance = new RowOrdering();
        public int Compare(EventRow a, EventRow b)
        {
            int dimCmp = ((int)a.Dimension).CompareTo((int)b.Dimension);
            if (dimCmp != 0) return dimCmp;
            return b.AvgMs.CompareTo(a.AvgMs); // descending within dimension
        }
    }

    private static string DimensionLabel(EventDimension d) => d switch
    {
        EventDimension.Biome      => "Biome",
        EventDimension.Weather    => "Weather",
        EventDimension.Boss       => "Boss",
        EventDimension.Invasion   => "Invasion",
        EventDimension.Difficulty => "Mode",
        EventDimension.Subworld   => "Subworld",
        _ => "?",
    };

    // ---- Drawing -------------------------------------------------------------

    public void Draw(SpriteBatch sb, Rectangle area, MetricCollector collector)
    {
        EventAggregator? agg = ModContent.GetInstance<ProfilerSystem>()?.Events;

        int divY = area.Y + (int)OverlayLayoutCurrent.ChromeHeight;
        ProfilerTheme.FillRect(sb, new Rectangle(area.X + 8, divY, area.Width - 16, 1), ProfilerTheme.Border);
        ProfilerTheme.FillRect(sb, new Rectangle(area.X + 8, divY + 5, 2, 14), ProfilerTheme.Accent);

        string header = agg == null || !agg.HasContext
            ? "EVENTS   ·   waiting for first tick"
            : $"EVENTS   ·   now: {_cachedNowSummary}";
        OverlayDraw.Text(sb, header, new Vector2(area.X + 18, divY + 6f), ProfilerTheme.Accent, 0.7f);

        if (_rowCount == 0)
        {
            string hint = agg == null
                ? "events aggregator not armed (enter a world)"
                : "no buckets yet — buckets appear once a context is active for 1 s";
            OverlayDraw.Text(sb, hint,
                new Vector2(area.X + 18, area.Y + OverlayLayoutCurrent.ChromeHeight + 38f),
                ProfilerTheme.TextDim, 0.6f);
            DrawFooter(sb, area, area.Y + OverlayLayoutCurrent.ChromeHeight + 62f);
            return;
        }

        double sessionAvg = agg?.SessionAverageMs ?? 0d;
        // Section header sits at divY+6; the column header used to land at
        // ChromeHeight+8 — a 2-pixel gap which superimposed on low-res
        // panels (the "EVENTS … bucket" overlap visible in screenshots).
        // Bump the data-row start so the column header has its own row.
        float rowY = area.Y + OverlayLayoutCurrent.ChromeHeight + 38f;
        int visible = Math.Min(_rowCount - _scrollOffset, VisibleRowCount);

        // Column header strip — sits one full row below the section header,
        // not overlapping it.
        DrawColumnHeader(sb, area, rowY - 14f);

        for (int i = 0; i < visible; i++)
        {
            int idx = _scrollOffset + i;
            DrawRow(sb, area, _rows[idx], rowY, sessionAvg);
            rowY += RowHeight;
        }

        if (_scrollOffset + visible < _rowCount)
        {
            OverlayDraw.Text(sb, $"+ {_rowCount - _scrollOffset - visible} more (scroll)",
                new Vector2(area.X + 18, rowY + 2f), ProfilerTheme.TextDim, 0.58f);
            rowY += 14f;
        }

        DrawFooter(sb, area, rowY + 4f);
    }

    private static void DrawColumnHeader(SpriteBatch sb, Rectangle area, float y)
    {
        Color c = ProfilerTheme.TextDim;
        OverlayDraw.Text(sb, "dim",     new Vector2(area.X + 18,  y), c, 0.54f);
        OverlayDraw.Text(sb, "bucket",  new Vector2(area.X + 70,  y), c, 0.54f);
        OverlayDraw.Text(sb, "dwell",   new Vector2(area.X + 360, y), c, 0.54f);
        OverlayDraw.Text(sb, "avg ms",  new Vector2(area.X + 420, y), c, 0.54f);
        OverlayDraw.Text(sb, "peak",    new Vector2(area.X + 488, y), c, 0.54f);
        OverlayDraw.Text(sb, "spikes",  new Vector2(area.X + 548, y), c, 0.54f);
    }

    private static void DrawRow(SpriteBatch sb, Rectangle area, EventRow row, float y, double sessionAvg)
    {
        // Dimension column.
        OverlayDraw.Text(sb, row.DimensionLabel,
            new Vector2(area.X + 18, y + 2),
            row.Active ? ProfilerTheme.Accent : ProfilerTheme.TextMuted,
            0.62f);

        // Bucket name.
        string name = OverlayDraw.Truncate(row.DisplayName, 38);
        Color nameColor = row.Active ? ProfilerTheme.Text : ProfilerTheme.TextMuted;
        OverlayDraw.Text(sb, name, new Vector2(area.X + 70, y + 2), nameColor, 0.66f);

        // Active glyph.
        if (row.Active)
        {
            OverlayDraw.Text(sb, "*", new Vector2(area.X + 60, y + 2), ProfilerTheme.Accent, 0.7f);
        }

        // Dwell -- mm:ss.
        OverlayDraw.Text(sb, FormatDwell(row.DwellTicks),
            new Vector2(area.X + 360, y + 2), ProfilerTheme.TextMuted, 0.62f);

        // Avg ms with cost-graded colour against session mean (plan section 9.4).
        double ratio = sessionAvg > 0d ? row.AvgMs / sessionAvg : 1d;
        Color costColor = ratio <= 1.0 ? ProfilerTheme.Good
                        : ratio <= 1.5 ? ProfilerTheme.Amber
                        : ProfilerTheme.Danger;
        OverlayDraw.Text(sb, row.AvgMs.ToString("F2"),
            new Vector2(area.X + 420, y + 2), costColor, 0.66f);

        OverlayDraw.Text(sb, row.PeakMs.ToString("F1"),
            new Vector2(area.X + 488, y + 2), ProfilerTheme.TextMuted, 0.6f);

        OverlayDraw.Text(sb, row.Spikes > 0 ? row.Spikes.ToString() : "—",
            new Vector2(area.X + 548, y + 2),
            row.Spikes > 0 ? ProfilerTheme.Danger : ProfilerTheme.TextDim,
            0.6f);
    }

    private static void DrawFooter(SpriteBatch sb, Rectangle area, float y)
    {
        OverlayDraw.Text(sb,
            "modded events without a vanilla flag are not attributed (this session)",
            new Vector2(area.X + 18, y),
            ProfilerTheme.TextDim, 0.52f);
    }

    private static string FormatDwell(long ticks)
    {
        // Terraria runs at 60 ticks/second. mm:ss is enough resolution for
        // sessions; rolls over at 60 minutes which is acceptable.
        long seconds = ticks / 60;
        long m = seconds / 60;
        long s = seconds % 60;
        return $"{m:D2}:{s:D2}";
    }

    /// <summary>
    /// Renders the "Forest · Day · Hardmode" header summary from the live
    /// aggregator. Called at 1 Hz from <see cref="Tick"/> and the result is
    /// cached in <see cref="_cachedNowSummary"/>; Draw reads the cache.
    /// </summary>
    private static string ComputeNowActiveSummary(EventAggregator agg)
    {
        var parts = new List<string>(8);

        // Biomes — only the ones marked active this tick. Cap to first three.
        for (int i = 0; i < BiomeRegistry.Count; i++)
        {
            if (!agg.IsActiveNow(EventDimension.Biome, i)) continue;
            if (parts.Count >= 3) break;
            parts.Add(BiomeRegistry.Biomes[i].DisplayName);
        }

        // Weather flags.
        foreach (var pair in WeatherSources.All)
        {
            if (!agg.IsActiveNow(EventDimension.Weather, (int)pair.Flag)) continue;
            parts.Add(WeatherSources.DisplayName(pair.Flag));
            if (parts.Count >= 6) break;
        }

        // Active boss (just one).
        BossSlotArray bosses = agg.Latest.Bosses;
        if (bosses.Count > 0)
        {
            parts.Add(BossSampler.DisplayName(bosses[0]));
        }

        // Vanilla invasion -- typed lookup (was .ToString() which boxes the enum).
        InvasionId inv = agg.Latest.VanillaInvasion;
        if (inv != InvasionId.None)
        {
            parts.Add(InvasionShortName(inv));
        }

        if (parts.Count == 0) return "(no buckets active)";
        return string.Join(" · ", parts);
    }

    private static string InvasionShortName(InvasionId id) => id switch
    {
        InvasionId.Goblins     => "Goblin Army",
        InvasionId.FrostLegion => "Frost Legion",
        InvasionId.Pirates     => "Pirate Invasion",
        InvasionId.Martians    => "Martian Madness",
        InvasionId.OldOnesArmy => "Old Ones Army",
        _ => "Invasion",
    };
}
