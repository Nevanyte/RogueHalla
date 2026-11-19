# P-ELITE-PICK-1: the elite camp killer's 5 s first pick (live world only)

- **Serves:** ORDER_OF_OPERATIONS phase 2, item 13 (P-MOBS-2 / P-CARDS-2), under the owner's 2026-11-18 approval "Killer
  first pick" (`docs/OWNER_DECISIONS.md` row "Elite and augment rework", 2026-11-18; plan of record
  `docs/backlog/AUGMENT_ELITE_REWORK.md`: decisions 1 and 6, packet row and details P-ELITE-PICK-1, wave W5, chest-file
  lane POOL-1 → PICK-1). Workbook chest rule: "the killer gets a 5 s first-pick window".
- **Implementer tier:** implementer-opus (hashed state, checkpoint format, `check_world` rule; cross-crate: `rh-sim` and
  `rh-policy`)
- **Reviewer:** reviewer-opus
- **Integration base:** `feature/unity-rework` @ `7673f8a02` (checkpoint `rh-checkpoint/33`, content hash
  `c890f52fd7ab4546`)
- **D-rules touched:** D1 loose proof (owner direction 2026-11-10; the rule is new, not a port, so no legacy capture
  exists; Legacy is pinned byte-identical by the oracle) · D2 n/a · D5/D6/D9 none (no wire change; C-OBS kept)

## 1. ID and outcome

Under `SimConfig::elites == Rework` only (the live world, `SimConfig::live_content()`), when an elite camp's chest drops,
the credited killer (decision 6: the player with the most damage) gets a 5 s window in which only they may choose at that
chest. After the window the chest is first-come, as today. Under `Legacy`, and on every other chest, nothing changes.

What is true when this packet lands:
- `Chest` carries a hashed `first_pick: Option<FirstPick { killer, until }>` (stub-defined, see section 4). A Rework
  elite **camp** chest is dropped with `Some(FirstPick { killer, until: drop time + window })`; every other chest has
  `None`.
- The window length is a registered content row in `constants.rs` (name containing `FIRST_PICK`, unit `"ms"`, value
  `5000.`), returned by `reward_chests::first_pick_window_ms()`.
- `choose` refuses a non-killer inside the window with the existing `Refusal::NotYours` (no new refusal code), for a card
  and for the skip (`pick == -1`) alike, and records nothing. The per-viewer `can_choose` in `RewardChestObserver` uses
  the same predicate, so the observation and the command never disagree.
- `reward_chests::validate_world` refuses a window on a campless chest, under `Legacy`, with a killer who never joined,
  or ending after `drop + window`. `CHECKPOINT_FORMAT` is bumped once.
- Bots wait: a non-killer `HumanBot` at the chest submits no `Choose` during the window, then picks.

### Decisions (the coordinator asked for them to be decided and pinned)

1. **Open while `server_time < until`** (exclusive end), as the chest's own `server_time < expires_at`. The killer may
   choose at any time; others from `server_time >= until`.
2. **Killer dies, is downed, resolves or leaves: the window ends at once** (the recommended option). Predicate: a
   non-killer is gated only while `server_time < until` **and** the killer is a standing player (`w.player(killer)` is
   `Some` and `standing()`). A player who stops standing never stands again inside a match (the only status reset is
   the lobby and the training console: `systems/lobby.rs:67`, `systems/training.rs:48-56`), so the derived predicate needs
   no extra state and no schedule row. The oracle checks after one step, so a stored early end (e.g. `until` set to now
   in `expire`) passes too.
3. **A killer who is not standing at the drop gets no window** (first-come at once). The derived predicate gives this
   even if the drop stores the window.
