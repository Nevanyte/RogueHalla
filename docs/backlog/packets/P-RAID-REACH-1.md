# P-RAID-REACH-1: the test bot's ranged seats fight from where the target can reach them; a fight on record names its camp

- **Serves:** not in the plan's phases: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot,
  per-elite numbers"; `docs/backlog/BALANCE_LAB_PATCH.md` section 1, the bot as a usable test player). Headless human
  seats only.
- **Implementer tier:** implementer-opus (rh-policy bot state across ticks; widened 2026-11-23 by the part-2 addendum)
- **Reviewer:** reviewer-opus
- **Integration base:** `coord/int-88` @ `14f1a628e`. Straight line; do not merge the base in.
- **D-rules touched:** D1 loose proof (RaidBot has no legacy counterpart; the leash it reads is the sim's own) · D2 n/a ·
  D5/D6/D9 none (no wire, no observation field, no `rh-sim` change).

## 1. ID and outcome

**Part 1, the reach.** A ranged seat (Wand, Bow) fights from where its target's camp cannot bring the fight to it: the
mob gives up (its leash band forbids the walk to the seat), walks home and heals to full, and the seat never moves
because `fight_step`/`Farm::act` walk only when the target is beyond `lock_range` (`exec/farm.rs:135-140`: 0.8 of a
projectile's range, 512 for a Wand; 40 for melee). After the packet, while the seat stands outside the leash band of the
target's home it steps toward the target until it stands inside the band, then fights as today. One helper decides "may I
fight from here", used by both approach branches.

**Part 2, the fight record (coordinator addendum, from the Opus merge review of integration 87).**
P-RAID-E-leave-fix2's lock arm reads too wide: `fight_step`'s `member` test accepts a regional mob with `camp: None` at any
distance, and `elite_mob` has no distance filter, so a lock on a chaser of another camp, or on another camp's elite of the
same kind, counts as this camp's fight. Across an Elite-to-Elite switch that brings back "one time-out is one give-up"
(P-RAID-E-leave-fix): `give_up(A)` bans A (count 1), the arbiter picks B, B's first act holds a lock on A's add (now
unlabelled), `fight_since` is set, and the next `watch_goal` (which sees the A-to-B switch) bans A again (count 2, a 40 s
ban at `ban_growth` 2). The same arm can credit another seat's kill of B to this seat.

### Where the scout's facts and the brief's premises were wrong or incomplete (verified)

- **The traced seed-7 seat shoots two elite camps, not one.** From (4423, 525) a Wand is inside `lock_range` (512) of camp
  11's Executioner (home (4848, 720), 467 u: confirmed) and of **camp 50's Gaoler** (home (4304, 752), 256 u). Camp 50 is a
  tiny camp: `radius 96`, `area 11` territory cells, one spot, so its leash reach is `min(96, 768) = 96` less the margin 24
  = **72 u**, on 11 cells. A seat there is out of reach of the Gaoler for good. The lab trace of seed 7 (Wand, 6000 ticks)
  records two attempts at camp 50 from exactly (4423, 525) and one at camp 11 from there, none killed. In an isolated live
  scene the same seat shoots **camp 50's Gaoler** (187 hits in 2 400 ticks, the Gaoler never below 0.994 of its hp, the
  seat never moves, 0 hits from inside the band) and ignores camp 11 whichever start I tried (a start 120 u from camp 11's
  home also locks camp 50's Gaoler, 435 u away). So the oracle's live scene is camp 50, not the Executioner.
- **`last_combat_at` is not set only when an idle mob wakes.** `crates/rh-sim/src/systems/mob_ai.rs:62` (`wake`) sets it,
  and so does `crates/rh-sim/src/systems/ledger.rs:1284-1290` whenever a mob deals damage to a player. A mob that keeps
  hitting its target keeps its 20 s give-up clock (`mob_ai.rs:267`) refreshed, so a fight where the mob reaches the seat
  can last as long as it likes; a mob that cannot reach (the trap) gives up 20 s after its last hit or wake.
- **`leash_ok` and the layers are reachable without any plumbing**, which the brief hoped for but the scout did not say:
  `RoomPathService::collision_map()` (`bots/paths.rs:258`) returns the `TileMap` the service was built from, whose
  `layers()` keep the public `LayerData` (camps with `radius`, `area`, `spots`, `elite_spot`, and the territory grid). So
  decision 2(a) holds without a new field in `PlanMap`, `Cx`, `CampSeen`, the wire or the observations (struct literals of
  `PlanMap`/`Cx` sit in some twenty existing tests; a new field would have broken all of them). Pinned green by
  `the_bots_public_statics_carry_the_layers_the_leash_predicate_reads`.
- **Decision 2(b) (a hand-derived distance bound) is not usable even as a fallback.** The smallest leash among the camps of
  ten lab seeds (reach less margin, from `CampDef.radius`) is **72 u** on seeds 5, 7 and 8 (a radius-96 camp; camp 50 of
  seed 7), 200 on seed 1, 316 on seed 2, 338 seed 10, 361 seed 4, 411 on seeds 3, 6 and 9. A bound under 72 u would pull
  every seat to melee distance of every elite; a bound that is not would sit outside the band of the small camps.
  The territory grid also cuts bands short (`leash_ok` requires the seat's cell and its four axis points to be on the
  camp's territory), so a distance alone is wrong for any camp. The helper reads the sim's predicate, as (a) says.
- **`MobSeen::camp` is a label, not the mob's camp** (as the scout said): the helper takes the camp index of the goal
  (`Target::Camp`), not the mob's label.

### Measured before the oracle (lab seeds 1 to 10, release, `--human-seats --max-ticks 6000`, eight `raid` seats, a
throwaway trace; first hit of a seat on a regional mob after at least 10 s without one on that camp opens a fight;
"outside" is `leash_ok(data, mob.camp, mob.home, seat, MOB_LEASH_MARGIN)` false at that hit; "killed" is the opened mob
dying (an elite fight: any elite of the camp dying) before the seat's next 10 s idle, to anyone)

| weapon | target | began outside the band | of those died | began inside | of those died |
|---|---|---|---|---|---|
| Wand | elite | 14 | 9 | 1 | 1 |
| Bow | elite | 2 | 2 | 2 | 2 |
| Sword | elite | 0 | | 18 | 15 |
| Wand | normal | 112 | 104 | 37 | 31 |
| Bow | normal | 52 | 43 | 101 | 92 |
| Sword | normal | 0 | | 18 | 12 |

- **Elite camps show the trap, normal camps do not.** Wand elite fights from outside the band: 5 of 14 ended with the elite
  alive (all on seeds 1, 2, 7: camp 50 and camp 11 of seed 7); a melee seat never fights from outside (its lock is 40 u).
  Normal camps: 93 % (Wand) and 83 % (Bow) of the fights begun outside the band end with the member dead, against 84 % and
  91 % from inside: no difference above the noise, because each member has its own home inside the camp and a normal camp's
  mobs wake in groups and chase. **The expected gain of this packet is elite fights from ranged seats; it does not claim
  farm speed.** The rule still covers both executors (decision 1).
- The coordinator's 40-seed figures (wand 486 elite attempts, 169 killed, 178 left; bow 363, 249, 54; sword 143, 60, 46) are
  taken as given; the wand's 178 "left" is what this packet is about, and a share of the bow's.
- What a seat standing inside the band does (decision 4): a seat started 52 u from camp 50's Gaoler (Wand) hit it 18 times
  and took it to 0.895 of its hp, then Sustain/the elite price handed it away and the Gaoler healed; a Bow seat did 12 hits
  and 0.967. The seat that steps closer is hit more; the existing death model and Sustain's leave decide how long it stays.
  This packet changes none of that and the oracle does not assert the Gaoler's hp (it would sit on a knife edge).

## 2. Dependencies

`coord/int-88` (P-RAID-E-leave-fix2 and P-RAID-S merged). None else.

## 3. Relevant legacy paths

Legacy has no RaidBot; the sim's own leash is the reference (read, not changed):
- `crates/rh-sim/src/systems/mob_ai.rs:35-40` `leash()`: with map layers it calls `leash_ok`; `:267` a mob gives up when its
  own position leaves the leash (margin 0) or 20 s after `last_combat_at` (`MOB_GIVE_UP_SECONDS`); `:310`, `:447` chase
  points must pass the leash with `MOB_LEASH_MARGIN`; `:62` `wake` and `ledger.rs:1290` refresh `last_combat_at`.
- `crates/rh-sim/src/mapdata/patience.rs:37-57` `leash_ok(data, camp, home, p, margin)`: reach `min(radius, MOB_LEASH_CAP)`
  when `home` is on the camp's territory, else `MOB_LEASH_RADIUS`; `p` must be within `reach - margin` of `home`, on the
  camp's territory, and (margin > 0) so must the four points `margin` away along the axes. `patience_plane` (`:60`) is the
  union over a camp's spots and what the client draws as the patience line.
- `crates/rh-sim/src/constants.rs:333-341`: `MOB_GIVE_UP_SECONDS` 20, `MOB_LEASH_RADIUS` and `MOB_LEASH_CAP` 24 tiles
  (768 u), `MOB_LEASH_MARGIN` 24. `crates/rh-sim/src/systems/mob_life.rs:696-738` and `mob_ai.rs:159-197`: a returning mob
  regenerates to full in 2 s and is set to full on reaching home.
- Bot side: `exec/farm.rs:135-140` `lock_range`; approach branches `exec/farm.rs:282-293` and `exec/elite.rs:685-698`;
  `fight_step`'s `member`/`locked`/`lock_on_camp`/`fighting` (`exec/elite.rs:~564-625`), `fight_since` (`:90`), `give_up`
  (`:~180`), `watch_goal` (`:~262-276`), `start` (`:~836`); percept `bots/raid/percept.rs:57-101` (`CampSeen`, `MobSeen`).

## 4. New paths

- `crates/rh-policy/tests/raid_reach.rs` (live oracle, Part 1)
- `crates/rh-policy/src/bots/raid/exec/elite_reach_tests.rs` (in-crate oracle, Parts 1 and 2; it needs the private fields
  `Elite::fight_since`/`giveups`, `Farm`'s `act`, and `Cx` built by hand, as `elite_leave_fix2_tests.rs` does)
- Stubs (commit `stub:`): `combat::may_fight_from` (returns `true`), the `mod reach_tests;` line in `exec/elite.rs`.

## 5. Exclusive write scope

- `crates/rh-policy/src/bots/raid/exec/combat.rs`: the helper `may_fight_from` (pinned signature below).
- `crates/rh-policy/src/bots/raid/exec/farm.rs`: the approach branch only (`Farm::act`, the `if d <= lock_range` test).
- `crates/rh-policy/src/bots/raid/exec/elite.rs`: the approach branch; and, for part 2, `fight_step`'s lock and member test,
  the fight record (`fight_since`) and `watch_goal` (and the code that reads `fight_since`: `give_up`, `start`, `track`,
  `watch_elites`, the `match self.fight_since` bound).
- `crates/rh-policy/src/bots/raid/percept.rs` only if a public static the helper needs is not exposed (it is: not needed).
- Not `exec/sustain.rs`, `exec/travel.rs`, `values.rs`, `spells.rs`, `exec/spell.rs`, the arbiter, `goal.rs`, `bot.rs`,
  `planning.rs`, or anything under `crates/rh-sim`. Existing tests and captures are read-only.

**Pinned signature** (the oracle calls it; changing it needs the oracle author):
`pub fn may_fight_from(paths: &RoomPathService, camp: u32, elite: bool, from: Vec2) -> bool` in
`exec/combat.rs`. Both approach branches call it with `&*cx.paths`, the goal's camp index and `from = cx.p.me.at`.

## 6. Contracts and references

C-INPUT (every action through `validate` + `Sim::submit`), C-OBS (nothing but the seat's own observation and the public
static map the seat was handed at `reset`: camps' radius, area, spots, territory are public static data, the client
draws the patience line from them), C-TICK (no sim change), C-RNG (no draw added; Elite/Farm draw nothing). Owner row
2026-11-22 (balance patch, test bot): headless human seats only, no live fill-bot change.

## 7. Behaviour to preserve

- **Melee seats unchanged** (lock 40 u, far inside any leash) and a ranged seat already inside the band locks and does not
  move (decision 3). A frame whose map has no layers, a camp without spots, or an unknown camp answers "may fight":
  hand-built tests on layer-less maps stay as they are.
- No price change (`values.rs` out of scope); the elite fight's bounds (120 s, bans, give-ups), Sustain's leave and the
  chest phase are untouched except as part 2 states.
- RNG draws: none added or moved. The pinned replays/digests of `raid_seats` and the determinism sweep stay as measured
  unless the new rule moves a seat (it may: a seat in a trap now walks; say so in the hand-off with the run).
- Part 2 keeps: a ranged seat locked on its own camp's elite or add from range is fighting (the leave-fix2 cases stay
  green exactly as they are).

## Decisions (coordinator, 2026-11-23) and how the code fits them

1. **A seat fights from inside its target's reach.** While the seat stands outside the leash band of the target's camp,
   being within `lock_range` is not enough: it steps toward the target (`step_toward` + `move_intent`, the existing
   approach intent) until inside the band, then locks as today. One helper, `combat::may_fight_from`, used by the approach
   branch of both executors, so the rule cannot drift. Condition for the lock: `d <= lock_range(in_hand) &&
   may_fight_from(&*cx.paths, camp, is_elite, cx.p.me.at)`.
2. **The reach comes from the sim's own predicate (option a).** `may_fight_from` reads
   `paths.collision_map().layers()` and calls `rh_sim::mapdata::patience::leash_ok(data, camp, home, from,
   MOB_LEASH_MARGIN)`: for an elite target `home` is the camp's elite spot (`spots[elite_spot]`), for a normal member any
   spot (the union, as `patience_plane`). No layers, no spots, an unknown camp: `true`. Not a hand-derived bound, so
   no guard on one is needed; the guard test instead checks the helper never says yes where `leash_ok` says no.
3. **Melee and an inside-the-band ranged seat are unchanged.** A melee seat 30 u from a mob that stands outside the band
   still locks (pinned green).
4. **A seat that steps closer takes more damage;** what it saw is above. Not tuned here.
5. **Part 2 (addendum).** (i) The fight start is stored with its camp (for example `fight_since: Option<(u32, f64)>`) and
   `watch_goal` bans the camp it left only when the fight on record is that camp's; this also closes the older item that
   a switch from elite A to elite B where the seat fights B on its first act bans A. (ii) A lock counts as this camp's
   fight only on a mob that is this camp's: labelled with this camp, or engaged within `DEFEND_U` of the seat (the existing
   rule); the bare unlabelled arm (`m.camp.is_none()`) does not count for the lock, and a lock on an elite counts only when
   that elite is this camp's (bound by the camp: for example within `CAMP_JOIN_RADIUS` of the camp's centre or of where
   its elite was last seen, not only by kind). (iii) Everything else of the lock rule stays. The oracle reads
   `Elite::fight_since` only through `is_some()`/`is_none()` and `giveups`, so the representation is the implementer's.
   The unlabelled arm of the non-lock member test (the nearest add within `FIGHT_U`) may stay: the tests do not pin it.

## 8. Acceptance checks

- `scripts/gate.sh --quick --base coord/int-88 --oracle packet/P-RAID-REACH-1` passes.
- `cargo test --locked -p rh-headless --test raid_seats`.
- A lab run, release, seeds 1 to 12, 6 000 ticks, eight raid seats, human seats (`balance_lab --human-seats --seeds 1..13
  --max-ticks 6000 --seat 0=raid ... --seat 7=raid`), base against head, reporting per weapon in hand (the report's
  `elites.by_kind_weapon`): elite attempts, killed, left; and for the whole run camps cleared, kill seconds and player
  deaths. Expected: the Wand's `left` falls, deaths may rise (decision 4).
- Existing tests that this packet touches and must keep green exactly as they are: see "Existing tests" below.

## Oracle files (read-only for implementers)

- `crates/rh-policy/tests/raid_reach.rs`
- `crates/rh-policy/src/bots/raid/exec/elite_reach_tests.rs` (created as a one-line placeholder by the `stub:` commit, its
  content is the oracle commit's)
- `docs/backlog/packets/P-RAID-REACH-1.md`

### What each test asserts, and where it stands on the base

| test | file | on the base |
|---|---|---|
| `a_wand_seat_in_the_trap_steps_into_the_band_and_hurts_the_elite` | raid_reach.rs | RED (assert: never in the band; 187 hits, 0 from inside) |
| `a_bow_seat_in_the_trap_steps_into_the_band_and_hurts_the_elite` | raid_reach.rs | RED (143 hits, 0 from inside) |
| `measure_a_seat_starting_inside_the_band` | raid_reach.rs | `#[ignore]`, measurement for the floors |
| `a_wand_seat_in_lock_range_but_outside_the_elites_band_steps_toward_it` | elite_reach_tests.rs | RED (no move) |
| `a_bow_seat_in_lock_range_but_outside_the_elites_band_steps_toward_it` | elite_reach_tests.rs | RED |
| `a_wand_seat_in_lock_range_but_outside_a_normal_camps_band_steps_toward_the_member` | elite_reach_tests.rs | RED (Farm; the shared rule, no farm-speed claim) |
| `the_helpers_reach_never_exceeds_the_sims_leash` | elite_reach_tests.rs | RED on the stub (says yes everywhere); seeds 1, 2, 3, 7, every camp, a 33 x 33 grid at 64 u |
| `a_give_up_is_not_counted_again_when_the_next_elite_goal_holds_an_unlabelled_lock` | elite_reach_tests.rs | RED (count 2) |
| `a_lock_on_an_unlabelled_regional_mob_does_not_start_a_fight` | elite_reach_tests.rs | RED |
| `a_lock_on_another_camps_elite_of_the_same_kind_does_not_start_this_camps_fight` | elite_reach_tests.rs | RED |
| `the_bots_public_statics_carry_the_layers_the_leash_predicate_reads` | elite_reach_tests.rs | guard (green) |
| `a_seat_inside_the_band_locks_and_stands`, `a_farm_seat_inside_the_band_locks_and_stands` | elite_reach_tests.rs | guards (green) |
| `a_melee_seats_approach_is_unchanged` | elite_reach_tests.rs | guard (green) |
| `a_lock_on_this_camps_own_elite_from_400_u_starts_the_fight`, `..._labelled_add_from_400_u_...` | elite_reach_tests.rs | guards (green) |

Floors and why: the live tests ask for `in_band_at <= 600` ticks (30 s; a Wand walks 256 u in a few seconds) and
`hits_in_band >= 5` (a seat that starts inside the band lands 12 to 18 hits before it leaves). They do not assert the
Gaoler's hp or a kill: the base seat is at 0.994 and a seat inside the band reaches 0.895 (Wand) and 0.967 (Bow) before
Sustain takes it away, so an hp floor would be a knife edge on a behaviour this packet does not change. Not validated
against an implementation: the 5-hit floor is read from the inside-band measurement, not from a fix; if a correct fix
fails it on a legitimate Sustain leave, that goes back to the oracle author.

## Existing tests that pin what this packet changes (all must stay green, none is edited)

- `exec/elite_leave_fix2_tests.rs` (`a_ranged_seat_locked_on_the_elite_gets_the_chest_phase` and the labelled-add case, the
  Wand seat at 400 u of camp 0 of the seed1 fixture): asserts `fight_since.is_some()` for a lock on the camp's own elite or
  labelled add, the kill record, the chest walk. Part 2 keeps these arms; the helper must not turn the seat's approach into
  a different first move there (those assert the fight record, not the move).
- `exec/elite_leave_tests.rs`, `elite_leave_fix_tests.rs` (give-up counts, bans, forgiveness): one time-out is one give-up;
  `fight_since` cleared by `give_up`/`start`. Part 2's camp-named record must leave these as they are.
- `bots/raid/fix_tests.rs`, `review_tests.rs`, `quest/*_tests.rs`, `tests/raid_hold.rs`: farm locks on melee seats 10 to 30 u
  from members on layer-less hand-built maps; the helper answers `true` there.
- `tests/raid_elite.rs` (live, Sword on the seed1 fixture, elite and chest outcomes) and `crates/rh-headless/tests/raid_seats.rs`
  (eight seats, behaviours and digests): a seat that now walks changes timing; the acceptance runs say whether anything moved.
- None of them asserts a Wand or Bow seat's approach move; no `oracle-amend:` commit is needed.

## 9. Validation commands

- `export PATH="$HOME/.cargo/bin:$PATH"`
- `cargo test --locked -p rh-policy --lib reach_tests` and `cargo test --locked --release -p rh-policy --test raid_reach`
- `cargo test --locked -p rh-policy` (the whole crate)
- `cargo test --locked -p rh-headless --test raid_seats`
- `scripts/gate.sh --quick --base coord/int-88 --oracle packet/P-RAID-REACH-1`
- Lab: `cargo run --release -p rh-headless --bin balance_lab -- --human-seats --seeds 1..13 --max-ticks 6000 --seat 0=raid
  ... --seat 7=raid` on the base and on the head; compare `elites.by_kind_weapon`, `world.camps_cleared`, kill seconds,
  `world.player_deaths`.

## 10. Risk and suggested model tier

implementer-opus, reviewer-opus. Part 2 changes the fight record's type across `elite.rs` state that every elite test
reads; part 1 touches the approach of both executors. The helper is small. Risks: (1) a seat that walks toward a Gaoler
with a 72 u band stands among its adds and may die more, which Sustain and the elite price then judge (decision 4); (2)
`leash_ok` with margin 24 is the chase rule; a seat exactly on the band's edge may flicker between approach and lock:
use the helper's answer at the seat's own position, no hysteresis is pinned; (3) the territory grid can make the band a
thin or odd shape (camp 50: 11 cells), so a seat may have no straight way into it: `step_toward` answers `None` and the
seat then stands as before (the premise test asserts a way exists in the tested scenes).

## 11. Known traps

- `MobSeen::camp` is a proximity label (480 u join radius); never use it as the mob's own camp for the leash: use the goal's
  camp index.
- A normal camp's members have different homes; the helper uses the union over spots (a seat inside any band), so a seat
  may be inside the band of one spot and out of reach of the mob at another. That is the same set the client draws.
- The elite goal can target a camp whose elite is out of sight: the helper is asked with the goal's camp and `elite = true`
  only on the elite approach; Farm passes `false`.
- `Elite::watch_goal` runs in `observe` with the goal the seat held BEFORE the arbiter's pick; `start` and `act` of the new
  goal run after it. The part-2 test drives exactly that order (observe with the old goal, `start`, `act`, then observe
  with the new goal).
- Do not edit `elite_reach_tests.rs` or `raid_reach.rs` to fit an implementation; a disagreement goes to the coordinator.

## Notes from previous attempts
