# P-UI-QUEST-ACTION-PROMPT-1: the prompt above the action row names the quest action that is in reach

- **Serves:** not in the plan: owner direction 2026-11-22 in the UI rework lane's chat ("Quest UI could be perfected", "make sure all
  in-game notifications are thoughtfully placed"); seen in Web round 2 of the lane (2026-11-23): right after a quest is accepted the
  prompt above the action row reads "Nearby quest actions · 1", which names neither the action (Meditate) nor the quest.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` at `3a9513d02` (on top of `feature/unity-rework`); worktree
  `/Users/nevinmckinnon/Documents/Projects/RH-ui-y`
- **D-rules touched:** D1 legacy has no per-quest action at a place and no such caption (`packages/` has no "Meditate", no Abandon, no
  list of actions; its F hint names the object of F: `packages/client/src/ui/weaponBar.ts:183-197`, "take <weapon>", "take <quest item>",
  talk hint). This packet adds no key and changes no input, so it proposes no `DEVIATIONS.md` row · D2 n/a · D5/D6 none · D9 untouched:
  the text is built from the delivered label and row name; the client decides nothing.

Write scope: see section 5. Evidence: `.agent-runs/lane-ui-rework/understand/web2-quest-action-prompt.txt`,
`docs/backlog/packets/P-UI-QUEST-OFFER-1.md`, `P-UI-ABILITY-ROW-1-fix.md`. `A/` below is `client-unity/UnityProject/Assets/`.

## 1. ID and outcome

All code is in `A/Scripts/UI/MatchHud.QuestRewards.cs`. `InitializeQuestRewardHud` (:59) builds the launcher
`QuestRewards/QuestActionLauncher` with one caption label `_questActionCaption` (size 11, centred; 300 units wide alone, 220 beside the
pickup pill: `HudRegions.PromptWidth`). `RefreshQuestRewardHud` (:98) writes it in one block (:163-166): a pending reward choice reads
"Choose quest reward"; else nearby actions read `NearbyActionsCaption()` (:174); else the offer reads "F · Talk to <giver>" (only when
`_questChoice` is empty and `_nearbyQuestCount` is 0, :163); else a pending turn-in reads "Return to turn in your reward".
`BindQuestRewardActions` (:338) runs once per new `QuestView` object. It skips Abandon (:374) and every reward-choice action (8), counts the
rest in `nearby` and keeps only that number in `_nearbyQuestCount` (:394). In the same loop it writes each counted action into a button of
the nearby-actions window as `quest.Name + " · " + action.Label`, or the label alone for action 9 or a missing row (:377-378).
`NearbyActionsCaption()` builds "Nearby quest actions · " + count from the count only, cached in `_nearbyCaptionCount` / `_nearbyCaption`
(:170-182). So after the accept of Stay A While the delivered label "Meditate" (`WireQuestAction.Label`) and the row's name
(`WireQuestRow.Name`) exist, and the prompt shows neither. The window and the caption would disagree on the order of words (the window:
quest, then action). `ResetQuestRewardHud` (:500) clears neither `_nearbyQuestCount`, `_nearbyCaptionCount` nor the caption label; after a
reset with a null view `BindQuestRewardActions` does not run again (`_questRewardView` is already null, :514), so the count of an earlier
match stays.

When this lands (units are HUD stage units; nothing is added to the wire, the library or the theme):

1. **The caption names what is there.** With exactly one nearby action (as `BindQuestRewardActions` counts: not Abandon, not an action 8)
   the launcher caption reads `ActionText` of that action: its delivered label, " · " (U+00B7 with one space each side), the quest's
   name: "Meditate · Stay A While".
2. **Action 9, or a quest without a name: the label alone** ("Pick up WallBuster"; the drop's handle is no row). Same rule as the window's
   text today. A counted action whose label is empty leaves the caption at "Nearby quest actions · 1": the launcher never shows an empty
   bar. On the wire every action other than 9 has a row (`QuestStateCodec.CheckQuestView`, `client-unity/RoguehallaNet/Protocol/
   StateItemsSocial.cs:384-418`, the last `foreach`), so the null-row arm of `ActionText` is defensive only; a test that needs "no name"
   uses a row whose Name is "".
3. **Two or more nearby actions: the text stays "Nearby quest actions · n"**, byte for byte.
4. **One order of words.** One new member `QuestPresentation.ActionText(QuestView view, QuestActionView action)` (`internal static`, in
   the same file, class at :520) returns `label` alone when `action.Action == PickupReward`, the row is null, or the row's name is empty;
   `label + " · " + row.Name` otherwise; a null label counts as "". The window's button text and the caption both call it. The window now
   reads "Meditate · Stay A While" (label first). The caption takes that text only when `string.IsNullOrEmpty(action.Label)` is false
   (item 2).
5. **Nothing is formatted in a steady frame.** `BindQuestRewardActions` computes the caption once per new view and stores it in the existing
   field `_nearbyCaption`: count 0 gives "", count 1 gives the text of the first counted action (the same string object the window's
   button got; with an empty label, "Nearby quest actions · 1" built at bind time), count 2 or more gives "Nearby quest actions · " + n.
   `NearbyActionsCaption()` keeps its name and signature and returns `_nearbyCaption`. `_nearbyCaptionCount` is removed. A refresh of an
   unchanged view allocates nothing. A new view object with the same actions builds an equal string and `Set` writes only on a difference,
   so the caption changes only when the delivered actions change.
6. **A reset leaves no caption behind.** `ResetQuestRewardHud` also sets `_nearbyQuestCount = 0`, `_nearbyCaption = ""` and writes "" to
   `_questActionCaption` (through `Set`), after its existing `_questChoice = ...` line (:515).
7. **Fit.** The longest caption the server can produce with exactly one counted action is "Collect hidden token · Whispers in the Brush"
   (44 characters; label of `quests/board/attempts/choices.rs` for `WhispersInTheBrush`, name of `quests/board/catalog.rs`). Others:
   "Deliver the coat · Even Skeletons Get Cold" and "Take the Burning Crown · The Burning Crown" (42). A Bargain trade is never alone (eight
   or more actions at once), the longest label "Trade ability power for attack damage" (37) is only reachable in a count of 8 or more, and
   "Pick up Forget-Me-Not Seed" (26) has no name. Estimate, not measured (Nunito, about .5 em a character): 44 characters need about 22 em,
   so in the 220 box (88 percent = 193.6 units) the label shrinks from 11 (text size 1) or 16.5 (text size 1.5) to about 8.8, above the
   floor 6; in the 300 box (264 units) it holds 11 at text size 1 and about 12 at 1.5. TMP never cuts a `HudUi.Label` (overflow is forced to
   Overflow), so there is no ellipsis: the rule is "shrink, down to `MinSize`, then run over". The swap from the count text to the action
   text is intended; the font size follows the text.
8. **Nothing else changes** (section 7). No library file: there is no arithmetic, and the one rule reads `QuestPresentation.PickupReward`,
   a HUD constant.

## 2. Dependencies

Base `lane/ui-rework/base-3`. Runs BEFORE P-UI-QUEST-OFFER-1-fix (both edit `ResetQuestRewardHud`; that packet is written on the tree that
holds this one). P-UI-TEXT-FIT-1 runs last in the wave and may change one label-creation statement in `InitializeQuestRewardHud`; for its
spec (the lane lead adds it): its prompt-lane row takes the new longest caption and the longest label-alone caption, "Collect hidden
token · Whispers in the Brush" and "Pick up Forget-Me-Not Seed". P-UI-JOURNAL-DETAILS-1 runs beside this packet and shares no file with it.

## 3. Relevant legacy paths

- `packages/client/src/ui/weaponBar.ts:183-197`: the F hint names the object of F ("swap X for Y" / "take <weapon>", "take <quest item>",
  the talk hint). No per-quest action list, no Meditate, no Abandon exists in `packages/` (verified with `rg`).

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/QuestActionPromptEditModeTests.cs` (and its `.meta`).

