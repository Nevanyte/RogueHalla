# P-RAID-E-leave: RaidBot leaves a lost elite fight by one view, bounded, with one hp held

- **Serves:** not in the plan's phases: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot,
  per-elite numbers"); the follow-up P-RAID-E named in its D2 and in reviewer-opus's three notes (2026-11-23). Headless
  human seats only; nothing live changes.
- **Implementer tier:** implementer-opus
- **Reviewer:** reviewer-opus (bot priority competition across two executors; shared pricing used by three)
- **Integration base:** `coord/int-86` @ `dd65ec18c`. **Rebased by the coordinator onto the tree that holds P-RAID-HOLD-1
  before the implementer starts**; do not merge the base in.
- **D-rules touched:** D1 loose proof (RaidBot has no legacy counterpart) · D2 n/a · D5/D6/D9 none (no sim, wire,
  observation or client change; every act goes through `validate` + `Sim::submit`).

## 1. ID and outcome

Three survival defects of the elite executor, measured in the lab by reviewer-opus (release, 8 raid seats, seeds 1 to 6,
6000 ticks; base row: 119 camps cleared, 10 deaths, 21 elite attempts, 7 kills, 8 card picks, 12 hand-aways, 2 deaths
"healing up" beside a live elite):

1. **Heal-up beside a live elite.** Sustain does not count an elite as a threat, so once the adds are dead it proposes a
   heal-up beside the elite, wins the seat from a falling Elite score, and stands still until killed.
2. **One fight, two views, and an unbounded loop.** Sustain's "the fight is lost" reads the farm model on whichever
   regional adds are nearest, while Elite prices the same fight by the bench table. 7 of 12 hand-aways were Sustain leaves
   out of fights the Elite term rated as winning; and the attempt, hand-away, 20 s ban, attempt loop has no bound.
3. **The death term jumps when the seat drinks.** `Elite::death` ignores a heal already running, Sustain's `hp_held`
   counts it: a drink below hp 0.65 lowers `held` by up to 0.4 at that moment and turns a fight negative.

After this packet: a seat beside a live elite never holds a heal-up and walks away from the elite when it leaves; while the
seat's goal is an Elite goal Sustain's "lost" decision is Elite's death term; the n-th give-up at one camp bans it longer
than the first; and Elite, Sustain and Farm price the hp they hold the same way, so a drink changes no price.

4. **The fall rule latches during a leave** (reviewer-opus on P-RAID-HOLD-1, F5): hp lost to the camp during the retreat reads
   as a fall since the stand began, so a seat that has escaped keeps walking ("hurt while healing") until hp climbs back.
   After this packet it stands once `hurt_seconds` pass with no hit and no further fall.

Not here: Sustain's hit and fall exits themselves (P-RAID-HOLD-1's, landed), fight micro (P-RAID-C2),
Farm taking the remains of an elite camp (P-RAID-E D5), a change to `Prices` itself, `arbiter.rs`, `bot.rs`.

## 2. Dependencies

Base `coord/int-86` @ `8d9b279ad` (P-RAID-E and P-RAID-HOLD-1 merged; the preview is not yet gated). **P-RAID-HOLD-1 has landed**
(`sustain.rs`: `stand_hp`, `last_at`, `heading`, `move_trail`, `hurt`, `away`, the gates-locked and hurt exits in `think`'s
stand part, `fall_share` 0.03 in `SustainPrices`). Its rule stays; this packet adds beside it. **This packet owns** in
`exec/sustain.rs`: `nearest`, `in_fight`, `view`, `fight`, the heal-up branch of `propose`, `hp_held`, and now **the stand part
of `think` and the life of `stand_hp`** (fact F5). The oracle files of HOLD-1 (`tests/raid_hold.rs`) are not touched.

