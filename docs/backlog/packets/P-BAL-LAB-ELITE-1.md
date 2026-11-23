# P-BAL-LAB-ELITE-1: the balance lab's elite and chest counters say what happened

- **Serves:** not in the plan: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot,
  per-elite numbers"; plan of record `docs/backlog/BALANCE_LAB_PATCH.md`). Fix of two integration-86 review findings (the
  merge review: later deaths booked as elite deaths; the P-RAID-E-leave review: "card picks" mixes killers, bystanders and
  other chests). Lands before the pilot measurement.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** `reviewer` (nothing in `rh-sim` or `rh-policy` changes)
- **Integration base:** `coord/int-86` @ `834cdd175`. Gate line:
  `scripts/gate.sh --quick --base coord/int-86 --oracle packet/P-BAL-LAB-ELITE-1`.
- **D-rules touched:** D1 n/a (new tooling, loose proof) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome

Until integration 86 RaidBots never fought elites on purpose; now they do and give up routinely (Sustain's leave, a 120 s
time-out, "never reached", growing per-camp bans of 20 to 300 s: `crates/rh-policy/src/bots/raid/exec/elite.rs`). The
lab's elite counters were written for a world where a fight ended in a kill or a death. After this packet:

1. Every elite attempt of a seat ends in exactly one of `killed`, `taken_by_other`, `died`, `died_elsewhere`, `left`, `open`
   (per elite kind and weapon): `attempts = killed + taken_by_other + died + died_elsewhere + left + open`.
2. A kill's seconds (first contact to the kill) are reported with samples (`kill_secs`).
3. Every elite kill has exactly one chest outcome: picked by the killer, picked only by a bystander, picked by nobody before
   the chest went, or still open at the end of the match.
4. Picks are split by reward chest kind (elite, trophy, ground) and, per augment card, by killer and bystander.
5. `world.elite_camps_cleared` is reported beside `camps_cleared`.

The schema stays `balance-lab/1`; the observer still only reads; the report is byte-identical across `--jobs`.

## 2. Dependencies

`coord/int-86` (P-RAID-E-leave merged). Independent of `balance_model.rs`: the evaluator preferring a measured elite cost
over `elite_assumptions` is a later packet; it will want `kill_secs` (sum, n) per kind, `killed / attempts` and
`died / attempts` per kind, and the chest outcome counts.

## 3. Relevant legacy paths

None: the lab is Rust-native. The oracle is the current code at the base and the scripted rules below. Lines are at
`834cdd175`.

## Established facts

**F1. What books an elite attempt today (`crates/rh-headless/src/balance_lab/observer.rs`).**
- `:273-283`: an attempt opens on the first `Event::Hit` by a seat on an elite mob (key `(seat, elite mob id)`), with the
  hit's weapon (or the active weapon), and `attempts += 1`. Only a hit the seat dealt opens it; nothing reads hits taken.
- `:376-386` (`fn death`): on any death of a seat, every attempt that seat still has in `elite_open` is `died += 1`,
  whatever killed the seat (attacker resolved at `:356-361`: `Death.by`, else the latest hit on the victim inside the
  5 s kill-credit window) and however long ago the fight was. The attempt stays in `elite_open` until the elite dies or the
  seat dies, so after a give-up a later death to a camp, a player or a hazard adds a death to the elite's `died`.
- `:389-406` (`fn mob_death`): when the elite dies, every open attempt on it ends: `killed` if `Death.by` is that seat,
  else `taken_by_other` (so a seat that left minutes ago is "taken by other"). `Death.by` is the killing blow, so one
  kill is booked for one seat only; the bots' private kill counts are not read. A kill is never counted twice.
- `:183-185` (`finish`): attempts still in `elite_open` are `open`.
- `EliteCounts` is `acc.rs:179-185`: `attempts, killed, died, taken_by_other, open`. No consumer outside the lab reads
  them: `rg '"died"|taken_by_other'` finds only the lab and its table (`table.rs:74-79`); `tests/balance_lab.rs` reads only
  the key `elites`; `balance_model.rs` does not read `elites` yet (its `P-BAL-EVAL-1` packet line 59 only lists the shape).

