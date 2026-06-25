/* ============================================================
   App shell — explorer, tabs, content panes, right rail
   v3 — interactive everywhere
   ============================================================ */
(function () {
  const A = window.ARCH;
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));
  const esc = s => String(s).replace(/[&<>]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" }[c]));
  const escAttr = s => esc(s).replace(/"/g, "&quot;");
  let graph = null;

  /* ---- derived indexes ---- */
  const nodeById = {}; A.nodes.forEach(n => nodeById[n.id] = n);
  const riskByNode = {}; A.risks.forEach(r => (riskByNode[r.node] = riskByNode[r.node] || []).push(r));
  const hotspotByNode = {}; A.changeFrontier.forEach(c => { hotspotByNode[c.node] = Math.max(hotspotByNode[c.node] || 0, Math.max(...c.bars)); });
  // entity -> owner node (for universal hover linking).
  // The tracked-entity set is derived at runtime from A.nodes[].state and
  // A.stateOwnership[]. No hardcoded project-specific identifiers.
  const ENTITY = {};
  const TRACKED = [];
  function normNode(name) {
    if (!name) return null;
    const lc = String(name).toLowerCase();
    for (const n of A.nodes) {
      if (lc === n.id || lc === (n.label || "").toLowerCase()) return n.id;
    }
    for (const n of A.nodes) {
      if (lc.includes(n.id) || (n.label && lc.includes(n.label.toLowerCase()))) return n.id;
    }
    return null;
  }
  (A.stateOwnership || []).forEach(s => {
    const owner = normNode(s.owner);
    if (owner && s.items) {
      String(s.items).split(/[,;]\s*/).forEach(raw => {
        const key = raw.trim().split(" (")[0].split(".")[0];
        if (key && /^[A-Z][\w]*$/.test(key) && !ENTITY[key]) {
          ENTITY[key] = owner;
          TRACKED.push(key);
        }
      });
    }
  });
  (A.nodes || []).forEach(n => (n.state || []).forEach(st => {
    const key = String(st).split(" (")[0].split(".")[0];
    if (key && !ENTITY[key]) {
      ENTITY[key] = n.id;
      TRACKED.push(key);
    }
  }));
  window.App = window.App || {};
  window.App.ENTITY = ENTITY;
  function linkifyStates(str) {
    let out = esc(str);
    TRACKED.forEach(t => {
      const re = new RegExp("\\b" + t.replace(/[.*+?^${}()|[\]\\]/g, "\\$&") + "\\b", "g");
      out = out.replace(re, `<span class="ent" data-entity="${t}">${t}</span>`);
    });
    return out;
  }
  function downstreamNodes(id) {
    const seen = new Set([id]); let fr = [id];
    while (fr.length) { const nx = []; fr.forEach(u => A.edges.forEach(e => { if (e.from === u && e.rel !== "peer" && !seen.has(e.to)) { seen.add(e.to); nx.push(e.to); } })); fr = nx; }
    seen.delete(id); return [...seen];
  }
  function daysSince(d) { return Math.round((Date.now() - new Date(d).getTime()) / 86400000); }

  /* path step label -> node id (for tracing on graph).
     Derived at runtime from A.dataFlow.steps[].sys mapping plus the optional
     A.dataFlow.pathMap override for projects that want custom routing. */
  const PATHMAP = (function () {
    const m = {};
    (A.dataFlow && A.dataFlow.steps || []).forEach(s => {
      const sys = String(s.sys || "").split("::")[0];
      if (sys && nodeById[sys]) m[sys] = sys;
    });
    Object.assign(m, (A.dataFlow && A.dataFlow.pathMap) || {});
    return m;
  })();
  function pathNodeSeq(steps) {
    const ids = (steps || []).map(s => PATHMAP[s] || (nodeById[s] ? s : null)).filter(Boolean);
    return ids.filter((id, i) => id !== ids[i - 1]); // collapse repeats
  }

  /* ---------------- tab registry ---------------- */
  const TABS = [
    { id: "graph", label: "Graph", ti: "◉", badge: A.nodes.length + " nodes" },
    { id: "flow", label: "Data Flow", ti: "↯", badge: ((A.dataFlow && A.dataFlow.steps) || []).length },
    { id: "deps", label: "Dependencies", ti: "⇄", badge: A.relationships.length },
    { id: "cov", label: "Coverage", ti: "▦" },
    { id: "paths", label: "Paths", ti: "⌥", badge: A.criticalPaths.length },
    { id: "concept", label: "Concept", ti: "✦" },
    { id: "source", label: "Source", ti: "#" },
  ];

  /* ---------------- top bar ---------------- */
  function renderTopbar() {
    const stale = daysSince(A.project.regenerated);
    const freshClass = stale > 30 ? "stale" : "fresh";
    $("#topbar").innerHTML = `
      <button class="tb-panel-toggle on" id="toggleLeft" title="Toggle explorer (⌘B)">☰</button>
      <div class="tb-brand"><div class="tb-logo"></div></div>
      <div class="tb-crumb">
        <span class="dim">context</span><span class="sep">/</span>
        <span class="proj">${esc(A.project.name)}</span><span class="sep">/</span>
        <span class="file">${esc(A.project.file ? A.project.file.split("/").pop() : "architecture.html")}</span>
      </div>
      <div class="tb-status"><span class="dot"></span>${esc(A.project.milestone)}</div>
      <div class="tb-spacer"></div>
      <button class="tb-panel-toggle on" id="toggleBottom" title="Toggle bottom strip">▭</button>
      <div class="tb-spacer"></div>
      <div class="tb-search-wrap">
        <div class="tb-search">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="11" cy="11" r="7"/><path d="m21 21-4.3-4.3"/></svg>
          <input id="search" placeholder="Search anything…" autocomplete="off" />
          <kbd>⌘K</kbd>
        </div>
        <div class="search-results" id="searchResults"></div>
      </div>
      <div class="tb-fresh ${freshClass}" data-tip="${escAttr("architecture.md regenerated " + stale + " days ago (" + A.project.regenerated + "). The upkeep-context skill keeps this in sync with HEAD.")}">
        <span class="fresh-dot"></span>${stale}d
      </div>
      <div class="tb-meta">HEAD <b>${esc(A.project.head)}</b></div>
      <div class="tb-viewmode" id="viewMode" data-tip="${escAttr("Switch between the rendered explorer and the raw architecture.md markdown that backs the current view")}">
        <button class="vm-btn active" data-vm="rendered">Rendered</button>
        <button class="vm-btn" data-vm="source">Source</button>
      </div>
      <button class="tb-panel-toggle on" id="toggleRight" title="Toggle context">▥</button>`;
  }

  /* ---------------- left explorer ---------------- */
  function renderExplorer() {
    const kinds = [["all", "All"], ["foundation", "Found."], ["env", "Env"], ["boundary", "Bound."], ["learner", "Learner"], ["observer", "Observer"]];
    const groups = [
      { name: "Overview", items: [
        { lbl: "Scope / Purpose", ico: "◷", tab: "source", anchor: "scope" },
        { lbl: "Repository Overview", ico: "▤", tab: "source", anchor: "overview" },
        { lbl: "Milestones", ico: "◈", tab: "source", anchor: "milestones" },
        { lbl: "Repository Tree", ico: "▸", tab: "source", anchor: "tree" },
      ]},
      { name: "Subsystems", count: A.nodes.length, items: A.nodes.map(n => ({
        lbl: n.label, node: n.id, kind: n.kind, meta: n.root.replace("src/", "").replace(/\/$/, "") || "main", isNode: true,
        risk: (riskByNode[n.id] || []).length, hot: (hotspotByNode[n.id] || 0) >= 60,
      })) },
      { name: "Execution", items: [
        { lbl: "Data Flow (" + (A.dataFlow && A.dataFlow.steps ? A.dataFlow.steps.length : 0) + " steps)", ico: "↯", tab: "flow", anchor: "flow" },
        { lbl: "Failure Semantics", ico: "⚠", tab: "flow", anchor: "failures" },
        { lbl: "Critical Paths", ico: "⌥", tab: "paths", anchor: "paths" },
      ].concat((A.bespoke || []).map(b => ({ lbl: b.title, ico: "✦", tab: "paths", anchor: "bespoke-" + b.id })))},
      { name: "Structure", items: [
        { lbl: "Dependency Layers", ico: "≡", tab: "deps", anchor: "layers" },
        { lbl: "Inter-System Relations", ico: "⇄", tab: "deps", anchor: "relations" },
        { lbl: "Adjacency Matrix", ico: "▦", tab: "deps", anchor: "matrix" },
        { lbl: "State Ownership", ico: "◫", tab: "deps", anchor: "state" },
        { lbl: "Coverage", ico: "▦", tab: "cov", anchor: "cov" },
        { lbl: "Concept Map", ico: "✦", tab: "concept", anchor: "concept" },
        { lbl: "Structural Notes", ico: "✎", tab: "concept", anchor: "notes" },
      ]},
    ];
    const html = `<div class="lr-filter" id="lrFilter">${kinds.map((k, i) => `<button class="lr-chip ${i === 0 ? "active" : ""}" data-kind="${k[0]}">${esc(k[1])}</button>`).join("")}</div>` +
      groups.map(g => `
      <div class="lr-group" data-group="${esc(g.name)}">
        <div class="lr-group-head">
          <span class="chev">▾</span>${esc(g.name)}
          ${g.count != null ? `<span class="gcount">${g.count}</span>` : ""}
        </div>
        <div class="lr-group-items">
          ${g.items.map(it => it.isNode ? `
            <div class="lr-item node-item" data-node="${it.node}" data-kind="${it.kind}" data-search="${esc(it.lbl)} ${esc(it.kind)}" data-tip="${escAttr(nodeById[it.node].tagline)}">
              <span class="swatch" style="background:var(--k-${it.kind})"></span>
              <span class="lbl">${esc(it.lbl)}</span>
              <span class="lr-flags">${it.hot ? '<span class="flag-hot" title="recent change hotspot">●</span>' : ""}${it.risk ? `<span class="flag-risk" title="${it.risk} risk(s)">▲</span>` : ""}</span>
              <span class="meta">${esc(it.meta)}</span>
            </div>` : `
            <div class="lr-item" data-tab="${it.tab}" data-anchor="${it.anchor || ""}" data-search="${esc(it.lbl)}">
              <span class="ico">${it.ico}</span><span class="lbl">${esc(it.lbl)}</span>
            </div>`).join("")}
        </div>
      </div>`).join("");
    $("#leftScroll").innerHTML = html;
    $$(".lr-group-items").forEach(el => el.style.maxHeight = el.scrollHeight + "px");

    $$(".lr-group-head").forEach(h => h.addEventListener("click", () => {
      const g = h.closest(".lr-group"); g.classList.toggle("collapsed");
    }));
    $$(".lr-item").forEach(it => it.addEventListener("click", () => {
      if (it.dataset.node) { switchTab("graph"); graph.select(it.dataset.node); }
      else openAnchor(it.dataset.tab, it.dataset.anchor);
    }));
    $$("#lrFilter .lr-chip").forEach(ch => ch.addEventListener("click", () => {
      $$("#lrFilter .lr-chip").forEach(c => c.classList.toggle("active", c === ch));
      const k = ch.dataset.kind;
      $$(".lr-item.node-item").forEach(it => it.classList.toggle("filtered-out", k !== "all" && it.dataset.kind !== k));
    }));
  }

  function scrollToAnchor(id) {
    const el = document.getElementById("a-" + id);
    if (!el) return;
    const view = el.closest(".ws-view");
    requestAnimationFrame(() => {
      view.scrollTo({ top: el.offsetTop - 20, behavior: "smooth" });
      el.classList.remove("pulse-anchor"); void el.offsetWidth; el.classList.add("pulse-anchor");
      setTimeout(() => el.classList.remove("pulse-anchor"), 1200);
    });
  }
  function openAnchor(tab, anchor) { switchTab(tab); if (anchor) scrollToAnchor(anchor); }

  /* ---------------- workspace tabs ---------------- */
  function renderTabs() {
    $("#wsTabs").innerHTML = TABS.map(t => `
      <button class="ws-tab" data-tab="${t.id}">
        <span class="ti">${t.ti}</span>${esc(t.label)}
        ${t.badge != null ? `<span class="badge">${esc(t.badge)}</span>` : ""}
      </button>`).join("");
    $("#wsBody").innerHTML = TABS.map(t => `<div class="ws-view ${t.id === "graph" ? "graph-view" : ""}" data-view="${t.id}"></div>`).join("") +
      `<div class="ws-view md-view" data-view="__md"><div class="doc"><pre class="md-pre" id="mdPre"></pre></div></div>`;
    $$(".ws-tab").forEach(b => b.addEventListener("click", () => switchTab(b.dataset.tab)));

    renderGraphTab(); renderFlowTab(); renderDepsTab(); renderCovTab();
    renderPathsTab(); renderConceptTab(); renderSourceTab();
  }

  let curTab = "graph", mdMode = false;
  function switchTab(id, opt) {
    if (id !== "__md") curTab = id;
    if (!(opt && opt.keepMd)) mdMode = false;
    $$(".ws-tab").forEach(b => b.classList.toggle("active", b.dataset.tab === curTab));
    const show = mdMode ? "__md" : curTab;
    $$(".ws-view").forEach(v => v.classList.toggle("active", v.dataset.view === show));
    $$("#viewMode .vm-btn").forEach(b => b.classList.toggle("active", (b.dataset.vm === "source") === mdMode));
    if (curTab === "graph" && !mdMode && graph) requestAnimationFrame(() => graph.fit());
    if (window.App.persist) window.App.persist();
  }
  function setMd(on) {
    if (mdMode === on) return;
    mdMode = on;
    if (mdMode) $("#mdPre").textContent = (window.App.markdownFor ? window.App.markdownFor(curTab) : "");
    switchTab(curTab, { keepMd: true });
  }

  /* ---------- GRAPH tab ---------- */
  function renderGraphTab() {
    $('[data-view="graph"]').innerHTML = `
      <div class="ws-toolbar">
        <div class="tool-group">
          <button class="tool-btn active" data-graph-mode="layered"><span class="ti">≡</span>Layered</button>
          <button class="tool-btn" data-graph-mode="force"><span class="ti">✦</span>Force</button>
          <button class="tool-btn" data-graph-mode="radial"><span class="ti">◎</span>Radial</button>
        </div>
        <div class="tool-group">
          <button class="tool-btn" data-impact-toggle id="gImpact" data-tip="${escAttr("Blast-radius mode: select a node to highlight everything that transitively depends on it.")}"><span class="ti">⊛</span>Impact</button>
          <button class="tool-btn" id="gPlay"><span class="ti">▶</span><span id="playLabel">Play tick</span></button>
        </div>
        <div class="tool-group">
          <button class="tool-btn" id="gFit"><span class="ti">⤢</span>Fit</button>
          <button class="tool-btn" id="gArrange" data-tip="${escAttr("Reset layout + cleared dragged positions")}"><span class="ti">↻</span>Reset</button>
        </div>
        <div class="tool-group">
          <button class="tool-btn" id="gZoomOut">−</button>
          <span class="zoom-val" id="zoomVal">100%</span>
          <button class="tool-btn" id="gZoomIn">+</button>
        </div>
        <div class="ws-hint" id="graphHint">drag to pan · scroll to zoom · click node to inspect</div>
      </div>
      <div class="graph-stage" id="graphStage"></div>
      ${graphLegend()}
      <div class="graph-minimap"><svg id="minimapSvg"></svg></div>`;
  }
  function graphLegend() {
    const kinds = Object.entries(A.kindMeta);
    return `<div class="graph-legend collapsed" id="graphLegend">
      <div class="gl-head"><span class="gl-title">Legend</span><span class="gl-toggle">show ▴</span></div>
      <div class="gl-body">
        <div><div class="gl-col-title">Subsystem role</div>
          ${kinds.map(([k, m]) => `<div class="gl-row"><span class="sw" style="background:var(--k-${k})"></span>${esc(m.label)}</div>`).join("")}</div>
        <div><div class="gl-col-title">Edge type</div>
          <div class="gl-row"><span class="ln" style="border-top:2px solid rgba(255,255,255,0.3)"></span>dependency</div>
          <div class="gl-row"><span class="ln" style="border-top:2px solid var(--cyan)"></span>load-bearing</div>
          <div class="gl-row"><span class="ln" style="border-top:2px dashed var(--violet)"></span>write-back</div>
          <div class="gl-row"><span class="ln" style="border-top:2px dashed var(--amber)"></span>hidden coupling</div></div>
      </div></div>`;
  }

  /* ---------- FLOW tab ---------- */
  function tagStyle(sys) {
    const n = nodeById[sys.split("::")[0]];
    const k = n ? n.kind : "observer";
    return `color:var(--k-${k});background:color-mix(in srgb, var(--k-${k}) 14%, transparent)`;
  }
  let flowView = "timeline";
  function curFlowSteps() { return (A.dataFlow && A.dataFlow.steps) || []; }
  function renderFlowTab() {
    const stepCount = curFlowSteps().length;
    $('[data-view="flow"]').innerHTML = `<div class="doc">
      <div class="doc-head" id="a-flow"><div class="doc-title">Core Execution / Data Flow</div>
      <div class="doc-sub">One traced operation · ${stepCount} step${stepCount === 1 ? "" : "s"} across the subsystem boundaries · click a step to flash it, hover a state to trace it</div></div>
      <div class="flow-controls">
        <div class="seg" id="flowView">
          <button class="seg-btn active" data-fv="timeline">Timeline</button>
          <button class="seg-btn" data-fv="swimlane">Swimlanes</button>
        </div>
        <button class="tool-btn solo" id="flowPlay" data-tip="${escAttr("Animate the traced operation across the topology graph")}"><span class="ti">▶</span>Play on graph</button>
      </div>
      <p class="doc-intro" id="flowIntro"></p>
      <div id="flowBody"></div>
      ${failuresHtml()}
    </div>`;
    paintFlowBody();
    $$("#flowView .seg-btn").forEach(b => b.addEventListener("click", () => { flowView = b.dataset.fv; $$("#flowView .seg-btn").forEach(x => x.classList.toggle("active", x === b)); paintFlowBody(); }));
    $("#flowPlay").addEventListener("click", () => playTick());
    wireFailures();
  }
  function paintFlowBody() {
    const f = A.dataFlow, steps = curFlowSteps();
    $("#flowIntro").innerHTML = esc((f && f.intro) || "");
    $("#flowBody").innerHTML = flowView === "swimlane" ? swimlaneHtml(steps) : timelineHtml(steps);
    $$('#flowBody .flow-step').forEach(st => st.addEventListener("click", () => flashStep(+st.dataset.step)));
  }
  function timelineHtml(steps) {
    return A.dataFlow.simsets.map(set => {
      const ss = steps.filter(s => s.set === set);
      if (!ss.length) return "";
      return `<div class="flow-set"><div class="flow-set-head"><span class="flow-set-name">${esc(set)}</span><span class="flow-set-line"></span></div>
        <div class="flow-steps">${ss.map(stepCard).join("")}</div></div>`;
    }).join("");
  }
  function stepCard(s) {
    return `<div class="flow-step ${s.fail ? "fail" : ""}" data-step="${s.n}" data-node="${s.sys.split("::")[0]}">
      <div class="fs-num">${String(s.n).padStart(2, "0")}</div>
      <div class="fs-main">
        <div class="fs-sys"><span class="fs-tag" style="${tagStyle(s.sys)}">${esc(s.sys)}</span><span class="fs-fn">${esc(s.fn)}</span></div>
        <div class="fs-io"><span class="io-k">reads</span><span class="io-v">${linkifyStates(s.reads)}</span>
          <span class="io-k">writes</span><span class="io-v write">${linkifyStates(s.writes)}</span></div>
      </div></div>`;
  }
  function swimlaneHtml(steps) {
    const cols = [];
    steps.forEach(s => { const c = s.sys.split("::")[0]; if (!cols.includes(c)) cols.push(c); });
    // Order by node-order in A.nodes (which respects the data-declared layer
    // sequence); anything not in A.nodes goes after, preserving first-seen
    // order from the steps. Project-agnostic.
    const nodeOrder = {};
    (A.nodes || []).forEach((n, i) => { nodeOrder[n.id] = i; });
    cols.sort((a, b) => (nodeOrder[a] != null ? nodeOrder[a] : 1e6) - (nodeOrder[b] != null ? nodeOrder[b] : 1e6));
    let h = `<div class="swim" style="grid-template-columns:34px repeat(${cols.length},minmax(120px,1fr))">`;
    h += `<div class="swim-corner"></div>` + cols.map(c => `<div class="swim-head" style="${tagStyle(c)}">${esc(c)}</div>`).join("");
    steps.forEach(s => {
      const owner = s.sys.split("::")[0];
      h += `<div class="swim-num ${s.fail ? "fail" : ""}">${String(s.n).padStart(2, "0")}</div>`;
      cols.forEach(c => {
        if (c === owner) h += `<div class="swim-cell flow-step ${s.fail ? "fail" : ""}" data-step="${s.n}" data-node="${owner}">
          <div class="swim-fn">${esc(s.fn)}</div><div class="swim-io">${linkifyStates(s.writes)}</div></div>`;
        else h += `<div class="swim-empty"></div>`;
      });
    });
    h += `</div>`;
    return h;
  }
  function failuresHtml() {
    return `<div class="card" id="a-failures" style="margin-top:14px">
      <div class="section-eyebrow">Failure semantics along the chain · ${A.failures.length} invariants · hover to highlight the bound steps</div>
      <div style="display:grid;gap:9px">
        ${A.failures.map((fl, i) => {
          const linked = String(fl.link).match(/\d+/g) || [fl.step];
          return `<div class="fail-inv" data-steps="${linked.join(",")}">
            <div class="fs-num" style="min-width:58px;color:var(--amber)">${esc(fl.link)}</div>
            <div class="fs-main"><div class="fs-fn" style="color:var(--tx);margin-bottom:3px">${esc(fl.title)}</div>
            <div style="font-size:11.5px;line-height:1.6;color:var(--tx-3)">${linkifyStates(fl.body)}</div></div></div>`;
        }).join("")}
      </div></div>`;
  }
  function wireFailures() {
    $$('#a-failures .fail-inv').forEach(fi => {
      const steps = fi.dataset.steps.split(",");
      fi.addEventListener("mouseenter", () => steps.forEach(n => $$(`#flowBody .flow-step[data-step="${n}"]`).forEach(s => s.classList.add("step-lit"))));
      fi.addEventListener("mouseleave", () => $$('#flowBody .flow-step.step-lit').forEach(s => s.classList.remove("step-lit")));
      fi.addEventListener("click", () => { flashStep(+steps[0]); });
    });
  }
  function flashStep(n) {
    $$(`#flowBody .flow-step[data-step="${n}"], #flowBody .swim-cell[data-step="${n}"]`).forEach(s => { s.classList.remove("flash"); void s.offsetWidth; s.classList.add("flash"); });
  }
  function playTick() {
    switchTab("graph");
    const steps = (A.dataFlow && A.dataFlow.steps) || [];
    const seq = steps.map(s => s.sys.split("::")[0]).filter((id, i, a) => id !== a[i - 1]);
    graph.flowNodes(seq, { interval: 560 });
  }

  /* ---------- DEPENDENCIES tab ---------- */
  function renderDepsTab() {
    const layers = `<div id="a-layers"><div class="section-eyebrow">Dependency direction · layered · downward-only</div>
      <div class="layer-stack">${A.layers.map((l, i) => `${i ? '<div class="layer-arrow">▼</div>' : ''}
        <div class="layer-box" data-layer="${esc(l.name)}"><div class="layer-name">${esc(l.name)}</div><div class="layer-role">${esc(l.role)}</div></div>`).join("")}</div>
      <p style="margin-top:14px;font-size:11.5px;line-height:1.6;color:var(--tx-3);max-width:680px">${linkifyStates(A.layersNote)}</p></div>`;

    const rel = `<div id="a-relations" style="margin-top:34px">
      <div class="section-eyebrow">Inter-system relationships · ${A.relationships.length} edges · click a row to trace it on the graph</div>
      <div class="rel-tools"><div class="tb-search mini"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="11" cy="11" r="7"/><path d="m21 21-4.3-4.3"/></svg><input id="relSearch" placeholder="filter relationships…"/></div></div>
      <div class="card" style="padding:6px 14px;overflow-x:auto">
        <table class="rel-table" id="relTable"><thead><tr><th>A</th><th>B</th><th>Mechanism</th><th>Data</th><th>Breaks if violated</th></tr></thead>
        <tbody>${A.relationships.map(r => `<tr data-a="${esc(normNode(r.a) || "")}" data-b="${esc(normNode(r.b) || "")}" data-hay="${escAttr((r.a + " " + r.b + " " + r.mech + " " + r.data + " " + r.breaks).toLowerCase())}">
          <td class="side">${esc(r.a)}</td><td class="side">${esc(r.b)}</td>
          <td>${linkifyStates(r.mech)}</td><td>${esc(r.data)}</td><td class="breaks">${esc(r.breaks)}</td></tr>`).join("")}</tbody></table>
      </div></div>`;

    const state = `<div id="a-state" style="margin-top:34px"><div class="section-eyebrow">State ownership · who owns what; who else reads it</div>
      <div class="grid-2">${A.stateOwnership.map(s => `<div class="kv-card"><div class="owner" data-node="${esc(normNode(s.owner) || "")}">${esc(s.owner)}</div><div class="items">${linkifyStates(s.items)}</div></div>`).join("")}</div></div>`;

    $('[data-view="deps"]').innerHTML = `<div class="doc">
      <div class="doc-head"><div class="doc-title">Dependencies & Structure</div>
      <div class="doc-sub">Layered direction, the full edge ledger, the adjacency matrix and state-ownership boundaries</div></div>
      ${layers}<div id="a-matrix" style="margin-top:34px">${matrixHtml()}</div>${rel}${state}</div>`;

    wireMatrix(); wireRelTable();
    $$('#a-state .owner[data-node]').forEach(o => { if (o.dataset.node) o.addEventListener("click", () => { switchTab("graph"); graph.select(o.dataset.node); }); });
    $$('#a-layers .layer-box').forEach(b => b.addEventListener("click", () => {
      const ids = b.dataset.layer.split("·").map(s => s.trim());
      switchTab("graph"); if (graph.hasNode(ids[0])) graph.select(ids[0]);
    }));
  }
  function matrixHtml() {
    const ids = A.nodes.map(n => n.id);
    const adj = {}; ids.forEach(a => adj[a] = {});
    A.edges.forEach(e => { adj[e.from][e.to] = e.rel; });
    return `<div class="section-eyebrow">Adjacency matrix · row → column means “row feeds / is depended-on-by column”</div>
      <div class="card matrix-scroll" style="padding:14px"><table class="matrix" id="adjMatrix"><thead><tr><th></th>
      ${ids.map((c, i) => `<th data-col="${i}">${esc(c)}</th>`).join("")}</tr></thead><tbody>
      ${ids.map((r, ri) => `<tr data-row="${ri}"><th class="rowh">${esc(r)}</th>${ids.map((c, ci) => {
        if (r === c) return `<td class="diag" data-row="${ri}" data-col="${ci}">·</td>`;
        const rel = adj[r][c];
        if (!rel) return `<td data-row="${ri}" data-col="${ci}"></td>`;
        const cls = rel === "strong" ? "strong" : (rel === "write" ? "wr" : rel === "peer" ? "pe" : "on");
        const ch = rel === "peer" ? "↔" : "→";
        return `<td class="${cls}" data-row="${ri}" data-col="${ci}" data-from="${r}" data-to="${c}" data-tip="${escAttr(r + " → " + c + ": " + (A.edges.find(e => e.from === r && e.to === c) || {}).label)}">${ch}</td>`;
      }).join("")}</tr>`).join("")}
      </tbody></table></div>`;
  }
  function wireMatrix() {
    const tbl = $("#adjMatrix"); if (!tbl) return;
    tbl.querySelectorAll("td[data-from]").forEach(td => {
      td.addEventListener("mouseenter", () => {
        tbl.querySelectorAll(`[data-row="${td.dataset.row}"]`).forEach(x => x.classList.add("mx-hl"));
        tbl.querySelectorAll(`[data-col="${td.dataset.col}"]`).forEach(x => x.classList.add("mx-hl"));
      });
      td.addEventListener("mouseleave", () => tbl.querySelectorAll(".mx-hl").forEach(x => x.classList.remove("mx-hl")));
      td.addEventListener("click", () => { switchTab("graph"); graph.selectEdge(td.dataset.from, td.dataset.to); });
    });
  }
  function wireRelTable() {
    const inp = $("#relSearch");
    if (inp) inp.addEventListener("input", () => {
      const q = inp.value.trim().toLowerCase();
      $$('#relTable tbody tr').forEach(tr => tr.style.display = (!q || tr.dataset.hay.includes(q)) ? "" : "none");
    });
    $$('#relTable tbody tr').forEach(tr => tr.addEventListener("click", () => {
      const a = tr.dataset.a, b = tr.dataset.b;
      if (a && b && graph.hasNode(a) && graph.hasNode(b)) { switchTab("graph"); graph.selectEdge(a, b); }
      else if (a) { switchTab("graph"); graph.select(a); }
    }));
  }

  /* ---------- COVERAGE tab ----------
     LENS provides a one-line description per coverage column. Projects override
     this via A.coverage.lenses = { <col-id>: "description", ... } when they
     want richer column hover text. Without that, the column id itself is used
     as the lens label - project-agnostic by default. */
  const LENS = (A.coverage && A.coverage.lenses) || {};
  const LVL = { 3: "Full inspection this pass", 2: "Partial inspection", 1: "Trusted from a prior pass" };
  const LVLSHORT = { 3: "full", 2: "partial", 1: "trusted" };
  function renderCovTab() {
    const c = A.coverage;
    let depthSum = 0, depthN = 0;
    const lensCount = {}; c.cols.forEach(l => lensCount[l] = 0);
    const rows = c.rows.map(row => {
      const lenses = c.cols.filter(co => row.cells[co]).sort((a, b) => row.cells[b] - row.cells[a]);
      const max = lenses.length ? Math.max(...lenses.map(l => row.cells[l])) : 0;
      if (max) { depthSum += max; depthN++; }
      lenses.forEach(l => lensCount[l]++);
      const chips = lenses.length ? lenses.map(l => {
        const v = row.cells[l], pv = (row.prev && row.prev[l]) || 0;
        const delta = v - pv;
        const arr = pv === 0 && v > 0 ? '<span class="cov-delta new">NEW</span>' : (delta > 0 ? `<span class="cov-delta up">▲${delta}</span>` : "");
        return `<span class="cov-chip lv${v}" data-tip="${escAttr(LENS[l] + " — " + LVL[v] + " (" + v + ")" + (pv ? " · prior pass: " + pv : " · not inspected last pass"))}">${esc(l)}<span class="lvl">${LVLSHORT[v]}</span>${arr}</span>`;
      }).join("") : `<span class="cov-empty">no inspection logged — trusted from structure</span>`;
      return `<div class="cov-row2 ${max ? "" : "uncovered"}" data-node="${esc(row.node || "")}">
        <div class="cov-mod">${esc(row.label)}</div>
        <div class="cov-depth"><div class="cov-depth-track"><div class="cov-depth-fill" style="width:${(max / 3) * 100}%"></div></div><span class="cov-depth-label">${max ? LVLSHORT[max] : "—"}</span></div>
        <div class="cov-chips">${chips}</div>
      </div>`;
    }).join("");
    const score = depthN ? (depthSum / depthN) : 0;
    const summary = `<div class="cov-summary">
      <div class="cov-score"><div class="cov-score-val">${score.toFixed(1)}<span>/3</span></div><div class="cov-score-lbl">mean depth across ${depthN} inspected modules</div></div>
      <div class="cov-lenscol">${c.cols.map(l => `<div class="cov-lenscell" data-tip="${escAttr(LENS[l])}"><span class="cov-lenscount">${lensCount[l]}</span><span class="cov-lensname">${esc(l)}</span></div>`).join("")}</div>
    </div>`;

    $('[data-view="cov"]').innerHTML = `<div class="doc">
      <div class="doc-head"><div class="doc-title">Coverage</div>
      <div class="doc-sub">Which inspection lenses examined each source module, how deeply, and what changed since the prior pass · ${esc(A.project.regenerated)}</div></div>
      <p class="cov-intro">Each pass walks the code through a set of <strong>lenses</strong>. A module's <strong>depth</strong> is the deepest lens applied to it this pass; <span style="color:var(--cyan)">▲ / NEW</span> marks modules freshly re-read since last time. Click a module to open its node.</p>
      ${summary}
      <div class="cov-scale"><span class="cov-scale-label">Depth</span><div class="cov-scale-group">
        <span class="cov-scale-item"><span class="cov-swatch lv3"></span>Full (3)</span>
        <span class="cov-scale-item"><span class="cov-swatch lv2"></span>Partial (2)</span>
        <span class="cov-scale-item"><span class="cov-swatch lv1"></span>Trusted (1)</span></div></div>
      <div class="cov-lens-key">${c.cols.map(co => `<span class="cov-lens-pill" data-tip="${escAttr(LENS[co])}"><b>${esc(co)}</b></span>`).join("")}</div>
      <div class="cov-rows" id="a-cov">${rows}</div>
      <p style="margin-top:18px;font-size:11.5px;line-height:1.6;color:var(--tx-3);max-width:740px">${esc(c.note)}</p></div>`;
    $$('#a-cov .cov-row2[data-node]').forEach(r => { if (r.dataset.node) r.addEventListener("click", () => { switchTab("graph"); graph.select(r.dataset.node); }); });
  }

  /* ---------- PATHS tab ---------- */
  function renderPathsTab() {
    const paths = `<div id="a-paths"><div class="section-eyebrow">Critical paths and blast radius · ${A.criticalPaths.length} chains · ▶ traces the chain across the graph</div>
      ${A.criticalPaths.map((p, i) => `<div class="path-card">
        <div class="path-head"><span class="path-name">${esc(p.name)}</span>
          <span style="display:flex;align-items:center;gap:10px"><span class="path-len">${esc(p.len)}</span>
          <button class="tool-btn solo path-play" data-path="${i}"><span class="ti">▶</span>Trace</button></span></div>
        <div class="path-flow">${p.steps.map((s, j) => `${j ? '<span class="path-arrow">→</span>' : ''}<span class="path-step">${esc(s)}</span>`).join("")}</div>
        <p class="path-blast">${linkifyStates(p.blast)}</p></div>`).join("")}</div>`;

    // Generic bespoke[] renderer. Each item in A.bespoke is a project-specific
    // deep-dive widget with shape: {id, title, subtitle, steps[], panels[], ctx?}.
    // Project-agnostic: the rendering reads from data only; no hardcoded
    // pipeline names, no hardcoded category-to-panel maps. Projects that need
    // a category-to-panel spotlight declare it via bespoke[].catPanel.
    const bespokeBlocks = (A.bespoke || []).map(b => `
      <div id="a-bespoke-${esc(b.id)}" style="margin-top:36px">${bespokeExplorer(b)}</div>
    `).join("");
    $('[data-view="paths"]').innerHTML = `<div class="doc">
      <div class="doc-head"><div class="doc-title">Critical Paths</div>
      <div class="doc-sub">End-to-end chains worth tracing${(A.bespoke && A.bespoke.length) ? " plus bespoke explorers" : ""}</div></div>
      ${paths}${bespokeBlocks}</div>`;
    (A.bespoke || []).forEach(b => wireBespokeExplorer(b));
    $$('.path-play').forEach(b => b.addEventListener("click", () => {
      const p = A.criticalPaths[+b.dataset.path];
      switchTab("graph"); graph.flowNodes(pathNodeSeq(p.steps), { interval: 600 });
    }));
  }
  function bespokeExplorer(b) {
    const stepsHtml = (b.steps || []).map(s => `<div class="tick-step" data-cat="${esc(s.cat || "")}" data-sys="${esc(s.sys || "")}" data-id="${esc(s.id)}">
      <div class="tick-num">${esc(s.id)}</div><div class="tick-body"><span class="sys">${esc(s.sys || "")}</span> ${s.sys ? "·" : ""} ${linkifyStates(s.body || "")}</div></div>`).join("");
    const panelsHtml = (b.panels || []).map(p => `<div class="tick-panel" data-panel="${esc(p.title)}">
      <div class="tick-ptitle">${esc(p.title)}</div>
      ${p.chart ? `<div class="mini-chart">${p.chart.map(h => `<div class="mini-bar" style="height:${h}%;background:${esc(p.chartColor || "var(--cyan)")}"></div>`).join("")}</div>` : ""}
      ${(p.rows || []).map(r => `<div class="tick-prow"><span class="label">${esc(r[0])}</span><span class="val">${esc(r[1])}</span></div>`).join("")}</div>`).join("");
    return `<div class="section-eyebrow" style="color:var(--violet)">${esc(b.title)} · bespoke widget</div>
      <div class="bespoke-banner">${esc(b.subtitle || "")}</div>
      <div class="tick-wrap" data-tick="${esc(b.id)}">
        <div class="tick-flow">${stepsHtml}</div>
        <div class="tick-side">${panelsHtml}</div>
      </div>`;
  }
  function wireBespokeExplorer(b) {
    const wrap = document.querySelector(`.tick-wrap[data-tick="${b.id}"]`);
    if (!wrap) return;
    const side = wrap.querySelector(".tick-side");
    const catPanel = b.catPanel || {};
    function clearDetail() {
      wrap.querySelectorAll(".tick-step").forEach(s => s.classList.remove("active"));
      wrap.querySelectorAll(".tick-panel").forEach(p => p.classList.remove("panel-lit"));
      const d = side.querySelector(".tick-detail"); if (d) d.remove();
    }
    wrap.querySelectorAll(".tick-step").forEach(st => st.addEventListener("click", () => {
      const wasActive = st.classList.contains("active");
      clearDetail();
      if (wasActive) return;
      st.classList.add("active");
      const step = (b.steps || []).find(s => s.id === st.dataset.id) || {};
      const pt = catPanel[st.dataset.cat];
      if (pt) { const panel = [...wrap.querySelectorAll(".tick-panel")].find(p => p.dataset.panel === pt); if (panel) panel.classList.add("panel-lit"); }
      const d = document.createElement("div"); d.className = "tick-detail"; side.insertBefore(d, side.firstChild);
      d.innerHTML = `<div class="tdh">Step ${esc(st.dataset.id)}${st.dataset.sys ? ` · <span class="sys">${esc(st.dataset.sys)}</span>` : ""}</div>
        <div class="td-body">${esc(step.body || "")}</div>
        ${(b.ctx && b.ctx[st.dataset.cat]) ? `<div class="td-ctx">${esc(b.ctx[st.dataset.cat])}${pt ? ` <span class="td-see">-> see ${esc(pt)}</span>` : ""}</div>` : ""}`;
    }));
  }

  /* ---------- CONCEPT tab (interactive mini-graph) ---------- */
  function renderConceptTab() {
    const c = A.concept;
    $('[data-view="concept"]').innerHTML = `<div class="doc">
      <div class="doc-head"><div class="doc-title">Concept & Reality</div>
      <div class="doc-sub">The domain knowledge map (distinct from the dependency graph) and what is actually true at HEAD ${esc(A.project.head)}</div></div>
      <div id="a-concept"><div class="section-eyebrow">Concept map · click a branch to open its subsystem · hover a leaf for its glossary definition</div>
        <div class="concept-stage"><svg id="conceptSvg"></svg></div></div>
      <div id="a-notes" style="margin-top:30px"><div class="section-eyebrow">Structural notes / current reality · ${A.notes.length} notes</div>
        <div class="grid-2">${A.notes.map(n => `<div class="note-card ${n.sev}">
          <div class="note-head"><span class="note-tag ${n.sev}">${esc(n.tag)}</span><span class="note-title">${esc(n.title)}</span></div>
          <div class="note-body">${linkifyStates(n.body)}</div></div>`).join("")}</div></div></div>`;
    renderConceptGraph();
  }
  const GLOSS = {}; A.glossary.forEach(g => GLOSS[g.term.toLowerCase()] = g.def);
  function leafDef(text) {
    for (const k in GLOSS) if (text.toLowerCase().includes(k)) return GLOSS[k];
    return text;
  }
  function renderConceptGraph() {
    const svg = $("#conceptSvg"), W = 860, H = 380;
    svg.setAttribute("viewBox", `0 0 ${W} ${H}`);
    const cx = W / 2, cy = 64;
    // branch.kind is the optional A.kindMeta key (foundation/learner/etc.)
    // that ties the concept branch to a subsystem role - its colour comes from
    // the kindMeta swatch CSS variable. branch.node is the explicit node id
    // the branch should highlight on click (optional). Project-agnostic
    // defaults: spread branches evenly across the canvas; colour by kind.
    const palette = ["var(--cyan)", "var(--violet)", "var(--amber)", "var(--teal)", "var(--sage)"];
    const branches = A.concept.branches.map((b, bi, arr) => ({
      b,
      x: W * ((bi + 1) / (arr.length + 1)),
      y: 180,
      node: b.node || null,
      col: b.kind ? `var(--k-${b.kind})` : palette[bi % palette.length],
    }));
    let s = "";
    // links root→branch
    branches.forEach(br => { s += `<path d="M ${cx} ${cy + 16} C ${cx} ${cy + 70}, ${br.x} ${br.y - 70}, ${br.x} ${br.y - 18}" fill="none" stroke="${br.col}" stroke-width="1.5" opacity="0.5"/>`; });
    // leaves
    branches.forEach(br => {
      const leaves = br.b.leaves.concat(br.b.trunks);
      leaves.forEach((lf, i) => {
        const n = leaves.length, spread = 150, lx = br.x - spread + (i / Math.max(1, n - 1)) * spread * 2, ly = br.y + 78 + (i % 2) * 40;
        s += `<line x1="${br.x}" y1="${br.y + 18}" x2="${lx}" y2="${ly - 12}" stroke="${br.col}" stroke-width="1" opacity="0.3"/>`;
        s += `<g class="cg-leaf" data-tip="${escAttr(leafDef(lf))}"><rect x="${lx - 70}" y="${ly - 12}" width="140" height="24" rx="6" fill="#11131b" stroke="${br.col}" stroke-opacity="0.35"/>
          <text x="${lx}" y="${ly}" text-anchor="middle" dominant-baseline="central" font-size="8.5" fill="#a6adbf" font-family="var(--mono)">${esc(lf.length > 26 ? lf.slice(0, 25) + "…" : lf)}</text></g>`;
      });
    });
    // branch nodes
    branches.forEach(br => {
      s += `<g class="cg-branch" data-node="${br.node}" style="cursor:pointer"><rect x="${br.x - 92}" y="${br.y - 18}" width="184" height="36" rx="9" fill="#11131b" stroke="${br.col}" stroke-width="1.6"/>
        <text x="${br.x}" y="${br.y}" text-anchor="middle" dominant-baseline="central" font-size="12" fill="${br.col}" font-family="var(--mono)" font-weight="600">${esc(br.b.head)}</text></g>`;
    });
    // root
    s += `<g><rect x="${cx - 70}" y="${cy - 16}" width="140" height="32" rx="16" fill="var(--violet-dim)" stroke="var(--violet)" stroke-width="1.4"/>
      <text x="${cx}" y="${cy}" text-anchor="middle" dominant-baseline="central" font-size="12" fill="#e7eaf2" font-family="var(--mono)" font-weight="600">${esc(A.concept.root)}</text></g>`;
    svg.innerHTML = s;
    svg.querySelectorAll(".cg-branch").forEach(g => g.addEventListener("click", () => { switchTab("graph"); graph.select(g.dataset.node); }));
  }

  /* ---------- SOURCE tab ---------- */
  function renderSourceTab() {
    const p = A.project;
    const scope = `<div id="a-scope"><div class="section-eyebrow">Scope / purpose</div>
      <p class="doc-intro" style="margin-bottom:14px">${linkifyStates(p.tagline)}</p>
      <div class="card" style="border-left:2px solid var(--cyan)"><div style="font-size:12px;line-height:1.65;color:var(--tx-2)">${esc(p.purpose)}</div></div></div>`;
    const overview = `<div id="a-overview" style="margin-top:34px"><div class="section-eyebrow">Repository overview · runtime composition</div>
      <p style="font-size:12.5px;line-height:1.7;color:var(--tx-2);max-width:780px">A multi-controller, multi-car vectorised trainer. <code style="color:var(--cyan)">TrainerConfig.layout</code> selects fleet composition via the <code style="color:var(--cyan)">TrainerLayout</code> enum. Throttle axis is [0, 1] (coast→full thrust; drag is the sole deceleration). Observations are 43-dimensional; reward is velocity projection onto centreline tangent plus centreline proximity. Episodes end on crash or 30-second timeout — no finish line or lap concept.</p>
      <div class="chip-row" style="margin-top:14px">${p.techStack.map(t => `<div class="dep-chip" style="cursor:default"><b style="color:var(--tx);font-family:var(--mono)">${esc(t.name)}</b><span style="color:var(--tx-4)">${esc(t.meta)}</span></div>`).join("")}</div></div>`;
    const milestones = `<div id="a-milestones" style="margin-top:36px"><div class="section-eyebrow">Roadmap · M1 → M7</div>
      <div class="timeline">${A.milestones.map(m => `<div class="ms ${m.status}" data-tip="${escAttr(m.note)}">
        <div class="ms-dot"></div><div class="ms-id">${esc(m.id)}</div><div class="ms-title">${esc(m.title)}</div></div>`).join("")}</div></div>`;
    const tree = `<div id="a-tree" style="margin-top:36px"><div class="section-eyebrow">Repository structure · click a folder to expand, a module to open its node</div>
      <div class="card"><div class="tree" id="repoTree"></div></div></div>`;
    $('[data-view="source"]').innerHTML = `<div class="doc">
      <div class="doc-head"><div class="doc-title">${esc(p.name)}</div>
      <div class="doc-sub">${esc(p.stack)} · ${esc(p.tests)} · regenerated ${esc(p.regenerated)}</div></div>
      ${scope}${overview}${milestones}${tree}</div>`;
    renderTree();
  }
  // TREENODE maps repoTree folder names to subsystem node ids when the folder
  // name does not match a node id directly. Projects may declare these via
  // node.{node: "<id>"} entries on each repoTree.children entry, which is the
  // preferred (data-driven) approach. This map is the fallback for projects
  // that did not annotate.
  const TREENODE = (A.repoTree && A.repoTree.folderToNode) || {};
  function renderTree() {
    const host = $("#repoTree");
    function nodeHtml(n) {
      const isDir = !n.file, kids = n.children || [], open = n.open;
      const sysId = n.node || TREENODE[n.name];
      const swatch = sysId ? `<span class="tree-ico dir" style="color:var(--k-${(nodeById[sysId] || {}).kind || "observer"})">▣</span>` :
        `<span class="tree-ico ${isDir ? "dir" : "file"}">${isDir ? "▣" : "·"}</span>`;
      const tog = (isDir && kids.length) ? `<span class="tree-tog">${open ? "▾" : "▸"}</span>` : `<span class="tree-tog">·</span>`;
      let h = `<div class="tree-node" ${sysId ? `data-sys="${sysId}"` : ""}>${tog}${swatch}<span class="tree-name ${n.file ? "file" : ""}">${esc(n.name)}</span>${n.anno ? `<span class="tree-anno">${esc(n.anno)}</span>` : ""}</div>`;
      if (kids.length) h += `<div class="tree-kids" ${open ? "" : 'style="display:none"'}>${kids.map(nodeHtml).join("")}</div>`;
      return h;
    }
    host.innerHTML = nodeHtml(A.repoTree);
    host.querySelectorAll(".tree-node").forEach(tn => tn.addEventListener("click", e => {
      e.stopPropagation();
      const kids = tn.nextElementSibling;
      if (kids && kids.classList.contains("tree-kids")) {
        const hidden = kids.style.display === "none"; kids.style.display = hidden ? "" : "none";
        const tog = tn.querySelector(".tree-tog"); if (tog && tog.textContent !== "·") tog.textContent = hidden ? "▾" : "▸";
      } else if (tn.dataset.sys) { switchTab("graph"); graph.select(tn.dataset.sys); }
    }));
  }

  /* ---------------- right rail ---------------- */
  function renderProjectRail() {
    const p = A.project;
    $("#rrScroll").innerHTML = `
      <div class="rr-section">
        <div class="rr-shead"><span class="rr-stitle">Project</span><span class="rr-scount">${esc(p.head)}</span></div>
        <p class="rr-tagline"><strong>${esc(p.name)}</strong> — ${esc(p.tagline.split("—")[0])}</p>
      </div>
      <div class="rr-section">
        <div class="rr-shead"><span class="rr-stitle">Vitals</span></div>
        ${[["Milestone", p.milestone, "cyan", "source", "milestones"], ["Tests", p.tests, "sage", "cov", "cov"], ["Frame budget", p.frameBudget + " used", "violet", "", "profiling"], ["Commits", p.commits + " to master", "", "", ""], ["Architecture", p.lines + " md lines", "amber", "", ""], ["Last commit", p.head + " · " + p.regenerated, "", "", ""]]
          .map(([k, v, c, tab, t2]) => `<div class="vital-row ${tab || t2 ? "vital-link" : ""}" ${tab ? `data-tab="${tab}" data-anchor="${t2}"` : (t2 ? `data-node="${t2}"` : "")}><span class="vital-k">${esc(k)}</span><span class="vital-v ${c}">${esc(v)}</span></div>`).join("")}
      </div>
      <div class="rr-section">
        <div class="rr-shead"><span class="rr-stitle">Risk Register</span><span class="rr-scount">${A.risks.length}</span></div>
        ${A.risks.map(riskHtml).join("")}
      </div>
      <div class="rr-section">
        <div class="rr-shead"><span class="rr-stitle">Change Frontier</span><span class="rr-scount">30d</span></div>
        ${A.changeFrontier.map(c => `<div class="cf-item" data-node="${c.node}"><span class="cf-name">${esc(c.name)}</span>
          <span class="cf-bars">${c.bars.map(b => `<span class="cf-bar" style="height:${Math.max(4, b)}%"></span>`).join("")}</span></div>`).join("")}
      </div>
      <div class="rr-section">
        <div class="rr-shead"><span class="rr-stitle">Decisions</span><span class="rr-scount">${A.decisions.length}</span></div>
        ${A.decisions.map(d => `<div class="decision" data-node="${d.node}"><span class="dm">◆</span><div>
          <div class="decision-title">${esc(d.title)}</div><div class="decision-why">${esc(d.why)}</div></div></div>`).join("")}
      </div>
      <div class="rr-section">
        <div class="rr-shead"><span class="rr-stitle">Glossary</span><span class="rr-scount">${A.glossary.length}</span></div>
        <div class="gloss-wrap">${A.glossary.map(g => `<span class="gloss-term" data-tip="${escAttr(g.def)}">${esc(g.term)}</span>`).join("")}</div>
      </div>
      <div class="rr-section">
        <div class="rr-shead"><span class="rr-stitle">Active Alerts</span><span class="rr-scount">${A.alerts.length}</span></div>
        ${A.alerts.map(a => `<div class="alert"><span class="alert-dot ${a.sev}"></span><div class="alert-text">${esc(a.text)}<div class="alert-meta">${esc(a.meta)}</div></div></div>`).join("")}
      </div>`;
    wireRailLinks();
  }
  function riskHtml(r) {
    return `<div class="risk-item"><div class="risk-head"><span class="risk-sev ${r.sev}">${r.sev.toUpperCase()}</span>
      <span class="risk-title">${esc(r.title)}</span><span class="risk-node" data-node="${r.node}">${esc(r.node)}</span></div>
      <div class="risk-trigger">${linkifyStates(r.trigger)}</div></div>`;
  }
  function wireRailLinks() {
    $$("#rrScroll [data-node]").forEach(el => el.addEventListener("click", e => {
      if (e.target.closest(".vital-link") && el.dataset.tab) return;
      switchTab("graph"); graph.select(el.dataset.node);
    }));
    $$("#rrScroll .vital-link").forEach(el => el.addEventListener("click", () => {
      if (el.dataset.tab) openAnchor(el.dataset.tab, el.dataset.anchor);
      else if (el.dataset.node) { switchTab("graph"); graph.select(el.dataset.node); }
    }));
  }

  function renderNodeRail(id) {
    const n = nodeById[id];
    if (!n) return renderProjectRail();
    const outs = A.edges.filter(e => e.from === id).map(e => ({ id: e.to, rel: e.rel, label: e.label }));
    const ins = A.edges.filter(e => e.to === id).map(e => ({ id: e.from, rel: e.rel, label: e.label }));
    const km = A.kindMeta[n.kind];
    const nodeRisks = riskByNode[id] || [];
    const down = downstreamNodes(id);
    const inPaths = A.criticalPaths.filter(p => pathNodeSeq(p.steps).includes(id));
    const cov = n.coverage || {};
    const chip = (d, dir) => {
      const k = (nodeById[d.id] || {}).kind || "observer";
      return `<span class="dep-chip" data-node="${d.id}" data-tip="${escAttr(d.label)}">${dir === "out" ? "" : '<span class="ar">←</span>'}<span class="sw" style="background:var(--k-${k})"></span>${esc(d.id)}${dir === "out" ? '<span class="ar">→</span>' : ""}</span>`;
    };
    $("#rrScroll").innerHTML = `
      <div class="rr-section">
        <button class="rr-back" id="rrBack"><span class="ar">←</span> back to project</button>
        <div class="dossier-id">
          <div class="dossier-glyph" style="border-color:var(--k-${n.kind});color:var(--k-${n.kind})">${esc(n.label[0].toUpperCase())}</div>
          <div><div class="dossier-name">${esc(n.label)}</div><div class="dossier-root">${esc(n.root)}</div></div>
        </div>
        <span class="dossier-kind" style="color:var(--k-${n.kind});background:color-mix(in srgb,var(--k-${n.kind}) 14%,transparent)"><span class="sw" style="background:var(--k-${n.kind})"></span>${esc(km.label)}</span>
        <p class="dossier-blurb">${linkifyStates(n.tagline)}</p>
        <svg class="ego-svg" id="egoSvg"></svg>
      </div>
      <div class="rr-section"><div class="rr-shead"><span class="rr-stitle">Owns</span></div><div class="dossier-owns">${linkifyStates(n.owns)}</div></div>
      ${n.state.length ? `<div class="rr-section"><div class="rr-shead"><span class="rr-stitle">State owned</span><span class="rr-scount">${n.state.length}</span></div>
        <div class="chip-row">${n.state.map(s => `<span class="ent dep-chip" style="color:var(--cyan)" data-entity="${escAttr(s.split(" (")[0])}">${esc(s)}</span>`).join("")}</div></div>` : ""}
      <div class="rr-section"><div class="rr-shead"><span class="rr-stitle">Dependencies</span><span class="rr-scount">${outs.length}↑ ${ins.length}↓</span></div>
        ${outs.length ? `<div class="chip-cap">depends on / writes</div><div class="chip-row" style="margin-bottom:12px">${outs.map(d => chip(d, "out")).join("")}</div>` : ""}
        ${ins.length ? `<div class="chip-cap">consumed by</div><div class="chip-row">${ins.map(d => chip(d, "in")).join("")}</div>` : ""}
      </div>
      ${down.length ? `<div class="rr-section"><div class="rr-shead"><span class="rr-stitle">Blast radius</span><span class="rr-scount">${down.length} reached</span></div>
        <div class="chip-cap">changing ${esc(n.label)} can ripple to</div>
        <div class="chip-row">${down.map(d => `<span class="dep-chip" data-node="${d}"><span class="sw" style="background:var(--k-${(nodeById[d] || {}).kind})"></span>${esc(d)}</span>`).join("")}</div>
        <button class="rr-mini-btn" id="rrImpact">⊛ show on graph</button></div>` : ""}
      ${inPaths.length ? `<div class="rr-section"><div class="rr-shead"><span class="rr-stitle">Critical paths</span><span class="rr-scount">${inPaths.length}</span></div>
        ${inPaths.map(p => `<div class="rr-path" data-path="${A.criticalPaths.indexOf(p)}"><span class="ti">▶</span>${esc(p.name)}</div>`).join("")}</div>` : ""}
      ${Object.keys(cov).length ? `<div class="rr-section"><div class="rr-shead"><span class="rr-stitle">Coverage</span></div>
        <div class="chip-row">${Object.entries(cov).map(([l, v]) => `<span class="cov-chip lv${v}" data-tip="${escAttr(LENS[l] + " — " + LVL[v])}">${esc(l)}<span class="lvl">${LVLSHORT[v]}</span></span>`).join("")}</div></div>` : ""}
      <div class="rr-section"><div class="rr-shead"><span class="rr-stitle">Files</span><span class="rr-scount">${n.files.length}</span></div>
        ${n.files.map(f => { const pa = f.split(" — "); return `<div class="file-line"><span class="fn">${esc(pa[0])}</span><span class="fa">${esc(pa[1] || "")}</span></div>`; }).join("")}</div>
      ${nodeRisks.length ? `<div class="rr-section"><div class="rr-shead"><span class="rr-stitle">Risks involving ${esc(n.label)}</span><span class="rr-scount">${nodeRisks.length}</span></div>${nodeRisks.map(riskHtml).join("")}</div>` : ""}`;
    if (graph) graph.renderEgo($("#egoSvg"), id);
    $("#rrBack").addEventListener("click", () => graph.deselect());
    $$("#rrScroll .dep-chip[data-node]").forEach(c => c.addEventListener("click", () => graph.select(c.dataset.node)));
    const imp = $("#rrImpact"); if (imp) imp.addEventListener("click", () => { switchTab("graph"); graph.setImpact(true); graph.select(id); });
    $$("#rrScroll .rr-path").forEach(rp => rp.addEventListener("click", () => { const p = A.criticalPaths[+rp.dataset.path]; switchTab("graph"); graph.flowNodes(pathNodeSeq(p.steps), { interval: 600 }); }));
  }

  function renderEdgeRail(e) {
    if (!e) return;
    const rel = A.relationships.filter(r => (normNode(r.a) === e.from && normNode(r.b) === e.to) || (normNode(r.a) === e.to && normNode(r.b) === e.from));
    const relName = { dep: "Dependency", strong: "Load-bearing dependency", write: "Write-back", peer: "Hidden coupling" }[e.rel];
    $("#rrScroll").innerHTML = `
      <div class="rr-section">
        <button class="rr-back" id="rrBack"><span class="ar">←</span> back to project</button>
        <div class="rr-shead"><span class="rr-stitle">Edge</span><span class="rr-scount tip-rel rel-${e.rel}" style="position:static">${esc(relName)}</span></div>
        <div class="edge-id"><span class="dep-chip" data-node="${e.from}"><span class="sw" style="background:var(--k-${(nodeById[e.from] || {}).kind})"></span>${esc(e.from)}</span>
          <span class="edge-arrow ${e.rel}">${e.rel === "peer" ? "↔" : "→"}</span>
          <span class="dep-chip" data-node="${e.to}"><span class="sw" style="background:var(--k-${(nodeById[e.to] || {}).kind})"></span>${esc(e.to)}</span></div>
        <p class="dossier-blurb">${linkifyStates(e.label)}</p>
      </div>
      ${rel.length ? `<div class="rr-section"><div class="rr-shead"><span class="rr-stitle">Contract</span></div>
        ${rel.map(r => `<div class="edge-contract"><div class="ec-row"><span class="ec-k">mechanism</span><span class="ec-v">${linkifyStates(r.mech)}</span></div>
          <div class="ec-row"><span class="ec-k">data</span><span class="ec-v">${esc(r.data)}</span></div>
          <div class="ec-row breaks"><span class="ec-k">breaks if</span><span class="ec-v">${esc(r.breaks)}</span></div></div>`).join("")}</div>` :
        `<div class="rr-section"><p class="risk-trigger">No formal contract row for this edge — see the two subsystems' dossiers for detail.</p></div>`}`;
    $("#rrBack").addEventListener("click", () => graph.deselect());
    $$("#rrScroll .dep-chip[data-node]").forEach(c => c.addEventListener("click", () => graph.select(c.dataset.node)));
  }

  /* ---------------- bottom KPIs ----------------
     KPIJUMP routes KPI label clicks to a section / node. Project-agnostic
     defaults below match the common KPI labels emitted by arch_seed.py;
     projects override via A.kpis[].jumpTo (a string like "paths:paths" or
     "graph:<node-id>") for any KPI whose label is not in the default set. */
  const KPIJUMP = {
    "Subsystems": () => switchTab("graph"),
    "Architecture": () => { switchTab("source"); },
    "Tests": () => openAnchor("cov", "cov"),
    "Last commit": () => openAnchor("source", "scope"),
    "Open gaps": () => openAnchor("concept", "notes"),
  };
  function kpiClick(k) {
    if (k.jumpTo) {
      const [tab, anchor] = String(k.jumpTo).split(":");
      if (tab && anchor) { openAnchor(tab, anchor); return; }
      if (tab) { switchTab(tab); return; }
    }
    const fn = KPIJUMP[k.label];
    if (fn) fn();
  }
  function renderBottom() {
    $("#bottom").innerHTML = `
      <div class="bottom-kpis">${A.kpis.map(k => `
        <div class="kpi ${k.tone || ""}" data-kpi="${esc(k.label)}" ${k.spark ? `data-tip="${escAttr("architecture.md line count growth over recent regenerations")}"` : ""}>
          <div class="kpi-label">${esc(k.label)}</div>
          <div class="kpi-vrow"><span class="kpi-val">${esc(k.value)}</span>${k.unit ? `<span class="kpi-unit">${esc(k.unit)}</span>` : ""}</div>
          <div class="kpi-foot">${k.spark ? `<div class="kpi-spark">${k.spark.map(h => `<div class="kpi-spark-bar" style="height:${h}%"></div>`).join("")}</div>` : `<div class="kpi-delta">${esc(k.delta || "")}</div>`}</div>
        </div>`).join("")}</div>
      <div class="bottom-status">
        <div class="bs-item"><span class="dot"></span>${esc(A.project.milestone)}</div>
        <div class="bs-item">nodes ${A.nodes.length} · edges ${A.edges.length}</div>
        <div class="bs-item">${esc(A.project.tests)}</div>
      </div>`;
    $$("#bottom .kpi").forEach(el => {
      const kpi = (A.kpis || []).find(k => k.label === el.dataset.kpi);
      if (!kpi) return;
      if (kpi.jumpTo || KPIJUMP[kpi.label]) {
        el.classList.add("kpi-link");
        el.addEventListener("click", () => kpiClick(kpi));
      }
    });
  }

  /* ---------------- rail toggles ---------------- */
  function initRailToggles() {
    const wb = $("#workbench");
    const app = document.querySelector(".app");
    function setLeft(c) { wb.classList.toggle("left-collapsed", c); $("#toggleLeft").classList.toggle("on", !c); if (window.App.persist) window.App.persist(); }
    function setRight(c) { wb.classList.toggle("right-collapsed", c); $("#toggleRight").classList.toggle("on", !c); if (window.App.persist) window.App.persist(); }
    function setBottom(c) { app.classList.toggle("bottom-collapsed", c); $("#toggleBottom").classList.toggle("on", !c); if (window.App.persist) window.App.persist(); }
    $("#toggleLeft").addEventListener("click", () => setLeft(!wb.classList.contains("left-collapsed")));
    $("#toggleRight").addEventListener("click", () => setRight(!wb.classList.contains("right-collapsed")));
    $("#toggleBottom").addEventListener("click", () => setBottom(!app.classList.contains("bottom-collapsed")));
    $("#edgeLeft").addEventListener("click", () => $("#toggleLeft").click());
    $("#edgeRight").addEventListener("click", () => $("#toggleRight").click());
    window.App.setLeft = setLeft; window.App.setRight = setRight; window.App.setBottom = setBottom;
  }

  /* ---------------- shared floating tooltip ---------------- */
  function initTooltip() {
    const tip = document.createElement("div");
    tip.className = "float-tip"; tip.style.display = "none";
    document.body.appendChild(tip);
    let visible = false;
    function place(x, y) {
      const w = tip.offsetWidth, h = tip.offsetHeight;
      let nx = x + 15, ny = y + 18;
      if (nx + w > innerWidth - 8) nx = x - w - 15;
      if (ny + h > innerHeight - 8) ny = y - h - 18;
      if (ny < 8) ny = 8; if (nx < 8) nx = 8;
      tip.style.left = nx + "px"; tip.style.top = ny + "px";
    }
    window.__tip = {
      show(x, y, html) { tip.innerHTML = html; tip.style.display = "block"; visible = true; place(x, y); },
      move(x, y) { if (visible) place(x, y); }, hide() { tip.style.display = "none"; visible = false; },
    };
    document.addEventListener("mouseover", e => { const t = e.target.closest("[data-tip]"); if (t) window.__tip.show(e.clientX, e.clientY, t.getAttribute("data-tip")); });
    document.addEventListener("mousemove", e => { const t = e.target.closest("[data-tip]"); if (t) window.__tip.move(e.clientX, e.clientY); });
    document.addEventListener("mouseout", e => { const t = e.target.closest("[data-tip]"); if (t) window.__tip.hide(); });
  }

  /* ---------------- boot ---------------- */
  function boot() {
    initTooltip();
    renderTopbar(); renderExplorer(); renderTabs(); renderProjectRail(); renderBottom(); initRailToggles();
    const setPlay = on => { const l = $("#playLabel"); if (l) l.textContent = on ? "Stop" : "Play tick"; };

    graph = Graph.init($("#graphStage"), {
      onSelect: id => { id ? renderNodeRail(id) : renderProjectRail(); if (window.App.persist) window.App.persist(); },
      onEdgeSelect: e => { e ? renderEdgeRail(e) : renderProjectRail(); },
      onImpact: info => { const h = $("#graphHint"); if (!h) return; h.innerHTML = info ? `<b style="color:var(--cyan)">${info.reached}</b> subsystems in the blast radius of <b>${graph.getSelected()}</b>` : "drag to pan · scroll to zoom · click node to inspect"; },
      onFlowStep: (i, id) => { setPlay(true); const h = $("#graphHint"); if (h) h.innerHTML = `tick → <b style="color:var(--cyan)">${id}</b> <span style="color:var(--tx-4)">(${i + 1})</span>`; },
      onFlowEnd: () => { setPlay(false); const h = $("#graphHint"); if (h) h.textContent = "drag to pan · scroll to zoom · click node to inspect"; },
    });
    window.App.graph = () => graph;

    $("#gFit").addEventListener("click", () => graph.fit());
    $("#gArrange").addEventListener("click", () => graph.arrange());
    $("#gZoomIn").addEventListener("click", () => graph.zoomIn());
    $("#gZoomOut").addEventListener("click", () => graph.zoomOut());
    $$("[data-graph-mode]").forEach(b => b.addEventListener("click", () => { graph.setMode(b.dataset.graphMode); if (window.App.persist) window.App.persist(); }));
    $("#gImpact").addEventListener("click", () => {
      const on = !graph.isImpact();
      graph.setImpact(on);
      const h = $("#graphHint");
      if (h && on && !graph.getSelected()) h.innerHTML = `<b style="color:var(--cyan)">Blast-radius mode</b> · select a node to see everything it can ripple to`;
      else if (h && !on) h.textContent = "drag to pan · scroll to zoom · click node to inspect";
    });
    $("#gPlay").addEventListener("click", () => { if (graph.isFlowing()) { graph.stopFlow(); setPlay(false); } else { playTick($("#pipeSel").value); setPlay(true); } });
    const lg = $("#graphLegend");
    lg.querySelector(".gl-head").addEventListener("click", () => { lg.classList.toggle("collapsed"); lg.querySelector(".gl-toggle").textContent = lg.classList.contains("collapsed") ? "show ▴" : "hide ▾"; });
    $$("#viewMode .vm-btn").forEach(b => b.addEventListener("click", () => setMd(b.dataset.vm === "source")));

    // expose API for features.js
    Object.assign(window.App, {
      switchTab, openAnchor, scrollToAnchor, esc, escAttr, flashStep, setMd,
      tabs: TABS, selectNode: id => { switchTab("graph"); graph.select(id); },
      renderProjectRail, curTab: () => curTab, setMdSource: () => { mdMode = false; },
    });
    if (window.App.initFeatures) window.App.initFeatures();

    switchTab("graph");
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot);
  else boot();
})();
