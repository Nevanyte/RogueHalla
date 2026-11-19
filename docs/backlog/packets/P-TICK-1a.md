# P-TICK-1a: the sim's step length is a setting (50 or 25 ms) in the core, save, restore and hash

- **Serves:** not in the plan: owner approval 2026-11-19 (ADR 0029); supports ORDER_OF_OPERATIONS phase 3, item 16a
- **Implementer tier:** implementer-opus (`rh-sim` state, tick, save/hash)
- **Reviewer:** reviewer-opus
- **Integration base:** feature/unity-rework
- **D-rules touched:** D1 byte-exact at 50 ms (every parity fixture and state hash unchanged) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome
A sim can be built with `SimConfig::tick_ms` 50 (default) or 25. Sim time advances by the sim's own value each step, the
value is saved, restored and folded into the state hash, and a sim reports it (`Sim::tick_ms()`, and through the world
for systems). History rings that are sized from the tick (`state.rs` `history_cap`, the vision trail in `item_view.rs`)
are sized for the shortest allowed step. Systems still read the old constant after this packet; P-TICK-1b moves them.
With the default, nothing observable changes.

## 2. Dependencies
- P-TICK-0 (ADR 0029 accepted)

## 3. Relevant legacy paths
- `packages/shared/src/constants.ts:11-13`: the legacy step (`TICK_RATE = 20`, `TICK_MS = 1000 / TICK_RATE`, `PATCH_MS`).
- `packages/server/src/sim/step.ts:302-305`: legacy `step(world, dtMs)` already takes the step length as a parameter:
  `s.serverTime += dtMs; s.tick++` (time first, then the counter; Rust `sim.rs:525-529` keeps that order).
- `packages/server/src/sim/movement.ts:101-105`: `recordHistory` trims with `cutoff = now - MAX_REWIND_MS - TICK_MS`
  (Rust `state.rs:136`); at 25 ms the cutoff reads the sim's step.
- `packages/server/src/rooms/RaidRoom.ts:323,395,626`: the room paces with the constant `TICK_MS` (server side, P-TICK-3).

No RNG draw, same-tick competition or death case is involved: the packet changes only the step length and where it is
recorded. The step adds `tick_ms` before the tick counter moves, every step, in every phase (lobby, active, ended).

## 3a. Established facts (oracle author, 2026-11-19)
- Today: `constants.rs:62-63` (`TICK_RATE: u32 = 20`, `TICK_MS: f64 = 1000.0 / TICK_RATE as f64`), `sim.rs:525`
  (`self.w.server_time += TICK_MS`), `state.rs:50-52` (`history_cap()`), `state.rs:68` (const assert),
  `state.rs:136` (trim cutoff), `checkpoint.rs:486-493` (clock bound `u32::MAX * TICK_MS`), `checkpoint.rs:821`
  (rewind samples at least one tick apart), `systems/arena_validation.rs:45,63,69,111-113,166,198`.
- Construction: `Sim::try_new` (`sim.rs:98`) returns `Result<Sim, &'static str>` and already refuses configs through
  `terrain::validate_config`, `abilities::validate_config`, `arena_rules::validate_config` (`sim.rs:136-138`); a
  `tick_ms` outside {50, 25} is refused there (and `Sim::new`/`reset_active` panic through it). `Sim::restore` must
  refuse it too (a checkpoint is the other way a config enters).
