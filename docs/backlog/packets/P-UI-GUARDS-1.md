# P-UI-GUARDS-1: guarantees of the action row and the left edge that wave 4 loosened are tested again

- **Serves:** not in ORDER_OF_OPERATIONS. The UI rework lane's own audit of the test expectations that wave 4 amended or
  weakened (2026-11-23: 79 rows, each judged by a reader and again by a second reader against the tree). The six rows
  below were judged RESTORE: the product still holds the guarantee by the code and no test asserts it any more.
- **Implementer tier:** implementer-sonnet (nothing to implement while every guard is green: see Outcome item 4)
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` (`88bbbdbe7` or later); worktree named in the brief
- **D-rules touched:** D1 tests only, no behaviour change, so no legacy number to match and no `DEVIATIONS.md` row · D2 n/a ·
  D5/D6/D9 none

Lane rules for every agent of this lane: `.agent-runs/lane-ui-rework/specs/COMMON.md` (in the main checkout; read only).

## 1. ID and outcome

Wave 4 changed or weakened 79 expectations of existing tests. For six of them the old guarantee is still true of the
product by the code, but a changed set-up line, a narrowed loop or a trigger that no longer fires means no test states it
any more. A later change could break each unseen. When this lands, each guarantee is stated again as a STANDING GUARD:
green on the base, and proved by a mutation run (the guard goes red on its assertion when the protected product line is
changed, then the line is put back).

1. **Row 1** (`WishFaceTests.AWishTheServerWouldRefuseReadsCoolingAndShrinksTheWedge`, loosened by P-UI-CHARGE-TILES-1,
   commit 015c77e2b): the loop over charge counts 0, 1, 2 became 0 only. With no time left the quest-spell face (1 or 2
   charges) is Ready with Seconds 0 and Sweep 0, equal to the Wish's face; Sweep of a one-charge Ready face is asserted
   by no test.
2. **Row 2** (`AbilityRowEditModeTests.ADeliveredQuestSpellShowsNameChargesAndCountdown`, P-UI-CHARGE-TILES-1, commit
   687da2bf4): a cooling quest tile turns Ready on a later snapshot with NO new State delivery (name shown, strong frame,
   empty seconds, badge unchanged) is asserted by no test; the old step now delivers a new spell row.
3. **Row 3** (`AbilityMotionEditModeTests.TheWishTileHasOneScaleWriter`, P-UI-ABILITY-FEEDBACK-1, commit 054b3f83d): a
   press popping the Wish tile while the accept flash pops the same tile (the real sequence: press on a ready Wish, the
   accepting snapshot a few frames later) has no scale bound.
4. **Row 4** (same test, P-UI-ABILITY-FEEDBACK-1-fix, commit b8a8087f6): a press on an IDLE ready Wish tile (no pop
   running, ready wash on) and the accept-overlap case have no scale bound; the peak assertion of the final test is met by
   the ready pop alone.
5. **Row 5** (`LeftEdgeFoldEditModeTests.TheHeadersAreRealClickTargetsAndTheGainsOneIsOnlyAsWideAsItsText`, P-UI-LEFT-EDGE-1,
   commit ef554bfc8): 'the rest of the GAINED band takes no pointer' is now only 'the hit box rect is narrow'. No test asks
   whether any other graphic of the band takes the pointer.
6. **Row 6** (`AbilityRowNearbyNoGarbageEditModeTests.ANearbyActionAllocatesNothing`, P-UI-QUEST-ACTION-PROMPT-1, commit
   34ceabf91): no test measures 200 steady refresh frames while the launcher shows the count caption
   ('Nearby quest actions · n', two or more actions).

## 2. Dependencies

None beyond the base. `AllocationMeter` (`client-unity/UnityProject/Assets/Tests/Editor/AllocationMeter.cs`) must be on the
base for Row 6 (it is).

## 3. Relevant legacy paths

None: presentation only, no legacy behaviour is ported or changed.

Product code the guards protect (read on the base; cite these in a mutation):
- `client-unity/RoguehallaNet/Net/Ui/AbilityFace.cs:36-48` `AbilityFaces.Spell`: Cooling only when `left > 0 && charges <= 0` (:42);
  every Ready path returns Sweep 0, Seconds 0 (:47; the Wish at :78).
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.QuestRewards.cs:159` calls `RefreshQuestTiles(view, session.ServerTimeMs)`
  every frame; `:417` `RefreshQuestTiles` recomputes `AbilityFaces.Spell` from the held spell and shows it on the tile.
  `AbilityTile.cs` `Apply` redraws frame, name, seconds and badge from the face.
