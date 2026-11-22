# P-BAL-PROBE-1: read-only quest and lock probes on `Sim`, and a quest-reward grant in the trusted build

- **Serves:** not in the plan: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot,
  per-elite numbers"; plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 2, "Two read-only sim probes" and
  "Paired mode ... `rh_sim::trusted_build::apply`, extended to grant a quest reward")
- **Implementer tier:** implementer-opus
- **Reviewer:** reviewer-opus (rh-sim state readers, the trusted build)
- **Integration base:** `agent/opus/P-BAL-Q25-SEAM-1` @ `5694f17de` (reviewed, waiting to merge; stacked on it). Until it
  merges the gate takes `--base agent/opus/P-BAL-Q25-SEAM-1`.
- **D-rules touched:** D1 n/a (no gameplay change; the probes read, the grant is a bench seam behind a feature) · D2 n/a ·
  D5/D6/D9 none

## 1. ID and outcome

The balance lab (`crates/rh-headless/src/balance_lab.rs`) measures quests from each seat's observation, which cannot see a
completion: a completed offer leaves the seat's journal (`quest_view/board.rs:17-22`) and the row's `owner_completed` is
always `false` (`quest_view/board.rs:118`). This packet adds, between steps and read-only:

1. `Sim::quest_attempts(owner) -> Vec<QuestAttemptProbe>`: every board attempt the seat holds (open, ready, turned in
   and waiting for payment, or completed), with its quest, deadline and progress.
2. `Sim::gates_locked() -> bool`: the coliseum lock (`w.rt.gates_locked`).
3. `TrustedBuild.rewards: Vec<RewardId>` (`#[serde(default)]`), paid by `trusted_build::apply` through the board's own
   reward code, so a granted spell is an ordinary owned spell and a granted permanent is the ordinary permanent effect.
4. Two quest-table review notes (reviewer-opus on P-BAL-Q25-SEAM-1): `at_turn_in` reads `TOP25.contact_range`, and
   `falcon_cap`'s range is exactly 1 (a restore bound), plus the doc points for `tuning.rs`.

Nothing a match can observe changes: no state, no event, no draw, no hash, wire or observation change.

## 2. Dependencies

`agent/opus/P-BAL-Q25-SEAM-1` (the `TOP25` table, `tuning.rs`, `tuning_tests.rs`). It must merge first.

## 3. Relevant legacy paths

None. The Top 25 board is Rust-native (`docs/quests.md`); the trusted build cites legacy `grantCard`
(`packages/server/src/sim/chests.ts:189`) for cards only, and quest rewards have no legacy counterpart. The oracle is the
current Rust code at the base. Lines below are at `5694f17de`, under `crates/rh-sim/src/`.

## Established facts

### F1. The board's attempt state (what the probe reads, and what it can tell apart)

- **Where.** `World.quests: Option<live::Quests>` (`quests/live.rs:69` `board: Option<BoardState>`, `:72`
  `rewards: Option<RewardState>`). Both are `Some` exactly when the match has a quest deal (`quests/live.rs:307-320`:
  both built from `k.quest_deal`). `BoardState.attempts: Vec<Attempt>` (`quests/board/attempts.rs:138-148`), one per
  (owner, offer), duplicates refused by `start` (`:161-171`) and by `validate` (`:566-568`).
- **`Attempt` fields** (`attempts.rs:55-73`): `owner`, `offer` (board offer id; `offer(w, id)` and `kind` give the
  `QuestId`, `:203-208`), `deadline` (server ms; `0` when the quest has no route timer, `:299`, and set to `0` at
  turn-in, `interactions.rs:345`, and at completion, `attempts.rs:190`), `progress` (anchors/stages; the dwell quests
  project `dwell_ms` as whole seconds in `progress()`, `:222-232`), `bits`, `branch`, `carrying`, `ready` (objective met,
  `achieve`, `:106-109`), `completed`, `pending: Option<PendingReward>` (turned in at the endpoint, reward not paid yet),
  `reward`, `dwell_ms`, `meditation`, `damaged`, `lock`.
