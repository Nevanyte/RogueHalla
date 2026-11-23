# P-UI-FLOWS-LEFTOVERS-1: journal fields only with a value, one CLOSE, trader list at the top, "Run over"

- **Serves:** not in the plan: owner direction 2026-11-22 (chat of the UI rework lane: "Quest UI could be perfected",
  "Please do a UI optimization sweep"). Seen in Web round 3a (2026-11-23): an empty PARTICIPANTS heading on every held
  quest, CLOSE in a different place on each tab; measured by the oracle check of P-UI-TEXT-AWAKE-1: a tall trader list
  opens at the bottom.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (head `c8e21c0e1`), stacked on `feature/unity-rework`
- **D-rules touched:** D1: presentation only. Legacy shows no participants, labels no name (`packages/client/src/ui/questLog.ts:180`
  puts the name in a heading of the row head, `:206` appends giver, objective, meter and progress with no participants),
  labels only the reward, and says "Run over" (`packages/client/src/ui/hud.ts:54`): items 1, 2 and 6 move toward legacy.
  Its trader panel has no scroll container (`packages/client/src/ui/traderPanel.css`); CLOSE has no counterpart across tabs.
  No difference is added: no `DEVIATIONS.md` row. D2 n/a. D5/D6 none. D9 none (no key is bound, nothing a key does changes).
- **Worktree:** `/Users/nevinmckinnon/Documents/Projects/RH-ui-d`
- **Evidence (lane folder, main checkout):** `.agent-runs/lane-ui-rework/understand/web3-journal-leftovers.txt` (its CHECK
  governs), `web3-results-leftovers.txt`, `webround/round-3a/` pictures 51, 55, `results/P-UI-TEXT-AWAKE-1.json`.
- Units: the windows sit on the `MatchUI` canvas (1920 x 1080, `MatchHud.cs:77-79`), not on the 1422 x 800 HUD stage:
  canvas units at 16:9 (1 stage unit = 1.35). "Computed" = from the evidence's metrics (label 24.56 x text size s), not run.

## 1. ID and outcome

Four leftovers of the three window tabs (Quests, Traders, Notices), all in presentation code under
`client-unity/UnityProject/Assets/Scripts/UI/`.

What is wrong today:

1. **Empty heading.** `QuestJournalView.RenderSelection` (`QuestJournalView.cs:346-399`) flips `_emptyHidden` (`:58`, built
   `:106-110`: the QUEST NAME `FieldLabel` object, which spends 24.56 s + 2 above a name the list row already says, and the
   field objects OBJECTIVE, GIVER, REWARD, PARTICIPANTS) on one condition, `has` (a quest is selected), when `has` changes
   (`_hasState`, `:350-354`). The board path sends every row with `participants: Vec::new()`
   (`crates/rh-sim/src/quest_view/board.rs:109`), so `ParticipantCopyCached` returns "", the value has no height and the
   heading stands over nothing (picture 51). Only BRIEF has a per-field rule (`QuestJournalCopy.ShowsBrief`, `:358-359`);
   an empty OBJECTIVE, GIVER or REWARD does the same.
2. **CLOSE.** Three `Initialize` methods hard-code three rectangles: Quests `CloseJournal` (`QuestJournalView.cs:79`) x
   .79-.965, y .925-.99, Quiet; Traders `Close` (`TraderDialogView.cs:59-60`) x .87-.975, y .915-.995, Quiet, then a raw
   `fontSize = 13`; Notices `Close` (`OrderedNoticeFeed.cs:48`) x .82-.98, y .92-.99, Secondary (the default). The roots share
   anchors (.12,.12)-(.88,.915).
