# tModLoader Integration Surface — UI System & Overlay

> Source: tModLoader.xml (tModLoader 1.4.4, build ~#5089). Serves components: UI Renderer (6).

## Summary

The overlay's *plumbing* — registering the F9 keybind, mounting a `UIState`, inserting a draw layer over live gameplay, and blocking gameplay input while the mouse is over a panel — is fully covered by the documented public modding API (`KeybindLoader`, `ModSystem.ModifyInterfaceLayers`, `ModSystem.UpdateUI`, `UIElement`, `Player.mouseInterface`). The overlay's *content* — every concrete widget the README draws (`UIPanel`, `UIText`, `UIScrollbar`, `UIList`, tab bar, foldable tree) — is built from public Terraria classes in `Terraria.UI` / `Terraria.GameContent.UI.Elements` that compile and link fine, but whose members carry **no `<summary>` text in `tModLoader.xml`**: only `UIElement` (40 members) and `UIList` (4 members) are documented. The nine-tab overlay is therefore buildable on the public API, but the colour-graded cost tree, sparklines, the 80x8 heatmap, and the retrospective card are all custom `DrawSelf` drawing onto the `SpriteBatch` — `UIElement.DrawSelf` is documented, the raw drawing it does is not, and font/text-measurement helpers (`FontAssets`, `DynamicSpriteFont`, `ChatManager`) are absent from this XML entirely. Net: ~all UI is buildable, but the *visual* layer leans on undocumented-here vanilla surfaces that need source/decompiler confirmation of exact signatures.

## The surface

| Fully-qualified member | Kind | What it does / why the profiler cares |
|---|---|---|
| `Terraria.UI.UIElement` | `[public-API]` type | Base class for every overlay widget. 40 documented members — the only UI primitive with real docs. |
| `Terraria.UI.UIElement.Append(Terraria.UI.UIElement)` | `[public-API]` method | Adds a child; children must be positioned within parent bounds. The whole nine-tab tree is built by `Append`. |
| `Terraria.UI.UIElement.DrawSelf(SpriteBatch)` | `[public-API]` method | "Used to draw this element (not the children). Use this to give an element custom visuals." The hook for graded bars, sparklines, heatmap. |
| `Terraria.UI.UIElement.Draw(SpriteBatch)` | `[public-API]` method | Calls `DrawSelf` then `DrawChildren`; override only when post-children logic is needed (keep `base.Draw`). |
| `Terraria.UI.UIElement.DrawChildren(SpriteBatch)` | `[public-API]` method | Draws children in append order. Append order = z-order within a panel. |
| `Terraria.UI.UIElement.Update(GameTime)` | `[public-API]` method | Per-frame update; recurses to children. Where a widget reads the ring buffer and refreshes. |
| `Terraria.UI.UIElement.OnInitialize` | `[public-API]` method | "Called before the first time this element is activated. Use this method to create and append other UIElement to this to build a UI." |
| `Terraria.UI.UIElement.OnActivate` | `[public-API]` method | Runs each time the owning `UIState` is set via `UserInterface.SetState`. Use to refresh on overlay toggle-on. |
| `Terraria.UI.UIElement.GetDimensions` / `GetInnerDimensions` / `GetOuterDimensions` | `[public-API]` methods | Resolved pixel rect. `GetDimensions` = mouse-interactible area; `GetInnerDimensions` = content area inside padding. Custom `DrawSelf` uses these to know where to draw. |
| `Terraria.UI.UIElement.Width` / `Height` / `Top` / `Left` (type `StyleDimension`) | `[public-API]` fields | Layout. Each is a `StyleDimension`. |
| `Terraria.UI.UIElement.MinWidth` / `MaxWidth` / `MinHeight` / `MaxHeight` | `[public-API]` fields | Clamp resolved size. |
| `Terraria.UI.UIElement.HAlign` / `VAlign` | `[public-API]` fields | Fractional alignment within parent inner dimensions. |
| `Terraria.UI.UIElement.PaddingTop/Left/Right/Bottom`, `MarginTop/Left/Right/Bottom` | `[public-API]` fields | Box-model spacing. `SetPadding(float)` sets all four. |
| `Terraria.UI.UIElement.IgnoresMouseInteraction` | `[public-API]` field | Set on pure-visual children (bars, sparklines) so they do not steal hover/click. |
| `Terraria.UI.UIElement.IsMouseHovering` | `[public-API]` property | True when the cursor is over this element. Drives hover tooltips on tree rows. |
| `Terraria.UI.UIElement.Parent` | `[public-API]` property | Walk up the tree (e.g. a fold-toggle finding its row). |
| `Terraria.UI.UIElement.LeftMouseDown(UIMouseEvent)` | `[public-API]` method | Overridable click entry. Fold a tree row, switch a tab, click the mode pill. |
| `Terraria.UI.UIElement.MouseOver(UIMouseEvent)` / `MouseOut(UIMouseEvent)` | `[public-API]` methods | Hover enter/leave. |
| `Terraria.UI.UIElement.OnMouseOver` / `OnMouseOut` / `OnUpdate` / `OnDraw` | `[public-API]` events | Event-style equivalents — attach a handler instead of subclassing. |
| `Terraria.UI.UIElement.CompareTo(object)` | `[public-API]` method | Sort key when an element sits inside a sorted container — relevant to the "sort ‹ consistent / drag ›" control on the Overview tab. |
| `Terraria.UI.StyleDimension` | `[public-API]` type | "Absolute pixel size, a percentage of the available space, or a combination." |
| `Terraria.UI.StyleDimension.Set(float, float)` | `[public-API]` method | `(pixels, percent)`. `Width.Set(200, 0f)` = 200 px; `Width.Set(0, 0.5f)` = 50 % of parent. |
| `Terraria.UI.UIState` | `[partial]` type | Root container mounted into a `UserInterface`. Public and usable; **no documented members in this XML** — referenced only via `<see cref>` from `IngameFancyUI` / `ModMenu`. |
| `Terraria.UI.UserInterface` | `[partial]` type | Owns one active `UIState`, drives its update/draw. **No documented members**; `UserInterface.SetState(UIState)` confirmed real via `<see cref>` only. |
| `Terraria.UI.UserInterface.SetState(Terraria.UI.UIState)` | `[partial]` method | Mounts a `UIState`; firing `OnActivate` down the tree. Cited by cref, never summarised. |
| `Terraria.UI.GameInterfaceLayer` | `[partial]` type | The unit of `ModifyInterfaceLayers`' list. Type named in the hook signature; **no own documented members**. |
| `Terraria.UI.LegacyGameInterfaceLayer` | `[needs-internals]` type | The concrete subclass a mod constructs to wrap a draw delegate. Not present in `tModLoader.xml` at all. |
| `Terraria.ModLoader.ModSystem.ModifyInterfaceLayers(List<GameInterfaceLayer>)` | `[public-API]` method | "Allows you to modify the elements of the in-game interface that get drawn." The insertion point for the overlay layer. |
| `Terraria.ModLoader.ModSystem.UpdateUI(GameTime)` | `[public-API]` method | "Ran every update and suitable for calling Update for UserInterface classes. Called on all clients." Where `UserInterface.Update` is driven. |
| `Terraria.ModLoader.ModSystem.PostDrawInterface(SpriteBatch)` | `[public-API]` method | Draws after interface; **explicitly deprecated** in its own summary in favour of `ModifyInterfaceLayers`. Listed for completeness — do not use. |
| `Terraria.ModLoader.KeybindLoader.RegisterKeybind(Mod, string, Keys)` | `[public-API]` method | Registers the F9 keybind; returns a `ModKeybind`. Default-binding overload (`Keys`). |
| `Terraria.ModLoader.KeybindLoader.RegisterKeybind(Mod, string, string)` | `[public-API]` method | Same, default binding given as a string. |
| `Terraria.ModLoader.ModKeybind` | `[public-API]` type | The handle returned by `RegisterKeybind`; poll it for press state. |
| `Terraria.ModLoader.ModKeybind.JustPressed` | `[public-API]` property | True on the frame the key goes down — the F9 toggle edge. |
| `Terraria.ModLoader.ModKeybind.JustReleased` / `Current` / `Old` | `[public-API]` properties | Release edge, held state, previous-frame state. |
| `Terraria.ModLoader.ModKeybind.GetAssignedKeys(InputMode)` | `[public-API]` method | The keys currently bound — for showing "press F9" hint text that respects rebinding. |
| `Terraria.ModLoader.ModPlayer.ProcessTriggers(TriggersSet)` | `[public-API]` method | "Use this to check on keybinds you have registered ... only called during gameplay. Called on the local client only." Where `JustPressed` is polled. |
| `Terraria.Player.mouseInterface` | `[public-API]` field | "If true, the mouse is currently overlapping with a user interface so any mouse interaction should be not be interpreted as gameplay input." Set true in a panel's `Update` to stop a click also swinging a weapon. |
| `Terraria.UI.IngameFancyUI` | `[public-API]` type | Fullscreen non-gameplay UI helper. **Not the right tool for this overlay** — it hides all other UI and locks the player out of play; see Plug-in points. |
| `Terraria.UI.IngameFancyUI.OpenUIState(Terraria.UI.UIState)` | `[public-API]` method | Shows a `UIState` fullscreen without managing a `UserInterface`. |
| `Terraria.Main.InGameUI` | `[public-API]` field | The vanilla `UserInterface` behind `IngameFancyUI`. "Used for non-gameplay in-game fullscreen UI which hide all other UI." |
| `Terraria.GameContent.UI.Elements.UIList` | `[partial]` type | Scrollable vertical list — candidate container for the mod-cost tree and tab content. 4 documented members. |
| `Terraria.GameContent.UI.Elements.UIList.Add(UIElement)` / `AddRange(IEnumerable<UIElement>)` | `[public-API]` methods | Append rows to the list. |
| `Terraria.GameContent.UI.Elements.UIList.Goto(UIList.ElementSearchMethod[, bool])` | `[public-API]` methods | Scroll to a matching element — e.g. jump to a mod row. |
| `Terraria.UI.UIElement.UIMouseEvent` (param type `Terraria.UI.UIMouseEvent`) | `[partial]` type | Click/hover event payload passed to `LeftMouseDown` etc. Named in signatures; no own summary. |
| `Terraria.Main.gameMenu` | `[public-API]` field | True in the main menus. Gate the overlay so it never draws on the title screen. |
| `Terraria.Main.playerInventory` | `[public-API]` field | True when the inventory is open — useful for layout/visibility decisions. |
| `UIPanel`, `UIText`, `UITextPanel`, `UIScrollbar`, `UIImage`, `UIImageButton` | `[needs-internals]` types | Standard `Terraria.UI` / `Terraria.GameContent.UI.Elements` widgets. **Zero documented members in `tModLoader.xml`.** They exist and are public (ExampleMod uses them), but exact constructor and method signatures must be confirmed against decompiled source. |
| `FontAssets`, `DynamicSpriteFont`, `ChatManager`, `Utils.DrawBorderString` | `[needs-internals]` types | Vanilla font assets and text-measure/draw helpers. **Absent from `tModLoader.xml` entirely.** Needed for the monospace-style overlay text and `MeasureString`-based layout. |

## Plug-in points

1. **The overlay mounts as a `UIState` driven by a mod-owned `UserInterface`.** Create a `UserInterface` instance in a `ModSystem`, build the overlay's root `UIState` (custom subclass), and call `UserInterface.SetState(state)` to mount it (toggle-off = `SetState(null)`). `UIState` and `UserInterface` are `[partial]` — public and used everywhere by ExampleMod, but `tModLoader.xml` carries no member summaries; `SetState` is confirmed only via `<see cref>`. **Do not** use `IngameFancyUI.OpenUIState` here: its own summary says it "hide[s] all other UI" and "the user can't play the game normally when active" — that violates the README's "drawn over live gameplay, not a separate window, no modal traps, Esc mid-fight" requirement. `IngameFancyUI` is `[public-API]` but architecturally wrong for this overlay.

2. **Drawing over live gameplay goes through `ModSystem.ModifyInterfaceLayers`** `[public-API]`. The hook hands you `List<GameInterfaceLayer>`; insert one custom layer whose draw delegate calls `UserInterface.Draw(...)` (or the `UIState.Draw`). Layer position controls z-order — insert after the vanilla HUD layers and before the cursor/mouse-text layer so the overlay sits above gameplay HUD but below the cursor. The concrete layer class to construct is `LegacyGameInterfaceLayer` (delegate + name + scale type), which is **`[needs-internals]`** — not in this XML; its constructor signature needs decompiler/source confirmation. `GameInterfaceLayer` itself is `[partial]` (named in the signature, no member docs). `ModSystem.PostDrawInterface` is `[public-API]` but its own summary deprecates it ("should no longer be used") — not an option.

3. **Per-frame update is driven from `ModSystem.UpdateUI(GameTime)`** `[public-API]` — its summary literally says it is "suitable for calling Update for UserInterface classes." Call `UserInterface.Update(gameTime)` here; that recurses `UIElement.Update` `[public-API]` through the whole tree.

4. **The F9 keybind** uses `KeybindLoader.RegisterKeybind(Mod, "ToggleOverlay", Keys.F9)` `[public-API]` at load, stored as a `ModKeybind` `[public-API]`. Poll it in `ModPlayer.ProcessTriggers(TriggersSet)` `[public-API]` with `ModKeybind.JustPressed` `[public-API]` to flip overlay visibility (`SetState` to the overlay state or `null`). `ProcessTriggers` is "only called during gameplay" and "on the local client only" — exactly the right scope for a client-side F9 toggle. `ModKeybind.GetAssignedKeys` lets tutorial copy show the *actual* bound key if the player rebinds.

5. **`Esc` dismissal** is **not** a custom keybind. `Esc` is owned by vanilla — when an interface state is active, vanilla's interface handling consumes `Esc` to close menus. The clean approach: detect that the overlay is showing and that `Esc`/menu-close intent fired, then `SetState(null)`. There is **no documented `tModLoader.xml` member** for "Esc was pressed while my UI is open"; the closest documented hook is polling raw input (`Microsoft.Xna.Framework.Input.Keys.Escape` via the standard XNA input path, not a tML-documented surface) inside `UpdateUI`/`ProcessTriggers`. Tag: **`[partial]`** — the toggle mechanism (`SetState`) is solid, but the specific "Esc closes my overlay without also opening the vanilla pause menu" interaction needs verification against vanilla input internals. **NEEDS DECOMPILER VERIFICATION** on whether a non-fullscreen mod overlay can suppress `Esc`'s default pause behaviour, or whether Esc-to-close must coexist with the pause menu.

6. **Gameplay-input suppression** (a click on a panel must not also swing a weapon) is done by setting `Player.mouseInterface = true` `[public-API]` inside the overlay's `Update` when the cursor is over an interactive element — its summary documents exactly this pattern: "UIElements that should block user interaction typically check `ContainsPoint(Main.MouseScreen)` in the Update method." Note `UIElement.ContainsPoint` is itself **`[needs-internals]`** (not documented in this XML, though clearly public and used by the cited pattern).

7. **The 5 Hz / 60 Hz refresh** is a mod-side throttle, not an API feature. `ModSystem.UpdateUI` fires every game update (~60 Hz). For Lite mode, run the *layout/data refresh* on a frame counter (every 12th update ≈ 5 Hz) while still calling `UserInterface.Update` each frame for input responsiveness; Standard mode refreshes data every frame. Cheap to implement; no API dependency. Tag: **`[public-API]`** (it is just arithmetic on `GameTime`).

8. **The mode pill click** is an ordinary `UIElement.LeftMouseDown` override (or an `OnLeftClick`-style handler) on a panel widget — `LeftMouseDown(UIMouseEvent)` is `[public-API]`. No special API; it mutates the mod's own mode state.

## Building the nine-tab overlay

| README UI element | Maps to | Assessment |
|---|---|---|
| Outer panel + title bar | `UIPanel` (or custom `UIElement` + `DrawSelf`) | `[needs-internals]` for `UIPanel` exact API; trivially replaced by a custom `UIElement` with a `DrawSelf` that draws a rounded/sliced background. **Custom drawing recommended anyway** — the README's "real palette, not the stock-tML look" quality bar means the stock `UIPanel` chrome will be overridden regardless. |
| Nine-tab tab bar | row of clickable `UIElement`/`UITextPanel` children | Buildable on `[public-API]` `UIElement` + `LeftMouseDown`. `UITextPanel` is `[needs-internals]` but a custom text button (UIElement + `DrawSelf` text) sidesteps it. Active-tab state is mod-side. **Clean.** |
| Tab content swap | `UserInterface.SetState` per tab, or one `UIState` swapping a child container | `[partial]` (`SetState`) or pure `[public-API]` (append/remove children, `RemoveAllChildren` is `[needs-internals]` but a custom container handles this). **Clean.** |
| Foldable mod-cost tree | nested `UIElement` rows; fold = append/remove child rows on click | Fully `[public-API]`: `Append`, `LeftMouseDown`, `Parent`. Row layout via `StyleDimension`. Double-click-to-drill needs click-timing logic mod-side (no documented double-click member — `[partial]`). **Clean, with mod-side double-click detection.** |
| Colour-graded cost bars (green→red) | custom `DrawSelf` drawing filled rects | **Custom drawing.** `DrawSelf` is `[public-API]`; the actual `SpriteBatch.Draw` of a 1x1 pixel scaled to a coloured rect uses XNA `SpriteBatch` + a white-pixel texture (`TextureAssets.MagicPixel` or `Main.magicPixel` — **`[needs-internals]`**, not in this XML). Mechanically simple, but the pixel-texture handle needs source confirmation. |
| Sparklines | custom `DrawSelf` — line segments or per-column rects | **Custom drawing**, same as bars: a loop of `SpriteBatch.Draw` calls over ring-buffer samples. No primitive exists; entirely `DrawSelf`. |
| 80x8 frame-time heatmap | custom `DrawSelf` — 640 coloured cells | **Custom drawing.** A double loop of small `SpriteBatch.Draw` rects. Cheap enough at 5 Hz; at 60 Hz it is 640 draw calls/frame — measure against Invariant 2, consider building the heatmap into a single `RenderTarget2D` or a small dynamic texture and drawing it once. |
| Session-retrospective card | `UIPanel`/custom panel + many `UIText`/custom text + bars | **Mostly custom drawing** for the polished look; text layout needs `MeasureString` (font helpers are `[needs-internals]`). The card is static once built — refresh once on session close, not per frame. |
| Scrollbars for long lists (94 mods) | `UIScrollbar` + `UIList` | `UIList` is `[partial]` (4 documented members incl. `Add`/`AddRange`/`Goto`); `UIScrollbar` is `[needs-internals]`. `UIList` has built-in scroll support and is the path of least resistance for the Full tree tab. **Verify `UIList` + `UIScrollbar` wiring against ExampleMod source.** |
| Monospace-style text everywhere | `FontAssets.MouseText` / `DynamicSpriteFont` + `ChatManager.DrawColorCodedString` or `Utils.DrawBorderString` | **`[needs-internals]`** — no font type appears in `tModLoader.xml`. Terraria ships no true monospace font; the "monospace-style" look means manual fixed-advance glyph layout in custom `DrawSelf`, which requires `MeasureString`. This is the single biggest undocumented-here dependency for the visual layer. |

**Verdict on the key question:** the nine-tab overlay is architecturally buildable on the public API — mounting, layering, input, and the widget tree are all `[public-API]` or `[partial]`-but-real. But every *distinctive visual* the README specifies (graded bars, sparklines, the heatmap, the polished card, monospace text) is custom `DrawSelf` rendering, and that rendering depends on vanilla drawing surfaces (`SpriteBatch`, the magic-pixel texture, `FontAssets`/`DynamicSpriteFont`, `MeasureString`) that `tModLoader.xml` **does not document**. The overlay is not blocked, but the UI Renderer is a *custom-drawing* component, not a compose-stock-widgets component.

## Invariant checks

- **Invariant 1 — Read-only.** Fully satisfied and structurally easy here. Every member used by the UI Renderer is a *draw* or *input-read* surface. The one write the overlay performs — `Player.mouseInterface = true` — is a vanilla-sanctioned UI convention (its own summary prescribes it) that suppresses *the player's own* click being double-counted as gameplay; it changes no game state, save, world, or other mod. `ModifyInterfaceLayers` only *adds a draw layer* — it does not remove or reorder vanilla gameplay. No risk.
- **Invariant 2 — Overhead budget.** The refresh throttle (point 7) is the primary control: Lite mode refreshes overlay *data/layout* at 5 Hz, keeping per-frame cost near zero when the data is unchanged. The watch items: (a) the 80x8 heatmap = 640 `SpriteBatch.Draw` calls — fine at 5 Hz, must be measured at 60 Hz and likely pre-rendered to a texture; (b) `MeasureString` per text element per frame is wasteful — measure once on data change, cache the layout; (c) `UIElement.Update` recurses the whole tree every frame even when nothing changed — for a 94-mod tree this is real work, so collapse off-screen/folded subtrees out of the tree (do not just hide them). None of this is hot-*game*-path (it is UI-thread frame work), but it still counts against the overhead the profiler reports about itself, so the same measurement discipline applies.
- **Esc / no modal trap.** The overlay must use the `ModifyInterfaceLayers` + mod-`UserInterface` path, **not** `IngameFancyUI`, precisely so it never becomes modal — `IngameFancyUI` would lock the player out of gameplay and contradict "Esc always dismisses, even mid-fight."

## Coverage verdict

The UI Renderer's **structure and integration** is ~95 % buildable on the documented public API:

- `[public-API]` and solid: F9 keybind (`KeybindLoader`, `ModKeybind`, `ProcessTriggers`), the layer insertion (`ModifyInterfaceLayers`), the update pump (`UpdateUI`), the entire `UIElement` widget model (layout, append, events, `DrawSelf`), `StyleDimension`, input suppression (`Player.mouseInterface`), menu gating (`Main.gameMenu`).
- `[partial]` but real: `UIState` / `UserInterface` / `UserInterface.SetState` / `GameInterfaceLayer` / `UIList` / `UIMouseEvent` — public, used universally by ExampleMod, but `tModLoader.xml` carries no member summaries, so signatures come from ExampleMod source rather than from these docs.
- `[needs-internals]`: the stock widget *implementations* (`UIPanel`, `UIText`, `UITextPanel`, `UIScrollbar`, `UIImage`, `LegacyGameInterfaceLayer`, `UIElement.ContainsPoint`/`RemoveAllChildren`) and the entire **drawing substrate** the visual design depends on — `SpriteBatch` raw draw, the magic-pixel texture, `FontAssets`/`DynamicSpriteFont`, `MeasureString`/`ChatManager`/`Utils.DrawBorderString`.

So: nothing in the UI Renderer is *blocked*, but the component is mis-described if called "public-API only." The honest framing — the overlay *shell* is public-API; the overlay *paint* (every graded bar, sparkline, heatmap cell, and the retrospective card) is custom `DrawSelf` against vanilla drawing types this XML does not document. Building it needs ExampleMod source and a decompiler open alongside `tModLoader.xml`, not `tModLoader.xml` alone.

## Open questions / NEEDS DECOMPILER VERIFICATION

1. **`LegacyGameInterfaceLayer` constructor signature** — the concrete class wrapping a draw delegate for `ModifyInterfaceLayers`. Not in `tModLoader.xml`. Need: `(string name, Func<bool> drawMethod, InterfaceScaleType scaleType)` confirmed, and the correct `InterfaceScaleType` value for an overlay (likely `UI` so it follows the player's UI-scale setting). **NEEDS DECOMPILER VERIFICATION.**
2. **Esc-to-dismiss without triggering the vanilla pause menu** — can a non-fullscreen mod overlay consume `Esc` so it closes the overlay *instead of* opening the pause menu, or do both fire? If both fire, the README's "Esc always dismisses" still works (overlay closes) but the pause menu also appears, which is a UX wart. **NEEDS DECOMPILER VERIFICATION** of vanilla `Esc`/`Main.menuMode` input ordering.
3. **The monospace-text approach.** Terraria ships no monospace font in `FontAssets`. Confirm the available fonts (`MouseText`, `DeathText`, `ItemStack`, `CombatText`) and decide: manual fixed-advance glyph placement (true monospace look, more `DrawSelf` work) vs. proportional with column alignment via `MeasureString`. **NEEDS DECOMPILER VERIFICATION** of `FontAssets` contents and `DynamicSpriteFont.MeasureString` signature.
4. **The white-pixel texture for rect drawing** — `TextureAssets.MagicPixel` vs `Main.magicPixel` vs allocating a 1x1 texture. Needs source confirmation of which is the current 1.4.4 surface. **NEEDS DECOMPILER VERIFICATION.**
5. **`UIList` + `UIScrollbar` wiring** — `UIList` is `[partial]` (has `Add`/`Goto`) but the scrollbar attachment (`UIList.SetScrollbar` or equivalent) is undocumented here. Confirm against `ExampleMod` UI examples whether `UIList` is performant for a 94-mod foldable tree or whether a custom virtualised list is warranted.
6. **Heatmap render cost at 60 Hz** — open until the Milestone 1 spike measures 640 per-cell `SpriteBatch.Draw` calls vs a pre-rendered `RenderTarget2D`. Not an API gap; an Invariant-2 measurement gap.
7. **Double-click detection** — no documented double-click member on `UIElement` (`LeftMouseDown` is single). "Double-click to drill into a hook" needs mod-side click-timing. Confirm whether a `UIElement.OnLeftDoubleClick`-style member exists in 1.4.4 source. **NEEDS DECOMPILER VERIFICATION.**
8. **Steam-controller input** — the README claims `UIElement` gets controller input "for free." `UIElement`'s documented members show only mouse events (`MouseOver`, `LeftMouseDown`); controller navigation is handled by vanilla `UILinkPointNavigator`, **absent from this XML**. Confirm whether mod `UIElement`s auto-participate in controller navigation or must register link points. **NEEDS DECOMPILER VERIFICATION.**

---

## How we plug in (post-implementation status)

> [!important] The in-game overlay was archived in v0.9.0.
> The F9 keybind is now `"OpenDashboard"` and opens the default browser to the loopback dashboard (`systems/web-dashboard.md`); it no longer toggles an overlay. The overlay shell + paint described below are preserved as the record of how the (now-archived) overlay plugged into the tModLoader UI surface, for a possible Steam-Deck / handheld revival. `ProfilerOverlaySystem` (in `UI/`) today owns only the keybind registration. See `systems/overlay.md`.

The 2026-05-19 analysis verdict was "the overlay shell is public-API; the overlay paint is custom DrawSelf against vanilla drawing types this XML does not document." That is exactly what was built (and later archived).

### The current keybind (live)

`ProfilerOverlaySystem : ModSystem` (`UI/ProfilerOverlaySystem.cs`) registers `KeybindLoader.RegisterKeybind(Mod, "OpenDashboard", "F9")` at `PostSetupContent`, stored as `DashboardKeybind`. `ProfilerPlayer.ProcessTriggers` polls it and launches the default browser at the dashboard URL (`open`/`xdg-open`/shell). Local client only (per `ProcessTriggers`' tModLoader documentation).

### The archived overlay shell (historical, for revival)

When the overlay was the player surface, `ProfilerOverlaySystem` also owned:

- `KeybindLoader.RegisterKeybind(Mod, "ToggleOverlay", Keys.F9)` → stored as the toggle keybind.
- `ModifyInterfaceLayers(List<GameInterfaceLayer> layers)` → inserts a `LegacyGameInterfaceLayer` whose draw delegate calls `OverlayPanel.Draw`.
- `UpdateUI(GameTime)` → drives the mod-owned `UserInterface.Update` while `OverlayState.Visible` is true.
- `ToggleVisibility()` → flips `OverlayState.Visible`.

This shell still exists in `UI/` but is not in the player path.

### The paint

`OverlayPanel.Draw(SpriteBatch, MetricCollector?, IOverlayTab)` (`UI/Overlay/OverlayPanel.cs`) owns the chrome and dispatches the active tab. The chrome draws:

- Background and rounded panel via `OverlayDraw.Rect` (filled rectangles on the magic-pixel texture).
- Header strip (mod name, frame-time NOW vs 30s avg pill, LIVE/PAUSED, CPU/MEM/BOTH metric pill).
- Tab strip — iterates `TabRegistry.Visible(collector)` and renders each tab's `Label`.
- Stats line (entity counts, alloc bytes/s, hook count).
- PROFILER HEALTH bar — fed by `HookCoverageView.MeasuredHooks() / TotalHooks()`.

Then the active tab's `Draw(sb, area, collector)` renders its content area below `OverlayLayout.DividerOffset`.

### IOverlayTab contract

`UI/Overlay/IOverlayTab.cs` defines a six-member contract: `Label`, `IsAvailable`, `Tick`, `MeasurePanelHeight`, `Draw`, `HandleClick`, `HandleScroll`. Each tab is a singleton instance in `TabRegistry.Tabs`; the order in the list is the order in the tab strip.

`TabRegistry.Visible(collector)` enforces `IsAvailable` — a tab returning false hides from the strip and receives no input dispatch. The audit (`plans/code-health-audit/overlay-ui.md`) found that pre-fix the chrome ignored `IsAvailable`; the post-fix routes all chrome paths through `Visible` / `ResolveActive`.

### Esc dismissal

Today F9 toggles visibility. Esc is not specially handled; tModLoader's vanilla pause-menu handling consumes it. The 2026-05-19 analysis's "Esc-to-dismiss without pause" concern remains open but is low-priority — the player can dismiss with F9.

### Drawing primitives

`OverlayDraw` (`UI/Overlay/OverlayDraw.cs`) wraps the vanilla drawing surface:

- `OverlayDraw.Rect(sb, x, y, w, h, color)` — uses `TextureAssets.MagicPixel.Value` (resolved at first call).
- `OverlayDraw.Text(...)` family — uses `FontAssets.MouseText.Value` (Terraria's primary UI font).
- `OverlayDraw.Truncate(text, maxWidth, font)` — fits a string to a pixel budget via `font.MeasureString`.

The "monospace style" of the overlay is layout-by-column-width, not a true monospace font (Terraria ships none). Numeric columns are measured once and rows align to the widest cell.

### Truncation caches

Per the audit fix in commit `aa914ce`: `OverviewTab._truncatedNames` (`Dictionary<int, string>` keyed by ModId) and `InsightsTab._rankedBodies` (`List<string>` parallel to `_ranked`) cache truncated row labels at the 1 Hz Tick cadence. No per-frame `OverlayDraw.Truncate` allocations on those paths.

### Input suppression

`Player.mouseInterface = true` is set while the cursor is over the overlay panel (`OverlayPanel.Draw`'s hover branch). Vanilla-sanctioned UI convention; suppresses the player's own click being interpreted as gameplay.

### Canonical home

`systems/overlay.md` carries the implementation reality including the five tabs, the 1 Hz refresh discipline, and the truncation caches.
