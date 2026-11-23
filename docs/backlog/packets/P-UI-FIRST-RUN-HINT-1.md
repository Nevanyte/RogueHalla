# P-UI-FIRST-RUN-HINT-1: the first-run hint is on screen for a new player's first 20 seconds of play

- **Serves:** not in the plan: owner direction 2026-11-22 (UI rework lane chat): "make sure all in-game notifications are
  thoughtfully placed". Seen in the lane's Web round 3a (2026-11-23): in two fresh sessions the hint was not on screen in the
  first seconds of play (`webround-3a/10-hud-spawn.png`: one second into the active match, clock 9:59, fresh profile, no pill
  above the action row). Since P-UI-CONTROLS-LINE-1 removed the standing controls line, the hint is the only on-screen
  reminder of the controls: a first-time player now gets none.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (stacked on `feature/unity-rework`; head 0363e05e9 when the spec
  was written, twelve wave 4 packets in). Gate with `--base lane/ui-rework/base-3`.
- **D-rules touched:** D1: presentation only. Legacy has no first-run hint (P-UI-CONTROLS-LINE-1's hand-off proposes the
  DEVIATIONS row for it); no new row, and the hand-off proposes one clause for that row: "shown for the first 20 seconds of
  play, not of joining". D2 n/a. D5/D6 none. D9: no rule, no key, no input; the words are unchanged.
- **Worktree:** `/Users/nevinmckinnon/Documents/Projects/RH-ui-x`

## 1. ID and outcome

The hint (`MatchHud.FirstRunHint`, "WASD or right-click move · Left-click attack · H help") is the sticky feedback line. A new
player (no profile, or fewer than two runs) sees it from the first accepted snapshot, and it stays on the feedback lane for
20 seconds of play (not of joining), then goes. Today it is wiped at every join and never put back, and its clock starts in the
lobby wait. When this packet lands:

1. **Play seconds.** Seconds are counted every frame (not only dirty ones) and only on a frame whose verdict is Count:
   `State == InMatch`, `Session.Phase == MatchPhase.Active` (wire byte 1; `Session.Phase` reads `Latest`, the object
   `HudLateUpdate` reads as `Snapshots.Newest`), and the lane is visible: `WorldHudVisible && _hud.gameObject.activeInHierarchy`
   (the expression of `TickNotices`, `MatchHud.Notices.cs:88`). So the Tab map, planning, the journal, a modal, the results
   card, the drawer, help, typing, a reconnect and a pending launch pause the count. A step is
   `MatchHud.NoticeDtOverride ?? Time.unscaledDeltaTime`; `Time.unscaledTime` is never read. The hint ends on the frame the
   total reaches 20 s (`>=`).
2. **Before play** (lobby, planning, no snapshot yet outside a pending launch, Reconnecting, lane hidden) the hint stands; the
   count neither advances nor resets. At dt .25 s (exact in float): 120 lobby frames (30 s) count 0; in play 79 frames stand
   (19.75 s) and the 80th ends it (20.00 s); 40 frames with the Tab map open count 0.
3. **Ends at once and for good:** practice (`_client.ConfirmedPractice`), `Phase == Ended`, `LastResult != null`,
   `State == Ended`. A later Active snapshot does not revive it; a match that ends inside the 20 s leaves no hint.
4. **The line survives resets.** While `_firstRunHint` is true and the launch is not pending: an empty `_feedback` becomes
   `FirstRunHint`; while `_feedback == FirstRunHint` the tick calls `SetStickyNotice(_feedback)` each frame (a no-op when the
   lane has the text; it re-puts it after `_noticesCleared`). Another non-empty `_feedback` is never replaced. While
   `LaunchPending` the tick does nothing (a restore would flash it in a practice join). `ResetTransientPresentation` itself
   does not change.
5. **Arming and words unchanged:** a null profile or fewer than two runs arms. `Bind` arms again and restarts the count from 0
   (a rebind arms it again). The text of `FirstRunHint` is unchanged.
6. **Ending** sets `_firstRunHint` false and, when `_feedback == FirstRunHint`, `_feedback = ""` and `SetStickyNotice("")` on
   that frame, not on the next dirty one. A `_feedback` that is not the hint is left alone.
7. A steady frame allocates nothing and writes no rect; no new object, colour or motion.

