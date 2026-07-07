#nullable enable

using System;
using System.Collections.Generic;
using PerformanceProfiler.Persistence.Report;
using Xunit;

namespace PerformanceProfiler.Tests.Simulation;

/// <summary>
/// S17 pins: the HTML report's self-containment contract and section
/// rendering, against a synthetic session shaped like the 2026-07-07 slow-mo
/// capture. The reader's LiteDB round-trip lives with the Ring-2 persistence
/// tests; these pin the pure writer.
/// </summary>
public sealed class ReportPins
{
    private static SessionReportData Sample()
    {
        var d = new SessionReportData
        {
            StartedUtc = new DateTime(2026, 7, 7, 19, 23, 0, DateTimeKind.Utc),
            EndedUtc = new DateTime(2026, 7, 7, 19, 37, 0, DateTimeKind.Utc),
            DurationMs = 14 * 60 * 1000,
            ProfilerVersion = "0.34.0",
            ModlistFingerprint = "abcd1234efgh5678",
            ModVersions = new List<string> { "CalamityMod@2.0.4", "ThoriumMod@1.7.2" },
            TicksObserved = 50_000,
            AvgFrameMs = 28.2,   // the honest slow-mo average
            MedianFrameMs = 33.2,
            MaxFrameMs = 78.8,
            SpikeCount = 50,
            StallCount = 3,
            PausedMs = 111_000,
            PauseCount = 3,
        };
        for (int m = 0; m < 14; m++) d.Minutes.Add((m, m is > 4 and < 9 ? 33d : 16.5, 40d));
        d.PerMod.Add(("CalamityMod", 7.36, 138.8, 1_400_000));
        d.PerMod.Add(("ImproveGame", 5.94, 22.1, 364_800));
        d.Stalls.Add((1_783_450_000_000, 2892d, "MainThreadFreeze", "disruptive"));
        d.Spikes.Add((1137, 40.1, 4.8));
        d.Segments.Add((2, "Eye of Cthulhu", 95_000, 21.4, 6));
        d.Insights.Add(("game time is advancing at 51% of real-time speed…", "preliminary", "ThisSession"));
        d.Insights.Add(("BannerCollector costs 60× more in one of your 2 modlists…", "low", "LifetimeData"));
        return d;
    }

    [Fact]
    public void Render_IsSelfContained_NoNetworkReferences()
    {
        string html = HtmlReportWriter.Render(Sample());

        // Env-gated artefact save: PP_WRITE_REPORT_SAMPLE=<dir> writes the
        // rendered sample so the e2e sweep can load it over file:// in a real
        // browser (the render check the pure-string asserts cannot do).
        string? outDir = Environment.GetEnvironmentVariable("PP_WRITE_REPORT_SAMPLE");
        if (!string.IsNullOrEmpty(outDir))
        {
            System.IO.Directory.CreateDirectory(outDir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "report-sample.html"), html);
        }

        // The property that makes the artefact durable: file:// with the
        // network cable pulled renders identically, forever.
        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=", html);
        Assert.DoesNotContain("@import", html);
    }

    [Fact]
    public void Render_CarriesEverySection_AndTheHonestyContract()
    {
        string html = HtmlReportWriter.Render(Sample());

        Assert.Contains("session report", html);
        Assert.Contains("per-minute frame health", html);
        Assert.Contains("per-mod cost", html);
        Assert.Contains("rough moments", html);
        Assert.Contains("encounters", html);
        Assert.Contains("insights", html);
        Assert.Contains("CalamityMod", html);
        Assert.Contains("Eye of Cthulhu", html);
        // The honest slow-mo numbers, not compute-window fiction.
        Assert.Contains("35", html);        // 1000/28.2 ≈ 35 fps
        // X3 travels into the artefact: pauses named and excluded.
        Assert.Contains("excluded from the stall list", html);
        // Data-strength badges + the descriptive-never-normative footer.
        Assert.Contains("lifetime data", html);
        Assert.Contains("this session", html);
        Assert.Contains("descriptive, never normative", html);
        // Escaping: mod names render escaped-safe (no raw angle brackets survive).
        Assert.DoesNotContain("<CalamityMod>", html);
    }

    [Fact]
    public void Render_EmptySections_AreOmittedNotBroken()
    {
        var d = new SessionReportData
        {
            StartedUtc = DateTime.UtcNow,
            EndedUtc = DateTime.UtcNow,
            DurationMs = 20_000,
            ProfilerVersion = "0.34.0",
            AvgFrameMs = 16.6,
            MedianFrameMs = 16.6,
            MaxFrameMs = 18.0,
        };
        string html = HtmlReportWriter.Render(d);

        Assert.DoesNotContain("per-minute frame health", html); // no minutes
        Assert.DoesNotContain("rough moments", html);           // no stalls/spikes
        Assert.Contains("session report", html);                 // shell intact
    }
}