- Digest pattern: `hash.rs:21-32` documents the three config fields whose default folds no word
  (`mob_spacing`, `regen_in_combat`, `camp_buffs`; `frost_wish` follows them, `statics.rs:263-300`): the default
  serializes as a unit. In JSON a unit is `null`, so those fields DO write a key (`"mob_spacing": null`) to a checkpoint.
  The brief's "no key to a checkpoint when it is 50" and "the pattern hash.rs already uses" therefore disagree; the
  oracle accepts either (at 50 the checkpoint's `tick_ms` is absent or `null`), and the digest guard is what pins the
  hash.
- **Trap: renaming the registry constants moves the content hash.** `TICK_RATE` and `TICK_MS` are rows 0 and 1 of the
  `content!` registry (`constants.rs:60-63`), and `content_hash` folds each row's name
  (`tests/content.rs` `removing_renaming_or_reordering_a_row_perturbs_the_hash`). Renaming them to
  `LEGACY_TICK_*` changes `GOLDEN_CONTENT_HASH 0x7a54_2a13_187f_034c` (`tests/content.rs:568`,
  `tests/frost_wall_content.rs:93`), `parity/abilities/hash-tape.json`, `protocol/fixtures/archetypes_v1.json` and the
  Unity `BakedContent.cs`. Keep the registry rows' names; add `LEGACY_TICK_MS`/`LEGACY_TICK_RATE` as plain `pub const`
  aliases outside the registry (or the reverse: keep the rows and deprecate nothing in the registry). Coordinator to
  confirm the brief's section 5 wording.
- `item_view.rs` has no tick-sized ring: its only `TICK_MS` use is a test loop (`item_view.rs:1279`). The vision trail
  and update periods counted in ticks are in `vision.rs:18,21,291,403,1035` (outside this write scope; P-TICK-1b).

## 3b. Existing tests that pin what this packet touches
- `crates/rh-sim/tests/content.rs:559-568` `hash_golden`: content hash `0x7a54_2a13_187f_034c` (see the trap above).
- `crates/rh-sim/tests/content.rs:349` `MISSING_AT_BASE` lists `TICK_RATE` by name.
- `crates/rh-sim/tests/frost_wall_content.rs:93`: the same content hash.
- `crates/rh-sim/src/state.rs:1301-1362` `the_history_ring_is_the_trimmed_vec_it_replaced`,
  `the_ring_holds_the_whole_rewind_window`: at 50 ms the ring holds `(MAX_REWIND_MS + TICK_MS) / TICK_MS + 1` samples and
  `len < history_cap()`; still true when the cap grows for 25 ms.
- `crates/rh-sim/src/checkpoint.rs:1352-1356`: the clock bound `u32::MAX * TICK_MS` is refused above and accepted at.
- `crates/rh-sim/tests/hash_coverage.rs`: every serialized leaf changes the state hash, with the config-field pattern at
  lines 635-769 (`mob_spacing`, `regen_in_combat`, `frost_wish`, `camp_buffs`); a non-default `tick_ms` belongs there.
- `crates/rh-sim/src/hash.rs:539-553` `the_statics_digest_sees_config_map_and_spawns`.
- Many tests read `TICK_MS` directly (about 40 test files under `crates/rh-sim`, `rh-policy`, `rh-server`); the
  deprecated alias keeps them building. No conflict found with the oracle below.

## 4. New paths
- tests beside the code they cover

## 5. Exclusive write scope
- `crates/rh-sim/src/statics.rs` (`SimConfig`), `crates/rh-sim/src/sim.rs` (the advance and accessor),
  `crates/rh-sim/src/state.rs` (`history_cap`, the ring trim), `crates/rh-sim/src/constants.rs` (rename to
  `LEGACY_TICK_MS`/`LEGACY_TICK_RATE` with `TICK_MS`/`TICK_RATE` kept as deprecated aliases until P-TICK-1b),
  `crates/rh-sim/src/hash.rs`, `crates/rh-sim/src/checkpoint.rs` and `checkpoint/`, `crates/rh-sim/src/replay.rs`,
  `crates/rh-sim/src/item_view.rs` (ring size only), `crates/rh-sim/src/systems/arena_validation.rs` (the
  `server_time == tick * tick_ms` check reads the sim's value)

## 6. Contracts and references
- C-TICK (as amended by ADR 0029), C-DET, C-PARITY; ADR 0007; `hash.rs` module docs on config fields that add no word

## 7. Behaviour to preserve
- At 50 ms: every parity fixture, every state hash, `CHECKPOINT_FORMAT` and `REPLAY_FORMAT` contents for existing files.
  The new field must add no word to the digest and no key to a checkpoint when it is 50 (the pattern `hash.rs` already
  uses for `mob_spacing`, `regen_in_combat`, `camp_buffs`).
- The schedule (system order) is untouched.

## 8. Acceptance checks
- `scripts/gate.sh --oracle packet/P-TICK-1a` passes (full).
- Digest guard: the seed-1 headless run's state hash at tick 4000 equals the value pinned on `packet/P-TICK-1a`.
- A 25 ms sim: `server_time == tick * 25` after 10,000 steps; save and restore round-trip with the same hash; the hash
  differs from the same world at 50 ms.
- Construction with any other `tick_ms` (for example 33) is refused.

## Oracle files (read-only for implementers)
- `crates/rh-sim/tests/tick_ms.rs` (integration test, public API only, instead of a module beside `sim.rs`): default 50
  and serde default 50; a 25 ms config writes `"tick_ms": 25`; a 25 ms sim keeps `server_time == tick * 25` exactly at
  every one of 10,000 steps; a 25 ms checkpoint records 25, restores with the same hash and keeps stepping at 25 in
  lock-step; a 50 ms checkpoint has no non-null `tick_ms` and restores at 50; the same world at 50 and 25 ms hashes
  differently (at construction, after a step, and when a 25 ms checkpoint is edited to 50); `Sim::try_new` refuses
  33, 0, 1, 20, 24, 40, 75, 100, 1000, `u32::MAX` and accepts 50, 25; `Sim::restore` refuses a checkpoint edited to 33;
  a 25 ms replay records 25, verifies at every step and ends at 25. Red on the stub: 7 tests fail by assertion, 3 pass
  (the 50 ms ones).
- `crates/rh-headless/tests/tick_ms_digest_guard.rs`: the digest guard. `run_bot_match_with(seed1 map from
  parity/maps/seed1.map.json, 8 Brawlers, seed 1, 4000 ticks, digest on)` with `SimConfig::default()`: 4000 env steps
  (sim tick 4001, `server_time == tick * 50`), not terminated, state hash `0x2b20_ce9e_ab4e_809a`, outcome digest
  `0x8c87_a634_c4ab_c2b3`. Pinned on this branch after the inert stub (the stub's field is `serde(skip)`, so it reaches no
  serializer and no hash). Green now; must stay green.
- `docs/backlog/packets/P-TICK-1a.md` (this file).

## Stub (implementer replaces)
- `statics.rs`: `SimConfig::tick_ms: u32` with `#[serde(skip, default = "stub_tick_ms")]`, default 50, unvalidated,
  unserialized, unhashed. `sim.rs`: `Sim::tick_ms()` returns 50.
- Compile-only `tick_ms: 50,` lines in every exhaustive `SimConfig { .. }` literal (22 files, mostly `rh-server` tests
  and binaries, `rh-headless/src/bin/determinism_sweep_support/mod.rs`, `rh-load/tests/smoke.rs`, two `rh-sim` test
  helpers). They are outside section 5's write scope; the implementer may switch them to the legacy constant but needs no
  other edit there.

## 9. Validation commands
- `cargo test -p rh-sim`, `cargo test -p rh-parity`, `scripts/gate.sh --quick --oracle packet/P-TICK-1a`

## 10. Risk and suggested model tier
High: state, save and hash. Opus.

## 11. Known traps
- A serde default alone is not enough: a field that serializes at 50 changes every checkpoint and digest. Skip it at 50.
- `history_cap()` is a `const fn` with a `const` assert; sizing it for 25 ms grows the ring, which must not change which
  samples a rewind at 50 ms picks (the trim is by time, `now - MAX_REWIND_MS - tick_ms`).
- `server_time` stays `f64`; at 25 ms the sum stays exact (25 is a binary-exact value), so no move to integer time.

## Notes from previous attempts
