# P-UI-JOURNAL-DETAILS-1: the journal's details read in one order, and actions never push the reward out

- **Serves:** not in ORDER_OF_OPERATIONS. Owner direction 2026-11-22 ("Quest UI could be perfected"); Web round 2 of the
  UI rework lane (2026-11-23): for a held quest the buttons Abandon and Meditate stand between GIVER and REWARD, the reward
  is cut at the pane's bottom with no sign of more, and with nothing selected five headings stand empty.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `lane/ui-rework/base-3` (`1db1fda91`; the integration branch is `feature/unity-rework`). Worktree
  `/Users/nevinmckinnon/Documents/Projects/RH-ui-c`.
- **D-rules touched:** D1: presentation only. Legacy has no journal like this one (its J quest log is a docked scrolling
  card, `packages/client/src/ui/questLog.ts`); it follows the one legacy rule that applies: the reward is always shown
  (`questLog.ts:223`), the buttons of a quest stand in one row at the foot (`questLog.ts:208`, `questLog.css:67`). No
  Abandon or Meditate there. No `DEVIATIONS.md` row. D2 n/a. D5/D6 none. D9: no key is bound or changed; only the place
  and order of server-offered buttons change, through the same sender.

## 1. ID and outcome

The quest journal (`client-unity/UnityProject/Assets/Scripts/UI/QuestJournalView.cs`, on the `MatchUI` canvas, 1920 x 1080
units at every 16:9 window; `QuestDetail` is 744.2 x 583.8 units, bands below are fractions of it) puts the action list
`QuestActions` inside the details `Content`, between `QuestBrief` and `QuestReward` (`QuestJournalView.cs:76`): a held quest
has 93 units of buttons plus two 10 unit gaps before REWARD. `QuestTextScroll` is a fixed .20 to .81 of the panel (356 units)
with a `RectMask2D` (`:71`, `:119`): in the 1080p picture REWARD starts at y 786 and the mask ends at 790; no scrollbar marks
the cut, and `RenderSelection` never touches `_detailScroll`, so the offset survives a change of selection. With nothing
selected the five `FieldLabel` children stay; each has a fixed `LayoutElement` (height 22, `:144`) under a font of 18 x text
scale (27 at 1.5). Rust pushes Abandon FIRST (`crates/rh-sim/src/quests/board/attempts/choices.rs:33`), so the destructive
button heads the same list as the quest's own actions.

When this packet lands: the details scroll holds the text only, REWARD stands 10 units under the giver (or the brief) with
any actions; the quest's own actions stand in a strip outside the scroll, at the foot of the details, a two-row window onto
a longer list; Abandon is a button beside TRACK OBJECTIVE in the rectangle of ACCEPT, and takes two presses; with nothing
selected one line of guidance shows; the scroll returns to the top when the selection changes; a thin indicator marks a
pane that has more below; no heading row is taller than its text; a settled `Refresh` allocates nothing.

## 2. Dependencies

None beyond the base (four packets landed since the draft; none touched `QuestJournalView.cs` or
`QuestJournalViewEditModeTests.cs`). `Assets/Tests/Editor/AllocationMeter.cs` (P-UI-ALLOC-METER-1 and -2) is on the base.
Other packets of the wave share no file with this one. P-UI-QUEST-OFFER-1-fix and P-UI-FRAME-COST-1 edit
`QuestJournalView.cs` after this merge (FRAME-COST-1: `Refresh`, `SetVisible`, `SetRows`).

## 3. Relevant legacy paths

- `packages/client/src/ui/questLog.ts:223`: `view.reward.textContent = "Reward: ..."`, the reward is always shown.
- `packages/client/src/ui/questLog.ts:208`: `root.append(reward, actions)`, the actions come after the reward, at the foot.
- `packages/client/src/ui/questLog.css:67`: `.quest-log-actions { display: flex; ... }`, one row of buttons.

