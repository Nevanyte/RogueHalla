# P-BAL-LAB-ELITE-1-fix: the balance lab counts a chest pick exactly once

- **Serves:** not in the plan: the owner-approved balance patch (`docs/backlog/BALANCE_LAB_PATCH.md`, 2026-11-22). Closes the two notes of the review of P-BAL-LAB-ELITE-1.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `coord/int-87` @ `c5b51f46a` (holds P-BAL-LAB-ELITE-1; the integration branch does not have it yet)
- **D-rules touched:** D1 n/a (tooling; a loose proof, owner direction 2026-11-10) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome

The lab's reward counters (`rewards.picks_by_kind`, `rewards.picks_by_chest_kind`, `augments.by_card.{offered,picked,
picked_by_killer,picked_by_bystander}`, `rewards.chests_offered`) are read by the 40-seed pilot measurement. Today
`Observer::chest_rows` keys offers and picks on the chest's *handle* (`observer.rs:647`, `:661`, `:665`, `:676`); the sim
reissues a viewer's handle when the chest's public facts change (`crates/rh-sim/src/reward_chests/observer.rs` near
`:196`, e.g. when another seat picks), so one pick is counted again under each new handle. Measured by the coordinator
on two full matches: `committed` offered 2, picked 4; `kill:attackSpeed:0.0750` offered 0, picked 1; picks seen 24
against 37 `ChestTaken` events.

**The cause is wider than a reissue (coordinator, from an Opus trace of twelve seeds).** The lab reads each seat with
`rh_sim::observe` (`crates/rh-sim/src/observe.rs:729-748`, "checked disposable preview"), which builds a fresh
`ItemObserver::new(viewer)` on every call; the chest observer's counter starts at `next: 1` (`reward_chests/observer.rs:36`).
So a viewer's handle values restart at 1 on every step and **collide across chests**: two different chests can carry the
same handle value for one seat at different times, and the second one's offers and picks are swallowed (an undercount),
besides one chest changing handle (a double count). Reference, seeds 1 to 12 at 6000 ticks, eight raid seats: keyed by
handle the lab read 24 picks, 19 chests offered, 55 card offers; keyed by chest identity (expiry plus position) 28 picks,
29 chests offered, 85 card offers; the sim pushed 28 `ChestTaken` events, so there named picks equal the events. When
this lands every pick is counted once, `picked <= offered` holds per card, and
`sum(picks_by_kind) + picks_unnamed == chest_taken_events` holds on every run. The review's second note (a zero-second
kill) becomes documented behaviour.

## 2. Dependencies

P-BAL-LAB-ELITE-1 (in the base). Nothing else.

## 3. Relevant legacy paths

None: lab tooling over the Rust sim; there is no legacy counterpart. Rust facts, verified by reading:
- `crates/rh-sim/src/reward_chests/mod.rs:330-422` (`fn choose`): a pick with `pick >= 0` marks `taken[i] = Some(who)` and
  `pickers.push(who)`; **`pick == -1` is a skip**: it pushes `who` onto `pickers` (so the seat cannot pick again), heals
  `skip_heal_share` of max hp on a shared camp chest (`owner.is_none() && camp.is_some()`), may `award_skip`, and marks
  **no option** taken. **Both paths push `Event::ChestTaken { chest, by, pick }` (`:421`)**; a refusal returns `Err` earlier
  and pushes none (`ChestRefused` instead). So a skip is one event whose option no observation can name: it lands in
  `picks_unnamed`. Two silent `return Ok(())` paths (`pick` not in 0..3 or an empty slot, `:386-391`; an owner-bound chest's
  non-kill option, `:403`) push no event.
- `crates/rh-sim/src/events.rs:67`: `ChestTaken { chest: u32, by: PlayerId, pick: i8 }`.
- `crates/rh-sim/src/reward_chests/view.rs:87-99`: `RewardChestView { handle, at, expires_at, options, picked, can_choose,
  first_pick }`; `RewardOptionView { id, taken, taken_by }`. `at` and `expires_at` do not change over a chest's life.
- `crates/rh-sim/src/reward_chests/observer.rs` near `:196`: the handle is reissued when a viewer's public facts change.
- `crates/rh-headless/src/balance_lab/observer.rs:349`: `chest_taken_events += 1` per event (already exact).

## 4. New paths

- `crates/rh-headless/src/balance_lab/chest_picks.rs` (the stub is committed; the implementer replaces `tally_chests`).
- `crates/rh-headless/tests/balance_lab_picks.rs` (oracle).

## 5. Exclusive write scope

- `crates/rh-headless/src/balance_lab/chest_picks.rs` (stub types and function; the implementer may add private items and
  fields, but keeps every `pub` name and field the oracle uses)
- `crates/rh-headless/src/balance_lab/observer.rs`, `acc.rs`, `table.rs`
- `crates/rh-headless/src/balance_lab/elite_fights.rs` (comments only)
- `crates/rh-headless/src/balance_lab.rs` (`NOTES`; the `mod` line is in the stub)
- `tools/balance-lab/README.md`

Nothing else. Nothing under `crates/rh-sim`; `SCHEMA` stays `balance-lab/1`.

## 6. Contracts and references

- C-OBS / C-RNG: the lab reads `&Sim`, events and ordinary observations only; it never mutates (the purity test of
  `tests/balance_lab.rs` stays green). `docs/backlog/packets/P-BAL-LAB-ELITE-1.md` decisions on chest recognition stay.
- `docs/OWNER_DECISIONS.md`: searched for chests, picks and the balance lab; no row decides this subject.

## 7. Behaviour to preserve

- Nothing under `crates/rh-sim` changes; state hashes and parity untouched.
- Report shape: no field is renamed or removed; `picks_unnamed` is appended after the existing `Rewards` fields (the stub
  commit adds it, always 0 until the implementer feeds it). `SCHEMA` stays `balance-lab/1`.
- Elite fight tally (`tally_elites`) and the chest watch (`ChestWatch`, `chests_gone`) behave as today: the elite chest
  outcome (`chest_killer` and friends) is already per seat, not per handle.
- Byte-identical report for `--jobs 1` and `--jobs 4` (existing test).

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-BAL-LAB-ELITE-1-fix` passes.
- `cargo test --release --locked -p rh-headless --test balance_lab_picks --test balance_lab_elite --test balance_lab` green.

