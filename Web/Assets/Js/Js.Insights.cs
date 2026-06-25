#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // ====== INSIGHTS TAB =================================================
    // The interpretive half: the engine's CONCLUSIONS (the per-mod measurements
    // live on the Observatory tab). Layout:
    //   1. an OVERVIEW pane — one panel split by a gentle vertical divider:
    //      left 2/3 = a cleaned summary (headline tiles + a confidence donut),
    //      right 1/3 = the cross-cutting signal roll-up.
    //   2. a KANBAN board below it — one column per insight FAMILY (cost
    //      deviation / temporal drift / distribution / headroom / structure /
    //      interaction / segment), cards sorted strongest-confidence first.
    //   3. a slide-in DRAWER on card click — the mod's full context: what it
    //      ADDS (roster composition), its cost/usage, and every live insight
    //      about it. This is the answer to 'an insight about one hook is
    //      meaningless without knowing everything the mod adds' — the drawer
    //      shows the whole roster so the finding can be judged in context.
    //
    // Every finding string is the engine's own descriptive render (InsightRenderer
    // bans verdict vocabulary); the UI only adds the badges, the board, and the
    // drawer. Reads /api/insights (the feed, which had no home before the
    // Observatory/Insights split), /api/cross-cutting, and /api/mod-observatory
    // (for the drawer's roster context).
    private const string JsInsights = @"
let lastCrossCutting = null;
let insAudience = 'all';   // kanban lens: all | player | modder

// Pattern -> family (mirrors the engine's families + segment/interaction groups).
const INSIGHT_FAMILIES = {
  ContextCorrelatedSpike: 'cost deviation', ContextConditionalCost: 'cost deviation',
  HotHookDominance: 'cost deviation', AllocationBurst: 'cost deviation',
  GcPauseCulprit: 'cost deviation', PeakContributorToSpike: 'cost deviation',
  HookFrequencyTail: 'cost deviation',
  SustainedCostShift: 'temporal drift', NewContributor: 'temporal drift', HeapLeak: 'temporal drift',
  FrameJitter: 'distribution', FrameHeadroom: 'headroom',
  CostConcentration: 'structure', FreeRemovalCandidate: 'structure',
  LoadoutCorrelatedCost: 'interaction', EventConditionalCost: 'interaction', LoadoutCombinationCost: 'interaction',
  SegmentOutlier: 'segment', SegmentTopMod: 'segment', SegmentDeathCorrelation: 'segment',
};
function insightFamily(p) { return INSIGHT_FAMILIES[p] || 'other'; }
// Fixed column order for the kanban (only families present are shown).
const FAMILY_ORDER = ['cost deviation', 'temporal drift', 'distribution', 'headroom', 'structure', 'interaction', 'segment', 'other'];

// Confidence ranking + colour, so cards sort strongest-first and badge + edge
// read consistently.
const CONF_RANK = { High: 3, Medium: 2, Low: 1, Preliminary: 0 };
function confTone(c) { return c === 'High' ? 'good' : c === 'Medium' ? 'accent' : c === 'Low' ? '' : 'dim'; }
function confColor(c) { return c === 'High' ? 'var(--good)' : c === 'Medium' ? 'var(--good-bar)' : c === 'Low' ? 'var(--muted)' : 'var(--dim)'; }

// Data-strength scope -> badge. ThisSession is the only scope the live store can
// earn today (the cross-session insight producer is a future DB-rework step), so
// most findings badge 'this session'; the lifetime / needs-persistence tones are
// wired so they light up for free once that producer lands.
function scopeBadge(scope) {
  if (scope === 'LifetimeData') return badge('lifetime data', 'good');
  if (scope === 'NeedsPersistence') return badge('needs persistence', 'warn');
  return badge('this session', 'accent');
}

async function pollInsights() {
  if (activeTab !== 'insights') return;
  try {
    const [ins, cc, mo] = await Promise.all([
      fetch('/api/insights',       { cache: 'no-store' }).then(r => r.json()),
      fetch('/api/cross-cutting',  { cache: 'no-store' }).then(r => r.json()),
      fetch('/api/mod-observatory',{ cache: 'no-store' }).then(r => r.json()),
    ]);
    lastInsights = ins;
    lastCrossCutting = cc;
    lastModObservatory = mo;   // for the card-click drawer's roster context
    renderInsights();
  } catch (e) { /* swallow — next tick will retry */ }
}
setInterval(pollInsights, 3000);

