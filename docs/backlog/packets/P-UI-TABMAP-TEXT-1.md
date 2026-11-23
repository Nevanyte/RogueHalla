# P-UI-TABMAP-TEXT-1: every camp's text on the Tab map lies inside the map and on no fixed mark

- **Serves:** not in the plan: owner direction 2026-11-22 in the UI rework lane chat ("When tab is clicked map layout is weird"). Seen in Web round 2 (2026-11-23): a camp's text near the right edge is cut (AUG reads AUC, +0.75% loses its end) and the texts of neighbouring camps print over each other and over neighbouring marks.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (head `be8da07a0`; it holds P-UI-ZONE-NAMES-1-fix, P-UI-TILE-PLATES-1 and P-UI-JOURNAL-DETAILS-1; no map file differs from `e165db46d`). Lane worktree `/Users/nevinmckinnon/Documents/Projects/RH-ui-a`. Implementer gate: `scripts/gate.sh --quick --oracle packet/P-UI-TABMAP-TEXT-1 --base lane/ui-rework/base-3`.
- **D-rules touched:** D1: legacy never puts text beside a mark. A camp's reward is one opaque pill centred on the camp (`packages/client/src/ui/planning.css:231-261`, `planning.ts:176-194`) and `crowdedChips` hides a chip that meets another marker, elites first, then index order (`packages/client/src/ui/planningMath.ts:177-195`, the order used here). Presentation only. DEV-022 (`docs/DEVIATIONS.md:48`) names one buff glyph per camp plus an elite marker, not the side the amount stands on: propose no row. D2 n/a. D5/D6 none. D9 untouched.

## 1. ID and outcome

