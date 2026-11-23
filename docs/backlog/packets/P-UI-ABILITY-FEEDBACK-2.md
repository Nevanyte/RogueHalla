# P-UI-ABILITY-FEEDBACK-2: arming the Wish aim while the Wish cannot be cast says why, at the press

- **Serves:** not in the plan: owner direction 2026-11-22 (UI rework lane: "ability and spell hub needs optimizing"). It closes a gap P-UI-ABILITY-FEEDBACK-1 left, seen in the lane's look round 3B (2026-11-23).
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (head `76ea9c0f9`, twenty-three wave 4 packets on integration 87)
- **D-rules touched:** D1 n/a (presentation only, no parity claim) · D2 n/a · D5/D6/D9 none (no rule in Unity: the line is drawn from delivered server facts; nothing is sent; no key changes)

Paths: Scripts = `client-unity/UnityProject/Assets/Scripts`, Net = `client-unity/RoguehallaNet`, Tests = `client-unity/UnityProject/Assets/Tests/Editor`.

## 1. ID and outcome

P-UI-ABILITY-FEEDBACK-1 promised that a refused cast shows ONE line above the action row for about a second ("Ready in N", "Casting", "Stunned", "Silenced") and pops no tile. The line is raised in exactly one place, `MatchHud.InputCue` (`Scripts/UI/MatchHud.Pulses.cs:20-43`, the push at `:27`), which runs from `Session.CueSent` and so only for a Cast command that was SENT.

