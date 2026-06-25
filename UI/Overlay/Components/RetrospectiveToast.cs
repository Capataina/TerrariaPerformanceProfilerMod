// ARCHIVED v0.9.0: in-game overlay replaced by browser dashboard.
// Sources retained for possible Steam-Deck variant or future revival;
// remove the #if false / #endif guards below to restore compilation.
#if false
#nullable enable

using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;
using PerformanceProfiler.Profiling;
using PerformanceProfiler.Data.Aggregators.Segments;
using PerformanceProfiler.UI;

using PerformanceProfiler.Data.Detectors;
using PerformanceProfiler.Data.Aggregators;
using PerformanceProfiler.Data.Stats;
using PerformanceProfiler.Persistence.Streams;
using PerformanceProfiler.Data.Collectors;
using PerformanceProfiler.Profiling.Events;
using PerformanceProfiler.Persistence;
using PerformanceProfiler.Persistence.Records;
namespace PerformanceProfiler.UI.Overlay.Components;

/// <summary>
/// Retrospective-card pop-up. Slides in from the bottom-right corner when
/// a promoted segment lands; auto-fades after a few seconds; clickable to
/// dismiss.
///
/// <para>
/// State is process-global (singleton-style static) because toasts are
/// independent of which overlay tab is active and survive overlay-toggle.
/// Drained once per frame from <see cref="SegmentStore.TryDequeueToast"/>.
/// </para>
///
/// <para>
/// Toast layout: anchored bottom-right of the screen, stacked vertically
/// with newer toasts at the bottom, ~340 px wide × <see cref="SegmentCard.PreferredHeight"/>
/// + 4 px each. Up to <see cref="MaxVisible"/> toasts on screen at once;
/// excess queued and shown as room frees up.
/// </para>
/// </summary>
internal static class RetrospectiveToast
{
    private const int Width = 340;
    private const int CardH = SegmentCard.PreferredHeight;
    private const int RibbonH = 16;            // separate band ABOVE the card carrying the promotion reason
    private const int ToastH = CardH + RibbonH; // total vertical footprint per toast
    private const int Gap = 6;
    private const int MarginX = 18;
    private const int MarginY = 36;
    private const int MaxVisible = 3;
    private const long LifetimeMs = 12_000;
    private const long FadeMs = 600;

    private struct Toast
    {
        public Segment Segment;
        public long SpawnUnixMs;
        public bool Dismissed;
    }

    private static readonly List<Toast> _live = new();
    private static readonly Queue<Segment> _pending = new();

    /// <summary>Drain freshly-promoted segments from the store and enqueue toasts.</summary>
    public static void Pump()
    {
        // Config gate: when toasts are disabled, drain-and-discard any pending
        // ones so they don't pile up against a future toggle-on.
        ProfilerConfig? cfg = ModContent.GetInstance<ProfilerConfig>();
        bool enabled = cfg == null || cfg.EnableRetrospectiveToasts;

        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        SegmentStore? store = sys?.SegmentStore;
        if (store == null) return;

        if (!enabled)
        {
            while (store.TryDequeueToast(out _)) { /* drop */ }
            _live.Clear();
            _pending.Clear();
            return;
        }

        while (store.TryDequeueToast(out Segment? seg))
        {
            if (seg != null) _pending.Enqueue(seg);
        }

        long now = Time.UnixMsNow();

        // Expire / dismiss handling.
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            Toast t = _live[i];
            if (t.Dismissed || now - t.SpawnUnixMs > LifetimeMs)
            {
                _live.RemoveAt(i);
            }
        }

        // Promote pending to live.
        while (_live.Count < MaxVisible && _pending.Count > 0)
        {
            Segment seg = _pending.Dequeue();
            _live.Add(new Toast { Segment = seg, SpawnUnixMs = now });
        }
    }

    /// <summary>Draw every currently-visible toast over the rest of the UI.</summary>
    public static void Draw(SpriteBatch sb)
    {
        if (_live.Count == 0) return;

        ProfilerSystem? sys = ModContent.GetInstance<ProfilerSystem>();
        SegmentStore? store = sys?.SegmentStore;

        int screenW = Main.screenWidth;
        int screenH = Main.screenHeight;
        long now = Time.UnixMsNow();

        // Stack bottom-up. Each toast is ribbon (16 px) + card.
        int y = screenH - MarginY - ToastH;
        int x = screenW - MarginX - Width;

        for (int i = 0; i < _live.Count; i++)
        {
            Toast t = _live[i];
            long age = now - t.SpawnUnixMs;
            float alpha = 1f;
            if (age < FadeMs) alpha = age / (float)FadeMs;
            else if (LifetimeMs - age < FadeMs) alpha = (LifetimeMs - age) / (float)FadeMs;
            if (alpha < 0f) alpha = 0f;
            if (alpha > 1f) alpha = 1f;

            // Slide-in offset on first FadeMs.
            int slide = age < FadeMs ? (int)(40 * (1f - alpha)) : 0;

            Rectangle ribbonRect = new Rectangle(x + slide, y, Width, RibbonH);
            Rectangle cardRect = new Rectangle(x + slide, y + RibbonH, Width, CardH);

            DrawRibbon(sb, ribbonRect, t.Segment.PromotionReason);

            double avg = store?.LifetimeAvgMsPerTick(t.Segment.Family, t.Segment.Key) ?? 0d;
            int samples = store?.LifetimeSampleCount(t.Segment.Family, t.Segment.Key) ?? 0;
            SegmentCard.Draw(sb, cardRect, t.Segment, avg, samples);

            y -= ToastH + Gap;
        }
    }

    /// <summary>Click anywhere on a visible toast dismisses it. Returns true if a toast was hit.</summary>
    public static bool TryDismiss(int screenX, int screenY)
    {
        int screenW = Main.screenWidth;
        int screenH = Main.screenHeight;
        int y = screenH - MarginY - ToastH;
        int x = screenW - MarginX - Width;

        for (int i = 0; i < _live.Count; i++)
        {
            var rect = new Rectangle(x, y, Width, ToastH);
            if (rect.Contains(screenX, screenY))
            {
                Toast t = _live[i];
                t.Dismissed = true;
                _live[i] = t;
                return true;
            }
            y -= ToastH + Gap;
        }
        return false;
    }

    private static void DrawRibbon(SpriteBatch sb, Rectangle ribbon, string reason)
    {
        Color stripe = reason switch
        {
            "boss-kill" => ProfilerTheme.Good,
            "death"     => ProfilerTheme.Danger,
            "stall"     => ProfilerTheme.Danger,
            "spike"     => ProfilerTheme.Amber,
            "outlier"   => ProfilerTheme.Amber,
            "rare"      => ProfilerTheme.Amber,
            "bookmark"  => ProfilerTheme.Text,
            _ => ProfilerTheme.TextMuted,
        };

        // Filled ribbon band darkened with the stripe colour as accent.
        ProfilerTheme.FillRect(sb, ribbon, ProfilerTheme.Header);
        ProfilerTheme.FillRect(sb, new Rectangle(ribbon.X, ribbon.Y, ribbon.Width, 2), stripe);

        if (!string.IsNullOrEmpty(reason))
        {
            OverlayDraw.Text(sb, reason.ToUpperInvariant(),
                new Vector2(ribbon.X + 8, ribbon.Y + 3),
                stripe, OverlayLayoutCurrent.TextScaleBody);
        }
    }
}

#endif