**Resume status** (`UI/MatchHud.Resume.cs:183`, `_feedback = status`) is the one other writer of `_feedback`: any non-empty
`ResumeStatus` change seen with the menu hidden and a session bound replaces the line (a save failure,
`Net/MatchClient.Resume.cs:216-224`, and the resume-join texts). An error outranks a reminder, so this stays: the status keeps
the line, the restore (item 4) never overwrites a non-empty `_feedback`, and the count runs on and ends without touching it.

**Lobby and planning.** The hint stands from the first accepted snapshot (Bind arms it, the launch's resets wipe it, the tick
restores it once the launch is not pending). In the lobby the lane is visible at bottom centre (`HudRegions.FeedbackBottom`
162, above the action row); the lobby draws only the roster (left) and the phase pill there, so a standing line covers nothing.
In planning the map panel is open (`_map.IsPlanning`, part of `BlocksKeyboardInputCore`), `WorldHudVisible` is false and the
lane is not drawn. No "show only from the active phase" rule is needed.

## 2. Dependencies

The lane base `lane/ui-rework/base-3` as it stands (`AllocationMeter` is on it:
`Assets/Tests/Editor/AllocationMeter.cs`). P-UI-ABILITY-FEEDBACK-1 (edits `MatchHud.Notices.cs`, `MatchHud.Pulses.cs`, the toast
lane model) and P-UI-HUD-GARBAGE-1 (edits `MatchHud.cs` `RefreshOwnerPresentation`, `ClearOwnerPresentation`,
`RefreshCollections`) build beside this packet; their hunks are apart from this packet's one deleted line, either merge order
works. P-UI-TEXT-AWAKE-1 and P-UI-PERF-WORST-FRAME-1 also build beside it; overlap with the write scope was not checked.

## 3. Relevant legacy paths

None: legacy has no first-run hint (presentation-only feature of the Unity client). The code that is wrong, read on the base
2026-11-23 (line numbers checked by the oracle author):

- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Tutorial.cs:35-59`: `FirstRunHint` (:35), `FirstRunHintSeconds` (:36),
  `_firstRunHint` (:37), `_firstRunHintUntil` (:38), `ArmFirstRunHint` (:40-46), `TickFirstRunHint` (:48-59). The tick sets
  `_firstRunHintUntil = Time.unscaledTime + 20` on its first call with `State == InMatch` and ends the hint when
  `unscaledTime` passes it; it only ever clears.
- `UI/MatchHud.cs:163`: `Bind` calls `ArmFirstRunHint()`. `:281`: `HudOwnerRefresh` calls `TickFirstRunHint()` and `:282` copies
  `_feedback` to the lane with `SetStickyNotice`. `HudLateUpdate` (:242) returns at `:252` (`if (!_dirty || _session == null)`)
  before `HudOwnerRefresh`, and at `:274` (`if (_client != null && _client.LaunchPending) return;`, true until the first
  accepted snapshot confirms the mode). So the 20 seconds begin at the first snapshot, in the lobby wait; planning and a held
  Tab map spend them, and in play, where snapshots come at 20 Hz and frames are faster, most frames count nothing.
- `UI/MatchHud.cs:209-210`: `ResetTransientPresentation` sets `_feedback = ""` and `SetStickyNotice("")`; `Net/MatchClient.cs:79`
  (`InitializeLayoutReview`, test only) and `JoinCore` (`:155` `Hud.Bind(session)`, `:157` `BindPresentationSession`; `:253`
  `ResetPresentationTimeline()`, `:268` `Hud.ResetTransientPresentation()`) call it; `Net/Session.cs:766` runs `TimelineReset` at
  the Welcome and `:626` at a source-clock cut (`sourceCut`, :595-597). `_firstRunHint` stays true and the tick only ever
  clears, so in a real join the hint is empty before the first frame. The existing hint tests bind through
  `InitializeLayoutReview`, which does no reset, so they never saw it.
- `UI/MatchHud.Notices.cs:19` `public static float? NoticeDtOverride`; `:29-35` `SetStickyNotice` (no-op when
  `text == _stickyNotice`); `:79` `_noticesCleared` (clears both models and `_stickyNotice`); `:84-96` `TickNotices`.
- `UI/MatchHud.Tutorial.cs:133` `TickTutorial` (called from `HudLateUpdate`, `MatchHud.cs:247`, every frame before the dirty
  check; its first line is `if (_tutView == null) return;`).
- `Net/MatchClient.cs:64-70`: `LaunchVerified`, `LaunchPending` (`_sessionTarget != null && Session != null && !LaunchVerified &&`
  state is Idle/Connecting/HelloSent/InMatch/Reconnecting), `ConfirmedPractice`.
- `RoguehallaNet/Net/Session.cs:127` `Phase` (`Latest == null ? null : (MatchPhase)Latest.Phase`), `:129` `TimelineEpoch`,
  `:132` `LastResult`, `:183` `TimelineReset`. `RoguehallaNet/Net/MatchPhase.cs:4`: Lobby 0, Active 1, Ended 2, Planning 3.

## 4. New paths

- `client-unity/RoguehallaNet/Net/Ui/FirstRunHintRules.cs` (namespace `Roguehalla.Net.Ui`), its synced copy
  `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/FirstRunHintRules.cs` and `.cs.meta`
  (`bash client-unity/sync-net.sh` through `in.sh`, then Unity writes the `.meta`). Created by the `stub:` commit; the
  implementer replaces its bodies.
- `client-unity/RoguehallaNet.Tests/FirstRunHintRulesTests.cs` (oracle).
- `client-unity/UnityProject/Assets/Tests/Editor/FirstRunHintEditModeTests.cs` and `.meta` (oracle, under 800 lines).

API of the rules file (the stub returns defaults: `Owed` false, `Judge` Stand, `Advance` returns `played`, `Spent` false):

```csharp
/// <summary>When the first-run hint is owed, counted and ended.</summary>
public enum FirstRunHintVerdict { Stand, Count, End }
public static class FirstRunHintRules
{
    public const float PlaySeconds = 20f;
    public static bool Owed(long? runs);                 // runs null or below 2
    public static FirstRunHintVerdict Judge(bool practice, bool matchOver, bool inMatch, MatchPhase? phase,
        bool laneVisible);
    public static float Advance(float played, float dt); // + dt when finite and above 0; at most PlaySeconds
    public static bool Spent(float played);              // played >= PlaySeconds
}
```
`Judge`: `End` when `practice || matchOver || phase == MatchPhase.Ended`; else `Count` when `inMatch && phase ==
MatchPhase.Active && laneVisible`; else `Stand`. `MatchPhase` is `Roguehalla.Net.MatchPhase`.

**Product change, `UI/MatchHud.Tutorial.cs`.** `_firstRunHintUntil` goes; `float _firstRunHintPlayed` comes; keep
`bool _firstRunHint` (tests set it by name). The const `FirstRunHintSeconds` goes too: `FirstRunHintRules.PlaySeconds` is the
one value. The comment above the fields is reworded to "shown from the first accepted snapshot and for the first 20 seconds
of play". `ArmFirstRunHint`: `_firstRunHint = FirstRunHintRules.Owed(profile?.Stats?.Runs)`, `_firstRunHintPlayed = 0f`, the
`_feedback` line as today. New `bool FirstRunHintLaneVisible => WorldHudVisible && _hud.gameObject.activeInHierarchy;`.
`TickFirstRunHint()` keeps its name and shape (a test builds a delegate to it) and becomes the per-frame tick: return if
`!_firstRunHint || _session == null || _client != null && _client.LaunchPending`; then `Judge(practice: _client != null &&
_client.ConfirmedPractice, matchOver: _session.LastResult != null || _session.State == SessionState.Ended, inMatch:
_session.State == SessionState.InMatch, phase: _session.Phase, laneVisible: FirstRunHintLaneVisible)`. `End`: item 6.
`Count`: `_firstRunHintPlayed = Advance(_firstRunHintPlayed, NoticeDtOverride ?? UnityEngine.Time.unscaledDeltaTime)` (the file
has no `using UnityEngine`), and `Spent` ends as `End`. Then item 4. The call moves: `TickTutorial` calls `TickFirstRunHint();`
as its first statement (before `if (_tutView == null) return;`) and the line `TickFirstRunHint();` in `HudOwnerRefresh`
(`MatchHud.cs:281`) is deleted, or a dirty frame would count twice. That one line is the only edit outside `Tutorial.cs`.

## 5. Exclusive write scope

- `client-unity/RoguehallaNet/Net/Ui/FirstRunHintRules.cs` and its synced copy and `.meta` (replace the stub bodies)
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Tutorial.cs` (fields, the const and its comment, `ArmFirstRunHint`,
  `TickFirstRunHint`, `FirstRunHintLaneVisible`, the first line of `TickTutorial`)
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.cs`: delete the line `TickFirstRunHint();` in `HudOwnerRefresh`,
  nothing else (572 lines now; `hud-seams.test.cjs` caps it under 600, a deleted line cannot break that)
- The oracle files below are read-only. No existing test is amended.
- Not in scope: `MatchHud.Notices.cs` (P-UI-ABILITY-FEEDBACK-1; `NoticeDtOverride` is read, not changed), `MatchHud.Resume.cs`,
  `Net/MatchClient*.cs`, `Session.cs`, `ToastLaneModel.cs`, `TrainingHudRig.cs`, `ControlsLineEditModeTests.cs`,
  `ToastLanesEditModeTests.cs`, `Tools/hud-seams.test.cjs`. `scripts/gate.sh` runs `hud-seams.test.cjs` in the full gate only:
  neither it nor `MatchHud` member names are changed here (`TickFirstRunHint`, `_firstRunHint`, `_feedback` keep their names).

## 6. Contracts and references

- Presentation only (AGENTS.md "Non-negotiables"; lane COMMON.md): no wire, no key, no rule. Unity holds no game rule: the
  verdict reads the server's Phase, State and result only.
- Motion and time: unscaled time; EditMode time does not advance, so tests drive time through `NoticeDtOverride`. Nothing that
  allocates in a steady frame (lane COMMON.md; `AllocationMeter.AssertNone`).
- `MatchHud` binds and unbinds sessions many times: no handler is left on an old session (this packet adds none).
- Readability gate: the new non-test C# file has a `/// <summary>` before its first type and at most 800 lines.
- `docs/OWNER_DECISIONS.md` searched for the subject (first-run hint, controls line): no decided row; owner direction
  2026-11-22 above governs.