With Quick cast off (the default) and a placed Wish (Frost's wall, the only one), R sends nothing: `MatchClient.ReadInput` hands the key to `WishAim.Key` (`Net/Net/Ui/WishAim.cs:42-57`, the arm branch `:50-54`), which opens the aim preview when the lifecycle allows a cast (`CanCast`, `:86-89`: in a match, a latest snapshot, phase 1, Me alive). Cooldown, a cast in progress, stun and silence are not read. A Frost player who presses R while the Wish cools gets an aim ring and no word; only the left click that follows sends the cast, and only then the line shows (`webround/round-3b/50-cast-refused.png`, `51-cast-refused-later.png`: ring up, no line). Evidence note: `.agent-runs/lane-ui-rework/understand/web3b-refused-cast-arm.txt` (its CHECK section governs).

Legacy (unreleased patch `78dd8bd4`, `packages/client/src/scenes/RaidScene.ts`, read with `git show 78dd8bd4:...`) denies at the press (`:1617-1621`, `castDenial` then `view.deny`) and opens no preview for a cast that cannot work (`:1623-1627` come after). Unity's port chose on purpose to keep no client-side denial (`docs/backlog/packets/P-UNITY-FROST-WALL-1.md` F5, `:97`). Whether R should refuse at the press as legacy does is a change of what the key does and is the owner's call; **this packet does not make that change.** It only makes the HUD say why, at the press, with the aim behaving as today ("Option B" of the note).

When this lands:

1. On the frame the Wish aim becomes armed (`MatchClient.WishAim.Aiming` goes from false to true), the HUD reads what a Wish cast would do now (`ReadCast(0)`, the same reading `InputCue` uses) and, when it has a refusal line, shows it exactly as `InputCue` does: `PushFeedbackLine(line, NoticeTone.Warn, CastReadiness.LineKey)`. No tile pops. Nothing is sent. The aim stays armed, as today.
2. Once per arming: frames in which the aim stays armed push nothing more; putting the aim away and arming again pushes again (when the reading still refuses).
3. A ready Wish, an empty Wish slot and Quick cast on (the aim is never armed there, and `InputCue` already says it) show nothing new: no second line, no doubled line.
4. The click that follows still sends what it sends today and `InputCue` still pushes its line; the line's key is the same, so there is one pill, restarted, never two.
5. A rebind (another session, the menu) forgets the remembered arm state, so a new session's first arming is read fresh. A HUD built without a client (some fixtures) does nothing and throws nothing.
6. No key is bound and nothing a key does changes. No word changes. `MatchClient`, `WishAim` and `Session` are not edited.

## 2. Dependencies

None beyond the base. Needs `AllocationMeter` only if the implementer adds an allocation test (not required: the edge check reads one bool).

## 3. Relevant legacy paths

- `packages/client/src/scenes/RaidScene.ts` at commit `78dd8bd4` (not in the working tree; `git show 78dd8bd4:packages/client/src/scenes/RaidScene.ts`): `castSlot` `:1592`; `:1598-1601` the key while its preview is up cancels it (a toggle, before any denial); `:1617-1621` `castDenial` then `view.deny` (the press is refused); `:1623-1627` only then the preview opens (`def.aim === "preview" && !confirm && !quickCast`). Verified by `git show` on 2026-11-23 (`:1615-1628` read).
- `packages/client/src/ui/abilitySlot.ts:140-160` `castDenial`; `:211` `DENY_MS` 1000; `:323-328` `view.deny` (per the note; the older tree under `packages/`).
- Unity differs on purpose (DEVIATION A of P-UI-ABILITY-FEEDBACK-1: the line is in the feedback lane, not on the slot), and this packet keeps the arm itself (F5 of P-UNITY-FROST-WALL-1). Owner decisions: `docs/OWNER_DECISIONS.md` has no row for the Wish key at the press; the open question stays with the owner.

## 4. New paths

- `Tests/AimFeedbackEditModeTests.cs` (+ `.meta`), the oracle.

## 5. Exclusive write scope

- `Scripts/UI/MatchHud.Pulses.cs`: one remembered bool, the edge check in `TickPulses`, the reset in `ClearPulses`.
  - `TickPulses` (`:60`) is called every HUD frame from `TickActionRow` (`Scripts/UI/MatchHud.ActionBar.cs:76`), which `HudLateUpdate` (`MatchHud.cs:242-249`) runs.
  - `ClearPulses` (`:62`) runs on every rebind: `Bind` (`MatchHud.cs:146`) calls `Unbind`, which calls `ClearPulses()` (`MatchHud.cs:190`) once a session was bound. If the first ever `Bind` skips it (`Unbind` returns early when `_session == null`, `:182`), the bool is false then anyway. Do not reset anywhere else; if that needs another file, stop and report.
  - `Tools/hud-seams.test.cjs:52-58` requires `InputCue`, `Pulse`, `TickPulses`, `ClearPulses`, `_pulses`, `PulseSeconds`, `PulsePop` to stay in this file and forbids `ShowcaseSettings` and tuple lists in it: run `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` through `in.sh`.
- The oracle files below (read-only for the implementer).
- Nothing else. No library file, so no sync and no dotnet test. Not `MatchClient.cs`, `WishAim.cs`, `Session.cs`, `MatchHud.cs`, `MatchHud.ActionBar.cs`.

## 6. Contracts and references

- C-INPUT, R-AUTH, D9: Unity holds no rule; the line reads delivered facts (`ReadCast`, `MatchHud.Pulses.cs:46-56`: newest snapshot's `ServerTimeMs`, `Me.Wish`, `Me.Effects`), the words stay in `CastReadiness` (`Net/Net/Ui/CastReadiness.cs:21-48`, hold `:23`).
- `docs/backlog/packets/P-UI-ABILITY-FEEDBACK-1.md` (the line, its lane, never late); `docs/backlog/packets/P-UNITY-FROST-WALL-1.md` F5 (the arm sends nothing and reads no cooldown: kept).
- `docs/agent/` lane rules (`.agent-runs/lane-ui-rework/specs/COMMON.md`): `MatchHud` binds many times; keep the `Session.CueSent` subscription and the press pulses.

## 7. Behaviour to preserve

- What R, the left click, right click, Esc and every other key do: unchanged. The arm still opens on a cooling Wish and stays open.
- `InputCue` unchanged (the sent-cast line and pop, the cast clip).
- The words and the hold of the line; the lane (`PushFeedbackLine`, `Scripts/UI/MatchHud.Notices.cs:56-59`, `ToastLaneModel.Push` with key and `dropIfLate`, never late, never queued).
- No per-frame allocation: the edge check reads one bool; the string is built once per arming.
- Quick cast on: the aim is never armed; the line comes once, through the sent cast.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-ABILITY-FEEDBACK-2 --base lane/ui-rework/base-3` prints `GATE PASS`.
- Every test of `AimFeedbackEditModeTests` green; the existing classes below green unedited; `hud-seams.test.cjs` green.

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/AimFeedbackEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/AimFeedbackEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-ABILITY-FEEDBACK-2.md`

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> AimFeedbackEditModeTests`
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> AbilityFeedbackEditModeTests,AbilityMotionEditModeTests,AbilityRowEditModeTests,ChargeTilesEditModeTests,StormInkOwnerWishInputEditModeTests,FrostWallAimEscapeEditModeTests,UiInputRoutingEditModeTests,ToastLanesEditModeTests,FirstRunHintEditModeTests`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> node --test client-unity/UnityProject/Tools/hud-seams.test.cjs`

## 10. Risk and suggested model tier

Sonnet: a few lines in one HUD partial. Hard parts: the edge is the AIM's, not the reading's (no push when the reading turns from ready to refused while armed); `_client` and `_client.WishAim` can be null in fixtures; the reset must be where the HUD rebinds.

## 11. Known traps

- `HudPulses.Play` restarts an entry for a rect already pulsing: a "no pop" assertion is made after `ClearPulses` (the lane's rule) and as a delta. A test that calls `ClearPulses` between the arm and the frame would also reset the remembered bool: the oracle never does.
- The reading uses the newest snapshot's server time (`ReadCast`), never the HUD clock.
- The remembered state is the AIM's: push on the false-to-true edge of `Aiming` only. Do not push when the reading turns from ready to refused while the aim stays armed.
- `_client` and `_client.WishAim` can be null in fixtures: check both.
- No `Assert.Multiple`; no `InternalsVisibleTo`; EditMode time does not advance (the lane runs on `MatchHud.NoticeDtOverride`).
- `Aiming` is public read (`MatchClient.cs:359`, `WishAim.cs:31`): the HUD may read it; do not add members to `MatchClient` or `WishAim`.

## Existing tests that pin this code (all stay green, unedited; verified by reading)

- `Tests/AbilityFeedbackEditModeTests.cs`: the sent-cast line and no pop (`ADeniedPressSaysWhyAndPopsNothing` `:233`, `ASecondPressReplacesTheLine` `:258`, `AReadyPressPopsAndSpeaksNothing` `:278`, `AnEmptySlotNeitherPopsNorSpeaks` `:295`, the 24-case matrix `:374`, `ARebindForgetsTheLine` `:455`, `ASteadyFrameWithTheLineAllocatesNothing` `:466`, `TheLineLastsOneSecondAndNeverShowsLate` `:485`). Every press goes through `Session.CollectSpellInput` + `FlushInput`; none arms an aim, so none sees the new edge.
- `Tests/AbilityMotionEditModeTests.cs`, `AbilityRowEditModeTests.cs`, `ChargeTilesEditModeTests.cs`: call `InputCue` (by reflection or `CollectSpellInput`), pulses, tiles. Unchanged by the edge.
- `Tests/StormInkOwnerWishInputEditModeTests.cs:16-45`: only Frost's R arms in a Wall room (calls `WishAim.Key` directly, no Wish in the snapshot, no HUD frame).
- `Tests/FrostWallAimEscapeEditModeTests.cs:36-65`: Esc closes the aim; opens it through `WishAim.Key` directly.
- `Tests/UiInputRoutingEditModeTests.cs:310-320` `QuestAndWishKeysQueueTheirSerializedCastSlot`: real keys; R of a non-Frost faction casts at once.
- `Tests/ToastLanesEditModeTests.cs`, `Tests/FirstRunHintEditModeTests.cs`: the lane's behaviour (a sticky first-run hint counts as a line; the lane holds 2).
- `RoguehallaNet.Tests/WishAimTests.cs` (`:73-81`, `:109-125`, `:148-159`): the arm, toggle and lifecycle gates in the library; untouched (no library change).
- `Tools/hud-seams.test.cjs:52-58`: names that stay in `MatchHud.Pulses.cs`.

No conflict: none of them asserts that arming shows no line.

## Notes from the oracle author

- **No stub commit:** the tests use only members that exist and are reachable (`MatchClient.WishAim` public, `MakeWishFrame` and the HUD fields by reflection, as the existing classes do). The stub is empty by design: the base is the stub.
- **Rig:** `AimFeedbackEditModeTests` copies the whole-HUD rig of `AbilityFeedbackEditModeTests` (about 40 lines) and adds a Frost seat (`WireSelf.Faction = Frost`) and the wall's terms 23-29 in the Welcome (`Hud = new WireHudContent { Terms = ... }`). The aim is armed through the real `WishAim.Key` with the frame `MatchClient.MakeWishFrame` builds (by reflection); `QuickCast` is set on that frame (the real key sets it from `UiDisplayPreferences.QuickCastStored`, `MatchClient.cs:470`), so no PlayerPrefs are touched. The client's `Update` is not run (the component is disabled), so nothing puts the aim away between frames except the test.
- **Test 1** is `ArmingTheAimWhileTheWishCoolsSaysWhen`; **test 2** `EachRefusalHasItsLine` (four `TestCase`s); **test 3** `TheLineComesOncePerArming`; **test 4** `TheClickAfterwardsShowsOnePill` (the click is `WishAim.Click` then `Session.FlushInput`, the real path of a left click while armed, not `Press(0)`). **Guards 5/6** `ArmingAReadyAimSaysNothing`, `AnEmptyWishSaysNothing`, `QuickCastOnSaysItOnceThroughTheSentCast`, `ARebindForgetsTheArm`, `AHudWithoutAClientTicksWithoutThrowing` (`_client` nulled by `FieldInfo`).
- **Additions to the spec's list:**
  - `ARefusalThatBeginsWhileTheAimIsArmedSaysNothing` (green guard): the Wish is ready at the arming and starts cooling under the armed aim; no line (the edge is the aim's).
  - `ARebindReadsTheFirstArmingOfTheNewSessionFresh` (RED): the spec's guard `ARebindForgetsTheArm` (a ready second Wish, aim still armed) cannot tell a reset from no reset, since both give no line. This test rebinds to a session whose Wish is cooling with the aim still armed (the client's `WishAim` is not touched by a rebind) and expects the line on the first frame: it passes only if `ClearPulses` forgot the remembered arm. In the spec's guard the arming's line is not asserted (it would be red on the base); it waits out the line and then rebinds.
- **Real-key test left out:** an R press through `MatchClient.ReadInput` needs the Keyboard/Mouse/InputSettings clone of `UiInputRoutingEditModeTests.SetUpFixture` (`:84-140`, private to that class, about 120 lines with camera and cursor set-up). Copying it into this class would double the rig for one line of proof. Tests 1-4 pin the HUD half through the real `WishAim.Key` and `MakeWishFrame`; the key half (`MatchClient.cs:466-471` calls `_wishAim.Key`) is the existing aim tests' and `QuestAndWishKeysQueueTheirSerializedCastSlot`'s.
- **Scratch run (catch-and-collect):** one run of a copy of the class in which every `Assert.That` was swallowed into a list (the copy and its `.meta` deleted, not committed; the run preceded the one edit that removed the arming-line assertion from the `ARebindForgetsTheArm` guard). Every entry was an assertion about the missing behaviour ("arming shows the line": 0 pills against the expected line; two entries in the per-arming, fresh-rebind and `cooling`/other `EachRefusalHasItsLine` cases); no other exception: every later line of every test ran (the nulled-client frames, the click, the cues count, the tone loop, the rebind deliveries). The tone loop in test 1 iterates over no pill on the base; on the implementation it iterates over the one.
- **Red on the base (8 of 14), each on its first assertion about the line:** `ArmingTheAimWhileTheWishCoolsSaysWhen` ("arming a cooling Wish shows its reason once": 0 pills, expected "Ready in 3"); `EachRefusalHasItsLine` cooling, casting, stunned, silenced ("<state>: arming shows the function's line": 0 pills); `TheLineComesOncePerArming` ("the arming shows the line"; its second arming fails the same way); `TheClickAfterwardsShowsOnePill` ("set-up: the arming shows the line"); `ARebindReadsTheFirstArmingOfTheNewSessionFresh` ("the first arming shows its line"). Green on the base and after (6): `ArmingAReadyAimSaysNothing`, `AnEmptyWishSaysNothing`, `QuickCastOnSaysItOnceThroughTheSentCast`, `ARebindForgetsTheArm`, `AHudWithoutAClientTicksWithoutThrowing`, `ARefusalThatBeginsWhileTheAimIsArmedSaysNothing`.
- **Existing classes run on the base (all green, unedited):** `AbilityFeedbackEditModeTests`, `AbilityMotionEditModeTests`, `AbilityRowEditModeTests`, `ChargeTilesEditModeTests`, `StormInkOwnerWishInputEditModeTests`, `FrostWallAimEscapeEditModeTests`, `UiInputRoutingEditModeTests`, `ToastLanesEditModeTests`, `FirstRunHintEditModeTests` (104 + 59 tests, no failure). `hud-seams.test.cjs` not run by the oracle author (no source changed); the implementer runs it.
- **Implementer note:** the lane advances at `.05` s per tick and two ticks per `Frame()`; the tests wait `Lane(2f)` for a line (hold 1 s plus fades) to run out.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