- `Scripts/UI/UiMotion.cs:79-93` `HudPulses.Play` (a rect already pulsing restarts, keeping its first origin) and `Tick`
  (the one writer of the pulsed rect's `localScale`, `1 + Pop * UiEase.Pop(t)`); `Pop = .08`, `Seconds = .15`.
- `Scripts/UI/MatchHud.ActionBar.cs:252-263` `TickWishTileMotion`: asks `Pulse(_wishTile)` on the flash edge and the ready edge;
  `MatchHud.Pulses.cs:58` `Pulse` -> `_pulses.Play`.
- `Scripts/UI/MatchHud.LeftEdge.cs:34-47`: the GAINED band's `HitBox` (`_gainsHit`) is the only raycast Image; the band is a bare
  RectTransform (`HudUi.cs` `At`), the heading label has raycastTarget false (`HudUi.cs` ~:150), the vitals plate Surface is drawn raycast
  false (`MatchHud.Legacy.cs` ~:97).
- `Scripts/UI/MatchHud.QuestRewards.cs:174` and `:182`: `NearbyActionsCaption() => _nearbyCaption`, built when the actions are
  bound (`:398`).

## 4. New paths

No new file. Tests are new methods in existing test files (see Oracle files).

## 5. Exclusive write scope

- `client-unity/RoguehallaNet.Tests/ChargeFaceTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/AbilityRowEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/AbilityMotionEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/LeftEdgeFoldEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/AbilityRowNearbyNoGarbageEditModeTests.cs`
- `docs/backlog/packets/P-UI-GUARDS-1.md`
- Nothing under `client-unity/UnityProject/Assets/Scripts/`, nothing under `client-unity/RoguehallaNet/Net/`. Not in scope:
  `HudTextScaleEditModeTests.cs`, `HudLegacyLayoutEditModeTests.cs` and every `CampKey*` test file.

All six tests are new methods committed by the oracle author, so the implementer has nothing of its own to write unless a
guard is found wrong (see Notes).

## 6. Contracts and references

- Presentation only: Unity holds no game rule (R-AUTH, ADR 0008). No wire, `crates/` or `packages/` change.
- `.agent-runs/lane-ui-rework/specs/COMMON.md` facts: tests are a separate assembly without `InternalsVisibleTo`; EditMode time
  does not advance (motion tests step the rig's `_local`); `AllocationMeter.AssertNone` is the only valid allocation test;
  `Graphic.Raycast` does no rectangle test (Row 5 reads `raycastTarget`, never calls `Raycast`).

## 7. Behaviour to preserve

No product file changes. No existing assertion is removed, widened or made conditional. Every pre-existing test stays as it is.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-GUARDS-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- The new tests are green on the base (they are guards; see Outcome item 4 of the spec: a guard that is red means the product
  regressed and is not to be weakened). Each guard has a recorded mutation that turns it red.

## 9. Validation commands

- dotnet: `dotnet test client-unity/RoguehallaNet.Tests --filter FullyQualifiedName~ChargeFaceTests`
- Unity: `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> AbilityRowEditModeTests,AbilityMotionEditModeTests,LeftEdgeFoldEditModeTests,AbilityRowNearbyNoGarbageEditModeTests`
  (never together with `TrainingConsoleIntegrationEditModeTests`: the allocation meter).

## 10. Risk and suggested model tier

Low. Tests only. implementer-sonnet is the default tier; the work is review of the oracle author's guards and a gate run.

## 11. Known traps

- Allocation tests use `AllocationMeter` exactly as the neighbouring tests of the class do.
- Motion tests step the rig's own clock `_local`; no sleeping, no wall clock.
- No `Assert.Multiple`; no `InternalsVisibleTo`.
- A mutation is undone with `git checkout -- <file>` before anything is committed; check `git status --short`.
- Assert what a player sees (a rect, a text, a size, an active flag), never a copy of the product's arithmetic.
- Row 5's graphics "exist on the tree today": a hit-box-only click target is a fact of the product, a graphic added to the
  band is what the guard catches.

## Existing tests that pin this code (read on the base, none changes)

- `WishFaceTests.AWishTheServerWouldRefuseReadsCoolingAndShrinksTheWedge` (`RoguehallaNet.Tests/WishFaceTests.cs:96-109`): Wish vs
  `AbilityFaces.Spell` for charges 0 only over 9 readyAt x 4 now (36 cells): equal Phase, Seconds, Sweep.
- `ChargeFaceTests` (`RoguehallaNet.Tests/ChargeFaceTests.cs`): `AChargeInHandReadsReadyWhileTheNextOneRecharges` (Ready, Sweep 0,
  Seconds 0 with a charge in hand and time left), `NoChargeStillCools`, `AMaximumOfOneChargeReadsAsBefore` (:63; (1, past) asserts
  Phase, Badge, Seconds 0, no Sweep), `ALanternWardRunsThroughItsRealStates`, `TheWishAndASpellDifferOnlyByHoldingACharge` (:124; for
  1 or 2 charges with no time left compares Phase only).
- `AbilityFaceTests` (`RoguehallaNet.Tests/AbilityFaceTests.cs`): face numbers of the Ready and Cooling faces, Seconds 0 / Sweep 0 at (2, past).
- `AbilityRowEditModeTests.ADeliveredQuestSpellShowsNameChargesAndCountdown` (`:315`): Charges 0 cooling look, then a NEW delivery
  (1 charge, ReadyAt 6000) at 7000 and the Ready look with badge 1.
- `AbilityMotionEditModeTests`: `TheWishTileHasOneScaleWriter` (`:386`; press 60 ms into the ready pop, scale <= 1 + Pop, wash on,
  peak above 1 + .9 Pop, pulses 1 before the press), `AnAcceptedCastPopsTheWishTileOnce` (counts pulses, no scale),
  `APressPopsAboutTheCentre` (a press pops the Wish tile), `TheReadyPulsePopsTheWishTileOnce`,
  `ReducedMotionKeepsTheWashAndTheWedgesAndDropsThePop`, `UiMotionEditModeTests.ARestartedPulseRestartsTheClockAndKeepsTheFirstOriginal`.
- `LeftEdgeFoldEditModeTests.TheHeadersAreRealClickTargetsAndTheGainsOneIsOnlyAsWideAsItsText` (`:414`): hit box and head toggle
  are real click targets; the hit box rect holds its centre and not a probe right of it.
- `AbilityRowNearbyNoGarbageEditModeTests`: `ANearbyActionAllocatesNothing` (`:176`; one action, caption 'Pick up WallBuster',
  200 refresh frames allocate nothing), `TheNearbyCaptionFollowsTheCount` (caption text only), and `QuestActionPromptEditModeTests`.

## Oracle files (read-only for implementers)

All six are new test methods in one `oracle:` commit; no `stub:` commit (nothing new is called) and no `oracle-amend:` commit
(no line of an existing test method changed).

- `client-unity/RoguehallaNet.Tests/ChargeFaceTests.cs` (new `ASpellWithNoTimeLeftReadsLikeTheWishAtAnyChargeCount`)
- `client-unity/UnityProject/Assets/Tests/Editor/AbilityRowEditModeTests.cs` (new `ACoolingQuestSpellTurnsReadyOnTheSnapshotClockAlone`)
- `client-unity/UnityProject/Assets/Tests/Editor/AbilityMotionEditModeTests.cs` (new `ThePressPopAndTheAcceptFlashPopShareOneScaleWriter`, `APressOnAnIdleReadyWishTilePopsOnceAndLeavesTheScaleAtOne`)
- `client-unity/UnityProject/Assets/Tests/Editor/LeftEdgeFoldEditModeTests.cs` (new `NothingInTheGainedBandTakesThePointerExceptTheHitBox`)
- `client-unity/UnityProject/Assets/Tests/Editor/AbilityRowNearbyNoGarbageEditModeTests.cs` (new `TwoNearbyActionsAllocateNothing`)
- `docs/backlog/packets/P-UI-GUARDS-1.md`

## Notes from the oracle author

**Every test is a standing guard, GREEN on the base** (as the spec says; the "red on a stub" rule does not apply). Red on the
base: none; no product regression was found, so there is no "Red on the base" entry. Run on the base `88bbbdbe7` plus the tests:
the four Unity classes (`AbilityRowEditModeTests`, `AbilityMotionEditModeTests`, `LeftEdgeFoldEditModeTests`,
`AbilityRowNearbyNoGarbageEditModeTests`) total 58, passed 58; dotnet `ChargeFaceTests`, `WishFaceTests`, `AbilityFaceTests` 27/27.
No scratch catch-and-collect run: the spec says it is not needed (no test stops early on a red assertion); the mutation runs below replace it.

**Each guard and its mutation** (scratch only, put back with `git checkout --`, `git status --short` showed only the test files after
each; the last status before the commit showed only the five test files):

| Row | Guard | Mutation | Failing message (on its assertion) |
|---|---|---|---|
| 1 | `ChargeFaceTests.ASpellWithNoTimeLeftReadsLikeTheWishAtAnyChargeCount` | `RoguehallaNet/Net/Ui/AbilityFace.cs:47` Ready path returns Sweep `charges == 1 ? .5f : 0f` | `sweep, charges 1 readyAt 0 now 1000`: expected 0, was 0.5 (`ALanternWardRunsThroughItsRealStates` step 2 also fails; the new guard is the one that names the no-time cells) |
| 2 | `AbilityRowEditModeTests.ACoolingQuestSpellTurnsReadyOnTheSnapshotClockAlone` | `MatchHud.QuestRewards.cs:~430` `AbilityFaces.Spell(..., Math.Min(serverTime, readyAt - 1d), total)` (a face that never sees time pass the ready point) | `ready: the name shows` expected True was False. ONLY this test fails (the delivered-spell test passes: it delivers 1 charge, Ready under the mutation too). A coarser mutation (cap the server time at `:159`) also fails two existing tests |
| 3 | `AbilityMotionEditModeTests.ThePressPopAndTheAcceptFlashPopShareOneScaleWriter` | `UiMotion.cs` `HudPulses`: a restarted pulse gets `Boost = Pop` and `Tick` uses `1 + (Pop + Boost) * ease` | `one pop, no stacked scale, at +70 ms`: expected <= 1.081, was 1.094 (`TheWishTileHasOneScaleWriter` also fails at +30 ms). A second mutation (`Play` adds a second entry on restart) fails only this test: `the press pop and the accept pop are one pulse at +40 ms`, expected 1 was 2 |
| 4 | `AbilityMotionEditModeTests.APressOnAnIdleReadyWishTilePopsOnceAndLeavesTheScaleAtOne` | `MatchHud.ActionBar.cs` `TickWishTileMotion` writes `_wishTile.localScale = 1 + .15 * max(flash, pulse)` (a second scale writer) | `the scale is exactly 1 after the pop`: expected 1, was 1.149 (the reduced-motion test fails too) |
| 5 | `LeftEdgeFoldEditModeTests.NothingInTheGainedBandTakesThePointerExceptTheHitBox` | (a) `MatchHud.LeftEdge.cs` `_gainsHeader.raycastTarget = true`; (b) an `Image` added on `_buildStrip` | (a) `no other graphic in the band takes the pointer: Label`; (b) `no graphic of the band or its ancestors up to the vitals panel takes the pointer: BuildStrip` |
| 6 | `AbilityRowNearbyNoGarbageEditModeTests.TwoNearbyActionsAllocateNothing` | `MatchHud.QuestRewards.cs:182` `NearbyActionsCaption()` builds the count text each call for two or more actions | `200 refresh frames with two nearby quest actions allocated managed memory ... 400 GC.Alloc samples` (the one-action test passes under it, so this guard is the only one on that state) |

**Deviations from the spec's RESTORE lines, and why**
- Row 1: a new test method instead of one added line in `AMaximumOfOneChargeReadsAsBefore`, so no existing test method changes
  (no `oracle-amend:`). It covers (1, past) AND the whole no-time-left grid for 0, 1, 2 charges, comparing Seconds and Sweep with the
  Wish's face and asserting both are 0.
- Row 2: a new test (not a step of the existing one) with Charges 0, ReadyAt 6000, delivered at 1000, then `Snap(12, 7000); Frame();`
  and no `Items`. As the spec says; the badge stays "0".
- Rows 3 and 4: the spec's trigger for Row 3 (press on a ready Wish with no earlier cooling) leaves the ready wash off at the press, so
  a "wash on" assertion cannot hold. The test cools the Wish, makes it ready (snapshot 55050), lets the ready pop finish (+300 ms), then
  presses; the accepting snapshot comes 30 ms later (server 55430, ReadyAt 65430). Wash on is asserted from the press to +150 ms.
  Row 4's idle case is the same set-up without the accepting snapshot. In the accept-overlap test the one-pulse assertion
  (`pulses.Count == 1` at every step) is an extra, and it is what catches a restart that adds a second pulse.
- Row 5: a new test method (sibling) instead of lines in the existing test. Ancestors are read with `GetComponents<Graphic>` on each
  transform from the band up to and including `VitalsPanel` (including the vitals panel itself): none takes the pointer on the base.
- Row 6: the two-action state is built as in `TheNearbyCaptionFollowsTheCount` (actions 9 with drops 37 and 38). The caption and
  `_nearbyQuestCount == 2` are asserted before `AllocationMeter.AssertNone(Refresh(), 200, ...)`. The measured path is the same cached
  field read, so the value is low, as the spec said; the mutation above shows it does catch a caption built on every call.

**Things to know**
- `unity-class.sh` printed no new `.meta` files; no new Unity script was added.
- The tests run in 14 to 37 s per class run.