**F2. Chests and picks today (`observer.rs:426-512`, `acc.rs:256-285`).** Chests are read only from each seat's own
observation (`chest_rows`); `rewards.picks_by_kind` counts picks by option kind (card, boost, kill); `augments.by_card`
has `offered`, `picked`, `held_by_winner`. Nothing says which chest a pick came from, whose kill the chest belongs to, or
whether the picker was the killer. `reward_chests::Chest` is `pub(crate)` (`crates/rh-sim/src/reward_chests/mod.rs:40-60`):
the lab cannot read a chest's camp or first-pick holder from `&Sim`, only the `RewardChestView` (handle, `at`,
`expires_at`, options with `taken_by`, `first_pick` as "mine" only) and `Event::ChestTaken { chest, by, pick }`
(`events.rs:67`). An elite camp's chest is dropped by `on_mob_killed` (`mod.rs:215-272`) when the elite dies: at the
camp's chest spot (not the elite's body) for a camp elite, at the body for a campless one; a Trophy (`drop_trophy`,
`:185`) has only `Kill` options and an owner; any other chest is ground loot. **The first-pick holder is the credited
killer (the player with most damage on the camp, `:254-266`), which can differ from the `Death.by` killing blow the lab
credits (see decision 6).**

**F3. Measured premises** (release build, `coord/int-86`, RaidBots in all eight `--human-seats` seats, generated maps,
6000 ticks; today's counters). A run of one seed takes about 25 s in release (map generation is about 15 s of it).

| seeds | attempts | killed | taken_by_other | died | open | seat deaths | chest offers / ChestTaken events |
|---|---|---|---|---|---|---|---|
| 1..7 (six seeds) | 27 | 10 | 4 | 0 | 13 | 2 (1 player, 1 mob) | 9 / 10 (picks: card 8, kill 1) |

Per seed (attempts, kills): 1: 4, 2; 2: 7, 2 (2 seat deaths, 3 ChestTaken events); 3: 5, 3; 4: 0, 0; 5: 4, 2; 6: 7, 1.
Seeds 1..4 (that is seeds 1, 2, 3) give 16 attempts and 7 kills. At 2400 and 3600 ticks seeds 1..6 gave at most 2 attempts
and no kill: elite fights start after minute 3, so a short run is no use. **Today's `died` is 0 on every measured seed**:
the matches are short and RaidBots rarely die in 6000 ticks, so the inflation of finding A cannot be shown by a real run
at this length. It is pinned by the scripted cases; the real run pins what it can (decision 9).

## Decisions (made here, pinned by the oracle)

1. **The schema stays `balance-lab/1`.** Every change is an added field; `died` and `taken_by_other` get narrower meanings
   and nothing outside the lab reads them (F1). Say the changed meaning in `NOTES` and in the README (as
   P-BAL-LAB-1-fix did for `completed`). **Coordinator's to confirm**; default in force: no bump.
2. **Existing names stay.** `attempts`, `killed`, `died`, `taken_by_other`, `open`, `offered`, `picked`, `picks_by_kind`,
   `chest_taken_events`, `camps_cleared` keep their place and type. `taken_by_other` is the "killed by other" bucket (no new
   name).
3. **Engagement and the leave bound.** A seat is engaged with an elite while it deals a hit to, or takes a hit from, the
   elite or any mob of the elite's camp. `LEAVE_MS = 10_000.` (pub const in the new module): an open attempt whose last
   engagement is more than `LEAVE_MS` before the step's time ends as `left` at that step, before the step's own events are
   read. 10 s is shorter than the smallest bot ban (20 s) and longer than a normal lull in a fight (kiting, heal-up,
   walking round an add). A seat that is engaged again after that opens a new attempt (`attempts` counts tries, so tries per
   kill is `attempts / killed`). Within the bound the same attempt continues. The attempt opens on the first hit the seat
   DEALS to the elite itself (as today: weapon of that hit), not on a hit taken or on a hit to an add.
4. **Order inside one step**: expire idle attempts, then engagements, then elite deaths, then seat deaths. So a killing blow
   and the seat's own death in one step books the kill; a seat that dies in the step its last engagement fell in is
   judged as engaged.
5. **How an attempt ends** (the first that applies, per attempt):
   - the elite died with `Death.by == this seat`: `killed` (one per elite death at most);
   - the elite died otherwise (another seat, a mob, nobody) while the attempt is open (engaged within the bound):
     `taken_by_other`. An attempt that already `left` is not "taken": the elite died later and the seat was not there;
   - the seat died while the attempt is open and the resolved attacker (the lab's existing `Death.by`-or-last-hit-in-the-5-s-
     window, `credit_window_open`) is the elite, or a mob of the elite's camp (its adds; for a campless elite, the elite
     itself only): `died`;
   - the seat died while open, the attacker is anything else: `died_elsewhere` (counted apart; cheap and tells the
     evaluator the bot was in two fights);
   - idle more than `LEAVE_MS`: `left`;
   - none of these at the last step: `open` (idle at most `LEAVE_MS` at the final step; no extra close-out pass).
   The window for "the attacker is the elite or its camp" is the sim's own 5 s kill-credit window, because the lab already
   resolves a death's attacker that way (`observer.rs:356-361`); an attempt engaged within 10 s with a death resolved to the
   elite camp inside 5 s is a fight death.
