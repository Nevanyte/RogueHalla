# P-BAL-LAB-1-fix: the balance lab reads quest outcomes from the sim's attempt probe

- **Serves:** not in the plan: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot,
  per-elite numbers"; plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 2). A fix of reviewer-opus's note on
  P-BAL-LAB-1 (`docs/MIGRATION.md`, integration 82, "Review notes, each in a fix packet").
- **Implementer tier:** implementer-sonnet
- **Reviewer:** `reviewer` (nothing in `rh-sim` changes)
- **Integration base:** `coord/int-84` @ `1e906d8bf` (holds `Sim::quest_attempts` and `Sim::gates_locked`). Gate line:
  `scripts/gate.sh --quick --base coord/int-84 --oracle packet/P-BAL-LAB-1-fix`.
- **D-rules touched:** D1 n/a (new tooling, no legacy counterpart; loose proof) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome

`balance_lab` measures quests from each seat's own observation. A completed offer leaves the seat's journal and
`owner_completed` is never set (`crates/rh-sim/src/quest_view/board.rs:17-22`, `:118`), so every completion was booked as
abandoned and only `dealt` was trustworthy. After this packet the lab probes `Sim::quest_attempts(owner)` for every human
seat after every step and reports, per quest: dealt, accepted, attempts, completed, ended by timer, ended by the seat's
death, ended otherwise (unknown), still open, turned in and never paid, and the time to complete (`complete_secs` with `sum`,
`n` and `samples`, the seconds of each completion, folded in seed order). The report says in `notes` what it cannot measure.
The observer still only reads; the report is still byte-identical across `--jobs`.

Also in this packet (minor items of the P-BAL-LAB-1 merge review): the death attribution from the last hit gets the sim's
kill-credit window, and the README gains the RaidBot command line.

## 2. Dependencies

`coord/int-84` (P-BAL-PROBE-1 merged). Independent of P-BAL-EVAL-1, which reads the same report (see traps: the schema).

## 3. Relevant legacy paths

None: the Top 25 board and the lab are Rust-native. The oracle is the current code at the base and the scripted rules in
section 8. Lines below are at `1e906d8bf`.

## Established facts

Verified against the code (the implementer re-verifies a line before relying on it).

**F1. What the lab does today (`crates/rh-headless/src/balance_lab/`).**
- `observer.rs:363-418` `quest_rows` reads `obs.quests.rows` per human seat per sampled step; `dealt` is counted when a row
  handle is first seen (`:378-380`); attempts, completion and the end causes come from `owner_accepted`, `owner_completed`
  and the row going missing (`:389-416`, `end_attempt` `:519-528`). `owner_completed` is always false, so `completed` is 0 and
  a completion appears as a row that vanished: booked `abandoned` (or `ended_timer`).
- Observations are not read on the step that ends the match (`balance_lab.rs` `play_full`: `observer.step(.., !p.terminated)`;
  `observer.rs:97-121`). The probe has no such limit (it reads `&Sim`), so it is read on every step including the last.
- `QuestCounts` (`acc.rs:185-207`): `dealt, accepted, attempts, completed, ended_timer, ended_death, abandoned,
  complete_secs: Sum{sum,n}`; `Quests.accepted_by_minute` counts first acceptances by `tick / 1200` (`acc.rs:210-216`).
  `Sum` is also used by `kill_secs`, `heal_secs`, `walk_secs` (these must not gain `samples`).
- `NOTES` (`balance_lab.rs`, fourth entry "Quest end causes are heuristic ...") describes the old heuristic and must be
  replaced.
- Death attribution (`observer.rs:298`): `by.or_else(|| self.last_hit_on_player.get(&id).copied().flatten())` has no time
  window; `last_hit_on_player` (`:63`, `:187`) stores no time.

