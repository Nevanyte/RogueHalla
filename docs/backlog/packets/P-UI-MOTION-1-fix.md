# P-UI-MOTION-1-fix: a non-interactive fade never takes the pointer, from the moment it is built

- **Serves:** the one note of the review of P-UI-MOTION-1 (LAND WITH NOTES, 2026-11-22); same owner direction as that packet ("make the front end as fluid as possible"), UI rework lane.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `agent/sonnet/P-UI-MOTION-1` (stacked; the gate compares against it with `--base`)
- **D-rules touched:** D1 n/a (presentation) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome
`HudFade`'s constructor (`client-unity/UnityProject/Assets/Scripts/UI/UiMotion.cs:26-34`) adds a `CanvasGroup` to the root when it has none (line 31). A new group has `blocksRaycasts` and `interactable` true, and the constructor never applies the `interactive` flag. A non-interactive fade built on an active root therefore takes the pointer until a `Set` or `Snap` changes its state; `Set` to the state already wanted returns early (line 45), so that may never happen. P-UI-MOTION-1 promises (its packet, "a non-interactive fade never takes the pointer", `docs/backlog/packets/P-UI-MOTION-1.md:97-99`) otherwise. The quest tracker and toast lanes of the next wave build exactly such fades.

Fix: at the end of the constructor the group reflects the starting state: `blocksRaycasts` and `interactable` are true only when the fade starts shown (`_shown`, the root's `activeSelf`) AND is interactive; false otherwise. Nothing else changes: no other member, no timing, no public signature. Also add one sentence to the `HudFade` class summary (line 15): the owner shows and hides the root through `Set` and `Snap` only (a direct `SetActive` leaves the fade's state stale).

## 2. Dependencies
`agent/sonnet/P-UI-MOTION-1` (HudFade exists there).

## 3. Relevant legacy paths
None: presentation only, no legacy behaviour.

## 4. New paths
- `client-unity/UnityProject/Assets/Tests/Editor/HudFadeConstructionEditModeTests.cs` (+ `.meta`), the oracle.

## 5. Exclusive write scope
- `client-unity/UnityProject/Assets/Scripts/UI/UiMotion.cs`: the `HudFade` constructor and the class summary only.
- Not in scope: `UiMotionEditModeTests.cs` (change nothing in it).

## 6. Contracts and references
- Lane rules: presentation only; motion on unscaled time, instant under reduced motion, no allocation in a steady frame.

## 7. Behaviour to preserve
- When `Set`, `Snap` and `Tick` write the two flags (`UiMotion.cs:48-49`, `:59-60`) does not change. `Set` still returns early on the wanted state.
- Existing tests that pin this code, all must stay green unchanged: `UiMotionEditModeTests` (13 tests, `Assets/Tests/Editor/UiMotionEditModeTests.cs`). The ones touching the flags:
  - `AFadeInRisesToOneAndIsActiveFromTheFirstFrame` (:56): an interactive fade takes the pointer once shown (:76).
  - `AFadeOutStopsTakingThePointerAtOnceAndDeactivatesAtZero` (:80): flags true while shown, false at once on `Set(false)`.
  - `ANonInteractiveFadeNeverTakesThePointer` (:108): flags false through a fade in and out (after `Snap(false)`).
  - `ReducedMotionSnaps` (:173): flags false after hiding.
  - `AFadeUsesAnExistingCanvasGroupAndAddsOnlyOne` (:196): an existing group is the one driven, one group only.
  - `SettingTheWantedStateAgainDoesNothing` (:156), `ReversingAFadeContinuesFromTheCurrentAlpha` (:132), `AnIdleTickAllocatesNothing` (:325): alpha, Active and allocation behaviour.
  No other code or test uses `HudFade` (grep of `Assets/Scripts` and `Assets/Tests`).

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-UI-MOTION-1-fix --base agent/sonnet/P-UI-MOTION-1` prints `GATE PASS`.
- `unity-class.sh <worktree> HudFadeConstructionEditModeTests,UiMotionEditModeTests`: 19 tests, all green.

## Oracle files (read-only for implementers)
- `client-unity/UnityProject/Assets/Tests/Editor/HudFadeConstructionEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/HudFadeConstructionEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-MOTION-1-fix.md`

## 9. Validation commands
- `.agent-runs/lane-ui-rework/bin/unity-class.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-y HudFadeConstructionEditModeTests,UiMotionEditModeTests`

## 10. Risk and suggested model tier
Three lines in one constructor; Sonnet. The only hazard is over-reach (an interactive fade must keep the pointer).

## 11. Known traps
- No stub needed: the tests compile against the existing class.
- Do not change when `Set`, `Snap` or `Tick` write the flags.
- `_shown` is read from `activeSelf` in the constructor; use it for the starting state.

## Test status on the stub (base) and notes from the oracle author
Run on the base: 19 tests, 14 pass (the 13 of `UiMotionEditModeTests` plus test 4), 5 red on assertions:
- `ANonInteractiveFadeOnAnActiveRootTakesNoPointerFromConstruction`: "blocksRaycasts right after construction" expected False, was True.
- `ANonInteractiveFadeOnARootWithItsOwnGroupTakesNoPointerFromConstruction`: same assertion, expected False, was True.
- `SettingTheWantedStateAgainLeavesItNonBlocking`: "blocksRaycasts after Set(true) and a tick" expected False, was True.
- `AFadeBuiltOnAnInactiveRootBlocksNothing(true)` and `(false)`: "blocksRaycasts" expected False, was True. On the base the constructor leaves the new group's defaults on an inactive root as well, so the spec's guess that this might be green was wrong; it is red, and the same constructor fix turns it green (`_shown` is false, so nothing is taken).
- `AnInteractiveFadeOnAnActiveRootKeepsThePointer` is green on the base: it guards the fix against over-reach.
No stub commit; no existing test changes (no `oracle-amend`).