### Decisions (coordinator, 2026-11-23; not open questions)

1. **A pick is one `Event::ChestTaken`.** Report invariant on every run: `sum(picks_by_kind) + picks_unnamed ==
   chest_taken_events`. `picks_unnamed` (new, appended after the `Rewards` fields) counts the events whose option the rows
   could not name, i.e. exactly these cases: a skip (`pick == -1`, see section 3); a pick whose chest no observation row
   showed (a pick on the match's last step, where `sample` is false and observations are empty; a chest that expired in
   the same step). Computed as `taken_events - named picks` (saturating); the tests also assert the identity, so a case
   that overshoots shows up.
2. **A chest is named by facts that do not change over its life:** `(expires_at.to_bits(), at.x.to_bits(), at.y.to_bits())`,
   never by handle. Two chests with the same `expires_at` at different positions are two chests. The sim has no public
   read of chests by id and none is added.
3. **Offers:** an option is offered once per (seat, chest, option index) (only while `can_choose`, as today);
   `chests_offered` counts once per (seat, chest). A handle reissue adds nothing.
4. **A pick implies an offer:** per card `picked <= offered`; an option first seen already taken by this seat counts offered
   1 and picked 1 (as the code does today).
5. **Structure:** counting is the pure `chest_picks::tally_chests(&[ChestStep]) -> ChestTally`, in the manner of
   `tally_elites`; the observer builds one `ChestStep` per step (its `ChestRow`s, with the class from `classify` and
   `seat_is_killer` from the `ChestWatch`, and the step's `ChestTaken` count) and at `finish` moves the tally into
   `Acc` (`rewards.chests_offered`, `picks_by_kind`, `picks_by_chest_kind`, `picks_unnamed`, `augments.by_card`).
   `by_card` also gets `held_by_winner` from `victory`, which stays in the observer. Note `acc.augments.by_card` entries
   created by the tally and by `victory` must merge, not overwrite. Each pick adds to `picked_by_killer` or
   `picked_by_bystander` only when its chest kind is `Elite`.
6. **Zero-second kills:** `kill_secs` stays the seat's own engagement, first contact to the kill; it may be 0 for a seat
   whose first contact is the killing blow. The coordinator amends `> 0.` to `>= 0.` in
   `tests/balance_lab_elite.rs` (near `:406`) in an `oracle-amend:` commit after this packet's oracle; the implementer
   does not touch that file. The implementer adds one sentence saying what `kill_secs` measures to `NOTES` and to the README.
6b. **`kill_secs` wording:** a zero-second sample is a seat credited with the kill whose first contact with the elite is the
   step the elite dies.
7. **Riding along (no test):** (a) `Event::Death.by` IS the sim's credited killer, the seat with the most damage on the
   mob (or a killing Smite), `crates/rh-sim/src/systems/mob_life.rs:258-282` and `:366-371`, and that seat holds the first
   pick (`reward_chests/mod.rs:254-266`): the lab's killer and the first-pick holder are always the same seat. Correct
   every place that says "killing blow" or that the first pick may go to another seat: `tools/balance-lab/README.md`
   (near lines 53 and 63-64), the `NOTES` entry in `balance_lab.rs`, the comments at `elite_fights.rs:34`, `acc.rs:194`
   and `:305`. Words only; no counter changes meaning. (b) the doc comments of `ChestWatch` (`observer.rs:34-37`) and `ChestStep.chests_gone`
   (`elite_fights.rs:50`) say a chest is gone when "in nobody's observation any more", but the code (`chests_gone`,
   `observer.rs:227`) uses `expires_at`, or the 30 s unseen wait when never seen: correct the comments. `elite_pos` falls
   back to an infinite position when an elite's last position is missing (`observer.rs:545-548`): add a comment saying
   the chest of such an elite is never matched by place and ends as `none` after the unseen wait.

### Stub signature (committed in `stub:`)

`chest_picks::{ChestKind {Elite, Trophy, Ground}, OptionRow {name, kind, taken_by_seat}, ChestRow {seat, x, y, expires_at,
can_choose, kind, seat_is_killer, options}, ChestStep {taken_events, rows}, CardPicks, ChestTally {chests_offered, cards,
picks_by_kind, picks_by_chest_kind, picks_unnamed, taken_events}, tally_chests(&[ChestStep]) -> ChestTally}`. The stub
returns an empty tally. `taken_events` of the tally is the sum of the steps'.

## Oracle files (read-only for implementers)

- `crates/rh-headless/tests/balance_lab_picks.rs`
- `docs/backlog/packets/P-BAL-LAB-ELITE-1-fix.md`

Existing tests that pin what this changes (none conflicts): `tests/balance_lab_elite.rs` asserts picks_by_chest_kind keys
and sums equal to `picks_by_kind`, `elite_picks >= chest_killer + bystander`, `picked_by_killer + picked_by_bystander <=
picked` per card (all stay true), and `samples all > 0.` near `:406` (the only line that changes: the coordinator's
`oracle-amend:`). `tests/balance_lab.rs` reads the key `elites` and the purity hash only. Searched the names
`picks_by_kind`, `chest_taken_events`, `chests_offered`, `by_card`.

## 9. Validation commands

- `export PATH="$HOME/.cargo/bin:$PATH"; cargo test --locked -p rh-headless --test balance_lab_picks` (scripted, debug)
- `cargo test --release --locked -p rh-headless --test balance_lab_picks` (adds the three real-run tests, about 1.5 min)

## 10. Risk and suggested model tier

Small and tool-only; the one care is merging two writers of `by_card` and keeping the report byte-identical across
`--jobs`. Sonnet.

## 11. Known traps

- `picks_by_kind` counted a pick only when the option's name was in `names`; keep `kind` per option, not per chest.
- A viewer's `can_choose` is false once it has picked: an option first seen already taken is offered and picked together.
- The watch (`ChestWatch.picked`, `chest_picks` of `EliteStep`) is per seat already; leave it.
- A skip shows no taken option anywhere: it can only be `picks_unnamed`.
- Observations are empty on the match's last step, so its events are unnamed.

## Notes from previous attempts
