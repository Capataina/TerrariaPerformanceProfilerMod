# Plan — Genericise the dashboard into a shadcn-style component library

> Goal: stop coding each pane individually. Build the dashboard out of a small
> set of **reusable, canonical UI components** so that every panel shares one
> implementation of spacing, scrolling, selection, hover, and empty states, and
> the *only* thing that varies per surface is the **content** inside. When that
> holds, a whole class of bugs (leaking strips, scroll regions that don't scroll,
> mis-centred placeholders, inconsistent hover/selection colours) becomes
> structurally impossible — and when something *does* break, it breaks in one
> place we already know how to look at, not in a pane-specific reimplementation.
>
> Adopt a **shadcn-rooted design language**: shadcn's token system + its
> OKLCH-based, perceptually-grounded colour scheme, which is the cleanest modern
> baseline to build on.
>
> Status: **NOT STARTED — directional skeleton only.** This file states the
> *why* and the target shape. It is the brief for a future dedicated build, not
> a record of work done.
>
> Date opened: 2026-06-22. Mod version at writing: `0.16`.

---

## Why this exists (the root cause)

Every recurring dashboard bug this project keeps re-finding has the **same**
shape: a surface was hand-coded with its own spacing / scroll / state styling,
and that bespoke copy drifted from every other surface's bespoke copy. The fix
each time is local; the disease is structural. The 2026-06-22 visual sweep is
the evidence — five separate "bugs", one cause:

| Symptom (this session) | Bespoke cause | What a shared component would have done |
|---|---|---|
| Memory strip leaks past the right edge | The strip sat flush to a panel that carries no body padding; the table/header inset differently | A `Panel` with a single padded body slot — every child inset identically, no per-pane gutter maths |
| Observatory "selected" bar is green; the table beside it selects in blue | Each surface invented its own `.selected` / `.sel` rule with its own colour | One `selectable-row` treatment; "selected" is one token everywhere |
| Observatory list stretches forever (no scroll on 150 mods) | The card list relied on a parent height nothing actually bounded | A `ScrollRegion` primitive that owns `max-height` + overflow as a contract, not a per-pane afterthought |
| Top bar clips when content scrolls to the end | Document-level overscroll on a fixed-height grid the shell didn't fully pin | An `AppShell` that owns the fixed/scroll split once |
| Cascade hover draws extra top/bottom edges; gold rows won't go blue | `box-shadow` left-bar in one place, `border-left` in another, `:hover` losing a specificity race | One `row` component with one hover/selection model and a reserved, tinted left bar |

The `Web/Assets/Css/Css.Coherence.cs` layer is the **embryo** of this idea: it
already pulled nine divergent empty-state classes onto one canonical `.empty`,
and now the selection accent too. The plan is to generalise that exact move from
"empty states + selection" to **every primitive the dashboard draws**.

> The mental model the user named: *"think of it like our very own shadcn
> component library"* — panels, tables, lists, drawers, chips, bars all come
> from one place; content is the only variable; a spacing or centring issue can
> then only mean the content is broken, and you know exactly where to look.

---

## Target shape — content is the only variable

Today a pane is HTML mount-points + a bespoke renderer that bakes in layout.
The target: a renderer **composes declared components** and passes *data*; the
component owns all structure, spacing, scroll, and state.

```
  TODAY (per-pane)                    TARGET (composed)
  ────────────────                    ─────────────────
  HtmlMemory: bespoke divs            Panel({ title, sub, actions, body:
  Css.Memory: bespoke spacing   ->      Strip(data) + Legend(data) })
  Js.Memory:  bespoke strip+table     Panel({ body: DataTable(cols, rows) })
                                      Panel({ body: Drawer(selection) })
  (spacing/scroll/state live           (spacing/scroll/state live in the
   in each of the three files)          component, once)
```

When every pane is built this way, a leaking strip is impossible unless `Strip`
itself leaks — and `Strip` is one file with one test.

---

## Component inventory (skeleton)

The set is intentionally small — shadcn-sized, not a framework. Some already
exist partially in `Js.Components.cs` / `Css.Components.cs`; the work is to make
them **the only** way each shape is drawn and retire the per-pane variants.

