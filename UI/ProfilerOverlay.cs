#nullable enable

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using Terraria.UI;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.UI;

/// <summary>
/// The F9 profiler overlay: a single dark panel, custom-drawn via
/// <see cref="ProfilerTheme"/> to match design/Mockups.html, showing live
/// per-tick measurements from <see cref="ProfilerSystem"/>'s rolling history.
///
/// Milestone 2 scope here is the overall-cost panel with the modern look; the
/// foldable per-mod tree is added by the later M2 tasks.
/// </summary>
public sealed class ProfilerOverlay : UIState
{
    private const float PanelWidth = 360f;
    private const float PanelHeight = 190f;

    public override void OnInitialize()
    {
        OverlayPanel panel = new OverlayPanel();
        panel.Left.Set(16f, 0f);
        panel.Top.Set(16f, 0f);
        panel.Width.Set(PanelWidth, 0f);
        panel.Height.Set(PanelHeight, 0f);
        Append(panel);
    }
}

/// <summary>
/// The custom-drawn overlay panel. Everything inside is hand-drawn in
/// <see cref="DrawSelf"/> with <see cref="ProfilerTheme"/>; no stock tModLoader
/// widget chrome is used.
/// </summary>
internal sealed class OverlayPanel : UIElement
{
    private const float HeaderHeight = 26f;
    private const float LineGap = 24f;

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        // Block gameplay clicks while the cursor is over the panel.
        if (IsMouseHovering)
        {
            Main.LocalPlayer.mouseInterface = true;
        }
    }

    protected override void DrawSelf(SpriteBatch spriteBatch)
    {
        Rectangle area = GetDimensions().ToRectangle();

        // Panel surface and header strip.
        ProfilerTheme.DrawPanel(spriteBatch, area, ProfilerTheme.Panel, ProfilerTheme.Border);
        Rectangle header = new Rectangle(area.X, area.Y, area.Width, (int)HeaderHeight);
        ProfilerTheme.FillRect(spriteBatch, header, ProfilerTheme.Header);
        ProfilerTheme.DrawBorder(spriteBatch, header, ProfilerTheme.Border);
        DrawText(spriteBatch, "PERFORMANCE PROFILER", new Vector2(area.X + 10, area.Y + 7), ProfilerTheme.Accent, 0.82f);

        float x = area.X + 14;
        float y = area.Y + HeaderHeight + 14f;

        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (collector == null || collector.History.Count == 0)
        {
            DrawText(spriteBatch, "waiting for a world...", new Vector2(x, y), ProfilerTheme.TextMuted, 0.8f);
            return;
        }

        RingBuffer<TickFrame> history = collector.History;
        TickFrame latest = history.Newest;

        DrawStat(spriteBatch, "tick", $"{latest.FrameTimeMs:F2} ms", x, y, ProfilerTheme.Amber);
        DrawStat(spriteBatch, "avg 30s", $"{AverageFrameTimeMs(history):F2} ms", x, y + LineGap, ProfilerTheme.Text);
        DrawStat(spriteBatch, "gc pause", $"{latest.GcTimeMs:F2} ms", x, y + LineGap * 2f, ProfilerTheme.Good);
        DrawStat(spriteBatch, "entities",
            $"npc {latest.NpcCount}   proj {latest.ProjectileCount}   dust {latest.DustCount}",
            x, y + LineGap * 3f, ProfilerTheme.Text);

        DrawText(spriteBatch, $"tick #{latest.TickIndex}    {history.Count} sampled",
            new Vector2(x, y + LineGap * 4f + 6f), ProfilerTheme.TextDim, 0.72f);
    }

    /// <summary>Draws a "label   value" stat line: a muted label, then the value in its colour.</summary>
    private static void DrawStat(SpriteBatch spriteBatch, string label, string value, float x, float y, Color valueColor)
    {
        DrawText(spriteBatch, label, new Vector2(x, y), ProfilerTheme.TextMuted, 0.8f);
        DrawText(spriteBatch, value, new Vector2(x + 92f, y), valueColor, 0.8f);
    }

    /// <summary>Draws a line of text with the slight border that keeps it readable over gameplay.</summary>
    private static void DrawText(SpriteBatch spriteBatch, string text, Vector2 position, Color color, float scale)
    {
        Utils.DrawBorderString(spriteBatch, text, position, color, scale);
    }

    private static double AverageFrameTimeMs(RingBuffer<TickFrame> history)
    {
        if (history.Count == 0)
        {
            return 0d;
        }

        double sum = 0d;
        for (int i = 0; i < history.Count; i++)
        {
            sum += history[i].FrameTimeMs;
        }

        return sum / history.Count;
    }
}
