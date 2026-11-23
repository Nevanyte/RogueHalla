# P-UI-TABMAP-TEXT-2-fix: the Tab map key's heading hides with the key, and its camp cells share one text size

- **Serves:** not in the plan: owner direction 2026-11-22 (UI rework lane): "When tab is clicked map layout is weird". Two faults P-UI-TABMAP-TEXT-2 (merged on the lane branch as `88bbbdbe7`) left behind, seen in the lane's look round 3d (2026-11-23, Web build of `88bbbdbe7`).
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` (head `88bbbdbe7`)
- **D-rules touched:** D1 presentation only: legacy's map has no key that hides and no camp cells (the camp block is this lane's, proposed as a `DEVIATIONS.md` row by P-UI-TABMAP-TEXT-2): no legacy number to match, no new row · D2 n/a · D5/D6 none · D9 untouched (no game rule, no input, nothing on the wire).

## 1. ID and outcome

P-UI-TABMAP-TEXT-2-fix. Pictures (lane folder `.agent-runs/lane-ui-rework/webround/round-3d/`):

1. `15-tabmap-1000x560.png` (1000 x 560 window, Tab held): the key is hidden (`CampKeyLayout.InKey`: a left column under `MinHeight` 610 high or `MinWidth` 240 wide hides it). The gold "MAP KEY" heading stays and "IN VIEW" is drawn on it. Code (checked on `88bbbdbe7`): `TacticalMapController.cs:159-160` builds `KeyHeading` as a child of `_contactRail` and keeps no field; `:268` computes `inKey`; `:283` does `_keyRoot.gameObject.SetActive(inKey)`; `:285-286` put `_inViewHeading` at `viewY = inKey ? CampKeyLayout.ViewY : 10f` (10 is the key heading's own place). Nothing hides the key heading.
2. `10-tabmap.png`, `11-tabmap-key-zoom.png` (1920 x 1080, text size 1.00), `12-tabmap-large.png` (1.50): the nine camp cells are drawn at different text sizes. `TacticalMapController.CampKey.cs` `BuildCampCells` (:23-37) gives each cell label base size 11 with the `MythicTextScale` that `HudUi.Label` adds (`HudUi.cs:167`); `PlaceCampGrid` (:41-60) calls `Refit()` on each cell alone (:56), so each cell takes the largest size ITS text fits its box at (`MythicTextScale.Apply` :27-50, `Fit` :104-120, `Fits` :95-102, `WidthShare` .88). Measured by this oracle on the base (window 1280 x 800 or 1920 x 1080, nine kinds): text size 1: kinds 0, 1, 2, 5, 6 at 11.0, kind 3 at 10.34, kind 4 at 10.83, kind 7 at 10.31, kind 8 (Move speed) at 9.89; text size 1.5: 16.5 for the short ones down to 11.93 (kind 8), 12.43 (7), 12.47 (3), 13.07 (4), 13.91 (5), 14.23 (1), 15.62 (6).

When this lands: with the key hidden "IN VIEW" stands alone at the top of the left column; the nine cells show the same size (the smallest of the sizes their own fits give), re-decided whenever a fit can change; a steady frame writes and allocates nothing.

### Outcome, in rules

1. **The heading follows the key.** On the Tab map `KeyHeading` is active exactly when the key (`_keyRoot`) is. With the key hidden "IN VIEW" stands alone at the top of the left column. On the planning screen nothing changes (the whole `_runRail`, which holds `ContactsPage`, is inactive there: `TacticalMapController.cs:275` `_runRail.gameObject.SetActive(tab)`).
2. **No two labels of the left column overprint** (guard, see test 2 and the measured exception under "Notes from the oracle author").
3. **One size for the fixed cells.** After any placement of the grid every visible cell whose kind has a fixed amount (`CampAmountKey.IsFixed(kind)`, `client-unity/RoguehallaNet/Net/Ui/CampAmountKey.cs:41`) has the same font size: the smallest of the sizes those cells' own fits give (each cell's fit exactly as today). No cell is drawn larger than its own box allows; the cell with the widest text decides. A cell whose kind varies ("Soul Defense · varies") keeps its own fit when that is smaller than the shared size and takes the shared size otherwise; it never raises or lowers the others.
4. **The shared size is re-decided** whenever a cell's fit can change: a kind is learned or a cell shifts (the key's `Version`), the key's width changes, the key is shown, and the text size setting changes while the map is open or closed. Today `ApplyCampKey` (`TacticalMapController.CampKey.cs:61-70`) returns early on version, on-flag and width only; a text-size change calls `MythicUi.ApplyTextScale` on the canvas (`MythicUi.cs:100-103`: `Apply` on every `MythicTextScale`, each alone), so the sizes would drift apart again.
5. **A steady frame writes nothing and allocates nothing** (`ApplyCampKey`'s early return stays, now also on whatever the implementer compares to know a refit happened, e.g. the applied text size).
6. **Nothing else changes**: no word, no colour, no glyph, no cell's place, no threshold of `CampKeyLayout` (`CampAmountKey.cs:102-118`).

## 2. Dependencies

P-UI-TABMAP-TEXT-2 (merged on the base, `88bbbdbe7`), P-UI-ALLOC-METER-1 (`Assets/Tests/Editor/AllocationMeter.cs` is on the base).

## 3. Relevant legacy paths

None. Legacy's map has no key that hides and no camp cells (see D1 above); no legacy number applies.

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/CampKeyOneSizeEditModeTests.cs` (+ `.meta`), the oracle.
- `docs/backlog/packets/P-UI-TABMAP-TEXT-2-fix.md`, this packet.

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.cs` (697 lines; the gate fails a C# file above 800): keep the key heading in a field (or find it once) and set it active with the key, in the place where `_keyRoot` is switched (`:283`, inside the layout pass `ApplyLayout`, so a resize from full to compact and back is right). One or two lines.
- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.CampKey.cs` (71 lines): the shared size (in `PlaceCampGrid` after the per-cell refits) and the wider early return of `ApplyCampKey`.
- Not in scope: `MythicTextScale.cs`, `HudUi.cs`, `CampAmountKey.cs` and `CampKeyLayout`, the marks files, `CampKeyEditModeTests.cs` (it must stay green unedited; if one of its assertions cannot hold with one shared size, STOP and report which, with the numbers: do not amend it), and everything the lane rules (`COMMON.md`) protect. No synced library copy is touched.

