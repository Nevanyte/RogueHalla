# P-ELITE-TUNE-1: Rework elite numbers to dmg x1.0, hp x1.5 (owner decision), before/after bench table, content move

- **Serves:** `docs/backlog/AUGMENT_ELITE_REWORK.md` decisions 1, 8 and 10, the P-ELITE-TUNE-1 row and the Verification "Elites" line (owner target: 10-20% solo death rate at 12 banked camps). ORDER_OF_OPERATIONS phase 2, item 13 (P-MOBS-2).
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer-opus
- **Integration base:** `coord/int-63` (integration plus P-NOALLOC-GATE-1, P-ELITE-BENCH-2, P-ELITE-SHARE-1; full gate pending). P-AUG-TUNE-1 joins it before the implementer starts: it appends `REWORK_WISHFUL_HIT_SHARE` and `REWORK_WISHFUL_BUDGET_SHARE` after `REWORK_EYE_FOR_AN_EYE_WEIGHT` and moves the content hash to `c890f52fd7ab4546`. Every gate run passes `--base coord/int-63`.
- **D-rules touched:** D1 loose proof (owner direction 2026-11-10; one behaviour test file) . D2 n/a . D5/D6/D9 none

## 1. ID and outcome
Under `SimConfig::elites == Rework` only, the elite numbers become `dmg_mult` 1.5 -> **1.0**, `hp_mult` 2.0 -> **1.5**, `k`
stays 0.65 (`ELITE_K`). `Legacy` stays byte-identical. The numbers are the owner's choice of 2026-11-19 ("dmg 1.0, hp
1.5", `docs/OWNER_DECISIONS.md` row "2026-11-19 Elite numbers for P-ELITE-TUNE-1"; MIGRATION "Elite measurement (owner
checkpoint, 2026-11-19)"), from a measured sweep on the P-ELITE-BENCH-2 Duelist bench at banked_12: 43.1% overall death
rate today, 18.3% with these numbers (target 10-20%).

What the implementer does, and nothing else:
1. Append two registry rows in `constants.rs`, after the last `REWORK_*` row at implementation time (the Wishful pair
   once P-AUG-TUNE-1 has landed): `REWORK_ELITE_DMG_MULT: f64 = 1.0, "multiplier"` and `REWORK_ELITE_HP_MULT: f64 = 1.5,
   "multiplier"`. No `REWORK_ELITE_K` row: `k` stays `crate::constants::ELITE_K`.
2. Point `REWORK_ELITES` (`crates/rh-sim/src/mob_runtime.rs`, about 453) at them. Update its seam comment and the
   `tests/content.rs` NON_GAMEPLAY description of `REWORK_ELITES` only if the gate asks (that file is an oracle file:
   if an edit is needed the implementer stops and reports).
3. Move the content hash through `scripts/regolden-content.sh` only (it also re-records the ability hash tape and the
   hitbox fixtures); never hand-edit the copies. Move 3 of the plan's decision 10 (accepted).
4. Record the before and after table. Build `--features elite-bench`, release, and run the owner command
   `camp_bench --elite --fighter both` (README of `tools/balance-camps/`) twice, `--seed 1` default seeds (3), all cores:
   at the OLD numbers (`git stash` the change, or the integration-branch binary) and at the new ones. Save the stderr
   tables as `tools/balance-camps/elite_before.txt` and `tools/balance-camps/elite_after.txt` and summarise both in
   `tools/balance-camps/README.md` (overall rate and the banked_12 Duelist rate before and after, and the per-camp spread).
   The after table should land near the owner's 18.3% at banked_12 Duelist overall; report the figure honestly if it
   does not, do not tune further.
5. `docs/agent/AUGMENT_REWORK.md`: replace the "To retune (P-ELITE-TUNE-1)" paragraph and the stored-rule sentences with what is now true.

Proposed DEVIATIONS row (the owner signs it): "under EliteRule::Rework elites deal x1.0 (legacy x1.5) and have x1.5 HP (legacy x2.0); k unchanged".

## 2. Dependencies
P-AUG-RULES-1 (seam), P-ELITE-BENCH-2 and P-ELITE-SHARE-1 (in `coord/int-63`), P-AUG-TUNE-1 (joins `coord/int-63` before the implementer starts; order of constants only).