- **Existing readers** (all `pub(crate)`, `attempts.rs:216-249`): `is_accepted` = attempt exists and not completed;
  `is_completed`; `progress`; `deadline` (0 when no attempt); `ready` = `ready && !completed`; `has_pending_choice` =
  `pending.is_some()`. `pending()` (`:251`) also gates on `owner_allowed`, so the probe must use the raw field
  (`has_pending_choice`), not `pending()`.
- **Life of an attempt.**
  - *Accept* (`accept`, `:287-310`): refused while `gates_locked` or the owner is a bot / not standing / not `Active`
    (`owner_allowed`, `:212-214`). Pushes a fresh `Attempt` (progress 0, deadline = now + max(route timer, route
    length x `route_pace` / `PLAYER_SPEED` + `route_slack_ms`)).
  - *Ready*: the tick (`interactions.rs:274-349`) sets `ready` when the objective is met. At the endpoint
    (`interactions.rs:336-347`) a ready attempt gets `pending = Some(reward)` and `deadline = 0`.
  - *Paid / completed*: in the same tick `rewards::system` runs after `attempts::system` (`quests/live.rs:815-816`);
    `pay_pending` (`rewards/inventory.rs:205-260`) grants and calls `attempts::finish` (`attempts.rs:257-270` ->
    `BoardState::finish` `:181-192`): `completed = true`, `pending = None`, `carrying = false`, `deadline = 0`. So "ready
    with pending" lasts more than a tick only when the spell slots are full (the offer id waits in
    `Owner.pending_choices` until action 8, `inventory.rs:240-246`, `resolve_choice` `:263-312`) or a permanent's
    `apply` refuses (the Bargain reopens, `attempts.rs:274-285`).
  - *Completed attempts are kept for the whole match* (never removed, also on departure: `departed` keeps
    `a.completed`, `:496-508`). The same seat can never retry that offer (`start` refuses an existing pair) and its
    journal hides the offer (`quest_view/board.rs:21`).
  - *Timer expiry*: `attempts::system` (`:446-492`) calls `interactions::abandon` when not locked, `deadline > 0` and
    `deadline <= server_time` (`:468-472`). `BoardState::abandon` (`:172-180`) **removes** the attempt (only when not
    completed and not pending). While the gates are locked the deadline is pushed back one step per tick instead
    (`:482-486`), so no attempt expires in the coliseum.
  - *Death*: the `DropBody` hook `on_drop_body` (`attempts/signals.rs:287-307`) removes the victim's attempts at once
    (`retain` at `:304-306`), keeping completed ones and turned-in ones (`pending`) except a turned-in Burning Crown.
    So a turned-in reward waiting for a slot choice survives death (`attempts.rs:246`).
  - *Down / not standing / bot / phase*: `attempts::system` also removes an open attempt when `!owner_allowed` (`:468`),
    on the first board tick after the owner stops standing; pending ones are skipped (`:463-466`).
  - *Abandon* (action 1, `interactions.rs:63-66`): removes it at once (same `BoardState::abandon`).
  - *Departure* (`departed`, `:496-508`, from `lifecycle::remove` / `abandon`): removes open and pending attempts.
  - *Retry* after expiry, death or abandon: a new `Attempt` from `start`, progress 0, a new deadline. **There is no
    attempt counter** in the state.
- **What the stored state can tell apart (finding, no state is added):** open (accepted, not ready), ready (objective met,
  not turned in), turned in and waiting for payment (pending), completed. **It cannot tell timer expiry, death, abandon
  and departure apart**: all four delete the attempt and leave nothing. It also cannot count attempts. A probe caller
  that wants the end cause must keep its own previous probe and classify the disappearance itself (the lab's documented
  heuristic: deadline passed -> timer; seat `Event::Death` -> death; else abandoned). Same-tick death and expiry are
  indistinguishable even then.

### F2. How a quest reward is paid today, and the smallest entry the grant can call

