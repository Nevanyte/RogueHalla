# P-TUTORIAL-3-fix: Tutorial done card and launch-gated arming

- **Serves:** not in the plan: owner approval 2026-11-15 (review notes on P-TUTORIAL-3).
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** feature/unity-rework (86dd4adc), stacked on agent/sonnet/P-TUTORIAL-3
- **D-rules touched:** D1 n/a · D2 n/a · D5 owner-approved 2026-11-15 · D6 none · D9 kept (presentation only)

## 1. ID and outcome
Two review notes on `agent/sonnet/P-TUTORIAL-3` (reviewer verdict LAND WITH NOTES):
1. **Done card.** After the Wish step the flow reaches Done (index 6) but `CardVisible(... flowActive = _tutArmed && Running)`
   is false and `RefreshTutorial` disarms, so the copy "Tutorial complete. Enter the Arena when ready." never shows. When
   the flow completes in practice, the step card stays up showing the Done copy (via `StepLabel` index > total, copy
   alone) with a "Close" button. Close only hides the card; it never calls Skip or saves "skipped" (the stored "done"
   must survive). The card hides when practice ends.
2. **Arm only on launch.** `StartTutorialFromWelcome` and `ReplayTutorial` arm the flow and save "step" before
   `StartTraining()`, which can return without launching (`_joinTraining` not interactable). Arm and save only when the
   Training launch actually started; Replay from outside the menu arms only when a launch starts.

## 2. Dependencies
agent/sonnet/P-TUTORIAL-3.

## 3. Relevant legacy paths
- none (legacy has no tutorial).

## 4. New paths
- none expected.

## 5. Exclusive write scope
- `client-unity/RoguehallaNet/Net/Ui/TutorialMarkerRules.cs` (+ synced copy)
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Tutorial.cs`, `client-unity/UnityProject/Assets/Scripts/UI/TutorialView.cs`
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Accounts.cs` only if `StartTraining` must report whether it launched (return bool; keep existing callers' behaviour)

## 6. Contracts and references
- Unity holds no game rules; presentation only. Card pointer blocking only on its button.

## 7. Behaviour to preserve
- All P-TUTORIAL-3 behaviour and the 8 TutorialMarkerRules oracle tests.

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-TUTORIAL-3-fix` passes; Unity EditMode named skip.

## Oracle files (read-only for implementers)
- `client-unity/RoguehallaNet.Tests/TutorialMarkerRulesFixTests.cs` (pure rules: done-card visibility, and an arm decision that requires a started launch)

## 9. Validation commands
- `dotnet test client-unity/RoguehallaNet.Tests`; `bash client-unity/sync-net.sh --check`

## 10. Risk and suggested model tier
Low; small presentation fix. Unity code uncompiled here.

## 11. Known traps
- `Flow.Skip()` is a no-op when Done, but `SkipTutorial` still saves "skipped": the Close path must not reuse it.

## Notes from previous attempts

## Rule surface (stubbed in `TutorialMarkerRules`, implementer fills in)
- `DoneCardVisible(confirmedPractice, windowFocused, higherPanelVisible, flowDone, doneDismissed)` = confirmedPractice && windowFocused && !higherPanelVisible && flowDone && !doneDismissed.
- `ShouldArm(launchStarted)` = launchStarted. Use it in `StartTutorialFromWelcome` and `ReplayTutorial` before arming and saving "step".
- `StepLabel(7, 5, copy, key)` already returns the copy alone (pinned, passes today). Close must only set a dismissed flag, never Skip or save "skipped".

## Oracle evidence
- Oracle file: `client-unity/RoguehallaNet.Tests/TutorialMarkerRulesFixTests.cs` (4 tests). The existing `TutorialMarkerRulesTests.cs` is untouched.
- `dotnet test client-unity/RoguehallaNet.Tests` builds: Failed 2, Passed 2166, Total 2168.
- Red on assertions: `ArmingRequiresAStartedLaunch` (expected False, was True) and `DoneCardShowsInPracticeAfterCompletionUntilClosed` (expected True, was False).
- Green against the stubs by design (negative guards): `DoneCardHidesWhenPracticeEndsOrIsCoveredOrUnfocused`, `DoneCardShowsTheCopyAloneWithoutStepPrefix`.
- `bash client-unity/sync-net.sh --check`: up to date.