## 5. Exclusive write scope

- `A/Scripts/UI/MatchHud.QuestRewards.cs`, four hunks only: `NearbyActionsCaption` (:170-182) with the two fields above it
  (`_nearbyCaptionCount`, `_nearbyCaption`); `BindQuestRewardActions` (the button text at :377-378 and the caption at the end, :394); the end
  of `ResetQuestRewardHud` (:515); the new `QuestPresentation.ActionText`. The file has 593 lines (limit 800).
- Not in scope, by member: `RefreshQuestRewardHud` (the caption priority block and the launcher's active and interactable lines),
  `OpenQuestActions`, `TryInteract`, `OpenOffer`, `OfferCaption`, `UpdatePromptLane`, `SubmitQuestRewardChoice`, `InitializeQuestRewardHud`
  (the launcher's place, size and children), `RefreshOffer` and everything of the offer card; files `Net/MatchClient.cs` (the F line),
  `MatchHud.Features.cs`, `QuestOfferCard.cs`, `QuestJournalView.cs` (its `ActionButton` is called as today), `HelpGuideView.cs`, the
  library, `Tools/*`, `hud-seams.test.cjs`, `docs/DEVIATIONS.md`.
- The implementer edits no oracle file (section "Oracle files").

## 6. Contracts and references

- R-AUTH / D9: presentation only, text from delivered data. The control mapping is the owner's (`P-QUEST25-CONTROLS`): no key is bound or
  changed here.
- Readability gate: the file stays under 800 lines; the new method has a `///` summary.
- Motion/alloc rule of the lane: nothing formatted in a steady frame (`AllocationMeter.AssertNone`, `A/Tests/Editor/AllocationMeter.cs`).

## 7. Behaviour to preserve

- Which caption wins (choice, nearby, offer, turn-in), `OpenQuestActions`, `TryInteract` (F), `interactable`, the prompt lane, geometry.
- The window's button of action 9 reads "Pick up WallBuster" (pinned by `PlayerFlowsEditModeTests.NearbyRewardPickupUsesTheOfferedHandleAndDropIndex`).
- Two or more actions: "Nearby quest actions · n".

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-QUEST-ACTION-PROMPT-1 --base lane/ui-rework/base-3` prints GATE PASS.
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` through the wrapper passes (no method name declared in two `MatchHud*.cs`
  files; `ActionText` is free today).
- `scripts/readability_check.py` passes (800 lines).

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/QuestActionPromptEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/QuestActionPromptEditModeTests.cs.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/AbilityRowNearbyNoGarbageEditModeTests.cs` (the two `oracle-amend:` commits below)
- `docs/backlog/packets/P-UI-QUEST-ACTION-PROMPT-1.md`

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/unity-class.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-y QuestActionPromptEditModeTests,AbilityRowNearbyNoGarbageEditModeTests,PlayerFlowsEditModeTests,PromptPairNarrowEditModeTests,AbilityRowEditModeTests,QuestOfferEditModeTests`
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` (through `bin/in.sh`).

## 10. Risk and suggested model tier

implementer-sonnet: one file, four hunks, presentation only. The one care point is the steady-frame allocation (build the string at bind
time) and the closure capture in `BindQuestRewardActions` (compute the text before `AddListener`).

## 11. Known traps

- Build the caption at the END of `BindQuestRewardActions`, from the first counted action's text, and keep the existing
  `_nearbyQuestCount = nearby` there. Do not read the row again in `NearbyActionsCaption()`: it runs every frame. The window's `Set(...)`
  of each button and the caption share one string.
- The closure of each button captures `action`; compute the text before `AddListener`. Do not change the lambda.
- Tests read `_nearbyQuestCount`, `_nearbyCaption`, `_promptPair`, `RefreshQuestRewardHud`, `ResetQuestRewardHud` and `LateUpdate` by
  reflection: keep these names. No test reads `_nearbyCaptionCount`.
- `hud-seams.test.cjs` allows no method name declared in two `MatchHud*.cs` files (8-space indent, so `QuestPresentation` members count).
- A text change reaches the fit in play through `MythicTextScale.LateUpdate`; `UpdatePromptLane` refits only when the pair state changes.
  Add no refit: tests call `ApplyTextScale`.
- `ResetQuestRewardHud` runs from `RefreshQuestRewardHud` (session or epoch change) and `BindPlayerFlows(null)`; with a non-null view the
  same frame binds again, so zeroing the count is safe.

## Existing tests that pin this code

| Test | Asserts | Effect |
|---|---|---|
| `AbilityRowNearbyNoGarbageEditModeTests.ANearbyActionAllocatesNothing` (A/Tests/Editor, :176; line 186) | set-up caption of a lone action 9 with no row is "Nearby quest actions · 1" | amended: now "Pick up WallBuster". The class summary words "the caption is cached by its count" (:16) become "the caption is built when the actions are bound" (first amend commit). |
| `AbilityRowNearbyNoGarbageEditModeTests.TheNearbyCaptionFollowsTheCount` (:195; lines 202 and 224) | caption "· 1" for a lone action 9 (twice); line 218 "· 2" stays | amended: lines 202 and 224 become "Pick up WallBuster" (second amend commit). |
| `PlayerFlowsEditModeTests.NearbyRewardPickupUsesTheOfferedHandleAndDropIndex` (:126) | window button of action 9 reads "Pick up WallBuster" | unchanged, green |
| `PromptPairNarrowEditModeTests` (`TheLabelsStillFitAtTheSmallScale` :390, line 406-416 writes its own caption text; geometry tests) | geometry of the pair; labels fit with own text | unchanged, green |
| `AbilityRowEditModeTests` (`NothingFloatsAboveTheRow`, `APickupAndAQuestActionShareThePromptLane`) | geometry of the lane | unchanged, green |
| `QuestOfferEditModeTests.ThePromptLaneOffersTheTalk` (:697), `TheRewardChoiceCaptionStillWins` (:728) | offer caption "F · Talk to Old Marta" alone / "Talk to Old Marta" beside the pill; "Choose quest reward" wins | unchanged, green |
| `QuestJournalViewEditModeTests` | the journal's buttons (`QuestJournalView.ActionButton` is called as today) | unchanged, green |

No `Tools/*.cjs` copies a caption or the window's text; no dotnet test changes. No conflict between the new tests and an existing test
other than the three assertions amended for the owner (re-statements: a lone action 9 has no row, so its text is now the label alone).

## For the owner (amended expectations, listed for approval)

Two `oracle-amend:` commits on `AbilityRowNearbyNoGarbageEditModeTests`, each a re-statement and not a loosening: the lone action 9
("Pick up WallBuster", no quest row) now names itself in the caption instead of the count (lines 186, 202, 224 and the summary words).

Owner paragraph the lane lead sends: after this packet a player who walks up to Stay A While and accepts sees "Meditate · Stay A While"
above the action row, and the nearby-actions window reads the same way. What is still missing is the keyboard: nothing sends a quest
action but a mouse click (click the prompt, then the action in the window). F picks up a prop, else opens a quest giver's offer card, else
does nothing for an action. Two ways to close it were not built because they change what F does ("F interacts", `P-QUEST25-CONTROLS`):
(C) F opens the window of actions and never sends; (D) F performs the one action directly, which needs a client list of safe server
action ids (6, Meditate, is proven; 9 and 3 have side effects) or a server flag (a wire change), and a guard: the server stores the exact
position at Meditate and breaks the channel when the player moves, so an F pressed while a move key is held kills the meditation, and a
second press inside one round trip restarts its 30 seconds. The caption, a click and F would have to agree on one order (today actions
beat the offer in the caption, F opens the offer first). One ambiguity in view: a dropped quest spell in reach beside a weapon on the
ground shows "Pick up <spell>" next to the pickup pill, which also says pick up, and F takes only the weapon. Which of C and D, and which
ids, is the owner's decision.

## Notes from the oracle author

(filled in with the oracle commit)

## Notes from previous attempts
