# P-UI-CONTROLS-LINE-1: the line of keys under the action row goes; the first-run hint names WASD and click

- **Serves:** not in ORDER_OF_OPERATIONS. UI rework lane (owner direction 2026-11-22: "ability and spell hub needs
  optimizing", "Please do a UI optimization sweep"); seen in Web round 1 of the lane (2026-11-23): the line `WASD move
  · Click attack · Shift/Q quest · R Wish` touches the bottom edge of the window and repeats keys the row shows as key
  caps. Supersedes the draft `P-UI-CONSUMABLES-TEXT-1` (never dispatched), whose edits to this line do not survive.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` at 6de332756 (all of wave 3 included). Worktree
  `/Users/nevinmckinnon/Documents/Projects/RH-ui-b`. Ordering: wave 4 round 1, side by side with P-UI-LEFT-EDGE-1 and
  P-UI-PLAYER-WORDS-2 (no shared file). P-UI-TILE-PLATES-1 follows (it shares `MatchHud.ActionBar.cs`);
  P-UI-TEXT-FIT-1 follows all four (it edits `HudTextScaleEditModeTests` in `BuildInventory` and the count line, so it
  starts from a file that holds 60 labels and a hint of 53 characters).
- **D-rules touched:** D1: presentation only. Legacy keeps a permanent line `WASD move · Click attack · R Wish` as the
  last child of the bar's root (`packages/client/src/ui/weaponBar.ts:99-100`, `weaponBar.css:138`: 11 px, `#bbc8d5` on
  `#11171de6`). This packet drops the permanent line on purpose, so the hand-off proposes ONE row, `proposed DEV-<n>`:
  legacy shows the plated line at all times; Unity shows no permanent line: the first-run hint names moving and
  attacking during a player's first two runs, the key caps on the row name E, R, Shift, Q and Space, and the help window
  lists every key. The owner signs the row and the coordinator numbers it; the implementer never edits `DEVIATIONS.md`.
  D2 n/a. D5/D6 none. D9: no game rule, no key bound, no input read; the hint names bindings that exist (read on the
  base): right click moves the player (`Net/MatchClient.cs` `ReadInput`: `if (haveCursor)` at line 409,
  `mouse.rightButton.wasPressedThisFrame` at 450 calls `OrderMove` at 453; `haveCursor` is false while a panel blocks
  the pointer; owner mapping `docs/backlog/packets/P-QUEST25-CONTROLS.md` section 1: "WASD and right click move, and
  left click attacks"), and H opens the help window (`UI/MatchHud.Features.cs:271-272`, `kb.hKey.wasPressedThisFrame`
  calls `ToggleHelp`, defined at line 205). The Controls tab (`HelpGuideView.cs`, `ControlsData` at line 47, H at line
  105) lists all of them.

All paths below are under `client-unity/UnityProject/Assets/` unless they start with `docs/`.

## 1. ID and outcome

What is wrong today. `CreateWeaponBar` (`Scripts/UI/MatchHud.ActionBar.cs:96`) ends (lines 143-144) with
`var controls = HudUi.Label(HudUi.Box(_weaponBar, "ControlsLine", HudAnchor.BottomCenter, 0, -14f, 240, 13), "Label",
"WASD move · Click attack · Shift/Q quest · R Wish", theme.SizeTiny, theme.TextSecondary, TextAlignmentOptions.Center);`
and `controls.raycastTarget = false;`. The bar `CombatActions` sits `HudRegions.ConsumablesBottom` (14,
`RoguehallaNet/Net/Ui/HudRegions.cs:52`) above the stage bottom and `HudUi.Place` keeps y for a bottom anchor, so the
box spans stage y 0 to 13 and the label (grown by `ceil(10 * .45)` = 5 each way) y -5 to 18: no margin. The text is
22.5 em of Nunito, so `MythicTextScale` fits it to 88 percent of 240 = 211 units (size 9.4) whatever the text-size
setting. At 1920 x 1080 the baseline is 5 px and the descender ink 2 px above the window's edge. The line is not a
`HudRegion`, so no region test sees it. It repeats caps of the row: Space (swap chip), R (Wish tile), Shift and Q (quest
tiles), E (Heal chip). Only moving (WASD) and attacking (click) have no cap in the row. The room under the row is 14
units (Consumables spans y 14 to 42) and the HUD keeps 12 elsewhere; lifting the stack to make room fails
`RoguehallaNet.Tests/HudRegionsTests.cs:176 FeedbackPillsSitBelowAndEventPillsAboveTheCombatZone` at the two 800-high
stages, so the line goes instead.