- Path: tick -> `attempts::system` sets `pending` at the endpoint -> `rewards::system` (`rewards.rs:34-37`) ->
  `inventory::pay_pending` -> `pay_claim` (`inventory.rs:205-260`): `awards_for_offer` (`awards.rs:136-149`: one
  `Award`, or `BlindRage` + `StillMind` for `RewardId::BlindRageAndStillMind`).
  - Permanent (`Award::is_permanent`, `awards.rs:36-47`): `permanent::apply(cx, owner, award, pending)`
    (`permanent.rs:161-198`) applies the effect then `inventory::grant` records it in `Owner.permanent`. Effects:
    `CrownPower` needs a carried crown (`crown_power`, refuses otherwise), `TradeUp` needs `pending.points` 1-3,
    `TheBargain` needs `pending.give/receive` (or a spare weapon): **these three depend on how the quest was played**.
    `ChefsKiss` sets `chef_bonus` and re-folds; `FeedOrHuntDamage` grants `TOP25.feed_damage_stacks` EmberImp stacks
    (`grant_stacks` -> `modifiers::grant_mob_stat`, which re-folds, and emits an owner notice event); `WarmCoat` clears
    chill; `BreakersHelm` is read by the damage ledger (`armor_bypass_fraction`, `permanent.rs:241-249`).
    `apply` refuses an award already held (`:162-164`).
  - Spell: `inventory::grant` (`inventory.rs:150-165`) puts `Owned::new(award, now)` (full charges: 1, or
    `TOP25.lantern_ward_charges` for Lantern Ward; `ready_at = now`) in the first empty slot. With fewer free slots than
    awards, the offer id goes to `pending_choices` (the full-slot choice); with exactly one free slot for the BlindRage
    bundle, the second award is dropped as ground loot (`:226-230`).
  - Notices: `quests::notices::owner(cx.ev, ...)` on payment (`:217-221`, `:247-258`) and in `grant_stacks`.
- Smallest entry: `inventory::grant` (`pub(super)`) for a spell, `permanent::apply` (`pub(super)`, needs a `Cx`) for a
  permanent. `Sim::with_cx` (`sim.rs:346-359`, `pub(crate)`) gives a `Cx` between steps; its events go to
  `w.pending_events` and come out of the next step. The trusted build already clears `sim.projection.marks` after
  changing build inputs (`trusted_build.rs:118-120`), which is what `with_cx`'s invariant (`sim.rs:342-345`) asks for.
  So the grant needs one `pub(crate)` entry in `quests/board/rewards.rs` (or `pub(crate)` on the two functions through
  that module), nothing else.
- `check_world` side: `rewards::validate` -> `inventory::validate` (`inventory.rs:337-373`) requires the owner to be a
  seated player, slots valid (`Owned::valid`: not permanent, full `max_charges`), `permanent` only permanent awards,
  `chef_bonus` present exactly when Chef's Kiss is held, and every `pending_choices` entry backed by a pending attempt.
  A grant through `grant`/`apply` satisfies all of them with no new state.

### F3. Existing public accessors (do not duplicate)

- No public reader of board attempts. `QuestObserver`/`observe` give the viewer's journal (`quest_view/board.rs`),
  which hides completed offers and pins `owner_completed: false`.
- `rh_sim::public_snapshot(&sim).arena.gates_locked` exposes the lock today (`observe/public.rs:95`, used by
  `tests/arena_no_alloc.rs:73`), but builds a whole public snapshot per call; `Sim::gates_locked()` is the one-field read
  the plan asks for. No `Sim::gates_locked` exists.
- Farm total: **not added.** `Player.farm` (`state.rs:596`) is public, `Farm::count` is `pub` (`modifiers/farm.rs:71`) and
  `rh_sim::modifiers::farm::DEFINITION.rows` is `pub` (`:36-60`, 20 normal types), so a lab total is one line:
  `DEFINITION.rows.iter().map(|r| p.farm.count(r.mob)).sum::<u64>()` (plus `farm.corpse_stacks()` if wanted).
  `elite_bench.rs:176-181` reads per-type counts the same way. `modifiers/farm.rs` leaves the write scope.
