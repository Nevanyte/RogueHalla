# P-UI-LEFT-EDGE-1: the two panels on the left edge can be folded, and stay as the player left them

- **Serves:** owner direction 2026-11-22 in the lane's chat ("Please do a UI optimization sweep"); seen in Web round 1
  (2026-11-23): a table of nine gained stats that all read zero at the start of a match and the champion panel stay
  open on the left edge, about 400 x 200 and 340 x 170 pixels of a 1920 x 1080 window, and the quest tracker has to sit
  under the first. Not in ORDER_OF_OPERATIONS (UI rework lane). Supersedes `P-UI-BUILD-DRAWER-1` for the left edge.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` at 3a9513d02 (wave 3 and P-UI-CONTROLS-LINE-1 merged);
  the integration branch is `feature/unity-rework`. P-UI-PLAYER-WORDS-2 runs in another worktree and shares no file
  with this packet; P-UI-TILE-PLATES-1 runs after this packet and P-UI-TEXT-FIT-1 last.
- **D-rules touched:** D1: legacy shows both panels for the whole run and folds only "Cards & mob bonuses"
  (`packages/client/src/ui/buildHud.ts:103-120` hides its root only when there is no player or the HUD is not visible;
  `packages/client/src/ui/championCombatHud.ts:71-78` likewise; `packages/client/src/ui/hud.ts:110` is the one
  `<details>`). This adds a fold the player may use, default unchanged. D2 n/a. D5/D6 none (no wire, no stored game
  state). D9 untouched: a view preference, no key bound, no outcome decided.
- **Proposed DEVIATIONS row** (one row, numbered by the coordinator): legacy keeps both panels open (lines above);
  Unity folds each on a click, remembered on the device, open by default. Whether folded should be the default is the
  owner's question; it stays open, the code ships open (`GainsFoldedDefault`, `ChampionFoldedDefault` are the one line
  to change).

Units are HUD stage units (1 unit = 1 legacy CSS px; a 1920 x 1080 window is a 1422 x 800 stage, scale 1.35). Line
numbers below are on the base (3a9513d02), checked by the oracle author.

## 1. ID and outcome
Two folds on the left edge, open by default, remembered on the device:
the GAINED table of the plate (nine cells, all zero at the start of a match) folds into its header line
`GAINED · 3 ▼`, and the champion combat panel folds into its head row. Folding moves everything that hangs under
either panel (chip row, hunt card, quest tracker) in the same frame, nothing hops. When this lands: both headers are
real click targets (the gains one only as wide as its text), a click folds and unfolds with exact inverse rects, the
choice is stored under two PlayerPrefs keys by a click only, and a fresh HUD in play reads them once.

### What is wrong today
- `CreateBuildHud` (`UI/MatchHud.Legacy.cs:130-162`) builds the GAINED caption as a plain label (line 135) and nine cells
  that are always there; `SetPlateMode` (line 202-212) adds `BuildTop + 5 * BuildRowStep` (100) to the active height
  whatever they show: 148 (line 210). `ApplyBuildSummary` (`UI/MatchHud.Features.cs:375-397`) shows all nine whenever a
  summary exists, zeros too. The only fold on the left edge is `ToggleExtras` (`MatchHud.Legacy.cs:190-195`, cards and
  mob rows).
- The cells are raycast targets; the plate image and the caption are not. `CreateExtras` sets the `BuildDetails` image
  `enabled = false` (line 168): no hit area, so a header built like it is dead to the mouse.
- `CreateDamageReadout` (`MatchHud.Legacy.cs:216-244`) builds the champion panel 126 high: a 28 head, three rows of 32,
  and one `Button` over the card (raycast true, line 240-243) that only carries the tooltip. `ApplyChampionRows` flips
  rows 0 and 1 on and off by data.
- The chip row (`ShowTerrain`, `UI/MatchHud.Terrain.cs:125-173`) and the hunt card (`PlaceHuntPanel`, line 194-200) hang
  from `_vitalsPanel.sizeDelta.y`; the tracker (`TickPlayerFlows`, `UI/MatchHud.PlayerFlows.cs:154-162`) and the offer
  card (`PlaceOffer`, `UI/MatchHud.QuestRewards.cs:287-296`) read it through `PlateChainBottom()` (Terrain.cs:176-182).
  `PlateChainBottom()` and `PlaceHuntPanel()` read the cached `_terrainTop` and `_terrainHeight`, which only
  `ShowTerrain` writes (Terrain.cs:141), not the row's rect. `TrackerFloor()` (Terrain.cs:185-191) reads the constant
  `ChampionHeight` (126).

### The behaviour
1. **Two folds, open by default.** `UiDisplayPreferences` (`UI/UiDisplayPreferences.cs`) keeps them, beside `MapToggle`
   (a PlayerPrefs int; not `ShowcaseSettings`). It gets `public const bool GainsFoldedDefault = false,
   ChampionFoldedDefault = false;` and `public const string GainsFoldedKey = "rh.ui.plate.gainsFolded",
   ChampionFoldedKey = "rh.ui.champion.folded";`. Static `GainsFoldedStored`, `ChampionFoldedStored` read a key (the
   default when absent); static `StoreGainsFolded(bool)`, `StoreChampionFolded(bool)` write it (`SetInt` 1/0) and
   `PlayerPrefs.Save()`. `ResetDefaults` (line 59-65) leaves them alone (a layout memory, not a setting). Only a click
   writes.
2. **Gains header.** `GainedLabel` (path `BuildStrip/GainedLabel`) stays a plain container, now 14 high (was 12); its
   child `Label` (path `GainedLabel/Label`, character spacing 3 as today, still stretched over it) reads
   `LeftEdgeText.GainedHeader`: open `GAINED ▲`, folded `GAINED · 3 ▼` (3 of the nine are not zero). The glyphs are
   U+25B2 and U+25BC, the triangles `TerrainChips` draws; U+25B8 stays unused. The control is a new first child `HitBox`
   (behind `Label`): `HudUi.Surface(rect, Color.white, theme.CornerRadiusSmall, null, true)`,
   `canvasRenderer.cullTransparentMesh = false`, a `Button` on `HitBox` whose `targetGraphic` is that Image, with
   `HudUi.ApplyColors(button, UiTheme.With(theme.SurfaceHover, 0f))` (clear at rest, theme hover colour), default
   navigation (as `BuildDetails` and the cells) and a `HudIconHint` with `LeftEdgeText.GainedHint` (new: the header had
   none; the nine cells keep theirs; it centres over `HitBox`).
   **The hit area is a box, not the band.** Left click is the attack and a HUD rectangle that takes the pointer
   swallows it, so the target is as small as its purpose allows: `HitBox` is at the left of the 278 band (TopLeft,
   x 0, y 0, 0 wide until first measured), 14 high, `GainsHitWidth` wide: `ceil(max over
   LeftEdgeText.GainedHeaderForms() of Label.GetPreferredValues(form).x) + Label.margin.x + Label.margin.z` (the widest
   text the header can show at the current text size plus the label's own padding; `HudUi.Label` sets the margin to
   zero). The hover fill covers that box only; the rest of the band stays click-through.
   `SizeGainsHit()` (`MatchHud.LeftEdge.cs`) sets it, from `RefreshGainsHeader` (the header's text changed) and from the
   first line of `LayoutTerrainNote()` (`MatchHud.Terrain.cs:104`), which `ApplyTextScale` calls on a text-size change
   (`MatchHud.cs:133`, not edited). It measures only while `Label` is active in the hierarchy (else it sets
   `_gainsHitStale` and the next `RefreshGainsHeader` with the strip showing measures), returns at once when not stale
   and `Label.fontSize` equals the last measured size, and calls `HudUi.Place` only when the width differs. Never per
   frame. The box comes from the widest form, so a new count or a fold never moves it.
3. **Gains fold** (`_gainsFolded`). Folded: the nine cells inactive, `_buildBox` stays active (`GainedStripVisible`
   true), `BuildStrip` 15 high (100 open), `BuildDetails` y 36 (121), `ExtrasBody` y 52 (137). `SetPlateMode`'s active
   height is `5 + 18 + BuildTop + GainedBodyHeight() + 3 + 14 + (_extrasOpen ? 66 : 0) + 8`, the body 0 folded, 85
   open: plate 63 folded, 148 open (129 and 214 with the extras open): 85 units, 115 px at 1080p. Not-active heights
   are unchanged. The flag ignores `_gainsShown`: folded without a summary is 63; open keeps 148.
4. **Champion fold** (`_championFolded`). The three rows move under a new child `ChampionBody` (full-panel stretch, no
   graphic; rows keep their places and children). The head row becomes a control: child `HeadToggle`, created first
   (behind the labels, after the panel's own `Gloss` and `Frame`), built as in item 2 (`Surface` radius 6, `Button`,
   `ApplyColors` clear at rest, `cullTransparentMesh` false) without a hint (the panel's tooltip stays). It is inset 1
   unit on the left, the right and the top so that the panel's frame and its radius-7 corners (`HudUi.Surface(
   _championHud, ..., 7f, ...)`, Legacy.cs:220) stay visible around it: anchors (0,1) to (1,1), pivot (.5,1),
   `offsetMin.x` 1, `offsetMax.x` -1, `anchoredPosition.y` -1, `sizeDelta.y` 26 (y 1 to 27: it leaves the bottom unit of
   the folded panel too and keeps 26 when the panel shrinks). The head box shrinks from 130 to 106 wide (x 26 to 132;
   Legacy.cs:222); the weapon box moves from x 150 to 136 (92 wide, to 228; Legacy.cs:223). `HeadToggle` holds
   `FoldGlyph`, a 14 x 14 box (`HudUi.Box(toggle, "FoldGlyph", HudAnchor.TopRight, 7, 6, 14, 14)`: x 228 to 242, y 7 to
   21 of the panel) with a `HudUi.Label(box, "Label", glyph, theme.SizeTiny, theme.TextSecondary,
   TextAlignmentOptions.Center)`: `▲` open, `▼` folded. Folded: `ChampionBody` inactive, panel 28 high (126 open), bottom
   still 48, `_championHud` active. `TrackerFloor()` reads `_championHud.sizeDelta.y` (`ChampionHeight` while null):
   618 open, 716 folded.
5. **Nothing hops.** A click runs one method (`FoldGains` or `FoldChampion`): state, preference, own parts,
   `SetPlateMode(_plateActive)`, then `FollowPlate()` (new, `MatchHud.Terrain.cs`). `FollowPlate` does what
   `ShowTerrain` does before it places the hunt card (Terrain.cs:138-142): while `_terrainRow` exists and is active, it
   computes `top = PlateTop + _vitalsPanel.sizeDelta.y + 4` and, when that differs from `_terrainTop`, assigns
   `_terrainTop` and calls `HudUi.Place(_terrainRow, HudAnchor.TopLeft, PlateLeft, top, TerrainWidth,
   _terrainHeight)`; then it calls `PlaceHuntPanel()`. In the click frame the plate, cells, chip row and hunt card are
   in place; the tracker follows in that frame's `LateUpdate` (it reads `PlateChainBottom()` and `TrackerFloor()` every
   frame, writes only on a change): once, 85 units. An open offer card reads the same two values when `PlaceOffer`
   runs; the click does not touch it. The fold is instant: no tween, no time. The weapon bar, minimap, Quests button
   and headers do not move. The handlers do not clear the event system's selection (`onClick` also fires for a keyboard
   submit; clearing it would drop keyboard focus).
6. **Click target and pointer rules.** Both header images have `raycastTarget` true, so the click reaches the button
   and `PointerHitsUi()` (`BlocksPointerInput`, `MatchHud.Features.cs:47-56`) is true over them: `MatchClient.ReadInput`
   has no cursor and sends no aim, attack or move there, as over a gained cell today. The gains target is the `HitBox`
   of item 2 only; the champion panel is a raycast target already (its `Surface` and its `Button`,
   Legacy.cs:220, 240-243), so its header blocks no new area. The plate stays click-through. Keyboard-focusable exactly
   as the extras line is; no key is bound, `Input` is not read.
7. **Remembered, and hermetic.** `ApplyFolds(bool gainsFolded, bool championFolded)` (in `MatchHud.LeftEdge.cs`) calls
   `FoldGains(gainsFolded, false)` and `FoldChampion(championFolded, false)` (no write) and is what the tests call.
   `InitializeFeatures` (`MatchHud.Features.cs:130`), right after `_uiPrefs = new ...` (line 137), holds the one
   statement that reads the stored values: `if (Application.isPlaying) ApplyFolds(UiDisplayPreferences.GainsFoldedStored,
   UiDisplayPreferences.ChampionFoldedStored);`. EditMode never plays, so no EditMode rig depends on the PlayerPrefs of
   the machine it runs on (`MapToggle` is read from it by `new UiDisplayPreferences()`: no measured rect depends on that,
   not a pattern to copy). A rebind, `ResetTransientPresentation` (`MatchHud.cs:207`), `ShowMenu`, a summary and the
   per-frame refresh never reset a fold. No `Create*` method reads PlayerPrefs: the fields start false.
8. **Refit.** Unfolding ends with `MythicTextScale.Refit()` on each one under the nine cells and `ChampionBody` (once per
   click): a label measured while inactive may be stale after a text-size change. A steady frame, folded or not,
   allocates nothing and writes no rect: nothing new runs per frame.

### New files and members
`client-unity/RoguehallaNet/Net/Ui/LeftEdgeText.cs` (namespace `Roguehalla.Net.Ui`; synced copy and `.meta`):
```csharp
/// <summary>Pure text of the two left-edge folds.</summary>
public static class LeftEdgeText
{
    public const string GainedTitle = "GAINED", OpenGlyph = "▲", FoldedGlyph = "▼";
    public static string FoldGlyph(bool folded);                          // folded ? FoldedGlyph : OpenGlyph
    public static int GainedCount(IReadOnlyList<BuildCell> cells);        // cells with !Dim; null -> 0
    public static string GainedHeader(IReadOnlyList<BuildCell> cells, bool folded);
    public static string GainedHint(IReadOnlyList<BuildCell> cells, bool folded);
    public static IReadOnlyList<string> GainedHeaderForms();              // built once: 11 forms
}
```
`GainedHeader`: folded `GAINED · n ▼` (U+00B7, n invariant culture); open `GAINED ▲`. `GainedHeaderForms()`: the open
form, then the folded form for n = 0 to 9, in that order (11 strings; the widest text the header can show).
`GainedHint`, lines joined by '\n': `Gained from boosts, cards and farmed mobs`; folded only: `Label Value` per cell that is
not dim, or `Nothing gained yet`; last line folded `Click to show the table`, open `Click to fold the table`. Counting
by `Dim` (raw != 0): a raw 100 reads `+0` and counts as one; accepted.
`UI/MatchHud.LeftEdge.cs` (`public sealed partial class MatchHud`, `/// <summary>` above it) holds the fold state,
`public bool GainsFolded`, `public bool ChampionFolded`, `CreateGainsHeader(UiTheme)` (replaces the caption line of
`CreateBuildHud`), `CreateChampionHeader()` (first call of `CreateDamageReadout`), `FoldGains`, `FoldChampion`,
`ApplyFolds`, `GainedBodyHeight()`, `PlaceGainsParts()` (strip, cells, `BuildDetails`, `ExtrasBody`),
`RefreshGainsHeader()` (end of `SetPlateMode`; writes only on a change), `SizeGainsHit()` and its fields
(`_gainsHitStale`, the last measured size). Names are declared once (none exists in any `MatchHud*.cs` today).

