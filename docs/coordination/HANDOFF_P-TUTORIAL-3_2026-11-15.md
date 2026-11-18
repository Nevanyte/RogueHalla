# Hand-off: P-TUTORIAL-3 and P-TUTORIAL-3-fix (2026-11-15)

PACKET: P-TUTORIAL-3, tutorial presentation (Unity), with its review-note packet P-TUTORIAL-3-fix
BRANCHES: agent/sonnet/P-TUTORIAL-3 (b0220f01) · agent/sonnet/P-TUTORIAL-3-fix (0f94d0d8, this branch, contains P-TUTORIAL-3)
ORACLES: packet/P-TUTORIAL-3 (43d246c2) · packet/P-TUTORIAL-3-fix (0bfbfbb0)
BASE: feature/unity-rework 86dd4adc, **stacked**: P-TUTORIAL-3 on agent/sonnet/P-TUTORIAL-2 (f5f3e33a, unmerged); the fix on agent/sonnet/P-TUTORIAL-3.
Merging agent/sonnet/P-TUTORIAL-3-fix brings P-TUTORIAL-2, P-TUTORIAL-3 and the fix.
SERVES: not in the plan: owner approval 2026-11-15. D5 flagged.

GATE:
- P-TUTORIAL-3: `scripts/gate.sh --quick --oracle packet/P-TUTORIAL-3` → GATE PASS (quick), skipped: Unity EditMode.
- Fix: `scripts/gate.sh --quick --base agent/sonnet/P-TUTORIAL-3 --oracle packet/P-TUTORIAL-3-fix` → GATE PASS (quick),
  skipped: Unity EditMode. Without `--base` the oracle step reports "oracle files changed" because the stacked
  P-TUTORIAL-3 commits touch the same files; the coordinator's merged-tree gate does not have this issue.
- `dotnet test client-unity/RoguehallaNet.Tests` green including 8 `TutorialMarkerRulesTests` + 4 `TutorialMarkerRulesFixTests`; `sync-net.sh --check` clean.

CHANGED: `RoguehallaNet/Net/Ui/TutorialMarkerRules.cs` (pure: card/welcome/done visibility, Start/Resume, edge
placement, step label, arm decision) + synced copy; new `UI/TutorialView.cs`, `UI/MatchHud.Tutorial.cs` (+ `.meta`);
hooks in `UI/MatchHud.cs` (init after `_chromeStage.SetAsLastSibling()`, `Snapshot`, `HudLateUpdate`);
`UI/ModernSettingsView.cs` "Replay tutorial" row (`ReplayTutorialRequested`); `UI/MatchHud.Accounts.cs`
`StartTraining()` now returns bool (its two `TrainingRequested` delegates became lambdas).

WHAT CHANGES FOR A PLAYER: a callsign with `tutorialDue` sees a welcome card (Start/Resume, Skip); Start opens Training
with a step card (bottom-left, "Step n/5 · …", Skip) and a guide marker (ground ring on screen, edge triangle off
screen); steps: move to ring → take a weapon (F) → hit the dummy → swap (Q/1/2) → Wish (R) → a Done card with Close.
Progress is saved to the profile (needs P-TUTORIAL-1 on the server; without it saves fail silently and the welcome card
never shows because `tutorialDue` is false). Settings → Replay tutorial.

D-RULES: D1/D2 n/a · D5 owner-approved · D6 untouched · D9 kept (presentation only; no input sent by the tutorial).

REVIEW:
- P-TUTORIAL-3 (reviewer): LAND WITH NOTES. Note 1 Done card never shows; note 2 flow armed/saved even when the Training
  launch did not start. Both fixed in P-TUTORIAL-3-fix.
- P-TUTORIAL-3-fix (reviewer): LAND.
Minor, not notes:
- No glyph at the target when on screen (ring only).
- Ring sprite draws at 0.80 of its half-width, so the visible ring is ~20% smaller than the target radius.
- Edge marker uses `TutorialMarkerRules.Place` (the oracle surface) rather than `EdgeThreat.Compute`; clamps to `Screen.width/height`, not the stage viewport (letterbox).
- Unused `using Roguehalla.Net;` in `TutorialView.cs`; "NEW HERE?" accent-on-surface text to be checked against `docs/rework/CONTRAST_GATE.md`.
- `WelcomeVisible`'s `dismissedThisSession` is always false in production (dismissal tracked in `_tutDismissedFor`).
- `SaveTutorial` failures are swallowed (fire-and-forget).
- A refused launch from the welcome card still hides the welcome card for the session.
- `_tutDoneDismissed` is set inside the per-tick update when practice ends (commented).

PINNED VALUES TO RE-TAKE: none.
DEVIATIONS (proposed row, not applied): "Guided first-login tutorial in Training (P-TUTORIAL-1..3). Legacy has only the
five fading practice-room hints (`packages/client/src/ui/trainingBar.ts:10`). Reversible: raise nothing / server returns
`tutorialDue=false`. Re-validation: n/a (presentation)."
NOT ESTABLISHED: **the four Unity files are uncompiled** (no Editor here); no EditMode run; no visual/layout check
(step card may overlap the training console bottom-left).
