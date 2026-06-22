# Dashboard preview harness

Lets the agent (or you) **see the browser dashboard rendered as a PNG without
building the mod or running the game**, so frontend iteration is a tight loop
instead of build → reload → re-enter world → screenshot.

```sh
python3 tools/preview/render.py          # render current .cs edits, empty /api data
python3 tools/preview/render.py --live   # also pull real /api/* from the running mod
python3 tools/preview/render.py --tabs    # one screenshot per tab, not one tall printout
```

Output (ephemeral, flushed on reboot): `/tmp/pp-preview/shots/full.png` (or
`tab-<name>.png`). The assembled page lands in `/tmp/pp-preview/site/`.

## How it works

```
.cs string constants ──► extract ──► /tmp/pp-preview/site ──► loopback server ──► Chrome --headless ──► PNG
(Web/Assets/*.cs)        (un-double   (index.html +           (+ /api/*.json       (--screenshot)
                          @"" strings)  dashboard.css/js)       fixtures)
```

- **No build needed** — it reads the C# verbatim-string assets directly, so it
  reflects un-committed, un-built edits. The in-game **Build + Reload** stays the
  final compile + visual check.
- **Data**: `--live` curls the running mod's `/api/*` (game open, world loaded)
  for real numbers. Without it, surfaces show their empty states (good for layout
  and theme work).
- **Full printout vs tabs**: the default reveals every pane and unlocks the
  fixed-height scroll regions into one tall image; `--tabs` shoots each tab alone.

## Requirements

- Google Chrome at `/Applications/Google Chrome.app` (headless screenshot engine).
- `python3` (stdlib only — no pip installs).

## Notes

- Excluded from the `.tmod` via `build.txt` (`tools\*`), so it never ships.
- The Chrome-screenshot + static-serve core is project-agnostic; only `extract()`
  knows this project's C#-string asset layout. A project with real `.html/.css/.js`
  files would point Chrome straight at them and skip extraction.
