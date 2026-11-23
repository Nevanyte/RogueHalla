# P-UI-ZONE-NAMES-1-fix: a zone name is measured only when its label is awake

- **Serves:** a defect of the UI rework lane's wave 3 packet P-UI-ZONE-NAMES-1, seen in Web round 2 (2026-11-23): in two
  matches all twelve zone names of the Tab map stood on the first of their six spots, where they stood before that packet,
  two on a camp mark or a mark's text. Owner direction 2026-11-22 (lane chat): "When tab is clicked map layout is weird".
  Not in ORDER_OF_OPERATIONS.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` (worktree `/Users/nevinmckinnon/Documents/Projects/RH-ui-a`)
- **D-rules touched:** D1: legacy sizes a zone name from its length (`labelSizePx` in `relayout`,
  `packages/client/src/ui/planning.ts`), never from a laid-out text, so it has no unawake label. P-UI-ZONE-NAMES-1's
  proposed row stays as it says: NO new `DEVIATIONS.md` row; this packet makes it true in a match. D2 n/a. D5/D6 none. D9 untouched.

## 1. ID and outcome

A zone name on the Tab map (and the planning map) is placed by `MapLabelPlacement` from the size of its label. Units are
layer units. The size of a name is the size of its label taken AWAKE (active in the hierarchy); a name not yet measured
counts 0 x 0. After this packet, in a real match (map and marks delivered while the Tab panel is closed, first open later)
the names leave spot 0 for the marks they overlap, as they already do when the panel is opened before its first frame.

Evidence (lane worktree-independent, in the main checkout): `.agent-runs/lane-ui-rework/understand/exp-zone-names.txt`
(cause) and `exp-zone-names/` with `second-reader/` (real-order rig, edge camps, variants F1, F2).

### What is wrong today

A match starts with the Tab map panel closed (`TacticalMapController.Initialize` ends with
`_panel.gameObject.SetActive(false)`, `TacticalMapController.cs:193`) and `Tick` calls `_large.SetMap(...)`
(`TacticalMapController.cs:447`) BEFORE `ShowPanel(...)` (`:455`); so does every later match of the HUD (`Bind` calls
`SetMap(null)`, `:341`) and the planning map. `TacticalMapMarks.SetMap` (`UI/TacticalMapMarks.cs:106`) runs a forced pass
(`RefreshZoneLabels(Forced)`) that creates the six zone labels and `SetActive(true)`s them: `activeSelf` true,
`activeInHierarchy` false, no `Awake`. A TMP text that never was active in the hierarchy measures at ONE TENTH of its
size (`GetPreferredValues`, `preferredWidth`, `preferredHeight`): 2.2 x 1.4 for FIRE against 22.3 x 13.5.

`BuildZoneMarks` (`UI/TacticalMapMarks.ZoneLabels.cs:179`) guards the measure with `activeSelf` (line 185) and measures
again only when the font size changed or the kept width is 0 or less (line 188). The tenth is neither, so
`_zoneNameMeasured` keeps it for the life of the marks object and every later pass (the `Present` pass on open included)
places boxes of about 5 x 1.4 that no mark touches: spot 0 wins. Measured on the base in the real order: with four edge
camps 0 of 4 names leave spot 0; the fixture's order moves 4. `Measure` (same file, line 250) caches (text, font size)
for good and is spared only because its labels are created inside `Present`, run only while open.

A second defect: a rebind calls `SetMap(null)`, which clears actors, chests and delivered points but NOT `_camps`,
`_wards`, `_chestRows` of the previous match. The next match's first closed pass (the forced pass of `SetMap(map)`,
before any `Present`) reads those old camps. With true sizes a name can be pushed off its first spot by camps that are
gone, and keeps that spot (`Choose` prefers the stored spot while it is free).

The tests miss the first: `ZoneNamesEditModeTests.BuildController` (`Assets/Tests/Editor/ZoneNamesEditModeTests.cs:106`)
calls `SetOpen(true)` (line 138) before its first `Frame()`, so the labels are created awake, an order no player sees;
its `Spot` reads the true `preferredWidth`, `ZoneLabelRect` the kept size, nothing compares them.
`ThePlanningMapAvoidsToo` passes: its givers hit even a tenth box.

## 2. Dependencies

none (base `lane/ui-rework/base-3`; P-UI-WORLD-NAMES-1 and P-UI-QUEST-ACTION-PROMPT-1 touched no Tab map file).
P-UI-TABMAP-TEXT-1 runs AFTER this packet in the same files and is revised on the base that holds it: its own edit of
`Measure` is replaced by this packet's guard, and it keeps only its camp-text statements. It may amend
`ZoneNamesEditModeTests.PictureMarks`, another method than the one amended here.

## 3. Relevant legacy paths

- `packages/client/src/ui/planning.ts` (`relayout`, `labelSizePx`): legacy sizes a zone name from its text length, never
  from a laid-out text. No legacy behaviour to port here; this is a Unity measurement defect.

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/ZoneNamesRealOrderEditModeTests.cs` (+ `.meta`), the oracle.

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapMarks.ZoneLabels.cs`, these members only: the field
  `_zoneNameTaken`; the first loop of `BuildZoneMarks` (from `into.Clear()` to the `_zoneNameSizes` writes, lines
  181-195); `Measure` (250-259). Nothing from `if (_map == null) return;` (196) down, and not `AddMeasured`.
- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapMarks.cs`: the `map == null` branch of `SetMap` (line 110, item 6)
  and nothing else (653 lines; the branch gets one more line, the 800-line gate holds).