- Owned spells reach the owner's observation as `QuestView.spells` (`quest_view/board.rs:121-133`: `reward` is
  `Award as u8`, `charges`, `ready_at`).

### F4. How `trusted_build::apply` is tested and guarded

- `crates/rh-sim/tests/trusted_build.rs` (`#![cfg(feature = "trusted-build-setup")]`): a minute-5 build on a board-less
  PvE world (`quests: false`) reaches the fold, observation, state hash and a checkpoint (`Sim::restore` runs
  `check_world`); refusals change nothing (state hash before/after); JSON parse with `deny_unknown_fields`.
- `crates/rh-sim/tests/trusted_build_feature_guard.rs`: no server build enables `trusted-build-setup` /
  `trusted-status-probes`; `lib.rs` declares `pub mod trusted_build` under the feature and names `trusted_build` exactly
  once (`:238-246`). The new `QuestAttemptProbe` re-export is not under the feature and must not name the module.
- The gate tests rh-sim with `trusted-status-probes`, which turns on `trusted-build-setup` (`Cargo.toml:25-30`).
- `TrustedBuild` is built by struct literal without `..Default::default()` in three places, which a new field breaks:
  `tests/trusted_build.rs:109` (`minute_five`), `crates/rh-headless/src/elite_bench.rs:170` and `:1257`. The stub adds
  `rewards: Vec::new()` there (compile-only lines; the implementer does not touch rh-headless).

### F5. The review notes and the tests that pin them

- `at_turn_in` (`attempts/choices.rs:126-144`) uses `QUEST_TURN_IN_RANGE` (`constants.rs:391`, 64); the turn-in menu
  actions are pushed only when `at_turn_in` holds (`quest_view/board.rs:50`), while the objective menu (`choices.rs:41`)
  and the tick's endpoint check (`interactions.rs:282`, which sets `pending`) use `TOP25.contact_range` (=
  `QUEST_TURN_IN_RANGE`, `tuning.rs:117`). After the fix `QUEST_TURN_IN_RANGE` has no reader left in `attempts.rs`
  (its `use` at `:22` goes); `quests/live.rs:933` still reads it (legacy quests, out of scope). Same value: no hash,
  behaviour or test change. `TOP25` is an immutable `static`: there is no test hook to doctor it, so the oracle reads
  the source. `resolve_choice` (`inventory.rs:264`) is the only gameplay caller.
- `falcon_cap` (`tuning.rs:95`, value 1) is read only by the deployment restore validator
  (`rewards/effects/terrain.rs:446`); the cast path drops the owner's previous falcon (`terrain.rs:266`), so a table
  value of 2 would admit restores that play can never produce. `tuning_tests.rs:354` pins `range!(falcon_cap, 1, 2, ...)`:
  amended to `1, 1` in its own `oracle-amend:` commit (passes today; it stops a retune to 2).
- `tuning.rs` docs: 39 of the 91 fields are "not folded" in the quest-table brief's F1 table
  (`docs/backlog/packets/P-BAL-Q25-SEAM-1.md`, counted: `grep -c "| not folded |"` = 39). `tornado_windup_ms` is also the
  Cleansed buff's length (`effects.rs:484`, restore bound `effects/checkpoint.rs:29`). Readers for the doc comments:
  `crown_burn_rate` (`attempts/signals.rs:309-345`: share of the carrier's max hp dealt as terrain damage once per
  whole second carried; also folded),
  `crown_power_cap` (`attempts.rs:243`, whole seconds carried, capped; also a restore bound `:525`), `pull_tick_distance`
  (`effects.rs:961`, distance per legacy tick scaled by `per_step`), `tornado_pull` (`effects.rs:980`,
  `effects/terrain.rs:428`), `turret_hp` (`terrain.rs:221,428,444`), `turret_damage_share` (`terrain.rs:399`),
  `route_pace` (`attempts.rs:302`). The implementer reads each reader for the exact unit.
