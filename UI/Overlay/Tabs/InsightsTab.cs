#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Profiling.Insights;
using PerformanceProfiler.UI.Overlay.Components;

namespace PerformanceProfiler.UI.Overlay.Tabs;

/// <summary>
/// The INSIGHTS tab, card-per-record layout. Each insight is one
/// <see cref="ProfilerCard"/> with the pattern name as the title, the
/// confidence and evidence-scope as <see cref="SeverityBadge"/> pills, the
/// subject (mod or hook) prominent below, the rendered body string on its
/// own line, and the supporting evidence in muted text.
///
/// <para>
/// The previous dense two-line-per-record layout was the worst readability
/// spot in the old UI. Cards give each insight breathing room and the
/// confidence + scope badges are now side-by-side instead of fighting for
/// space on a single text run.
/// </para>
/// </summary>
internal sealed class InsightsTab : IOverlayTab
{
    public string Label => "INSIGHTS";

    private const int VisibleCards = 6;
    private const float CardHeight = 72f;
    private const float CardHeightCompact = 56f;
    private const float CardGap = 6f;
    private const int RefreshIntervalTicks = 60;

    private readonly InsightsEngine _engine = InsightsEngine.GetOrCreateShared();
    private readonly List<InsightRecord> _ranked = new List<InsightRecord>(VisibleCards);
    private readonly List<string> _rankedBodies = new List<string>(VisibleCards);
    private const int BodyTruncateMax = 96;
    private long _nowTick;
    private long _lastRefreshTick = -RefreshIntervalTicks;

    public bool IsAvailable(MetricCollector? collector) => collector != null && collector.History.Count > 0;

    public void Tick(MetricCollector collector)
    {
        long sessionLengthTicks = collector.History.Count;
        long latestTick = sessionLengthTicks > 0
            ? collector.History[collector.History.Count - 1].TickIndex
            : 0L;
        _nowTick = latestTick;

        if (latestTick - _lastRefreshTick < RefreshIntervalTicks) return;
        _lastRefreshTick = latestTick;

        if (!OverlayState.Paused)
        {
            _engine.Evaluate(collector, latestTick, sessionLengthTicks);
        }
        _engine.Store.TopInto(_ranked, VisibleCards, latestTick);

        _rankedBodies.Clear();
        for (int i = 0; i < _ranked.Count; i++)
        {
            string body = InsightRenderer.Render(_ranked[i], Audience.Player, Density.Short);
            _rankedBodies.Add(OverlayDraw.Truncate(body, BodyTruncateMax));
        }
    }

    public float MeasurePanelHeight(MetricCollector collector)
    {
        float cardH = OverlayState.Mode == OverlayMode.Compact ? CardHeightCompact : CardHeight;
        int rows = _ranked.Count;
        if (rows == 0) return OverlayLayoutCurrent.ChromeHeight + 60f;
        return OverlayLayoutCurrent.ChromeHeight + rows * (cardH + CardGap) + 32f; // gated footer
    }

    public void HandleClick(float localX, float localY, MetricCollector collector) { }
    public void HandleScroll(int delta, MetricCollector collector) { }

    public void Draw(SpriteBatch sb, Rectangle area, MetricCollector collector)
    {
        int contentLeft = area.X + (int)OverlayLayoutCurrent.PanelPaddingX;
        int contentRight = area.Right - (int)OverlayLayoutCurrent.PanelPaddingX;
        int contentWidth = contentRight - contentLeft;
        int y = area.Y + (int)OverlayLayoutCurrent.ChromeHeight;
        float cardH = OverlayState.Mode == OverlayMode.Compact ? CardHeightCompact : CardHeight;

        int live = _engine.Store.LiveCount;
        if (_ranked.Count == 0)
        {
            Rectangle emptyCard = new Rectangle(contentLeft, y, contentWidth, (int)cardH);
            Rectangle body = ProfilerCard.Draw(sb, emptyCard,
                live == 0 ? "INSIGHTS  ·  none yet — play a session" : $"INSIGHTS  ·  {live} live",
                null);
            OverlayDraw.Text(sb, "insights appear when detectors observe meaningful patterns",
                new Vector2(body.X + 8, body.Y + 8),
                ProfilerTheme.TextDim,
                OverlayLayoutCurrent.TextScaleBody);
        }
        else
        {
            for (int i = 0; i < _ranked.Count; i++)
            {
                Rectangle cardRect = new Rectangle(contentLeft, y, contentWidth, (int)cardH);
                DrawInsightCard(sb, cardRect, _ranked[i], i < _rankedBodies.Count ? _rankedBodies[i] : string.Empty);
                y += (int)(cardH + CardGap);
            }
        }

        // Gated detectors footer.
        if (_engine.GatedLabel.Length > 0)
        {
            OverlayDraw.Text(sb, _engine.GatedLabel,
                new Vector2(contentLeft + 4, y + 4),
                ProfilerTheme.TextDim,
                OverlayLayoutCurrent.TextScaleBody);
        }
    }

    private static void DrawInsightCard(SpriteBatch sb, Rectangle cardRect, InsightRecord rec, string body)
    {
        // Title strip: pattern name + right-side stat ("12 hits" confirmation count).
        string title = rec.Pattern.ToString();
        string rightStat = $"{rec.ConfirmationCount} hit" + (rec.ConfirmationCount == 1 ? "" : "s");
        Rectangle bodyRect = ProfilerCard.Draw(sb, cardRect, title, rightStat);

        // Subject (mod name) — top-left of body in Row scale.
        string subject = "—";
        if (rec.Subject.ModId >= 0 && rec.Subject.ModId < HookInterceptor.ProfiledModNames.Length)
            subject = HookInterceptor.ProfiledModNames[rec.Subject.ModId];
        if (rec.Subject.HookId >= 0 && rec.Subject.HookId < PerModAttribution.Hooks.Count)
            subject += " · " + PerModAttribution.Hooks[rec.Subject.HookId].DisplayName;

        OverlayDraw.Text(sb, OverlayDraw.Truncate(subject, 64),
            new Vector2(bodyRect.X + 8, bodyRect.Y + 4),
            ProfilerTheme.Text,
            OverlayLayoutCurrent.TextScaleRow);

        // Body string on the next line.
        OverlayDraw.Text(sb, body,
            new Vector2(bodyRect.X + 8, bodyRect.Y + 22),
            ProfilerTheme.TextMuted,
            OverlayLayoutCurrent.TextScaleBody);

        // Bottom row: confidence + scope badges, plus evidence summary on the right.
        int badgeY = bodyRect.Bottom - 18;
        SeverityBadge.DrawConfidence(sb, new Vector2(bodyRect.X + 8, badgeY), rec.Confidence);
        int scopeX = bodyRect.X + 8 + SeverityBadge.MeasureWidth(rec.Confidence.ToString().ToLowerInvariant()) + 4;
        SeverityBadge.DrawScope(sb, new Vector2(scopeX, badgeY), rec.Scope);

        // Evidence summary on the right of the bottom row.
        string evidence = $"share {rec.Magnitude.RatioOrDelta:F2} · pAdj {rec.Evidence.PValueAdjusted:F2}";
        OverlayDraw.Text(sb, evidence,
            new Vector2(bodyRect.Right - evidence.Length * 6 - 8, badgeY + 2),
            ProfilerTheme.TextDim,
            OverlayLayoutCurrent.TextScaleBody);
    }
}
