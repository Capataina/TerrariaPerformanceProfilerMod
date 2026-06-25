#nullable enable

namespace PerformanceProfiler.Web;

internal static partial class DashboardAssets
{
    // ====== INSIGHTS TAB =================================================
    // The interpretive half: the engine's CONCLUSIONS, not the per-mod
    // measurements (those live on the sibling Observatory tab). This is the
    // 'evidence, not vibes' payoff surface — the ranked feed of natural-language
    // findings the InsightsEngine produces (the /api/insights records, which had
    // no home on the dashboard until this split), each badged with its
    // confidence and its data-strength scope (Invariant 3: every insight shows
    // how strong its evidence is). An infographic summary row sits above the
    // feed (findings-by-family radial bars + a pattern×confidence multi-level
    // donut + headline tiles), and the cross-cutting signal roll-up — which mods
    // recur across the detector patterns — sits in the sidebar.
    //
    // Every string here is the engine's own descriptive render (banned-vocabulary
    // enforced in InsightRenderer); the UI only adds the badges + the layout.
    private const string JsInsights = @"
let lastCrossCutting = null;
let insAudience = 'all';   // feed lens: all | player | modder

// Pattern -> family (mirrors the engine's 5 families + segment + interaction
// groups), so the feed + summary can group findings by the kind of thing they
// interpret rather than by raw pattern key.
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

// Confidence ranking + colour, so the feed sorts strongest-first and the badge
// + card edge read consistently across the page.
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
    const [ins, cc] = await Promise.all([
      fetch('/api/insights',      { cache: 'no-store' }).then(r => r.json()),
      fetch('/api/cross-cutting', { cache: 'no-store' }).then(r => r.json()),
    ]);
    lastInsights = ins;
    lastCrossCutting = cc;
    renderInsights();
  } catch (e) { /* swallow — next tick will retry */ }
}
setInterval(pollInsights, 3000);

function renderInsights() {
  renderInsightSummary();
  renderInsightFeed();
  renderCrossCutting();
}

// ----- infographic summary row ---------------------------------------
// Three reads of the live finding set: a radial bar per family (count), a
// multi-level donut (outer = top patterns, inner = confidence mix, centre =
// total), and headline tiles. Descriptive aggregates of the same records the
// feed lists below.
function renderInsightSummary() {
  const root = document.getElementById('ins-summary');
  if (!root) return;
  const ins = lastInsights;
  function shell(body, sub) { root.innerHTML = panel({ title: 'insight summary', sub, body }); }

  if (!ins || !ins.worldLoaded || !ins.records || ins.records.length === 0) {
    renderIfChanged('insSummary', 'empty', () => shell(
      emptyState('no insights yet — the engine needs a few minutes of play to calibrate its baselines'), '—'));
    return;
  }

  const recs = ins.records;
  const byFamily = {}, byPattern = {}, byConf = { High: 0, Medium: 0, Low: 0, Preliminary: 0 };
  const mods = new Set();
  for (const r of recs) {
    const fam = insightFamily(r.pattern);
    byFamily[fam] = (byFamily[fam] || 0) + 1;
    byPattern[r.pattern] = (byPattern[r.pattern] || 0) + 1;
    if (byConf[r.confidence] != null) byConf[r.confidence]++;
    if (r.subjectModId != null && r.subjectModId >= 0) mods.add(r.subjectModId);
  }

  const famKeys = Object.keys(byFamily);
  const sig = recs.length + '|' + famKeys.map(f => f + ':' + byFamily[f]).join(',') + '|' +
    ['High', 'Medium', 'Low', 'Preliminary'].map(c => byConf[c]).join(',');
  renderIfChanged('insSummary', sig, () => {
    const familyItems = famKeys.sort((a, b) => byFamily[b] - byFamily[a])
      .map((f, i) => ({ label: f, value: byFamily[f], color: modColor(i * 3 + 2) }));
    const radial = radialBars({ items: familyItems, w: 210 });
    const radialKey = legend(familyItems.map(it => ({ color: it.color, label: it.label, value: fmtInt(it.value) })), { stack: true });

    const patItems = Object.keys(byPattern).sort((a, b) => byPattern[b] - byPattern[a]).slice(0, 8)
      .map((p, i) => ({ value: byPattern[p], label: humanizeLabel(p), color: modColor(i * 2 + 1), valueLabel: fmtInt(byPattern[p]) }));
    const confItems = ['High', 'Medium', 'Low', 'Preliminary'].filter(c => byConf[c] > 0)
      .map(c => ({ value: byConf[c], label: c.toLowerCase(), color: confColor(c), valueLabel: fmtInt(byConf[c]) }));
    const dnut = donut({ rings: [patItems, confItems], inner: 0.42, ringGap: 3, w: 180,
      center: { top: fmtInt(recs.length), mid: 'findings' } });
    const dnutKey = legend(confItems.map(c => ({ color: c.color, label: c.label, value: c.valueLabel })), { inline: true });

    const strong = (byConf.High || 0) + (byConf.Medium || 0);
    const tiles = statGrid([
      statTile({ k: 'findings live', v: fmtInt(recs.length), big: true }),
      statTile({ k: 'medium+ confidence', v: fmtInt(strong), vClass: strong > 0 ? 'good' : '', sub: 'of ' + fmtInt(recs.length) }),
      statTile({ k: 'mods implicated', v: fmtInt(mods.size) }),
      statTile({ k: 'families active', v: fmtInt(famKeys.length) }),
    ], { cols: 'repeat(2, minmax(0, 1fr))' });

    shell(`<div class='ins-summary-grid'>
      <div class='iss-cell'><div class='iss-h'>findings by family</div>${radial}${radialKey}</div>
      <div class='iss-cell'><div class='iss-h'>pattern × confidence</div>${dnut}${dnutKey}</div>
      <div class='iss-cell'>${tiles}</div>
    </div>`, `${recs.length} live · ${famKeys.length} families`);
  });
}