Server facts the design relies on (Rust, read only):
- `crates/rh-sim/src/quests/board/attempts/choices.rs:33`: `push(1, 0, None, "Abandon")` first; `:34-42`: the rest of the
  list is withheld when the quest is ready, gates are locked or the player's action is blocked, and depends on the
  player's place.
- `crates/rh-sim/src/quest_view/board.rs:41-47`: `choices` are built only for an accepted row; `:115` `owner_accepted`.
- `crates/rh-sim/src/quests/live.rs:647`: `accept_eligibility` answers `Already` for an accepted row, so `CanAccept` is
  false and `QuestJournalCopy.Hint` returns "" there: Abandon and ACCEPT never show together.
- Wire limits (the codec throws on a violation): `client-unity/RoguehallaNet/Protocol/StateItemsSocial.cs:433`,
  `client-unity/RoguehallaNet/Protocol/Messages.cs:65-70`: Abandon (1), Meditate (6), action 4: target 0; Carry (2): target 0
  or 1; action 5: target <= 80; RewardChoice (8): target <= 2; PickupReward (9): only with a `Drops` entry whose `Index` is
  its target.
- The HUD refreshes the journal on every collections delivery and session change, open or not
  (`MatchHud.cs:223-225`, `MatchHud.PlayerFlows.cs:95-98`).

## 4. New paths

- `client-unity/RoguehallaNet/Net/Ui/QuestActionStrip.cs`, `ScrollThumb.cs` (namespace `Roguehalla.Net.Ui`, a summary
  comment each) and their copies under `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/` (by
  `bash client-unity/sync-net.sh`) with `.meta`.
- `client-unity/UnityProject/Assets/Scripts/UI/ScrollIndicator.cs` (namespace `Roguehalla.Client`, `/// <summary>`) and `.meta`.
- Tests (oracle): `client-unity/RoguehallaNet.Tests/QuestActionStripTests.cs`, `ScrollThumbTests.cs`,
  `client-unity/UnityProject/Assets/Tests/Editor/QuestJournalDetailsEditModeTests.cs` and `.meta`.

The oracle author committed the two library files as stubs (every function returns 0 or false, constants as in the API
below) with their synced copies; the implementer replaces the bodies. `ScrollIndicator.cs` is NOT stubbed: no test names the
type, tests find `ScrollIndicator` by object path.

### Public API

```csharp
public static class QuestActionStrip   // count is the number of own actions shown; count <= 0 gives 0 for all three
{
    public const float RowHeight = 44f, RowGap = 5f, ScrollGap = 6f;
    public const int MaxRows = 2;
    public static float Height(int count);         // rows = min(count, 2); rows * 44 + (rows - 1) * 5: 0, 44, 93
    public static float ContentHeight(int count);  // count * 44 + (count - 1) * 5: 44, 93, 142; 60 gives 2935
    public static float ScrollBottom(int count);   // 0 for none, else Height + ScrollGap: 0, 50, 99, 99
}
public static class ScrollThumb
{
    public const float MinLength = .06f, Slack = .5f;
    // false (top = bottom = 0) unless viewport > 0 and content - viewport > Slack (a NaN viewport or content gives
    // false). Else length = max(MinLength, min(1, viewport / content)); p = clamp01(normalized), a NaN normalized
    // counts as 1 (top) and gives true; top = (1 - p) * (1 - length); bottom = top + length (fractions of the track
    // from its top). 390 / 1170: p 1 gives (0, .3333), p 0 (.6667, 1), p .5 (.3333, .6667); 390.4: false;
    // 100 / 10000: .06.
    public static bool Compute(float viewport, float content, float normalized, out float top, out float bottom);
}
public sealed class ScrollIndicator
{
    public const float Width = 4f, Inset = 3f;
    public ScrollIndicator(UnityEngine.UI.ScrollRect scroll, RectTransform frame);  // builds ScrollIndicator/Handle
    public bool Visible { get; }
    public void Refresh();  // Compute, then handle anchors x 0..1, y 1 - bottom to 1 - top
}
```

## 5. Exclusive write scope