function renderInsights() {
  renderInsightOverview();
  renderInsightKanban();
}

// ----- combined overview pane (summary | cross-cutting) --------------
// One panel, two halves divided by a gentle vertical rule. Built once; each half
// fills + gates independently.
function renderInsightOverview() {
  const root = document.getElementById('ins-overview');
  if (!root) return;
  if (!root.querySelector('.ins-ov-grid')) {
    root.innerHTML = panel({ cls: 'ins-ov-panel', body:
      `<div class='ins-ov-grid'>
        <div class='ins-ov-left'>
          <div class='section-h'><span>insight summary</span><span class='section-sub' id='ins-sum-sub'></span></div>
          <div id='ins-sum-body'></div>
        </div>
        <div class='ins-ov-right'>
          <div class='section-h'><span>cross-cutting signals</span><span class='section-sub' id='ins-cc-sub'></span></div>
          <div id='ins-cc-body'></div>
        </div>
      </div>` });
  }
  fillInsightSummary();
  fillCrossCutting();
}

// Left 2/3: headline tiles + a confidence donut. Family distribution is left to
// the kanban columns below (so this no longer duplicates it).
function fillInsightSummary() {
  const body = document.getElementById('ins-sum-body');
  const subEl = document.getElementById('ins-sum-sub');
  if (!body) return;
  const ins = lastInsights;
  if (!ins || !ins.worldLoaded || !ins.records || ins.records.length === 0) {
    if (subEl) subEl.textContent = '';
    renderIfChanged('insSum', 'empty', () => setHTML(body,
      emptyState('no insights yet — the engine needs a few minutes of play to calibrate its baselines')));
    return;
  }
  const recs = ins.records;
  const byConf = { High: 0, Medium: 0, Low: 0, Preliminary: 0 };
  const fams = new Set(); const mods = new Set();
  for (const r of recs) {
    if (byConf[r.confidence] != null) byConf[r.confidence]++;
    fams.add(insightFamily(r.pattern));
    if (r.subjectModId != null && r.subjectModId >= 0) mods.add(r.subjectModId);
  }
  const strong = byConf.High + byConf.Medium;
  if (subEl) subEl.textContent = `${recs.length} live`;

  const sig = recs.length + '|' + ['High', 'Medium', 'Low', 'Preliminary'].map(c => byConf[c]).join(',') + '|' + mods.size + '|' + fams.size;
  renderIfChanged('insSum', sig, () => {
    const confItems = ['High', 'Medium', 'Low', 'Preliminary'].filter(c => byConf[c] > 0)
      .map(c => ({ value: byConf[c], label: c.toLowerCase(), color: confColor(c), valueLabel: fmtInt(byConf[c]) }));
    const dnut = donut({ data: confItems, inner: 0.6, w: 150, center: { top: fmtInt(recs.length), mid: 'findings' } });
    const key = legend(confItems.map(c => ({ color: c.color, label: c.label, value: c.valueLabel })), { stack: true });
    const tiles = statGrid([
      statTile({ k: 'findings live', v: fmtInt(recs.length), big: true }),
      statTile({ k: 'medium+ confidence', v: fmtInt(strong), vClass: strong > 0 ? 'good' : '', sub: 'of ' + fmtInt(recs.length) }),
      statTile({ k: 'mods implicated', v: fmtInt(mods.size) }),
      statTile({ k: 'families active', v: fmtInt(fams.size) }),
    ], { cols: 'repeat(2, minmax(0, 1fr))' });
    setHTML(body, `<div class='ins-sum-layout'>
      <div class='ins-sum-tiles'>${tiles}</div>
      <div class='ins-sum-chart'>${dnut}${key}</div>
    </div>`);
  });
}