3. **Trader list.** `RebuildChoices` (`TraderDialogView.cs:277`) reads `_choiceScroll.verticalNormalizedPosition` (`:280`)
   before it clears the rows. On the first build the content is 0 high and ugui answers 0 for content not taller than its
   viewport; that 0 is written back (`:358`) once the rows exist, so a taller list stands at the BOTTOM
   (`ReflowIfLayoutChanged`, `:441-452`, repeats it around the opening's reflow: stat trader, text size 1.5, wide canvas,
   estimate 332, opened 368, viewport 288.06; the check read 3.8e-07 with P-UI-TEXT-AWAKE-1, as on this base). The same 0
   is read for a list shorter than its viewport that a rebuild grows (a wrapped barter label on a short canvas).
   `SelectMerchant` (`:191-200`) keeps the old position for another trader's list.
4. **Words after the end.** `QuestJournalView.Refresh`, branch `State != InMatch` (`:221-227`), writes "Quest journal
   unavailable while disconnected." for every state but Reconnecting, Ended included, by a plain `_state.text =` write
   (`:225`). `TraderDialogView.StateCopy` (`:584-594`) returns "Match ended" for Ended (`:591`). The banner reads
   `PlayerWords.Word(SessionState.Ended)` (`RoguehallaNet/Net/Ui/PlayerWords.cs:288`), "Run over" (P-UI-PLAYER-WORDS-3).

What is true when this packet lands:

1. **A field is shown only when it has a value.** In `RenderSelection` write every text first, then show the field object
   only when `has && !string.IsNullOrEmpty(text)`, compare before set (`activeSelf != on`), for OBJECTIVE, GIVER, REWARD and
   PARTICIPANTS; BRIEF keeps `has && ShowsBrief(q)`. Cache the field objects in `Initialize`; `_emptyHidden` and `_hasState`
   go. Nothing selected: all hidden; the name reads "Select a quest". The field order is unchanged and no word changes
   except the two after-the-end texts (6).
2. **No QUEST NAME heading.** Its `FieldLabel` object is set inactive once in `Initialize`, never touched again (it stays in
   the tree). The `QuestName` field is always active: guidance when nothing is selected, the name otherwise, first line of
   the details. An empty name leaves a zero-height field and a 10 unit gap (never sent: the catalog defs read,
   `crates/rh-sim/src/quests/catalog.rs:74-200`, all carry a name).
3. **Height gained** (computed): QUEST NAME 24.56 s + 2; an empty PARTICIPANTS field 24.56 s + 12; together 49.12 s + 14:
   63.1 at s 1, 75.4 at 1.25, 87.7 at 1.5. A one-line board quest: 318.3 becomes 255.2 at s 1. With two action rows (pane
   292.1) it scrolls above about s 1.17, wraps ignored. A two-line objective just fits at s 1 (290.1 of 292.1, a knife edge
   that is NOT pinned); with a two-line reward too it scrolls (323.1). A quest with two action rows and wrapped texts can
   still be cut by the pane's end: accepted, nothing pinned. Picture 55 fills 686 of 691.
