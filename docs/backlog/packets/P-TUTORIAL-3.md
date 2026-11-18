# P-TUTORIAL-3: Tutorial presentation (Unity)

- **Serves:** not in the plan: owner approval 2026-11-15. Proposed home: ORDER_OF_OPERATIONS Phase 3, item 16.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** feature/unity-rework (86dd4adc), stacked on agent/sonnet/P-TUTORIAL-2 (f5f3e33a) until it merges
- **D-rules touched:** D1 n/a · D2 n/a · D5 flagged (owner-approved 2026-11-15) · D6 none · D9 kept (presentation only)

## 1. ID and outcome
A player whose profile says `TutorialDue` sees a small welcome card on the menu (Start / Skip; Resume when stored progress
is a `step` of the current version). Start launches Training (`MatchLaunchMode.Training`, as `UI/MatchHud.Accounts.cs:109`).
In a confirmed practice session a compact step card on the HUD stage (bottom-left, where the legacy training bar sat)
shows "Step n/5 · copy" with a Skip button, and a guide marker points at the step target: a ground ring
(`Presentation/GroundMarkArt.Ring`) plus a glyph at the target when on screen, and an edge marker computed with
`UI/EdgeThreatView.cs` `EdgeThreat.Compute` when off screen. Progress is saved fire-and-forget with `SaveTutorialAsync`
(`step` per advance, `done`, `skipped`). Settings gets a "Replay tutorial" row. Reduced motion
(`ShowcaseSettings.Current.ReducedMotion`) stops any pulse. The card never takes world clicks except its own buttons;
it hides while `TrainingHigherPanelVisible` or the training window is unfocused.

## 2. Dependencies
P-TUTORIAL-2 (TutorialFlow/Observer, SaveTutorialAsync, profile fields).

## 3. Relevant legacy paths
- `packages/client/src/ui/trainingBar.ts:10-20`: hint card placement/fade (legacy layout also in `docs/rework/LEGACY_LAYOUT.md`).

## 4. New paths
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Tutorial.cs` (partial: init, snapshot refresh, tick)
- `client-unity/UnityProject/Assets/Scripts/UI/TutorialView.cs` (welcome card, step card, guide marker; uGUI via `HudUi`/`UiTheme`)
- `client-unity/RoguehallaNet/Net/Ui/TutorialMarkerRules.cs` (pure: on/off-screen decision and edge placement input from target + camera-projected point; card visibility gate) and its synced copy
- `.meta` files for new scripts (fresh GUIDs, repo convention)

## 5. Exclusive write scope
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.cs` (three hooks: `InitializeTutorial()` after `_chromeStage.SetAsLastSibling()` L92; `RefreshTutorial` in `Snapshot` L228; `TickTutorial()` in `HudLateUpdate` L246)
- `client-unity/UnityProject/Assets/Scripts/UI/ModernSettingsView.cs` (a replay row via `Row` L154 + `HudUi.Button`, or in `FooterRow` L266)

## 6. Contracts and references
- Unity holds no game rules (AGENTS.md); C-OBS: markers only for targets in the current observation.
- `docs/rework/LEGACY_LAYOUT.md`, `docs/rework/UI_VERIFICATION.md`, `docs/rework/CONTRAST_GATE.md`.