## 2. Dependencies
The lane base `lane/ui-rework/base-3` (3a9513d02). `AllocationMeter` (`Assets/Tests/Editor/AllocationMeter.cs`, packets
P-UI-ALLOC-METER-1 and -2) is on it.

## 3. Relevant legacy paths
- `packages/client/src/ui/buildHud.ts:103-120`: `BuildHud.update` hides its root only when there is no player or the
  HUD is not visible; no fold (verified by reading).
- `packages/client/src/ui/championCombatHud.ts:71-78`: `update` hides the panel only when not allowed (alive and
  training or active phase); no fold.
- `packages/client/src/ui/hud.ts:110`: `extras = el("details", "hud-extras")`, the one fold legacy has ("Cards & mob
  bonuses", line 111).
Unity's fold is a deliberate addition (proposed DEVIATIONS row above).

## 4. New paths
- `client-unity/RoguehallaNet/Net/Ui/LeftEdgeText.cs`, its synced copy
  `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/LeftEdgeText.cs` (`sync-net.sh`) and `.meta`
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.LeftEdge.cs` and `.meta`
- `client-unity/RoguehallaNet.Tests/LeftEdgeTextTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/LeftEdgeFoldEditModeTests.cs` and `.meta`

## 5. Exclusive write scope
- `client-unity/UnityProject/Assets/Scripts/UI/UiDisplayPreferences.cs` (item 1)
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Features.cs` (the one statement of item 7)
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Terrain.cs` (`FollowPlate`, `TrackerFloor`, the first line of
  `LayoutTerrainNote`)
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Legacy.cs`: `CreateBuildHud` (the caption line), `SetPlateMode`,
  `CreateDamageReadout` (`ChampionBody`, head and weapon boxes); the file stays under 420 lines (373 today;
  `hud-seams.test.cjs:140-144` pins it)
- the new files of section 4 (everything except the oracle files below is the implementer's)
- `MatchHud.LeftEdge.cs` has its summary, at most 800 lines, no duplicate method name: run `node --test
  client-unity/UnityProject/Tools/hud-seams.test.cjs` through the wrapper.
- Stub-commit files that the implementer replaces: `LeftEdgeText.cs` and its copy, `MatchHud.LeftEdge.cs`,
  `UiDisplayPreferences.cs`.
- Not in scope: `MatchHud.cs`, the other `MatchHud.*.cs`, `HudTextScaleEditModeTests.cs` (TEXT-FIT), `AbilityTile.cs`,
  `BuildHudText.cs`, `SentencedHuntPanel.cs`, `QuestTrackerView.cs`, `hud-seams.test.cjs`, `CardDisplayCatalog.cs`.

## 6. Contracts and references
- R-AUTH / ADR 0008 / D9: presentation only, no game rule, no key bound. No wire, no `crates/`, no `packages/`.
- Theme colours only (`HudLegacyLayoutEditModeTests` checks every HUD graphic uses a theme colour: white, clear, state
  colours). Text through `HudUi.Label`.
- COMMON rules of the lane (readability gate: `/// <summary>` and at most 800 lines per new file).

## 7. Behaviour to preserve
- Default unfolded: every existing rect, count and path is unchanged on every rig that does not click or call
  `ApplyFolds` (the plate is 148, the champion 126, `TheReadoutStripSetsTheFloor` keeps 148 + 66 and the champion top
  800 - 48 - 126).
- `ToggleExtras` keeps its name and its `SetPlateMode(_plateActive)` (QuestTracker and QuestOffer tests call it).
- `_buildBox` stays active when folded (`BuildHudCellsEditModeTests` pins `GainedStripVisible`): fold the cells.
- `ApplyChampionRows` flips `_championRow[0..1]` by data: fold `ChampionBody`, never the rows. Never deactivate
  `_vitalsPanel` or `_championHud` (`TrainingHudRig.Panels` reads both).
- `ApplyLegacyViewport` (Legacy.cs:340-347) writes only x of both sizes.
- The weapon bar, minimap, Quests button and headers do not move on a fold.

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-UI-LEFT-EDGE-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` (through the wrapper) is green.
- `bash client-unity/sync-net.sh --check` is clean.
- The new `.meta` files are committed.

### Existing tests that pin this code (none turns red: the default is unfolded and no EditMode rig reads the stored values, item 7)
Read on the base; each is listed with what it asserts. All of them stay green on the stub (run, see "Validation").
- `BuildHudCellsEditModeTests` (`Assets/Tests/Editor/BuildHudCellsEditModeTests.cs:33,49,59,60`): `SetPlateMode(true)`,
  `GainedStripVisible` false before a summary and true after, cell texts equal `BuildHudText.Cells`, mob text; and the
  widest gains fit the value box. A pieces-only fixture (never `InitializeFeatures`).
- `ChampionPanelNumbersEditModeTests` (`:52,103,116`), `ChampionPanelNoGarbageEditModeTests` (`:44`),
  `MatchHudIconPresentationEditModeTests` (`:59,79-94`), `HudVisualFix2EditModeTests` (`:65,160-195`): `SetPlateMode`,
  `ApplyChampionRows` row texts, `_championRow` activeSelf by data, tooltips. Pieces-only.
- `HudLegacyLayoutEditModeTests` (`:175-203`): plate at left 12, top 10, width 294; champion left 12, bottom 48, width 250,
  height at most 150; nine cells 14 high, `BuildStrip` 278 wide, `BuildDetails` and `ExtrasBody/MobBonuses` exist.
- `HudVisualFixEditModeTests` (`:107`), `HudVisualFix4EditModeTests` (`:36,116`: "the live plate: its 148 unit height is what
  the chip row and the card sit under"), `HudPhasePillNarrowEditModeTests` (`:43`), `ToastLanesEditModeTests` (`:372`),
  `AbilityRowEditModeTests` (`:443`): measure the plate and the champion panel unfolded.
- `QuestTrackerEditModeTests` (`:63,183,408-417`): the tracker docks under the plate chain and follows `ToggleExtras`;
  `TheReadoutStripSetsTheFloor` pins the floor as the champion top minus 8.
- `QuestOfferEditModeTests` (`:640-671`): the offer card clear of tracker and champion panel, `PlateChainBottom` with the
  extras open and closed.
- `TrainingBannerEditModeTests` (`:51,85`) and `TrainingBannerNotesEditModeTests` (`:30,43`) through `TrainingHudRig`:
  plate height in the practice modes. `UiInputRoutingEditModeTests` (`:484`): `BuildDetails` as a control.
- `HudTextScaleEditModeTests`: a hand-written list of 60 labels, pinned by count; it lacks the new labels and stays
  green (`GainedHeading` finds `GainedLabel/Label`), as does `NoLabelOfTheMatchHudIsOptedOutOfTheSetting` (both new
  labels come from `HudUi.Label`).
- `client-unity/UnityProject/Tools/hud-seams.test.cjs` (`:140-144`): `MatchHud.Legacy.cs` under 420 lines.
- Flipping a default (`GainsFoldedDefault`) would amend `LeftEdgeFoldEditModeTests` test 1 and every test that assumes 148:
  it is the owner's question, not this packet's.

## Oracle files (read-only for implementers)
- `client-unity/RoguehallaNet.Tests/LeftEdgeTextTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/LeftEdgeFoldEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/LeftEdgeFoldEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-LEFT-EDGE-1.md`

The `stub:` commit holds what the implementer replaces: `RoguehallaNet/Net/Ui/LeftEdgeText.cs` (bodies return "", 0, an
empty list), its synced copy and `.meta`, `UI/MatchHud.LeftEdge.cs` and `.meta` (flags false, empty `ApplyFolds`), and the
consts, keys, readers (return the default) and empty writers in `UI/UiDisplayPreferences.cs`.

## 9. Validation commands
- `/Users/nevinmckinnon/Documents/Projects/RogueHalla/.agent-runs/lane-ui-rework/bin/in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter LeftEdgeText`
- `/Users/nevinmckinnon/Documents/Projects/RogueHalla/.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> LeftEdgeFoldEditModeTests`
- the same with the existing classes listed in section 8 (at least `BuildHudCellsEditModeTests`,
  `QuestTrackerEditModeTests`, `QuestOfferEditModeTests`, `HudLegacyLayoutEditModeTests`, `HudTextScaleEditModeTests`,
  `ChampionPanelNumbersEditModeTests`, `UiInputRoutingEditModeTests`)
- `.../in.sh <worktree> node --test client-unity/UnityProject/Tools/hud-seams.test.cjs`
- `.../in.sh <worktree> bash client-unity/sync-net.sh --check`
- `.../in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-LEFT-EDGE-1 --base lane/ui-rework/base-3`

## 10. Risk and suggested model tier
implementer-sonnet. A UI-layout change over four partial files with exact rects; the hard parts are the same-frame
follow (`FollowPlate`), the hit box width measured only on an active label, and keeping `MatchHud.Legacy.cs` under 420
lines. Reviewer: reviewer.

## 11. Known traps
- `ApplyChampionRows` flips `_championRow[0..1]` by data: fold `ChampionBody`, never the rows. Never deactivate
  `_vitalsPanel` or `_championHud`. `_buildBox` stays active when folded: fold the cells.
- `ToggleExtras` keeps its name; `BuildDetails` and `ExtrasBody` are placed by `PlaceGainsParts`, not by
  `CreateExtras`' constants. `SetPlateMode` runs from `ApplyBuildSummary`, `RefreshLegacyHud`, `ToggleExtras`:
  idempotent.
- Neither `Button` nor `Image` goes on `GainedLabel`, and it is not resized: `MythicTextScale` fits `Label` to 88 percent
  of its box, so a narrower box would shrink the text. The box is `HitBox`, a sibling of `Label`.
- `HudTestPaths.Find` falls back to a leaf-name search and can find a node under the wrong parent: the new tests use the
  exact path with `Transform.Find`.
- The `InitializeFeatures` statement is the only reader of the keys and the click the only writer: every test that
  clicks restores both keys in `TearDown`. No test covers font coverage of U+25B2 and U+25BC (the terrain chips draw
  them, Body font role): the look check is a Web round (folded plate, glyphs, hover fill, a real click on both headers).
- A text label that has never been active in the hierarchy measures at one tenth of its size: `SizeGainsHit` measures
  only while `Label` is `activeInHierarchy`; a test that says a text fits asserts that first.
- Time in EditMode does not advance: nothing here uses time.
- Capture tools copy layout maths (`HudLayout.MinimapSize`, `tabMapRect`): this packet changes neither.
- Not established (no EditMode test reaches): the `InitializeFeatures` statement (`Application.isPlaying` is false);
  a real pointer click on both headers, the hover fill, the champion header's corners over the panel's rounded frame, the
  glyphs on screen; what Space or Enter does on a selected header (if the module's Submit fires on it while an offer card
  is open, Enter may accept the offer and flip the fold); the header stays selected after a pointer click, so its hover
  tint and its tooltip stay until the next press elsewhere, as the extras line behaves today; the gains `HitBox` is a new
  place where an attack click is swallowed (the box, not the 278 band); where the offer card sits after a fold
  (`HudRegions.OfferCard` centres it unless that would touch the tracker, so it need not move).

## The oracle (what each test pins; the files are the source of truth)
dotnet `LeftEdgeTextTests` (namespace `Roguehalla.Net.Tests`, 4 tests): the count is the cells that are not Dim (null, empty
and `Summary()` 0; the eight-cell summary 8; one cell 1; raw 100 counts 1); the header (`GAINED · 2 ▼` folded, `GAINED ▲`
open, null and zeros folded `GAINED · 0 ▼`, glyph constants); the hint (heading, one `Label Value` per non-dim cell while
folded, `Nothing gained yet`, last line `Click to show the table` / `Click to fold the table`); the 11 header forms.
EditMode `LeftEdgeFoldEditModeTests` (13 tests; the full HUD of `QuestTrackerEditModeTests` on a real `MatchClient`;
`SetUp` saves, deletes and `TearDown` restores both PlayerPrefs keys; every find is `_hud.transform.Find(path)`;
a click is `ExecuteEvents.ExecuteHierarchy<IPointerClickHandler>` with a Left `PointerEventData` and must return the
header object itself). Numbers at the 1280 x 800 window:
1. `TheDefaultIsUnfolded`: flags equal the constants, both constants false, plate 148, champion 126, the nine cells
   switched on (`activeSelf`), the header reads `GAINED ▲`.
2. `TheGainsHeaderFoldsTheTableAndTheTrackerRisesInTheSameFrame`: tracker top 166 (set-up); a click: flag, plate 63,
   strip visible, cells hidden in the hierarchy, `GAINED · 3 ▼`, `BuildDetails` 85 higher, no keyboard block, both roots
   active; ONE `TickPlayerFlows` (no `Frame`): tracker top 81 (`ChainBottom() + 8`).
3. `AnotherClickRestoresTheFirstRectsExactly`: the rects of plate, `BuildStrip`, `BuildDetails`, champion, tracker, `HitBox`
   after two clicks of each header equal the first, within .01.
4. `TheChampionHeaderFoldsTheBodyAndKeepsTheRoot`: 126 high, bottom 48, floor 618; `HeadToggle` 1 inside on left, right,
   top, 26 high; a click: 28, bottom 48, root active, `ChampionBody` inactive, `_championRow[2]` still on, name and weapon
   label active with the same texts, glyph `▼`, floor 716, header still 1 inside left, right, top and 1 above the bottom.
5. `ARowRefreshAndASummaryDoNotUndoAFold`: after `ApplyFolds(true, true)`, `ApplyChampionRows(null)` and `(Summary())`
   keep rows 0 and 1 on by data but hidden, body inactive, panel 28; then a summary, `RefreshLegacyHud` and
   `ResetTransientPresentation`: plate 63, `GAINED · 3 ▼`, both flags true.
6. `TheClickWritesTheKeyAndTheStoredReadersReadIt`: defaults when absent; a click writes 1, the next 0, readers follow;
   `StoreGainsFolded(true)` alone moves only its reader; likewise the champion's.
7. `TheStoredValuesFoldAFreshHudThroughApplyFoldsAndNeverWrite`: stored true, fresh rig, `ApplyFolds(Stored, Stored)`:
   flags, plate 63 (no summary, strip not visible), champion 28; `ApplyFolds(false, false)`: 148 and 126; keys still 1.
8. `TheHeadersAreRealClickTargetsAndTheGainsOneIsOnlyAsWideAsItsText`: both Images enabled, raycast target, not culled
   when clear, `targetGraphic`, interactable, hit at the centre, click flips the flag; a point in the right of the old
   band is not a hit on `HitBox`.
9. `TheHeaderSaysWhatIsHidden`: `GAINED · 3 ▼` then `GAINED · 0 ▼` folded, `HudIconHint.Text` on `HitBox` equals
   `GainedHint(cells, true)`, `GAINED ▲` open.
10. `ASteadyFoldedFrameAllocatesNothingAndWritesNoRect`: folded plate 63 (set-up), `AllocationMeter.AssertNone` over a
    frame of `TickTerrain` plus the tracker view, then +7 on both heights survives three `Frame()` calls.
11. `TheChipRowAndTheHuntCardFollowOnTheClick`: no `Frame()`; chip row top 162; a click: 77; hunt card 4 under the
    row; `PlateChainBottom` is the card's bottom; one `TickPlayerFlows`: tracker 8 under the card; click again: 162.
12. `ARebuiltHudIgnoresTheStoredValuesInEditMode`: standing guard, GREEN on the stub on purpose (item 7).
13. `TheGainsHitBoxIsAsWideAsItsWidestHeaderAtTheCurrentTextSize`: width = `ceil(max preferred width over the 11 forms) +
    margin.x + margin.z` at text scale 1 and again at 1.4 (larger), 14 high, at the left of the band, under half the
    band, unchanged by a fold and a new summary.

## Notes from the oracle author
- Scratch run done (not committed): a copy of `LeftEdgeFoldEditModeTests` in which every assertion line was wrapped in a
  catch-and-collect helper (`AssertionException` kept, the test carried on, failed at the end with the list) and a
  missing path gave a dummy object (an Image with a Button, or a label for a `/Label` path) so the later lines ran.
  On the stub the run listed only assertions about the missing behaviour (missing `HitBox`, `HeadToggle`,
  `ChampionBody`; flags, heights, texts, keys, tracker and chip-row tops that did not move), and no other exception.
  One scratch-only defect was in the scratch's own dummy label, fixed in the scratch, not in the test.
- Set-up numbers re-derived on the stub: plate 148, champion 126, tracker top 166, chip row top 162, floor 618,
  `BuildDetails` top 136 open (51 folded expected), all as the spec says.
- Red on the stub (run through `unity-class.sh`, 12 red, 1 green): tests 2, 3, 6, 8, 11, 13 at the find of
  `GainedLabel/HitBox`; test 4 at the find of `ChampionCombatHud/HeadToggle`; test 5 at the find of `ChampionBody`;
  test 1 at the header text (`GAINED` for `GAINED ▲`); test 9 at the header text (`GAINED` for `GAINED · 3 ▼`);
  test 7 at `GainsFolded` true (false); test 10 at the set-up plate 63 (148). Test 12 is green by design.
  dotnet `LeftEdgeTextTests`: 4 of 4 red on assertions (a reference implementation written in a scratch copy passed all 4
  and was removed).
- The EditMode tests cannot see the real values of test 13's formula (the stub's forms list is empty) until the
  implementation exists; the formula is read from the packet, not measured.
- Deviation from the spec text: test 1 checks the cells with `activeSelf`, test 2 with `activeInHierarchy` (the strip is
  hidden without a summary, so the cells are not in the hierarchy in test 1); helper `AssertCells(active, why, ownSwitch)`.
  The champion header checks in test 4 use the panel's measured rect, not literals, for left, right and top.
- Existing classes run on the stub, all green: `BuildHudCellsEditModeTests`, `QuestTrackerEditModeTests`,
  `QuestOfferEditModeTests` (40), `HudLegacyLayoutEditModeTests`, `HudTextScaleEditModeTests`,
  `ChampionPanelNumbersEditModeTests` (73), `UiInputRoutingEditModeTests`, `TrainingBannerEditModeTests`,
  `HudVisualFix4EditModeTests` (37); `hud-seams.test.cjs` 14 of 14; `sync-net.sh --check` clean.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