- Tests that touch the changed code: `attempts/scenarios.rs` (all 25 quests, both step rates) and
  `attempts/scenarios/full_slots.rs` exercise `at_turn_in` through `resolve_choice` and the journal; same value, so they
  stay green. `tests/top25_tuning.rs` scans board sources for numeric consts and tuning floats (new code in
  `attempts.rs`/`rewards/**` must add no numeric literal outside `TOP25`). `tests/content.rs` `GOLDEN_CONTENT_HASH`
  must not move. No existing test reads `falcon_cap` besides `tuning_tests.rs:354`.

## Decisions (made here, pinned by the oracle)

- **Probe shape:** `Sim::quest_attempts(&self, owner) -> Vec<QuestAttemptProbe>`, one row per stored attempt of `owner`,
  in storage order (the order the seat accepted them; deterministic). A `Vec`, not an iterator: the attempt type and the
  board helpers are private to `quests::board`, and an `impl Iterator` borrowing them would need a public iterator type
  or a boxed one; the probe runs between steps and may allocate (its doc says so). The row (`sim.rs`, re-exported as
  `rh_sim::QuestAttemptProbe`): `offer: u32`, `quest: QuestId`, `accepted` (= `is_accepted`), `ready` (= `ready`),
  `turned_in` (= `pending.is_some()`), `completed`, `deadline: f64` (= the journal's `expires_at`), `progress: u8` (=
  `progress()`, the journal's `owner_progress`). No attempts counter (the state has none, F1). A world without the board,
  a bot seat, an unknown id: empty.
- **Grant (`TrustedBuild.rewards: Vec<RewardId>`):** paid after the cards/stacks/boosts re-fold, in request order, all or
  nothing (every refusal is checked before anything is written). Spells through `inventory::grant` (full charges, ready
  now); permanents through `permanent::apply` with a default `PendingReward`. A request with no rewards behaves exactly
  as today (bot seats and board-less worlds still take cards, stacks and boosts). **No event**: the grant leaves
  `w.pending_events` as it was (the module's "No RNG draw, no event"), so the owner notice of `grant_stacks` is dropped.
  New `BuildRefusal` variants (checked only when `rewards` is non-empty, after the existing checks):
  - `NoBoard`: the world has no quest board (`w.quests` without `board`/`rewards`).
  - `BotSeat(PlayerId)`: a bot seat (it can own no quest reward, `inventory.rs:312`, `casts.rs:55`).
  - `RewardNeedsQuest(RewardId)`: `CrownPower`, `TradeUp`, `TheBargain` (their size comes from how the quest was played;
    a bench grants their stacks through `stacks` instead).
  - `RewardHeld(RewardId)`: a permanent already held, or asked twice.
  - `SlotsFull(RewardId)`: not enough empty spell slots for this spell (two for `BlindRageAndStillMind`); no pending
    choice and no ground loot are made.

## 4. New paths

- `crates/rh-sim/src/quests/board/attempts/scenarios/probes.rs` (oracle, in-crate: it uses the `Raid` scenario helpers)
- `crates/rh-sim/tests/quest_probes.rs` (oracle, public API)
- `crates/rh-sim/tests/top25_review_notes.rs` (oracle, source reads)

## 5. Exclusive write scope

- `crates/rh-sim/src/sim.rs` (the two accessors and `QuestAttemptProbe` only)
- `crates/rh-sim/src/lib.rs` (the stub's re-export line only; already done)
- `crates/rh-sim/src/trusted_build.rs`
- `crates/rh-sim/src/quests/board/attempts.rs` (one `pub(crate)` probe reader; drop the `QUEST_TURN_IN_RANGE` import)
- `crates/rh-sim/src/quests/board/attempts/choices.rs` (the one read in `at_turn_in`)
- `crates/rh-sim/src/quests/board/rewards.rs`, `rewards/inventory.rs`, `rewards/permanent.rs`, `rewards/awards.rs` (the
  smallest `pub(crate)` grant entry)
- `crates/rh-sim/src/quests/board/tuning.rs` (doc comments only; no value, field or order change)
- Nothing in `observe.rs`, `schedule.rs`, `content.rs`, the protocol, rh-policy or rh-headless (the stub's compile lines
  in `elite_bench.rs` are done).

## 6. Contracts and references

- C-INPUT: the grant is not an input path (no command reaches it); it exists only under `trusted-build-setup`.
- C-OBS: the probes are server/bench reads on `&Sim`, not part of any observation.
- C-TICK, C-RNG: no draw, no schedule row, no reorder.
- `check_world` (`checkpoint.rs:312`): a world after a grant must pass it unchanged; no new rule needed (no new state).

## 7. Behaviour to preserve

- Content hash (`tests/content.rs` `GOLDEN_CONTENT_HASH`), every state hash, `CHECKPOINT_FORMAT`,
  `OBSERVATION_VERSION`, `PROTOCOL_VERSION`: unchanged.
- No allocation in a step (`tests/no_alloc.rs` and friends): the probes allocate only when called, between steps.
- `trusted_build` stays behind its feature (`tests/trusted_build_feature_guard.rs`); `lib.rs` names it once.
- Every existing test unchanged, except the listed `oracle-amend:` (`tuning_tests.rs` `falcon_cap` 1..=1) and the stub's
  compile-only `rewards: Vec::new()` line in `tests/trusted_build.rs:109`.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-BAL-PROBE-1` passes (with `--base agent/opus/P-BAL-Q25-SEAM-1` until that
  merges).
- `cargo test -p rh-sim --features trusted-status-probes probes` and `--test quest_probes --test top25_review_notes`
  green; `cargo test -p rh-sim --test content` green (hash unmoved).

## Oracle files (read-only for implementers)

- `crates/rh-sim/src/quests/board/attempts/scenarios/probes.rs`
- `crates/rh-sim/tests/quest_probes.rs`
- `crates/rh-sim/tests/top25_review_notes.rs`
- `crates/rh-sim/src/quests/board/tuning_tests.rs` (`oracle-amend:` falcon_cap range 1..=1, for the owner)
- `docs/backlog/packets/P-BAL-PROBE-1.md`

## 9. Validation commands

- `cargo test -p rh-sim --features trusted-status-probes --lib probes`
- `cargo test -p rh-sim --features trusted-status-probes --test quest_probes --test top25_review_notes`
- `cargo test -p rh-sim --lib probes` (without the feature: the grant tests compile out, the probe tests still run)
- `scripts/gate.sh --oracle-check --base agent/opus/P-BAL-Q25-SEAM-1` (oracle author),
  `scripts/gate.sh --quick --oracle packet/P-BAL-PROBE-1 --base agent/opus/P-BAL-Q25-SEAM-1` (implementer)

## 10. Risk and suggested model tier

Opus: reads of private board state from `Sim`, a `Cx` built between steps for `permanent::apply`, and an all-or-nothing
grant whose refusals must be checked before the first write; a wrong order leaves a half-granted world that
`check_world` refuses or a build-mark cache that disagrees with the fold.

## 11. Known traps

- `attempts::pending()` returns `None` for a dead or bot owner; the probe must read the stored field.
- Ready-with-pending normally lasts zero ticks (paid in the same tick, F1); `turned_in` is seen between steps only with
  full slots.
- A completed attempt's `deadline` is 0; an untimed quest's deadline is 0 while open.
- `with_cx` events go to `w.pending_events` and appear in the next step's events: the grant must leave them as it found
  them. `with_cx` requires the build-mark cache to be refreshed afterwards (`projection.marks = Default::default()`).
- `permanent::apply` for `ChefsKiss` reads the current fold: pay rewards after the cards/stacks re-fold.
- `grant_mob_stat` (via `FeedOrHuntDamage`) does nothing for a non-standing seat; `apply` already refuses `NotStanding`.
- New code in `quests/board/**` (non-test) must add no numeric literal (scan in `tests/top25_tuning.rs`).

## Notes from previous attempts
