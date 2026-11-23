# P-BAL-LAB-TIME-1: the balance lab reports where each seat's time goes, and one row per seat

- **Serves:** not in the plan: the owner-approved balance patch (`docs/backlog/BALANCE_LAB_PATCH.md`, section 2, the lab).
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `coord/int-88` @ `40e8d7b1b` (integration 88 preview; holds P-BAL-LAB-ELITE-1 and its fixes)
- **D-rules touched:** D1 n/a (tooling; loose proof, owner direction 2026-11-10) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome

The 40-seed pilot (`docs/evidence/balance-patch-2026-11/pilot/README.md`, on `feature/unity-rework`) measured 0.63 camps
per player-minute against the workbook's 2.9, and every quest verdict moves with that number. The lab reports 29.9 s from
first hit to clear (including time away healing, and 483 elite camps of 2,414), 5.0 s of heal-up and 7.8 s of walk:
under half of a seat's minute. The pilot also showed one seat's outcome varies more between matches than any effect the
patch looks for, and the report has no per-seat rows.

When this lands the report says where every seat-step went (nine buckets), split by match-minute band, and has one row
per seed and seat. The identities in decision 3 hold on every run. No game behaviour changes.

## 2. Dependencies

P-BAL-LAB-ELITE-1 and its fixes (in the base). Not in this packet: grants inside a match, the evaluator, anything under
`crates/rh-sim` or `crates/rh-policy`. The gate's release-only list (`scripts/gate.sh:161`, protected) needs
`--test balance_lab_time`: the coordinator adds it on merge (the implementer cannot).

## 3. Relevant legacy paths

None: lab tooling over the Rust sim; no legacy counterpart. Facts read in the lab (base `40e8d7b1b`):
- `crates/rh-headless/src/balance_lab.rs:21` `SCHEMA` stays `balance-lab/1`; `:310` `NOTES` is `[&str; 8]`; `:295-308`
  `Report` (declaration order is key order); `:239-246` the run loop calls `observer.step` once per `env.advance`, so the
  report's `seeds[].ticks` equals the steps one seat has.
