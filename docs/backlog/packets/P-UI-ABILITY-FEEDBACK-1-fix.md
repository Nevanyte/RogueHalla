# P-UI-ABILITY-FEEDBACK-1-fix: a replaced toast line holds for its new text, and the one-scale-writer test overlaps two pops again

- **Serves:** not in the plan: a fix of two faults that P-UI-ABILITY-FEEDBACK-1 (merged on the lane branch as `df64266a4`) left behind, found by the second reader of its hand-off and read in the code by the lane lead
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` (head `fe3360a5c`: sixteen wave 4 packets on integration 87)
- **D-rules touched:** D1 n/a (presentation only) · D2 n/a · D5/D6/D9 none (no wire, no game rule, no `crates/`)

## 1. ID and outcome
Two small rules.

1. **A keyed line that is replaced holds for its NEW text.** Before P-UI-ABILITY-FEEDBACK-1, `ToastLaneModel.Restart` set the hold
   from the text the line shows now. That packet gave every line its own `Item.Hold` (so the cast line holds 1 s) and made `Restart`
   read it (`client-unity/RoguehallaNet/Net/Ui/ToastLaneModel.cs:109-114`). `Hold` is set when the line is created. On a keyed hit
   with a new text and no explicit hold, `Push` replaces the text and leaves `Hold` alone (`ToastLaneModel.cs:54-55` never-late hit,
   `:72-73` plain keyed hit), so the new text is held for the OLD text's time. `NoticeRouter.HoldSeconds`
   (`client-unity/RoguehallaNet/Net/Ui/NoticeRouter.cs:79-84`: `2.2 + .045 * (length - 20)` for a text over 20 characters, clamped to
   2.2 .. 4.5 s) grows with length, so a longer replacement is shown too briefly and a shorter one too long. The earlier packet's
   rule was "every other caller of Push is unaffected"; no test pinned this case. When it lands: a keyed push that hits a line
   (showing or waiting) and gives no explicit hold sets that line's hold to `NoticeRouter.HoldSeconds(newText)` before the restart; one
   that gives a valid hold (finite, above 0) sets that hold, as today. Both keyed paths of `Push`. The unkeyed same-text hit (the
   counter, `:80-86`) is untouched: its text does not change. Nothing else in the model changes.
2. **`AbilityMotionEditModeTests.TheWishTileHasOneScaleWriter` overlaps two pops again.** The test exists to show that two pulses on
   one tile do not fight over its scale. It used to press 50 ms after an accepted cast, inside the accept pop (original at
   `f90ca108a`, asserting `localScale.x <= 1 + HudPulses.Pop + .001` and `Glow.enabled` for 150 ms after the press). Under
   P-UI-ABILITY-FEEDBACK-1 that press is refused (the Wish is cooling: `MatchHud.Pulses.cs:22-27` pushes the feedback line and no
   pop), so the amend moved the press to 300 ms after a ready snapshot, AFTER the ready pop has ended. A second scale writer that
   stacks on the press's pop then passes. This packet's oracle restores the overlap: the press happens while the READY pop of the
   same tile is still running, the tile reading Ready at the press. Product code is not touched for this item (the product has one
   writer, `HudPulses`); the amended test is a standing guard, green on the base by design.

## 2. Dependencies
P-UI-ABILITY-FEEDBACK-1 and P-UI-ABILITY-MOTION-1 (both merged in the base).

## 3. Relevant legacy paths
none (client presentation only; Unity holds no rule).

Code read for this packet:
- `client-unity/RoguehallaNet/Net/Ui/ToastLaneModel.cs`: `Push` 45-96 (never-late keyed branch 49-66, hit at 51-59; plain keyed hit
  67-77, hit at 69-75; unkeyed counter 78-87; new item 88-95, `Hold` set there), `Restart` 109-114, `Advance` 196-215 (a line fades in
  .12 s, then `HoldLeft` counts down in the Hold phase, then fades out .25 s), `SetSticky` 123-138 (sticky lines are skipped by `Find`
  at :103).
- Product callers of `Push`: `MatchHud.Notices.cs:53` (routed notices: key from the router, no hold: the path this fix changes) and
  `:58` (`PushFeedbackLine`: cast lines, `CastReadiness.HoldSeconds` = 1 s, `dropIfLate: true`; `CastReadiness.cs:23`).
- `MatchHud.Pulses.cs:19-39` (`InputCue`: a Cast pops the tile only when the reading has no refusal line and is not Empty),
  `MatchHud.ActionBar.cs:251-263` (`TickWishTileMotion`: asks `HudPulses` for the pop on the flash or ready-pulse edge),
  `MatchHud.ActionBar.cs:70-77` (`TickActionRow`: `TickWishTileMotion` then `TickPulses`), `UI/UiMotion.cs` `HudPulses`
  (`Seconds` .15, `Pop` .08; `Play` on a rect already pulsing RESTARTS it, keeping one entry; `Tick` is the one scale writer).

## 4. New paths
none (tests are added to existing files).

## 5. Exclusive write scope
- `client-unity/RoguehallaNet/Net/Ui/ToastLaneModel.cs` (`Push`: the two keyed-hit branches only) and its synced copy
  `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/ToastLaneModel.cs` (run `bash client-unity/sync-net.sh` through the lane's
  `in.sh` wrapper; the gate checks the copy).
- Nothing else. No `MatchHud` file, no `AbilityTile`, no `CastReadiness`, no test file (the oracle files are read-only).

## 6. Contracts and references
- Presentation only (R-AUTH): no wire, no rule. `docs/agent/README.md` step 3 and the lane rules (`.agent-runs/lane-ui-rework/specs/COMMON.md`).
- No allocation in a steady frame: `ToastLaneModelTests.ATickThatChangesNothingAllocatesNothing` stays green (the change is in `Push`, not `Tick`).

## 7. Behaviour to preserve
- `Hold` stays per line. Do NOT go back to computing the hold in `Restart` (the cast line's 1 s would be lost on a restart): set `Hold`
  where the text is replaced.
- A hold that is not finite or not above 0 is "no explicit hold" (`hasHold`, `ToastLaneModel.cs:48`).
- A keyed hit WITH an explicit hold keeps that hold (the cast line's 1 s). An unkeyed repeat of the same text restarts with the same hold.
- A sticky line (the first-run hint, `SetSticky`) is not a keyed push and is unaffected.
- Fade times, queueing, `MinShowSeconds`, the never-late removal while invisible, `End`: unchanged.

## 8. Acceptance checks
- `dotnet test client-unity/RoguehallaNet.Tests --filter ToastLane` green (the new tests in `ToastLaneHoldTests` red on the base, green after).
- `scripts/gate.sh --quick --oracle packet/P-UI-ABILITY-FEEDBACK-1-fix --base lane/ui-rework/base-3` prints `GATE PASS`.
- Unity classes `AbilityMotionEditModeTests`, `AbilityFeedbackEditModeTests`, `ToastLanesEditModeTests`, `FirstRunHintEditModeTests` stay green.

## Oracle files (read-only for implementers)
(filled in by the oracle commit)

## 9. Validation commands
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter "ToastLaneHoldTests|ToastLaneModelTests"`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> bash client-unity/sync-net.sh`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> AbilityMotionEditModeTests,AbilityFeedbackEditModeTests,ToastLanesEditModeTests,FirstRunHintEditModeTests`

