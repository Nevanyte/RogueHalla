# P-UI-TABMAP-SHEET-1-fix: the open map's header allocates nothing while nothing changes

- **Serves:** the one note of the review of P-UI-TABMAP-SHEET-1 (LAND WITH NOTES, 2026-11-23). Same owner direction as that packet
  ("When tab is clicked map layout is weird", "make the front end as fluid as possible"; UI rework lane, owner request 2026-11-22).
  Not a Rust phase item: presentation only.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the branch forks at `20a1a004e` (an older `lane/ui-rework/base-2`; it contains P-UI-TABMAP-SHEET-1 and P-UI-ALLOC-METER-1). `lane/ui-rework/base-2` has since moved to `a7dc34e9b` (P-UI-ABILITY-ROW-1 merged, 16 commits, 17 files, none under the tab map). History is not rewritten here: the lane lead may rebuild `packet/` and `agent/sonnet/` on the current base-2, or keep this fork. Either way the gate takes `--base 20a1a004e` for this branch as it stands, or `--base lane/ui-rework/base-2` after a rebuild.
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
The base is `20a1a004e` (the fork point of this branch, an earlier `lane/ui-rework/base-2`); current `lane/ui-rework/base-2` (`a7dc34e9b`) is a descendant and works equally. Both
contain `P-UI-TABMAP-SHEET-1` (the sheet and `PresentHeader` exist only there) and `P-UI-ALLOC-METER-1`
(`Assets/Tests/Editor/AllocationMeter.cs`, the meter the oracle uses). The packet branch is linear on `20a1a004e` (`brief:` and `oracle:` commits only, no merge).

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
- `scripts/gate.sh --quick --oracle packet/P-UI-TABMAP-SHEET-1-fix --base 20a1a004e` (or `--base lane/ui-rework/base-2` after a rebuild) prints `GATE PASS`.
- `TabMapHeaderNoGarbageEditModeTests`, `TabMapSheetEditModeTests`, `TabMapFlowEditModeTests`, `TabMapVisualFix3EditModeTests`, `TacticalMapEditModeTests` green.

## Oracle files (read-only for implementers)
- `client-unity/UnityProject/Assets/Tests/Editor/TabMapHeaderNoGarbageEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/TabMapHeaderNoGarbageEditModeTests.cs.meta`

## 9. Validation commands
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> TabMapHeaderNoGarbageEditModeTests,TabMapSheetEditModeTests,TabMapFlowEditModeTests`
- `scripts/gate.sh --quick --oracle packet/P-UI-TABMAP-SHEET-1-fix --base 20a1a004e` (`--base lane/ui-rework/base-2` after a rebuild)

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
- The meter: `GC.GetAllocatedBytesForCurrentThread()` returns 0 in this editor even for a control that allocates (measured: 0 after 20000 string
  concatenations; confirmed by P-UI-ALLOC-METER-1, whose measured table is in `docs/backlog/packets/P-UI-ALLOC-METER-1.md`), so a test using it passes on any
  code. `ChampionPanelNoGarbageEditModeTests` uses it and is therefore vacuous here (not changed by this packet; for the owner's information).
  The first version of this oracle used `Profiler.GetMonoUsedSizeLong()` with a control; the control read 0 inside the whole suite, so the gate went red on the
  test's own set-up. The oracle now calls `AllocationMeter.AssertNone(present, 200, "PresentHeader with nothing changed")`
  (`Assets/Tests/Editor/AllocationMeter.cs`, on the lane base). The meter counts GC.Alloc samples and checks itself against a known allocating and a known
  clean control on every call. `present` is the `System.Action` made once with `Delegate.CreateDelegate`. The Profiler loop, its control and the 32768-byte limit are removed;
  the set-up assertions stay (the map is open, the hint is not empty, a phase line is set).
- Changed in this fix pass (lane lead's decisions): (1) the meter above; (2) the base of the packet is `lane/ui-rework/base-2` and the gate command takes
  `--base lane/ui-rework/base-2`. Test 2 (`AChangeStillReachesTheHeader`) is unchanged.
- Run with `unity-class.sh` on `TabMapHeaderNoGarbageEditModeTests,TabMapSheetEditModeTests,TabMapFlowEditModeTests,TabMapVisualFix3EditModeTests,TacticalMapEditModeTests`:
  - On `packet/P-UI-TABMAP-SHEET-1-fix` (no fix): 42 cases, 41 pass, 1 red. `AnUnchangedHeaderAllocatesNothing` RED on the assertion
    "PresentHeader with nothing changed allocated managed memory in 200 calls (meter UnityEngine.TestTools.Constraints GC.Alloc samples)" (the set-up assertions and the meter's
    self-check passed first). `AChangeStillReachesTheHeader`: green on the base, as designed (it guards a cache that sticks). The other four classes: green.
  - On `agent/sonnet/P-UI-TABMAP-SHEET-1-fix` (91ace0235) with this packet's test file checked out over its copy: 42 of 42 pass.
- Base drift (checker finding, second fix pass): the 16 commits between `20a1a004e` and current base-2 touch `AbilityTile`, `MatchHud.ActionBar/Legacy/Pulses/QuestRewards`,
  `HudRegions`, `AbilityFace`, `HudLegacyLayoutEditModeTests`, `HudTextScaleEditModeTests` and docs; none touches `TacticalMapController*` or any Tab map test. To measure on the
  current tree without merging into the packet, I made a scratch branch from `a7dc34e9b`, copied this packet's two test files onto it, ran
  `TabMapHeaderNoGarbageEditModeTests,TabMapSheetEditModeTests,TabMapFlowEditModeTests,TabMapVisualFix3EditModeTests,TacticalMapEditModeTests,AbilityRowEditModeTests,HudTextScaleEditModeTests`:
  79 cases, 78 pass, 1 red: `AnUnchangedHeaderAllocatesNothing` on the same assertion message as above; everything else (the ability row and text-scale classes included) green. The scratch branch is deleted.
  So the two result lines above hold on the current base-2 as well; the earlier run lines were taken on the `20a1a004e` tree and are still correct for it.
- The implementer must reach 0 steady allocation on every call, including `Preferred`/label writes only on change.