**F2. What the probe returns (`crates/rh-sim/src/sim.rs:34-52`, `:354-364`, `quests/board/attempts.rs:254-275`).**
`QuestAttemptProbe { offer, quest: QuestId, accepted, ready, turned_in, completed, deadline, progress }`, in storage (accept)
order. `Sim::quest_attempts` is empty without the board, for a bot seat and for an unknown id (`board(w)` is `None`, or no
stored attempt of that owner). `QuestId::definition().id` is the def id string the report keys on
(`quests/board/catalog.rs:171-182`, `:200`); the view's `QuestRow.def` is the same string (`quest_view/board.rs:96`:
`def.id.to_owned()`). Verify with the real run that the two keys agree (`dealt` and the new fields must land on the same
`by_quest` entry).
- `accepted` is `!completed` for a stored attempt (`attempts.rs:216-218`): it stays true while the attempt is ready and while
  it is turned in. It is not "open".
- `turned_in` is `pending.is_some()` (`:243-246`, `:254` comment): the objective is met at the endpoint and the reward waits
  (full spell slots, or a refused permanent). `completed` is the paid row, kept for the whole match; the departure pass keeps
  completed rows (`:532`).
- `deadline` is 0 with no route timer, once turned in and once completed (`:190` sets 0 when finishing). While
  `gates_locked` the pass pushes every timed deadline back by one tick instead of running the attempt (`:493-513`), and the
  expiry test is `!locked && deadline > 0 && deadline <= server_time` (`:495`).
- An attempt is dropped (`interactions::abandon`, `:496-499`) when the seat is no longer `owner_allowed`
  (`!bot && standing`, `:212-214`, so on death) or its deadline passed. **A turned-in (`pending`) attempt and a completed one
  are skipped by that pass (`:488-491`): a seat that dies after turning in keeps the row.** A departure drops open and
  pending attempts (`departed`, `:532`). A manual abandon removes the row too (`:172`). A retry re-creates the row with the
  same `offer` id and no counter (`start`, `:161-168`).
- Probing less often than every step can miss a short attempt entirely; probe every step. Dwell quests report `progress` in
  whole seconds (`:228`). The state cannot tell timer, death, abandon and departure apart once the row is gone.
- The death pass can run in the step after the `Event::Death` (the board pass order vs the damage pass is the implementer's
  to check on a real seed): the tally accepts a death flag from the step the row vanishes or the step before.

**F3. Measured premises (debug build, this machine, `coord/int-84`, scratch probe over `match_setup` with `human_seats: true`
and `--seat 0=raid ... --seat 7=raid`, max 2400 ticks; all three matches ran to the tick limit).** Completion counted as a row
that is `completed` at some step; time as first-seen to first `turned_in || completed`, 20 ticks a second:

| seed | attempts rows | completed | sample seconds |
|---|---|---|---|
| 1 | 12 | 12 | 6.85 to 68.1 |
| 2 | 16 | 14 | 8.45 to 58.0 |
| 3 | 20 | 18 | 13.7 to 72.2 |

Seeds 1..3 together took about 58 s to play in a debug build with the probe. The oracle runs seeds 1..3 (two matches, about
two thirds of that) and floors completions at 10 (measured 26). Offers seen completing: three or four per seed, several
seats completing the same offer.

## Decisions (made here, pinned by the oracle)

1. **Schema stays `balance-lab/1`.** The evaluator's oracle on `packet/P-BAL-EVAL-1` requires a schema that starts with
   `balance-lab/1` and asserts a `balance-lab/2` report is an `Err` (its test `unmeasured_is_not_a_verdict`, case (e)); it
   builds reports by hand with `completed`, `abandoned`, `complete_secs {sum, n}`, and reads `complete_secs.samples` only when
   present. A bump would break it. The additions are optional fields; the changed meaning of `completed` and `abandoned` is
   stated in `notes` and in the README. Existing report consumers: only the evaluator (`rg 'balance-lab/'` finds the lab, its
   test and the README). **Coordinator's to confirm**: default in force is "no bump".
2. **Field names stay.** The existing `counters_are_consistent` test reads `attempts`, `completed`, `ended_timer`,
   `ended_death` and `abandoned` (and `n()` panics when one is missing), so they cannot be renamed here. `abandoned` is kept as
   the "ended otherwise (unknown)" bucket: abandon, departure and anything the state cannot tell apart; the README says so.
   New fields: `open`, `turned_in_unpaid` (and `samples` inside `complete_secs`). **Coordinator's to confirm**: renaming
   `abandoned` to `unknown` needs an `oracle-amend` of that test and the evaluator's reader; default in force is no rename.