## 7. Behaviour to preserve
- Training console focus/keyboard ownership (`MatchHud.Training.cs`), the HUD stage layout, and every existing panel.

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-TUTORIAL-3` passes (C# + sync steps). Unity EditMode (`client-unity/UnityProject/Tools/editmode.sh`) is reported as the named skip (no Editor in this environment); the hand-off says the Unity scripts are uncompiled.

## Oracle files (read-only for implementers)
- `client-unity/RoguehallaNet.Tests/TutorialMarkerRulesTests.cs`

## 9. Validation commands
- `dotnet test client-unity/RoguehallaNet.Tests`; `bash client-unity/sync-net.sh --check`; `client-unity/UnityProject/Tools/editmode.sh` (where an Editor exists)

## 10. Risk and suggested model tier
Medium: Unity code cannot be compiled here. Prior presentation on tag `tutorial-pre-rework` (`TutorialDirector.cs`,
`GuideArrow.cs`, `MatchHud.Tutorial.cs`) targets the old HUD; re-implement against `HudUi`/`HudStage`/`EdgeThreatView`/`GroundMarkArt`.

## 11. Known traps
- `MatchHud.LateUpdate` now only wraps `HudLateUpdate()` in PerfMarks.
- Copy the `RefreshTrainingConsole` gate (`_trainingWindowFocused`, `TrainingHigherPanelVisible`, accepted snapshot identity).
- The canvas is ScreenSpaceOverlay: pass a null camera to `RectTransformUtility`.

## Line references verified on the base (f5f3e33a)
All brief references hold except as noted. `MatchHud.cs`: `_chromeStage.SetAsLastSibling()` L92 (`InitializeTrainingConsole()` L91), `Snapshot` L228, `HudLateUpdate` L246. `ModernSettingsView.cs`: `Row` L154, `FooterRow` L266 (its row is 62/36 high; the Reset button sits TopRight), `SwitchRow` L70-83 for the pattern. `EdgeThreatView.cs`: `EdgeThreat.Compute(dx, dy, groundDistance, width, height, far)` L21 (origin bottom-left, y up; `dx`/`dy` are the bearing, so the helper's output angle feeds it). `GroundMarkArt.Ring(Color32 core, bool fill=false, float band=Band)` L18. `MatchHud.Training.cs`: gate is `_trainingWindowFocused` L14, `TrainingHigherPanelVisible` L26, and the accepted-snapshot checks in `RefreshTrainingConsole` L39-46. `MatchHud.Accounts.cs:109` is `_client.Join(..., MatchLaunchMode.Training)`. Correction: the flow has `TutorialFlow.StepCount = 7` (Welcome=0, Move..Wish=1..5, Done=6), so the card says "Step n/5" with n = index 1..5; `TutorialFlow.Running` is false when skipped or at Done. The synced copy of `TutorialFlow.cs` has no `.meta` on the base; the new helper's copy gets one (neighbour convention in `Assets/RoguehallaNet/Net/Ui/*.cs.meta`).

## Helper surface (`Roguehalla.Net.Ui.TutorialMarkerRules`, pure)
- `CardVisible(confirmedPractice, windowFocused, higherPanelVisible, flowActive)`: all of practice, focus, flow active; no higher panel.
- `WelcomeVisible(tutorialDue, menuVisible, dismissedThisSession, inMatch)`: due, menu shown, not dismissed, not in a match.
- `WelcomeAction(AccountTutorialState stored, int currentVersion)`: "Resume" only for state "step", same version, step > 0; else "Start" (null, done, skipped, legacy "started", old version, step 0).
- `Place(x, y, inFront, width, height, margin)` -> `MarkerPlacement{OnScreen, X, Y, Angle}`; screen coordinates origin top-left, y down, `Angle = atan2(dy, dx)` of the ray from the centre in those coordinates. On screen = in front and inside [0,w]x[0,h]; otherwise clamped to the rectangle inset by `margin` along the centre ray; behind-camera points are mirrored through the centre first and are always off screen; a point exactly at the centre when behind lands bottom-centre (Angle = pi/2).
- `StepLabel(index, total, copy, wishKey)`: "Step n/total \u00B7 copy" for 1..total, `{wish}` replaced by `wishKey`; index 0 or > total gives the copy alone.

## Oracle evidence
`dotnet test client-unity/RoguehallaNet.Tests` on the stub commit: Failed 8, Passed 2156, Total 2164; all 8 failures are the new TutorialMarkerRulesTests (assertions on the stub defaults false/null/default). `sync-net.sh --check` clean. `gate.sh --oracle-check` cannot judge C#.

## Notes from previous attempts
