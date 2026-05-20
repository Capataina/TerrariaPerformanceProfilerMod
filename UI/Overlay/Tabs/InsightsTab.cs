#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Insights;

namespace PerformanceProfiler.UI.Overlay.Tabs;

/// <summary>
/// The INSIGHTS tab. Renders the top-ranked records from a shared
/// <see cref="InsightsEngine"/> as one short-form line per row plus a
/// confidence badge.
///
/// <para>
/// The engine itself is owned by this tab as a singleton-of-process for
/// now (no <c>ProfilerSystem</c> wiring yet, deliberately scoped to keep
/// the tab self-contained). Every per-frame <see cref="Tick"/> drives one
/// detector pass against the live collector, which is cheap because every
/// in-scope detector reads already-smoothed accessors. When the Events
/// tab and LiteDB land the engine can move up to a shared owner and the
/// tab becomes a pure view.
/// </para>
/// </summary>
internal sealed class InsightsTab : IOverlayTab
{
    public string Label => "INSIGHTS";

    private const float RowHeight = 28f;
    private const int VisibleRows = 8;

    private readonly InsightsEngine _engine = new InsightsEngine();
    private IReadOnlyList<InsightRecord> _ranked = System.Array.Empty<InsightRecord>();
    private long _nowTick;

    public bool IsAvailable(MetricCollector? collector) => collector != null && collector.History.Count > 0;

    public void Tick(MetricCollector collector)
    {
        // History.Last gives us the most recent tick index. If history is
        // empty we already returned false from IsAvailable.
        long sessionLengthTicks = collector.History.Count;
        long latestTick = sessionLengthTicks > 0
            ? collector.History[collector.History.Count - 1].TickIndex
            : 0L;
        _nowTick = latestTick;

        if (!OverlayState.Paused)
        {
            _engine.Evaluate(collector, latestTick, sessionLengthTicks);
        }
        _ranked = _engine.Store.Top(VisibleRows, latestTick);
    }

    public float MeasurePanelHeight(MetricCollector collector)
    {
        int rows = _ranked.Count;
        if (rows == 0) return OverlayLayout.RowsTopOffset + 40f;
        return OverlayLayout.RowsTopOffset + 10f + rows * RowHeight + 14f;
    }

    public void HandleClick(float localX, float localY, MetricCollector collector)
    {
        // No drill-down yet; the click-through panel is a future feature.
    }

    public void HandleScroll(int delta, MetricCollector collector)
    {
        // No scroll yet; the top-8 cap from the store means everything fits.
    }

    public void Draw(SpriteBatch sb, Rectangle area, MetricCollector collector)
    {
        int divY = area.Y + (int)OverlayLayout.DividerOffset;
        ProfilerTheme.FillRect(sb, new Rectangle(area.X + 8, divY, area.Width - 16, 1), ProfilerTheme.Border);
        ProfilerTheme.FillRect(sb, new Rectangle(area.X + 8, divY + 5, 2, 14), ProfilerTheme.Accent);

        int live = _engine.Store.LiveCount;
        string header = live == 0
            ? "INSIGHTS   ·   none yet — play a session"
            : $"INSIGHTS   ·   {_ranked.Count} of {live} live · top by score";
        OverlayDraw.Text(sb, header, new Vector2(area.X + 18, divY + 6f), ProfilerTheme.Accent, 0.72f);

        if (_ranked.Count == 0)
        {
            OverlayDraw.Text(sb,
                "insights appear when detectors observe meaningful patterns",
                new Vector2(area.X + 18, area.Y + OverlayLayout.RowsTopOffset + 4),
                ProfilerTheme.TextDim, 0.6f);
            return;
        }

        float rowY = area.Y + OverlayLayout.RowsTopOffset;
        for (int i = 0; i < _ranked.Count; i++)
        {
            DrawInsightRow(sb, area, _ranked[i], rowY);
            rowY += RowHeight;
        }

        IReadOnlyDictionary<string, List<string>> gated = _engine.GatedPatterns();
        if (gated.Count > 0)
        {
            OverlayDraw.Text(sb,
                "gated detectors waiting on: " + string.Join(", ", System.Linq.Enumerable.ToArray(gated.Keys)),
                new Vector2(area.X + 18, rowY + 2f), ProfilerTheme.TextDim, 0.55f);
        }
    }

    private static void DrawInsightRow(SpriteBatch sb, Rectangle area, InsightRecord rec, float y)
    {
        string label = rec.Pattern.ToString();
        OverlayDraw.Text(sb, label, new Vector2(area.X + 18, y), ProfilerTheme.TextMuted, 0.6f);

        Color badgeColor = rec.Confidence switch
        {
            Confidence.High => ProfilerTheme.Good,
            Confidence.Medium => ProfilerTheme.Amber,
            Confidence.Low => ProfilerTheme.TextMuted,
            _ => ProfilerTheme.TextDim,
        };
        string badge = rec.Confidence == Confidence.Preliminary ? "preliminary" : rec.Confidence.ToString();
        OverlayDraw.Text(sb, badge, new Vector2(area.X + 540f, y), badgeColor, 0.6f);

        string body = InsightRenderer.Render(rec, Audience.Player, Density.Short);
        OverlayDraw.Text(sb, OverlayDraw.Truncate(body, 80), new Vector2(area.X + 26f, y + 13f),
            ProfilerTheme.Text, 0.62f);
    }
}
