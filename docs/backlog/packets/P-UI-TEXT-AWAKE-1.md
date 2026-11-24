# P-UI-TEXT-AWAKE-1: a text is never measured, fitted or cached while its label has not woken

- **Serves:** not in the plan: owner direction 2026-11-22 (chat of the UI rework lane, UI optimization sweep). A defect class found in wave 3 (the Tab map's zone names measured at a tenth while the map was closed); the sweep found a second place that reaches a player (the trader's rows) and a latent one in `MythicTextScale`.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` (the oracle was written on `f90ca108a`; `TraderDialogView.cs`, `MythicTextScale.cs`, `MythicUi.cs` and `HudUi.cs` are byte-identical to the spec's reference tree `fdd42f54a`). Worktree: `/Users/nevinmckinnon/Documents/Projects/RH-ui-d`.
- **D-rules touched:** D1: legacy (`packages/client/src/ui/traderPanel.css`) fixes no row height and a choice is a button that grows with its text; the packet moves Unity toward legacy, so NO `DEVIATIONS.md` row · D2 n/a · D5/D6 none · D9 untouched (no rule, no input, no wire).

## 1. ID and outcome

A text label created under an inactive parent has had no `Awake`: `TextMeshProUGUI.isOrthographic` is false and every measure (`GetPreferredValues`, `preferredWidth`, `preferredHeight`) returns ONE TENTH of the true value (not 0; measured 2026-11-23, `.agent-runs/lane-ui-rework/understand/exp-zone-names.txt`). `gameObject.activeSelf` does not tell: read `activeInHierarchy`. Two places measure, fit and cache such labels:

**The trader's rows (`TraderDialogView`).** `Initialize` hides its window (`TraderDialogView.cs:87`). The HUD calls `Refresh()` on every collections change (`MatchHud.PlayerFlows.cs:95-102` `RefreshPlayerFlows`, called from `MatchHud.cs:223,225`), so a trade delivered in a match builds its rows under the inactive window: `RebuildMerchants` (:252) and `RebuildChoices` (:277) create labels that have had no `Awake` and end with `ReflowMerchantRows` (:470) and `ReflowChoiceRows` (:485). Those test `row.activeSelf` (true, :475, :490), so `FitHeight` (:503) calls `GetPreferredValues` on a label that measures one tenth (5.32 for 53.2), writes the minimum height (72 merchant, 76 choice, 40 applied stat) and stores `_fitCache[row id] = (text, row width, font)` (:512). `SetVisible(true)` (:108) activates the window and calls `Refresh`; the trade is the same, nothing is rebuilt, and `NeedsReflow` (:454, `activeSelf` at :459) finds the cache equal: no row is measured awake. Neither does `LateUpdate` (:524), the same view, a new view object with the same signature, or closing and opening. A signature change, a click on the other merchant, a resize or (rows once awake) a text-size change heals it. The signature holds the x and y of every merchant in sight, so the state lasts only while every visible merchant is held still and nothing else changes the trade (for example until the first trade, or until another merchant comes into sight). Measured in EditMode, real order, through the real chain: the written height is formula-short in 49 of 60 combinations, 0 of 60 with the fix. A merchant label is always two lines (a name over "Weapon barter" or "Stat trader"), 53.2 high at text size 1, 66.5 at 1.25, 79.8 at 1.5; FitHeight's rule needs 72, 83, 96, the row stays 72. The text leaves its button on the merchant rows from about size 1.4 (1.25 units at 1.4, 3.9 at 1.5, inside the 8 gap; nothing is cut). Applied-stat rows are 40 where the rule gives 43, 50, 56 (their text fits either way). On narrow windows choice rows are short too (980 wide: 76 against 88 and 102).

**`MythicTextScale`.** `HudUi.Label` adds it with `FitBox` and calls `Apply` at once (`HudUi.cs:164-165`); `Fit` (`MythicTextScale.cs:83`) measures through `Fits` (:70) on a label that has not woken: everything "fits" at a tenth, the size stays the wanted size and `_fitted` stores the text (:42). `OnEnable` (:49) and `LateUpdate` (:51) refit only when the text differs from `_fitted`: a long text in a narrow box keeps the wanted size for ever (box 120 wide, label rect 120 x 32, size 14, "Old Wandering Villager from Far Hills": 14 where the awake fit is the floor, 6). No label of today's HUD is harmed (the first terrain chip and the lobby body heal on a text change): this is a guard for the class. The zone label's own defect (fitted against a stretched box, never refitted) and the awake assertion in the fit tests' helper are NOT this packet's (lane lead ruling 2026-11-23: P-UI-TEXT-FIT-1's last revision takes both). No existing test reads a row rect or runs `MythicTextScale.OnEnable`, so none sees either defect.

When this packet lands, in game terms: on the first visible frame of the trader window every row is as tall as its text needs and no text stands out of its button; a never-woken `FitBox` label is fitted once, when it wakes.

## 2. Dependencies

None beyond the lane base. (P-UI-TEXT-FIT-1's last revision, not yet written, owns the fit tests' helper and the zone label: not here.)

## 3. Relevant legacy paths

- `packages/client/src/ui/traderPanel.css:5`: `.trader-choices { display: grid; gap: 8px; }` and no row height anywhere in the file (verified): a choice grows with its text. The Unity change moves toward it.

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/TraderRowsRealOrderEditModeTests.cs` (+ `.meta`)
- `client-unity/UnityProject/Assets/Tests/Editor/AsleepFitEditModeTests.cs` (+ `.meta`)
- this packet.

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/UI/TraderDialogView.cs`: `NeedsReflow`, `ReflowMerchantRows`, `ReflowChoiceRows`, `FitHeight` only (about +5 lines).
- `client-unity/UnityProject/Assets/Scripts/UI/MythicTextScale.cs`: `Apply` only (about +9 lines).
- No new public member, no `RoguehallaNet` change, no existing test amended (none turns red).
- Not in scope: `TacticalMapMarks*.cs`, `TacticalMapController*.cs` (P-UI-TABMAP-TEXT-1), the fit tests' helper and `HudTextScaleEditModeTests.cs` (P-UI-TEXT-FIT-1's last revision), `MythicUi.cs`, `HudUi.cs`, `MatchHud*.cs`. No node test names either file. No word, key or layout number changes (minimums 72, 76, 40, gaps 8 and 4, the 16 and 24 unit allowances stay).

## 6. Contracts and references

- Presentation only; no wire, no rule (R-AUTH, D9). `Roguehalla.Client` namespace, lane rules `.agent-runs/lane-ui-rework/specs/COMMON.md` (tests are a separate assembly: members read by tests are public or reached by reflection).
- Readability gate: a tracked C# file stays under 800 lines (`TraderDialogView.cs` is 594, `MythicTextScale.cs` 101).

## 7. Behaviour to preserve

- Awake labels, `FitBox`-false labels and 3D `TextMeshPro` labels behave as today.
- The control order (window shown, then the trade delivered) is unchanged.
- At the default text size on a wide window merchant and choice rows do not change.
- Word, key and layout numbers do not move.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-TEXT-AWAKE-1` passes (add `--base lane/ui-rework/base-3`).
- The three `Trader*EditModeTests`, `PlayerFlowsEditModeTests`, `UiLabFixesEditModeTests` and the classes of the "Test readers of such a size" list below stay green.