// Right 1/3: one stacked bar per signal class — which mods recur across the
// detector patterns. Each segment is a leader mod (its per-mod hue), width = its
// share of that class's appearances.
function fillCrossCutting() {
  const body = document.getElementById('ins-cc-body');
  const subEl = document.getElementById('ins-cc-sub');
  if (!body) return;
  const cc = lastCrossCutting;
  if (!cc || !cc.worldLoaded || !cc.groups || cc.groups.length === 0) {
    if (subEl) subEl.textContent = '';
    renderIfChanged('insCC', 'none', () => setHTML(body, emptyState('no cross-cutting signals recorded yet')));
    return;
  }
  const groups = cc.groups.filter(g => g.leaders && g.leaders.length > 0);
  if (groups.length === 0) {
    if (subEl) subEl.textContent = '';
    renderIfChanged('insCC', 'noleaders', () => setHTML(body, emptyState('signals recorded but no leaders yet')));
    return;
  }
  const distinct = new Set();
  groups.forEach(g => g.leaders.forEach(l => distinct.add(l.modId)));
  if (subEl) subEl.textContent = `${groups.length} classes`;

  const sig = groups.map(g => g.signalClass + '[' + g.leaders.map(l => l.modId + ':' + (l.appearances || 0)).join(',') + ']').join('|');
  renderIfChanged('insCC', sig, () => {
    const sections = groups.map(g => {
      const leaders = g.leaders.slice().sort((a, b) => b.appearances - a.appearances);
      const total = leaders.reduce((s, l) => s + (l.appearances || 0), 0) || 1;
      const segs = leaders.map(l => ({
        frac: (l.appearances || 0) / total, color: modColor(l.modId), label: l.modName, value: fmtInt(l.appearances),
      }));
      const lg = splitLegend(leaders.slice(0, 3).map(l => ({
        color: modColor(l.modId), label: truncate(l.modName, 14), value: fmtInt(l.appearances),
      })));
      return `<div class='cc-class'>
        <div class='section-h'><span>${escapeHtml(humanizeLabel(g.signalClass))}</span><span class='section-sub'>${fmtInt(leaders.length)} mods</span></div>
        ${splitBar(segs, { tall: true })}${lg}
      </div>`;
    }).join('');
    setHTML(body, `<div class='cc-stack'>${sections}</div>`);
  });
}

// ----- the kanban board ----------------------------------------------
// One column per insight family; cards sorted strongest-confidence first. Audience
// lens filters player- vs modder-facing findings. Clicking a card opens the mod
// context drawer.
function renderInsightKanban() {
  const root = document.getElementById('ins-kanban');
  if (!root) return;

  let board = root.querySelector('#kanban-body');
  if (!board) {
    root.innerHTML = panel({
      title: 'findings by category',
      actions: segmented({ id: 'kan-aud', attr: 'data-aud', active: insAudience, options: [
        { value: 'all', label: 'all' }, { value: 'player', label: 'player' }, { value: 'modder', label: 'modder' }] }),
      body: `<div id='kanban-body'></div>`,
      pad: 'tight',
    });
    board = root.querySelector('#kanban-body');
    const ctl = root.querySelector('#kan-aud');
    if (ctl) ctl.addEventListener('click', e => {
      const b = e.target.closest('[data-aud]'); if (!b) return;
      insAudience = b.dataset.aud;
      ctl.querySelectorAll('button').forEach(x => x.classList.toggle('active', x.dataset.aud === insAudience));
      renderInsightKanban();
    });
  }

  const ins = lastInsights;
  if (!ins || !ins.worldLoaded || !ins.records || ins.records.length === 0) {
    renderIfChanged('insKanban', 'empty', () => setHTML(board,
      emptyState('no insights yet — play for a few minutes so the engine can calibrate its baselines and surface findings')));
    return;
  }

  let recs = ins.records.slice();
  if (insAudience !== 'all') recs = recs.filter(r => r.audience === 'Both' || (r.audience || '').toLowerCase() === insAudience);
  if (recs.length === 0) {
    renderIfChanged('insKanban', 'nomatch:' + insAudience, () => setHTML(board, emptyState('no ' + insAudience + '-facing findings right now')));
    return;
  }

  // Group by family, sort each column strongest-first.
  const groups = {};
  for (const r of recs) {
    const f = insightFamily(r.pattern);
    if (!groups[f]) groups[f] = [];
    groups[f].push(r);
  }
  const byStrength = (a, b) => (CONF_RANK[b.confidence] || 0) - (CONF_RANK[a.confidence] || 0)
    || Math.abs(b.ratioOrDelta || 0) - Math.abs(a.ratioOrDelta || 0)
    || (b.lastSeenTick || 0) - (a.lastSeenTick || 0);
  const cols = FAMILY_ORDER.filter(f => groups[f] && groups[f].length).map(f => ({ fam: f, cards: groups[f].sort(byStrength) }));

  const sig = insAudience + '|' + cols.map(c => c.fam + ':' + c.cards.length + ':' +
    c.cards.map(r => r.pattern + r.subjectModId + r.confidence + (r.ratioOrDelta || 0).toFixed(2)).join('')).join('|');
  if (_renderSig['insKanban'] === sig) return;
  _renderSig['insKanban'] = sig;

  const html = cols.map(col => `<div class='kan-col'>
    <div class='kan-col-h'><span>${escapeHtml(col.fam)}</span><span class='kan-count'>${fmtInt(col.cards.length)}</span></div>
    <div class='kan-col-body'>${col.cards.map(kanbanCard).join('')}</div>
  </div>`).join('');
  setHTML(board, `<div class='kanban'>${html}</div>`);

  board.querySelectorAll('.kan-card').forEach(el => {
    el.addEventListener('click', () => openInsightDetail(parseInt(el.dataset.mod, 10)));
  });
}