The first-run hint is `MatchHud.FirstRunHint` (`internal const string`, `Scripts/UI/MatchHud.Tutorial.cs:35`), today
`Right-click move · Left-click attack · E potion · R Wish · H help` (65 characters, 27.6 em). `Bind` calls
`ArmFirstRunHint` (`MatchHud.cs:163`, defined at Tutorial.cs:40): when the profile is null, has no `Stats` or has
`Stats.Runs < 2`, `_feedback` becomes the hint. `HudOwnerRefresh` (`MatchHud.cs:279`) writes `SetStickyNotice(_feedback)`
on each dirty frame, which puts it in slot 0 of the feedback lane (`ToastLane`, pill 24 high, `FeedbackBottom` 162).
`TickFirstRunHint` (Tutorial.cs:48, called at `MatchHud.cs:281`) starts a 20 s timer on the first InMatch frame
(`Time.unscaledTime`) and clears the hint when it ends or at once in practice.

Outcome:

1. **The line is not built.** The two last statements of `CreateWeaponBar` go. The objects `ControlsLine` and
   `ControlsLine/Label` exist nowhere under `CombatActions`. No other statement of the bar changes: bar bottom 14, width
   458, `Consumables` bottom 14 and height 28, slots bottom 48 (all pinned by
   `HudLegacyLayoutEditModeTests.WeaponBarIsCentredWithSlotsWishTileConsumablesAndSwapChip`, unchanged).
2. **Nothing under the row dips into the 14 units.** At 1280 x 800, 1920 x 1080 and 2560 x 1440 every `RectTransform`
   under `CombatActions` (inactive ones included) has its lowest edge at least `HudRegions.ConsumablesBottom` above the
   stage bottom, within .01. Today `ControlsLine` measures 0 and its label -5. Read on the base: the only children
   placed at y 0 of the bar are the two chips of `Consumables`, whose labels sit 7 to 8 units under the chip's top
   (grown rects end at 16 or higher); every other child is raised by 34.
3. **The hint** (the same constant, same arming, timer and lane) reads `WASD or right-click move · Left-click attack · H
   help` (middle dot U+00B7; 53 characters, 23.4 em by the summed advance widths of `Nunito-Regular.ttf`, `hmtx`
   without kerning). It names the two things the row has no cap for and the key that opens the help window, and drops
   `E potion` and `R Wish`, which the row shows as caps.
4. **It fits its pill at text scales 1 and 1.5** (`ToastLane.Paint`, `Scripts/UI/ToastLane.cs:89-106`: pill width
   `min(LaneWidth, ceil(preferred at 13 x scale / .88) + 28)`, label inset 14 each side, `MinSize` 9). Scale 1: pill 374
   wide, text at 13. Scale 1.5: the pill is at the lane's 460 maximum and the text is held to about 16.2 (today's text:
   436 wide at scale 1, and 13.8 at 1.5). The tests require: preferred width at the fitted size at most 88 percent of
   the label width; pill at most `HudRegions.LaneWidth(stageWidth)`; `fontSize` at least 13 at scale 1 and at least 15
   at 1.5; overflow mode not Ellipsis or Truncate. Nothing resizes at snapshot rate: the pill is measured only when its
   text or the text scale changes (already so in `Paint`).
5. **Nothing else about the hint changes:** its name, `internal` visibility, the `Runs < 2` rule, the 20 s, the
   practice rule, the lane slot, the help window's Controls tab and every key binding.
6. The HUD label inventory of `HudTextScaleEditModeTests` loses one label: 61 on the base, 60 after. No new file in
   `client-unity/RoguehallaNet` (no dotnet test, no `sync-net.sh`).

## 2. Dependencies

Base `lane/ui-rework/base-3` (6de332756). None else.

## 3. Relevant legacy paths

- `packages/client/src/ui/weaponBar.ts:99-100` and `packages/client/src/ui/weaponBar.css:138`: legacy builds a permanent
  `WASD move · Click attack · R Wish` line as the last child of the bar root. Dropped on purpose (see D-rules; one
  proposed DEVIATIONS row). Unity has not matched it since the line was widened to `Shift/Q quest` for the Top 25 keys.

## 4. New paths

- `Assets/Tests/Editor/ControlsLineEditModeTests.cs` and its `.meta` (two fixtures, `ControlsLineLayoutEditModeTests`
  and `ControlsLineHintEditModeTests`).

## 5. Exclusive write scope

- `Scripts/UI/MatchHud.ActionBar.cs`: `CreateWeaponBar`, its last two statements only (delete by content: the label and
  `controls.raycastTarget = false;`; `theme` stays used by the slot code above).