What the implementer must keep of HOLD-1 (reviewer-opus's list):
- in `propose`, `!gates_locked && !self.hurt(cx)` stays beside this packet's "no living threat of any class in reach";
- the order of the hold in `think`: threat branch, healed, locked, hurt;
- the life of `stand_hp`: set by `start`, cleared in `observe` when the goal is not Sustain (changed only as F5 says);
- `move_trail` before `watch` in `observe`, both only with sustain on;
- to reconcile: `away()` scans every mob at any distance (ties by key) while `nearest()` will count elites within `threat_u`:
  ideally one threat list with one key tie-break; `hurt()` and `in_fight()` both read `hurt_at`, so if the drink bookkeeping of
  `watch` moves into `held.rs`, `hurt_at` and the hp-fall stamp must survive in `Sustain`.

## 3. Relevant legacy paths

None: RaidBot has no legacy counterpart. References: `docs/backlog/packets/P-RAID-E.md` (F1 to F4, "The candidate"),
`P-RAID-C.md`, `P-RAID-C-fix.md`.

## Established facts (verified on `coord/int-86` @ `dd65ec18c`; file:line)

### F1. Note 1: Sustain sees only regional mobs

- `Sustain::nearest` (`exec/sustain.rs:168-177`) keeps `m.class == MobClass::Regional`; `in_fight` (`:139-151`) reads
  Regional rows and accepts a camp goal of kind Farm or Sustain only; `view` (`:180-213`) starts from `nearest`, so it
  returns `None` with only the elite near. (The review's `:180-190` is `view` on its tree.)
- `propose` (`:374`) pushes the heal-up (`Sustain/None`, value `deficit x rest_value`, minutes `deficit / (regen x 60)`:
  about 0.29 ce a minute, `SustainPrices::LOCAL` `ready_hp` 0.85, `rest_value` 0.6, `regen_per_second` 0.008) whenever
  `hp < ready_hp && nearest().is_none()`: a live elite beside the seat does not stop it. The arbiter lets a pre-empting
  kind (Sustain) take a committed goal when it ranks first (`arbiter.rs:113-123`), and a committed Elite goal whose score
  turned negative ranks below any positive candidate.
- Handed a `Sustain/None` goal, `think` (`:289-323`) finds no threat and returns `Step::running(Vec::new(), "healing up")`:
  no intent. The sim's target lock outlives the goal (`drop_lock` runs only on input, `rh-sim/src/systems/targeting.rs:16`),
  and the Elite executor's `watch_goal` (`elite.rs:200-213`) bans the camp for `ban_seconds`. Nothing walks the seat away.
- Lab (reviewer's trace, seeds 1 to 6): seed 6 seat p1, `Elite/Camp(17)` committed at hp 0.51 handed to `Sustain/None` at
  t=263350 with the elite at 0.9, died t=276500 "healing up"; seed 2 seat p1 handed from `Elite/Camp(13)` to
  `Sustain/Camp(23)` at t=254950, died t=279850 "healing up".

### F2. Note 2: two views and no bound

- Elite's price of a fight: `Elite::death` (`elite.rs:381-410`): `death_prior(kind, weapon, stacks, held / elite_left)` (bench
  table `ElitePrices::kind_death`, `weapon_factor`) raised by `by_rate` (the exchange tally); the score turns negative at
  about death 0.35 (value 3.6, success `0.9 x (1 - d) x clear`, loss 6: `3.24 (1 - d) = 6 d`). Sustain's price of it:
  `Sustain::fight`/`death` (`sustain.rs:243-273`): `farm::death_chance(hp_held, duel share, members left)`, lost when
  `death >= disengage_death - disengage_lead` (0.35) and `hp < ready_hp`. The two never agree: the farm model counts the
  elite camp's regional adds as the fight and knows nothing of the elite.
- Hand-aways in the lab: 12: 7 Sustain's leave (often labelled as a neighbouring camp, `Sustain/Camp(21)` while fighting
  44), out of fights Elite rated winning (seed 3 p3: Elite 7.50, elite at 0.365, Sustain left at hp 0.51; seed 6 p3: Elite
  1.55, left at hp 0.82; seed 2 p7: 1.13), 1 the heal-up of F1, about 4 Elite's own exchange term.
- `Elite::ban` (`elite.rs:141-143`) pushes `(camp, now + ban_seconds x 1000)`; nothing counts give-ups. The exchange tally
  kept after a Sustain leave still looks favourable (lost 0.06, dealt 0.37), so the camp is proposed again when the 20 s
  ban ends and the leash heals the camp between tries. Seen: seed 6 p3 four tries at camp 44 from 113 s to 295 s with no
  kill; seed 6 p1 four tries at camp 17 ending in the F1 death.
- `ban` is called from `watch_goal` (a fight handed away while the elite stood) and from `fight_step` (fight past
  `give_up_seconds`, never reached, no way there, empty camp). All are give-ups of the camp; the count covers them all.
- Executors share no state through `Cx` (`goal.rs`): Sustain reaches Elite's view only by a pure function it calls.

### F3. Note 3: three hp-held counts

- `Sustain::hp_held` (`sustain.rs:99-111`): hp share + `potion_hp(held)` + the heals running (`hots`, the shares of the
  drinks the executor saw, times what is left of `heal_seconds` until `healing_until`).
- `Elite::death` (`elite.rs:384-387`) and `Farm::propose` (`farm.rs:153-157`): hp share + `potion_hp(held)` only. A drink
  moves a potion from "held" to "healing": `held` falls by `large_share` 0.4 (or `small_share` 0.25) the tick it is
  drunk, the heal returns it over `heal_seconds` 5 s (`SustainPrices`). Lab: seed 6 p3 scored 0.52 at hp 0.47 with one
  potion and -0.46 1.1 s later after the drink (hp 0.51, no potion), and was handed to Roam.
- `Me` (`percept.rs:22-37`) carries `potion_small`, `potion_large`, `potions_healing: u8`, `healing_until: Option<f64>`; the
  share of each running heal is **not** in the frame, only in Sustain's memory (`watch`, `sustain.rs:114-135`: a stock that
  fell by one is a drink of that size). A shared helper needs that memory in each user.