// One kanban card. The family is the column, so the card carries the finding
// sentence (the value), the confidence + data-strength badges, the subject mod,
// the pattern, and a magnitude strength bar. Clickable -> mod context drawer.
function kanbanCard(r) {
  const conf = r.confidence || 'Preliminary';
  const edge = confColor(conf);
  const hasMod = r.subjectModId != null && r.subjectModId >= 0;
  const modName = r.subjectModName || (hasMod ? 'mod ' + r.subjectModId : 'session');
  const modChip = `<span class='chip'><span class='dot' style='background:${hasMod ? modColor(r.subjectModId) : 'var(--muted)'}'></span>${escapeHtml(modName)}</span>`;
  const strength = Math.max(0, Math.min(1, Math.abs(r.ratioOrDelta || 0)));
  return `<div class='kan-card' data-mod='${hasMod ? r.subjectModId : -1}' style='border-left-color:${edge}' title='${escapeHtml(r.mediumText || r.shortText || '')}'>
    <div class='kc-badges'>${scopeBadge(r.scope)}${badge(conf.toLowerCase(), confTone(conf))}</div>
    <div class='kc-text'>${escapeHtml(r.shortText || '')}</div>
    <div class='kc-foot'>${modChip}<span class='kc-pat'>${escapeHtml(humanizeLabel(r.pattern))}</span></div>
    <div class='kc-bar'>${cellBar(strength, edge)}</div>
  </div>`;
}

