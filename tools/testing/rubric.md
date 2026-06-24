# L8 audit rubric — what every review agent applies

This is the shared checklist that turns "looks off" into a structured, fixable
finding. It is **principle-based**, so it covers panes that do not exist yet: a new
tab inherits the whole rubric for free. Every review agent reads this file plus
`design-bar.md` before looking at its screenshots.

Each finding you emit carries a **severity** so the fix-list ranks itself:

| Severity | Meaning |
|---|---|
| **P1** | Broken, unreadable, wrong, or dishonest. A user is actively misled or blocked. |
| **P2** | Real clarity / dead-space / hierarchy problem. Works, but reads as unfinished. |
| **P3** | Polish. A refinement that raises the ceiling. |

Emit findings as JSON objects (see the protocol in `README.md`):
`{ severity, category, panel, state, title, what, fix }`.
`panel` is the panel title (or `"page"` for a whole-tab issue); `state` is the
screenshot state (`default` / `scrolled` / `selected` / `whole` / `drawer`).

> **The L4 / L8 split.** If a finding can be written as a true/false assertion
> (nothing overflows, no box above its sticky header, a drawer moves off-screen),
> it belongs in L4 and is already gated there — do not re-file it. L8 is for the
> judgement half: legibility, completeness, hierarchy, encoding-fit, taste. If it
> needs an eye, it is yours.

---

## Layout & alignment

- **Overlap / leak.** Elements overlapping; text or a row leaking past a sticky
  header (L4 catches the gross case; you catch the near-misses under its threshold).
- **Overflow / clipping.** Content wider than its container; a chip, label, number,
  or bar clipped at an edge; a value cut off with no ellipsis.
- **Vertical baseline misalignment.** Labels that should share a line sitting a few
  px apart; a value not centred against its bar; a number floating above its
  caption. The most common amateur tell, and invisible to a per-element assertion —
  look for it specifically.
- **Inconsistent spacing.** Gutters / padding / gaps that vary where they should be
  uniform; a ragged or broken grid; tiles that do not line up across a row.
- **Dead space.** A panel mostly empty with its content clustered in one corner; a
  chart given far more height than its data uses.

## Readability

- Text too small at the real render size; low contrast (muted-on-muted, a value that
  vanishes into its background); truncation with no ellipsis; a wall of same-weight
  text with no scanning path; numbers with too many decimals to read at a glance.

## Colour & encoding

- Clashing or muddy palette; the **same colour meaning two things** across panes, or
  two colours meaning one thing.
- **Decoration vs encoding (project rule).** Colour must *encode* — a severity, a
  per-mod series, a state. Flag any colour that carries no meaning and is just
  decorating. The chrome is deliberately monochrome; **the only colour on screen
  should be data.** Flag colour that has crept into the chrome.
- Colourblind-unsafe pairs (red/green as the only distinction).

## Affordance completeness

- A long list (roughly >12 rows) with **no search / sort / filter**.
- A missing or cramped **empty state**; no "no data yet" copy where a pane can be
  empty; an empty state crushed against its header.
- No **hover / selection / focus feedback** on something interactive; something that
  looks clickable and is not (or the reverse); a drawer with no obvious open/close
  affordance.

## Chart appropriateness (the Chart.Guide discipline)

- **Wrong chart for the data shape.** Match the encoding to the question:
  part-to-whole → donut / stacked / treemap; distribution → histogram / box;
  relationship → scatter / heatmap; over time → line / area / sparkline; flow →
  sankey; ranking → sorted bars. **A bar used for everything is the current smell** —
  see `design-bar.md` for the richer vocabulary the data already supports.
- Missing context (a value with no baseline / reference line / comparison);
  misleading axes (truncated or dual-Y); gridline clutter; too many series; a rainbow
  where a sequential ramp belongs.

## Information hierarchy

- The most important number in a panel is not the most prominent thing in it; no
  visual path (everything the same weight, the eye has nowhere to land first); a
  headline metric buried in a table.

## Consistency (the component-library discipline)

- The same concept rendered differently across panes (two "selected" styles, two
  empty-state treatments, two ways of drawing a bar) — the drift the component
  library exists to kill, now caught visually.

## Honesty (Invariant 3 — non-negotiable, always P1)

- **Normative vocabulary in player copy** ("remove", "core", "bad mod", "should")
  instead of descriptive phrasing. The profiler describes; it never prescribes.
- An insight or number shown **without its data-strength / confidence / baseline**
  context where the design calls for one.

---

## How to look

1. Read `design-bar.md` first, so you are judging against a ceiling, not just
   hunting for broken things.
2. Open the tab's `_whole.png` to get the layout and hierarchy in context.
3. Open each panel crop (`NN-title.png`) and its state variants (`--scrolled`,
   `--selected`) and the `_drawer.png` if present. The crops are where legibility,
   alignment, and encoding problems are visible up close.
4. Read the existing page dossier (`context/pages/<tab>.md`) so you do not re-report
   what is already known and can confirm or update prior findings.
5. Be specific and concrete: name the panel, the state, what is wrong, and the fix.
   A finding with no suggested fix is half a finding.