## 3. Relevant legacy paths
Legacy has no Rework; the seam reads legacy values by name. Legacy elite numbers: `packages/shared/src/constants.ts` `ELITE_K`
(0.65), `ELITE_HP_MULT` (2), `ELITE_DMG_MULT` (1.5) (Rust `constants.rs:287,289,320`; seam doc `mob_runtime.rs:439-445` cites `mobs.ts:37-41`, `constants.ts:485,513-514`). Where Rust reads them (all through `EliteTuning`):
- `spawn_hp`, `crates/rh-sim/src/mob_runtime.rs` (after the table, about 495): `k` and `hp_mult`.
- `World::faction_spec`, `crates/rh-sim/src/state.rs:1096-1105` (the elite's `k` and `hp_mult` for `f.hp`) and `point_damage`, `state.rs:1131-1137` (`MOB_DMG_MULT * dmg_mult`, `k`).
- `sentenced::npc_pulse`, `crates/rh-sim/src/systems/sentenced.rs:541-543` (`k * d`).
- Births: `systems/mobs.rs:75,163,209,470`, `systems/mob_checkpoint.rs:350`.
No RNG draw, tick order or schedule row changes.

**Stored rule (read this).** `EliteTuning::stored_rule` (`mob_runtime.rs`, below `of`) stores a Rework world whose numbers equal legacy as `Legacy`. After this packet the numbers differ, so every live world stores `EliteRule::Rework` in `MobRuntime::elites`, which serializes as `"rework"` and folds the tag in the state hash (`hash.rs` module docs: Legacy is a unit with no hasher word, Rework a word; `check_elite_rule`, `systems/mobs.rs:638-655`, admits exactly one value per statics digest). That path is exercised today only by the doctored checkpoints in `elite_rule_tests`; no live world has run it. No code change is expected (the seam was built for this), the oracle pins that it works (a Rework checkpoint with a live, hurt elite restores and hashes equal). A `Legacy`-config world is unaffected.

## 4. New paths
- `crates/rh-sim/tests/elite_tune.rs` (the oracle).
- `tools/balance-camps/elite_before.txt`, `tools/balance-camps/elite_after.txt` (implementer, recorded tables).

## 5. Exclusive write scope
- `crates/rh-sim/src/constants.rs` (append the two rows only)
- `crates/rh-sim/src/mob_runtime.rs` (`REWORK_ELITES` and its seam comment only)
- the regolden copies, by `scripts/regolden-content.sh` only: `crates/rh-sim/tests/content.rs` `GOLDEN_CONTENT_HASH` (the script edits this oracle-adjacent copy; the gate treats it as script output, see the P-CONTENT-REGOLDEN-1 packet), the two `tools/unity-browser-reload-proof/*.mjs`, `BakedContent.cs` (needs `--allow-artefacts`), the ability hash tape and the hitbox fixtures (the script's two protected commands)
- `tools/balance-camps/` (README, `elite_before.txt`, `elite_after.txt`)
- `docs/agent/AUGMENT_REWORK.md`

Must not touch: anything else. Bots (`rh-policy`) read `EliteTuning::of(Legacy)` and stay as they are (P-AUG-BOTS-1 threads the rule later).

## 6. Contracts and references
C-PARITY (Legacy and every parity fixture unchanged), C-TICK/C-RNG (no change), C-NUM (f64 by name), `docs/agent/AUGMENT_REWORK.md` (rule seam), plan decisions 1, 8, 10.

## 7. Behaviour to preserve
- A `Legacy`-elite world is byte-identical: same hash, same checkpoint, same events; the legacy constants keep their values.
- No new allocation in the step (`no_alloc`).
- The Rework content hash moves exactly once, through the script; nothing else in content changes.

## 8. Acceptance checks
- `cargo test -p rh-sim --test elite_tune` green.
- The existing tests listed below as "move" are green after the oracle-amends (those files are oracle files; the implementer does not edit them).
- `scripts/gate.sh --quick --oracle packet/P-ELITE-TUNE-1 --base coord/int-63`.
- `elite_before.txt` and `elite_after.txt` exist and the README summary matches them.

## Existing tests that pin what this packet changes
(Measured by running `cargo test -p rh-sim` with `REWORK_ELITES` set to hp 1.5 / dmg 1.0; see the results block below.)

RESULTS_PLACEHOLDER

## Oracle files (read-only for implementers)
- `crates/rh-sim/tests/elite_tune.rs`

AMEND_PLACEHOLDER

## 9. Validation commands
- `export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"; cargo test -p rh-sim --test elite_tune`
- `scripts/regolden-content.sh --check`, then `scripts/regolden-content.sh`
- `cargo run --release -p rh-headless --features elite-bench --bin camp_bench -- --elite --seed 1 --fighter both > /dev/null 2> tools/balance-camps/elite_after.txt`

## 10. Risk and suggested model tier
implementer-sonnet, reviewer-opus. The code change is two constants and a table; the risk is the stored-rule path (first live use of the `"rework"` runtime word) and the content-hash/regolden steps. If the stored-rule path needs a code change in `mobs.rs`, `hash.rs` or `checkpoint.rs`, stop: that is Opus's and the packet goes back to the coordinator.

## 11. Known traps
- Appending the two rows after P-AUG-TUNE-1's rows: the registry order is the content-hash order; do not insert before the Wishful pair.
- The elite bench numbers move with the content-hash script? No: the bench reads the sim; but run the "before" table on the integration-branch tree, not on the new one with old numbers hand-patched.
- Bots still plan with legacy elite numbers (see section 5); the bench fighters are scripted, not bots, so the bench is unaffected.
- The oracle's first-hit damage measure assumes an idle player beside an elite is hit on the same tick under both rules (elite AI does not read its own HP before its first attack); if a seed ever breaks that, the test names the zone.

## Notes from previous attempts
