#nullable enable

using System;
using PerformanceProfiler.Profiling;
using Xunit;

namespace PerformanceProfiler.Tests;

/// <summary>
/// Pins the Baseline service that every detector now reads from instead of
/// carrying its own absolute floor. The two pieces that matter are: median
/// is calibration-gated (no values until we've seen at least
/// <see cref="Baseline.MinCalibrationTicks"/> samples), and median tracks
/// the real distribution (high-refresh and low-refresh sessions produce
/// dramatically different baselines, which is the whole point).
/// </summary>
public class BaselineTests
{
    [Fact]
    public void Uncalibrated_BeforeMinTicks()
    {
        Baseline b = new Baseline();
        RingBuffer<TickFrame> hist = new RingBuffer<TickFrame>(1800);
        PushFrames(hist, frameMs: 16.0, periodMs: 16.0, count: Baseline.MinCalibrationTicks - 1);

        b.Recompute(hist, tracksAllocations: false, allocBytesThisTick: 0);

        Assert.False(b.IsCalibrated);
    }

    [Fact]
    public void Calibrated_AtMinTicks()
    {
        Baseline b = new Baseline();
        RingBuffer<TickFrame> hist = new RingBuffer<TickFrame>(1800);
        PushFrames(hist, frameMs: 16.0, periodMs: 16.0, count: Baseline.MinCalibrationTicks);

        b.Recompute(hist, tracksAllocations: false, allocBytesThisTick: 0);

        Assert.True(b.IsCalibrated);
    }

    [Fact]
    public void Median_TracksHighRefreshSession()
    {
        // 120 fps player: frame median ~8.3 ms.
        Baseline b = new Baseline();
        RingBuffer<TickFrame> hist = new RingBuffer<TickFrame>(1800);
        PushFrames(hist, frameMs: 8.3, periodMs: 8.3, count: 600);

        b.Recompute(hist, tracksAllocations: false, allocBytesThisTick: 0);

        Assert.True(b.IsCalibrated);
        Assert.InRange(b.FrameMsMedian, 8.0, 9.0);
        Assert.InRange(b.TickPeriodMsMedian, 8.0, 9.0);
    }

    [Fact]
    public void Median_TracksLowRefreshSession()
    {
        // 25 fps player: frame median ~40 ms.
        Baseline b = new Baseline();
        RingBuffer<TickFrame> hist = new RingBuffer<TickFrame>(1800);
        PushFrames(hist, frameMs: 40.0, periodMs: 40.0, count: 600);

        b.Recompute(hist, tracksAllocations: false, allocBytesThisTick: 0);

        Assert.True(b.IsCalibrated);
        Assert.InRange(b.FrameMsMedian, 39.5, 40.5);
        Assert.InRange(b.TickPeriodMsMedian, 39.5, 40.5);
    }

    [Fact]
    public void Median_IsRobustAgainstSpikes()
    {
        // 99% of frames at 8 ms, 1% at 200 ms — median must stay at 8 ms,
        // not drift toward the spikes the way a mean would.
        Baseline b = new Baseline();
        RingBuffer<TickFrame> hist = new RingBuffer<TickFrame>(1800);
        for (int i = 0; i < 990; i++)
        {
            TickFrame f = new TickFrame { FrameTimeMs = 8.0, TimestampUnixMs = i * 8 };
            hist.Push(in f);
        }
        for (int i = 0; i < 10; i++)
        {
            TickFrame f = new TickFrame { FrameTimeMs = 200.0, TimestampUnixMs = 8000 + i * 200 };
            hist.Push(in f);
        }

        b.Recompute(hist, tracksAllocations: false, allocBytesThisTick: 0);

        Assert.InRange(b.FrameMsMedian, 7.5, 8.5);
    }

    [Fact]
    public void AllocRate_ZeroWhenTrackingOff()
    {
        Baseline b = new Baseline();
        RingBuffer<TickFrame> hist = new RingBuffer<TickFrame>(1800);
        PushFrames(hist, frameMs: 16.0, periodMs: 16.0, count: 60);

        b.Recompute(hist, tracksAllocations: false, allocBytesThisTick: 12345);

        Assert.Equal(0d, b.AllocBytesPerTickMedian);
    }

    [Fact]
    public void AllocRate_ConvergesWhenTrackingOn()
    {
        Baseline b = new Baseline();
        RingBuffer<TickFrame> hist = new RingBuffer<TickFrame>(1800);
        for (int i = 0; i < 200; i++)
        {
            TickFrame f = new TickFrame { FrameTimeMs = 16.0, TimestampUnixMs = i * 16 };
            hist.Push(in f);
            b.Recompute(hist, tracksAllocations: true, allocBytesThisTick: 10_000);
        }

        // EMA with alpha=0.05, 200 iterations toward 10_000 should be ~very close.
        Assert.InRange(b.AllocBytesPerTickMedian, 9_900d, 10_100d);
    }

    [Fact]
    public void Reset_ClearsBaseline()
    {
        Baseline b = new Baseline();
        RingBuffer<TickFrame> hist = new RingBuffer<TickFrame>(1800);
        PushFrames(hist, frameMs: 16.0, periodMs: 16.0, count: 200);
        b.Recompute(hist, tracksAllocations: true, allocBytesThisTick: 5_000);

        Assert.True(b.IsCalibrated);
        Assert.True(b.FrameMsMedian > 0);

        b.Reset();

        Assert.False(b.IsCalibrated);
        Assert.Equal(0d, b.FrameMsMedian);
        Assert.Equal(0d, b.TickPeriodMsMedian);
        Assert.Equal(0d, b.AllocBytesPerTickMedian);
    }

    private static void PushFrames(RingBuffer<TickFrame> hist, double frameMs, double periodMs, int count)
    {
        long ts = 0;
        for (int i = 0; i < count; i++)
        {
            TickFrame f = new TickFrame { FrameTimeMs = frameMs, TimestampUnixMs = ts };
            hist.Push(in f);
            ts += (long)periodMs;
        }
    }
}