- `Scripts/UI/MatchHud.Tutorial.cs`: the string of `FirstRunHint` (line 35) only, and the comment above it (lines
  33-34) if it names the old keys (it does not today).
- Not in scope: `MatchHud.cs`, `MatchHud.Legacy.cs`, `MatchHud.Notices.cs`, `ToastLane.cs`, `HudRegions.cs`,
  `HelpGuideView.cs`, `HudLegacyLayoutEditModeTests.cs`, `ToastLanesEditModeTests.cs`, `Tools/hud-seams.test.cjs` (it
  names `CreateWeaponBar` and `CreateConsumables` only; the implementer runs it), `docs/rework/LEGACY_LAYOUT.md:142` and
  `docs/backlog/packets/P-UNITY-HUD-TEXTSCALE-1.md:110` (they describe legacy and history), `docs/DEVIATIONS.md`.

## 6. Contracts and references

- Presentation only (lane rules); `docs/backlog/packets/P-QUEST25-CONTROLS.md` section 1 (owner key mapping; no key is
  bound or changed here); `UiInputRoutingEditModeTests.DigitKeysNoLongerSwapWeaponsOutsideChestChoice` (line 323) stays
  green.

## 7. Behaviour to preserve

- Every rect of the bar other than the line (rects pinned by `HudLegacyLayoutEditModeTests`, `AbilityRowEditModeTests`,
  `HudRegionsTests`, `HudRegionsEditModeTests`).