## 7. Behaviour to preserve

- The text of `FirstRunHint` (`MatchHud.Tutorial.cs:35`) and the `internal const` name (read by `GetRawConstantValue` in
  `ControlsLineHintEditModeTests.HintConst`).
- `ResetTransientPresentation` (`MatchHud.cs:205-218`) is unchanged: the hint survives it by the tick's restore, not by an
  edit there.
- Arming: null profile or fewer than two runs. Practice never shows it. Resume status outranks it.
- The pill's placement, size and fade (`FeedbackLane`, `HudRegions.FeedbackPill`): no new object, colour or motion.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-FIRST-RUN-HINT-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- `dotnet test client-unity/RoguehallaNet.Tests --filter FirstRunHintRulesTests` green; Unity class `FirstRunHintEditModeTests`
  green (15 tests, 18 cases); the existing classes below stay green.
- `bash client-unity/sync-net.sh --check` clean (the copy is up to date).

### Existing tests that pin this code (read on the base 2026-11-23; none is amended)

| Test | What it asserts about the code this packet changes |
|---|---|
| `ControlsLineEditModeTests.cs` `ControlsLineHintEditModeTests` (:166; SetUp :195-224) | `SetUp` binds through `InitializeLayoutReview` (no reset), then asserts `_firstRunHint` is true and `_feedback == FirstRunHint` (:216-217): `Bind` must arm and set `_feedback`. `TheFirstRunHintNamesMoveAndAttack` (:317): after `Frames(2)` at dt .05 one active pill in `FeedbackLane` with the exact hint text, in feedback slot 0. `TheHintFitsItsPillAtScaleOneAndOnePointFive` (:333): the pill fits at scales 1 and 1.5. Both stay green: 5 frames at .05 s are .25 s of play, far under 20 s, and the rig's snapshot is Phase 1. `HintConst` reads the const `FirstRunHint` by reflection (:244-245): keep it a const field of `MatchHud`. |
| `ToastLanesEditModeTests.cs` (:21) | `ClearFirstRunHint` (:87) sets `_firstRunHint` false and `_feedback` empty after Bind (SetUp :69; again :398 after a rebind): with `_firstRunHint` false the tick must do nothing, so every lane test starts with an empty lane. Its comment says expiry needs `Time.unscaledTime`, which the new tick no longer reads. `NoticeDtOverride` is set at :54, :103, :331. |
| `HudLegacyLayoutEditModeTests.cs:230-233` | Calls `SetStickyNotice` directly and `TickNotices`, not the hint tick. Unaffected. |
| `HudTextScaleEditModeTests.cs:147` | Adds a notice label "Resume is unavailable ..." under `FeedbackLane/Toast0/Text`; builds the HUD from pieces without a session. Unaffected. |
| `PlayerFlowsEditModeTests.cs` (rig :41-56) | Same `InitializeLayoutReview` rig, no hint assertion; the hint stands in the lane there as before. Unaffected (reads `ServerNotices` history, not the lane). |
| `Tools/hud-seams.test.cjs` | Greps no hint member (checked: no `FirstRun`, `TickTutorial` or `HudOwnerRefresh` in it); caps `MatchHud.cs` under 600 lines. |