// ----- the insight feed (the hero) -----------------------------------
// Ranked finding cards: each is one engine conclusion (the descriptive
// shortText) with a confidence-tinted edge, a family eyebrow, the subject mod
// chip, the pattern label, a confidence + data-strength badge pair, and a
// magnitude strength bar. Filter by audience lens (player / modder). Sorted
// strongest-confidence first.
function renderInsightFeed() {
  const root = document.getElementById('ins-feed');
  if (!root) return;

  let scroll = root.querySelector('#feed-scroll');
  if (!scroll) {
    root.innerHTML = panel({
      title: 'insight feed',
      actions: segmented({ id: 'feed-aud', attr: 'data-aud', active: insAudience, options: [
        { value: 'all', label: 'all' }, { value: 'player', label: 'player' }, { value: 'modder', label: 'modder' }] }),
      body: scrollRegion('feed-scroll', '', { maxH: '40rem' }),
      pad: 'flush',
    });
    scroll = root.querySelector('#feed-scroll');
    const ctl = root.querySelector('#feed-aud');
    if (ctl) ctl.addEventListener('click', e => {
      const b = e.target.closest('[data-aud]'); if (!b) return;
      insAudience = b.dataset.aud;
      ctl.querySelectorAll('button').forEach(x => x.classList.toggle('active', x.dataset.aud === insAudience));
      renderInsightFeed();
    });
  }

  const ins = lastInsights;
  if (!ins || !ins.worldLoaded || !ins.records || ins.records.length === 0) {
    renderIfChanged('insFeed', 'empty', () => setHTML(scroll,
      emptyState('no insights yet — play for a few minutes so the engine can calibrate its baselines and surface findings')));
    return;
  }

  let recs = ins.records.slice();
  // Audience lens: 'Both' always shows; otherwise match the selected surface.
  if (insAudience !== 'all') recs = recs.filter(r => r.audience === 'Both' || (r.audience || '').toLowerCase() === insAudience);
  if (recs.length === 0) {
    renderIfChanged('insFeed', 'nomatch:' + insAudience, () => setHTML(scroll, emptyState('no ' + insAudience + '-facing findings right now')));
    return;
  }

  recs.sort((a, b) => (CONF_RANK[b.confidence] || 0) - (CONF_RANK[a.confidence] || 0)
    || Math.abs(b.ratioOrDelta || 0) - Math.abs(a.ratioOrDelta || 0)
    || (b.lastSeenTick || 0) - (a.lastSeenTick || 0));

  const sig = insAudience + '|' + recs.map(r => r.pattern + ':' + r.subjectModId + ':' + r.confidence + ':' + (r.ratioOrDelta || 0).toFixed(3)).join(',');
  if (_renderSig['insFeed'] === sig) return;
  _renderSig['insFeed'] = sig;

  setHTML(scroll, `<div class='feed'>` + recs.map(insightCard).join('') + `</div>`);
}

