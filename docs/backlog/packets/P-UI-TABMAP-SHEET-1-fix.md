# P-UI-TABMAP-SHEET-1-fix: the open map's header allocates nothing while nothing changes

- **Serves:** the one note of the review of P-UI-TABMAP-SHEET-1 (LAND WITH NOTES, 2026-11-23). Same owner direction as that packet
  ("When tab is clicked map layout is weird", "make the front end as fluid as possible"; UI rework lane, owner request 2026-11-22).
  Not a Rust phase item: presentation only.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `agent/sonnet/P-UI-TABMAP-SHEET-1` (this packet stacks on that branch; the gate compares against it with `--base`).
- **D-rules touched:** D1 n/a (presentation) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome
`PresentHeader` decides the Tab map header's hint text, the Close button and which labels fit. To skip the work when nothing changed it
builds a key by string concatenation on every call (`hint + "|" + _phaseLine + "|" + toggle + "|" + width + "|" + _title.text`,
`TacticalMapController.Sheet.cs:87`) and compares it with `_headerKey` (`:88`). `PresentTactics` calls it on every paint
(`TacticalMapController.cs:525`), about 30 times a second for as long as the map is open: each call allocates several strings and boxes a bool
and a float. The lane rule is that a steady frame allocates nothing.

After this packet `PresentHeader` allocates nothing when nothing it depends on changed. The key becomes fields compared one by one (the toggle
flag, the phase line string, the header width, the title string; any other input the method reads): when all equal the last call's it returns
at once. `ApplySheet` already resets the key when the layout changes (`_headerKey = null`, `:76`): it resets the fields the same way. What the
header shows, and when it changes, stays exactly as it is.

## 2. Dependencies
`P-UI-TABMAP-SHEET-1` (branch `agent/sonnet/P-UI-TABMAP-SHEET-1`) is the base: the sheet and `PresentHeader` exist only there.

## 3. Relevant legacy paths
None: Unity-only presentation. Current code (on the base):
- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.Sheet.cs:81-102` `PresentHeader`; `:20` the `_headerKey` field;
  `:60-78` `ApplySheet` (resets `_headerKey` at `:76`, then calls `PresentHeader` at `:77`); `:44-51` `SetPhaseLine` (calls `PresentHeader` at `:50`).
- `TacticalMapController.cs:521-525` `PresentTactics` writes `_title.text` ("LOBBY MAP" / "TACTICAL MAP") then calls `PresentHeader()` every paint;
  `:489` planning writes `_title.text = "PLAN YOUR RUN"`; `:258-267` `ApplyLayout` stores `_layout` and calls `ApplySheet`; `:58` `ToggleMode => _prefs.MapToggle`.

## 4. New paths
- `client-unity/UnityProject/Assets/Tests/Editor/TabMapHeaderNoGarbageEditModeTests.cs` (+ `.meta`)

## 5. Exclusive write scope
- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.Sheet.cs` (`PresentHeader`, its cache fields, the reset in `ApplySheet`)
- `docs/backlog/packets/P-UI-TABMAP-SHEET-1-fix.md`
- Not in scope: `TabMapSheetEditModeTests.cs` (change nothing in it), `TacticalMapController.cs`, every other file.

## 6. Contracts and references
- Lane rule (`.agent-runs/lane-ui-rework/specs/COMMON.md`): a steady frame allocates nothing; unscaled time through `TacticalMapController.SecondsClock` only.
- Readability gate: `TacticalMapController.Sheet.cs` stays at most 800 lines (106 now); keep its `/// <summary>`.

## 7. Behaviour to preserve
- The header's text, Close button, inset and fit rule are unchanged (the 15 cases of `TabMapSheetEditModeTests` stay green without a change).
- Every input of the base's key remains an input: hint (a function of the toggle flag), `_phaseLine`, toggle flag, `_layout.Header.W`, `_title.text`.
  The label widths also depend on the text-size setting, which the base's key does not contain; do not add or drop inputs, only change how they are compared.

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-UI-TABMAP-SHEET-1-fix --base agent/sonnet/P-UI-TABMAP-SHEET-1` prints `GATE PASS`.
- `TabMapHeaderNoGarbageEditModeTests`, `TabMapSheetEditModeTests`, `TabMapFlowEditModeTests`, `TabMapVisualFix3EditModeTests`, `TacticalMapEditModeTests` green.

## Oracle files (read-only for implementers)
- `client-unity/UnityProject/Assets/Tests/Editor/TabMapHeaderNoGarbageEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/TabMapHeaderNoGarbageEditModeTests.cs.meta`

## 9. Validation commands
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> TabMapHeaderNoGarbageEditModeTests,TabMapSheetEditModeTests,TabMapFlowEditModeTests`
- `scripts/gate.sh --quick --oracle packet/P-UI-TABMAP-SHEET-1-fix --base agent/sonnet/P-UI-TABMAP-SHEET-1`

## 10. Risk and suggested model tier
Small, one method. implementer-sonnet; reviewer (not Opus): no Rust, no wire, no state.

## 11. Known traps
- Compare strings with `==` or `ReferenceEquals` on the stored reference; never build a string to compare.
- The header width is a float from the layout: compare it as it is (no rounding to a string).
- Reset every cache field in `ApplySheet` where `_headerKey = null` is now (a layout change must re-evaluate what fits).
- Reading `_title.text` and `ToggleMode` must not allocate (they do not today); `Preferred(...)` and the label writes only run when something changed.
- Time only through `SecondsClock`; the test assembly cannot see `internal` members, so the test reaches `PresentHeader` by reflection: keep its name and
  a parameterless `void` signature.

## Existing tests that pin this code (all stay green, none changes)
- `TabMapSheetEditModeTests` (15 cases): `TheHeaderCarriesTitlePhaseAndHint` (hold hint, no Close; toggle hint and Close after `SetToggleMode(true)` + `SetOpen`;
  `SetPhaseLine` text, shown on a wide header, null clears); `TheHeaderDropsWhatDoesNotFit` (640x480 header 392 wide: the long phase line and the toggle hint
  are dropped by the width rule, hint inset 92 with Close else 0); `TheHudHandsItsPhaseLineToTheMap`; rects, sheet, rails, dim, HUD veil tests that go through `ApplySheet`.
- `TabMapFlowEditModeTests`: reads `MapHint` / `MapHeading` texts (`HintText()`, `TitleText()` at :414-415) across open, toggle and planning flows.
- `TabMapVisualFix3EditModeTests`, `TacticalMapEditModeTests`: the controller's other behaviour; neither names the header members.

## Notes from the oracle author
- The spec's test 2 (`AChangeStillReachesTheHeader`) is green on the base and says so in its summary: it guards a sticking cache. The layout-change step
  shrinks the stage to 640x480 units and runs a frame, then asserts the long phase line is dropped.
- No stub commit: both tests compile against the base.
- Result of the run: recorded below.