No existing test conflicts with the packet. The classes run green on the stub (see "Oracle run results").

## Oracle

The oracle author's `stub:` commit adds `FirstRunHintRules.cs` (defaults above) and its synced copy and `.meta`. The EditMode
tests name no new member (reflection only): no stub for them. No test reads `_firstRunHintPlayed`: on the base the field does
not exist and a reflection read would throw, not fail an assertion. The counting is read from when the pill goes.

dotnet `FirstRunHintRulesTests` (namespace `Roguehalla.Net.Tests`), all red on the stub:
1. `OwedFollowsTheRunCount`: null, 0, 1 owed; 2, 3 and `long.MaxValue` not.
2. `JudgeEndsInPracticeAndWhenTheMatchIsOver`: practice, matchOver or `Ended` give End whatever the rest is. This table is the
   only place `SessionState.Ended` is covered: no HUD-level test sets that state.
3. `JudgeStandsUntilPlayIsLive`: phase null, Lobby, Planning; not inMatch with Active (a Reconnecting session); lane hidden
   with Active: Stand; inMatch, Active, visible: Count.
4. `AdvanceCountsOnlyGoodSteps`: `Advance(0, .25f)` is .25; NaN, -1, 0, +Infinity leave it; `Advance(19.9f, 5f)` is 20; 80
   steps of .25 from 0 give 20 and `Spent`; `Spent(19.75f)` is false.

