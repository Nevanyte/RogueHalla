# Hand-off: P-UI-INPUT-CUES-1 (2026-11-22)

PACKET: P-UI-INPUT-CUES-1: casts, pickups, swaps and potions answer the key on the frame it is pressed (presentation only)
BRANCH: agent/sonnet/P-UI-INPUT-CUES-1 (b78e4b01d, plus this hand-off commit)
ORACLE: packet/P-UI-INPUT-CUES-1 (80e4ba9c4; brief 78e5d3bc0, stub 82396fb51)
BASE: feature/unity-rework 83edb687f, **stacked** on agent/sonnet/P-NET-ARRIVAL-CLOCK-1 (747e75902, handed over, not yet
merged), at the coordinator's request. Merging this branch brings P-NET-ARRIVAL-CLOCK-1 too. Both touch `Session.cs`
in different places. `git merge-tree` is clean against the integration branch and against agent/sonnet/P-NET-STEER-1.
SERVES: not in ORDER_OF_OPERATIONS: owner direction 2026-11-22 (the Sonnet audit he asked for, finding 5 in
`docs/backlog/BALANCE_LAB_PATCH.md`), run by the client-input lane at the coordinator's request.
LANE: client-input lane (worktree claims under session `lane-client-input`).

GATE:
- Final: `scripts/gate.sh --quick --base agent/sonnet/P-NET-ARRIVAL-CLOCK-1 --oracle packet/P-UI-INPUT-CUES-1` on
  b78e4b01d → GATE PASS (quick); Unity EditMode 2000/2000; `dotnet test client-unity/RoguehallaNet.Tests` 2356/2356;
  `sync-net.sh --check` clean.
- History:
  - The implementer's gate passed on attempt 2/2, before stacking. Attempt 1 hit NullReferenceExceptions in five
    `UiInputRoutingEditModeTests`: an ArenaView without Awake. It was fixed with `_actors?.`.
  - The implementer had also committed two Unity assets that the EditMode run rewrites: `ColiseumTorchArt.asset` and
    `ColiseumWeaponRackArt.asset` (a trailing space). The lane removed them from the commit.
  - The lane re-gated on the stacked base (PASS), applied the review-note fix, and gated again (PASS, above).
- No full gate run by the lane; the coordinator's merge gate is the one that counts.

ORACLE (`client-unity/RoguehallaNet.Tests/InputCueTests.cs`):
- `EachCueableIntentRaisesOneCueOnTheFrameItLeaves`: 0 cues before the change → 6 after (Wish, slots 1 and 2, Swap,
  Pickup, potion; none for Move, Aim or Attack), raised after the frame is sent.
- `NoCueForWhatNeverLeaves`: green before and after.
- Added with the note fix (not an oracle file): `InputCueFaultTests.AThrowingCueSubscriberCostsNoFrameAndNoOrderBookkeeping`.
  It is red on the pre-fix code, which sends 1 of 2 frames, and green after.

CHANGED: `client-unity/RoguehallaNet/Net/Session.cs` (`CueSent`, raised in `FlushInput` after each frame is sent and its
prediction bookkeeping is done; a subscriber's exception is rethrown after the whole flush) and its synced copy;
`UnityProject/Assets/Scripts/UI/MatchHud.cs` (subscribe on bind, unsubscribe and clear pulses on unbind, the pulses);
`UI/MatchHud.QuestRewards.cs` (keeps the two quest-spell tile rects); `View/ArenaView.Fx.cs` (`LocalCastCue`);
`Presentation/SpriteActorPresenter.CombatCues.cs` (own-actor cast clip); new `RoguehallaNet.Tests/InputCueFaultTests.cs`.
`MatchClient.cs` untouched.

WHAT CHANGES FOR A PLAYER: the moment a cast (R, Shift, Q), potion (E), weapon swap (Space or a slot) or pickup (F) leaves,
the matching HUD element gives a ~150 ms scale pop (up to 10%, none under Reduced Motion): the Wish tile, the quest-spell
tile, the potion button, the weapon slot (or the whole bar for a toggle), the Pickup button. A cast also starts the own
character's existing Cast clip, capped at 0.6 s; the server's Wish cue restarts it from the real start. Nothing shows an
outcome early: no swapped weapon, potion count, health, item, cooldown sweep or target effect. Nothing on the wire changes.

D-RULES: D1 n/a (presentation) · D2 n/a · D5/D6 none · D9 unchanged (no outcome predicted; ADR 0008 "animate").

REVIEW (reviewer, Sonnet): LAND WITH NOTES, one note, fixed on this branch before hand-off.
- Note 1: `CueSent` was raised before the frame's `Motion` bookkeeping. A throwing HUD handler would skip
  `MovementOrderSent` for that frame and lose the rest of the flush.
- Fix b78e4b01d, by the lane (a few lines, same gate): cues come after the bookkeeping; the first exception is captured
  and rethrown after the whole flush; a test pins it.
- The fix was re-reviewed: LAND, no notes.
Judgement call for whoever looks at a real render: a cast the server refuses (no stock), and the quest spells (which have
no server cast cue to replace it), still play the character's Cast clip for up to 0.6 s. The reviewer judged this
acceptable as "animate"; it is the weakest point against "nothing that looks like success".
Minor, not notes:
- The local cue always plays the Cast clip. The server's cue plays Attack for Blaze Step and Block for Last Stand, so for
  those two Wishes the animation jumps when the server cue arrives. The clip could be chosen from the HUD's known Wish.
- A local cast clip outranks Block and Hit in clip selection (`SpriteActorPresenter.cs:430`) and holds off HauntedFists
  while it plays (`HauntedFists.cs:33`). Spamming a refused cast can mask the own hit or block animation for up to 0.4 s.
- `InputCue` does not guard an unbuilt `_slots` or `_questSlotTiles` array; each path null-checks the rect.
- A pulse on an already-pulsing element restarts it mid-scale (a small step).
- No EditMode test covers the pulses or unbind; the oracle covers the library event only.
- Fix review: cue exceptions after the first are swallowed (a comment would say so); the fault test's comment repeats
  "16 commands per frame" (`Wire.MaxCommandsPerInput`).

PINNED VALUES TO RE-TAKE: none.
DEVIATIONS: none.
SHARED RECORDS: none beyond the coordinator's own backlog row and MIGRATION checkpoint entry.
NOT ESTABLISHED: nothing was looked at in a real build or browser: how the pulses and the cast clip look, and the cue's
timing against a real server cast, are unverified. EditMode proves the Unity code compiles and the existing suite holds.
RISKS: each EditMode run rewrites the two StormInk assets above; restore them, never commit them.