### F4. The minor list (each: one line, pinned or left out)

- `watch_goal` bans the old camp on an Elite-to-Elite switch only when the executor sees the switch (timing): **left out**.
- `watch_elites` may mark an elite dead when its last-seen point is in view but it walked out of sight (`elite.rs:179-186`):
  **left out** (a false kill costs a 3 s chest search then `Done`).
- Two elites of one kind: `elite_mob` (`elite.rs:94-103`) follows the nearest to `near`: **left out**.
- `chest_candidate` prices `death: 0` with adds near the chest (`elite.rs:457-476`): **left out**.
- Loot's own `Choose` once the ask window closes: **left out**.

### F5. The fall rule latches during a leave (on `8d9b279ad`)

- `start` sets `stand_hp = Some(hp)` when the goal begins (`sustain.rs:472-478`). For a leave that is before the retreat.
  `think` returns from the threat branch (the retreat) before `stand_hp` is touched (`:385-386` is the stand part only), so hp
  lost to the camp during the retreat is a fall of more than `fall_share` below the mark, and `hurt()` (`:173-180`) stays
  true; the stand part then answers "hurt while healing" with a move (`away`) until hp is within 0.03 of where the leave began.
  With no potion a retreat that cost 0.2 hp is about 21 s of regeneration plus the 3 s quiet spell, all walking (by reading;
  the lab showed 17 walk frames in six seeds held by the latch alone, seats there drink).
- Measured on this base (oracle test `a_leave_that_cost_hp_during_the_retreat_stands_once_the_hits_stop`): a leave adopted
  at hp 0.5, hp falling to 0.3 under hits during ten retreat frames, the add left behind, then 100 frames (5 s) with no hit:
  the step is still `Running` with a move ("move off while hurt", why "hurt while healing").
