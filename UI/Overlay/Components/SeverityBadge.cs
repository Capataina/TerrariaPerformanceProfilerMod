// ARCHIVED v0.9.0: in-game overlay replaced by browser dashboard.
// Sources retained for possible Steam-Deck variant or future revival;
// remove the #if false / #endif guards below to restore compilation.
#if false
#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Detectors.Insights;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Data.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Profiling.Persistence;
using PerformanceProfiler.Profiling.Persistence.Records;
namespace PerformanceProfiler.UI.Overlay.Components;

/// <summary>
/// Coloured pill that represents a severity level (spike severity, stall
/// severity, confidence, evidence scope, self-health severity). One
/// implementation; one colour mapping per enum kind so the overlay never
/// drifts on what "amber" means.
///
/// <para>
/// The badge is two pieces: a thin coloured left edge that reads as the
/// severity hue at a glance, and a label in muted text. Compared to a
/// fully-coloured pill this stays legible even on dense rows where many
/// badges might sit in close proximity.
/// </para>
/// </summary>
internal static class SeverityBadge
{
    private const int BadgeMinWidth = 56;

    /// <summary>Draw a badge for a <see cref="StallSeverity"/>.</summary>
    public static void DrawStall(SpriteBatch sb, Vector2 anchor, StallSeverity severity)
    {
        Color hue = severity switch
        {
            StallSeverity.Freeze     => ProfilerTheme.Danger,
            StallSeverity.Disruptive => ProfilerTheme.Danger,
            StallSeverity.Noticeable => ProfilerTheme.Amber,
            _                         => ProfilerTheme.TextMuted,
        };
        Draw(sb, anchor, severity.ToString().ToLowerInvariant(), hue);
    }

    /// <summary>Draw a badge for an insight <see cref="Confidence"/>.</summary>
    public static void DrawConfidence(SpriteBatch sb, Vector2 anchor, Confidence confidence)
    {
        Color hue = confidence switch
        {
            Confidence.High        => ProfilerTheme.Good,
            Confidence.Medium      => ProfilerTheme.Amber,
            Confidence.Low         => ProfilerTheme.TextMuted,
            _                      => ProfilerTheme.TextDim,
        };
        string label = confidence == Confidence.Preliminary ? "prelim" : confidence.ToString().ToLowerInvariant();
        Draw(sb, anchor, label, hue);
    }

    /// <summary>Draw a badge for an insight <see cref="EvidenceScope"/>.</summary>
    public static void DrawScope(SpriteBatch sb, Vector2 anchor, EvidenceScope scope)
    {
        Color hue = scope switch
        {
            EvidenceScope.LifetimeData     => ProfilerTheme.Accent,
            EvidenceScope.NeedsPersistence => ProfilerTheme.Amber,
            _                              => ProfilerTheme.TextDim,
        };
        string label = scope switch
        {
            EvidenceScope.LifetimeData     => "lifetime",
            EvidenceScope.NeedsPersistence => "needs db",
            _                              => "this run",
        };
        Draw(sb, anchor, label, hue);
    }

    /// <summary>Draw a badge for <see cref="SelfHealthSeverity"/>.</summary>
    public static void DrawSelfHealth(SpriteBatch sb, Vector2 anchor, SelfHealthSeverity severity)
    {
        Color hue = severity switch
        {
            SelfHealthSeverity.Severe     => ProfilerTheme.Danger,
            SelfHealthSeverity.Concerning => ProfilerTheme.Amber,
            _                             => ProfilerTheme.Good,
        };
        Draw(sb, anchor, severity.ToString().ToLowerInvariant(), hue);
    }

    /// <summary>Generic draw — caller provides label + colour.</summary>
    public static void Draw(SpriteBatch sb, Vector2 anchor, string label, Color hue)
    {
        float scale = OverlayLayoutCurrent.TextScaleBody;
        int charW = (int)(6 * scale / 0.62f);
        int padding = 6;
        int width = System.Math.Max(BadgeMinWidth, label.Length * charW + padding * 2 + 4); // +4 for the left bar
        int height = (int)(14 * scale / 0.62f);

        Rectangle area = new Rectangle((int)anchor.X, (int)anchor.Y, width, height);
        ProfilerTheme.FillRect(sb, area, ProfilerTheme.Panel);
        ProfilerTheme.DrawBorder(sb, area, ProfilerTheme.Border);

        // Coloured left edge — the severity hue.
        ProfilerTheme.FillRect(sb, new Rectangle(area.X, area.Y, 3, area.Height), hue);

        // Label text.
        OverlayDraw.Text(sb, label, new Vector2(area.X + 6, area.Y + 1), hue, scale);
    }

    /// <summary>Width of a badge for the supplied label, useful when packing badges right-to-left.</summary>
    public static int MeasureWidth(string label)
    {
        float scale = OverlayLayoutCurrent.TextScaleBody;
        int charW = (int)(6 * scale / 0.62f);
        int padding = 6;
        return System.Math.Max(BadgeMinWidth, label.Length * charW + padding * 2 + 4);
    }
}

#endif