## 6. Contracts and references

- Presentation only (R-AUTH, D9): nothing on the wire, no rule in Unity.
- Lane rules `.agent-runs/lane-ui-rework/specs/COMMON.md`: text through `HudUi.Label`, theme colours only, no allocation in a steady frame, a text label never active in the hierarchy measures a tenth of its size (`MythicTextScale.cs:38` skips the fit of a never-woken `TextMeshProUGUI`; cells are built hidden, so decide only after a cell has been shown, as `PlaceCampGrid` does today).
- `docs/OWNER_DECISIONS.md`: searched for the subject (Tab map key, camp cells): no row.

## 7. Behaviour to preserve

- Every cell's place, word, glyph and colour; `CampKeyLayout` numbers; the key's 21 children (twelve rows and nine cells), IN VIEW at y -366 on a full key.
- A cell's own fit stays the rule for its box: the shared size is the minimum of the fits, so each fixed cell still fits its box.
- Existing tests that pin this code, all green on the base (run on the stub: 89 of 89 pass in the eight classes listed under Acceptance):
  - `CampKeyEditModeTests` (`Assets/Tests/Editor/CampKeyEditModeTests.cs`): `TheKeyIsWholeOnTheThreeCanvasesAtTextSizeOneAndOnePointFive` (:448; each cell: text fits its box, `fontSize` at least 9 at text size 1 and 10.5 at 1.5, the varying "Soul Defense · varies" cell at least 6 and fits; the measured smallest fixed fits are 9.89 and 11.93, so one shared size passes); `ACompactWindowKeepsTheTexts` (:536; key hidden under 610 high, map keeps its texts); `TheKeyRowsStandWhereTheLayoutPutsThem` (:639; 21 key children, row places, IN VIEW at y -366); `ASteadyFrameAllocatesNothing` (:589; `Present`, `LayerChanged` and `ApplyCampKey` allocate nothing over 200 calls); `PlanningKeepsTheTexts` (:573; no cell active in planning); `ALateKindShiftsTheCellsAfterItOnce` (:608; cells shift once, `Version` +1); `AFixedKindLeavesTheMapAndStandsInTheKey`, `ADifferingAmountKeepsEveryTextOfItsKind`, `ABindForgetsTheLatch`, `ALateDifferingAmountMakesOnePassAndStays`, `ClearingACampAndItsClockMoveNothing` (cell texts, slots, latch, passes: no font size asserted).
  - `DeclutterLegendEditModeTests` (`:58-59`): the IN VIEW heading starts at or below the last key row.
  - `CampTextEditModeTests` (`MythicUi.ApplyTextScale(Panel, 1.5f)` at :604 and :709: camp texts on the map only; no key cell), `TabMapMarksEditModeTests`, `TabMapSheetEditModeTests`, `ZoneNamesEditModeTests`, `ZoneNamesRealOrderEditModeTests`, `MapScreenEditModeTests`: not about the key's heading or cell sizes; kept green.
  - `DisplayFontGapsEditModeTests` (:36-37, :61) lists "MAP KEY" and "IN VIEW" as display labels with their gaps: the labels keep their words and sizes.
  - No existing test names `KeyHeading` or reads a cell's `fontSize` against a constant. No conflict found, no `oracle-amend`.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-TABMAP-TEXT-2-fix --base lane/ui-rework/base-3` prints `GATE PASS`.