- Wanted: `stand_hp` is reset to the current hp once `hurt_at` is older than `hurt_seconds`, and cleared in the retreat branch
  ("not again until `hurt_seconds` pass with hp no lower"); a fall after that still ends the stand (HOLD-1's oracle).

## 4. New paths

- `crates/rh-policy/src/bots/raid/exec/held.rs` (stub: the shared hp-held helper, design decision 4).
- `crates/rh-policy/src/bots/raid/exec/elite_leave_tests.rs` (oracle: in-crate tests of the three notes on hand-built
  frames; a `mod` line in `elite.rs` from the stub commit).
- `crates/rh-policy/tests/raid_elite_leave.rs` (oracle: the pure-function and ban table tests visible from outside).

## 5. Exclusive write scope

- `exec/elite.rs`: `death` (use the shared hp held), `ban` and its bookkeeping (a per-camp count of give-ups, reset when the
  elite dies or the camp respawns untouched), the stubbed pure functions `ban_seconds_after` and `elite_fight_lost`, a
  `HotWatch` field and its `observe` call.
- `exec/sustain.rs`: the functions listed in section 2 only, plus `hp_held` and `watch`'s drink bookkeeping moved into the
  shared helper (the stub does not edit this file), plus the stand part of `think` and `stand_hp`'s life (F5: reset once
  `hurt_at` is older than `hurt_seconds`, cleared in the retreat branch). Keep what section 2 lists of HOLD-1.
- `exec/farm.rs`: the `hp` line of `propose` (`:153-157`), a `HotWatch` field, and an `observe` that feeds it (Farm has
  none today). Nothing else.