// ----- mod context drawer --------------------------------------------
// The slide-in opened by a card click. Leads with WHAT THE MOD ADDS (its roster
// composition) so a single-hook finding can be judged against the whole mod, then
// its roster-vs-usage, cost & engagement, and every live insight about it. Reads
// the observatory card (roster) + the insight records for the mod. Degrades
// gracefully when the observatory snapshot has not arrived or the subject is
// session-level (no mod).
function openInsightDetail(modId) {
  const drawer = document.getElementById('ins-drawer');
  const body = document.getElementById('ins-dr-body');
  const titleEl = document.getElementById('ins-dr-title');
  const metaEl = document.getElementById('ins-dr-meta');
  if (!drawer || !body) return;

  const card = (modId >= 0 && lastModObservatory && lastModObservatory.cards)
    ? lastModObservatory.cards.find(c => c.modId === modId) : null;
  const recs = (lastInsights && lastInsights.records)
    ? lastInsights.records.filter(r => (r.subjectModId == null ? -1 : r.subjectModId) === modId) : [];
  const name = card ? card.modName : (recs[0] && recs[0].subjectModName) || (modId >= 0 ? 'mod ' + modId : 'session-level findings');
  if (titleEl) titleEl.textContent = name;
  if (metaEl) metaEl.textContent = card ? `${(card.cpuSharePct * 100).toFixed(1)}% cpu · ${(card.usageSharePct * 100).toFixed(1)}% usage` : `${recs.length} finding${recs.length === 1 ? '' : 's'}`;

  let html = '';

  // 1. What this mod adds — the roster composition. The answer to 'is this cost
  //    justified by what the mod actually provides'.
  if (card) {
    const r = card.roster, u = card.usage;
    const totalRoster = ROSTER_CATS.reduce((s, kv) => s + (r[kv[0]] || 0), 0);
    const legendSegs = ROSTER_CATS
      .map(kv => ({ frac: r[kv[0]] || 0, color: kv[2], label: kv[1], value: fmtInt(r[kv[0]] || 0) }))
      .filter(s => s.frac > 0);
    const compHtml = legendSegs.length > 0
      ? splitBar(legendSegs, { tall: true }) + splitLegend(legendSegs)
      : `<div class='comp-empty'>no content registered (library-shaped mod)</div>`;
    html += sectionBlock('what this mod adds', compHtml, totalRoster > 0 ? fmtInt(totalRoster) + ' entries' : '');

    html += sectionBlock('cost & engagement',
      statLine('cpu share', dash(card.cpuSharePct, v => (v * 100).toFixed(2) + '%')) +
      statLine('smoothed ms this tick', dash(card.smoothedMsThisTick, v => fmtMs(v) + ' ms')) +
      statLine('average ms', dash(card.averageMs, v => fmtMs(v) + ' ms')) +
      statLine('usage share', dash(card.usageSharePct, v => (v * 100).toFixed(2) + '%')));

    const rosterRows = [
      ['items', r.items, u.itemsCreated], ['npcs', r.npcs, u.npcsSpawned], ['buffs', r.buffs, u.buffsApplied],
      ['projectiles', r.projectiles, null], ['mounts', r.mounts, null], ['accessories', r.accessories, u.accessoryEquippedTicks],
      ['biomes', r.biomes, u.ticksInOwnedBiomes], ['invasions', r.invasions, u.invasionsFought], ['bosses', r.bosses, u.bossesFought],
    ].map(kv => {
      const used = kv[2] == null ? `<td class='dim'>—</td>` : `<td>${fmtInt(kv[2])}</td>`;
      return `<tr><td class='l'>${kv[0]}</td><td>${fmtInt(kv[1])}</td>${used}</tr>`;
    }).join('');
    html += sectionBlock('roster vs usage',
      dtable(`<tr><th class='l'>category</th><th>roster</th><th>used / counted</th></tr>`, rosterRows));
  } else if (modId >= 0) {
    html += callout('Observatory snapshot has not arrived yet — open the Observatory tab once, or wait a poll, to load the mod roster here.', 'warn');
  }

  // 2. Every live insight about this mod / subject.
  if (recs.length > 0) {
    const sorted = recs.slice().sort((a, b) => (CONF_RANK[b.confidence] || 0) - (CONF_RANK[a.confidence] || 0));
    const list = sorted.map(r => {
      const conf = r.confidence || 'Preliminary';
      return `<div class='dr-insight' style='border-left-color:${confColor(conf)}'>
        <div class='dr-ins-badges'>${badge(humanizeLabel(insightFamily(r.pattern)), '')}${scopeBadge(r.scope)}${badge(conf.toLowerCase(), confTone(conf))}</div>
        <div class='dr-ins-text'>${escapeHtml(r.shortText || '')}</div>
      </div>`;
    }).join('');
    html += sectionBlock('insights about this mod', `<div class='dr-insights'>${list}</div>`, fmtInt(recs.length));
  }

  setHTML(body, html || emptyState('no detail available'));
  drawer.classList.remove('hidden');
}

function closeInsightDetail() {
  const d = document.getElementById('ins-drawer');
  if (d) d.classList.add('hidden');
}
(function () {
  const close = document.getElementById('ins-dr-close');
  if (close) close.addEventListener('click', closeInsightDetail);
  document.addEventListener('keydown', e => { if (e.key === 'Escape') closeInsightDetail(); });
})();
";
}