// One finding card. Built from the shared chip / badge / cellBar vocabulary; the
// only bespoke chrome is the confidence-tinted left edge + the card frame.
function insightCard(r) {
  const fam = insightFamily(r.pattern);
  const conf = r.confidence || 'Preliminary';
  const edge = confColor(conf);
  const modName = r.subjectModName || (r.subjectModId >= 0 ? ('mod ' + r.subjectModId) : '');
  const modChip = modName
    ? `<span class='chip'><span class='dot' style='background:${modColor(r.subjectModId)}'></span>${escapeHtml(modName)}</span>`
    : '';
  // Strength bar: |ratioOrDelta| as a 0..1 visual weight. The sentence carries
  // the real figure; this is the at-a-glance magnitude.
  const strength = Math.max(0, Math.min(1, Math.abs(r.ratioOrDelta || 0)));
  const badges = scopeBadge(r.scope) + badge(conf.toLowerCase(), confTone(conf));
  return `<div class='ins-card' style='border-left-color:${edge}' title='${escapeHtml(r.mediumText || r.shortText || '')}'>
    <div class='ic-head'>
      <span class='ic-fam'>${escapeHtml(fam)}</span>
      <span class='ic-badges'>${badges}</span>
    </div>
    <div class='ic-text'>${escapeHtml(r.shortText || '')}</div>
    <div class='ic-foot'>
      ${modChip}
      <span class='ic-pat'>${escapeHtml(humanizeLabel(r.pattern))}</span>
      <span class='ic-bar'>${cellBar(strength, edge)}</span>
    </div>
  </div>`;
}

// ----- cross-cutting signals (sidebar) -------------------------------
// Which mods recur across the detector patterns. One stacked bar per signal
// class: each segment is a leader mod (colour = its per-mod hue), width = its
// share of that class's appearances, with a compact legend of the top leaders.
// Descriptive only — a leader is the mod measured most often in that pattern.
function renderCrossCutting() {
  const root = document.getElementById('ins-cross');
  if (!root) return;

  let scroll = root.querySelector('#cc-scroll');
  if (!scroll) {
    root.innerHTML = panel({ title: 'cross-cutting signals', sub: '—',
      body: scrollRegion('cc-scroll', '', { maxH: '40rem' }), pad: 'flush' });
    scroll = root.querySelector('#cc-scroll');
  }
  const subEl = root.querySelector('.panel-sub');
  const cc = lastCrossCutting;

  if (!cc || !cc.worldLoaded || !cc.groups || cc.groups.length === 0) {
    if (subEl) subEl.textContent = '—';
    renderIfChanged('insCross', 'none', () => setHTML(scroll, emptyState('no cross-cutting signals recorded yet')));
    return;
  }
  const groups = cc.groups.filter(g => g.leaders && g.leaders.length > 0);
  if (groups.length === 0) {
    if (subEl) subEl.textContent = '—';
    renderIfChanged('insCross', 'noleaders', () => setHTML(scroll, emptyState('signals recorded but no leaders yet')));
    return;
  }

  const distinct = new Set();
  groups.forEach(g => g.leaders.forEach(l => distinct.add(l.modId)));
  if (subEl) subEl.textContent = `${groups.length} classes · ${distinct.size} mods`;

  const sig = groups.map(g => g.signalClass + '[' +
    g.leaders.map(l => l.modId + ':' + (l.appearances || 0)).join(',') + ']').join('|');
  if (_renderSig['insCross'] === sig) return;
  _renderSig['insCross'] = sig;

  const sections = groups.map(g => {
    const leaders = g.leaders.slice().sort((a, b) => b.appearances - a.appearances);
    const total = leaders.reduce((s, l) => s + (l.appearances || 0), 0) || 1;
    const segs = leaders.map(l => ({
      frac: (l.appearances || 0) / total, color: modColor(l.modId),
      label: l.modName, value: fmtInt(l.appearances),
    }));
    const lg = splitLegend(leaders.slice(0, 4).map(l => ({
      color: modColor(l.modId), label: truncate(l.modName, 16), value: fmtInt(l.appearances),
    })));
    return `<div class='cc-class'>
      <div class='section-h'><span>${escapeHtml(humanizeLabel(g.signalClass))}</span><span class='section-sub'>${fmtInt(leaders.length)} mods</span></div>
      ${splitBar(segs, { tall: true })}
      ${lg}
    </div>`;
  }).join('');

  setHTML(scroll, `<div class='cc-stack'>${sections}</div>`);
}
";
}
