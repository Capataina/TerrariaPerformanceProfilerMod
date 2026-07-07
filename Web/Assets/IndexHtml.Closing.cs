#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    private const string HtmlClosing = @"</main>

  <footer class=""footstrip"">
    <span id=""foot-cadence"">polling /api · 500 ms · 1-7 to switch tabs</span>
    <span id=""foot-clock"">—</span>
    <span class=""foot-spacer""></span>
    <span id=""foot-mode"">—</span>
  </footer>

  <!-- ===== Mod card slide-in (shared .drawer component) ================= -->
  <aside class=""drawer hidden"" id=""modcard"" role=""dialog"" aria-label=""mod detail"">
    <header class=""drawer-h"">
      <span class=""dr-close"" id=""mc-close"" title=""close"">&times;</span>
      <span class=""dr-title"" id=""mc-name"">—</span>
      <span class=""dr-meta"" id=""mc-rank"">—</span>
    </header>
    <div class=""drawer-body"" id=""mc-body""></div>
  </aside>

  <!-- ===== Tooltip layer (single instance reused) ======================= -->
  <div class=""tooltip hidden"" id=""tooltip""></div>

</div>

<script src=""/dashboard.js""></script>
</body>
</html>
";
}
