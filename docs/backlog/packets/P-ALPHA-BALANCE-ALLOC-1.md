# P-ALPHA-BALANCE-ALLOC-1 — Reserve scratch only for raid PvP

- **Serves:** Phase 2, item 11. Preserve the allocation budget on the integrated alpha balance release.
- **Base:** `c39bc1b77`, the integrated Saturday first-play and alpha balance candidate.
- **D-rules:** D1 retains the recorded outcomes and state hashes. D2 retains the deterministic allocation budget. D5, D6 and D9 are unaffected.

## Outcome

A simulation reserves the two raid PvP damage scratch buffers only when its configuration enables raid PvP. Construction, clone, checkpoint restore and actor-capacity growth follow that rule. Enabled buffers keep the same initial capacity and remain reusable between ticks.

## Failing oracle

PR #94's official perfproof compared `c39bc1b77` against `5a4fcee9c`. Relative to the baseline, DHAT counted 20 extra allocations in each of `sim_crowded_8`, `sim_duel_chase` and `sim_fists_duel`, 6 in `headless_seed1_8bots` and 16 in `headless_arena_8bots`. Each additional simulation accounted for two allocations from the unconditional raid scratch reservations. All instruction ratios met the 1.03 limit. Replay verification also reported the intentional content identity difference while matching all 3,000 steps and the final state hash.

`raid_pvp_scratch_is_reserved_only_for_enabled_rules` checks zero capacity for both disabled configurations and full capacity for enabled raid PvP, including clone, serialized checkpoint restore and actor-capacity growth.

## Exclusive write scope

- `crates/rh-sim/src/scratch.rs`
- `crates/rh-sim/src/sim.rs`
- `crates/rh-sim/src/checkpoint.rs`
- `crates/rh-sim/src/systems/ledger/camps_tests.rs`
- This packet

## Contracts and validation

Scratch stays outside the checkpoint, replay and state hash. The ledger, combat configuration, content tables, tick order and protocol stay as recorded in P-ALPHA-BALANCE-TARGETS-1. No gameplay rule changes.

Run the focused allocation regression, existing camp tests, allocation/hash/content suites and schedule parity. Confirm the zero-allocation budget against the official baseline. The historical replay's intentional content-status difference is independently declared as `verify_replay.contentHash`; its replay step count, final state hash and verification status remain exact.
