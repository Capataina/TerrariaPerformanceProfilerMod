using Microsoft.Xna.Framework;
using Terraria.GameContent.UI.Elements;
using Terraria.ModLoader;
using Terraria.UI;
using PerformanceProfiler.Profiling;

namespace PerformanceProfiler.UI;

/// <summary>
/// The F9 profiler overlay: a single panel showing live per-tick measurements
/// drawn from <see cref="ProfilerSystem"/>'s rolling history.
///
/// Milestone 1 scope is the overall tick cost, not yet the per-mod btop tree
/// (that is Milestone 2). The text lines refreshed every frame are fields;
/// static text is a local in <see cref="OnInitialize"/>.
/// </summary>
public sealed class ProfilerOverlay : UIState
{
    // Set in OnInitialize, which Activate() always runs before the first Update;
    // null! documents that the compiler's nullable check is satisfied there.
    private UIText _tickTime = null!;
    private UIText _avgTickTime = null!;
    private UIText _gcTime = null!;
    private UIText _entityCounts = null!;
    private UIText _status = null!;

    public override void OnInitialize()
    {
        UIPanel panel = new UIPanel();
        panel.Left.Set(16f, 0f);
        panel.Top.Set(16f, 0f);
        panel.Width.Set(300f, 0f);
        panel.Height.Set(176f, 0f);
        panel.BackgroundColor = new Color(14, 18, 26) * 0.92f;
        panel.BorderColor = new Color(40, 48, 60) * 0.92f;
        Append(panel);

        AddLine(panel, 6f, "PERFORMANCE PROFILER", 1f);
        _tickTime = AddLine(panel, 38f, "tick: --");
        _avgTickTime = AddLine(panel, 62f, "avg 30s: --");
        _gcTime = AddLine(panel, 86f, "gc pause: --");
        _entityCounts = AddLine(panel, 110f, "entities: --");
        _status = AddLine(panel, 140f, "waiting for a world...");
    }

    /// <summary>Creates a left-aligned text line at the given y offset and appends it to the panel.</summary>
    private static UIText AddLine(UIPanel panel, float top, string text, float scale = 0.85f)
    {
        UIText line = new UIText(text, scale);
        line.Left.Set(6f, 0f);
        line.Top.Set(top, 0f);
        panel.Append(line);
        return line;
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        MetricCollector? collector = ModContent.GetInstance<ProfilerSystem>()?.Collector;
        if (collector == null || collector.History.Count == 0)
        {
            _status.SetText("waiting for a world...");
            _tickTime.SetText("tick: --");
            _avgTickTime.SetText("avg 30s: --");
            _gcTime.SetText("gc pause: --");
            _entityCounts.SetText("entities: --");
            return;
        }

        RingBuffer<TickFrame> history = collector.History;
        TickFrame latest = history.Newest;

        _tickTime.SetText($"tick: {latest.FrameTimeMs:F2} ms");
        _avgTickTime.SetText($"avg 30s: {AverageFrameTimeMs(history):F2} ms");
        _gcTime.SetText($"gc pause: {latest.GcTimeMs:F2} ms");
        _entityCounts.SetText($"npc {latest.NpcCount}   proj {latest.ProjectileCount}   dust {latest.DustCount}");
        _status.SetText($"tick #{latest.TickIndex}  ({history.Count} sampled)");
    }

    /// <summary>Mean tick wall-time across the whole rolling history.</summary>
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