- `exec/held.rs` (new, the helper's home), `exec/mod.rs` (the `pub mod held;` line, from the stub).
- `values.rs`: **appended fields of `ElitePrices` and `SustainPrices` only** (`ban_growth`, `ban_max_seconds` are in the
  stub; more allowed). No field of `Prices`.
- Questions, not scope: `percept.rs` (a field for the heal shares would also fix F3: ask), `arbiter.rs`, `bot.rs`, `goal.rs`,
  `chest.rs`, `quest/`.

## 6. Contracts and references

C-INPUT (every act through the paced queue, `validate` + `Sim::submit`), C-OBS (frame facts only: the elite's kind is public
in `WireCamp`, stacks are the seat's own summary), C-TICK and determinism (no clock, no hash order, no draw in `observe`,
`propose`, `urgent` or the new functions; ordered iteration over `camps`, `mobs`, `banned` and the new count; no `HashMap`),
`values.rs` header rule (no price of an executor's own: the ban growth and cap are `ElitePrices` fields),
`crates/rh-policy/AGENTS.md`. Owner decision row "Balance patch, test bot, per-elite numbers": nothing reaches a live room.
No draw is added or moved; registry order is unchanged.

## 7. Behaviour to preserve

| Test | What it asserts | Why it holds |
|---|---|---|
| `tests/raid_elite.rs` (4) | executioner/sword/36 stacks/2 potions kills on 4 of 6 seeds, at most 1 death, card within 400 ticks; weak seats decline; `best_card`/`death_prior` pure; replay and no-draw guard with elite OFF | the favourable fight is not left (one view says winning); the pure functions are untouched |
| `tests/raid_sustain.rs` (4), `raid_sustain_fix.rs` | `farm_only()` controls; sustain leave and heal-up on normal camps; a leave does not outlive its Sustain goal | no Elite goal, no elite mob: the widened `nearest` is a no-op; the farm model still prices regular camps |
| `tests/raid_bot.rs` (4) | `farm_only()` clears the 3-member scene, replays exactly | `farm_only()` turns Elite and Sustain off |
| `raid/fix_tests.rs`, `raid/review_tests.rs`, `raid/values_tests.rs`, `exec/elite_tests.rs` | arbiter and registry on hand-built frames; value rows; the bench row, death prior and priced pick | no signature of an existing function changes |
| `crates/rh-headless/tests/raid_seats.rs` | eight RaidBots, `Prices::LOCAL`, no fault, exact replay | **not run by the quick gate: run it** |

None is amended; if one must move, stop and tell the coordinator which and why. P-RAID-HOLD-1's oracle is not touched.

## 8. Acceptance checks

- `scripts/gate.sh --quick --base <the rebased base the coordinator names> --oracle packet/P-RAID-E-leave` passes;
  `cargo test --locked -p rh-headless --test raid_seats` passes (not in the quick gate).
- `cargo test --locked -p rh-policy --lib elite_leave`, and `--test raid_elite_leave --test raid_elite --test raid_sustain
  --test raid_sustain_fix --test raid_bot` green.
- Lab, seeds 1 to 6, 6000 ticks, eight raid seats, against the base row above: elite-fight deaths and hand-aways down (no
  death "healing up" beside an elite), kills and camps cleared not down beyond noise (reviewer's base: 119 cleared, 7 kills).
  Report elite attempts by camp so a four-try loop is visible.

## 9. Validation commands

`export PATH="$HOME/.cargo/bin:$PATH"`; `cargo test --locked -p rh-policy --lib elite_leave`; `cargo test --locked -p
rh-policy --test raid_elite_leave`; lab: `cargo build --release --locked -p rh-headless --bin balance_lab`; `SEATS=(); for i in
0 1 2 3 4 5 6 7; do SEATS+=(--seat "$i=raid"); done; ./target/release/balance_lab --human-seats --seeds 1..7 --max-ticks 6000
"${SEATS[@]}"`; `python3 scripts/readability_check.py`.

## 10. Risk and suggested model tier

implementer-opus, reviewer-opus. Hard parts: (1) widening `nearest` must not change a regular camp fight (`raid_sustain*`);
(2) the leave must walk away from an elite that out-runs the seat (a Blood Saint moves faster than a melee lock follows: the
leave may fail "cornered", and the 3 s `CORNERED_MS` rule stands); (3) the shared hp held needs the drink memory in three
executors without a new percept field; (4) the ban bound must not starve a favourable camp: a kill resets the count.

## 11. Known traps

- **The sim lock outlives the goal** (`targeting.rs:16`): a leave must emit an input (the move) so the lock drops; a heal-up
  that emits nothing leaves the seat locked on the elite.
- **Two stand rules in one file**: P-RAID-HOLD-1 ends a stand on a hit; this packet removes the stand beside an elite. A seat
  with an elite in reach takes the threat branch (leave or `Done`), never the stand.
- **`Sustain::view` picks a camp by label or by nearness** (`:190-199`): with an Elite goal the camp is the goal's, and the
  elite row (`MobClass::Elite`) is the fight; do not price an elite camp by its regional adds.
- **`hots` is memory**: after a `reset` it is empty; the heals running then read as 0 to come (as today).
- **The count must survive `propose`'s `banned.retain`** (expired entries are dropped there): keep the count apart from the
  ban list. Reset it on a kill and when the camp respawns untouched (`camp.state == 0 && !mine`, as `tallies` is).
- **`ban` is called by every give-up**, not only a hand-away; the count follows `ban`.
- **A drink must not change a price in either direction**: the helper counts heals still to come only until
  `healing_until`; at the end of the heal the hp itself has risen, so the sum is continuous.

## Design decisions

1. **One view of an elite fight (Note 2a).** `elite.rs` exports `pub fn elite_fight_lost(prices: &Prices, kind: MobTypeId,
   weapon: Option<WeaponId>, stacks: f64, hp_share: f64, held: f64, elite_left: f64) -> bool`: `true` when `death_prior(kind,
   weapon, stacks, stacks_per_camp, held / elite_left)` is at least `sustain.disengage_death - sustain.disengage_lead` and
   `hp_share < sustain.ready_hp` (the two conditions `Sustain::fight` uses for a farm fight; the Elite score's break-even is
   the same 0.35). `Sustain::fight`, when `cx.goal` is `Elite/Camp(c)` and the camp has an elite, calls it with what Sustain
   sees (the camp's `elite` kind, the elite mob's `hp_share`, `summary.mob_rows` stacks, `me.weapons[slot]`, the shared hp
   held) instead of `death`. Elite's tally exchange (`by_rate`) is not reachable from Sustain and stays Elite's own term.
   `Fight::camp` is the goal's camp. For every other goal Sustain is unchanged.
2. **The ban bound (Note 2b).** `pub fn ban_seconds_after(e: &ElitePrices, giveups: u32) -> f64` = `min(ban_seconds x
   ban_growth^(giveups - 1), ban_max_seconds)` for `giveups >= 1` (1 gives `ban_seconds`). LOCAL: `ban_growth` 2.0 (20, 40, 80,
   160 s), `ban_max_seconds` 300 (a camp respawns in 180 s). `Elite::ban` counts per camp (an ordered `Vec<(u32, u32)>`) and
   uses it; a kill or an untouched respawn resets the count.
3. **Threat includes the elite (Note 1).** `nearest`, `view` and `in_fight` count `MobClass::Elite` rows within `threat_u`; for
   the Elite goal `in_fight` is true while an elite or add is in reach (`works_camp` includes `GoalKind::Elite`). The heal-up
   is not proposed while any living threat is in reach; the leave walks away from the nearest one (`away_point`).
4. **Shared hp held (Note 3).** `exec/held.rs`: `pub fn hp_held(s: &SustainPrices, hp_share: f64, large: u8, small: u8,
   healing: u8, healing_until: Option<f64>, now: f64, hots: &[f64]) -> f64` (the body of `Sustain::hp_held`), and `pub struct
   HotWatch` (`watch(&mut self, s, large, small, healing)` keeps the shares of the heals running; `shares() -> &[f64]`:
   `Sustain::watch`'s drink bookkeeping moved). Sustain, Elite and Farm each hold a `HotWatch`, feed it in `observe` and call
   `hp_held`. With sustain off the helper returns `hp_share` (as `potion_hp` returns 0).

## Oracle (loose proof) and its choices

In-crate (`exec/elite_leave_tests.rs`, module `elite::leave_tests`: hand-built frames, no sim, so each case is
deterministic and runs in milliseconds) and `tests/raid_elite_leave.rs` (the pure functions through the public path). Red
on the stub by assertion.

1. **Note 1** (frames: Holy seat, hp 0.5, an executioner camp with the elite alive 100 u away, no regional alive): Sustain
   proposes no `Sustain/None` candidate; a `Sustain/None` goal does not stand (`Done`, or a move); a `Sustain/Camp` goal moves
   the seat away from the elite. Base: heal-up proposed, `Running` with no intent.
2. **Note 2a**: `elite_fight_lost` on hand-built inputs (pure): executioner/sword/36 stacks/held 1.3/elite at 0.365 is not
   lost; avalancheTroll/sword/24 stacks/held 0.5/elite full is lost. Sustain's own `propose` in a frame with an Elite goal and
   regional adds hitting the seat makes no `Sustain/Camp` leave while the elite term says winning. **Pinned on the pure
   function plus a hand-built frame, not a lab scene**: a single-seat lab scene showed no Sustain leave (the reviewer's note on
   `raid_elite` test 1), so the frame is the cheapest scene that does. **Note 2b**: the `ban_seconds_after` table; and
   `Elite::ban` bookkeeping: after one give-up the camp is free at `ban_seconds`, after the second the ban outlasts it, and a
   long run is capped.
3. **Note 3**: `hp_held` unchanged by a drink (hp, potions minus one, one heal running with its full share to come); `HotWatch`
   sees a stock fall as a drink; Elite's and Farm's candidate `death` for one fight equal across the drink (a frame before it,
   a frame after with the heal running; each executor sees both through `observe`).
4. **Guards**: `Prices::farm_only()` proposes nothing from Elite or Sustain; existing tests unchanged.
5. **No lab sweep in the oracle**: the elite-fight death rate needs the 8-seat lab (acceptance, section 8); a single-seat sweep
   of a lost elite fight depends on a rare event and was not built.

## Oracle files (read-only for implementers)

- `crates/rh-policy/src/bots/raid/exec/elite_leave_tests.rs` (13 tests, 12 red on the stub by assertion, 1 green guard; the 12 are unchanged by P-RAID-HOLD-1, none turned green)
- `crates/rh-policy/tests/raid_elite_leave.rs` (3 tests, red on the stub by assertion)
- `docs/backlog/packets/P-RAID-E-leave.md` (this brief)

The `stub:` commit (the implementer's to replace: `exec/held.rs`, `ban_seconds_after` and `elite_fight_lost` in `exec/elite.rs`,
the two `ElitePrices` fields and their numbers in `values.rs`, the `pub mod held;` line, the `mod leave_tests` line) is not an
oracle file. No existing test or capture is amended (no `oracle-amend:` commit).

Routing: serves: the P-RAID-E follow-up (see the top) · tier: implementer-opus · reviewer: reviewer-opus · D-rules: D1 loose
proof, no D5/D6/D9 change · write scope: section 5.

Red on the stub, by test: `no_heal_up_is_proposed_with_a_live_elite_in_reach`, `a_heal_up_held_beside_a_live_elite_does_not_stand`,
`a_leave_with_only_the_elite_near_walks_away_from_it`, `the_elite_death_term_says_when_the_elite_fight_is_lost`,
`sustain_does_not_leave_a_fight_the_elite_term_rates_as_winning` (the base proposes a `Sustain/Camp(0)` leave, value 3),
`the_ban_after_n_give_ups_is_longer_than_after_one_and_capped`, `the_executor_bans_a_camp_longer_at_each_give_up`,
`a_drink_does_not_change_the_hp_held`, `the_heal_watch_reads_a_stock_that_fell_as_a_drink`,
`the_elite_candidate_does_not_change_when_the_seat_drinks` (measured on the base: death 0.321 before the drink, 0.536 after),
`the_farm_candidate_does_not_change_when_the_seat_drinks` (0.155 before, 0.431 after). Red for F5:
`a_leave_that_cost_hp_during_the_retreat_stands_once_the_hits_stop` (still walking after 5 s). Green on the base by design:
`farm_only_proposes_nothing_from_elite_or_sustain`.

## NOT ESTABLISHED

- That the elite's tally exchange (`by_rate`) agrees with Sustain's view during a fight: only the table term is shared.
- The growth 2.0 and cap 300 s are not tuned; the lab comparison sizes them.
- Deaths beside an elite in a real match: pinned on frames, measured only by the lab acceptance.
- A heal-up when the elite is beyond `threat_u` but chasing: not scened.
- The minor list of F4 is left out, not tested.

## Decisions for the coordinator (default in force)

- **D1: ban growth shape.** Default: geometric, 2.0, capped 300 s. Alternative: ban until the camp respawns after N tries.
- **D2: Sustain's elite threshold.** Default: Sustain's `disengage_death - disengage_lead` applied to Elite's death prior
  (equals Elite's score break-even, about 0.35). Alternative: a separate `ElitePrices` field.
- **D3: the drink memory.** Default: a `HotWatch` held by each of three executors. Alternative (ask first): a `Me` field of the
  running heals' shares in `percept.rs`.
- **D4: the minor list.** Default: left out (F4).

## Coordinator's answers (2026-11-23)

- **D1 yes:** the ban grows geometrically (`ban_growth` 2.0, capped at `ban_max_seconds` 300). The numbers are
  untuned; the lab comparison says whether a seat still spends minutes on one camp.
- **D2 yes:** Sustain's threshold for an elite fight is `disengage_death - disengage_lead`, the same line the Elite
  score turns negative at. No separate field.
- **D3 yes:** each of Sustain, Elite and Farm holds its own `HotWatch`. No percept change.
- **D4 yes:** the minor list stays out.
- **Order.** P-RAID-HOLD-1's implementer is working in `exec/sustain.rs` now (the stand branch of `think`, the hurt
  clock in `watch`, and the heal-up not proposed while the seat is being hurt). When it has landed, the coordinator
  re-cuts this packet's three commits in a line on the tree that holds it, and only then does this packet's
  implementer start. The implementer keeps HOLD-1's rule as it finds it and adds this packet's beside it: the
  heal-up is not proposed while a living threat of any class is in reach.
- **What the oracle leaves to the lab:** the deaths beside an elite are pinned as frames only. The acceptance lab
  row (seeds 1 to 6, 6000 ticks, against 119 camps, 10 deaths, 21 attempts, 7 kills, 8 picks, 12 hand-aways, 2
  deaths "healing up" beside an elite) carries that proof, and reviewer-opus repeats its trace.
