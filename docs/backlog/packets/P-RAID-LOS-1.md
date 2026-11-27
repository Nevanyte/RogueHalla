# P-RAID-LOS-1: the test bot's ranged seats lock a camp's mob, and pick their fight point, only with line of sight to it

- **Serves:** not in the plan's phases: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot,
  per-elite numbers"; `docs/backlog/BALANCE_LAB_PATCH.md` section 1, RaidBot, the combat adapter, P-RAID-C). Headless
  human seats only. Found by two reviewer-opus reviews of integration 88.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer-opus (the bot's fight rules)
- **Integration base:** `coord/int-89` @ `7bc23f775`. Straight line; do not merge the base in.
- **D-rules touched:** D1 loose proof (RaidBot has no legacy counterpart; the wall rule is the sim's own) · D2 n/a ·
  D5/D6/D9 none (no wire, no observation field, no `rh-sim` change).

## 1. ID and outcome

A ranged RaidBot seat (Bow, Wand) locks a camp's mob from wherever it stands inside `lock_range`, and walks to a fight
point chosen inside the camp's leash band and the weapon's lock range. Neither step asks whether the seat can see the
mob. A mob behind a wall is locked all the same, the seat stands still and sends arrows or bolts into the wall.

After this packet (the brief's one behaviour change): **a ranged seat locks a camp's mob only from a point with line of
sight to it, and a fight point is chosen only among points with line of sight to the target.** With no such point in the
band the seat keeps the existing fall-back (`Reach::engage` locks from where it stands: it must not stand still for
ever; the stuck bound `STUCK_MS` and the farm's 20 s no-contact bound stay). Melee is unchanged.

### Established facts (each read in the code at `7bc23f775`)

1. **Neither decision reads line of sight.** `exec/combat.rs`:
   - `locks_from_here` (`:97-114`): `d > lock_range` is "no"; otherwise `!ranged || (mob.engaged && d <= DEFEND_U) ||
     may_fight_from(..)`. `may_fight_from` (`:67-93`) is a leash-band test (`leash_ok` margin 0 via `in_leash`, `:44-47`);
     no `los`.
   - `Reach::scan` (`:159-192`) collects floor cells `may_fight_from` accepts, cached per `(camp, elite)` (the layers
     are static); `Reach::nearest` (`:195-209`) filters `dist(q, mob_at) <= range` and takes the one nearest the seat. No `los`.
   - `Reach::engage` (`:215-257`): beyond range walk to the mob (`step_toward`, `:222-225`); in range, lock when
     `locks_from_here` (`:226-232`); else walk to `nearest(.., range - LOCK_SLACK_U)` (`:233-242`), and lock when there
     is none (`:242`), when the seat is within `2 * PLAYER_RADIUS` of the point or when the progress has not improved by
     `PROGRESS_U` (8 u) for `STUCK_MS` (1 500 ms) (`:242-257`).
2. **Callers.** `exec/farm.rs:332` (`self.reach.engage(cx, index, false, &mob, in_hand)`, after the `NO_CONTACT_MS`
   bound at `:305-309`: `Step::failed("no contact with the camp")` and a `FAILED_BAN_MS` ban) and `exec/elite.rs:722`
   (`engage(cx, index, elite_target, &mob, in_hand)`). The farm's no-contact bound is 20 s; the elite fight's only bound is
   its 120 s give-up (`elite.rs` `give_up`, as the REACH-1-fix packet records). No other caller of `locks_from_here`
   (`rg locks_from_here`: `combat.rs:97`, `:229` only). `spell.rs` and `spells.rs` use `may_fight_from` or their own
   rule: out of scope.
3. **What the executors hold.** `Cx` carries `cx.paths: RoomPathService`; `RoomPathService::los(a, b)`
   (`crates/rh-policy/src/bots/paths.rs:252-254`) is `has_line_of_sight(&self.map, a, b, MASK_SIGHT)`
   (`rh-sim/src/geometry.rs:283-285` = `!ray_blocked(..)`). `combat.rs` already reads `cx.paths` (`collision_map`,
   `camp_of`), so no new dependency.
4. **The sim's rule.** `rh-sim/src/systems/projectiles.rs:216-220`: a projectile stretch is cut at
   `raycast(map, pr.pos, b, MASK_SIGHT)` and ends with `ProjectileEnd::Wall`. The origin is the shooter's own position
   (`:95` `origin: owner.pos()`, no muzzle offset), the mask is `MASK_SIGHT` for an arrow, a bolt and the wand's splash
   alike (`:292`, `:459` use `ray_blocked(.., MASK_SIGHT)` for ball-arrow and splash tests), and a wall tests the ground
   position (`ProjectileLaunch::height`, `state.rs:844-846`). Bow and Wand shots therefore meet the same wall rule.
   **`RoomPathService::los` is the sim's rule**, the same mask on the same tile map: `raycast` and `ray_blocked` are two
   walks of the same tiles with the same mask (`geometry.rs:19`, `:132`; read only in part, see (c)). Where they could differ: (a) a projectile has a radius
   (`ARROW_RADIUS` 7 u, `constants.rs:131`) for hitting bodies, but the wall test is the centre line only, so a mob that
   stands with its centre behind a corner but its body in view can be hit by a line the bot's `los` to the mob's centre
   calls blocked (the bot's test is the stricter one there, which is allowed); (b) the shot is aimed at where the bot
   leads the mob, not at its present centre, so a mob walking out from behind a wall may be aimed through a corner: a
   streak of one or two wall ends is not the defect; (c) a ray that grazes a tile corner may be decided differently by
   `raycast` and `ray_blocked` (not measured). `Lock` intents are acted on by the seat, which aims at the target;
   `los` from the seat's position to `mob.at` is the closest bot-side test.
5. **Seen, measured.** The brief's trace (lab seed 1, a Bow seat on a Farm goal on camp 35 shooting a member it could not
   see, 8 projectiles on the wall, about 110 ticks) came out of `raid_reach_fix.rs` by its amendment `f2e5faa1d` (a
   comment there names it "an older defect of the ranged lock"). A reviewer measured 2.4% of ranged lock thinks in twelve
   lab seeds with no line of sight. The oracle below reproduces it on the base (measured in this packet, see the table).
   Not measured here: the 2.4% figure and the camp-35 scene itself (the oracle's named scene is seed 1, camp 50 start,
   Bow, where the seat is stuck at (3669, 3736) from about tick 2176 to the end of the run: 59 wall ends in a row).
6. **Why the test is behavioural.** The lock is an intent (`IntentBody::Lock`) whose effect on the sim is an `Attack`
   command; the in-crate Frame tests cannot see a wall effect, and a function-level test (`locks_from_here` with a
   wall between) would pin the function's signature rather than the behaviour. The oracle reads the effect on the sim:
   `Event::ProjectileEnded { reason: Wall }` of the seat's own projectile, the streak since the seat's last hit.
   An implementer's own in-crate unit tests (hand-built frame, wall between seat and mob) are welcome inside
   `combat.rs`'s test module but are not the oracle.

### What the oracle measured on the base (`coord/int-89` @ `7bc23f775`, release, `-- --ignored --nocapture measure`)

20 sweep runs (lab seeds 1 to 10, a Bow and a Wand each, seat started at the arena centre, 4 800 ticks, camp price 50,
elite goals off, banked build as `raid_reach_fix.rs`) and one named scene (seed 1, Bow, start (3700, 3500)):

| run | wall ends | worst streak (wall ends, no hit between) | hits |
|---|---|---|---|
| seed 1 Bow | 29 | 26 | 146 |
| seed 2 Bow | 19 | 18 | 63 |
| seed 2 Wand | 33 | 33 | 14 |
| seed 9 Wand | 84 | 84 | 282 |
| named scene (seed 1 Bow, (3700, 3500)) | 103 | 59 | 173 |
| the other 16 sweep runs | 0 to 5 | 0 to 3 (seed 10 Bow 3; seed 3 Wand 1; seeds 6, 7 Bow 1) | 2 to 737 |

Totals over the 20 sweep runs: 4 330 hits; 18 of 20 runs with 10 or more hits (seed 3 Bow 2, seed 10 Bow 2). A diagnostic
at the wall-end ticks of seed 2 Wand showed the mobs in lock range (up to 640 u) of camp 11 with `los == false` and the
seat 125 u from a wall on the line to them. The seat's lock target itself was not logged (a Spy on the seat's commands was
not built); the streaks and the wall positions are the evidence.

## 2. Dependencies

`coord/int-89` @ `7bc23f775` (holds P-RAID-REACH-1-fix: `Reach`, `engage`, the farm's no-contact bound). None else.

## 3. Relevant legacy paths

Legacy has no RaidBot. The sim's own rules are the reference (read, not changed): `crates/rh-sim/src/systems/
projectiles.rs:95, :216-220, :292, :459`; `geometry.rs:19, :132, :283`; `constants.rs:131`. Bot side (to change):
`exec/combat.rs:97-114` (`locks_from_here`), `:159-209` (`scan`, `nearest`), `:215-257` (`engage`); `exec/farm.rs:332`
and `exec/elite.rs:722` (the call sites; change them only if the new rule needs an argument).

## 4. New paths

- `crates/rh-policy/tests/raid_los.rs` (live; runs in debug in about 37 s and in release in about 27 s)
- No stub commit: the oracle compiles against the base.

## 5. Exclusive write scope

- `crates/rh-policy/src/bots/raid/exec/combat.rs` and its in-crate tests (a new `#[cfg(test)] mod` file beside it is
  fine, with its `mod` line in `combat.rs`)
- `crates/rh-policy/src/bots/raid/exec/farm.rs` and `exec/elite.rs` only where they call `Reach::engage` /
  `locks_from_here`

Not `exec/spell.rs`, not `spells.rs`, nothing under `crates/rh-sim` or `crates/rh-headless`, no existing test or capture.

## 6. Contracts and references

C-INPUT (every action through `validate` + `Sim::submit`), C-OBS (`los` reads only the static map the seat was handed:
the same `cx.paths` the walk uses), C-RNG (no draw added), C-TICK (no sim change). Owner row 2026-11-22.

## 7. Behaviour to preserve

- Melee seats unchanged (`a_melee_seats_approach_is_unchanged`, `a_melee_seat_on_a_small_camp_walks_in_and_locks_as_before`).
- A frame whose map has no layers, a camp without spots or an unknown camp answers "may fight" and locks from where it
  stands (hand-built layer-less tests); the same for a frame whose map answers `los` trivially.
- The stuck and no-contact bounds stay: with no point with line of sight in the band the seat locks from where it stands
  as `engage` does today (`:242`); the farm's 20 s no-contact give-up and the elite's 120 s give-up are not touched.
- The `DEFEND_U` clause of `locks_from_here` (a mob already fighting at the seat's side) is a lock on a mob that has
  reached the seat: the implementer decides whether it is also gated by `los` (a mob that hits the seat without line of
  sight to it is not shooting through a wall); the oracle does not pin either.
- Existing live reach scenes stay green (guards below).

## 8. Acceptance checks

- `cargo test --locked -p rh-policy --test raid_los` green (the oracle).
- Guards, all must stay green: `cargo test --locked --release -p rh-policy --test raid_reach`, `--test raid_reach_fix`,
  `--test raid_spell_reach`; and the in-crate `elite_reach_tests` and `elite_reach_fix_tests` (`cargo test --locked -p
  rh-policy --lib reach`).
- `scripts/gate.sh --quick --base coord/int-89 --oracle packet/P-RAID-LOS-1`.
- `cargo test --locked -p rh-headless --test raid_seats` (digests of seats that now choose another point may move; say so).

## Oracle files (read-only for implementers)

- `crates/rh-policy/tests/raid_los.rs`
- `docs/backlog/packets/P-RAID-LOS-1.md`

### The test

| test | file | base |
|---|---|---|
| `a_ranged_seat_does_not_go_on_shooting_at_a_target_behind_a_wall` | raid_los.rs | RED (five runs exceed 6 wall ends in a row: 26, 18, 33, 84, 59) |
| `measure` | raid_los.rs | `#[ignore]`, measurement |

Assertions, over the 20 sweep runs plus the named scene: no seat fault; worst streak of the seat's projectiles ending on a
wall with no hit of the seat between them is at most 6 (`MAX_STREAK`; base: 26, 18, 33, 84, 59 in five runs, 3 or less in
the rest); the seat still fights: at least 3 000 hits over the sweep (base 4 330), at least 15 of 20 runs with 10 or more
hits (base 18), at least 20 hits in the named scene (base 173). The floors keep a seat that never shoots from passing.

## Existing tests that pin what this packet changes

| test | asserts | state under a correct fix |
|---|---|---|
| `tests/raid_reach.rs` (two live tests) | at least 5 hits from inside the leash band on seed 7 camp 50, and the seat in that band within 600 ticks | guard; not run in this packet's author pass |
| `tests/raid_reach_fix.rs` (five live scenes) | first hit within 600 ticks and at most 100 stalled ticks beside a full-hp member; stall count stops once the camp is cleared | guard; a fix that refuses to lock without `los` and has no fall-back would stall these |
| `tests/raid_spell_reach.rs` | no Smite from outside the band | guard (spell path, outside the write scope) |
| `elite_reach_tests.rs` (approach and lock tests with hand-built frames) | a Wand or Bow seat in lock range outside the band steps toward the target; inside the band it locks and stands | hand-built frames may have a wall between seat and mob; a fix that gates the lock on `los` can turn a "locks" expectation into a "moves"; **if one goes red, the packet goes back to the coordinator**, the implementer does not edit the test |
| `elite_reach_fix_tests.rs` (`from_every_bearing_...` 48 bearings at 300 u, lock within 600 thinks, and the stall tests) | a ranged seat on a small camp ends in a lock | the fall-back (lock from where it stands when no point with `los` exists) keeps the "ends in a lock" claim; same rule on a red |
| `crates/rh-headless/tests/raid_seats.rs` and the other raid digests | behaviours and digests of seats | may move; report |

No conflict established with the oracle: the test file is new and asserts a property the base violates. The guards were
NOT run on a fix (none exists); the base state is green at the integration merge (coordinator's record).

## 9. Validation commands

- `export PATH="$HOME/.cargo/bin:$PATH"` and `CARGO_BUILD_JOBS=4` if the machine is busy
- `cargo test --locked --release -p rh-policy --test raid_los`
- `cargo test --locked --release -p rh-policy --test raid_reach --test raid_reach_fix --test raid_spell_reach`
- `cargo test --locked -p rh-policy --lib reach`
- `scripts/gate.sh --quick --base coord/int-89 --oracle packet/P-RAID-LOS-1`
- Measurement: `cargo test --release -p rh-policy --test raid_los -- --ignored --nocapture measure`

## 10. Risk and suggested model tier

implementer-sonnet, reviewer-opus. Risks: (1) `los` per candidate cell in `scan` costs a ray per cell, but the `los`
depends on the target's position, not only on the camp: do not cache it by camp in `points`; filter the cached points by
`los(q, mob_at)` in `nearest` (a ray per candidate within range, per think while walking; keep it cheap, for example
test candidates in order of distance from the seat and stop at the first that passes); (2) a seat that cannot lock from
here and has no point with `los` locks from where it stands: the wall shooting then continues until the existing bounds
end the fight; the oracle's `MAX_STREAK` of 6 is only met if the seat moves to a point with `los` in those scenes, as the
measured scenes need; (3) `step_toward` to a fight point may route through a place without `los` to the mob and lock
before arrival: lock only when `los` holds from the seat's current point; (4) the elite fight has no 20 s bound: a seat
that stands where it cannot see the elite and has no better point waits for the 120 s give-up, as before.

## 11. Known traps

- `los(a, b)` is a centre-line test; a mob moves: test against `mob.at` of this think, and do not cache by camp.
- `Reach::points` is cached per `(camp, elite)` for the whole match; line of sight to a moving target is not a property of
  the camp. Filter at use.
- `MobSeen::at` is the seat's observation of the mob; `los` from `cx.p.me.at`.
- The in-crate frame tests build `Cx` with a `RoomPathService` over a lab arena: a hand-placed mob can have a wall
  between it and the seat by accident. Check before concluding a test is wrong.
- Not established: that a fix reaches the oracle's floors (none was written); that the wall ends of the base all have
  a lock on a mob without `los` (the wall ends are the effect, the lock target was not logged); the seed-wide 2.4% figure.

## Notes from previous attempts
