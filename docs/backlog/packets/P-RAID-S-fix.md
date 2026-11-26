# P-RAID-S-fix: ShoulderCharge is a dash out of a fight in reach, never a dash at a player

- **Serves:** not in ORDER_OF_OPERATIONS: owner-approved balance patch (`docs/backlog/BALANCE_LAB_PATCH.md` wave 5, row P-RAID-S; this is its review note).
- **Implementer tier:** implementer-sonnet (one crate, one file, one condition; no `rh-sim` change, no draw, no state)
- **Reviewer:** reviewer
- **Integration base:** `coord/int-88` @ `14f1a628e` (integration 87 plus P-RAID-S).
- **D-rules touched:** D1 n/a (Rust-native quest board, no legacy) · D2 n/a · D5/D6/D9 none
- **Decisions in `docs/OWNER_DECISIONS.md`:** none found for this subject (searched "spell", "cast", "charge").

## 1. ID and outcome

P-RAID-S's ShoulderCharge rule (`When::Leaving`, `RuleAim::AwayAlongLeave`) fires on a "leave", which `scene()` reads as: the
running goal is Sustain on a camp. Sustain lasts through the heal-up after the seat has walked out of reach, and `leave_away`
is the unit vector away from the nearest living hostile mob at any distance. The reviewer traced six lab seeds: 36 of 51
dashes fired with no mob within 240 u (nearest 246 to 734 u), a seat standing still at 0.58 hp dashed 67 u and stood
still again, about 12 dashes were followed by a walk back the opposite way, and every dash spends the 12 s cooldown so the
spell is often gone for the next real leave. One traced case: a seat at 5% hp dashed away from a mob and landed 129 u from
an enemy player, who killed it half a second later; the rule ignores players.

After this packet a seat dashes only (a) while a living hostile mob is within reach and (b) when no enemy player stands
near the landing point.

## 2. Dependencies

Base `coord/int-88`. Nothing in `rh-sim`.

## 3. Relevant code (read at `14f1a628e`)

- `crates/rh-policy/src/bots/raid/exec/spell.rs:110-116` `scene()`: `leave_away = Some(unit vector from the nearest hostile
  mob to the seat)` when the goal is `Sustain` on `Target::Camp(_)`; `nearest(...)` over `hostile(m, cx)` at **any distance**.
  Players are not read for it. `spell.rs:98-108` already builds `hostile_distances` (mobs and enemy players, `o.alive &&
  o.faction != cx.p.me.faction`): the enemy-player filter to reuse.
- `crates/rh-policy/src/bots/raid/spells.rs:136-137` the rule (`Leaving`), `:173` (`holds`: `leave_away.is_some()`),
  `:183-188` (`AwayAlongLeave`: aim = `me + v * CHARGE_RANGE`), `:93` `CHARGE_RANGE = 220` (a hand copy of the sim's
  `charge_range`, `quests/board/tuning.rs:186`).
- `crates/rh-policy/src/bots/raid/exec/sustain.rs:231-238` `Sustain::nearest`: the nearest threat within `threat_u(m)`
  (`:95-102`): `FIGHT_U` (240) for a melee mob, `max(FIGHT_U, bow range + BOW_MARGIN_U)` for a mob with a projectile weapon.
  `exec/sustain.rs` names "no threat, so healing up, until `ready_hp`" for the stand after a leave.
- `crates/rh-policy/src/bots/raid/exec/farm.rs:22` `pub const FIGHT_U: f64 = 240.`

**How the two tests differ.** Sustain's threat test widens the reach for a bowman (bow range plus a margin) because walking
out of reach of the shorter weapons only is not leaving. The spell's test is the simpler one: plain `FIGHT_U` for every mob.
A dash is 220 u, so a mob that shoots from beyond 240 u is not escaped by a dash anyway; keep the spell's test as the
simple one (coordinator decision 1). Do not import `threat_u`.

## 4. The change (coordinator's decisions)

1. **Leaving means a threat is in reach.** `leave_away` is set only while the nearest living hostile mob is within
   `exec::farm::FIGHT_U`. Outside it the seat is resting and holds the dash. (The goal test, the nearest-mob test and the
   direction are unchanged.)