4. **The window is the credited killer's wherever they stand.** Eligibility near the camp (`near_camp`) shapes only the
   cards rolled (the pickers' builds); a killer who landed the kill from outside the camp still holds the window.
5. **Refusal code:** `Refusal::NotYours` ("Not yours"), the code an ineligible picker at another player's Trophy gets
   (`mod.rs:300-302`). The window check sits where that check sits (after `Gone`, before reach), so a far non-killer
   inside the window hears "Not yours" rather than "Too far away"; the oracle pins only players in reach.
6. **The skip is gated too.** A skip (`pick == -1`) records the picker and pays the skip heal on a camp chest
   (`mod.rs:308-322`); inside the window it is the killer's alone.
7. **Field shape:** one `Option<FirstPick>` field, last in `Chest`, `#[serde(default)]` (test JSON that declares chests
   without the key keeps reading `None`). It hashes its `Option` tag on every chest (`hash.rs:14-19`), so the state hash
   of every world holding a reward chest moves, Legacy included; the Legacy **behaviour** and draws are byte-identical
   (pinned). See section 7 for the measured effect on existing pins.
8. **`until` is stored, not derived:** the drop time is not stored on a chest, but a shared chest's `expires_at` is
   `drop + lifetime_ms` (`insert`, `mod.rs:61-80`), so `validate_world` checks `until <= expires_at - lifetime_ms +
   window`. A past `until` is valid (the window ended while the chest stands for 10 more seconds).

## 2. Dependencies

- P-AUG-POOL-1 (merged at integration 62): `drop_augment_in` with `Picker`s, `validate_world(w, pool, near)`.
- P-AUG-RULES-1 (merged): `SimConfig::elites`, `EliteRule`.
- Serial merges (coordinator): P-AUG-NATIVE-1a (also bumps `CHECKPOINT_FORMAT`), P-ELITE-TUNE-1 (appends to
  `constants.rs`, regolds), P-AUG-POOL-1-fix (regolds). Whichever of PICK-1 and NATIVE-1a merges second bumps the format
  once more and re-records the hash tape at the merge.
- P-WIRE19-A (later) puts `until` on the wire; this packet changes no wire byte.

## 3. Relevant legacy paths

Legacy has no first-pick window: this is the owner's rework (decision 1). The seams it builds on:
- `packages/server/src/sim/mobLife.ts:158-198`: the kill goes to the player who did the most damage over the whole fight
  (`brain.threat`), ties by `tieRank`; `:228-234`: no killer, no `payMobKill` (so no chest).
- `packages/server/src/sim/chests.ts:217-229` `install`: the `mobKilled` link drops a camp chest (`rollChest`) or, for a
  campless elite, a chest at its body with no pickers; `:87-98` `rollChest` (eligible contributors' builds);
  `:127-160` `handleChoose` (the refusal order: busy, gone, Trophy owner, far, blocked, picked; skip pays on a camp chest).

Rust today (base `7673f8a02`):
- `crates/rh-sim/src/systems/mob_life.rs:243-252` credited killer (most damage, `tie`); `:584-603` the `MobKilled` hook
  fires with `killer: id` and the camp's contributor snapshot.
- `crates/rh-sim/src/hooks.rs:130-141` `HookArgs::MobKilled { killer, camp, elite, contributors, .. }`.
- `crates/rh-sim/src/reward_chests/mod.rs:50-60` `Chest`; `:61-80` `insert`; `:118-145` `drop_augment_in`;
  `:150-173` `drop_trophy`; `:179-222` `on_mob_killed` (the drop, with the killer in hand); `:278-376` `choose`
  (`:300-302` the `NotYours` check); `:393-396` `expire`; `:402-466` `validate_world`.
- `crates/rh-sim/src/reward_chests/observer.rs:159-168` per-viewer `can_choose`.
- `crates/rh-sim/src/checkpoint.rs:23` `CHECKPOINT_FORMAT = "rh-checkpoint/33"`; `:663` the `validate_world` call
  (passes `k.cards.pool()`; the rule needs `k.config.elites` too).
- `crates/rh-sim/src/schedule.rs:1140` `reward_chests::expire` (per-tick system), `:1336` `on_choose`, `:1737` the
  `mobKilled` chest link.
- Bots: `crates/rh-policy/src/bots/perception.rs:338-352` (the live `HumanBot` reads a chest's cards only when
  `can_choose`), `bots/loot.rs:528-537` `pick_intent` (no `Choose` without readable options);
  `bots/temporal_percept/modules/chests.rs:195-223, 247-283` and `temporal_percept/brain/compose14.rs:896` (the private
  Brain, not live). `bots/bot/loot_seat_tests.rs:158-205` already pins "no choose before the cards were readable".
  So the live bot should already wait once `can_choose` is false; the oracle proves it end to end. The implementer
  touches `rh-policy` only if that proof fails.

## 4. New paths

- `crates/rh-sim/src/reward_chests/first_pick_tests.rs` (oracle)
- `crates/rh-policy/tests/first_pick_bots.rs` (oracle)

Stub (commit `stub:`, the implementer's to replace or edit):
- `reward_chests/mod.rs`: `pub(crate) struct FirstPick { pub killer: PlayerId, pub until: f64 }`
  (`deny_unknown_fields`); `Chest::first_pick: Option<FirstPick>` (`#[serde(default)]`, last field); `insert` and
  `drop_trophy` write `None`; `pub(crate) fn first_pick_window_ms() -> f64 { 0. }`; `mod first_pick_tests;`.
- Compile-only `first_pick: None,` lines in the `Chest` literals of `reward_chests/tests.rs` (6) and
  `reward_chests/pool_tests.rs` (1; a P-AUG-POOL-1 oracle file, touched only by this compile line).

## 5. Exclusive write scope

- `crates/rh-sim/src/reward_chests/mod.rs` (the drop sets the window; `choose` gates; `validate_world` rule; the window
  fn; keep the `FirstPick` field names and the `Option` shape, which the oracle and test JSON use)
- `crates/rh-sim/src/reward_chests/observer.rs` (`can_choose` reads the same predicate)
- `crates/rh-sim/src/checkpoint.rs`: `CHECKPOINT_FORMAT` and the one `reward_chests::validate_world` call line
- `crates/rh-sim/src/constants.rs`: append one row after the last `REWORK_*` row, e.g.
  `REWORK_ELITE_FIRST_PICK_MS: f64 = 5000., "ms";`
- `crates/rh-sim/README.md:71` and `crates/rh-sim/src/fighter.rs:7` (doc mentions of the format string), optional
- `crates/rh-policy/src/bots/perception.rs`, `bots/loot.rs`, `bots/temporal_percept/modules/chests.rs` only if the bot
  oracle fails without a change
- The content-hash copies, by `scripts/regolden-content.sh` only (it rewrites `tests/content.rs` `GOLDEN_CONTENT_HASH`,
  the two reload-proof `.mjs`, `BakedContent.cs`, `parity/abilities/hash-tape.json` and
  `protocol/fixtures/archetypes_v1.json`; the tape's recorder also writes the new `checkpoint` string). Gate flags:
  `--allow-artefacts` (`BakedContent.cs`), `--regen-fixtures`; the `hash-tape.json` lines trip the oracle-capture stop and
  the coordinator waives them, as for P-AUG-TUNE-1.

## 6. Contracts and references

- C-OBS: the window's end is public (everyone may know it), but it reaches viewers only with P-WIRE19-A. Here the only
  observable is the viewer's own `can_choose`, which is already per viewer.
- C-RNG / determinism: the window draws nothing; the Rework drop's options and reward-RNG continuation equal Legacy's.
- C-INPUT: the gate is inside `choose` (the one `Choose` path); bots go through `validate` + `submit`.
- `crates/rh-sim/AGENTS.md`: a `check_world` rule per new invariant with a refusal test each; bump `CHECKPOINT_FORMAT`.
- `docs/agent/AUGMENT_REWORK.md`: never branch on the rule where a table would do. This rule has no table: the drop reads
  `k.config.elites == EliteRule::Rework` (as `mob_life.rs:530` does for the helper share), and `validate_world` takes the
  rule.

## 7. Behaviour to preserve

- `Legacy`: every chest's options, spot, expiry, hold and the reward-RNG words after a drop are those of today
  (pinned by `legacy_has_no_window_and_the_rework_drop_differs_from_it_by_the_window_alone`); no window, no new refusal.
- The `Option` tag changes the state hash of any world holding a reward chest (decision 7). Measured on the stub
  (section "Existing tests" below): which pinned folds moved.
- Wire and observation: `RewardChestView` keeps exactly `handle, at, expires_at, options, picked, can_choose`; no
  `PROTOCOL_VERSION`, `OBSERVATION_VERSION` or policy-contract bump.
- Schedule: no new row (`crates/rh-parity/tests/schedule.rs`).
- `no_alloc`: the gate allocates nothing (a `Copy` field and a predicate).

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-ELITE-PICK-1` passes (flags as in section 5).
- `cargo test -p rh-sim --features trusted-status-probes --lib reward_chests::` green (the oracle and the existing chest
  tests).
- `cargo test -p rh-policy --test first_pick_bots` green.
- `cargo test -p rh-sim --features trusted-status-probes --test checkpoint --test content --test hash_coverage` green.
- `scripts/regolden-content.sh --check` clean.

## Existing tests that pin what this packet changes

(Filled in after the stub run; see below.)

## Oracle files (read-only for implementers)

(Filled in with the oracle commits.)

## 9. Validation commands

- `cargo test -p rh-sim --features trusted-status-probes --lib reward_chests::first_pick_tests`
- `cargo test -p rh-policy --test first_pick_bots`
- `scripts/regolden-content.sh` then `scripts/regolden-content.sh --check`
- `scripts/gate.sh --quick --oracle packet/P-ELITE-PICK-1 --allow-artefacts --regen-fixtures`

## 10. Risk and suggested model tier

Opus implementer, reviewer-opus: new hashed state on a shared struct, a checkpoint-format bump that collides with
P-AUG-NATIVE-1a, a `check_world` rule, and a cross-crate proof (the live bot). The gate itself is a few lines.

## 11. Known traps

1. **One predicate, two readers.** `choose` and the observer's `can_choose` must call the same function (the
   `birth_available` precedent, `mod.rs:42-47`), or a bot reads "open" and spams refused picks.
2. **The killer's own gate.** The killer passes the window; every other check (busy, gone, reach, picked, taken,
   owned, needs) applies to them as before.
3. **Same tick.** The window opens at the drop (the `mobKilled` link, inside the payout pass) and the chest is held one
   tick (`birth_hold_ticks`), so no pick can race the drop. At the end, a pick submitted for the tick whose
   `server_time >= until` succeeds for anyone.
4. **Format bump order.** NATIVE-1a may bump first; read the constant, never a literal. The oracle-amends below replace
   the literals of the existing format pins with the constant and add `rh-checkpoint/33` to their refused lists.
5. **Content hash.** The new row moves `content_hash`; regold only through the script.
6. **`validate_world` signature.** It needs the rule (or `k`); its one caller is `checkpoint.rs:663`.

## Notes from previous attempts