### The change (what the oracle pins)

**Outcome 1. A row whose label is not active in the hierarchy is neither measured nor cached, and opening never moves the scroll position.**
`NeedsReflow` (:459), `ReflowMerchantRows` (:475) and `ReflowChoiceRows` (:490) test `row.activeInHierarchy` where they test `activeSelf` (such a row is skipped); `FitHeight` returns `minimum` right after `if (text == null) return minimum;` (:508) when `!text.gameObject.activeInHierarchy`, before it measures (:510) and before it writes the cache (:512) (no row test reaches that guard, a row's label being active whenever the row is: test 8 does, by reflection). `ReflowChoiceRows` also remembers whether it measured any row and returns before `SetContentHeight` (:500) when it measured none, so a hidden rebuild keeps the estimate `RebuildChoices` wrote (:312, :336, :354). Without that a hidden rebuild leaves content height 0; `ReflowIfLayoutChanged` (:441) then reads `verticalNormalizedPosition` at the next opening from a content no taller than its viewport (ugui answers 0) and writes 0 back after the content grew: a tall list reopens at the bottom. That is the whole change to the file.
While hidden: no `_fitCache` entry; every row keeps its creation height (72, 76, 40); the content height is the rebuild's estimate, 3 x (76 + 8) = 252 for a barter merchant and 2 x 40 + 3 x (76 + 8) = 332 for the stat case (the base leaves 248 and 336, written from tenth measures). Nothing reads row geometry then (`UiFocusNavigation.Cycle` reads none, `SelectMerchant` and `TryTrade` need `IsVisible`). A hidden `LateUpdate` writes no rect. Hidden means not active in the hierarchy: `IsVisible` (:46) reads the root's `activeSelf`, so with an ancestor inactive it can be true while every row is skipped; the first `LateUpdate` after the ancestor wakes measures them. The merchant list has no scroll and no content height: nothing to keep.

**Outcome 2. The call that shows the window makes every row right.** `SetVisible(true)` wakes the labels, then `Refresh` runs `ReflowIfLayoutChanged`: no entry, so every row is measured awake, once, in that call, and the list keeps its scroll position (the read and write-back around the reflow, :448-450). A row is `max(minimum, ceil(preferred.y + 16))`, `preferred` = `GetPreferredValues(text, rowWidth - 24, Infinity)`. Merchants stack 8 apart; applied rows 4 apart, then choice rows 8 apart; content = heights and gaps minus 4. On the first visible frame every row is as tall as its text needs and no text is taller than its button.

**Outcome 3. Later calls change no row and measure nothing again:** `LateUpdate`, `Refresh`, a new view object with the same trade, `SetVisible(false)` then `(true)`: same cache entry objects, same heights and positions. Also right at the first open: a merchant that moves while the window is closed after it was open (rows rebuilt hidden); a text-size change (`MythicUi.ApplyTextScale`) while hidden, with at least one `LateUpdate` running hidden. The control order (window shown, then the trade delivered) is unchanged.

**Outcome 4. What a player sees.** From about text size 1.4 on a wide window a merchant's two lines no longer stand out of their button; on narrow windows every row is as tall as its text needs. At the default size on a wide window merchant and choice rows do not change. Accepted by the lane lead: a stat trader's applied rows are 43 high (were 40; content 342, was 336) at size 1; their text fits either way.

**Outcome 5. The fit guard.** In `MythicTextScale.Apply`, after the `!FitBox` return (:32-37, so after `_scale = scale` at :28), before `Fit` (:38):

```csharp
if (text is TextMeshProUGUI && !text.isOrthographic)   // never woken: every measure reads a tenth
{
    float asleep = Mathf.Max(wanted, MinSize);
    text.enableAutoSizing = false; text.fontSize = text.fontSizeMax = text.fontSizeMin = asleep;
    text.overflowMode = TextOverflowModes.Overflow;
    _fitted = null; return;                             // OnEnable refits when nothing is recorded as fitted
}
```

`OnEnable` (:49) refits when `FitBox && _text != null && !string.Equals(_fitted, _text.text)`, and `string.Equals(null, text)` is false even for "", so a woken label is fitted once, awake; `LateUpdate` is the backstop. Limited to `TextMeshProUGUI`: the 3D `TextMeshPro` (made as `OverheadIndicators.Text`, `Presentation/OverheadIndicators.cs:131-138`) never sets `isOrthographic` in `Awake` and would be skipped for ever. Awake, `FitBox`-false and 3D labels behave as today.

**Outcome 6. In EditMode** the callbacks never run by themselves: a label made asleep keeps the wanted size after `SetActive(true)` until a test calls `OnEnable` (reflection), `Refit()` or `MythicUi.ApplyTextScale`, which fit awake once it has woken. The size read right after creating a label asleep is the wanted size before and after, unless its text is more than about ten times wider than its box (the base measures at a tenth); no existing test is in that case. Evidence (lane spec): the guard alone, full EditMode suite on `be8da07a0`: 2332 tests, 2330 passed (the two `AtmosphereBrushProfileEditModeTests` failures fail the same on the clean tree). The trader change alone ran on five classes (the three `Trader*EditModeTests`, `PlayerFlowsEditModeTests`, `UiLabFixesEditModeTests`): 45 of 45. The gate is the first full run of both.

Test readers of such a size: the terrain chip (`MatchHud.Terrain.cs` `ShowTerrain`; `HudVisualFix4EditModeTests`, the `TerrainChip` row of `HudTextScaleEditModeTests`), the zone labels (`TacticalMapMarks.ZoneLabels.cs` `BuildZoneMarks` keys `_zoneNameFont` on `fontSize`; `ZoneNamesEditModeTests`, `ZoneNamesRealOrderEditModeTests`, `TabMapMarksEditModeTests`), the lobby body (`NameLegibleHubCardEditModeTests`). Four more code sites read a size, read and not harmed: `MatchHud.LeftEdge.cs` `SizeGainsHit` (returns early unless active in the hierarchy), `SentencedHuntPanel.cs`, `ToastLane.cs` and `QuestOfferCard.cs` (read the size, set another, measure, restore). In play, by the rule, the guard moves the zone names at text size 1.5 (15 to 13.35) and the first terrain chip (16.5 to 16.44) at their first wake; no EditMode test sees it. The lane lead accepts both; the lane's last Web round looks at both at the largest text size.

**Not here (hand-off minors):** `HideRows` clears the whole cache (:438); `FitHeight` measures at row width minus 24 for a label row width minus 12 wide; `MeasureWidth` caches its fallback.

### Existing tests that pin this code (verified by reading them; all stay green, none amended)

- `TraderDialogViewEditModeTests` (7 tests, `[SetUp]` builds a non-world-space canvas, `Initialize`, a `Session` on a fake transport, `Bind`): empty/unavailable/available-empty views and a read-only opening; an identical trade replacement keeps the same row objects (`AreSame` on `Merchant_0` and `Choice_0`) and still sends, a changed one rebuilds; duplicate merchants, labels and signed stat deltas in source order (`AppliedStat_i` text); stat-used blocks only the stat trade; current picks queue every stat pick and metadata replacement invalidates old callbacks; disconnect/resume invalidate rows; rebinding an unseen session clears rows. None reads a row rect, height, `_fitCache` or content height.
- `TraderCopyEditModeTests` (6 tests): status line per case; state copy outside a match; no player-facing text mentions the server (reads `GetComponentsInChildren<TMP_Text>(false)` under `TraderDialog`); choice buttons carry the offer alone; sending says "Trading…"; the empty states.
- `TraderWordsEditModeTests` (3 tests): detail heading names the trade ("The Villager · Weapon barter"); the status default reads in words; stat lines use the trade names (`words[i] + "  1"`).
- `PlayerFlowsEditModeTests`: runs the real HUD; `ShowPlayerFlow(1)` / `TraderDialog.IsVisible` at :404-406 and the tab changes at :427-471. Reads no row geometry.
- `UiLabFixesEditModeTests`: its trader lines (:100-134) are about the overhead merchant marker, not the dialog.
- `MythicTextScale` readers (size or fit after `HudUi.Label`): `HudTextScaleEditModeTests` (every `FitBox` HUD label fits), `HudVisualFix2EditModeTests`, `HudVisualFix4EditModeTests`, `NameLegibleHubCardEditModeTests`, `ZoneNamesEditModeTests`, `ZoneNamesRealOrderEditModeTests`, `TabMapMarksEditModeTests`, plus the per-panel fit checks (`AbilityRowEditModeTests`, `ChampionPanelNumbersEditModeTests`, `ControlsLineEditModeTests`, `MatchHudStatusNotesEditModeTests`, `QuestActionPromptEditModeTests`, `QuestOfferEditModeTests`, `PromptPairNarrowEditModeTests`, `QuestTrackerEditModeTests`, `WishTileEditModeTests`, `TrainingBannerEditModeTests`). They create labels awake or under an active parent in the usual fixtures; the lane spec's guard-only run of the full suite had them all green.

No conflict between these tests and the packet: none needs an `oracle-amend:`.

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/TraderRowsRealOrderEditModeTests.cs` (holds two classes: `TraderRowsRealOrderEditModeTests`, tests 1-6 and 8, and `TraderRowsWholeHudEditModeTests`, test 7)
- `client-unity/UnityProject/Assets/Tests/Editor/TraderRowsRealOrderEditModeTests.cs.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/AsleepFitEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/AsleepFitEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-TEXT-AWAKE-1.md`

There is no `stub:` commit: the tests use only public API (`TraderDialogView`, `MythicTextScale.Apply`, `MythicUi.ApplyTextScale`, `HudUi`) and reflection on existing private members, so they compile on the base.

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> TraderRowsRealOrderEditModeTests,AsleepFitEditModeTests`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> TraderDialogViewEditModeTests,TraderCopyEditModeTests,TraderWordsEditModeTests`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> PlayerFlowsEditModeTests,UiLabFixesEditModeTests`
- the classes of outcome 6 (terrain chip, zone names, lobby body readers), then the gate once.
- No `Tools/hud-seams.test.cjs` change expected (no `MatchHud` member is renamed); no node test names either file.

## 10. Risk and suggested model tier

Sonnet: two small edits with exact code in this packet. The risk is a test-order trap: the fixtures follow the real order (build hidden, deliver, refresh hidden, then show), and an implementer who guards only `FitHeight` leaves the three row tests (`NeedsReflow` and the two reflows) reading `activeSelf` and the content height at 0.

## 11. Known traps

- Change only the four members and `Apply`: guard `FitHeight` AND the three row tests, keep the early return of `ReflowChoiceRows` (it keeps the scroll); leave the hand-off minors. No layout number, word or key moves.
- `Apply`: the guard comes after `_scale = scale` (a woken label refits at the stored scale) and after the `FitBox` return; the flag is `isOrthographic`, never `activeInHierarchy` (a woken label hidden again measures right); never extend it to `TextMeshPro`.
- The tests pin private names: `_fitCache`, `FitHeight`, `OnEnable`, `LateUpdate`, the row object names (`Merchant_i`, `Choice_i`, `AppliedStat_i`), `_textScale`: do not rename them. `TraderDialogView` is not `[ExecuteAlways]`: its `LateUpdate` never runs in EditMode.
- Test 6 needs at least one hidden `LateUpdate` (two are harmless): with none the base heals at `SetVisible(true)`.
- Not established: the Web player (TMP `Awake` timing, fonts); the scroll position at the FIRST opening of a tall list (by reading the code it is already at the bottom on the base: `RebuildChoices`, outside the write scope, reads an empty content, which ugui answers 0, and writes it back; never asserted); the ancestor-inactive state of outcome 1 (by reading); allocation of a hidden `LateUpdate`.

## Notes from the oracle author

**Red and green on the base (run through `unity-class.sh`, 40 cases: 6 green, 34 red; every red is an assertion).**

- `TraderRowsRealOrderEditModeTests.EveryRowIsRightOnTheFirstFrameOfARealMatch` (12 cases): red in 11, each on "X is as tall as its text needs (n)" (a Merchant row 72 against 83 or 96 or more; `stat` at size 1.0 wide: AppliedStat 40 against 43). The sight lines (text not taller than its button) are red too, collected by the scratch run (merchants at 1.5 wide: 79.8 > 72). Green: `barter`, 1.0, wide (standing guard, as the spec says). The layout lines (position of each row, content = sum minus 4) are derived from the heights actually written and are green on the base in the stuck state: they guard the stacking rule once the heights are right.
- `TheRealOrderEqualsTheControlOrder` (12): red in 11 on "row i height, real order against control order" (real 72, control 83/96/...; stat 1.0 wide: 40 against 43, content 336 against 342); `barter`, 1.0, wide is the standing guard.
- `AHiddenWindowMeasuresAndCachesNothing` (barter, stat): red on "no row was measured and cached while hidden" (expected 0, was 5 and 7). The content line is red too (scratch run: 248 against 252, 336 against 332). The 55-height and the `SetVisible(true)` cache-count lines are green standing guards.
- `LaterCallsChangeNoRowAndMeasureNothingAgain` (barter, stat): red on the opening formula (Merchant_0 72 against 96). The `AssertUnchanged` lines after it are green on the base (standing); on the fixed code they pin that nothing is re-measured.
- `AReopenedWindowIsRightAfterARebuildWhileClosed` (barter, stat): red on the formula (72 against 96). The scroll-position line (stat) is a standing guard, green on the base, as the spec says.
- `ATextSizeChangedWhileHiddenIsRightAtTheFirstOpen`: red on the formula (72 against 96; sight 79.8 > 72).
- `TraderRowsWholeHudEditModeTests.TheWholeHudRealChain` (barter, stat): red on the formula (Merchant_0 72 against 96). Widths are the editor's overlay canvas (relations only).
- `FitHeightOnAHiddenRowReturnsTheMinimumAndCachesNothing`: red on "and stores nothing" (expected 0, was 1); the result-is-72 line is green.
- `AsleepFitEditModeTests.AnAsleepLabelIsFittedWhenItWakes`: red on "once awake it is fitted like its twin" (expected 6, was 14). `ATextSizeAppliedWhileAsleepIsFittedWhenItWakes`: red on the same line (expected 6, was 21).
- Green standing guards (as specified): `ATextThatFitsKeepsTheWantedSizeEitherWay`, `ATextChangedWhileAsleepIsFittedWhenItWakes`, `AnAwakeLabelStillFitsAtOnce`, `AThreeDimensionalLabelIsStillFittedAtOnce` (its set-up read `isOrthographic` false, so no stop-and-report).

**Existing classes, green on the base (same run command):** `TraderDialogViewEditModeTests`, `TraderCopyEditModeTests`, `TraderWordsEditModeTests`, `PlayerFlowsEditModeTests`, `UiLabFixesEditModeTests`: 45 of 45. `HudVisualFix4EditModeTests`, `HudTextScaleEditModeTests`, `NameLegibleHubCardEditModeTests`, `ZoneNamesEditModeTests`, `ZoneNamesRealOrderEditModeTests`, `TabMapMarksEditModeTests`: 81 of 81.

**Scratch run (not committed).** For each new class I made a copy in which every `Assert.` statement was wrapped in a catch-and-collect call (catches `AssertionException`, keeps the message, carries on; the list is written out in TearDown and the test does not fail). 40 of 40 ran to the end with no exception other than assertion failures, so no line is a set-up defect (object paths, deliveries in key order, the scroll set-up, the reflection call all execute). Every collected entry is an assertion about the missing behaviour (heights, cache count, content estimate, sight, real against control, fit after wake). Differences from the spec's lists: (a) the `barter`, 1.0, wide case of tests 1 and 2 is the only green case of each, as the spec says; (b) narrow `stat` at 1.0: Merchant_1 is 72 against 96 and the applied rows 40 against 43, so the red set is the spec's. I did not remove the early return of `ReflowChoiceRows` to show the scroll line red (the scratch would have meant editing product code, which an oracle author does not do): the spec's claim that line is red only on an implementation that writes content height 0 is not demonstrated here; on the base it passes, as the spec says.

**Where the tests differ from the spec text.** Test names and combinations follow the spec (12 combinations as a `[Combinatorial]` over case x scale x canvas). Test 7 lives in a second class in the same file (it needs a different fixture); the file is about 500 lines. Rows are found by name through `View.Root` (public) instead of `canvas.transform.Find("TraderDialog/...")`: the same objects. A new view object per delivery carries a rising tick. The base `lane/ui-rework/base-3` has moved since the spec to `f90ca108a` (P-UI-TABMAP-TEXT-1 and P-UI-QUEST-OFFER-1-fix merged); the two product files are byte-identical to the spec's tree, so nothing in the spec changes.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