2. **Not toward a player.** The dash is not asked when an enemy player (alive, faction not the seat's) stands within
   `DASH_PLAYER_CLEARANCE_U` of the landing point (`me + away * CHARGE_RANGE`). Put the constant beside the rule with its
   reason. **Chosen: 200 u.** Reason: a melee weapon reaches 72-112 u (`rh-sim/src/content.rs:256-301`) plus the body radius,
   so a player within about 130 u of the landing point hits the seat as it arrives; 200 adds a step of approach margin and
   stays under the dash's own 220 u, so a player a whole dash away (the case that is only worth it) never blocks. A bow
   outranges any constant; those players are not what this clearance covers. (The reviewer's traced case landed 129 u from
   the player: inside 200.)
   Implement it in `scene()` (leave `leave_away` `None` when the landing is near a player) so `decide_cast`, `holds` and the
   public `SpellScene` stay as they are.
3. **Nothing else changes**: the rule table, the other three rules, `CastGate`, `Refs`, the registry.

**Not here (go to P-RAID-S2):** `CastGate::pass` treats any part-recharged multi-charge spell as "took"; `urgent` ignores
silenced.

## 5. Exclusive write scope

- `crates/rh-policy/src/bots/raid/exec/spell.rs`
- `crates/rh-policy/src/bots/raid/spells.rs` only if the named constant belongs beside the rule table
- Nothing else; no existing test.

## 6. Contracts and references

C-INPUT, C-OBS (the scene reads only the frame), C-RNG/C-TICK (no draw).

## 7. Behaviour to preserve

`tests/raid_spells.rs` stays green unchanged (its pure `decide_cast` tests build `SpellScene` by hand and do not touch
`scene()`); `exec/review_tests` urgent/draw tests; `cargo test -p rh-headless --test raid_seats`.

## 8. Acceptance checks

- `scripts/gate.sh --quick --base coord/int-88 --oracle packet/P-RAID-S-fix` passes.
- `cargo test --locked -p rh-headless --test raid_seats` passes.
- A lab run, release build, seeds 1 to 6, 6000 ticks, eight raid seats, human seats, base against head, reporting
  `quests.spell_casts` by spell, camps cleared and player deaths. On the base the review counted 51 ShoulderCharge and
  2 Cleanse; expect about 15 dashes after the fix. Report the numbers; a ShoulderCharge count far from 15 is a finding
  to explain, not a failure by itself.

## Oracle files (read-only for implementers)

- `crates/rh-policy/tests/raid_spells_fix.rs`

## The oracle, test by test

The scene is `exec::spell::Spell::ride_along` on hand-built `RaidPercept`s and `Cx` (the pattern of `tests/raid_hold.rs`):
`scene()` is private, so the whole path from percept to asked act is exercised. Seat at (4000, 4000), mob east, so the
dash runs west and lands at x = 3780.

| Test | Kind | Asserts |
|---|---|---|
| `a_seat_resting_out_of_reach_of_every_mob_holds_the_dash` | **RED** on the base | Sustain on a camp, ShoulderCharge held and charged, a mob 250, 500, 734 u away: no act |
| `a_seat_with_a_mob_in_reach_dashes_away_from_it` | guard | mob 150 u away: a `Cast` of reward 3 at a point west of the seat, same y |
| `the_dash_is_not_asked_toward_an_enemy_player_at_the_landing_point` | **RED** on the base | mob 150 u away, enemy player 0, 100, 150 u past the landing point: no dash |
| `a_player_far_from_the_landing_point_does_not_stop_the_dash` | guard | an enemy 700 u off to the side, a dead enemy and a same-faction player at the landing point: the dash is asked |
| `cleanse_smite_and_turret_answer_as_before` | guard | rooted seat with Cleanse (own position); Elite goal with an elite 300 u away: Smite (`CastAim::Mob`, elite) and Turret (own position) |

The red boundary for decision 1 is tested at 250 u (just past 240); the guard at 150 u. Exactly 240 and the player
clearance edge (200 u) are not pinned: the implementer may use `<=`.

**Not tested (said plainly):** the live check that a resting seat casts no ShoulderCharge over a few hundred ticks. A
live scene needs a camp, a leave and a regenerating seat, which the existing helpers do not stage cheaply; the
implementer's lab run (section 8) is the live evidence. Taking a part-recharged spell as a charge, and silenced, are
P-RAID-S2's.

## Existing tests that pin what the packet touches

| Test | Asserts | Conflict |
|---|---|---|
| `crates/rh-policy/tests/raid_spells.rs` `shoulder_charge_is_cast_away_from_a_lost_fight_only_while_leaving` | `decide_cast` on a hand-built `SpellScene { leave_away: Some(+x) }` gives a point away along +x; none without | none: pure, `scene()` untouched |
| `tests/raid_spells.rs` live tests (Cleanse, Smite, Hook/Tornado, unruled, no spell) | casts in a live sim | none: no ShoulderCharge held in them |
| `crates/rh-policy/src/bots/raid/review_tests.rs:270-` | `urgent` false and no draw in a no-spell scene | none |
| `crates/rh-headless/tests/raid_seats.rs` | seat behaviour end to end | none expected; run it |

No conflict found; no `oracle-amend:` commit.

## 9. Validation commands

- `export PATH="$HOME/.cargo/bin:$PATH"; cargo test --locked -p rh-policy --test raid_spells_fix`
- `scripts/gate.sh --quick --base coord/int-88 --oracle packet/P-RAID-S-fix`

## 10. Risk and suggested model tier

Sonnet: one condition and one distance. The risk is a dash that now never fires; the lab count (about 15 expected) checks it.

## 11. Known traps

- `scene()`'s `nearest` takes the nearest hostile mob at any distance; test the reach on that mob, not on any mob in view.
- A dead or same-faction player is no clearance reason (the guard test pins it); reuse the `o.alive && o.faction !=
  cx.p.me.faction` filter of `hostile_distances`.
- The landing point is computed from the unit vector and `CHARGE_RANGE`, as `aim_of` does; do not recompute a different one.
- Do not edit `Sustain`: its bow margin is intended.

## Notes from previous attempts