Units are layer units (the marks object's space, y up; drawn k times larger). s = 1.2. R is item 5's size.

**What is wrong today.** `RefreshBadgeTexts` (`client-unity/UnityProject/Assets/Scripts/UI/TacticalMapMarks.cs:414-438`) puts a camp's amount ("+3%") and elite tag ("Aug") at `RewardSlot(p, row) + (8, 0)` (`:425-426`, `RewardSlot` at `:386`): left edge 21.2 layer units right of the camp point, centre 2 up, the tag one row (14.4) above the amount, left aligned, whatever the edge or a neighbour holds. The texts are pooled children of the marks graphic (`BadgeText`, `:390-400`, box 40 x 14); one that crosses the layer edge meets the next sibling, an opaque rail 16 HUD units away, and is cut there. A text is 15 to 27 units wide and camps stand about 27 apart, so it covers the next plate and text. `ContactNear` (`:281-286`) skips a camp's drawing while a rival dot is within 8 units, which shifts which text a pool label holds. `Measure` (`TacticalMapMarks.ZoneLabels.cs:255-265`) on a cache miss gives zero and stores nothing for a label not active in the hierarchy. `BuildZoneMarks` (`TacticalMapMarks.ZoneLabels.cs:180-246`) lists a camp's texts at the right slot (`:213-214`).

**Outcome (11 rules).**

1. **One block per camp with text.** A row of `_camps` (index order) with `AmountText` or `EliteText` has a block of 1 or 2 lines, the amount below and the tag above (14.4 apart), placed together. The block exists whether or not the text is drawn: `ContactNear` skips only the drawing. `Large` only. `W` the wider line, `LineH` one line.
2. **Four places, tried in this order: Right, Below, Above, Left.** (px, py) the camp point, `H = LineH + (Lines - 1) x 14.4`. Right `(px + 21.2, py + 2 - LineH/2, W, H)` (today's place); Below `(px - W/2, py - belowGap - H)`; Above `(px - W/2, py + 21)`; Left `(px - leftGap - W, same y as Right)`. The caller computes the gaps from item 6: `leftGap = 21.2 + w888 + 2` (clear of the ward countdown reserve) and `belowGap = 16 + h888/2 + 2` (clear of the clock box). Left is last: it stands about 38 units from its mark. Worked with leftGap 38.1, belowGap 24, W 15.02, LineH 12.14, camp (240, 240): Right (261.2, 235.93), Below (232.49, 203.86), Above (232.49, 261), Left (186.88, 235.93), each 15.02 x 12.14; belowGap 27.1 puts Below at y 200.76. Lines 2: H 26.54, Below y 189.46; lines centred at rect.Y + LineH/2 and + 14.4 (242 and 256.4 at Right, today's). Both gaps grow with R (estimates: R 9 gives 38.1 and 24.07; text scale 1.5, R 13.5, gives about 45.6 and 27.1), so Below stays clear of the clock box.
3. **The choice.** A place is taken when its rect lies inside the map rect inset by `Margin` 2 (within .001) and overlaps no obstacle and no block placed earlier (both sides of the intersection above .001; touching is free). Order of placement: elite camps first, then the others, each in index order. No place free: the place inside the inset with the least summed overlap area, ties within .001 to the earlier place. None inside (a map smaller than a block; the smallest layer is 75, so only the dotnet tests reach it): the least area outside. Bounds not finite or not above 0 (a layer not laid out): every block Right. Placement is a pure function of blocks, obstacles, bounds, leftGap and belowGap: the same inputs give the same places on every call, in any obstacle order.
4. **Obstacles are fixed boxes, present whether or not the thing is live.** NOTHING MAY HOP (lane ruling): a camp text keeps its place from frame to frame, across closing and opening the map, when something arrives or leaves, and when the terrain map object is replaced mid-match (item 7); it moves only when the map's camps (or their words), size or the text size change. Per camp, with text or not: the plate (20.4 square if `IconKey` is set, else 11.2), the elite diamond (11.2 square at p + (0, 13.5), elite only), the ward ring (13.6 square at p + (-13.2, 2)), the ward countdown box (right edge p.x - 21.2, centre p.y + 2), the clock box (centred p + (0, -16)), the chest-seconds plate (31.2 x 14.4 centred p + (0, -15): 3 characters as `OnPopulateMesh` draws them, fixed on purpose so a digit moves nothing); the countdown and clock boxes are `w888` x `h888`. Per found zone: its icon, 26 square at `ZoneAt`. NOT obstacles: chests, chest rows, delivered points (eggs, quest marks, merchants, givers, objectives, quest drops), the moving marks (rivals, rings, ping, route, marked holders, carriers), chips, zone names: a text may stand over one for a moment.
5. **One reference size R, one measuring label.** Every block text and "888" is measured on ONE dedicated label `CampTextMeasure`, a child of the marks object: `HudUi.Label(transform, "CampTextMeasure", "", 9f, ...)`, then `MythicTextScale.FitBox = false` and `Refit()`, so its `fontSize` is R whatever it holds: 9 x clamp(scale, .85, 1.5) of the scale last applied to it (`HudUi.Label` applies the settings' scale at creation, `MythicUi.ApplyTextScale` reaches it later). **R is read from `CampTextMeasure.fontSize`, never from `ShowcaseSettings`** (the tests change the size only through `ApplyTextScale(Panel, ...)`, which leaves the setting alone). Before the label exists R is unset in the signature (item 7), so the first pass runs and creates it, awake (a pass runs only while the marks object is active in the hierarchy; a label made under a closed panel measures a tenth, COMMON.md). Its text stays empty (`Measure(label, text)` passes the text). Drawn labels keep their own fit, at most R: a drawn text may be narrower than its block, never wider.
6. **`w888`, `h888`** = `Measure(CampTextMeasure, "888")`: three digits, the widest a ward countdown (digits only on the Tab map), a clock (respawn 180 s) or a chest second shows; about 15 x 12 at R 9 (an estimate). The pass computes the two gaps of item 2 from them.
7. **When it runs.** `PlaceCampTexts(force)` runs in `Present` directly before `RefreshBadgeTexts`, and in `LayerChanged` (forced, then `RefreshBadgeTexts()`). **`SetMap` forgets the blocks and the signature only for a null map** (`Bind` always passes null first): `if (map == null) <clear both>`. For a new non-null map object (the server reports destroyed terrain, `Collections.Apply` builds a map with the same camps through `MapBlob.WithDestroyedTerrain`, `TacticalMapController.Tick` hands it to `SetMap`) it keeps both and the signature decides, so no camp text moves; its forced zone pass (`RefreshZoneLabels(Forced)`, after `ResetZoneSpots`) then reads the kept blocks through `BuildZoneMarks` (item 10), the same boxes as before. A call does nothing (no pass, no count, no label) when the marks object is not `Large` or not active in the hierarchy; with no `_camps` row with text it clears its blocks and signature and does nothing more. The large marks are presented only while the panel is open, so every Present pass is awake; a `LayerChanged` while closed places nothing, and the first Present after opening places, as the signature then differs or is unset. The signature, compared exactly in `Present`: the row count; per camp row its world X and Y, `AmountText`, `EliteText`, `Elite`, `IconKey != null`; the layer rect width and height; k (`localScale.x`); R (the measuring label's `fontSize`; unset while it does not exist); and that the last pass had no unmeasured block. A pass runs when it differs or when forced, and adds one to the public `CampTextPlacements`; a steady Present, a clock step, a rival, a ward, a chest, a giver, a quest drop, a ping or a replaced terrain map runs none. The signature is stored only after a pass that measured all.
8. **Measured sizes.** A line is `Measure(CampTextMeasure, text)`. `Measure` is not edited: an awake label measures a non-empty text above 0, and a block's texts are never empty. A width or height not above 0 makes the block unmeasured: every block of the pass is Right with a zero rect `(px + 21.2, py + 2, 0, 0)`, the signature is not stored, the next Present measures again (not seen in a match).
9. **Drawing.** `ShowBadgeText` (`TacticalMapMarks.cs:402-411`) takes `int align` (-1 left edge at x, 0 centred, 1 right edge at x; wards pass 1, `:434`) in place of `bool leftOfPoint`: pivot x 0, .5 or 1, alignment Left, Center or Right. A camp's lines go to x = rect.X (Right), rect.Right (Left), rect.X + W/2 (Below, Above), centre y per item 2. Clocks and countdowns stay. A row with text and no block (a `Present` while the marks object is not awake, before any pass) draws nothing and uses no pool label; the drawing loop guards the row index against the stored block count.
10. **Zone names.** In `BuildZoneMarks` the two statements adding a camp's amount and tag at the right-hand slot (`TacticalMapMarks.ZoneLabels.cs:213-214`) become one: the camp's placed rect, when its width is above 0. The rest of the zone pass and `Measure` stay.
11. **No label is created early** (a bare marks object holds exactly six text labels, the minimap none, and no `CampTextMeasure` or `CampBadgeText` exists before a pass has a block), and a steady Present and a forced pass allocate nothing (pooled lists and arrays, `for` loops). New public reads: `int CampTextPlacements`, `int CampTextPlace(int campRow)` (-1: no block), `Rect? CampTextRect(int campRow)` (null: no block; x, y the bottom-left corner, as `ZoneLabelRect`).

### New library file `client-unity/RoguehallaNet/Net/Ui/CampTextPlacement.cs` (namespace `Roguehalla.Net.Ui`, uses `MapRect` of `MapScreenLayout.cs:6`)

```csharp
public struct CampTextBlock   // X, Y: the camp point, layer units, y up; W, LineH: widest line, one line's height
{                             // (0 or not finite: not measured); Lines: 0 none, 1 or 2 (amount below, tag above)
    public float X, Y, W, LineH; public int Lines; public bool Elite;
}
public static class CampTextPlacement
{
    public const int None = -1, Right = 0, Below = 1, Above = 2, Left = 3, Places = 4;   // the order of trying
    public const float Margin = 2f, RightGap = 21.2f, AboveGap = 21f, LinePitch = 14.4f, Tolerance = .001f;
    public static MapRect RectOf(int place, CampTextBlock block, float leftGap, float belowGap);   // item 2
    // blocks in map index order; the buffers are the caller's (least of the three lengths written; null: nothing)
    public static void Place(IReadOnlyList<CampTextBlock> blocks, IReadOnlyList<MapRect> obstacles, MapRect bounds,
        float leftGap, float belowGap, int[] places, MapRect[] rects);
}
```
Every entry starts None; Lines 0 stays None with a zero rect. A zero rect is no obstacle for later blocks; a block with W or LineH not above 0 or not finite (or a `leftGap` or `belowGap` not above 0 or not finite) is Right with item 8's zero rect. Open choices, recorded: Margin 2, the gaps, the order of places, touching is clear, the reserves. The stub (committed here) has the whole surface: `RectOf` returns a zero rect, `Place` writes nothing.

## 2. Dependencies

`lane/ui-rework/base-3` (see Integration base). Follows P-UI-ZONE-NAMES-1 and its fix in the same two files. P-UI-TEXT-FIT-1 runs after this one (disjoint label creations). P-UI-MAP-PAINT-1 will change how often the map repaints: `CampTextPlacements` counts passes, never Presents or repaints, and no test pins a repaint rate.

## 3. Relevant legacy paths
- `packages/client/src/ui/planning.css:231-261`: `.pl-camp-rewards` is one flex row centred on the camp (`left/top 50%`, `translate(-50%,-50%)`), `.pl-camp-reward` an opaque pill (verified by reading).
- `packages/client/src/ui/planning.ts:176-194`: `campRewardBadge` and `eliteChestBadge` build the pills: amount `+N` (and "Aug" for elites) inside the badge.
- `packages/client/src/ui/planningMath.ts:177-195`: `crowdedChips` hides a chip that meets another marker or a shown chip, elites first then index order; the order of placement of this packet.
- Unity today: `TacticalMapMarks.cs:386` (`RewardSlot`), `:414-438` (`RefreshBadgeTexts`), `TacticalMapMarks.ZoneLabels.cs:180-246`.

## 4. New paths
- `client-unity/RoguehallaNet/Net/Ui/CampTextPlacement.cs` (library), its synced copy `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/CampTextPlacement.cs` (`bash client-unity/sync-net.sh` through `in.sh`) and the copy's `.meta`.
- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapMarks.CampTexts.cs` with `.meta` (needs a `/// <summary>` before its first type, at most 800 lines): the fields, `PlaceCampTexts`, the measuring label, the signature, the public reads.

The stub commit already creates the library file, its copy and the partial file (with the public reads returning "no block", `CampTextPlacements` 0, and `PlaceCampTexts` doing nothing); the implementer replaces their bodies.

## 5. Exclusive write scope
- `CampTextPlacement.cs` (library) and its synced copy; `TacticalMapMarks.CampTexts.cs` (both with their `.meta`).
- `UI/TacticalMapMarks.cs`: `SetMap` (one statement, the null-map clearing), `LayerChanged` (its body), `Present` (one statement), `ShowBadgeText` (the parameter, and the equality guards of test 5 if needed), `RefreshBadgeTexts` (the two camp statements, the ward call's last argument). Not touched: `RewardSlot` (no caller afterwards, stays), `WardSlot`, `RefreshCampClocks`, `RefreshChestTexts`, the clock and chest label statements, `BadgeText`, `OnPopulateMesh`. Do not add lines beyond what these need; the file is 653 lines.
- `UI/TacticalMapMarks.ZoneLabels.cs`: the two `BuildZoneMarks` statements. `Measure` is not edited.
- Not in scope: `TacticalMapController*.cs`, `TacticalMapSurface.cs`, `MapLabelPlacement.cs`, `CampMarks.cs`, `CampBadges.cs`, the zone-name pass, the minimap, `Tools/*`, every oracle file below.
- Contingent `oracle-amend:` (only if red when run): `ZoneNamesEditModeTests.cs`, `ZoneNamesRealOrderEditModeTests.cs` (see section 7).

## 6. Contracts and references
- Presentation only (COMMON.md): no game rule, no wire change. Rust decides outcomes; this draws server observations.
- `docs/DEVIATIONS.md` DEV-022 (no new row proposed).
- `docs/OWNER_DECISIONS.md` searched for the Tab map and camp texts: no row decides this subject.
- Lane rules: motion/steady frames allocate nothing; text through `HudUi.Label`; theme colours only; `Present` stays awake-only for placement; readability gate (summary, 800 lines); `TacticalMapMarks.cs` is 653 lines.
- COMMON.md traps: a label never active in the hierarchy measures a tenth of its size; fixtures follow the order of a real match; time in EditMode does not advance (`TacticalMapController.SecondsClock`).

## 7. Behaviour to preserve

Existing tests that pin this code (run after the change; no class is called green by reading). Verified by reading on the lane base:
- `MapScreenEditModeTests.MarksDrawZoneIconsOnlyWhenTheMapCarriesRegionsAndLabelOnlyTheLargeMap` (:109): a bare Large marks object with a 6 x 6 region map holds exactly six `TMP_Text` (one name per zone), a minimap marks none: guards item 11 (no label created early).
- `ZoneNamesEditModeTests` (class): names choose spots clear of camp plates, texts, givers, wards, chests. Its helper `PictureMarks` (:265) puts each camp's text at the right-hand place `(at.x + 21.2, at.y + 2 - h/2)`: a text now elsewhere leaves a phantom box there.
- `ZoneNamesRealOrderEditModeTests` (class): the measure-when-awake rules for names; helper `EdgeMarks` (:280) puts the four edge camps' texts at the right-hand place likewise. If a class is red, ONE `oracle-amend:` commit per file makes that helper read `CampTextRect` (expectations stay); the implementer reports it, the coordinator commits it.
- `TabMapMarksEditModeTests` (class): `MarkLabelsAreAtLeastTenPixels` (:236) needs an active `CampBadgeText` label after `DeliverMarks` (the fixture of that class may open before its first frame; the pass must have run by then); `AMarkStaysOnItsWorldPoint` (:260) pins the camp clock at p + (0, -16).
- `TabMapVisualFix3EditModeTests`, `DeclutterEditModeTests`, `HudVisualFix2EditModeTests`, `TacticalMapEditModeTests` (`CampRewardBadgesAndWardBeaconsAreDrawnForRowsThatHaveThem` :209 asserts `RewardBadgeCount` 1, `BadgesDrawn` 1 on a bare Large marks object without a panel: no pass runs there, those counts stay), `TabMapFlowEditModeTests`, `MinimapReturnEditModeTests`: use the marks and controller; must stay green. `PlayerFlowsEditModeTests` builds a whole HUD with these marks.
- dotnet `MapLabelPlacementTests`: the zone-name placement, untouched.
- `client-unity/UnityProject/Tools/hud-seams.test.cjs` pins MatchHud members only; this packet touches none. Seven `Tools/` scripts copy `HudLayout.MinimapSize` and the wide Tab-map layout: this packet changes neither formula.
- Rivals still hide a camp's drawing within 8 units (`CampDropPx`), but no longer move any other text.

No existing expectation is expected to change. The coordinator is told if an `oracle-amend:` was needed.

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-UI-TABMAP-TEXT-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- dotnet `CampTextPlacementTests` all green; Unity `CampTextEditModeTests` all green.
- The classes listed in section 7 stay green (Unity classes through `unity-class.sh`).
- `bash client-unity/sync-net.sh --check` passes.

## Oracle files (read-only for implementers)
- `client-unity/RoguehallaNet.Tests/CampTextPlacementTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/CampTextEditModeTests.cs` and its `.meta`
- `docs/backlog/packets/P-UI-TABMAP-TEXT-1.md`

## 9. Validation commands
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter CampTextPlacementTests`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> CampTextEditModeTests`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> ZoneNamesEditModeTests,ZoneNamesRealOrderEditModeTests,TabMapMarksEditModeTests` (then `TabMapVisualFix3EditModeTests,DeclutterEditModeTests,HudVisualFix2EditModeTests`, then `TacticalMapEditModeTests,TabMapFlowEditModeTests,MinimapReturnEditModeTests,MapScreenEditModeTests`)
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> bash client-unity/sync-net.sh`

## 10. Risk and suggested model tier

implementer-sonnet, reviewer. Risk: the signature rules (a pass only when something that moves a text changed; no hop when the terrain map object is replaced), real-order measurement (a label measured while inactive reads a tenth), and zero allocation in a forced pass. The pure placement is small and fully pinned by dotnet tests.

## 11. Known traps
- Blocks are indexed by the `_camps` row, not the pool label (`used++` skips drawn-away camps, blocks do not). In `Present` the order is `PlaceCampTexts(false)`, `RefreshBadgeTexts`, `PlaceZoneNamesIfMarksChanged`; in `LayerChanged` place, `RefreshBadgeTexts()`, `RefreshZoneLabels`. EditMode runs no `LateUpdate`; blocks use R, so none depends on a pool label's refit. Read a label's `preferredWidth` only when it is active in the hierarchy.
- `Roguehalla.Net.Protocol` and `Roguehalla.Net.Ui` both have a `MapRect`: a file importing both starts with `using MapRect = Roguehalla.Net.Ui.MapRect;`. Tests read public members only (no `InternalsVisibleTo`).
- Opening the map applies the controller's own k (about 1.3 in the fixture, an estimate); a later `Frame()` leaves a hand-set k alone.
- `AllocationProbe` (dotnet) counts bytes on a worker thread; in Unity `GC.GetAllocatedBytesForCurrentThread` reads 0, so Unity tests use `AllocationMeter`.
- `HudFade`, `TMP` "tenth size" and hidden-panel facts: COMMON.md of the lane.
- Not established (for the oracle to measure, recorded below): `w888`/`h888`, the text widths, the 3.25 layer units per world unit, the 2.39 overlap of test 3, the premise of test 8 phase B, how the picture's camps fall under the new places, whether the base's steady Present or `LayerChanged` allocates, and that a wall tile at cell 0 passes the Welcome map's checks. Whether a text left of, below or above its mark reads as that mark's on a dense map is for the lane's next Web round.

## Tests (the oracle)

dotnet `CampTextPlacementTests` (style of `MapLabelPlacementTests`; bounds (0, 0, 480, 480), leftGap 38.1, belowGap 24, W 15.02, LineH 12.14, Lines 1 unless said; no time). Each first asserts a result is not None. Places are worked in the order Right, Below, Above, Left:
1. `AWorkedBlockHasTheFourPlaces`: `RectOf` gives item 2's numbers (Within .001), Below at y 200.76 for belowGap 27.1; Lines 2 gives H 26.54; a place of -1 or 4 gives all 0.
2. `ABlockNearAnEdgeEndsInsideTheMap`: no obstacle, each rect inside (2, 2, 476, 476): (470, 240) Below, (240, 470) Below, (470, 470) Below, (10, 470) Below, (10, 10) Right, (470, 10) Above, (10, 240) Right; (470, 10) with an obstacle (460, 30, 18, 20) on its Above place: Left.
3. `TwoNearCampsGetBlocksThatDoNotOverlap`: camps (100, 100), (100, 108), 20.4 plates as obstacles: first Right, second Below; no two results overlap, none meets a plate.
4. `AnEliteIsPlacedBeforeItsNeighbours`: A (index 0, plain) (100, 100), B (index 1, elite) (100, 104): B Right, A Below; with both plain: A Right, B Below.
5. `TheTagTravelsWithTheAmount`: obstacle (261, 249, 20, 10), camp (240, 240): Lines 1 is Right, Lines 2 is Below.
6. `NoFreePlaceTakesTheLeastOverlapAndIsRepeatable`: camp (240, 240), an obstacle on each `RectOf` (disjoint), the Left one half as wide: Left; all four whole: Right (tie, earlier place); a second call equal; results inside the inset.
7. `ObstacleOrderChangesNothing`: 40 camps at (40 + 27 c, 40 + 27 r), c 0..7, r 0..4; obstacles the 40 plates and 20 ward rings; list reversed, rotated: equal. No place is None and two different places occur.
8. `UnmeasuredAndBadInputsAreSafe`: W 0, NaN, Lines 0, `leftGap` 0, `belowGap` 0, null buffers: no throw; unmeasured blocks Right with the zero rect; Lines 0 None; bounds (0, 0, 0, 0): every block Right.
9. `PlacingAllocatesNothing`: places not None; `AllocationProbe.PerCall` over 40 blocks and 200 obstacles reads 0.0.

Unity `CampTextEditModeTests` (fixture copied from `ZoneNamesRealOrderEditModeTests`: the real order, `BuildController(open, camps)` runs no frame; camps are `MapCamp(x, y, 8, 1)` in world units, 192 a side; amounts stat 2 with 25, 50, 75, 100 read "+0.75%", "+1.5%", "+2.25%", "+3%"; `Elite 1` adds "Aug"). Tests 1 to 6 first assert the panel is open, `CampTextPlacements` above 0 and `CampTextRect(i).HasValue` for every camp with text (red on the stub):
1. `TextsAtTheRightAndTopEdgesStayInsideTheMap`: camps (184, 96), (184, 40) elite, (96, 4) elite, (96, 100); at k 1, then after `SetMarkScale(1.4)` with no Present: each block inside the layer rect inset by 1.99, each drawn label inside its block.
2. `NeighbouringTextsTouchNeitherEachOtherNorEachOthersPlates`: camps (80, 96), (92, 96).
3. `ARivalHidesOneTextAndMovesNoOther`: A (96, 96), B (96, 99), C (30, 150); a rival at (96, 94): A's text not drawn, every `CampTextRect`/`CampTextPlace` and B's and C's drawn position unchanged, `CampTextPlacements` unchanged; rival removed: all equal again.
4. `NothingThatTicksReplacesAText`: ten frames, a clock, a ward, a quest drop and giver, a chest, their removal: counter, rects, places, drawn positions unchanged; then one reward word changed adds exactly one pass, and so does `MythicUi.ApplyTextScale(Panel, 1.5f)` then a `Present`.
5. `ASteadyPresentAndAForcedPassAllocateNothing`: `AllocationMeter.AssertNone` over `LayerChanged()` and over a steady `Present`.
6. `TheZoneNamesSeeThePlacedBlocks`: the picture of `ZoneNamesEditModeTests.PictureCamps`, at k 1 and 1.4, every `ZoneLabelRect` and `CampTextRect` has a value and no name meets a camp's block.
7. `ACampWithNoTextCreatesNoLabel` (standing guard, green on the stub; guards item 11).
8. `TheCampTextsOfARealMatchAreMeasuredWhenTheMapOpens` (the real order; closed step, open, text scale 1.5 phase B, phase C).
9. `ThePlanningOrderPlacesWhenThePanelShows`.
10. `ARebindForgetsTheBlocks`.
11. `ATerrainChangeInTheMiddleOfAMatchMovesNoText`.

## Notes from the oracle author
<!-- filled in with the measured numbers, the red/green table and the scratch-run result before the oracle commit -->

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