- No dotnet test and no `RoguehallaNet` change, so no `sync-net.sh`.
- Not in scope: `TacticalMapSurface.cs`, `TacticalMapController*.cs`, `MapLabelPlacement.cs`, `MythicTextScale.cs`
  (P-UI-TEXT-FIT-1), every other test, the camp texts' places.

### The rules (implementation spec)

1. **The flag.** One flag per name, `_zoneNameTaken[region]` (new field `readonly bool[] _zoneNameTaken = new
   bool[MapZones.Count + 1]` beside `_zoneNameMeasured`), says the kept `_zoneNameMeasured[region]` was taken from a label
   active in the hierarchy at font size `_zoneNameFont[region]`. It replaces the test "the kept width is 0 or less". The
   size that reaches the pass is the kept size when the flag is set, else 0 x 0. In the first loop of `BuildZoneMarks`, for
   a name whose label is `activeSelf` (line 185's test stays, so a zone without a name stays 0 x 0):
```csharp
var label = _zoneLabels[i]; float font = label.fontSize;
if (label.gameObject.activeInHierarchy)
{
    if (!_zoneNameTaken[region] || _zoneNameFont[region] != font)
    { _zoneNameMeasured[region] = label.GetPreferredValues(label.text, 0f, 0f);
      _zoneNameFont[region] = font; _zoneNameTaken[region] = true; }
}
else if (_zoneNameFont[region] != font) _zoneNameTaken[region] = false;   // item 3
if (_zoneNameTaken[region]) { w = _zoneNameMeasured[region].x; h = _zoneNameMeasured[region].y; }
```
2. **Which statements change:** the `if` of line 188 and the `w`, `h` assignment of line 192; line 190 stays in the measure
   branch and sets the flag. Unchanged: lines 185 and 194, `PlaceZoneNames` line 134, `RefreshZoneLabels`,
   `ResetZoneSpots`, `PlaceZoneNamesIfMarksChanged`, `ZonePlate`, `MapLabelPlacement`. A name not yet measured is 0 x 0:
   `Spot` gives no valid rect, `Choose` returns -1 and `PlaceZoneNames` takes today's fallback (spot 0, offset (0, -24), box 0).
3. **A text size change while the map is closed** (`fontSize` no longer equals `_zoneNameFont`) clears the flag at the next
   pass (the `else if`): the name reads 0 x 0 until its label is next awake, then is taken at the new size. The odd font
   at 1.5 (15 against 13.35 in the control order) is NOT fixed here.
4. **When a name is placed again: no new statement.** On the first open frame `ShowPanel` activates the panel (`Awake`
   runs at once) before the first pass that follows: the forced pass of the mark-scale change (`ApplyLayout`), else the
   `Present` pass of `PlaceZoneNamesIfMarksChanged` (it compares `_zoneNameSizes` with `_zoneNameSizesStored`). Closing and
   opening again runs no pass: a name moves only when the marks, the map's size or the text size change.
5. **`Measure`:** a cache hit is returned whatever the label's state; on a miss a label not active in the hierarchy gives
   `Vector2.zero` ("not measured") and nothing is stored; the `Clear()` at 256 entries comes after that test:
```csharp
if (_measured.TryGetValue(key, out size)) return size;
if (!label.gameObject.activeInHierarchy) return Vector2.zero;   // new: nothing stored
if (_measured.Count > 256) _measured.Clear();
```
   Its only caller, `AddMeasured`, passes the size to `MapLabelPlacement.AddText`, which adds no box when a side is not above 0.
6. **After a rebind: sizes and marks.** Sizes outlive the map and may; nothing is cleared. Marks: the `map == null` branch
   of `SetMap` also clears the kept lists, after `ClearDelivered()`:
```csharp
_camps.Clear(); _wards.Clear(); _chestRows.Clear(); _realIcons = 0;   // new (_realIcons counts _camps rows)
```
   `Present` (line 152) clears all of them first on every call and refills them, so nothing a new match needs is lost.
   Between the rebind and that `Present` they are read by `BuildZoneMarks` in the forced pass of `SetMap(map)` (the
   defect), by `OnPopulateMesh`, and by `CampMarkerCount`, `WardMarkCount`, `ChestBadgeCount`, `RewardBadgeCount`. Pooled
   labels stay. The minimap's marks run the same branch.
7. **Numbers** (1280 x 720, 6 x 6 tiles, edge camps). Preferred widths at text size 1, all 13.5 high: FIRE 22.3,
   HAUNTED 52.1, PLAGUE 39.7, FROST 33.3, HOLY 27.6, COLISEUM 54.2. Base, real order: kept 2.2, 5.2, 4.0, 3.3, 2.8, 5.4 by
   1.4, spot 0 for all six. After: kept = the preferred sizes, spots 0, 1, 1, 1, 1, 0; closed, before the first open: 0 x 0
   for all six, spot 0.

## 6. Contracts and references

- Presentation only (COMMON lane rules): no wire, no `crates/`. R-AUTH untouched. Time in tests through
  `TacticalMapController.SecondsClock`, no wall clock.

## 7. Behaviour to preserve

- A map opened before its first frame gives today's result (test 6 is a standing guard).
- Pass counts as today in the fixture's order; a quiet frame allocates nothing (`ZoneNamesEditModeTests`).
- Do not touch line 185 and `PlaceZoneNames` line 134 (`activeSelf`); `ResetZoneSpots` clears nothing else; `SetMap`
  clears only the three lists and the count of item 6, never a kept size. `_zoneNameFont` now means "the font at which
  the kept size was taken" (exact float compare).
- `Measure` order: cache hit, awake test, 256 clear; a refused measure stores nothing, not even 0.
- A closed pass stores spot 0 for a name not yet measured, as the control order's first pass does: add no "do not store" case.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-ZONE-NAMES-1-fix --base lane/ui-rework/base-3` prints `GATE PASS`.
- Run, green: `ZoneNamesRealOrderEditModeTests`, `ZoneNamesEditModeTests`, `TabMapMarksEditModeTests`,
  `HudVisualFix2EditModeTests`, `MapScreenEditModeTests`, `TabMapFlowEditModeTests`, `TabMapSheetEditModeTests`,
  `TabMapVisualFix3EditModeTests`, `DeclutterEditModeTests`. (`MinimapCaptureEditModeTests.PhysicalRightReleaseProjectsOverlayPositionAndFlushesChangedPointExactlyOnce`
  is red on the base too, not this packet's.)

## Existing tests that pin this code

- `ZoneNamesEditModeTests` (8 tests, `Assets/Tests/Editor/ZoneNamesEditModeTests.cs`): all build with the map opened
  before the first frame (labels awake at the first pass). `ThePicturesNamesAreClearOfTheirMarks` (:313) spots of HAUNTED
  and HOLY above 0 and names clear of picture marks at k 1 and 1.4; `ANameWithNothingNearItIsWhereItIsToday` (:354) spot 0,
  label 24 under the icon, plate 104 x 56; `ANameKeepsItsSpotWhileItIsFree` (:366) spot kept, no hop;
  `TheBoxesAreTheTextAndThePlateFollows` (:401) rect width/height equal `preferredWidth/Height`, plate geometry;
  `AQuietFrameAllocatesNothingAndPlacesNothing` (:427) no allocation in a forced pass, placement counts; 
  `ThePlanningMapAvoidsToo` (:450) HAUNTED name misses both givers (AMENDED, see below);
  `ANarrowedAndRestoredRectBringsTheSpotBack` (:469) edge spot and return; `TextScaleOnePointFiveKeepsNamesOffTheirIcons`
  (:496) widths 1.2x and equal to `preferredWidth` at 1.5. None changes expectation except the one amended.
- `TabMapMarksEditModeTests` (three zone-name tests, e.g. `ZoneNamesFollowTheLayer` :298): the labels follow the layer;
  read no size.
- `HudVisualFix2EditModeTests` (:224, :245): zone labels draw after the mesh and fog; carry a dark outline; read no size.
- `MapScreenEditModeTests.MarksDrawZoneIconsOnlyWhenTheMapCarriesRegionsAndLabelOnlyTheLargeMap`: reads no size.
- `TabMapFlowEditModeTests`, `TabMapSheetEditModeTests`, `TabMapVisualFix3EditModeTests`, `DeclutterEditModeTests`: map
  classes run for regression; none reads a zone-name size.

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/ZoneNamesRealOrderEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/ZoneNamesRealOrderEditModeTests.cs.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/ZoneNamesEditModeTests.cs` (the `oracle-amend:` commit: one method,
  `ThePlanningMapAvoidsToo`, for the owner)
- `docs/backlog/packets/P-UI-ZONE-NAMES-1-fix.md`

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> ZoneNamesRealOrderEditModeTests,ZoneNamesEditModeTests`
- the map classes of section 8; then the quick gate with `--oracle packet/P-UI-ZONE-NAMES-1-fix`.

## 10. Risk and suggested model tier

implementer-sonnet: three small edits in two files, exact code given above. The hard part is already done (cause and
form). Risk: the flag form of rule 1 was run by neither reader (second reader ran F2, no flag); if the flag form and a
test differ, the oracle author's scratch run decides (see notes).

## 11. Known traps

- Leave lines 185 and 134 (`activeSelf`) alone; clear nothing in `ResetZoneSpots`.
- `Measure` order: hit, awake test, 256 clear. P-UI-TABMAP-TEXT-1 calls it for a pool label that may be created while
  closed: zero means "not measured".
- Do not use F1 (gating line 185 on `activeInHierarchy`): it zeroes the sizes of every closed pass and loses the stored spots.
- Do not copy the fixture's `IsOpen` and `_layer1 > 300` assertions into closed builders. Opening re-applies the scale 1.3
  (camps then stand about 15 units from the icon, still on a real box); camps delivered while closed reach the large
  marks on the first open `Present`: count them then.
- A text label never active in the hierarchy measures at a tenth: any new assertion about a width first asserts
  `activeInHierarchy`.

## Not established

- The Web player was not read (same TMP code); the full `MatchHud` rig was not run: test 7 rebinds the controller rig.
  Whether the names read well where they land is a look question.
- At text size 1.5 applied while closed the name's FONT differs from the control's (15 against 13.35); not fixed here.
- After the cache's 256-entry clear a closed pass sees fewer text boxes.
- Hand-off: DEVIATIONS none proposed; for the owner, one sentence: the zone names did not move in a real match until
  this packet, because they were measured at a tenth of their size while the map panel was closed.

## Notes from the oracle author

(filled in at the oracle commit)

## Notes from previous attempts
