# P-TUTORIAL-2-fix: Step 1 ring never contains the spawn

- **Serves:** not in the plan: owner approval 2026-11-15 (owner asked for this fix after the live playthrough).
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** feature/unity-rework (86dd4adc), stacked on `claude/serene-pasteur-m3kd4y` @ f36eb0b9 (P-TUTORIAL-1..3 combined)
- **D-rules touched:** D1 n/a · D2 n/a · D5 owner-approved 2026-11-15 · D6 none · D9 kept (presentation only)

## 1. ID and outcome
Step 1 of the tutorial ("walk into the glowing ring") must need real movement. A headless playthrough on a live Training
server showed it completing on the first observation: the Training rack is 96 u from spawn, `SetRing` places the ring at
`min(RingDistance, rackDistance * 0.5)` = 48 u = `RingRadius`, and the inclusive `<=` check passes at spawn. When this
packet lands, the ring centre is always `RingDistance` (120 u) from the spawn along the rack direction (or +x without a
rack), so the spawn is never inside the 48 u ring, whatever the rack distance. The P-TUTORIAL-2 review note (ring
distance halving) that the coordinator accepted is hereby reversed.

## 2. Dependencies
agent/sonnet/P-TUTORIAL-2 (merged into `claude/serene-pasteur-m3kd4y`).

## 3. Relevant legacy paths
- none (legacy has no tutorial).

## 4. New paths
- none.

## 5. Exclusive write scope
- `client-unity/RoguehallaNet/Net/Ui/TutorialFlow.cs` (`SetRing`, around L133-141) and its synced Unity copy (via `bash client-unity/sync-net.sh`).

## 6. Contracts and references
- Unity/library holds no game rules; presentation thresholds only.

## 7. Behaviour to preserve
- All other tutorial steps; `MoveNeedsTheOwnPositionInsideTheRingAimedAtTheRack` (rack far away → ring at 120 u).

## 8. Acceptance checks
- `scripts/gate.sh --quick --base claude/serene-pasteur-m3kd4y --oracle packet/P-TUTORIAL-2-fix` passes.
- Live: a headless Training playthrough shows step 1 needing movement.

## Oracle files (read-only for implementers)
- `client-unity/RoguehallaNet.Tests/TutorialFlowRingFixTests.cs`

## 9. Validation commands
- `dotnet test client-unity/RoguehallaNet.Tests`; `bash client-unity/sync-net.sh --check`

## 10. Risk and suggested model tier
Trivial one-line change; Sonnet.

## 11. Known traps
- The ring check is inclusive (`<=`); the fix must keep the ring centre strictly more than `RingRadius` from spawn.

## Notes from previous attempts

## Oracle evidence
- Oracle: `client-unity/RoguehallaNet.Tests/TutorialFlowRingFixTests.cs` (3 tests; `TutorialFlowTests.cs` untouched).
  - `TrainingGeometryFirstObservationDoesNotAdvanceAndRingIsClearOfSpawn`: spawn (784,144), rack (880,144); no advance; ring centre (904,144), 120 u from spawn (>= 2 x RingRadius).
  - `SweepOfRackDistancesAndDirectionsNeverAdvancesOnFirstObservation`: 9 directions x rack 1..400 u (step 3), over 1000 cases; no advance, centre exactly RingDistance from spawn, radius 48.
  - `WalkingIntoTheRingStillAdvancesAfterAFirstNonAdvancingObservation`: rack 5/96/300 u; first obs no, x+30 no, x+100 advances to Pickup.
- Run 2026-11-15 `DOTNET_ROOT=/opt/dotnet dotnet test client-unity/RoguehallaNet.Tests`: build OK; Failed 3, Passed 2168, Total 2171; the three red are exactly the new tests, on assertions.
- Existing pin kept green: `TutorialFlowTests.MoveNeedsTheOwnPositionInsideTheRingAimedAtTheRack` (rack 400 u away, ring at 120 u).
- Legacy quirk: the rack-at-<=1 u case already falls back to +x; the sweep includes rack 1 u.
