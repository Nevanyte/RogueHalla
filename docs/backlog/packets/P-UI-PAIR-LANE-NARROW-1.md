# P-UI-PAIR-LANE-NARROW-1: the prompt lane scales with the action row (pickup pill and quest launcher)

- **Serves:** not in ORDER_OF_OPERATIONS. The coordinator's merge review of the UI lane's wave 2 (`reviewer-opus`, 2026-11-23): "on a stage
  narrower than about 1040 HUD units the pickup pill shrinks with the bar while the quest launcher stays full size at 130, so the pair lane
  splits (the pair test runs only wide)"; owner direction 2026-11-22: "ability and spell hub needs optimizing".
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (head 4102837d4, which holds integration 85, `feature/unity-rework` @ ce3841ccd)
- **D-rules touched:** D1 presentation only. Legacy has no pair: its weapon bar stacks one pickup hint above the slots and only narrows the slot
  width under uiW 600 (`packages/client/src/ui/weaponBar.css:130`, `html[data-ui-narrow] .wb-slot { width: 88px; }`, verified); the quest
  launcher and the shared prompt lane are Unity additions (P-UI-ABILITY-ROW-1). No `DEVIATIONS.md` row is proposed. D2 n/a. D5/D6/D9 none.
  No key is bound or changed. `docs/OWNER_DECISIONS.md` has no row on the prompt lane or the action row (searched "PromptPair", "ability",
  "prompt lane").
- **Worktree:** `/Users/nevinmckinnon/Documents/Projects/RH-ui-c`

## 1. ID and outcome

On a narrow stage the action row (`_weaponBar`, 458 x 110, pivot bottom centre) is scaled by `s = HudRegions.AbilityRowScale(..)`
(`MatchHud.Legacy.cs:352-354`, `s` in .6..1). The pickup pill `Pickup` is a CHILD of that row (`CreatePickupHint`, `MatchHud.ActionBar.cs:154-156`;
its local region `PickupRegion(pair)` is `PromptPair(0)` or `Prompt()` moved 14 down, `MatchHud.ActionBar.cs:151-152`, so 116 above the row
bottom), so it is drawn at `s`. The box `QuestRewards` holding the launcher is a child of `PlayerHUD`, which is not scaled;
`UpdatePromptLane` (`MatchHud.QuestRewards.cs:300-309`) places it from `PromptPair(1)` or `Prompt()` at scale 1 only, and returns early unless the
pair state changed. Result: below a stage of about 1037 units the pair is not centred, its gap is `4 + 4 s` instead of 8, and the two sit at
different heights and sizes; a lone launcher is 300 x 26 at 130 at every `s` while a lone pill is `300 s` x `26 s` at `14 + 116 s`. They never
overlap. This packet makes the prompt lane one pure layout of `HudRegions`, scaled with the row, and the live rects equal it.

Stage height 800; minimap 270 (236 at 800, its room limit); champion panel right edge 262; `s` from `AbilityRowScale(w, 262, w - 12 - minimap)`.
C = stage width / 2. Today: pill = [C - 224 s, C - 4 s], bottom 14 + 116 s, 220 s x 26 s; launcher = [C + 4, C + 224], bottom 130, 220 x 26.

| Stage w | s | pill x | launcher x | gap | pill bottom, h | launcher bottom, h | pair centre minus C |
|---|---|---|---|---|---|---|---|
| 1422 | 1 | 487..707 | 715..935 | 8 | 130, 26 | 130, 26 | 0 |
| 1280 | 1 | 416..636 | 644..864 | 8 | 130, 26 | 130, 26 | 0 |
| 1040 | 1 (1.0044 before the clamp) | 296..516 | 524..744 | 8 | 130, 26 | 130, 26 | 0 |
| 900 | .6987 | 293.5..447.2 | 454..674 | 6.79 | 95.0, 18.2 | 130, 26 | +33.8 |
| 800 | .6 (.5677 before the clamp) | 265.6..397.6 | 404..624 | 6.4 | 83.6, 15.6 | 130, 26 | +44.8 |

