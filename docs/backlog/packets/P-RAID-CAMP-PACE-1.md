# P-RAID-CAMP-PACE-1 — Opening and developed camp fights

## 1. Outcome and plan item

Phase 2, the playable raid loop and measured balance. The owner requested that walking for a particular camp buff remain worthwhile after minute ten, while the opening five minutes retain their pace. A shared live combat setting isolates the intentional balance change from the legacy oracle. The paired PvP packet owns player-to-player damage.

## 2. Dependencies

The 0426504ee restored alpha, themed camp payouts, the real damage ledger, the 20/40 Hz clock, and the balance lab.

## 3. Legacy evidence

The tactical-farming change 31256fcd0 raised normal kill payouts from one to three stacks and doubled normal mob HP. The three-stack schedule remains in `packages/shared/src/mobStats.ts` lines 9–12. The preceding restoration did not change either value. Existing normal-camp defenses do not grow as a raid advances.

## 4. New paths

This packet. Existing gameplay and lab entry points receive the implementation and regression tests.

## 5. Exclusive write scope

Normal camp defense, config and content identity under rh-sim, explicit config literals across crates, the checkpoint hash-coverage scenario, the live runtime characterization, normal-clear timing in the headless balance lab, progression and deviation documentation, the Unity Help Guide camp description, the release-only headless test in section 9, and this packet. The paired PvP packet owns the shared ledger call site. Refresh all six content hash copies only with `scripts/regolden-content.sh`, including its generated ability tape and hitbox fixture. The binary protocol and captured source inputs stay intact.

## 6. Contracts

C-TICK uses milliseconds of active raid time. C-RNG gains no draw. C-INPUT and C-OBS keep combat decisions in the server. A legacy config omits the pace field, preserving its original checkpoint bytes and statics digest. An explicit null from an interim checkpoint also restores the legacy setting. The live profile uses its own tag. Pacing numbers are registered content.

## 7. Behavior to preserve

Active minutes zero through five, the three permanent stacks, their themed type, GV, mob outgoing damage, elite camps and their escorts, true damage, practice actors and the original replay configuration.

## 8. Acceptance

At the opening, repeated equal player strikes kill the same normal mob in the same number of blows as before. A physical strike just large enough to kill that full-health mob in the legacy configuration takes multiple hits by minute ten. Ordinary landed damage faces greater resistance at minute twenty. True damage bypasses it. Elite camp defenders retain the original defenses. A normal clear's actual elapsed duration belongs to exactly one five-minute interval.

## 9. Validation

Run the `normal_camp_raid_pacing_preserves` rh-sim unit test, the `augment_rules` statics-identity tests, `balance_lab_time`, and paired eight-seat RaidBot matches. The release-only `raid_camp_clear` test (rh-headless, feature `elite-bench`) plays 144 input-driven camp fights across four raid ages. Opening pairs agree exactly. Fights that start at minute five can span the beginning of the ramp. Compare the same full-raid seeds before and after. The checkpoint perturbation suite covers both live farming options and requires that switching either to its legacy value changes the restored state hash. Run the full gate with PostgreSQL and Unity. The pre-change test failed on the minute-ten full-health kill.

## 10. Risk and tier

Implementation and review at gameplay simulation tier. RaidBot sample outcomes depend on travel, interrupted pulls, builds and surviving seats. The timings measure active play and distinguish controlled blows from open-world camp elapsed time.

## 11. Known traps

Measure from raid start rather than lobby creation. Use the complete camp roster to distinguish elite escorts from ordinary camps. Prevent training or Plague companions from acquiring raid defenses. The public farm bonus describes a credited kill and remains truthful throughout the match.