`A/` is `client-unity/UnityProject/Assets/`.
- `client-unity/RoguehallaNet/Net/Ui/QuestActionStrip.cs`, `ScrollThumb.cs` (stubbed), their copies and `.meta` under
  `A/RoguehallaNet/Net/Ui/`
- `A/Scripts/UI/QuestJournalView.cs` (499 lines, about 650 after, at most 800; no partial), new `A/Scripts/UI/ScrollIndicator.cs` and `.meta`
- Oracle files (below) are read-only.
- Not in scope: `MatchHud.QuestRewards.cs` (`QuestPresentation.Abandon` = 1, `RewardChoice` = 8, `PickupReward` = 9 are
  declared at `:519`; `SameHandle` at `:522`; `FindRow` `:544`; `IsPending` `:551`; `TrySend` `:579`: read, never edit),
  `QuestJournalCopy.cs`, `CardDisplayCatalog.cs`, `BakedContent.cs`, the left list, the tabs, `MythicUi.cs`,
  `KeyboardScrollPane.cs`, `Tools/*` (`ui-scenarios.cjs` clicks the first name matching /accept/i: no new object name
  contains "accept"), `hud-seams.test.cjs`.

## 6. Contracts and references

- Presentation only (R-AUTH, ADR 0008): every outcome is drawn from server observations; the client decides nothing.
  Buttons send the delivered tuple through `QuestPresentation.TrySend`.
- Lane rules: motion on unscaled time and nothing that allocates in a steady frame; text through `HudUi.Label` /
  `MythicUi.Text`; theme colours from `UiTheme.Current` only; a new non-test C# file has a `/// <summary>` before its first
  type and at most 800 lines; no key bound or changed (the control mapping is the owner's, `P-QUEST25-CONTROLS`).

## 7. Behaviour to preserve

- The text of the fields, the status line, the hint, ACCEPT (and its pending state), TRACK OBJECTIVE, the left list and
  its scroll restore, the tabs, the sender (`TrySend`) and its guards (selected quest equals the action's quest and
  `IsCurrentView`).
- `"Select a quest"` stays the text of `QuestName/Value` when nothing is selected (three existing tests read it).
- `ActionButton` and `CreateScroll` are not edited (`MatchHud.QuestRewards.cs:91` and `:367` call them); `CreateActionList`
  (`QuestJournalView.cs:154`) has no other caller and is deleted.

## 8. Outcome in detail (numbers and rules)

1. **Scrolled content.** `QuestDetail/QuestTextScroll/Viewport/Content` holds only `QuestName`, `QuestObjective`,
   `QuestGiver`, `QuestBrief` (hidden when it repeats the objective, as today), `QuestReward`, `QuestParticipants`, in that
   order. REWARD stands directly under the brief or the giver, 10 units (the layout spacing) below it, with no, one or many
   actions.
2. **The strip: the quest's own actions.** One container `QuestDetail/QuestActions`, a child of `QuestDetail` outside the
   scroll, built by NEW code in `QuestJournalView.cs` (not `CreateScroll`, whose padding 4 and spacing 8 would give 3120,
   not 2935): frame `QuestActions` with a `ScrollRect` (vertical only, Clamped, sensitivity 36, inertia), child `Viewport`
   (clear `Image` with raycast, `RectMask2D`), child `Content` (anchors (0,1)-(1,1), pivot (.5,1); `VerticalLayoutGroup`
   spacing 5, padding left and right 8, top and bottom 0, child width and height controlled, no force-expand height;
   `ContentSizeFitter` vertical PreferredSize), buttons `Content/Action<i>`. No `KeyboardScrollPane` (its focus ring would
   frame the buttons). Frame anchors (.025, .20)-(.975, .20), pivot (.5, 0), `anchoredPosition` (0, 0), `sizeDelta`
   (0, height): as wide as `QuestTextScroll` (707), its bottom edge the status line's top; the buttons are 691 wide, 44
   high, one per row, 5 apart. For `n` shown actions the height is `QuestActionStrip.Height(n)`: 0, 44, then 93. Beyond two
   rows the strip scrolls and holds all `n`: it is a two-row window onto a longer list and stays that. With `n` = 0 it is
   inactive. `QuestTextScroll` has its top anchor moved from .81 to .87 (its other anchors kept) and a bottom offset
   `QuestActionStrip.ScrollBottom(n)`: 0, 50 or 99 units (height plus a 6 unit gap).
   **When it is written.** The strip's height and active state and the text pane's bottom edge are written only when the
   number of visible strip rows (0, 1 or 2) or the selected quest changes, in the branch of `RenderActions` that rebuilds
   the buttons, never because a delivery arrived. The buttons are rebuilt as today when the view object changes. The
   strip's scroll offset is not written there; it returns to the top (`StopMovement`, then
   `Content.anchoredPosition = Vector2.zero`) only when the selected quest changes. A delivery of an identical list keeps
   the offset. Accepted exception to the lane's no-hop rule, for the hand-off: while the journal is open the pane's bottom
   moves by 49 or 50 units per step and by 99 when none becomes two or more, as the count of the selected quest's own
   actions changes. The triggers are server decisions (`choices.rs:34-42`): the player reaching or leaving the quest's
   place, a stun or a cast that withdraws the actions, an action just taken, and the quest becoming ready. It is
   tolerable because the text is anchored at the top and does not move.
