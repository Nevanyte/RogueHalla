# P-UI-JOURNAL-POLISH-1: the journal says what matters first, in the game's words, and Accept cannot be sent twice

- **Serves:** not in the plan: owner direction 2026-11-22, chat of the UI rework lane ("Quest UI could be perfected").
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (the tree with P-UI-QUEST-TRACKER-1, P-UI-TOAST-LANES-1,
  P-UI-ABILITY-ROW-1, P-UI-TABMAP-SHEET-1-fix and P-UI-ALLOC-METER-1 in it). Integration branch: `feature/unity-rework`.
- **D-rules touched:** D1: presentation; copy and emphasis only, every fact shown is a delivered one · D2 n/a · D5/D6 none ·
  D9 untouched: the distance in the hint is drawn from delivered positions and decides nothing (the server's `CanAccept`
  stays the only gate).
- Evidence: `.agent-runs/lane-ui-rework/understand/quest-ui.txt` (defects 4, 5, 8, 10, 14, 15) in the lane folder.
- Worktree: `/Users/nevinmckinnon/Documents/Projects/RH-ui-x`.

## 1. ID and outcome

The journal (`client-unity/UnityProject/Assets/Scripts/UI/QuestJournalView.cs`) says what matters first and in the
game's words, and ACCEPT cannot be sent twice. Today:

- The live facts of the selected quest (progress, time left, readiness) are one muted 13-unit line at the bottom of
  the detail panel (`_status`, created at `QuestJournalView.cs:74`, written in `RenderSelection`, :302-311). Its wording
  describes the architecture: "Acceptance queued · waiting for server update", "Server deadline in 134s", "Awaiting
  server deadline update" (`DeadlineCopy`, :408-416; also shown for a quest that has expired).
- For board quests the server sends the same sentence as `Brief` and `Objective`: it is printed twice, the brief
  bright and the objective (the thing to do) dim (`CreateQuestField` calls at :69-70).
- ACCEPT stays enabled after it is pressed: a double click sends `QuestAccept` twice and the server answers the second
  with a refusal notice (`TryAcceptFrom`, :420-429, never looks at `_acceptPendingFor`).
- An open quest that cannot be accepted from where the player stands shows no button and no reason (:315).
- After a click nothing marks which row the detail panel belongs to (`GetRow`, :236-253, only stripes rows).
- The list row of an open quest that is not in reach reads "OPEN", the enum's name (`QuestRowLabel`, :230-234).

When this lands: the status line leads with the live facts in the game's words, in a larger size and bright when the
player holds the quest; the objective comes first in the detail and a brief that repeats it is hidden; ACCEPT turns
into a disabled "ACCEPTING…" after one press and cannot send twice; an offer out of reach says where to go; the
selected row stays marked; list rows read "OFFER · HERE", "OFFER", "ACCEPTED", "TURN IN", "COMPLETE", ….

## 2. Dependencies

On the base already: `QuestTracker` (`client-unity/RoguehallaNet/Net/Ui/QuestTracker.cs`: `Clock` :62, `DwellSeconds`
:80), `AllocationMeter` (`Assets/Tests/Editor/AllocationMeter.cs`, no test of this packet uses it). Nothing else.

## 3. Relevant legacy paths

