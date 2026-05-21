#nullable enable

namespace PerformanceProfiler.Web;

/// <summary>
/// Bundles the dashboard's HTML, CSS, and JS as C# string constants.
/// Inlining keeps the mod a single self-contained .tmod with no asset
/// pipeline work; the trade-off is HTML/CSS/JS edits require a rebuild.
///
/// <para>
/// Each asset lives in its own partial-class file so the language tools
/// (linters, formatters, syntax highlighting in an IDE) can still see
/// the contents as a single logical string per asset. The router
/// <see cref="DashboardRouter"/> serves each at the corresponding path.
/// </para>
/// </summary>
internal static partial class DashboardAssets
{
}
