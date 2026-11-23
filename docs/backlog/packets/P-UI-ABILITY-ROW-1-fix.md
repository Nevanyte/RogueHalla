# P-UI-ABILITY-ROW-1-fix: a frame with nearby quest actions allocates nothing, and a test builds that case

- **Serves:** not in ORDER_OF_OPERATIONS. Note 1 of the coordinator's merge review of UI rework wave 2 (`reviewer-opus`, 2026-11-23), a
  broken guarantee of P-UI-ABILITY-ROW-1: near a quest giver `RefreshQuestRewardHud` builds `"Nearby quest actions · " + _nearbyQuestCount`
  every frame, inside the function that `AbilityRowEditModeTests.ASteadyQuestRefreshFrameAllocatesNothing` measures with a rig that never
  builds that case. Owner direction 2026-11-22 (UI optimization sweep).
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` (head 632ecb349), which already contains P-UI-QUEST-OFFER-1.
- **D-rules touched:** D1: legacy has no nearby-quest-actions launcher (a search of `packages/` finds no such text), nothing to match; the
  player sees the same texts and behaviour; no DEVIATIONS row. D2 n/a. D5/D6 none. D9 untouched: a string is cached, no rule is decided.
- **Worktree:** `/Users/nevinmckinnon/Documents/Projects/RH-ui-c`

## 1. ID and outcome

1. With the same delivered state and the server time fixed, one call of `RefreshQuestRewardHud` allocates nothing (200 calls,
   `AllocationMeter.AssertNone`) in each state: (a) nearby quest actions and nothing else; (b) a pending reward choice, with the overlay open,
   with the choice sent and awaited, and with LATER pressed (deferred); (c) nearby actions together with the pickup pill (the pair lane,
   `_promptPair` true); (d) quest spells delivered, one cooling and one ready; (e) a board quest row alone (no spell, pending or action).
2. `"Nearby quest actions · n"` is built only when `n` changes and keeps its exact text; `n` going 1, 2, 1 shows 1, 2, 1. Nothing a
   player sees or does changes.
3. Every `QuestHandle?` against `QuestHandle?` comparison in the file goes through one helper that boxes nothing and answers as
   `Nullable<T>.Equals(object)` did.
4. Still allowed: a call that sees a new `QuestView` object; the rebuild of a spell's strings on the frame its remaining whole seconds change
   (once a second in play). The tests deliver one snapshot time (`Session.ServerTimeMs` stays 1000), so no key changes in the 200 calls.

### What is wrong today (read from the code, base 632ecb349; confirmed by the oracle author's runs below)

`RefreshQuestRewardHud` (`client-unity/UnityProject/Assets/Scripts/UI/MatchHud.QuestRewards.cs:96`) runs every frame (`TickPlayerFlows` ->
`RefreshPlayerFlows`). `_nearbyQuestCount` (field, line 29) counts the `view.Actions` whose `Action` is neither `Abandon` nor `RewardChoice`,
and is set when a new `QuestView` arrives (`BindQuestRewardActions`, line 303, assignment at line 359). While it is above 0, no reward choice
is pending and the box shows, line 163 passes `"Nearby quest actions · " + _nearbyQuestCount` to `Set`. It sits in the `else` after the
`!others && _offerPrompt` branch (line 161, `OfferCaption()`, line 169). The string is built before `Set` compares it, so each frame allocates
two strings. The caption label (`_questActionCaption`) exists, so the strings reach a live label.

Every place on the per-frame path that can allocate:
1. The caption concat (line 163): nearby actions delivered, no pending choice.
2. `_questChoice.Equals(_questChoiceDeferred)` in the overlay test (line 124): `Nullable<QuestHandle>.Equals(object)` boxes its argument
   when it has a value. `_questChoiceDeferred` has one after LATER (`CloseQuestRewardModal`, line 458-463), for as long as the choice stays pending.
3. `WaitingForQuestChoice` (line 373, `_questChoice.Equals(_questChoiceSent)`): the same boxing, run while the reward overlay is open (line
   151), from the click until the server's next view clears `_questChoiceSent`.
4. Not a cause: `QuestPresentation.HasRewardHud` -> `HasBoardRows` is an index loop on this base (test 8 keeps it so).
5. The offer path (`RefreshOffer` line 216, `OfferCaption`, `PlaceOffer` line 272, `QuestOffer.Pick`, `Find`) reads clean; OFFER-1's
   `AClosedCardWithAnOfferInReachAllocatesNothing` (`QuestOfferEditModeTests.cs:966`) and `AnOpenCardAllocatesNothing` (line 978) measure it. Out of this packet.
6. Allowed, not per frame: `BindQuestRewardActions` (lambdas, `FindRow`, `IsPending`, concats) runs once per new `QuestView` object;
   `RefreshQuestTiles` (line 378) builds strings only when a slot's key changes; `UpdatePromptLane` (line 286) writes only when the pair
   state changes; `OpenQuestActions` and `SubmitQuestRewardChoice` run on a click.

## 2. Dependencies

P-UI-QUEST-OFFER-1 (merged on the base), P-UI-ALLOC-METER-1 (`Assets/Tests/Editor/AllocationMeter.cs`, on the base). P-UI-QUEST-REWARD-PROMPT-1
(base-4) comes after this packet (see Known traps).

## 3. Relevant legacy paths

None: no legacy equivalent (`rg "Nearby quest actions" packages/` finds nothing). Presentation only.

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/AbilityRowNearbyNoGarbageEditModeTests.cs` and its `.meta` (oracle).

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.QuestRewards.cs` (554 lines; keep under 800, which
  `client-unity/UnityProject/Tools/hud-seams.test.cjs:92` pins): `NearbyActionsCaption` and its fields, `QuestPresentation.SameHandle`, and
  each `QuestHandle?` `.Equals(` on another `QuestHandle?`: the overlay test (line 124), `WaitingForQuestChoice` (line 373) and
  `BindQuestRewardActions` (lines 318 and 319; one rule for the whole file). The `QuestHandle` against `QuestHandle` calls
  (`row.Handle.Equals`, `.Value.Equals(...)`, lines 330, 365, 503, 511, 541) use `IEquatable<QuestHandle>` and stay.
- The oracle files and this packet are read-only for the implementer.
- No library class and no dotnet test: the change is a string cached by an int and a null-aware compare.
- Not in scope: `UI/AbilityTile.cs`, `RoguehallaNet/Net/Ui/AbilityFace.cs` (green under the meter; if test 7 is red, stop and report the
  allocation), `AbilityRowEditModeTests.cs` (other packets amend it), `QuestOfferEditModeTests.cs`, `MatchHud.cs`, `MatchHud.PlayerFlows.cs`,
  `MatchHud.Features.cs`, `Net/MatchClient.cs`, `QuestJournalView.cs`, `QuestOfferCard.cs`, `AllocationMeter.cs`, `hud-seams.test.cjs`.

## 6. Contracts and references

- Presentation only (lane COMMON.md): no rule decided in Unity (R-AUTH, D9). "Nothing that allocates in a steady frame."
- API (all in `MatchHud.QuestRewards.cs`; both names are free in every `MatchHud*.cs`, which the node test "no method name is declared in
  two MatchHud files" needs):

```csharp
// New fields of MatchHud: int _nearbyCaptionCount = -1; string _nearbyCaption = "";
string NearbyActionsCaption();   // rebuilds "Nearby quest actions · " + _nearbyQuestCount only when the count
                                 // differs from _nearbyCaptionCount; returns _nearbyCaption
// QuestPresentation (same file): both null true; one null false; else a.Value.Equals(b.Value).
public static bool SameHandle(QuestHandle? a, QuestHandle? b);
```

`NearbyActionsCaption()` replaces only the operand `"Nearby quest actions · " + _nearbyQuestCount` in line 163; the `_offerPrompt` branch,
`OfferCaption` and the `_promptCaption*` fields stay as OFFER-1 left them. The text depends on the count alone, so `ResetQuestRewardHud`
(line 465) needs no change. `SameHandle` must answer exactly as `Nullable<QuestHandle>.Equals(object)` did: a quest against null is not
equal, two nulls are. Do not move `QuestPresentation` out of this file.

## 7. Behaviour to preserve

- Every text and state a player sees, including the caption "Nearby quest actions · n", "Choose quest reward", "Return to turn in your reward",
  "Waiting for server…", and what LATER and the click on the launcher do.
- Names reached by reflection: `_questChoice`, `_questChoiceDeferred`, `_questChoiceSent`, `_nearbyQuestCount`, `_promptPair`,
  `_questChoiceStatus` (new tests) and `_questChoiceSpells` (`AbilityRowEditModeTests.cs:357`): do not rename.
- OFFER-1's work (index loops, the offer caption cached by key, `RefreshOffer`): do not redo or undo.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-ABILITY-ROW-1-fix --base lane/ui-rework/base-3` prints `GATE PASS`.
- All 8 tests of `AbilityRowNearbyNoGarbageEditModeTests` green, plus the existing classes below still green.
- If any `MatchHud` member is moved, `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` (this packet should not need it).

## Existing tests that pin this code (all green on the stub; each must stay green)

- `AbilityRowEditModeTests` (`Tests/Editor/AbilityRowEditModeTests.cs`): `APickupAndAQuestActionShareThePromptLane` (line 260: a nearby
  action then a pickup pill, asserts the pair layout and that the launcher stays active); `ADeliveredQuestSpellShowsNameChargesAndCountdown`
  (line 315: tile texts, wedge and frame colours); `TheEquippedLineStillListsBothSpells` (line 354: reads `_questChoiceSpells`);
  `NothingFloatsAboveTheRow` (line 242: the box holds only the launcher); `AnUnchangedTileAllocatesNothing` (line 454) and
  `ASteadyQuestRefreshFrameAllocatesNothing` (line 473): one-spell frame allocates nothing under `AllocationMeter`.
- `PlayerFlowsEditModeTests` (`Tests/Editor/PlayerFlowsEditModeTests.cs`): `FullQuestSlotsPresentServerChoiceAgainAfterReconnectAndSendTheChosenTuple`
  (line 74: overlay opens on a pending choice, equipped line, sends the chosen tuple after reconnect);
  `NearbyRewardPickupUsesTheOfferedHandleAndDropIndex` (line 126: the launcher opens the nearby overlay, the action sends QuestInteract 9).
- `QuestOfferEditModeTests` (`Tests/Editor/QuestOfferEditModeTests.cs`): `TheRewardChoiceCaptionStillWins` (line 728: LATER, then the launcher
  reads "Choose quest reward"); `AClosedCardWithAnOfferInReachAllocatesNothing` (966) and `AnOpenCardAllocatesNothing` (978).
- `UiInputRoutingEditModeTests`: digit keys and quest overlay key routing (not touched by this change).
- Node test `Tools/hud-seams.test.cjs`: file at most 800 lines, no method name declared in two MatchHud files.
- No existing test asserts the old behaviour this packet changes, so there is no `oracle-amend:`.

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/AbilityRowNearbyNoGarbageEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/AbilityRowNearbyNoGarbageEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-ABILITY-ROW-1-fix.md`

## 9. Validation commands

- `unity-class.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-c AbilityRowNearbyNoGarbageEditModeTests,AbilityRowEditModeTests,PlayerFlowsEditModeTests,QuestOfferEditModeTests,UiInputRoutingEditModeTests`
- `scripts/gate.sh --quick --oracle packet/P-UI-ABILITY-ROW-1-fix --base lane/ui-rework/base-3` through `in.sh`.

## 10. Risk and suggested model tier

Low risk: a cached string and a null-aware compare in one file. Sonnet. The only trap is `SameHandle` semantics.

## 11. Known traps

- `Typing` (`MatchHud.Features.cs`) is reached through `WorldHudVisible` when no earlier gate is true. It calls `GetComponent<TMP_InputField>()`
  on the selected object and a missing component allocates in the editor only; the overlay's `UiFocusNavigation.Cycle` may have left a button
  selected after it closed. Test 5 clears the selection before it measures; no other workaround.
- `Session.ServerTimeMs` is the last snapshot's `ServerMs`, not a clock: every snapshot is delivered with 1000.
- `AllocationMeter` warms the loop once and checks itself on every call: never lower the 200 or catch its failure.
- For P-UI-QUEST-REWARD-PROMPT-1 (base-4, after this packet): it drops `_questChoiceDeferred`, compares `_questChoice` with a new
  `_questChoiceOpen`, and replaces the nearby caption. It must use `QuestPresentation.SameHandle` for every handle comparison and keep every
  caption cached by its key. If it changes the captions it amends test 2 of the new class; if it changes when the overlay opens or what LATER
  does, it amends tests 3, 4 and 5, each in its own `oracle-amend:` commit listed in its packet.

## Not established

- `BindQuestRewardActions` allocates on every new quest view object, so "a steady frame" means the frames between two deliveries of the
  quests state. How often the server re-sends that state was not measured.
- The meter counts `GC.Alloc` profiler samples in the editor; it does not show allocation in a player build.

## Notes from the oracle author

(filled after the runs, in the oracle commit)

## Notes from previous attempts