None: the journal is a Unity-only presentation of server facts. Server fact used for the hint's wording only:
`QUEST_ACCEPT_RANGE` = 64 world units, `crates/rh-sim/src/constants.rs:392`, used by the accept path at
`crates/rh-sim/src/quests/live.rs:634` (which also refuses a giver behind a wall: that case reads "Cannot be accepted
right now"). The client never decides acceptance.

## 4. New paths

- `client-unity/RoguehallaNet/Net/Ui/QuestJournalCopy.cs` and its synced copy
  `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/QuestJournalCopy.cs` (+ `.meta`).
- `client-unity/RoguehallaNet.Tests/QuestJournalCopyTests.cs` (oracle).
- `client-unity/UnityProject/Assets/Tests/Editor/QuestJournalPolishEditModeTests.cs` (+ `.meta`) (oracle).

## 5. Exclusive write scope

- `client-unity/RoguehallaNet/Net/Ui/QuestJournalCopy.cs` (the stub exists; implement it), its synced copy and `.meta`
  (run `bash client-unity/sync-net.sh` through the wrapper)
- `client-unity/UnityProject/Assets/Scripts/UI/QuestJournalView.cs`
- Not in scope: `UI/TraderDialogView.cs` (P-UI-TRADER-COPY-1 owns it), `MatchHud.PlayerFlows.cs`,
  `MatchHud.QuestRewards.cs`, `QuestTracker.cs`, `QuestTrackerView.cs`, the help guide, `CardDisplayCatalog.cs`,
  `BakedContent.cs`.
- `PlayerFlowsEditModeTests` is also amended by other packets of the lane; the coordinator merges those one at a time.

## 6. Contracts and references

- Presentation only (C-OBS, R-AUTH): no new rule; every number shown is a delivered one.
- The journal is built with `MythicUi` on canvas fractions, not `HudUi`: keep its helpers and palette
  (`ShowcasePalette`); do not move it to the HUD stage.
- Library file: `/// <summary>` before the first type, at most 800 lines; Unity copy only through `sync-net.sh`.
- `client-unity/UnityProject/Tools/hud-seams.test.cjs` pins certain `MatchHud` members; this packet touches none.

### The numbers and API (the spec of the implementation)

New pure file `client-unity/RoguehallaNet/Net/Ui/QuestJournalCopy.cs` (namespace `Roguehalla.Net.Ui`):

```csharp
/// <summary>The journal's words for a quest: the state on its list row, the status line, the reason it cannot be
/// accepted. Every input is a delivered fact.</summary>
public static class QuestJournalCopy
{
    /// <summary>The server's accept range in world units: used only to choose the hint's wording.</summary>
    public const double GiverReach = 64d;
    public static string RowState(QuestRowView q);
    public static string Status(QuestRowView q, bool acceptPending, bool rewardPending, int secondsLeft);
    public static string Hint(QuestRowView q, double distanceToGiver);
    public static bool ShowsBrief(QuestRowView q);
}
```

- `RowState`, first match wins: `OwnerCompleted` "COMPLETE"; `State == Expired` "EXPIRED"; `State == Done` "DONE";
  `OwnerAccepted && OwnerReady` "TURN IN"; `OwnerAccepted` "ACCEPTED"; `State == Preview` "PREVIEW"; `CanAccept`
  "OFFER · HERE"; otherwise "OFFER". An accepted quest that has since expired or been finished by someone else keeps
  `OwnerAccepted`, so the state comes before it: the row never says "TURN IN" for a dead quest.
- `Status`, first match wins (`time` below):

  | Case | Text |
  |---|---|
  | `acceptPending` | "Accepting…" |
  | `rewardPending` | "Choose your reward" |
  | `OwnerCompleted` | "Completed" |
  | `State == Expired` | "Expired" |
  | `State == Done` | "Finished" |
  | `OwnerAccepted && OwnerReady` | "Ready to turn in" + time |
  | `OwnerAccepted` | "Accepted · " + progress + time |
  | `State == Preview` | "Preview" + time |
  | `CanAccept` | "Offer" + time |
  | otherwise | "Offer · not in reach" + time |

  `progress` is `OwnerProgress + " / " + n + " s"` when `QuestTracker.DwellSeconds(q.Def)` is `n > 0`, else
  `OwnerProgress + " progress"` (today's words). `time` is "" when `secondsLeft < 0` (no deadline), " · time is up"
  when it is 0, else " · " + `QuestTracker.Clock(secondsLeft)` + " left" ("2:14 left").
- `Hint`: only for `State == Open && !CanAccept && !OwnerAccepted && !OwnerCompleted`, else "". With a distance above
  `GiverReach`, or one that is NaN or infinite: "Go to " + giver + " to accept", where giver is `GiverLabel` or "the
  quest giver" when that is null or blank. Within reach (64 included): "Cannot be accepted right now".
- `ShowsBrief`: false when `Brief` is null or blank, or equals `Objective` after trimming both (ordinal).

### The view (`QuestJournalView.cs`)

- **Status line** (`QuestStatus`): text is `QuestJournalCopy.Status(...)` with
  `secondsLeft = ExpiresAt > 0 ? max(0, ceil((ExpiresAt - Session.ServerTimeMs) / 1000)) : -1` (NaN or infinite counts
  as 0, above 1000000000 as 1000000000; -1 while `Session.Latest` is null). Its `MythicUi.Text` size argument is 15
  (was 13): its `MythicTextScale.BaseSize` is then 22.5 (was 19.5), because `MythicUi.Text` multiplies by 1.5
  (`MythicUi.cs:95`). Colour `ShowcasePalette.Text` when the player holds the quest (`OwnerAccepted`, a pending accept
  or a pending reward), `ShowcasePalette.MutedText` otherwise. Rect unchanged. Empty without a selection, as today.
  `DeadlineCopy` and `QuestProgressText` go.
- **Detail fields**, in this order: `QuestName`, `QuestObjective`, `QuestGiver`, `QuestBrief`, `QuestActions`,
  `QuestReward`, `QuestParticipants`. `QuestObjective` uses `ShowcasePalette.Text`, `QuestBrief`
  `ShowcasePalette.MutedText`. The `QuestBrief` field object is inactive when `ShowsBrief` is false. Its text is still
  written verbatim while the field is hidden; only `SetActive` changes (the existing max-length test reads
  `QuestBrief/Value` with `Brief == Objective` and must stay green).
- **Accept**: while `_acceptPendingFor` is the selected quest, the button stays visible, is not interactable and reads
  "ACCEPTING…"; otherwise it reads "ACCEPT QUEST". `TryAcceptFrom` returns false and sends nothing while
  `_acceptPendingFor` equals the handle. When the pending state ends is unchanged (a new quest view, a new session or
  timeline, the journal's reset paths; `Refresh` :173-228).
- **Hint**: a new label `AcceptHint` in the detail panel on the Accept button's rect ((.52, .025) to (.965, .12)), 13
  units, `ShowcasePalette.MutedText`, centred, wrapping. It shows `QuestJournalCopy.Hint(q, distance)` while the Accept
  button is hidden and the hint is not empty; `distance` is from `Session.Me` (x, y) to (`GiverX`, `GiverY`), NaN when
  the session has no `Me`.
- **Selected row**: `RenderSelection` calls `MythicUi.Select(row.Button, row.Handle equals _selected)` for every
  visible row (`TraderDialogView.cs:403` does the same for its merchants; `MythicUi.Select`, `MythicUi.cs:152`, sets
  `colors.normalColor` to `UiTheme.Current.SurfaceSelected` or `SurfaceRaised`). The alternating stripe of unselected
  rows (`GetRow` :241-242, :246) is lost on purpose: every unselected row is `SurfaceRaised`.
- **List row**: the second line is `QuestJournalCopy.RowState(q) + "  ·  " + GiverLabel` (separator and first line
  unchanged; `QuestRowLabel` :230-234).

Nothing else changes: the journal's size and place, its header strings ("Waiting for quest data…", "No quests are
currently available.", "N quests"), the track button, the action buttons, the participants text.

## 7. Behaviour to preserve

- `_acceptPendingFor = null` when a new `QuestView` object arrives (`Refresh` :209), on rebind (:169), on timeline
  change (:186), when the session leaves InMatch (:179) and when the quest list is unavailable (:192, :199). Nothing
  else ends it: a server that never answers leaves "ACCEPTING…" until the next view, session or timeline (RISKS).
- `_accept.gameObject.SetActive(has && q.CanAccept)` stays the rule for showing the button (:315); while pending the
  row is still `CanAccept` until the server answers.
- `QuestTracker.Clock(0)` is "0:00": the status uses " · time is up" for 0 and never prints "0:00 left".
- `Refresh()` runs every frame and calls `RenderSelection()`: write a text only when it changed, build the status
  string only when its inputs changed (selection, the quest object, pending flags, the whole second) and the hint text
  only when the far or near flag, the quest object or the giver label changed, so the steady frame of an open journal
  allocates nothing new compared with today (it already builds the `_state` text each frame). Do not add work to the
  frame of a hidden journal. No test of this packet measures allocation.
- Stale callbacks stay dead (`IsCurrentView`, :273-278), the `Session.LastResult` block of `TryAcceptFrom`, the
  session binding of the Accept listener (:322-330).

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-JOURNAL-POLISH-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- `QuestJournalCopyTests` (dotnet) and `QuestJournalPolishEditModeTests` green; `QuestJournalViewEditModeTests`,
  `PlayerFlowsEditModeTests`, `QuestTrackerEditModeTests`, `HudTextScaleEditModeTests` still green with the three
  amended expectations below.
- `bash client-unity/sync-net.sh --check` says the Unity copy is up to date.

## Existing tests that pin this code

Verified by reading each. Nothing outside these reads a list row's "OPEN"/"OFFER", the order of the detail fields,
the status size, "Acceptance queued", "Awaiting server deadline" or "Server deadline" (searched `Assets/Tests`,
`Tools`, `RoguehallaNet.Tests`).

`QuestJournalViewEditModeTests` (`Assets/Tests/Editor/QuestJournalViewEditModeTests.cs`); its `Row` helper (:393) builds
`State = accepted ? Open : Preview`, `ExpiresAt = 12345.5`, `OwnerReady = accepted`, `OwnerProgress = accepted ? 2 : 0`,
`Def = "test"`, giver label "Wanderer & Co.":

- `UntimedAcceptedQuestShowsProgressWithoutWaitingForADeadline` (:61): accepted row, `ExpiresAt = 0`; status contains
  "Accepted · 2 progress" and not "deadline". **Changes** (amend 1): the helper row is `OwnerReady`, which now reads
  "Ready to turn in".
- `AcceptUsesCurrentHandleAndFreshCanAcceptThenSessionBatchesOnlyThatIntent` (:298): status "Awaiting server deadline
  update" (:305), `TryAccept` true (:306), status "Acceptance queued · waiting for server update" (:307), a replacement
  clears it (:314, `DoesNotContain "Acceptance queued"`), one `QuestAccept` frame, result blocks acceptance. **Changes**
  (amend 2).
- `ReplacementPreservesSelectedHandleAndScrollButClearsRemovedSelectionAndTracking` (:173): status contains "Ready"
  (:180) for an accepted ready row; "Ready to turn in" still contains it. Stays green unchanged.
- `MaximumLengthServerStringsRemainVerbatimInScrollableFixedSizeTextFields` (:149): reads `QuestBrief/Value` with
  `Brief == Objective`, expects the text verbatim (:162-163), scrollable content. Stays green: the brief text is still
  written while its field is hidden.
- `UnseenUnavailableAndAvailableEmptyAreDistinctAndRowsPreserveExactServerText` (:113): detail texts exact, button label
  "ACCEPT QUEST" (:141), tracking. Unchanged.
- `OldAcceptTrackAndRowCallbacksCannotActAfterReplacementBeforeRefresh` (:196),
  `OldSessionCallbacksAndRemovedRowsCannotAcceptAfterRebindWelcomeOrDisconnect` (:341),
  `JournalUsesExactProjectedChoicesAndRevokesThemBeforeRefresh` (:74),
  `MetadataReplacementPreservesScrollPositionWhenRowsOverflowViewport` (:225, row 0 label first line),
  `SelectedParticipantsUseTypedRosterIdsKeepDuplicateLabelsAndClearOnReplacementOrRebind` (:247): stale callbacks,
  actions and participants; unchanged.

`PlayerFlowsEditModeTests` (`Assets/Tests/Editor/PlayerFlowsEditModeTests.cs`):

- `PlanningPreviewRetiresDeferredAcceptAndWaitsForFreshNativePermission` (:438): after `TryAccept` the status contains
  "Acceptance queued" (:458). **Changes** (amend 3): it contains "Accepting". Other journal lines of that file
  (:366, :442-449, :475-498) read `TryAccept`, row 0 activity and `QuestName` only: unchanged.

`QuestTrackerEditModeTests`, `TabMapSheetEditModeTests`, `MatchLaunchEditModeTests`,
`TrainingConsoleIntegrationEditModeTests` only open or close the journal or read `IsVisible`: unchanged.

## Amended expectations (for the owner)

Each in its own `oracle-amend:` commit, one test method each. The coordinator reads these three:

1. `QuestJournalViewEditModeTests.UntimedAcceptedQuestShowsProgressWithoutWaitingForADeadline` (:61): sets
   `row.OwnerReady = false` on its row; keeps both assertions.
2. `QuestJournalViewEditModeTests.AcceptUsesCurrentHandleAndFreshCanAcceptThenSessionBatchesOnlyThatIntent` (:298):
   status before the accept (:305) is "Preview · time is up" (the helper row is `Preview` with `ExpiresAt` 500, the
   snapshot of tick 20 is 1000 ms); after the accept (:307) it contains "Accepting…"; the `DoesNotContain` after the
   replacement (:314) becomes "Accepting".
3. `PlayerFlowsEditModeTests.PlanningPreviewRetiresDeferredAcceptAndWaitsForFreshNativePermission` (:458): contains
   "Accepting" instead of "Acceptance queued".

## Oracle files (read-only for implementers)

ORACLE_FILES_PLACEHOLDER

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/in.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-x dotnet test client-unity/RoguehallaNet.Tests --filter QuestJournalCopyTests`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-x QuestJournalPolishEditModeTests,QuestJournalViewEditModeTests,PlayerFlowsEditModeTests`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> bash client-unity/sync-net.sh`
- the quick gate of section 8.

## 10. Risk and suggested model tier

implementer-sonnet. Small, local; the risks are the per-frame allocation rule, the pending/hide interplay of the Accept
button and its listener, and keeping the three amended tests honest.

RISKS: a server that never answers an accept leaves "ACCEPTING…" (disabled) until the next quest view, session or
timeline; today a second click would send a second command that the server refuses with a notice. The stripe of the list
rows is gone on purpose.

## 11. Known traps

- `Refresh()` is per frame: see section 7.
- The pending state is cleared when a new `QuestView` arrives; `QuestJournalViewEditModeTests` pins it ("a new reliable
  replacement clears only local pending feedback").
- `MythicUi.Text` multiplies the size argument by 1.5 for `MythicTextScale.BaseSize` (`MythicUi.cs:95`).
- Tests are a separate assembly with no `InternalsVisibleTo`: nothing a test reads may be `internal`; tests read the
  UI by object path (`QuestJournal/QuestDetail/...`).
- `QuestPresentation.ShowsOffer` (`MatchHud.QuestRewards.cs:386`) hides an `OwnerCompleted` board row (`IsBoardRow`
  :384: underscore in the def, `letters`, `watchtowers`, `gatecrasher`); the tests use `Def = "test"` so completed rows
  stay listed.
- Do not edit any test in the oracle list; if one cannot be satisfied, report `blocked` with `oracleDefect`.

## Notes from the oracle author

NOTES_PLACEHOLDER

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
