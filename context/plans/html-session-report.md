# Plan — HTML Session Report (S17)

> Slot: atlas S17. Design seed: context/notes/future-html-report.md
> (2026-05-20). Version target: 0.34.0.

## Why

The dashboard dies with the game process; the LiteDB is not human-readable.
Players want to keep and share "how did my session go" — one double-clickable
file, no game, no server. Also the Workshop-appeal artefact: the thing players
post.

## Design

### Generator

`Persistence/Report/HtmlReportWriter.cs` — pure: takes a `SessionReportData`
(assembled by a `SessionReportReader` from LiteDB reads) and returns the HTML
string. IO wrapper writes `reports/session-<yyyyMMdd-HHmm>-<id>.html` beside
the LiteDB store. Purity keeps it fully testable off-game (Engineering
Standards: pure logic separable from I/O).

### Document shape (single file, self-contained)

- Head: inlined CSS reusing the dashboard's OKLCH token block (extract the
  token constants into a shared C# string both bundlers include — single
  source, no drift).
- Data: one inlined `<script>const REPORT = {...}</script>` JSON blob; a small
  inlined JS renderer (no fetch, no polling, no external refs — the
  self-contained check is a test).
- Sections, in the session's narrative order:
  1. **Header** — session date/duration/world context, modlist roster (n mods,
     fingerprint), profiler version, honesty badges legend.
  2. **KPI strip** — avg fps (real cadence), render fps, worst frame, realtime
     speed %, stalls (cause-split, pauses excluded and named), spikes.
  3. **Session ribbon** — the per-minute heat gradient (same data as the
     dashboard's minute-by-minute panel; SVG bars, colour = the severity ramp).
  4. **Per-mod cost** — table (update/draw split once S01 lands) + share donut
     (inline SVG); top-N with "+K more" row.
  5. **Moments** — worst spike clusters + real stalls with cause + context
     (boss/biome segment at the time).
  6. **Segments** — boss/event cards with per-segment cost summaries.
  7. **Memory** — session trend strip + phase verdict (once S04 guard lands).
  8. **Insights** — the session-end feed snapshot with confidence + data-
     strength badges, phrased exactly as the live feed (same producer).
  9. **Footer** — "descriptive, never normative" note + generation timestamp
     + profiler version.

Every number carries its badge (`this session` / `lifetime data`) — the
honesty contract travels into the artefact.

### Triggers

- Dashboard: `export report` button in the topbar (next to reset) →
  `/api/export-report` → writes file, returns `{path}`, toast with the path.
- Chat command: `/profiler-report` (matches the existing `/profiler-*` family).
- Config: `AutoExportHtmlReport` (default OFF) writes one at session end.

### Non-goals (v1)

No cross-session comparison pages, no report browser/manager UI, no PNG
export. The report renders one session; history lives in the dashboard.

## Test plan

- Pure-writer unit tests: golden fixture `SessionReportData` ⇒ assert section
  presence, badge presence, zero `http://`/`https://` refs, valid HTML shell
  (tag-balance smoke).
- Round-trip: synthetic session → LiteDB → reader → writer → Playwright loads
  the file:// output, asserts each section renders + screenshot for the visual
  record.
- Empty-session edge: thin session (< 30s) produces the honest "session too
  short for lifetime-grade numbers" note rather than confident stats.

## Acceptance

1. One command/click produces a file that renders correctly from `file://`
   with the network disabled (Playwright-verified).
2. Report numbers match the dashboard's for the same session (cross-checked in
   the round-trip test).
3. Every stat badged; stall section names pause exclusion; footer carries the
   honesty note.