3. **The partition is on `attempts`, not `accepted`.** `attempts` counts every attempt (a retry of the same offer is a new
   one); `accepted` stays the number of distinct (seat, offer) first acceptances, as it is today (`accepted <= dealt` is an
   existing test). Per quest: `attempts = completed + ended_timer + ended_death + abandoned + open + turned_in_unpaid`.
4. **Where a completion time ends: at the first step the row is `turned_in || completed`**, not at payment. Reason: the
   seconds are how long the seat's play took (accept, route, objective, walk to the endpoint); the payment step can wait on the
   seat's own choice when spell slots are full, which is not quest difficulty. The time runs from the step the row first
   appears (the accept). A paid row never seen turned in (the usual one-step case) ends at its first step. Counting the
   completion itself is by `completed`, so a turn-in never paid is no completion and no sample.
5. **An attempt is the pair (offer, first-seen step), found by presence**: a row that is absent at one step and present at a
   later step is a new attempt of the same offer. Detecting a retry from a reset deadline is not needed with every-step
   probing (a retry needs the row gone first); a retry within one step is not distinguished (listed in NOT ESTABLISHED).
6. **How a vanished row ends** (evaluated at the step it is first absent, with its last probed row, in this order):
   timer if `!locked`, `deadline > 0` and `deadline <= now_ms` (the existing lab's order: timer wins over a same-step death,
   `observer.rs:519-526`; `<=` as the sim's own test); else death if the seat died at this step or the step before; else
   unknown. While locked nothing is a timer (the sim pushes deadlines back), `deadline == 0` is never a timer.
7. **Ends still standing at the last read:** `completed` rows are completions; rows with `turned_in && !completed` are
   `turned_in_unpaid`; the remaining accepted rows (ready or not) are `open`. A `turned_in` row that never completes is in
   `turned_in_unpaid`, never lost (the paired-mode case: a seat holding a granted reward accepts the quest that pays it).
8. **`samples` order and folding:** per seat in seat order, each seat's completions in the order they happened; seats of one
   seed in seat order; seeds folded in seed order (`Acc::fold_in` concatenates `samples`). `sum` is the f64 sum of the same
   samples folded the same way. Seconds are `(now_ms_at_turn_in - now_ms_at_first_seen) / 1000`, so `samples` are multiples of
   0.05. Only `complete_secs` of a quest carries `samples`; `kill_secs`, `heal_secs`, `walk_secs` keep `{sum, n}`.
9. **Kill-credit window:** the fallback attribution takes the last hit on the victim only if
   `death_at_ms - hit_at_ms <= KILL_CREDIT_SECONDS * 1000` (`rh_sim::constants::KILL_CREDIT_SECONDS`, 5 s; the sim's test is
   `at - when > KILL_CREDIT_SECONDS * 1000.0` means out, `systems/credit.rs:24-26`). Oracle: `credit_window_open`. A hit older
   than the window leaves `attempt = None`, so the death is booked `hazard` as an unattributed death already is.

## 4. New paths

- `crates/rh-headless/src/balance_lab/attempts.rs`: the pure tally (`ProbeRow`, `StepRead`, `AttemptTally`, `tally_seat`);
  declared and stubbed by the `stub:` commit, filled by the implementer.
- `crates/rh-headless/tests/balance_lab_attempts.rs`: the oracle.

## 5. Exclusive write scope

- `crates/rh-headless/src/balance_lab.rs` (`mod attempts` is already declared by the stub; `SCHEMA` unchanged; `NOTES`; the
  stubbed `credit_window_open`; the probe is read in `play_full` or the observer)
- `crates/rh-headless/src/balance_lab/attempts.rs`, `.../observer.rs`, `.../acc.rs`, `.../table.rs`
- `crates/rh-headless/src/bin/balance_lab.rs` only if a doc line needs it (expected: no change)
- `tools/balance-lab/README.md` (the RaidBot command line, the new fields and the changed meaning of `completed` and
  `abandoned`, what the lab cannot measure)
- **Not touched:** `crates/rh-headless/src/lib.rs` (the lab is already `pub mod balance_lab;`, and P-BAL-EVAL-1 edits that
  file in parallel), `crates/rh-headless/Cargo.toml`, `Cargo.lock`, anything in `rh-sim`, `rh-policy`, `parity/`.

## 6. Contracts and references

C-INPUT (the observer submits nothing), C-RNG (it draws nothing), the purity test of P-BAL-LAB-1 (final state hash equals
an unobserved run), C-TICK untouched. Plan: `docs/backlog/BALANCE_LAB_PATCH.md` section 2. Probes: `docs/backlog/packets/P-BAL-PROBE-1.md`.

## 7. Behaviour to preserve

- `tests/balance_lab.rs` stays as it is and green: purity (`the_observer_changes_nothing...`), byte identity across jobs,
  the schema key order, `counters_are_consistent` (`completed + ended_timer + ended_death + abandoned <= attempts`,
  `accepted <= dealt`, `accepted <= attempts`), `dealt` of human seats at least 24 over seeds 1..4, bot seats dealt 0.
- `dealt` still counts a row handle's first sighting in the seat's observation; `accepted_by_minute` still counts first
  acceptances by `tick / 1200` (from `first_accept_ticks`).
- The report holds no wall clock; maps are `BTreeMap`; field order is declaration order, `complete_secs` serializes as
  `{"sum":..,"n":..,"samples":[..]}`.
- Without `--human-seats` the quests block stays zero (bot seats return an empty probe).

## 8. Acceptance checks

`crates/rh-headless/tests/balance_lab_attempts.rs` (new). Scripted cases on hand-written rows:

1. `a_completion_is_timed_from_the_accept_to_the_turn_in_and_counted_once`: accept at 10 s, turned in at 40 s, paid at 41 s,
   paid row persists: one attempt, one completion, sample 30.0, partition holds.
2. `a_completion_in_one_step_and_a_row_already_paid_at_the_first_read`.
3. `a_turn_in_that_is_never_paid_has_its_own_bucket`: `turned_in_unpaid` 1, not open, no sample.
4. `an_attempt_whose_row_vanished_after_its_deadline_is_a_timer_end`: deadline passed or equal; also with a same-step death.
5. `a_seat_that_died_before_the_deadline_is_a_death_end`.
6. `a_vanished_row_nothing_explains_is_unknown_and_an_untimed_row_never_expires`.
7. `nothing_expires_while_the_gates_are_locked`: vanishing at a locked step is unknown; locked rows stay open.
8. `a_retry_on_the_same_offer_is_a_second_attempt_and_one_acceptance`: attempts 2, accepted 1, timed from the retry's own
   first step.
9. `a_death_one_step_before_the_row_vanishes_still_ends_it_by_death` (and two steps is not; a turned-in row of a dead seat
   stays `turned_in_unpaid`).
10. `attempts_still_accepted_at_the_end_are_open_and_a_ready_one_is_not_a_completion`, `a_seat_with_no_rows_has_no_tally`.
11. `the_death_attribution_window_is_the_sims_kill_credit_window` (`credit_window_open`: 5000 ms inside, 5050 out).

Real run (RaidBots in all eight human seats, seeds 1..3, 2400 ticks):

12. `raid_bots_complete_quests_and_every_attempt_is_in_one_bucket`: at least 10 completions (measured 26); per quest
    `attempts = completed + ended_timer + ended_death + abandoned + open + turned_in_unpaid`; `accepted <= attempts`,
    `accepted <= dealt`; `complete_secs.samples` has `n` entries, `n == completed`, entries sum to `sum` within 1e-6, all
    non-negative; `notes` mention `quest_attempts` and no longer contain the old "else abandoned" sentence.
13. `the_report_with_samples_is_byte_identical_for_jobs_1_and_4`: the report contains `"samples":[` and is byte-identical.

These run in a debug build (about a minute for the two matches; the report of `--jobs 1` is cached between the two tests).

Not done by the implementer: no new test file, no edit of the files under "Oracle files". The README line and the
`NOTES` text are checked by reading in review (not pinned).

## 9. Validation commands

```
export PATH="$HOME/.cargo/bin:$PATH"
cargo test -p rh-headless --test balance_lab_attempts
cargo test -p rh-headless --test balance_lab
scripts/gate.sh --quick --base coord/int-84 --oracle packet/P-BAL-LAB-1-fix
cargo run --release -p rh-headless --bin balance_lab -- --human-seats --seeds 1..3 --max-ticks 2400 --seat 0=raid --seat 1=raid --seat 2=raid --seat 3=raid --seat 4=raid --seat 5=raid --seat 6=raid --seat 7=raid
python3 scripts/readability_check.py
```

## 10. Risk and suggested model tier

Low: tooling only, one crate, no sim change. implementer-sonnet; reviewer `reviewer`. The one subtle point is the death
step lag (F2, last bullet) and the order of bucket tests (decision 6).

## 11. Known traps

- Do not bump the schema (decision 1): the evaluator's oracle refuses `balance-lab/2`.
- Do not rename `abandoned` or drop `attempts` (decision 2): an existing test reads both.
- `accepted` in a probe row is not "open": it is true while ready and while turned in. Open = accepted, not turned in, not
  completed.
- A completed row persists for the whole match and shows `accepted: false`; a naive "first seen" would restart nothing, but a
  naive "row present means open" would count completions as open.
- The probe must be read on the step that ends the match too (observations are not).
- Probing happens after the step and after the events are read: `died` is from this step's `Event::Death` for the seat id.
- `Sim::quest_attempts` allocates: read it only for seats that are `!bot` (the report's `seating`), and build `StepRead`
  rows without cloning the whole sim. Keep the cost small; a debug match must stay near the measured time.
- `samples` must not appear on the other `Sum` fields, and `Sum::fold_in` must keep seed-order concatenation.
- The old notes list has a fixed length constant (`NOTES: [&str; 6]`); adjust it when replacing the note.
- Do not call `Sim::quest_attempts` in a way that mutates or allocates inside the sim (it is `&self`); the purity test
  compares state hashes.

## Oracle files (read-only for implementers)

- `crates/rh-headless/tests/balance_lab_attempts.rs`
- `docs/backlog/packets/P-BAL-LAB-1-fix.md`

## Existing tests that pin what this packet changes

- `crates/rh-headless/tests/balance_lab.rs::counters_are_consistent`: asserts `completed + ended_timer + ended_death +
  abandoned <= attempts`, `accepted <= dealt`, `accepted <= attempts` on seeds 1..4 human seats. Compatible (decisions 2, 3).
- `...::the_observer_changes_nothing_the_watched_match_ends_in_the_unwatched_state_hash`, `the_report_bytes_do_not_depend_on_jobs`,
  `the_report_has_its_schema_key_order_hash_and_per_seed_records` (reads `SCHEMA`, key order), `human_seats_can_be_dealt...`:
  stay green. No conflict; nothing needs an `oracle-amend`.
- `packet/P-BAL-EVAL-1` (unmerged): hand-built reports with `balance-lab/1`; compatible with decision 1.

## NOT ESTABLISHED

- Grants (paired mode, trusted-build rewards) are not handled here: such a seat can accept the quest that pays its reward and
  the attempt then stays `turned_in` and never `completed`; it lands in `turned_in_unpaid`, which is the only handling.
- A retry inside one step (abandon and re-accept before the next read) is indistinguishable from continuing the attempt.
- Departure, manual abandon and any unexplained loss are one bucket (`abandoned`, "unknown"); the report cannot split them.
- A death flag only from the step the row vanishes or the step before: a death two or more steps before the drop reads as
  unknown. Whether the board pass ever lags the death by more than one step is not established.
- Timer versus death in one step is booked as timer (old order); no evidence which is more common.
- Completion times come from RaidBots' play on generated maps, 2400 ticks, three seeds measured: a plumbing proof, not a
  balance number.
- The `notes` and README wording are not pinned by a test.

## Coordinator's answers (2026-11-23)

The three defaults stand. 1: no schema bump; the report stays `balance-lab/1`, and the changed meaning of
`completed` and `abandoned` is stated in `notes` and the README. 2: the name `abandoned` stays and means "ended
otherwise (unknown)"; no `unknown` field is added. 3: the write scope leaves out `crates/rh-headless/src/lib.rs` and
`Cargo.toml` (P-BAL-EVAL-1 edits beside this packet). The paired mode's granted rewards are P-BAL-LAB-2's: that
packet keeps a granted seat off the offer that pays its grant, or leaves that offer out of the arm's statistics.

## Notes from previous attempts

None.