6. **Kill credit is `Death.by` (the killing blow), unchanged.** The sim's first-pick holder is the player with most damage
   (F2), so a chest's "killer" for the first pick can be another seat than the lab's killer. The lab's chest outcome is
   about the seat with `Death.by` credit; the difference is stated in `NOTES`. **Coordinator's to confirm** whether the
   evaluator wants the damage leader instead (the lab cannot read it from `&Sim` today: `camp_damage` in the observer has
   it, seat damage on the camp, so it is available if wanted); default in force: killing blow.
7. **Kill seconds.** `kill_secs` per (kind, weapon) is a `SampledSum` (`sum`, `n`, `samples`): for each `killed` attempt,
   `(kill step ms - first contact ms) / 1000`, where first contact is the first engagement (dealt or taken, elite or its
   camp) of the engaged streak the kill ended (a streak is broken by an idle gap over `LEAVE_MS`; a streak that began
   before the attempt opened still counts, so a hit taken before the seat's first hit is time spent). Samples in kill order,
   folded in seed order. `kill_secs.n == killed`.
8. **Chest outcome of a kill.** Each `killed` attempt has one chest, found by the observer (how it recognises the elite's
   chest is the implementer's: first seen in the step of the elite's death or the next steps, at the camp chest spot or the
   body; a Trophy and a ground chest are not that chest). Events for the pure function: `ChestPick { elite, seat }` for the
   first pick a seat makes from it, `ChestGone { elite }` when the chest is no longer in any seat's observation or expired.
   At `ChestGone`: `chest_killer` if the kill's seat picked from it; else `chest_other` if another seat did; else
   `chest_none`. A kill whose chest never went by the last step is `chest_open`. `killed = chest_killer + chest_other +
   chest_none + chest_open`. `chest_bystander_picks` counts picks by seats other than the killer from the kills' chests
   (including chests the killer also picked from), counted at `ChestGone`. A kill with no chest at all (the sim dropped
   none) is `chest_none` once the observer is sure none appeared: say in the code how long it waits (suggest the chest's own `expires_at` when it was seen, otherwise 30 s after the kill).
9. **Reward chest kinds.** `rewards.picks_by_chest_kind: {"elite": n, "trophy": n, "ground": n}` (a `BTreeMap`; a key is
   absent when 0), the picks the observer already counts, by the chest they came from; the sum of its values equals the
   sum of `picks_by_kind`. A chest with any `kill` option is `trophy`; an elite's chest as in decision 8 is `elite`; the
   rest `ground`. Per card, `augments.by_card[]` gains `picked_by_killer` and `picked_by_bystander`: picks from an elite
   chest by the kill's seat and by another seat. `offered` and `picked` keep their meaning (all chests); per card
   `picked_by_killer + picked_by_bystander <= picked`.
10. **Elite camps.** `world.elite_camps_cleared` counts `Event::CampCleared` for camps whose mobs include an elite; it is a
    subset of `camps_cleared`, which keeps counting all camps. Added right after `camps_cleared`'s siblings
    (`camps_credited`, `camps_by_type`: position is the implementer's, key order of the report keys at top level does not
    change).
11. **New EliteCounts fields** (names pinned): `died_elsewhere, left, chest_killer, chest_other, chest_none, chest_open,
    chest_bystander_picks, kill_secs`. All are sums folded in seed order; `kill_secs` concatenates samples.
12. **The real run pins what it can.** `died` is 0 on measured seeds (F3). The real run asserts the partition, no `died`
    larger than the seat deaths whose resolved cause was `elite` or `mob` (`world.player_deaths.by_cause.elite + .mob`),
    `died + died_elsewhere <= player_deaths.total`, the one-outcome-per-kill rule, sample consistency, the pick
    arithmetic and byte identity. The inflation itself (later death after a give-up) is pinned by the scripted case.

## 4. New paths

- `crates/rh-headless/src/balance_lab/elite_fights.rs`: the pure tally (`EliteStep` and its fact types, `EliteTally`,
  `tally_elites`, `LEAVE_MS`); declared and stubbed by the `stub:` commit, filled by the implementer.
- `crates/rh-headless/tests/balance_lab_elite.rs`: the oracle.

## 5. Exclusive write scope

- `crates/rh-headless/src/balance_lab.rs` (`NOTES`, its length constant; `SCHEMA` unchanged; the report struct only if a
  block needs a new field)
- `crates/rh-headless/src/balance_lab/elite_fights.rs`, `.../observer.rs`, `.../acc.rs`, `.../table.rs`
- `crates/rh-headless/src/bin/balance_lab.rs` only if needed (expected: no)
- `tools/balance-lab/README.md` (new fields, the changed meaning of `died` and `taken_by_other`, what it cannot measure)
- **Not touched:** `balance_model.rs`, `crates/rh-headless/src/lib.rs`, `Cargo.toml`, `Cargo.lock`, `rh-sim`, `rh-policy`,
  `tools/balance-lab/inputs.json`, any existing test.

## 6. Contracts and references

C-INPUT (the observer submits nothing), C-RNG (it draws nothing), the purity test of P-BAL-LAB-1 (final state hash equals
an unobserved run). Model packet: `P-BAL-LAB-1-fix.md` (pure tally, partition, notes, byte identity).

## 7. Behaviour to preserve

- `tests/balance_lab.rs` and `tests/balance_lab_attempts.rs` stay as they are and green (purity, byte identity,
  `counters_are_consistent`, the quest tests). No existing test reads a field whose meaning changes (F1): no
  `oracle-amend` is needed.
- Existing fields keep name, type and position; new fields are added after them. `Sum` fields other than `kill_secs` of
  elites gain no `samples`.
- No wall clock in the report; maps are `BTreeMap`; field order is declaration order.
- `dealt`, quest counters, `world` camp counters and the death causes keep their numbers.

## 8. Acceptance checks

`crates/rh-headless/tests/balance_lab_elite.rs` (new). Scripted cases on hand-written `EliteStep`s (all at the stub fail
by assertion): kill then pick by the killer; kill then pick by a bystander; killer and bystander both pick (killer wins,
one bystander pick); kill, nobody picks, chest gone; kill with the chest not gone (`chest_open`); give up then die later
to a camp (`left`, `died` 0, `died_elsewhere` 0); give up, come back, kill (two attempts, one kill, seconds timed from the
second streak); die to the elite; die to the elite's adds; die to a different camp's mob while engaged
(`died_elsewhere`); a campless elite is only the elite itself; two seats on one elite (one kills, the other open is
`taken_by_other`, the one that left stays `left`); a kill's seconds start at a hit taken; the end of the match splits
`open` from `left` by the bound; the bound is exact (10 000 ms idle is still open, 10 050 is `left`).

Real run (release only): seeds 1..4 (seeds 1, 2, 3), 6000 ticks, RaidBots in all eight `--human-seats` seats, `--jobs 1`:
- the partition per kind and weapon; at least 10 attempts (measured 16) and at least 4 kills (measured 7);
- `killed = chest_killer + chest_other + chest_none + chest_open`; `kill_secs.n == killed`, samples sum to `sum`;
- `died <= by_cause.elite + by_cause.mob`, `died + died_elsewhere <= player_deaths.total`;
- `picks_by_chest_kind` sums to `picks_by_kind`; its `elite` is at least `chest_killer + chest_bystander_picks`;
  per card `picked_by_killer + picked_by_bystander <= picked`;
- `world.elite_camps_cleared <= camps_cleared`;
- byte identity of `--jobs 1` and `--jobs 4`; `notes` mention the narrower `died`.

It is marked release-only (`#[cfg_attr(debug_assertions, ignore = ...)]`: three 6000-tick seeds with map generation are
too slow in debug). Run it with `cargo test --release -p rh-headless --test balance_lab_elite`. **The gate's debug test
run therefore does not run it**: the implementer runs the release command and pastes the result; the coordinator's full
gate does not either. Flagged for the coordinator.

## 9. Validation commands

```
export PATH="$HOME/.cargo/bin:$PATH"
cargo test -p rh-headless --test balance_lab_elite
cargo test --release -p rh-headless --test balance_lab_elite
cargo test -p rh-headless --test balance_lab --test balance_lab_attempts
scripts/gate.sh --quick --base coord/int-86 --oracle packet/P-BAL-LAB-ELITE-1
cargo run --release -p rh-headless --bin balance_lab -- --human-seats --seeds 1..7 --jobs 4 --max-ticks 6000 --seat 0=raid --seat 1=raid --seat 2=raid --seat 3=raid --seat 4=raid --seat 5=raid --seat 6=raid --seat 7=raid
python3 scripts/readability_check.py
```

## 10. Risk and suggested model tier

Low to medium: tooling only, one crate. implementer-sonnet; reviewer `reviewer`. The subtle parts are recognising the
elite's chest from observations (decision 8) and the step order (decision 4).

## 11. Known traps

- Do not bump the schema; do not rename or retype an existing field.
- `Death.by` for a seat is often `None`; the attacker comes from the last hit on the seat inside the 5 s window
  (`last_hit_on_player`), as `observer.rs:356-361` does. A hit taken is `Event::Hit` with the seat as victim and `by` a mob.
- An elite's `camp` is `MobInfo.camp` (a campless elite has a negative or absent camp: check `Mob.camp` type before
  comparing).
- Observations are not sampled on the final step (`balance_lab.rs` `play_full`): a chest pick in the last step is seen via
  `Event::ChestTaken` only; the pure function has `ChestPick` for both sources, the observer chooses.
- Chest handles are per observer (`RewardChestView.handle`); `Event::ChestTaken.chest` is the entity id. Join through the
  position and timing, not by comparing the two numbers.
- Do not read `last_hit_on_player` after the seat's death step to judge a later attempt: judge when the death event comes.
- The tally is global (all seats in one call), because a chest outcome needs the killer and the bystanders.

## Oracle files (read-only for implementers)

- `crates/rh-headless/tests/balance_lab_elite.rs`
- `docs/backlog/packets/P-BAL-LAB-ELITE-1.md`

## Existing tests that pin what this packet changes

None pin `died`, `taken_by_other` or `picks_by_kind` by value. `tests/balance_lab.rs` reads the top-level key `elites`
(the key-order test) and stays green; `tests/balance_lab_attempts.rs` is about quests. `table.rs` prints the elite columns;
update it for the new buckets (not pinned). Nothing needs an `oracle-amend`.

## NOT ESTABLISHED

- Real-run evidence that `died` is no longer inflated: none (0 deaths to elites at 6000 ticks); only the scripted case.
- The first-pick holder (most damage) versus the killing blow (decision 6).
- Whether a seat can be hit by the elite's camp without the lab seeing the hit (area or status damage with `by` unset): such
  a death reads `died_elsewhere` or hazard.
- The elite chest recognised from observations: a ground chest near the camp chest spot at the same moment could be
  mistaken for it; the 6000-tick runs have few chests (about 9 offers over six seeds).
- RaidBot behaviour on generated maps, three seeds: a plumbing proof, not a balance number.

## Notes from previous attempts

None.

## Coordinator's answers (2026-11-23)

- **No schema bump:** the report stays `balance-lab/1`; `NOTES` and the README say that `died` and
  `taken_by_other` are narrower than before.
- **The chest's "killer" is the killing blow,** as decided here, and `NOTES` says so and says that the sim gives
  the first pick to the seat with the most damage, which may be another seat. If the pilot shows the two differ
  often, P-BAL-LAB-2 adds the damage leader beside it; nothing here needs to.
- **The two release-only tests** are part of acceptance: the implementer pastes the result of
  `cargo test --release --locked -p rh-headless --test balance_lab_elite`, and the reviewer runs it again. The
  coordinator's full gate does not run them (its debug run skips them, as it skips the other release-only lab
  tests).
- **Recognising an elite's chest** is the implementer's design within the write scope; it says in the hand-off how
  it tells an elite's chest from a ground chest near the camp, and what would fool it.
