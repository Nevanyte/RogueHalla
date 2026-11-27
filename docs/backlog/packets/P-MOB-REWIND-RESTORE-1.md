# P-MOB-REWIND-RESTORE-1 — Restore same-tick mob rewind samples

## ID and outcome

Phase 2, item 13 (P-MOBS-1). Regional mob checkpoint admission accepts an equal timestamp in the first pair after a
displacement reset. The live 40 Hz balance profile can restore past the prior failure.

## Dependencies

Based on `0fac6aa75`, the integrated Saturday and alpha balance candidate after the allocation fix. The existing
[rework backlog](../AUGMENT_ELITE_REWORK.md) records this defect before the balance changes.

## Relevant legacy paths

[`packages/server/src/sim/movement.ts`](../../../packages/server/src/sim/movement.ts) appends rewind samples in production order.

## New paths

The focused regional rewind test module and this packet.

## Exclusive write scope

- `crates/rh-sim/src/systems/mob_checkpoint.rs`
- `crates/rh-sim/src/systems/mob_checkpoint/bleed_tests.rs` (shared PvE fixture visibility)
- `crates/rh-sim/src/systems/mob_checkpoint/rewind_tests.rs`
- `crates/rh-headless/tests/rework_at_25ms.rs` (historical comment only)
- This packet

## Contracts and references

C-TICK and C-NUM retain the existing writer, sample order and exact checkpoint state. D1 retains the source movement samples
and rewind meaning. D2 retains the existing checkpoint and state hash comparisons. D5, D6 and D9 are unaffected.

## Behaviour to preserve

A later duplicate, a positive gap shorter than the configured step, backward time, an empty history, out-of-range time and
invalid positions all remain invalid. Both samples retain the existing positional bounds, from -1,024 to the respective
arena dimension in world units plus 1,024. Runtime movement and the checkpoint, replay and wire formats stay the same.

## Acceptance checks

PR #94's Linux x86-64 producer failed the unchanged `rework_at_25ms` release test at seed 2, step 3773. The refused regional
NPC had two valid positions at 94,325 ms. The focused regression uses the displacement reset and real movement writer to
produce a pair with different positions, then restores that checkpoint to the same state hash. It also checks malformed
histories and zero-delta reader behaviour.

The unchanged eight-seed cohort restored 202 checkpoints with zero policy faults on Linux amd64. Supplemental seed 11 restored
all 26 checkpoints over 4,009 steps with zero policy faults.

## Validation commands

- `cargo test --release -p rh-sim --lib regional_mob_restore_admits_only_the_first_same_tick_rewind_pair`
- `cargo test --release -p rh-headless --test rework_at_25ms`

## Risk and suggested model tier

Small checkpoint-admission change. Use an independent reviewer with Rust and determinism experience to check the history
producer order and that later pairs still require a full step.

## Known traps

Abilities land before movement. Frost Wall can displace a regional mob, reset its rewind history and replan its existing
destination. Walking can then add a different position at the reset timestamp. Position lookup uses its existing clamp
at or before that first timestamp. For later times, any interpolated pair has a positive delta. Ice movement's velocity
calculation returns zero when the latest pair shares a timestamp.