4. **One CLOSE.** `FlowWindowClose` (below) holds the Traders values; the code gives no reason to leave them: x .975 is the
   tab strip's right edge, y .915-.995 clears the tabs (.825-.90), no title reaches x .87, `JournalState` ends at y .80. At
   16:9: x 1500 to 1653, y 96 to 165 from the top. The three views create the button through `FlowWindowClose.Create`; names
   stay `CloseJournal`, `Close`, `Close`, handlers stay. Notices becomes Quiet (a visible change for the owner: its CLOSE
   was the filled Secondary look); the raw `fontSize = 13` line is deleted (not established as smaller: do not claim it).
   **The label fits its box.** It is a plain `MythicUi.Text`, not a `FitBox` label: at text size 1.5 it is 36 units and TMP
   may shrink it only to .85 of that (30.6); "CLOSE" cannot wrap; its box is the button width minus 12 (the insets). The
   share .105 is 153.2 wide at 1920, 102.1 at 1280 x 800 and 78.2 on 980 x 2121. Where it does not fit, the shared class
   gives the button a minimum width, `FlowWindowClose.MinWidth` in canvas units: it grows to the LEFT from x .975, top and
   bottom unchanged. The number is the measured preferred width plus 12, rounded up (see "Notes from the oracle author":
   the stub holds 0). It holds when the canvas changes size (the HUD's scaler follows the window), so it is not an offset
   computed once from `rect.width` in `Create`. No text-size rule changes.
5. **A trader's list stands at its top** at every opening and when another trader's list replaces it; a rebuild while open
   keeps the position. Opening is the hidden-to-shown edge of `SetVisible` (a tab click calls `SetVisible(true)` on an open
   window: no move). "Another trader" is a different (index, `Kind`, `Name`) of the selected merchant, or no list before
   (bind, timeline reset, `ClearTradePresentation`); the same merchant moving or a click on the selected row is the same
   trader. The position is right AFTER the reflow: while the top is pending the write-back writes 1, not the value read
   before the reflow, and it stays pending until a reflow has measured rows awake. A rebuild while open writes its
   remembered position after `ReflowChoiceRows` (today before, `:358-359`, so growing 332 to 368 turns .4 into about .67,
   computed). **The position is READ only when it means something:** before a rebuild or a reflow it counts when the
   content was taller than the viewport then (`content.rect.height > viewport.rect.height + .5`), otherwise the read counts
   as the top, 1. This restates P-UI-TEXT-AWAKE-1's "opening never moves the scroll position" as "opening puts the list at
   the top"; its tests stay green (read).
6. **Words after the end,** by session state. Quests (`Refresh`, `_state`): null session "Quest data is not connected."
   and Reconnecting "Reconnecting · quest actions paused" stay; **Ended `PlayerWords.Word(SessionState.Ended)`**; Idle,
   Connecting, HelloSent, Rejected, Failed, Detached keep "Quest journal unavailable while disconnected.". Traders
   (`StateCopy`): Reconnecting, Failed, Rejected and the default `PlayerWords.Word(state)` (Idle, Connecting, HelloSent,
   Detached) keep their words; **Ended returns `PlayerWords.Word(state)`**, no literal. InMatch with a result (a dead
   player) is unchanged. The Quests tab's state text goes through the compare-before-set helper `SetText` (`:462-467`) in
   all of `Refresh` (the three `_state.text =` writes: `:225`, `:238`, `:246`), so a steady frame writes and allocates
   nothing; no new colour or motion.

## 2. Dependencies

Base `lane/ui-rework/base-3`. P-UI-TEXT-AWAKE-1 (merged, `docs/backlog/packets/P-UI-TEXT-AWAKE-1.md`),
P-UI-PLAYER-WORDS-3 (merged) and P-UI-ALLOC-METER-1/-2 (`Assets/Tests/Editor/AllocationMeter.cs`, present on the base).

## 3. Relevant legacy paths

- `packages/client/src/ui/questLog.ts:180`, `:206`: the quest row head carries the name only; the body lists giver, objective,
  meter, progress, the reward: no participants, no field labels except the reward (verified by reading).
- `packages/client/src/ui/hud.ts:54`: `"Run over"` is legacy's word for the end (verified with `rg "Run over"`).
- `packages/client/src/ui/traderPanel.css`: no scroll container in the legacy trader panel.

## 4. New paths

- `client-unity/UnityProject/Assets/Scripts/UI/FlowWindowClose.cs` and `.meta` (`/// <summary>` before the type).
- `client-unity/UnityProject/Assets/Tests/Editor/FlowsLeftoversEditModeTests.cs` and `.meta` (oracle).

`FlowWindowClose` (namespace `Roguehalla.Client`), as the stub holds it:

```csharp
public static class FlowWindowClose
{
    public static readonly Vector2 AnchorMin = new Vector2(.87f, .915f), AnchorMax = new Vector2(.975f, .995f);
    public const MythicUi.ButtonStyle Style = MythicUi.ButtonStyle.Quiet; public const float FontSize = 16f;
    public const float MinWidth = 0f;   // canvas units (Outcome 4); the stub holds 0, the implementer sets 109
    public static UnityEngine.UI.Button Create(Transform card, string name, UnityEngine.Events.UnityAction onClick)
        => MythicUi.Button(card, name, "CLOSE", AnchorMin, AnchorMax, onClick, Style, FontSize);  // + MinWidth rule
}
```

(The stub's `Create` returns null so the tests compile and fail on assertions; the implementer replaces it with the line
above plus the MinWidth rule, and sets `MinWidth` to the measured number.)

`TraderDialogView` gains `int _listMerchant = -1; TradeKind _listKind; string _listName; bool _listToTop = true;`,
`float ReadList()` (the scroll's `verticalNormalizedPosition` when the content is taller than the viewport + .5, else 1; 1
without a scroll), `bool ListChanged(TradeView view)` (the selected merchant's key is (index, `Kind`, `Name`) when `view` is
available and the index in range, else -1; stores it, true when it differs), `void PlaceList(float read, bool measured)`
(writes `_listToTop ? 1 : read` to the scroll's `verticalNormalizedPosition`; clears `_listToTop` if set and `measured`) and
`bool ChoiceRowsAwake()` (no rows, or one active in the hierarchy). Call sites: `RebuildChoices` reads `float scroll =
ReadList();` (today the raw position), after `HideRows`: `if (ListChanged(view)) _listToTop = true;`, its end
`PlaceList(scroll, ReflowChoiceRows());` (the old write before the reflow goes; early returns stay); `ReflowChoiceRows`
returns its `measured`; `ReflowIfLayoutChanged` reads `ReadList()` and calls `PlaceList(scroll, ReflowChoiceRows())`;
`SetVisible` sets `opening = visible && !activeSelf` before `SetActive`, `_listToTop = true` on it and, after `Refresh()`,
`if (opening && _listToTop) PlaceList(1f, ChoiceRowsAwake())`; `ClearTradePresentation` sets `_listMerchant = -1;
_listToTop = true`.

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/UI/`: `QuestJournalView.cs` (`Initialize`, `RenderSelection`, `Refresh`'s state
  text, a `Show` helper), `TraderDialogView.cs` (Outcome 5, 6, `StateCopy`), `OrderedNoticeFeed.cs` (the CLOSE line), new
  `FlowWindowClose.cs` and `.meta` (the three files are 638, 598 and 134 lines now; the limit is 800).
- Tests (oracle, read-only for the implementer): new `Assets/Tests/Editor/FlowsLeftoversEditModeTests.cs` and `.meta`; this
  packet document.
- Amended expectations, each its own `oracle-amend:` commit, listed for the owner (see "Oracle amendments" below).
- Not in scope: every `MatchHud*.cs` (P-UI-RUN-END-1 owns the HUD's words for the end, not these tab texts),
  `QuestActionStrip.cs`, `ScrollThumb.cs`, `ScrollIndicator.cs`, `KeyboardScrollPane.cs`, `MythicUi.cs`, `PlayerWords.cs`,
  `ConnectionCopy.cs`, other tests, `Tools/*.cjs`. No other open packet edits the three views (P-UI-PLATES-OPAQUE-1 leaves
  them out); merged packet documents stay. Not done: scroll-follow on keyboard focus, the band above the details pane, a
  better scroll cue.
- Visible changes the hand-off lists for the owner: the Notices CLOSE look (Secondary to Quiet), the three CLOSE places, the
  QUEST NAME heading and every empty field gone, the two after-the-end texts, a CLOSE minimum width.

## 6. Contracts and references

- R-AUTH / D9: no game rule moves to Unity; text and visibility only, from delivered state.
- Lane rules (`.agent-runs/lane-ui-rework/specs/COMMON.md`): text through `MythicUi.Text`, theme colours only, no allocation
  in a steady frame, `MatchHud` untouched, `client-unity/UnityProject/Tools/hud-seams.test.cjs` untouched (no `MatchHud`
  member is renamed or removed).

## 7. Behaviour to preserve

- The field order of the details (Name, Objective, Giver, Brief, Reward, Participants), the 10 unit stack gap, and every
  word except the two after-the-end texts.
- P-UI-TEXT-AWAKE-1: no row is measured or cached while hidden; `ReflowChoiceRows` keeps its early return and the estimates
  252 and 332; its tests stay green.
- Button names `CloseJournal`, `Close`, `Close` and their handlers; `QuestName/FieldLabel` stays in the tree (inactive),
  `AssertHeadings` resolves it by `Req`.
- Existing tests that pin this code, each verified by reading (all stay green unless listed under "Oracle amendments"):
  - `QuestJournalDetailsEditModeTests` (`Assets/Tests/Editor/QuestJournalDetailsEditModeTests.cs`):
    `NothingSelectedShowsOneLineOfGuidance` (:237-257) asserts no heading before a selection, every field hidden, the name
    reads "Select a quest", and after a held row is selected QuestName, QuestObjective, QuestGiver, QuestReward and
    QuestParticipants all have an active heading (:251-252): **amended**. `AssertHeadings` (helper :625-641, used by
    `NoHeadingIsTallerThanItsRowAndNothingOverlaps` :263 at scale 1 and 1.5, and
    `TheHeadingsAreRightWhenTheJournalOpensAfterDeliveriesWhileHidden` :277) asserts that every active field has an active,
    tall-enough heading: **amended**. `RewardStandsUnderTheGiverWhateverTheActions` (:74) asserts giver-to-reward 10 and brief
    above reward; `AnUnchangedRefreshWritesNoRectAndAllocatesNothing` (:379) asserts `Refresh` allocates nothing with a
    selection; `TheScrollReturnsToTheTopWhenTheSelectionChanges` (:291), `TheIndicatorShowsOnlyWhileTheContentIsTaller` (:318)
    read content heights of long rows (all fields filled, so unchanged by the field rule).
  - `QuestJournalViewEditModeTests` (:256-295): participant text is read from `Value.text` of a possibly hidden field (so texts
    are written before a field is toggled), "" after a replacement without participants, "Select a quest" after a rebind;
    :113-130 the states "Waiting for quest data…", "Quest journal unavailable.", "No quests are currently available.";
    :359 `Reconnecting · quest actions paused` and :372 "Waiting for quest data…" after a Welcome.
  - `QuestJournalPolishEditModeTests.TheObjectiveComesFirstAndTheRepeatedBriefIsHidden` (:126-146): sibling order Name,
    Objective, Giver, Brief; the brief is hidden when it equals the objective, its text still written verbatim.
  - `PlayerFlowsEditModeTests` :441 and :503, `TabMapSheetEditModeTests` :504: find `CloseJournal` and click it; :503 reads
    `QuestName/Value`.
  - `TraderCopyEditModeTests` (`ASessionNotInAMatchShowsTheStateCopyAlone`, :75-96): Reconnecting reads "Connection interrupted ·
    resuming" (:80) and Ended reads "Match ended" (:93): **amended**. `NoPlayerFacingTextMentionsTheServer` (:99-132) checks
    no visible text of the window says server/authoritative/delivered/session in each state, Ended included ("Run over" is
    clean).
  - `TraderWordsEditModeTests.TheStatusDefaultReadsInWords` (:57-75): Idle "Not connected", HelloSent "Connecting…", Detached
    "Disconnected" through `PlayerWords.Word`.
  - `TraderRowsRealOrderEditModeTests` (rows right on the first frame of a real match, hidden window measures and caches
    nothing, later calls change no row, `AReopenedWindowIsRightAfterARebuildWhileClosed` :175-199 asserts the stat list
    reopens at 1 after a rebuild while closed); `TraderRowsWholeHudEditModeTests` (:443-506).
  - `TraderDialogViewEditModeTests`: selection, rebind and stale-view guards; reads `ChoiceScroll/Viewport/Content` only.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-FLOWS-LEFTOVERS-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- The 20 tests of `FlowsLeftoversEditModeTests.cs` are green; the three amended expectations are the only changed lines of
  existing tests.
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` still passes (no `MatchHud` member touched).

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/FlowsLeftoversEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/FlowsLeftoversEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-FLOWS-LEFTOVERS-1.md`

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/unity-class.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-d JournalFieldsEditModeTests,FlowCloseEditModeTests,TraderListTopEditModeTests,FlowEndWordsEditModeTests`
- the existing classes that pin the code: `QuestJournalDetailsEditModeTests,QuestJournalViewEditModeTests,QuestJournalPolishEditModeTests,TraderCopyEditModeTests,TraderWordsEditModeTests,TraderDialogViewEditModeTests,TraderRowsRealOrderEditModeTests,TraderRowsWholeHudEditModeTests,PlayerFlowsEditModeTests,TabMapSheetEditModeTests`.
- `scripts/gate.sh --quick --oracle packet/P-UI-FLOWS-LEFTOVERS-1 --base lane/ui-rework/base-3` through `bin/in.sh`.

## 10. Risk and suggested model tier

implementer-sonnet; reviewer. Three views, one new small class, no library change, no wire. The hard part is the scroll
position arithmetic of Outcome 5 (reads that mean nothing, write after the reflow) and a CLOSE label that must fit at text
size 1.5 on a narrow canvas.

## 11. Known traps

- `RenderSelection`: write the texts BEFORE toggling a field (`QuestJournalViewEditModeTests` reads `Value.text` of a possibly
  hidden field, :269-293); `Initialize`'s first call must write `false` to the toggles (they start active). Nothing else
  reads `_emptyHidden` or `_hasState`. `QuestName/FieldLabel` stays in the tree, inactive. If the allocation test of the
  settled refresh fails on the no-selection write `_status.text = ""` in `RenderSelection` (:375), that write goes through
  `SetText` too.
- `PlaceList` is the only writer of the choice scroll after a rebuild or reflow; a steady `LateUpdate` reaches neither
  (`NeedsReflow` is false). Do not clear `_listToTop` on a reflow that measured nothing (hidden rows are skipped).
  P-UI-TEXT-AWAKE-1's early return and estimates (252, 332) stay.
- A player's own leave reads "Run over" on both tabs, like the banner: accepted (the product unbinds the HUD before it leaves
  the session; unverified here).
- `Create` passes no size (`MythicUi.Button` adds 44 minimums, `MythicUi.cs:127`). Keep the names: `PlayerFlowsEditModeTests`
  :441 and `TabMapSheetEditModeTests` :504 find `CloseJournal`; the console's own `Close` (`ui-scenarios.cjs`,
  `TrainingConsoleIntegrationEditModeTests` :563) shares the name `Close`.
- Not established: the mid-list position in `ReflowIfLayoutChanged` drifts when the content height changes under it (it
  writes the normalized value read before the reflow): left as it is; only the top and the rebuild path are pinned.
  Unverified at brief time (the oracle author's scratch run settles them, see below): the width of "CLOSE" at 30.6; that
  `OrderedNoticeFeed` builds standalone on a host; that two wrapped lines at 1.5 lift a barter list past 266.7; the ugui
  getter's 0 for content not taller than the viewport; the red status of tests 15 and 16; that every quest name is
  non-empty; every "computed" height.

## Oracle amendments (for the owner)

Each is its own `oracle-amend:` commit.

1. `QuestJournalDetailsEditModeTests.NothingSelectedShowsOneLineOfGuidance` (:251-252): after selecting a held row without
   participants, QuestObjective, QuestGiver and QuestReward have an active heading; QuestName and QuestParticipants have none
   (kind: re-statement of the new field rule and of Outcome 2).
2. `QuestJournalDetailsEditModeTests.AssertHeadings` (helper, :636-639): its heading rules skip `QuestName` (kind: loosening:
   "every active field has a visible heading" is dropped for the name; :632-634 still check the value and the order).
3. `TraderCopyEditModeTests.ASessionNotInAMatchShowsTheStateCopyAlone` (:93): Ended reads `PlayerWords.Word(SessionState.Ended)`,
   not "Match ended" (kind: re-statement; legacy `hud.ts:54`).

## Oracle: four classes, 20 tests

All in `FlowsLeftoversEditModeTests.cs`. No `Assert.Multiple`, no `InternalsVisibleTo`; no test reads `Time` or the wall
clock (state is delivered; `Refresh` and `LateUpdate` are called by hand). Each class has a case in the real order: build
hidden, deliver, refresh hidden, then open. A test green on the base is a named standing guard; any other is red on it.

**`JournalFieldsEditModeTests`** (rig of `QuestJournalDetailsEditModeTests`: WorldSpace 1920 x 1080 canvas).
1. `AHeldQuestWithNoParticipantsShowsNeitherHeadingNorValue`. 2. `AParticipantShowsHeadingAndLines`. 3. `AnEmptyFieldHidesItsHeading`
([TestCase] objective, giver, reward). 4. `TheNameHasNoHeadingAndLeadsTheDetails`. 5. `TheFieldOrderAndTheStackAreUnchanged`
(standing guard). 6. `ASettledRefreshAllocatesNothing`.

**`FlowCloseEditModeTests`** (three views on one canvas at 1920 x 1080, 1280 x 800, 980 x 2121).
7. `TheThreeCloseButtonsStandInOnePlace`. 8. `TheThreeCloseButtonsLookTheSame`. 9. `TheCloseLabelsShareOneTextSize` (standing
guard). 10. `TheCloseLabelFitsItsButtonAtTheLargestTextSize`. 11. `EachCloseKeepsItsNameAndStillCloses` (standing guard).

**`TraderListTopEditModeTests`** (text size 1.5, 1920 x 1080). 12. `AStatListFirstOpensAtTheTop`. 13. `EveryOpeningStartsAtTheTop`.
14. `AnotherTradersListStartsAtTheTop`. 15. `ARebuildWhileOpenKeepsThePosition`. 16. `ABarterListThatGrowsPastTheViewportStandsAtTheTop`
(standing guard, see the notes). 16b. `ALongBarterListFirstOpensAtTheTop` (added by the oracle author, see the notes).

**`FlowEndWordsEditModeTests`**. 17. `TheQuestsTabSaysTheEndedWord`. 18. `TheTradersTabSaysTheEndedWord`. 19. `ADisconnectKeepsItsOwnWords`
(standing guard).

## Notes from the oracle author

Scratch run: one run per class with every `Assert` replaced by a catch-and-collect helper (`AssertionException` kept,
test failed in a TearDown with the list; not committed). Every entry on the stub was an assertion about the missing
behaviour; no set-up, delivery or lookup threw. Two defects found and fixed this way before the commit: test 10 measured
the label with a width bound of 0 (below), and the first draft had no test that is red for a barter list on first opening.

Results on the stub (`unity-class.sh` on the four classes, 28 tests with the added one): red on an assertion, green as
standing guards (each is named; a guard is green on the base by design).

- Green on the stub (standing guards): 5 `TheFieldOrderAndTheStackAreUnchanged`, 9 `TheCloseLabelsShareOneTextSize`,
  11 `EachCloseKeepsItsNameAndStillCloses`, 16 `ABarterListThatGrowsPastTheViewportStandsAtTheTop`, 19 `ADisconnectKeepsItsOwnWords`.
- Red: 1, 2 (the delivery without participants), 3 (x3), 4, 6 (set-up: the empty reward and participants are still shown): the
  field or heading is active when it should not be. 7 (x3): the Quests CLOSE has anchorMax (.965, .99), Traders' left edge and
  the Notices' right edge differ. 8: Notices' `normalColor` is `SurfaceRaised`. 10 (x3): Traders' label needs 96.24 and has 90.14
  (1280 x 800) or 66.20 (980 x 2121). 12: position 3.8e-07 at the first opening. 13: position .4 after a rebuild while closed and
  after a reopening. 14: .670 for another trader's list and for a click on the selected row. 15: .670 after a rebuild while open.
  16b: position -4.8e-08. 17: "Quest journal unavailable while disconnected." and the word is the state text. 18: "Match ended".

Where the lane spec was corrected by measurement:

- **Test 10's recipe (`GetPreferredValues("CLOSE", 0, 0).x`) measures one capital (23.48), not the word**: a width bound of 0
  breaks a word letter by letter, so the spec's recipe is green on every canvas for the wrong reason. The test measures with a
  box wide enough for the line (`GetPreferredValues("CLOSE", 100000, 0).x`): 96.24 at the smallest size TMP may use (30.6, text
  size 1.5), on all three labels. Label widths (button width minus 12, text size 1.5): Quests 243.4 / 158.2 / 118.3, Notices
  221.5 / 143.6 / 107.2, Traders 141.2 / 90.1 / 66.2 at 1920 x 1080 / 1280 x 800 / 980 x 2121. Only Traders does not fit, at 1280 x 800
  and on the narrow canvas: a measured red, as expected by the spec. **`FlowWindowClose.MinWidth` is 109** (96.24 + 12 = 108.24,
  rounded up). The test also resizes the canvas to 980 x 2121 and reads again with no frame in between (`Canvas.ForceUpdateCanvases`
  only): the minimum width must follow a change of the canvas by a rectangle callback (`OnRectTransformDimensionsChange`) or by anchors,
  not by a per-frame update, which never runs in EditMode.
- Test 16 (a barter list that grows past the viewport) is **green on the base**: the read before the rebuild returns 1 for a list
  that is not taller than its viewport, so the spec's mechanism ("0 is read") does not occur for a 248-high list. It stays as a
  standing guard. The mechanism of Outcome 3 that does occur for a barter list is the first build (an empty content reads 0), so
  test 16b (a barter list with three long labels, delivered while hidden, then opened on a 1920 x 1000 canvas: content taller
  than the 266.7 viewport, asserted; position 1) is added and is red (-4.8e-08). This is the one test beyond the spec's 19.
- Test 14's second half (a click on the selected row keeps .4) is red on the stub (.670), not green: the base drifts the position
  on any rebuild that grows the content (332 to 368), as Outcome 5 says for test 15. The implementation must keep .4 there too.
- Test 13's third step (a tab click on the open window keeps .4) is green on the stub, as the spec said; its first two are red.
- Test 17's fit assertion reads the state-text label at text size 1.5: on the stub the long disconnect text needs 41.74 of 36.93
  (two lines), which is a second red entry; "Run over" is one line.
- Test 6 is red only on its set-up assertions (the fields are shown); `AllocationMeter.AssertNone` itself is green on the stub, as the
  spec expected for the allocation lines of 6 and 17.
- `OrderedNoticeFeed` builds standalone on a host (`Initialize(canvas.transform, onClose)`): no `PlayerFlowsEditModeTests` rig needed.
- Every quest name of the catalog is non-empty (seven of twenty defs read by the spec; not re-verified here): test 4's name is
  "Echo Quest".
- Not established: two wrapped lines at 1.5 lifting a barter list: the long labels of 16 and 16b are three lines or more; the
  assertion on the content height guards it, the number of lines is not pinned.

The stub is `FlowWindowClose.cs` only (its `Create` returns null and nothing calls it); the three views are untouched, so the
oracle needs no other stub. The existing classes that pin the code (listed under section 7) were run on the stub: 116 of 116
green, before the three `oracle-amend:` commits.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
