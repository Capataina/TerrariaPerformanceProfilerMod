#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    /// <summary>The dashboard SPA. Single page; JS handles tab routing + polling.
    /// Per-pane HTML lives in <c>IndexHtml.&lt;Section&gt;.cs</c> partials, concatenated
    /// once at type-init in stable order.</summary>
    public static readonly string IndexHtml = string.Concat(
        HtmlPreamble,
        HtmlSummary,
        HtmlTimeline,
        HtmlLag,
        HtmlInsights,
        HtmlSelf,
        HtmlMemory,
        HtmlClosing);
}