| Component | Owns | State today |
|---|---|---|
| `AppShell` | the fixed top bar / tabs / footer + the single scroll region; overscroll containment | bespoke in `Css.Shell` (works, but not a declared primitive) |
| `Panel` | border, radius, header (title / sub / actions), **one padded body slot** | bespoke `.panel` + per-pane body insets — the leak source |
| `ScrollRegion` | `max-height` + overflow + poll-stable `scrollTop` (the `setHTML` trick) | reinvented per surface (`.dor-scroll`, `.obs-scroll`, `.det-scroll`, `.mem-table-wrap`, `.modtable`) |
| `DataTable` | sticky header, sortable cols, perf-tint, **one** row hover + selection model | `.dtable` exists and is good — make it universal |
| `SplitBar` + `Legend` | stacked composition, containment, thin/tall variants | `.split-bar` exists; the strip is a near-duplicate to fold in |
| `RowList` / `Card` | ranked clickable list, one hover + `selected` treatment | observatory + mod cascade each rolled their own |
| `Drawer` | detail header + sectioned body | `.mem-drawer` / `.ins-detail` are parallel reimplementations |
| `StatCell` / `KpiTile` / `Ring` | label-over-value, capped width, optional bar/gauge | `.mem-card`, `.topstat`, `.ins-kpi` overlap |
| `EmptyState` | the canonical muted/centred placeholder | **done** — `emptyState()` + `Css.Coherence` (the proof the pattern works) |
| `Chip`, `CellBar`, `SegmentedControl` | small tokens | mostly shared already |

Cross-cutting **interaction contract** (the thing that keeps drifting):
one hover model (subtle bg + reserved tinted left bar, no extra edges), one
selection token (the signature blue), one focus-visible ring. Declared once,
inherited everywhere.

---

## Design tokens — adopt shadcn's scheme

Move from our ad-hoc palette to shadcn's **semantic token layer**, backed by
**OKLCH** (perceptually uniform — equal numeric steps look like equal visual
steps, which is the "rooted in science" part and why tints/states stay legible).

Semantic tokens to define (shadcn naming, dark-first since the dashboard is a
dark dev tool):

```
--background  --foreground         (page / text)
--card        --card-foreground    (Panel surface / its text)
--popover     --popover-foreground (Drawer / tooltip)
--primary     --primary-foreground (the one signature accent)
--secondary   --muted  --muted-foreground
--accent      --accent-foreground
--destructive                       (danger / stalls)
--border  --input  --ring          (lines, fields, focus)
--radius                            (one radius scale)
+ a charted data ramp (perf-0..4, series colours) kept as its own block
```

Our current `Css.Palette.cs` vars map cleanly onto this (e.g. `--accent` →
`--primary`, `--panel` → `--card`, `--border-soft`/`--border` → `--border`/
`--input`). The data-viz ramp (`--perf-0..4`, `--cpu`, `--alloc`, …) stays a
separate, deliberately-chosen block — semantic UI tokens and chart encodings
are different concerns and should not be collapsed.

> Honesty-contract note (Invariant 3): adopting shadcn is a **visual/structural**
> change only. No component may introduce normative copy or judgement; the
> descriptive-not-prescriptive rule governs component *content* exactly as it
> does today.

---

## Migration approach (when this is picked up)

Strangler pattern, not a big-bang rewrite — the dashboard must stay working
throughout:

1. **Tokens first.** Land the shadcn token layer alongside the current palette
   (alias old vars to new) so nothing breaks; this is the contract every
   component compiles against (mirrors the contract-decoupling pattern used for
   the data-layer waves).
2. **One component, one pane, end-to-end** as the reference (likely `Panel` +
   `ScrollRegion`, since they cause the most bugs). Prove the shape on Memory.
3. **Fan out pane by pane**, retiring each bespoke CSS/JS block as its pane
   moves onto the components. Blast-radius rule: deleting a `.mem-strip` style
   means the strip now comes from `SplitBar` — verify the pane, not just the
   diff.
4. **The coherence layer shrinks as it wins**: every alias it currently holds
   (`.tl-empty`, `.lag-empty`, …) disappears when its pane stops emitting the
   legacy class. A shrinking `Css.Coherence.cs` is the success metric.

Verification stays dual-surface and uses the existing preview harness
(`tools/preview/render.py`) per pane; interaction states (hover/selection/
scroll-to-end) need the live browser, since the harness only shoots static
frames — that limitation is itself part of why these bugs slip through and is a
reason to make the states component-owned and testable in isolation.