- `CampKeyOneSizeEditModeTests` is fully green.
- Green and unedited: `CampKeyEditModeTests`, `CampTextEditModeTests`, `TabMapMarksEditModeTests`, `TabMapSheetEditModeTests`, `DeclutterLegendEditModeTests`, `ZoneNamesEditModeTests`, `ZoneNamesRealOrderEditModeTests`, `MapScreenEditModeTests` (run by the oracle author on the stub: 89 passed).
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` still passes if a `MatchHud` member is touched (none is expected).

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/CampKeyOneSizeEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/CampKeyOneSizeEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-TABMAP-TEXT-2-fix.md`

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> CampKeyOneSizeEditModeTests`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> CampKeyEditModeTests,CampTextEditModeTests,TabMapMarksEditModeTests,TabMapSheetEditModeTests,DeclutterLegendEditModeTests,ZoneNamesEditModeTests,ZoneNamesRealOrderEditModeTests,MapScreenEditModeTests`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-TABMAP-TEXT-2-fix --base lane/ui-rework/base-3`

## 10. Risk and suggested model tier

implementer-sonnet; reviewer. Small code (a field and a line in `TacticalMapController.cs`, a few lines in `CampKey.cs`); the risk is in the order of events: `MythicTextScale.LateUpdate` leaves a label alone while its text equals the text it was last fitted for, so a size written after `Refit()` stays until the next `Apply`/`Refit`, and a text-size change applies every label of the canvas alone.

## 11. Known traps

- `MythicTextScale.LateUpdate` (:63-77) and `Apply`: the controller must notice a text-size change (compare the applied text size or the cells' sizes) and decide the shared size again. Do not edit `MythicTextScale`.
- Write the shared size the way the component writes a fitted size (`fontSize`, `fontSizeMax` and `fontSizeMin` together, auto sizing off, overflow Overflow), or TMP changes it back. After a refit of every visible fixed cell, read their `fontSize`, take the minimum, write it to each; a varying cell takes the lesser of the shared size and its own fit.
- A hidden cell (kind not seen) takes no part. With no fixed cell visible there is nothing to share.
- A label never active measures a tenth of its size: measure and decide only after a cell has been shown.
- The heading must also be right after a resize from a full window to a compact one and back: put the switch where `_keyRoot` is switched (`:283`), not in the build.
- Steady frame: no allocation (no LINQ, no list built per call, no string per call).
- No `Assert.Multiple`; no `InternalsVisibleTo`; EditMode time does not advance.

## Test list (what each test pins)

All in `CampKeyOneSizeEditModeTests`, on the real-order rig of `CampKeyEditModeTests` (built closed, rows delivered closed, three frames, then opened).

1. `TheKeyHeadingHidesWithTheKey`: windows 1000 x 480 and 1000 x 560: key hidden (premise), `KeyHeading` not active in the hierarchy, `InViewHeading` active. Windows 1000 x 600 and 1920 x 1080: key shown (premise), `KeyHeading` active, its text reads "MAP KEY" and its rendered text lies above the key root's top.
2. `TheHeadingFollowsAResizeFromCompactToFullAndBack` (added by the oracle author for the resize trap): compact, then `_stage.sizeDelta` 1422 x 800 (key shown, heading active), then compact again (both hidden).
3. `NoTwoLabelsOfTheLeftColumnOverprint`: windows 1000 x 480, 1000 x 560, 1000 x 600, 1920 x 1080 at text sizes 1 and 1.5: every active non-empty `TMP_Text` that is a direct child of `ContactsPage` or anywhere under `Key`; pairwise overlap of the rendered text bounds (world, divided by the panel's scale) at most .5 in both axes at once; the message names the pair and the overlap. See the exception below.
4. `TheFixedCellsShareOneTextSize`: nine kinds, windows 1920 x 1080, 1280 x 800, 980 x 2121, text sizes 1 and 1.5: all nine fixed cells within .001 of one size; that size at least 9 (text size 1) or 10.5 (1.5); each fits its box at it (the measure of `MythicTextScale.Fits`: preferred width at most box width x .88 - .005 and preferred height at most box height + .01); at the size plus .3 at least one cell no longer fits; each label asserted active in the hierarchy first.
5. `ATextSizeChangeKeepsOneSize`: open at 1 (one size), set the setting to 1.5 and `MythicUi.ApplyTextScale(canvas, 1.5)` then three frames with the map open (one size, larger than before, every cell fits); then close, change the text size to 1 while closed, open (one size, equal to the first within .01); close, change to 1.5, open (one size, equal to the open-change size within .01).
6. `ALateKindTakesAndGivesTheSharedSize`: nine camps, eight kinds (a ninth camp repeats kind 0) delivered closed: eight cells, one size; then the ninth camp becomes kind 8 (Move speed): nine cells, one size, not larger than before, each fits.
7. `AVaryingKindDoesNotMoveTheOthers` (red on the base because the eight fixed cells differ): ten camps, kind 2 (Damage) with two amounts, at text sizes 1 and 1.5: the eight fixed cells share one size, fit, and at that size plus .3 at least one of the eight no longer fits; the varying cell reads "Damage · varies", is at most the shared size, at least 6 and fits its box.
8. `ASteadyFrameWritesNothing` (red on the base on its first line, the shared size): ten more frames change no cell's `fontSize`; `AllocationMeter.AssertNone` over `ApplyCampKey` (the member `CampKeyEditModeTests.ASteadyFrameAllocatesNothing` measures) over 200 calls; the sizes stay.

## Notes from the oracle author

- **Red on the stub, on an assertion each** (run with `unity-class.sh`, 8 of 8 red, first failed assertion shown):
  - `TheKeyHeadingHidesWithTheKey`: "1000x480: the MAP KEY heading is hidden with the key" (expected False, was True).
  - `TheHeadingFollowsAResizeFromCompactToFullAndBack`: "compact: the heading is hidden" (expected False, was True).
  - `NoTwoLabelsOfTheLeftColumnOverprint`: "1000x480 text 1: no two labels overprint (worst: ContactsPage/KeyHeading "MAP KEY" and ContactsPage/InViewHeading "IN VIEW" by 17.52399 units)"; also 26.29 at text 1.5 and the same at 1000 x 560.
  - `TheFixedCellsShareOneTextSize`: "1920x1080 text 1: kind 3 has the shared size" (expected 11.0, was 10.34).
  - `ATextSizeChangeKeepsOneSize`: "text 1: kind 3 has the shared size" (11.0 against 10.34).
  - `ALateKindTakesAndGivesTheSharedSize`: "eight kinds: kind 3 has the shared size" (11.0 against 10.34; the premises, eight cells and no Move speed cell, hold).
  - `AVaryingKindDoesNotMoveTheOthers`: "text 1: kind 3 has the shared size" (11.0 against 10.34).
  - `ASteadyFrameWritesNothing`: "settled: kind 3 has the shared size" (11.0 against 10.34). Its allocation line is a standing guard that is green on the base.
- **Scratch run done** (one class copy in which each assertion is caught and collected, `Assert.That` replaced by a collecting helper, not committed): every collected entry in all eight tests is an assertion about the missing behaviour (heading hidden, overprint, one shared size, fits at the shared size). Every other line of every test ran: no exception from set-up, deliveries, lookups or rect maths; the premises (key hidden or shown, eight or nine or ten cells, the varying kind, the late kind) held; on the base the later comparisons (size larger at 1.5, the same size after a change made while closed, the Damage cell text, the varying cell at least 6 and fitting, the steady frame writing nothing) pass on their own, so those lines are consistent with the packet's rules.
- **Spec deviation 1, rendered text bounds instead of rects.** The spec says to compare world rects of the labels. The rectangles are useless here: `HudUi.Label` stretches a label over its whole parent and grows it by `ceil(size * .45)` above and below, and the key heading's `offsetMin.y -20` / `offsetMax.y -10` (`TacticalMapController.cs:159-160`) leave its rectangle as tall as the whole left column; the key rows' rectangles overlap each other by the growth. The test compares `TMP_Text.textBounds` (the rendered text) after `ForceMeshUpdate`, in HUD units. For the same reason the "KeyHeading lies above the key's first row" line of test 1 compares the rendered text with the key root's top.
- **Spec deviation 2, text size 1.5 is checked on the headings only, and the base has two real overprints at 1.5 on a shown key (not in this packet's scope, reported for the lane lead).** Measured on the base at 1000 x 600 and 1920 x 1080, text size 1.5, by rendered text bounds: the key's row "Reward chest" overprints "Route stop" by 5.03 units, and the "MAP KEY" heading overprints the first key row "You" by 3.67 units (a 13 x 1.5 heading of about 26 high from y 10 against the key at y 34). Neither can be fixed inside the write scope without moving the key or its rows (outcome 6 forbids it), so test 3 counts a pair with a label under the key at text size 1 only, and at text size 1.5 counts pairs of direct children of the contact rail (the two headings): that is the pair that goes red on the compact windows and green after outcome 1. If the lane lead wants the key's rows clear of each other at 1.5, it is its own packet. The planning screen was not examined for a similar overprint (not built here; `_runRail` is hidden in planning).
- **Spec deviation 3, one test added** (`TheHeadingFollowsAResizeFromCompactToFullAndBack`, for the packet's resize trap) and the spec's test 4 sequence extended (closed change at 1, then at 1.5). Test names differ from the spec's numbering only in the added test; the spec's tests 2 to 7 are 3 to 8 here.
- The spec's test 5 builds nine camps because the map's camp count must equal the rows delivered (`CampMarks.Glyphs` gives no rows otherwise, `CampMarks.cs:85`): the ninth camp first repeats kind 0, then becomes Move speed.
- **Corrected after the implementer's report (oracle defect, 2026-11-23).** `NoTwoLabelsOfTheLeftColumnOverprint` asserted `LeftColumnLabels().Count >= 2` as a premise on every window. On the compact windows (1000 x 480 and 1000 x 560) the key is hidden, so rule 1 hides the heading and every key row, and the only active label of the column is IN VIEW (the delivered contacts sit under the scroll, not directly under `ContactsPage`): the premise contradicted rule 1 and no implementation could satisfy both. The premise now asks for at least 2 labels when the key is active and at least 1 when it is hidden. No other line changed; a scratch run on the implementation (every assertion of the class caught and collected) listed exactly four entries, this premise at 1000 x 480 and 1000 x 560 at both text sizes, and no other line contradicted the packet. On the compact windows the pair check has one label and so is vacuous; the heading's hiding there is pinned by `TheKeyHeadingHidesWithTheKey` and `TheHeadingFollowsAResizeFromCompactToFullAndBack`.
- There is no `stub:` commit: the tests compile against existing members only (fields found by path and by reflection on `ApplyCampKey`), so nothing needs a stub.