EditMode `FirstRunHintEditModeTests` (namespace `Roguehalla.Client.Tests`). Rig A copies `ControlsLineHintEditModeTests`
(`Transport`, `SetUp`, `TearDown`, `Frame`, `Active`, `TextOf`, `HintConst`): a real `MatchClient` on a fake transport via
`InitializeLayoutReview`; its frames are the HUD's `LateUpdate`. Time is only `MatchHud.NoticeDtOverride`: `Frame(dt)` sets it
(and sets `_dirty` true), every call passes .25f, `Seconds(s)` is `Frames(round(s / .25))`. `Quiet(dt)` is `Frame` without the
`_dirty` write. The set-up follows a real join: `InitializeLayoutReview` (Bind arms); `_hud.ResetTransientPresentation()` (what
`BindPresentationSession` calls); `_session.TimelineReset += reset` (removed in `TearDown`); Connect, Open, Welcome; `Snap(0)`.
`Snap(byte phase)` delivers a snapshot with that Phase, `Tick = ++_tick`, `ServerTimeOverrideMs = _ms += 50` (both must rise or
the session cuts the clock; `_ms` starts at 50, the Welcome's). `SnapCut()` sets `_ms = 20` and calls `Snap(1)`: a higher tick
with a lower server time, which `Session.cs:595` takes for a source cut. `SetUp` asserts `_firstRunHint` is true. `TearDown`
restores `NoticeDtOverride` (null) and `UiMotion.ReducedOverride`.
1. `TheHintIsOnTheLaneInTheLobby`
2. `LobbyAndPlanningSecondsAreNotCounted`
3. `TwentySecondsOfPlayAndNoMore`
4. `OnlyVisibleSecondsCount`
5. `AClockCutNeitherWipesNorRestartsTheHint` (`[TestCase(false)]` direct reset, `[TestCase(true)]` real cut)
6. `AMatchThatEndsInsideTwentySecondsLeavesNoHint`
7. `APhaseEndedSnapshotEndsItForGood`
8. `ARebindArmsItAgain`
9. `ProfileRunsDecideWhetherTheHintIsOwed` (async; runs 0, 1, 2)
10. `InPracticeTheHintNeverShows` (async, `TrainingHudRig`; standing guard)
11. `ASteadyTickAllocatesNothing` (standing guard)
12. `AfterARealJoinTheHintStandsThenGoes` (async, `TrainingHudRig`, arena)
13. `QuietFramesCountToo`
14. `NothingIsRestoredWhileTheLaunchIsPending`
15. `AFeedbackLineThatIsNotTheHintSurvives`

The exact steps and numbers of each are in the test file's comments; the rules are items 1-7 of section 1.

## Oracle files (read-only for implementers)

- `client-unity/RoguehallaNet.Tests/FirstRunHintRulesTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/FirstRunHintEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/FirstRunHintEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-FIRST-RUN-HINT-1.md`

(The `stub:` commit files, `FirstRunHintRules.cs`, its synced copy and `.meta`, are the implementer's to replace.)

## 9. Validation commands

- `in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter FirstRunHintRulesTests`
- `in.sh <worktree> bash client-unity/sync-net.sh` then `bash client-unity/sync-net.sh --check`
- `unity-class.sh <worktree> FirstRunHintEditModeTests,ControlsLineHintEditModeTests,ToastLanesEditModeTests`
- `in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-FIRST-RUN-HINT-1 --base lane/ui-rework/base-3`

## 10. Risk and suggested model tier

implementer-sonnet: one small state machine in the library and one tick in a partial. The risk is ordering (the
`LaunchPending` return first; practice, over and Ended judged before the lane) and the call moving to `TickTutorial`, which
runs every frame; the tests pin both.

## 11. Known traps

- **Statics.** Restore `MatchHud.NoticeDtOverride` and `UiMotion.ReducedOverride` in every `TearDown` (tests 10, 12 and the other
  `TrainingHudRig` tests in a `finally`). A `TrainingHudRig` frame before the override is set runs at the real, tiny
  `Time.unscaledDeltaTime`: the override 0f makes those frames count nothing (`Advance` ignores a step of 0).
- **The rig order is the test.** Without the two `ResetTransientPresentation` calls and the `TimelineReset` subscription the
  base shows the hint and the wipe is not exercised.
- **Name pins.** `_firstRunHint` (bool), `_feedback`, `_stickyNotice`, `TickFirstRunHint` and the const `FirstRunHint` are read
  or set by name in `ControlsLineEditModeTests` and `ToastLanesEditModeTests` (see the table).
- **Order of checks.** The `LaunchPending` return comes before everything; `ConfirmedPractice` is false while pending, so a
  practice join is never restored. Practice, match over and Ended are judged before the lane.
- **`Session.Phase` is nullable:** before the first snapshot it is null, which is Stand, never End.
- **Merge.** P-UI-ABILITY-FEEDBACK-1 adds a cast line that is dropped when the lane has no room and removed when the lane
  hides: the restore here relies on `_stickyNotice` mirroring the sticky line of the model, so a removal of that line must go
  through `SetStickyNotice` or `_noticesCleared`. The visible expression is copied: if `TickNotices` changes its rule, this one
  follows by hand.
- A packet branch stays linear. A new `.meta` is committed with its file.
- A backgrounded browser tab can hand the tick one large `dt` and spend the hint at once, as it ends every toast; accepted,
  no cap.

### Not established, for the hand-off

- **A busy first match.** With the hint standing, the feedback lane (two lines) has room for one more line, so with
  P-UI-ABILITY-FEEDBACK-1 a refused cast shows nothing while another refusal line is up. Both hand-offs say so. No change in code.
- **The blink.** At a clock cut inside the hint's time, or at a reconnect, `_noticesCleared` empties the lane model, so the hint
  blinks off for a frame and fades back in. Accepted; a change would reach `MatchHud.Notices.cs`, outside the write scope.
- **Resume status.** The status that replaces the hint stays as a never-ending sticky line (nothing times it out). That is a
  defect of its own, not this packet's: the hand-off records it. Test 15 pins only that this packet leaves such a line alone.
- **Wiring not pinned at HUD level.** `State == Ended` in `matchOver` and `State == InMatch` in `inMatch` (no HUD-level test
  drives an Ended or a Reconnecting session); the rules table covers `Judge`.

## Notes from the oracle author

(Filled with the oracle commit: the run results and the scratch run.)

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