3. **The actions and where each stands.** The strip holds everything delivered for the selected quest except Abandon
   (`QuestPresentation.Abandon` = 1), `RewardChoice` (8) and `PickupReward` (9), in delivered order. Button `i` of
   `_actions` is the `i`th shown, so sibling order is display order. Abandon is a new button `QuestDetail/AbandonQuest`, a
   child of `QuestDetail` and never of the strip, `ButtonStyle.Secondary` as `TrackQuest` is (no new colour; the theme has
   no danger button style), in the rectangle of `AcceptQuest` (.52-.965 by .025-.12: 331 by 55 units), beside TRACK
   OBJECTIVE. It is shown exactly when the server delivered Abandon for the selected quest, labelled with the delivered
   label. Each strip button sends its own delivered tuple and Abandon the first delivered Abandon tuple (Rust delivers
   one; the wire fixes the target of Abandon and Meditate at 0), all through `QuestPresentation.TrySend`, guarded as the
   strip buttons are today (selected quest equals the action's quest and `IsCurrentView`). The client decides nothing.
   Abandon and the accept button never show together (section 3, `live.rs:647`); they share one rectangle, and the
   server's answer swaps them.
   **Abandon is armed before it sends** (lane lead's ruling): since the server's answer swaps the two buttons under the
   pointer, the second click of a double click on ACCEPT would abandon the quest just taken. Abandon takes two presses.
   When it is shown (hidden to shown, or the selected quest changed) it is unarmed and reads the delivered label. A click
   that passes the guard above and finds it unarmed arms it, sends nothing and reads "Confirm abandon"; a click while
   armed sends the delivered tuple and disarms. It disarms, sending nothing, when the selected quest changes (item 6's
   comparison), when the journal closes (`SetVisible(false)`, which `Close` calls), when the button is hidden, and when a
   new view no longer delivers Abandon for that quest. A new view that still delivers it for the held quest keeps the arm
   (the view is replaced about once a second while a timed quest runs); the click handler is rebuilt with its tuple. No
   clock, no rectangle is written, and the label is written only when the armed state (or the delivered label, unarmed)
   changes, so a settled refresh allocates nothing. For the owner, in the hand-off: abandoning a quest in the journal now
   takes two presses. The armed text is "Confirm abandon" (lane lead's ruling: "Press again to abandon" was estimated to
   wrap at text size 1.5 and not to fit the 49-unit box).
4. **Bands of `QuestDetail`** (fractions of panel height; units from the panel bottom at 1080p). `DetailHeading`
   (.025, .885)-(.975, .99): 516.7 to 578.0, 61.3 high (was .84, 87.6), size unchanged. Scroll frame: top .87 = 507.9
   (was .81), bottom .20 = 116.8; 391.1 high with no own action, 341.1 with one row, 292.1 with two or more. Strip: 116.8
   up by 44 or 93. `QuestStatus` (.115-.20), `TrackQuest` (.035-.48 by .025-.12), `AcceptHint` and `AcceptQuest`
   (.52-.965 by .025-.12): rectangles, names and parents unchanged. `AbandonQuest` is new there.
5. **Nothing selected.** `QuestName/FieldLabel` and the fields `QuestObjective`, `QuestGiver`, `QuestReward`,
   `QuestParticipants` (and `QuestBrief`, as today) are inactive. `QuestName/Value` stays active, reads "Select a quest".
   `TrackQuest` stays not interactable; the strip and `AbandonQuest` are inactive. A selection brings everything back. The
   state is cached, starts unknown so that the first render (in `Initialize`) writes it, and is written when `has` changes.
6. **Scroll reset.** When the effective handle (`has ? q.Handle : null`) differs from the one remembered at the last
   render, including a change to none, `RenderSelection`, after writing the texts, calls `_detailScroll.StopMovement()` and
   sets `_detailContent.anchoredPosition = Vector2.zero`. Handles are compared with
   `QuestPresentation.SameHandle(QuestHandle?, QuestHandle?)` (internal static, boxes nothing). A refresh or a replaced
   `QuestView` with the same handle does not reset.
7. **Position indicator.** New `UI/ScrollIndicator.cs`, built twice: on `QuestTextScroll` and on the strip frame. A track
   `ScrollIndicator` (`HudUi.Fill`, `UiTheme.With(theme.Border, .5f)`) at the frame's right edge: anchors (1,0)-(1,1),
   pivot (1, .5), width 4, `anchoredPosition.x` -3 (inside the 8 unit content padding of both panes; the strip's buttons
   end 8 in). Child `Handle` (`HudUi.Fill`, `UiTheme.With(theme.TextSecondary, .85f)`). No `Scrollbar`, no `Selectable`, no
   raycast. Built inactive; hidden while `ScrollThumb.Compute` says the content fits. The handle follows
   `ScrollRect.onValueChanged` (one cached listener added in the constructor), which `ScrollRect` raises from its
   `LateUpdate` when position, content size or viewport size changed. Nothing polls; the view's `Refresh` never calls it. A
   rect is written only when a value moved by more than .0005. The strip's mask clips the top and bottom of a focused
   button's `UiFocusHalo` (inset -3); accepted (padding 0 keeps 2935).
8. **Heading rows.** The fixed `LayoutElement` of `FieldLabel` (`QuestJournalView.cs:144`) is removed: the row takes its
   height from the text, so no heading is taller than its row at any text size (at 1.0 a row grows from 22 to about 24,
   estimate).
9. **Allocation-free refresh.** A settled `Refresh()` with a held quest selected, Abandon unarmed or armed, allocates
   nothing. Known sources, all fixable inside `QuestJournalView.cs`: `_state.text` is concatenated on every call
   (`:235`; write it when `_visibleRowCount` changes); `Find` calls `QuestPresentation.FindRow` (`:270`) and `RenderStatus`
   calls `IsPending` (`:341`), whose `foreach` over a `ReadOnlyCollection` allocates an enumerator (cache per pair `_view`,
   handle, or loop by index in this file). Possible, not measured: `GetComponentInChildren` on `_track` per call (`:315`;
   cache the label in `Initialize`, as `_acceptLabel` is; cache the Abandon label the same way), a TMP text set to an equal
   string (compare first, as `RenderStatus` does). Any other source the meter reports: fix it here if it is in this file;
   outside it: stop and report.

## 9. Existing tests that pin this code

Searched `Tests/Editor` for `QuestJournalView`, `QuestTextScroll`, `QuestDetail`, `FieldLabel`, `Select a quest`, `Abandon`,
`QuestActions`, `Action = 1`.
- `QuestJournalViewEditModeTests` (`Assets/Tests/Editor/QuestJournalViewEditModeTests.cs`):
  `JournalUsesExactProjectedChoicesAndRevokesThemBeforeRefresh` (`:74`) finds the action list at
  `QuestDetail/QuestTextScroll/Viewport/Content/QuestActions` (`:89`): **turns red by design**, amended in an `oracle-amend:`
  commit (below). Its fixture delivers two Carry actions, no Abandon, so nothing else of it changes. The class also reads
  the field texts at `QuestDetail/QuestTextScroll/Viewport/Content/<name>/Value` (`Text`, `:409`), reads
  "Select a quest" (`:192`, `:221`, `:294`), `QuestDetail/AcceptQuest`, `TrackQuest`, `QuestStatus`, `QuestRows`, and the
  `_detailScroll` / `_detailContent` / `_scroll` / `_content` fields by reflection: all stay green.
- `QuestJournalPolishEditModeTests`: the accept button, pending state, status line, the field order name < objective <
  giver < brief (`:130-140`), the hint: green, unaffected.
- `PlayerFlowsEditModeTests`: `:467` (status contains "Accepting") and `:503` (`QuestName/Value`) read the journal; `:141`
  reads the HUD's NearbyQuestActions window, not the journal: green.
- `QuestActionPromptEditModeTests` hits for `Action`: the HUD's nearby window, not the journal: green.

## 10. Oracle (what the tests pin)

dotnet (`client-unity/RoguehallaNet.Tests`):
1. `QuestActionStripTests.HeightsFollowTheCountAndStopAtTwoRows`: `Height` 0, 44, 93, 93, 93 for 1, 2, 3, 60 and 0 for 0 and
   -1; `ContentHeight` 44, 93, 142, 2935 for 1, 2, 3, 60 and 0 for 0 and -1; `ScrollBottom` 50, 99, 99, 99 for 1, 2, 3, 60
   and 0 for 0 and -1.
2. `ScrollThumbTests`: `TheThumbIsShownOnlyWhileTheContentIsTaller`, `TheLengthIsViewportOverContentAndThePlaceFollowsThePosition`.

EditMode `QuestJournalDetailsEditModeTests` (a NEW class; see the file's header for the rig):
`RewardStandsUnderTheGiverWhateverTheActions`, `NoActionIsInTheScrollOrOverlapsTheRest`,
`AbandonStandsInTheFooterAndTheDeliveredTuplesAreSent`, `TheStripHeightFollowsTheOwnActionsAndTheScrollFollowsTheStrip`,
`NothingSelectedShowsOneLineOfGuidance`, `NoHeadingIsTallerThanItsRowAndNothingOverlaps` (1, 1.5),
`TheHeadingsAreRightWhenTheJournalOpensAfterDeliveriesWhileHidden`, `TheScrollReturnsToTheTopWhenTheSelectionChanges`,
`TheIndicatorShowsOnlyWhileTheContentIsTaller`, `AnUnchangedRefreshWritesNoRectAndAllocatesNothing`,
`ADeliveryThatChangesNothingShownWritesNoRectAndKeepsTheStripScroll`, `ADoubleClickOnAcceptNeverAbandons`,
`AbandonIsArmedUntilSomethingChanges`, `TheArmedTextFitsTheButton` (1, 1.5).

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/QuestActionStripTests.cs`
- `client-unity/RoguehallaNet.Tests/ScrollThumbTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/QuestJournalDetailsEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/QuestJournalDetailsEditModeTests.cs.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/QuestJournalViewEditModeTests.cs` (one amended line, its own `oracle-amend:` commit, below)
- `docs/backlog/packets/P-UI-JOURNAL-DETAILS-1.md`

### Amended expectation, for the owner (a re-statement, not a loosening)

`QuestJournalViewEditModeTests.JournalUsesExactProjectedChoicesAndRevokesThemBeforeRefresh`, line 89: the list was found at
`QuestDetail/QuestTextScroll/Viewport/Content/QuestActions`; it is now found at `QuestDetail/QuestActions/Viewport/Content`
(the strip, outside the scroll). Everything else of the test (labels, the stale-callback revocation, the one sent
command) is unchanged. It is red on the stub by design and green once the strip exists.

### Red tests on the stub (what each fails on)

dotnet: `QuestActionStripTests.HeightsFollowTheCountAndStopAtTwoRows` (Height 1 is 0, not 44);
`ScrollThumbTests.TheThumbIsShownOnlyWhileTheContentIsTaller` and `TheLengthIsViewport...` (`Compute` returns false).

EditMode `QuestJournalDetailsEditModeTests` (16 cases, all red on the stub, each on an assertion, none on an exception):
- `RewardStandsUnderTheGiverWhateverTheActions`: giver to reward is 20, 64, 113 (not 10).
- `NoActionIsInTheScrollOrOverlapsTheRest`: `Action0` is inside the details scroll.
- `AbandonStandsInTheFooterAndTheDeliveredTuplesAreSent`: no object `QuestActions/Viewport/Content`.
- `TheStripHeightFollowsTheOwnActionsAndTheScrollFollowsTheStrip`: no object `QuestDetail/QuestActions`.
- `NothingSelectedShowsOneLineOfGuidance`: the `QuestName` heading is active before a selection.
- `NoHeadingIsTallerThanItsRowAndNothingOverlaps(1)`: red on the base too (not a standing guard): a heading needs 24.56,
  its row is 22. `(1.5)`: needs 36.83 of 22.
- `TheHeadingsAreRightWhenTheJournalOpensAfterDeliveriesWhileHidden`: needs 36.83 of 22; giver to reward 113.
- `TheScrollReturnsToTheTopWhenTheSelectionChanges`: position stays .2 after `SelectQuest` of another quest.
- `TheIndicatorShowsOnlyWhileTheContentIsTaller`: no object `QuestTextScroll/ScrollIndicator`.
- `AnUnchangedRefreshWritesNoRectAndAllocatesNothing`: no object `QuestDetail/QuestActions`. On the base the meter reports
  1200 GC.Alloc samples in its quietest measurement of 200 calls (6 per call) for the settled refresh.
- `ADeliveryThatChangesNothingShownWritesNoRectAndKeepsTheStripScroll`: no object `QuestDetail/QuestActions`.
- `ADoubleClickOnAcceptNeverAbandons`, `AbandonIsArmedUntilSomethingChanges`, `TheArmedTextFitsTheButton(1, 1.5)`: no
  object `QuestDetail/AbandonQuest`.

Existing classes run on the stub and green before the amend: `QuestJournalViewEditModeTests` and
`QuestJournalPolishEditModeTests` (17 tests together with the 16 red ones: 33 run, 17 passed). After the amend the one
amended test is red until the strip exists.

## 11. Known traps

- `RenderActions` returns at once while (`_actionView`, `_actionRow`) equal (`_view`, row) (`:393`), and both start null:
  the first `RenderSelection` in `Initialize` does nothing there, so build the strip inactive, with scroll offset 0, height
  0 and the cached rows 0 and quest none.
- `_view` is replaced on every delivery, about once a second while a timed quest runs: key the detail scroll reset, the
  strip's layout and reset and the Abandon arm on the handle and the row count, never on the view. Do not reset through
  `verticalNormalizedPosition` (it forces a canvas rebuild). `Refresh` runs every frame while open: write the strip, the
  empty state and the rects on changes only. The left list's own scroll restore and row rewrite on every delivery (the
  `collectionChanged` branch) is older than this packet and out of scope; the no-write sentinels cover the details pane only.
- **The view refreshes while hidden** (`MatchHud.cs:223-225`): those runs come before any label of the journal has been
  active in the hierarchy, and such a label measures a tenth of its size (`TextMeshProUGUI` created under an inactive parent
  has had no `Awake`). Product code never keeps a size it measured there.
- NUnit here has no `Assert.Multiple`. Tests are a separate assembly with no `InternalsVisibleTo`.
- Capture tools copy layout maths (`HudLayout.MinimapSize`, `tabMapRect`): not touched here.
- The numbers are an estimate until measured on a real render: the pictured quest at the monk has one own action,
  Meditate, in the strip and Abandon in the footer: its pane is 341.1 units and REWARD ends at about 311 below the scroll
  top, so it fits with about 30 to spare. PARTICIPANTS can still lie below the pane; the indicator says so. With two or
  more own actions the pane is 292.1: REWARD is cut by about 19 and the indicator says so. No test asserts visibility.

## Notes from the oracle author

- **Measured on the base** (Unity probe, not committed): a button made by `ActionButton` is 44.0 high and 690.98 wide
  (the spec's 44 and 691 hold, so 93 and 2935 are right). The armed text "Confirm abandon" in a button of the
  `AcceptQuest` rectangle (label box 319.2 x 49.5, `MythicUi.Button` font 16, scale applies 24 at 1.0 and 36 at 1.5):
  one line at both sizes. At 1.0: needs 185 x 32.7 at the largest size (limit 281 wide, 49.5 high), 157 x 27.8 at the
  smallest. At 1.5: 278 x 49.1 at the largest (just inside 281 and 49.5), 236 x 41.7 at the smallest. The old wording
  "Press again to abandon" at 1.5 needs 98 high (it wraps), which confirms the lane lead's ruling. Test 13 asserts the
  height at the smallest size the auto-size may reach (a wrap at the smallest size would still fail it: two lines are
  55.6 at 1.0).
- **The setter does not raise `onValueChanged`**: measured on a plain `ScrollRect` (`verticalNormalizedPosition = .3f`
  raised 0 events), so test 8 keeps the line "position set and `Refresh()` called without the event: the handle stays".
- **Heading height**: the spec said 27 against 22 at 1.5; the label's preferred height is 36.83 (27 is the font size) and
  24.56 at 1.0, so the 1.0 case is red on the base and is not a standing guard. The tolerance is .5 as in the spec.
- **"not `Content` among its ancestors"** (spec test 2): the strip's own list is also named `Content`
  (`QuestActions/Viewport/Content`), so the test reads it as "not under `QuestTextScroll`" (the details pane), the
  intended meaning.
- **Guards**: in `NoActionIsInTheScrollOrOverlapsTheRest` the `AbandonQuest` versus `QuestStatus` check is a standing
  guard (gap .08 units) and is named so in the test; `QuestStatus`, `TrackQuest`, `AcceptQuest`, `AcceptHint` anchors in
  test 4 and "consecutive fields do not overlap" in test 6 are green on the base by design, inside red tests. The rig
  asserts `QuestDetail` is 583.8 +/- 1 high (the units of this packet).
- **Test 10 (c)** carries one assertion not in the spec: after setting the strip to .4 again its content offset is above
  100, so the reset in `SelectQuest` of the second quest is not satisfied by an offset that was already 0.
- **Scratch run done** (one per new EditMode class, not committed): a copy of the class with every assertion wrapped in a
  catch-and-collect helper, a missing object answered by a dummy, and the base's real journal. All 17 cases ran to the
  end. Every entry was an assertion about the missing behaviour (a missing object, a wrong rect, a wrong text, 1200
  allocation samples); the only non-assertion exceptions were null references reading a command that was never sent
  (`LastCommand()` after a missing Abandon), which I fixed by returning from `AssertLast` after its own not-null check.
  Lines that act on `AbandonQuest`, the strip and `ScrollIndicator` can only be exercised against dummies on the stub,
  so their expected values (the arm sequence of 11 and 12, the strip offsets of 10, the handle anchors of 8) were
  checked against this packet's rules by reading, not by a passing run. After that run test 10 (c) got its rig
  assertion and `AssertLast` got its quest argument; the class was compiled and run once more (16 red on assertions).
  The dotnet pair was also run against a throwaway correct implementation (3 green) and the stub restored.
- `ScrollIndicator.cs` is not stubbed; no test names the type.

## Notes from previous attempts