`s` first drops under 1 at a stage of 1037 units. P-UI-ABILITY-ROW-1 accepted the split ("on a narrow stage the pill scales with the bar and the
box does not"); this packet removes it.

## 2. Dependencies

None beyond the base (P-UI-ABILITY-ROW-1 and its fix are in it; `AllocationMeter` is in it, `Assets/Tests/Editor/AllocationMeter.cs`).
P-UI-ABILITY-MOTION-1 and P-UI-QUEST-REWARD-PROMPT-1 may merge before or after: see the traps.

## 3. Relevant legacy paths

- `packages/client/src/ui/weaponBar.css:130`: legacy has one pickup hint stacked above the slots, no pair and no launcher; only the slot width
  narrows (verified by reading). Nothing to port: this is a Unity layout correction.

Unity code read on the base:
- `client-unity/RoguehallaNet/Net/Ui/HudRegions.cs:99-100` `Prompt(width = PromptWidth)`: BottomCenter, X 0, Y `PromptBottom` (130), 300 x 26.
- `HudRegions.cs:105-113` `AbilityRowScale`; `HudRegions.cs:115-122` `PromptPairWidth = 220` and `PromptPair(int index)`: X = (index == 0 ? -1 : 1) *
  (110 + 4), Y 130, 220 x 26. `HudRegions.cs:52-55` `ConsumablesBottom` 14, `AbilityWidth` 458, `PromptBottom` 130, `PromptHeight` 26, `PromptWidth` 300.
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.QuestRewards.cs:19` the field line `bool _questTilesFresh = true, _promptPair, _questViewKnown;`;
  `:155` and `:165` the two callers in `RefreshQuestRewardHud` (box shown or not); `:299-309` `UpdatePromptLane`; `:311-315` `RefitLabel`;
  `:491` `ResetQuestRewardHud` calls `UpdatePromptLane()` with the box hidden.
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.ActionBar.cs:151-152` `PickupRegion`, `:154-156` `CreatePickupHint`.
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Legacy.cs:352-354` the row scale; `UI/HudUi.cs:46-69` `Place` (pivot = anchor point, size from the region).

## 4. New paths

- `client-unity/RoguehallaNet.Tests/PromptPairTests.cs` (dotnet, oracle)
- `client-unity/UnityProject/Assets/Tests/Editor/PromptPairNarrowEditModeTests.cs` and its `.meta` (oracle)

## 5. Exclusive write scope

- `client-unity/RoguehallaNet/Net/Ui/HudRegions.cs` (the pair and lone members only) and its synced copy
  `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/HudRegions.cs` (run `bash client-unity/sync-net.sh` through the wrapper; `.meta` exists)
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.QuestRewards.cs`: `UpdatePromptLane` and the new field only
- Not in scope: `UI/MatchHud.ActionBar.cs` (`PickupRegion` stays), `UI/MatchHud.Legacy.cs` (the row's scale), `UI/HudLayout.cs`,
  `UI/MatchHud.Pulses.cs`, `UI/MatchHud.cs`, `Tests/Editor/AbilityRowEditModeTests.cs`, `AbilityFaceTests.cs`, `HudRegionsTests.cs`,
  `Tools/hud-seams.test.cjs`, the rest of `MatchHud.QuestRewards.cs` (captions and reward flow: P-UI-QUEST-REWARD-PROMPT-1).

## 6. Contracts and references

- R-AUTH / ADR 0008: presentation only, no game rule, no wire change. Theme colours only; text through `HudUi.Label`. No key changes
  (`docs/backlog/packets/P-QUEST25-CONTROLS.md` untouched).
- `HudRegions` is the single source of lane numbers (its class summary, `HudRegions.cs:47`).

## 7. Behaviour to preserve

- At `s = 1` everything is exactly where it is today: the lone launcher at `Prompt()` (300 x 26 at 130, `localScale` one), the pair at `PromptPair(i)`.
- `PromptPair(int)` stays and equals `PromptPair(index, 1f)`; `Prompt()` stays and equals `PromptLone(1f)`. `PickupRegion(bool)` keeps calling the
  one-argument `PromptPair(0)` (the pill's local region inside the scaled row, which is why the pill needs no change).
- `_promptPair` stays a `bool` field (read by name through reflection, `AbilityRowNearbyNoGarbageEditModeTests.cs:297`).
- `ResetQuestRewardHud` keeps `if (_questRewardHud != null) UpdatePromptLane();` (`MatchHud.QuestRewards.cs:491`).
- No label box is resized; the lane scales by `localScale` only.

## The design

1. **Pure layout.** In `HudRegions`: `public const float PromptPairHalfGap = 4f, PairScaleMin = .6f;` (4 is the literal in `PromptPair(int)` today).
   `public static float PairScale(float rowScale)`: NaN and infinities give 1, else clamped to `PairScaleMin..1`.
   `public static HudRegion PromptPair(int index, float scale)` returns a `HudRegion` (anchor `BottomCenter`), the element's rectangle on the stage
   after scaling, with `s = PairScale(scale)`: `X = (index == 0 ? -1 : 1) * (PromptPairWidth / 2 + PromptPairHalfGap) * s`,
   `Y = ConsumablesBottom + (PromptBottom - ConsumablesBottom) * s` (14 + 116 s), `Width = PromptPairWidth * s`, `Height = PromptHeight * s`.
   `public static HudRegion PromptLone(float scale)`: `X = 0`, the same `Y`, `Width = PromptWidth * s`, `Height = PromptHeight * s`.
2. **Properties** (every `s` in .6..1, any stage wide enough for the row): the two pair rects do not intersect, the gap is `2 * PromptPairHalfGap * s`
   (8 s), both have the same top and bottom, the union is centred on the stage centre, both lie inside the stage, and the pair stays within the row's
   footprint (`224 s <= 229 s`, half of `AbilityWidth * s`), so a row that clears the champion panel and the minimap leaves the pair clear of them.
   The lone rect is centred, has the pair's top, bottom and height, and is `300 s` wide. One factor `s` scales width, height, offset and bottom.
3. **Worked layout** (same inputs as the table). Pair: 900: pill 293.5..447.2, launcher 452.8..606.5, gap 5.59, bottom 95.05, height 18.17.
   800: pill 265.6..397.6, launcher 402.4..534.4, gap 4.8, bottom 83.6, height 15.6. At 1422, 1280, 1040: the table rows (unchanged).
   Lone: 900: 345.2..554.8 (width 209.6), bottom 95.05, height 18.17. 800: 310..490 (width 180), bottom 83.6, height 15.6. At s = 1: 300 x 26 at 130.
4. **Live layout.** The pill is not touched: its local region (`PickupRegion(true)`, x -114, y 116) inside the scaled row is `PromptPair(0, s)` on the
   stage. `UpdatePromptLane` places the launcher box with the X and Y of `PromptPair(1, s)` (paired) or `PromptLone(s)` (alone), at its natural size
   (220 x 26 paired, 300 x 26 alone: `PromptPairWidth`, `PromptWidth`, `PromptHeight`) and `localScale = (s, s, 1)` (pivot bottom centre, set by
   `HudUi.Place`), so the whole launcher subtree, caption included, scales as the pill does. `s` is `PairScale(_weaponBar.localScale.x)`
   (`_weaponBar` is null before `CreateWeaponBar`: read it as `_weaponBar != null ? _weaponBar.localScale.x : 1f`).
5. **One lane, scaled with the row (lane lead ruling).** Where `s` is under 1 the prompt lane always sits at the row's scaled place and size, whether
   it holds one prompt or two. The lone launcher is at `PromptLone(s)`, scaled by `s`, as the lone pill already is (local `Prompt()` in the row, so
   `PromptLone(s)` on the stage). Nothing hops vertically and the scale never changes when the second prompt appears or leaves: the bottom edge, the
   height and the scale stay; the only move is sideways, from the centre to its half (width `300 s` to `220 s`), which exists today. At `s = 1` the
   lone launcher is at `Prompt()` with `localScale` one, the pair at `PromptPair(i)`. When the pair ends, the box is back at `PromptLone(s)` with
   `localScale (s, s, 1)`.
6. **Re-placing, no steady cost.** The lane is rewritten when the pair state changes, or, in either state, when the row's scale differs from the scale
   last placed (new field `float _promptPairScale = 1f;`, declared on a new line right after `MatchHud.QuestRewards.cs:19`; compared with `==`
   against the raw `localScale.x`, no allocation). `UpdatePromptLane` runs whether or not the box is shown, so a box that appears is already at the
   row's scaled place. On a scale-only change only the box is written. `RefitLabel` runs on a pair-state change only (the local boxes change then).
   A steady frame (same state, same scale) allocates nothing and writes no rect.
7. **Labels.** No label box is resized; the launcher caption (local box 220 x 36 paired, 193.6 usable) and the pill text (local box 184 x 26, 161.9
   usable) fit in local units as today, then the transform scales them: visual size = fitted size x s. `MythicTextScale` shrinks and never cuts, floor
   `MinSize` 6, so the floor is 6 x .6 = 3.6 units at `s` .6. Wanted local sizes: caption 11 x clamp(text scale, .85, 1.5), pill 10. A text that fits
   at its wanted size shows at .6 of it: caption 6.6 (9.9 at 1.5), pill 6.0 (9.0 at 1.5). The lone launcher is scaled the same way.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-PAIR-LANE-NARROW-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- Every test under "Oracle files" is green; `AbilityRowEditModeTests`, `AbilityFaceTests` (dotnet), `HudRegionsTests`,
  `AbilityRowNearbyNoGarbageEditModeTests` stay green and unedited.
- `bash client-unity/sync-net.sh --check` reports the copy up to date.

## Existing tests that pin this code (none is amended)

Checked by grep for `PromptPair`, `PromptBottom`, `QuestRewards`, `QuestActionLauncher`, `Pickup`, `_promptPair`, `UpdatePromptLane` in
`Assets/Tests/Editor`, `RoguehallaNet.Tests` and `Tools/*.cjs`. None pins the narrow pair or the narrow lone launcher.
- `AbilityFaceTests.TheTwoPromptPillsSitSideBySideFourFromTheCentreInsideThePromptLane` (`RoguehallaNet.Tests/AbilityFaceTests.cs:179-203`, stages 1280,
  1422, 1707 x 800): one-argument `PromptPair(0/1)`: anchor BottomCenter, 220 x 26, left ends 4 left of centre, right starts 4 right, no overlap,
  inside, same top and bottom as `Prompt()`, `PromptPair(0).Y == 130`. The one-argument forms stay.
- `HudRegionsTests` (`RoguehallaNet.Tests/HudRegionsTests.cs:135-140`): `Prompt()` is BottomCenter X 0 Y 130 300 x 26; `Prompt(200f).Width == 200`.
  `:149-160`: the centre-bottom stack (Consumables, AbilityRow, `Prompt()`, feedback pills) is inside, disjoint and ordered with gaps >= 4.
- `AbilityRowEditModeTests.NothingFloatsAboveTheRow` (`:241-255`, windows 1280 x 800 and 1920 x 1080, both s = 1): box measures `Prompt()`, holds only
  the launcher, pickup alone measures `Prompt()`.
- `AbilityRowEditModeTests.APickupAndAQuestActionShareThePromptLane` (`:259-288`, 1280 x 800, s = 1): launcher alone, pair, alone again measure
  `Prompt()` / `PromptPair(i)`. NOTE: its comment on line 272 swallows the `Frame();` that follows it, so the pair step runs without a frame (this
  packet's new tests do not copy that comment; the existing test is not edited).
- `AbilityRowEditModeTests.TheRowShrinksOnANarrowStage` (`:426-449`, 700 x 600): the row scale is between .6 and 1, 8 clear of champion panel and minimap.
- `AbilityRowEditModeTests.ASteadyQuestRefreshFrameAllocatesNothing` (`:472-483`) and `AbilityRowNearbyNoGarbageEditModeTests` (`:285-300`, the
  pair at 1280 x 800, reads `_promptPair`, 200 refresh frames allocate nothing).

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/PromptPairTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/PromptPairNarrowEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/PromptPairNarrowEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-PAIR-LANE-NARROW-1.md`

## 9. Validation commands

- `.../bin/in.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-c dotnet test client-unity/RoguehallaNet.Tests --filter PromptPairTests`
- `.../bin/unity-class.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-c PromptPairNarrowEditModeTests,AbilityRowEditModeTests,AbilityRowNearbyNoGarbageEditModeTests`
- `.../bin/in.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-c bash client-unity/sync-net.sh`
- `.../bin/in.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-c scripts/gate.sh --quick --oracle packet/P-UI-PAIR-LANE-NARROW-1 --base lane/ui-rework/base-3`

## 10. Risk and suggested model tier

implementer-sonnet: two small members in a pure file and one method in a partial. The risk is in the live half (a scale applied twice, a steady-frame
write, `Reset` leaving a stale scale).

## 11. Known traps

- `HudUi.Place` sets the size from the region (`HudUi.cs:46-69`): pass the natural size (220 x 26 paired, 300 x 26 alone) with `localScale`, never the
  scaled size from `PromptPair(1, s)` or `PromptLone(s)` (it would scale twice). It sets the pivot to the anchor point (bottom centre): do not change it.
- Set `localScale` to `(s, s, 1)` in both branches (`Vector3.one` only when `s` is 1); store the last scale in `_promptPairScale` only when placing,
  and compare the raw `localScale.x`, not `PairScale(..)`, so a clamped value never re-places each frame.
- `ResetQuestRewardHud` (rebind, `session == null`) calls `UpdatePromptLane` with the box hidden, so the pair ends and the box is placed at the lone
  place of the current scale there. A `Frame()` after a direct call of that method shows the pair again while the view and the pickup are still
  delivered: test 10 asserts before it.
- Later `Items` calls replace only the state keys they carry (`Collections.Apply`): a pickup-only delivery keeps the earlier quest view and its
  launcher. State items go in ascending key order: Props (2) before Quests (10).
- The viewport change (`HudStage.ViewportChanged` -> `ApplyLegacyViewport`) and the lane run in different calls: the box follows on the next `Frame()`.
- P-UI-ABILITY-MOTION-1 may merge before or after. It plays a press pulse on the pickup pill (a scale pop of about a twelfth for 150 ms, position
  corrected while it plays); it does not animate `_weaponBar.localScale`, change the pill's parent or `PickupRegion`. So no test here sends a pickup
  cue: live rects are compared with the pure layout with no pulse playing.
- Nothing is static: tests restore `UiTheme.Current` and `EventSystem.current`.
- Accepted legibility at the .6 floor: at stage 800 (UI scale .8) the launcher is 132 x 15.6 units when paired, the caption reads about 6.6 units
  (about 5.3 px) and a long giver name shrinks toward 3.6 units, as the pill already does. The label sizes are not changed.

## Not established

- The look: the lane's next Web round judges it (the scaled pair, the lone launcher at the small scale, the caption at 6.6 units).
- The fitted font size of a given string (TMP metrics), and the screen pixel size of the text at the lowest stage scale (.8 px per unit).
- Layout on a stage under 800 units: the narrowest supported stage is 800. Under about 814 units the row itself sits at the .6 floor and loses its 8
  unit margin from the champion panel (at 800 the row clears the panel by .6, below 800 it overlaps it); not this packet's. The pair and the lone
  launcher stay inside the row's footprint, so at 800 they clear the panel (pill left edge 265.6, lone launcher left edge 310, panel right edge 262).

## Notes from the oracle author

(filled in with the oracle commit)

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