## 10. Risk and suggested model tier
Low: a few lines in `Push`. implementer-sonnet; reviewer (not reviewer-opus: no `rh-sim`, no wire).

## 11. Known traps
- The library file has a synced copy: a gate red on "Unity copy is not up to date" means the sync was not run.
- A waiting line (lane full) takes the same hit branch; set `Hold` there too (`Find(_waiting...)`, `:69`).
- `Restart` revives a leaving line; the new `Hold` must be set before `Restart` reads it.
- No `Assert.Multiple`; no `InternalsVisibleTo`; EditMode time does not advance (the dotnet tests pass `dt` in).

## Notes from the oracle author
(filled in by the oracle commit)

## Existing tests that pin this code (read 2026-11-23; all stay green)
| Test | What it asserts | Affected? |
|---|---|---|
| `ToastLaneModelTests.AKeyedPushReplacesTheTextInPlaceWithoutACounterAndRestartsTheHold` (`client-unity/RoguehallaNet.Tests/ToastLaneModelTests.cs:159`) | "Imp: +1 kill stack" then "+2 kill stacks" then "+3": one line, no counter, hold restarts (opaque 2.0 s after the last push) | No: all three texts are 19-20 characters, hold 2.2 s either way |
| `ToastLaneModelTests.AKeyedPushMakesNoSecondLineBesideOtherLines` (:179), `AKeyedPushReplacesTheTextOfAWaitingLine` (:191) | in-place replace beside other lines; a waiting line's text is replaced, not added | No: they assert text and counts, not the hold |
| `ToastLaneModelTests.TheSameTextAgainIsOneLineWithACounterAndAFreshHold` (:123) | unkeyed repeat: counter, fresh hold | No: the unkeyed path is untouched |
| `ToastLaneModelTests.ALongerTextHoldsLonger` (:110) | a 40-character text holds 3.1 s | No: a new line |
| `ToastLaneHoldTests.ARestartedKeyedLineTakesTheHoldOfThePush` (`ToastLaneHoldTests.cs:94`) | keyed "a" then "b" with hold 1 s: "b" gone at 1.5 s | No: explicit hold |
| `ToastLaneHoldTests.ALineWithRoomShowsAtOnceAndASecondPressReplacesIt` (:70), `ALeavingKeyedLineIsRevivedByANeverLatePush` (:161) | never-late keyed replace and revive with an explicit 1 s hold | No: explicit hold |
| `ToastLaneHoldTests.AHoldThatIsNotFiniteAndAboveZeroUsesTheDefault` (:107) | a bad hold on a NEW line gets 2.2 s | No: a new line |
| `ToastLanesEditModeTests`, `AbilityFeedbackEditModeTests` | lane layout; the cast feedback lines (1 s hold, replaced in place by key) | No: cast lines carry the explicit 1 s |
| `FirstRunHintEditModeTests` | the first-run hint is a STICKY line (`MatchHud.Tutorial.cs:52,72` through `SetStickyNotice`), not a keyed push | No: `Find` skips sticky lines (`ToastLaneModel.cs:103`); read and confirmed |
| `AbilityMotionEditModeTests.TheWishTileHasOneScaleWriter` (:386) | the one test this packet amends (see below) | Amended |