- `balance_lab/observer.rs:141` `Observer::step(&Sim, events, sample)`; `:371` `hit` (sees every `Event::Hit`: victim and
  `by`, mobs are known by `self.mobs` with `camp` and `elite`; `camp_of` is negative for a mob with no camp);
  `:441` `camp_cleared` (credits the seat with most damage; `w.camps_credited += 1`, `w.kill_secs.add`; the camp is elite
  when `self.elite_camps` holds it); `:499` `death` (`w.player_deaths.total`); `:545` `mob_death` (`EliteDeath.killer`,
  the credited seat: elite kills); `:242` `probe_quests` (the `Sim::quest_attempts` rows per human seat; open attempt =
  `accepted && !turned_in && !completed`; completions are counted in `balance_lab/attempts.rs` `SeatTally`);
  `:661` `chest_rows` / `chest_picks.rs` `tally_chests` (named picks per seat); `:703` `minute_sample` (farm stacks of
  standing players at ticks 5, 8, 12 minutes, the sim's `p.farm.count(t)` over `farm_types`).
- `rh-sim` `state.rs:781` `Player::standing()` (`Alive`, not down, not resolved): the lab's `alive`. `constants.rs:80`
  `PLAYER_SPEED` 170 u/s, so one full-speed step is 8.5 u. `content.rs:309` `attack_interval` is
  `max(floor, 1 / (rate * (1 + boost) * as_mult))`, floors 0.2 to 0.35 s.
- The existing `MINUTE` is `tick / 1200` (`observer.rs:17`): the lobby counts as minutes too; the bands use the same clock.

## 4. New paths

- `crates/rh-headless/src/balance_lab/time_budget.rs` (stub committed: types, constants and a `TimeTally` that counts nothing).
- `crates/rh-headless/tests/balance_lab_time.rs` (oracle).

## 5. Exclusive write scope

- `crates/rh-headless/src/balance_lab/time_budget.rs` (replace the stub bodies; keep every `pub` name and field the oracle uses)
- `crates/rh-headless/src/balance_lab/observer.rs`, `acc.rs`, `table.rs`
- `crates/rh-headless/src/balance_lab.rs` (the `Report`, the fold of seat rows, `NOTES`; the `mod` line is in the stub)
- `tools/balance-lab/README.md`

## 6. Contracts and references

C-OBS and C-RNG are untouched (the observer reads only). `crates/rh-headless/AGENTS.md`: same seed, same hash.

## 7. Behaviour to preserve

- Every existing report field, value and key order. New keys come after `augments` (`seats`) or at the end of `world`
  (`time`, `camps_credited_normal`, `kill_secs_normal`, `kill_secs_elite`). `tests/balance_lab.rs:114` pins the
  top-level order through `augments`; `seats` goes after `augments`, before `notes`.
- The purity test (`tests/balance_lab.rs:78`: the watched final state hash equals an unwatched run's), `--jobs` byte
  identity (`:102`), and the notes pins (`balance_lab_attempts.rs:376` wants `quest_attempts` and not `else abandoned`;
  `balance_lab_elite.rs:468` wants `left` and `died_elsewhere`). All stay green; none is amended.
- `SCHEMA` stays `balance-lab/1`. Memory: no per-step row kept (decision 4).

## 8. Decisions (the coordinator's; adjusted names are marked)

1. **Every seat-step lands in exactly one bucket**, first match wins: `dead`, `elite`, `camp`, `player`, `quest_moving`,
   `quest_still`, `moving`, `resting`, `idle`, as defined in the task (fight = the seat dealt or took a hit against a mob of
   an elite camp (the elite or its adds) / a mob of a normal camp / another player; a mob with no camp is `camp`; hazard
   damage is not a fight; quest = an open attempt in the sim's probe).
   - **Fight window: `FIGHT_WINDOW_STEPS = 60` (3 s).** A hit at step `s` keeps its kind in the window for steps `s` to
     `s + 59`; step `s + 60` is out (exclusive: pinned by the oracle). Each kind (elite, camp, player) has its own last-hit
     step. Why 3 s: the longest content attack interval is of the order of a second (`attack_interval`; floors 0.2 to
     0.35 s; I did not measure the faction `rate`), so 3 s bridges two or three missed or wound-up swings of one fighter
     and a ranged seat's re-aim, while it stays far under the lab's 10 s `LEAVE_MS` for an elite attempt, which is a
     give-up rule, not a time budget. It is a named constant: 3 s is a choice, not a measurement; the implementer
     reports the pilot's table at 2 s and 5 s if the reviewer asks.
   - **Death clears the windows** (my addition; the decision is silent): a step with `alive == false` is `dead`, and the
     seat's three windows are reset, so a respawned seat is not "fighting" on its first step. Pinned.
   - **Moved: `MOVED_DIST = 1.0` world units per step** (the seat's position change between the last two steps; about
     12% of a full-speed step of 8.5 u, above rounding, below any slow). A knockback or pull counts as moved (the lab
     cannot tell). The first step of a seat has no previous position: `moved = false`. The observer computes the
     distance, and the tally compares it (`dist >= MOVED_DIST` is moved), so the edge is pinned. The distance across a
     death/respawn jump is the observer's to zero (a dead step has no position change: the first alive step after
     respawn is not moved).
   - `alive` is `Player::standing()`. `hp_full` is `hp >= max_hp`.
2. **Report shape** (appended; `SCHEMA` unchanged). Names are pinned by the oracle:
   - `world.time` = `{ "steps": N, "buckets": { "<bucket>": { "all": n, "m0_5": n, "m5_11": n, "m11_plus": n }, ... } }`
     for all nine bucket names. Bands by `tick / 1200`: 0 to 4, 5 to 10, 11 and later (ticks 0 to 5999, 6000 to 13199,
     13200 on).
   - top-level `seats`: array after `augments`, one row per seed and seat, in seed order then seat order:
     `{ seed, seat, faction, weapon (the weapon in hand at the last step: the weapon at minute 5 is not added, see
     "Not established"), steps, buckets: { <bucket>: n }, camps_normal, camps_elite, deaths, quests_completed,
     elite_kills, farm_stacks: { m5, m8, m12 } (a number where the seat stood at that sample, else null), picks:
     { "<option name>": n } }`. `steps` is the seat-steps the seat had in the match (every match tick), alive or not.
   - `world.camps_credited_normal` (a number beside `camps_credited`; the elite share is the difference),
     `world.kill_secs_normal` and `world.kill_secs_elite` (`Sum`: `sum`, `n`), the existing `kill_secs` unchanged.
   - Adjusted names: the task's "farm stacks at minutes 5, 8 and 12 where alive" is `farm_stacks.{m5,m8,m12}`; "picks
     named" is the option name from `chest_picks` (what `tally_chests` already names), per seat.
3. **Identities on every run** (the oracle's heart; real report): per seat the buckets sum to `steps`; the seats' steps
   sum to 8 times the sum of `seeds[].ticks`; `world.time.buckets.*.all` sums to `world.time.steps`, equals the sum of
   the seats' buckets bucket by bucket, and each bucket's three bands sum to its `all`; per-seat `camps_normal +
   camps_elite` sum to `camps_credited`, `camps_normal` sum to `camps_credited_normal`; `deaths` sum to
   `player_deaths.total`; `quests_completed` sum to the quests' `completed`; `elite_kills` sum to the elites' `killed`;
   `kill_secs_normal.n + kill_secs_elite.n == kill_secs.n` (and the sums add).
4. **Incremental**: `TimeTally::push(&TimeStep)` and `finish()`; per seat counters and the last hit steps only, never a
   row per step. `tally_time(&[TimeStep], seats)` wraps it for the scripted tests.
5. **The observer still only reads.** No draw, no mutation, nothing hash-ordered. The elite and camp hit facts come from
   the `Event::Hit` stream the observer already walks (`hit`, `:371`): a hit *by* a seat on a mob, or on a seat *by* a mob
   (a seat is the victim: `Event::Hit.by` resolves to the mob id, the lab already counts `hits_on_players_by_mob`),
   and player versus player in either direction. An elite camp's mob is a mob whose `elite` is set or whose camp is in
   `elite_camps`, as `elite_of` (`:194`) resolves it.
6. `NOTES` and the README say what each bucket means, the window and distance, and what the buckets do not show: a seat
   walking to a quest it has not accepted is `moving`; a seat fleeing a fight is `moving` once the window passes; time at
   a chest is `idle` or `resting`; a hit by area or status damage with no attacker is no fight; time spent knocked
   about is `moving`.
7. Not in this packet: grants inside a match, the evaluator, `rh-sim`, `rh-policy`.

## Question the new table must answer (acceptance measurement; no numbers guessed here)

The pilot's 0.63 camps a player-minute is a minute of 1200 seat-steps, of which a camp takes 29.9 s of "first hit to
clear". Run the pilot's command (40 seeds, release, eight raid seats) with the new report and say where the minute goes:

- **Large `moving`, `resting`, `idle`, or `quest_*`** (alive and not fighting) point at the bot: it walks to far camps,
  heals instead of fighting, stands, or spends its time on quests (the pilot says a quest costs 10 to 190 s).
  `resting` is the cleanest bot signal: the bot's own heal-up (5.0 s a camp in the pilot).
- **Large `camp` and `elite`** with long per-camp seconds (`kill_secs_normal` far above the bench's 9.5 to 16.5 s) point
  at the game (or at ranged leash, P-RAID-REACH-1): fights that are simply long. `elite` is 483 elite camps of 2,414; it
  has its own `kill_secs_elite`, so normal-camp fight time is no longer hidden inside the 29.9 s.
- **Large `dead`** is the death cycle (265 deaths in the pilot, and the hazard after minute 11); the band split shows
  whether it is a late-game effect.
- `player` should be small for eight bots in a quiet start; if it is large the bot is hunting seats, not farming.
- Per-seat rows show whether the spread between seats of one match is in the mix (one seat mostly `resting` or
  `moving`) or in the weapon/faction.

The implementer writes the answer into the hand-off (mix of buckets of the alive seat-steps, per band), without changing
a threshold to improve it.

## 9. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-BAL-LAB-TIME-1` passes.
- Release: `cargo test --release -p rh-headless --test balance_lab_time` and `--test balance_lab` (unchanged) pass.
- The pilot's command with `--seeds 1..9 --jobs 3` (or the seeds the coordinator names) gives a table; quote it.

## Existing tests that pin what this changes

| Test | Asserts | Conflict |
|---|---|---|
| `tests/balance_lab.rs:78` observer purity | watched state hash equals an unwatched run | none; must stay |
| `tests/balance_lab.rs:102` | `--jobs 1`, 3 and 2 bytes equal | none |
| `tests/balance_lab.rs:114` | top-level key order through `augments`; schema; content hash | none: `seats` is after `augments` |
| `tests/balance_lab.rs:171` `counters_are_consistent` | camps/deaths consistency | none |
| `tests/balance_lab_attempts.rs:376`, `balance_lab_elite.rs:468` | notes contain `quest_attempts`, not `else abandoned`; `left`, `died_elsewhere` | keep those words in `NOTES` |
| `tests/balance_eval*.rs`, `balance_inputs.rs`, the pilot dry fixture | the evaluator reads `world.camps_credited`, `kill_secs`, `heal_secs`, `walk_secs`, `player_deaths`, `minutes` | unchanged fields stay |

No conflict; no `oracle-amend:` commit.

## Oracle files (read-only for implementers)

- `crates/rh-headless/tests/balance_lab_time.rs`

## 10. Risk and suggested model tier

Sonnet: lab-only, one new module plus plumbing, with a pure oracle. Hard parts: getting the hit kinds from the existing
event walk without double counting; seat rows folded in seed order (`Acc` needs a vector, not a map, that
`fold_in` appends); memory.

## 11. Known traps

- A step with several events: a seat can hit a camp mob and be hit by a player in the same step: all three flags may be
  set; the priority picks `elite` over `camp` over `player`.
- `Event::Hit` on a mob with `by: None` (area, status) is no fight; so is a hit on a seat with no attacker.
- A seat's own death step: `alive` is read after the step; the `Death` event and the dead state coincide (check the
  step on which `standing()` first goes false). The windows reset on a dead step.
- Credit: per-seat camps and elite kills must be the same seats the world counts credit to (`camp_cleared` best-damage
  seat, `EliteDeath.killer`), or the sums break. A `camps_credited` camp with no first hit still counts in `camps_*`
  but adds no `kill_secs` sample, so `kill_secs.n` can be below `camps_credited`: the oracle bounds, not equals.
- Bot seats (`bot: true`) have no quest probe: `quest_open` is false there.
- The two quest buckets use the probe's open attempt, not the observation.
- `Sim::tick()` at the first step is 1; the band uses the step's own tick.

## Oracle check on the base

- Scripted tests (12, debug): all red by assertion on the stub (e.g. `step 0 must land in exactly one bucket once, got []`).
- Release (`cargo test --release -p rh-headless --test balance_lab_time`): `test result: FAILED. 0 passed; 16 failed; 0 ignored`
  (the four real-run tests fail on `the report has no seats array` / missing `all`). Measured on the base (seeds 1..4, 6000
  ticks, eight raid human seats): 59 camps credited, 2 deaths (so `dead` is asserted `>= deaths` and `> 0`, not a count).
- Guards: none are green on the stub; the existing lab tests stay the guards for purity and `--jobs` identity.

## Not established

The weapon at minute 5 (not read). Whether 3 s is the right window. Anything about human players.

## Notes from previous attempts


## Coordinator's amendments (2026-11-23)

- **The highest-ranked open fight window wins**, whatever the newest hit was (`oracle-amend:` `2fbae7680`): two scripted
  tests expected the newest hit's bucket, against decision 1 and against the window test.
- **A seat's `elite_kills` is the sim's credit, and the world reports it.** The first identity demanded that the per-seat
  elite kills sum to the elites block's `killed`. They do not on a long run (77 against 76 on six full-length seeds):
  `killed` counts only a kill that closed an attempt its killer still had open. The per-seat credit stays the sim's
  (`Death.by` of an elite). The report gains `world.elite_kills_credited` (every elite death credited to a seat); the
  identities are `sum(seats.elite_kills) == world.elite_kills_credited` and
  `sum(elites.killed) <= world.elite_kills_credited`. `NOTES` and the README say what the difference is.
\n