- The hint's name, `internal` visibility, compile-time constant (`_feedback == FirstRunHint` is compared in
  `TickFirstRunHint`), the `Runs < 2` arming, the 20 s, the practice rule, slot 0 of the feedback lane.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-CONTROLS-LINE-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` green (through `in.sh`), run before the gate.
- The tests below.

### Existing tests that pin this code

- `Assets/Tests/Editor/HudTextScaleEditModeTests.cs`: asserts a label inventory. `BuildInventory` has
  `Add("ControlsLine", Find(weaponBar, "ControlsLine/Label"), "WASD move · Click attack · Shift/Q quest · R Wish")` at
  line 185 (`Find` asserts the path exists), and `TheInventoryCoversTheWholeMatchHud` asserts 61 labels at line 295.
  Every test of the class (20 cases) runs `BuildInventory` in `SetUp`, so every case turns red once the line is gone.
  **Amended in the one `oracle-amend:` commit (listed for the owner):** the `Add("ControlsLine", ...)` row is deleted
  and the count reads 60. Both edits are green together on the base too (the file has 60 labels either way).
- `Assets/Tests/Editor/HudLegacyLayoutEditModeTests.cs:140 WeaponBarIsCentredWithSlotsWishTileConsumablesAndSwapChip`:
  bar bottom 14, width 458, slots bottom 48, Consumables bottom 14 height 28. Unchanged, stays green.
- `Assets/Tests/Editor/ToastLanesEditModeTests.cs:285 TheFirstRunHintIsAStickyFeedbackLine`: uses its own sentence and
  the `_firstRunHint` / `_feedback` fields, not the constant. Stays green.
- `RoguehallaNet.Tests/HudRegionsTests.cs:176 FeedbackPillsSitBelowAndEventPillsAboveTheCombatZone`: why the stack is not
  lifted. `AbilityRowEditModeTests`, `HudRegionsEditModeTests`: the row's rects. Stay green.
- Searched, no other pin: every other `.cs` under `Tests/Editor`, `RoguehallaNet.Tests`, the `Tools/*.test.cjs` files
  and tool scripts for `ControlsLine`, `WASD move`, `Click attack`, `Right-click move`, `E potion`, `FirstRunHint`.

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/ControlsLineEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/ControlsLineEditModeTests.cs.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/HudTextScaleEditModeTests.cs` (the one `oracle-amend:` commit)
- `docs/backlog/packets/P-UI-CONTROLS-LINE-1.md`

No stub commit: every symbol the tests use exists.

Tests (the two rigs are copies; no helper is shared between fixtures):

- `ControlsLineLayoutEditModeTests` (real `CreateLegacyChrome` and `CreateCombatHud`, measured in HUD units at 1280x800,
  1920x1080, 2560x1440):
  - `TheControlsLineIsGone(window)`: bar bottom 14 and Consumables bottom 14 / height 28 (set-up); then
    `bar.Find("ControlsLine")` null, no Transform named `ControlsLine` under the stage or chrome, no TMP text under the
    bar containing `WASD`, `Click attack` or `Shift/Q`.
  - `NothingUnderTheRowCrossesTheConsumablesBottom(window)`: more than 20 rects under the bar (set-up); every
    RectTransform under the bar (inactive included, bar excepted) has `Bottom >= HudRegions.ConsumablesBottom - .01`;
    the message lists every offender with path and bottom. If any other rect is listed once the line is gone, do not
    loosen the test: report it to the lane lead (a finding about the row).
- `ControlsLineHintEditModeTests` (real `MatchClient` on a fake transport, `Bind` through `InitializeLayoutReview`,
  frames are the HUD's `LateUpdate`, time only through `MatchHud.NoticeDtOverride`; the hint is NOT cleared in `SetUp`
  and `SetUp` asserts it armed and equal to the constant; scales are set by writing `_textScale` and raising
  `ShowcaseSettings.Changed` by reflection, never `SetTextScale`):
  - `TheFirstRunHintNamesMoveAndAttack`: one active pill in `FeedbackLane`; text and the constant (read by reflection)
    equal `WASD or right-click move · Left-click attack · H help`; no `potion`, `Wish`, `Shift`, `Space`; slot 0.
  - `TheHintFitsItsPillAtScaleOneAndOnePointFive` `[TestCase(1f, 13f)]` `[TestCase(1.5f, 15f)]`: after `SetScale`,
    `fontSize >= least`, preferred width <= `rect.width * MythicTextScale.WidthShare`, pill <= `LaneWidth`, overflow
    not Ellipsis or Truncate, text equals the constant. The scale-1 case is a standing guard (green on the base).

### Red on the stub (measured)

See "Notes from the oracle author" (filled after the run).

## 9. Validation commands

- `/Users/nevinmckinnon/Documents/Projects/RogueHalla/.agent-runs/lane-ui-rework/bin/unity-class.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-b ControlsLineLayoutEditModeTests,ControlsLineHintEditModeTests,HudTextScaleEditModeTests,HudLegacyLayoutEditModeTests,ToastLanesEditModeTests,AbilityRowEditModeTests,HudRegionsEditModeTests`
- `.../bin/in.sh <worktree> node --test client-unity/UnityProject/Tools/hud-seams.test.cjs`
- `.../bin/in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-CONTROLS-LINE-1 --base lane/ui-rework/base-3`
  then restore the two `Coliseum*Art.asset` files with `git checkout --`.

## 10. Risk and suggested model tier

Low. Two deletions and one string. implementer-sonnet; reviewer. The hand-off carries `proposed DEV-<n>` (legacy's
permanent controls line is dropped) and lists the one amended expectation (`HudTextScaleEditModeTests`).

## 11. Known traps

- Delete both statements (the label and `controls.raycastTarget = false`); delete by content, not by line number.
- The hint stays a compile-time `internal const string`. The separator is U+00B7 as in the old text; write it as the
  file does. Do not make it `public`: the tests read it by reflection (no `InternalsVisibleTo`).
- A `MythicTextScale` refits when the pill's text or scale changes through `ToastLane.Paint`; the test calls
  `SetScale` then `Frames`.
- The 15 floor has 7 percent of margin below the computed 16.2 and the old text sits 8 percent under it; both rest on
  summed advance widths without kerning (23.409 em new text, 27.586 old, 22.503 for the line). If a run shows 15 is on
  the wrong side, report it; do not move it.
- `ResetTransientPresentation` clears `_feedback` but not `_firstRunHint`, so a timeline reset removes the hint for the
  rest of the match. Base behaviour; leave it. The hint test rig does not subscribe that handler, which is why the hint
  survives `SetUp`.
- A returning player (`Stats.Runs >= 2`) sees no hint and now no line, and a player in practice who skipped the
  tutorial sees none either: the help window (H, or its button) is the only reminder. This is the design; no test.
- The 20 s expiry and practice rule read `Time.unscaledTime` (does not move in EditMode): untested, unchanged.
- The layout fixture runs windows whose bar scale is 1; on a narrow stage the bar scales below 1 around its bottom
  centre, so its bottom stays 14. Not tested.
- Restore statics in `TearDown` (`MatchHud.NoticeDtOverride`, `UiMotion.ReducedOverride`, `EventSystem.current`).
- Later packets that add plates or tiles under `CombatActions` (P-UI-TILE-PLATES-1) keep the second layout test green:
  nothing they add may extend below 14.
- Tool scripts copy layout formulas; this packet changes none.

## Notes from the oracle author

(filled after the run)

## Notes from previous attempts
