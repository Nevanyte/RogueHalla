# P-RAID-REACH-1-fix: the test bot's ranged seats fight from where the camp's mob can reach them, walk to such a place, and give an idle camp up

- **Serves:** not in the plan's phases: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot,
  per-elite numbers"; `docs/backlog/BALANCE_LAB_PATCH.md` section 1). Headless human seats only. Two notes of
  reviewer-opus on P-RAID-REACH-1 (integration 88 merge review).
- **Implementer tier:** implementer-opus (rh-policy bot state across ticks; it replaces a rule the previous packet pinned)
- **Reviewer:** reviewer-opus
- **Integration base:** `coord/int-88` @ `055d3a22f` (holds P-RAID-REACH-1). Straight line; do not merge the base in.
- **D-rules touched:** D1 loose proof (RaidBot has no legacy counterpart; the leash is the sim's own) · D2 n/a ·
  D5/D6/D9 none (no wire, no observation field, no `rh-sim` change).

## 1. ID and outcome

P-RAID-REACH-1 made a ranged seat step into "the band" before it fights, where the band is `leash_ok` at
`MOB_LEASH_MARGIN` (24 u). The approach aims at the mob itself and the farm goal has no time limit. On a camp whose
resting spots lie outside their own margin-24 band the seat walks onto an idle member and sends a move every tick for
ever: it never locks, never hits, and the camp is never given up. Reproduced live (below). A second effect: a lock on this
camp's elite counts as the camp's fight only while the elite stands within `CAMP_JOIN_RADIUS` (480 u) of the camp's
centre, which is narrower than the camp's leash on most elite camps.

After this packet:
1. the helper `may_fight_from` follows the sim's give-up rule (margin 0, plus the mob's melee reach), not the stricter
   margin-24 retreat rule;
2. a seat that may not fight from where it stands walks to the nearest place it may fight from, not to the mob, and locks
   there; with no such place, or no way to it, it locks from where it stands as before P-RAID-REACH-1;
3. a Farm goal that has neither hit nor been hit by the camp for a named time gives the camp up for a while;
4. a lock on this camp's elite counts when the elite stands inside this camp's leash from its elite spot.

### What the sim's rule is (establish 1; verified in the code)

- **A chase point is not leash-tested.** `combat_policy::decide` (`crates/rh-sim/src/combat_policy.rs:122-188`): `chase =
  !los || d > p.far`; a chase returns `Move::Point(to_move)`, the target's own position, with no call to `walkable`.
  `walkable` (the closure the mob passes) is called only on the retreat arm (`:150-170`).
- **`MOB_LEASH_MARGIN` (24 u) is only on retreat and spacing steps:** `crates/rh-sim/src/systems/mob_ai.rs:310`
  (`leash(.., MOB_LEASH_MARGIN)` inside the `walkable` closure of `decide`) and `:447` (the spacing step).
- **A chasing mob gives up** (`mob_ai.rs:267`, `fight`): when its OWN position fails `leash(.., margin 0)` or
  `now - b.last_combat_at >= MOB_GIVE_UP_SECONDS * 1000` (20 s, `constants.rs:333`). `last_combat_at` is set by `wake`
  (`mob_ai.rs:62`) and refreshed whenever the mob damages a player (`systems/ledger.rs:1284-1290`).
- **`leash_ok` at margin 0** (`mapdata/patience.rs:37-57`): the point is within `min(radius, 768)` of the home AND on
  the camp's territory cells. At margin > 0 it also needs the four axis points `margin` away on the territory. That second
  condition is what puts a small camp's resting spot outside its own band at margin 24 (the spot sits within 24 u of a
  territory edge).
- **The mob's reach** (`combat_policy::spacing`, `:15-48`): it chases until `d <= far` and attacks when the shot or swing
  reaches `attack`: melee `attack = reach + PLAYER_RADIUS - NPC_MELEE_EDGE` (Axe and Sword reach 112, Shield 96:
  118 u and 102 u; `constants.rs:79, :313-314`), Bow `0.92 * 520`, Wand `0.8 * 640` (`:315-320`, content weapons). A
  ranged mob therefore hits from far beyond its band; the bot knows a mob's weapon only through `MobSeen::weapon`, which
  the helper's signature does not carry.
- **The precise set a seat may fight from.** For a melee target: the seat's point `p` such that some point `q` with
  `leash_ok(home, q, margin 0)` (the mob's own position never fails the give-up test) lies within the mob's attack of
  `p`, i.e. `p` inside the margin-0 band, or within 118 u (Axe/Sword; 102 for a Shield mob) of it. For a ranged target:
  that, or any `p` the mob can shoot from a point of the band (up to 0.92 of 520 or 0.8 of 640 from it); the ranged
  extension needs the mob's weapon and is NOT demanded by the oracle (decision below). The 20 s clock is never the limit
  while the mob keeps hitting (each hit refreshes it).

### Census at margin 24 against margin 0 (establish 2; a throwaway measurement over lab seeds 1 to 12, 607 camps)

| | margin 24 | margin 0 |
|---|---|---|
| camps with a resting spot outside its own band | 6: seed 1 camp 50 (3 of 3 spots), seed 5 camp 30 (1 of 3), seed 5 camp 51 (3 of 3), seed 7 camp 51 (3 of 3), seed 7 camp 50 (elite, 1 of 1), seed 11 camp 50 (elite, 1 of 1) | **0** |
| camps with an empty band | 0 | 0 |

The coordinator's five stall camps are confirmed; seed 5 camp 30 is a sixth (one spot of three). Margin 0 alone puts
every resting spot of every camp inside its own band, so the margin alone cures the "never locks beside an idle member"
case where the seat reaches the spot. It does not cure a seat that starts outside the new band and walks onto the mob
(approach at the mob, no lock), which the approach rule (2) cures. Whether the live scenes are cured is NOT established
(no implementation was run: an author does not write the fix).

Elite camps: 92 of the 124 elite camps of seeds 1 to 12 have a band that reaches more than 480 u from the camp's centre
(note 2 is real on most elite camps).

### The three live traces (the oracle's scenes), measured

Scene: `tests/raid_reach_fix.rs` (the lab map, a human seat, prices that route the seat to the camp; see its header). The
pre-packet rule is `14f1a628e`'s: reproduced by making `locks_from_here` not ask `may_fight_from` (a temporary local edit,
reverted; the same numbers the reviewer gave).

| scene | pre-packet (`14f1a628e`) | head (`055d3a22f`) |
|---|---|---|
| seed 7 camp 51 from (536, 3952), Wand | first hit tick 21, 128 hits, 0 stalled ticks | never hits; 2 379 of 2 400 ticks within 60 u of a full-hp member |
| same, Bow | tick 14, 109 hits | never hits, 2 379 |
| seed 1 camp 50 from (3700, 3500), Wand and Bow | tick 25 / 19 | first hit tick 1 220 after 1 197 stalled ticks, both weapons |
| seed 5 camp 51 from (1584, 3768), Bow | tick 15 | first hit tick 779 after 748 stalled ticks |

(The coordinator's start (3600, 3416) on seed 1 camp 50 does not stall in this harness: first hit 436 / 805 at the head
against 406 / 733 before. The scene above uses (3700, 3500), which gives the ~1 194 the coordinator measured.)

## 2. Dependencies

`coord/int-88` @ `055d3a22f` (P-RAID-REACH-1 and the in-crate oracle `exec/elite_reach_tests.rs`). None else.

## 3. Relevant legacy paths

Legacy has no RaidBot; the sim's own rules are the reference (read, not changed): `crates/rh-sim/src/systems/mob_ai.rs:35-40,
:62, :267, :310, :447`; `combat_policy.rs:15-48, :122-188`; `mapdata/patience.rs:37-57`; `constants.rs:79, :313-325, :333-341`;
`systems/ledger.rs:1284-1290`. Bot side (to change): `exec/combat.rs:20-65` (`may_fight_from`, `locks_from_here`);
`exec/farm.rs:285-299` and `exec/elite.rs:715-729` (the approach blocks); `exec/elite.rs:640-645` (the elite lock's
radius); `exec/farm.rs` `Farm` (`banned`, `FAILED_BAN_MS` 20 s, `EMPTY_MS` 3 s: the existing give-up and ban machinery,
used only for "no member in sight at the camp"); `exec/elite.rs` `give_up` (120 s bound, bans).

## 4. New paths

- `crates/rh-policy/tests/raid_reach_fix.rs` (live, release)
- `crates/rh-policy/src/bots/raid/exec/elite_reach_fix_tests.rs` (in-crate, hand-built frames; created as a one-line
  placeholder by the `stub:` commit, content in the `oracle:` commit)
- The `stub:` commit adds the `mod reach_fix_tests;` line to `exec/elite.rs` (already in the write scope).

## 5. Exclusive write scope

- `crates/rh-policy/src/bots/raid/exec/combat.rs`
- `crates/rh-policy/src/bots/raid/exec/farm.rs` (the approach block and the bound)
- `crates/rh-policy/src/bots/raid/exec/elite.rs` (the approach block and the elite lock's bound)

Nothing else; nothing under `crates/rh-sim`; no existing test or capture.

## 6. Contracts and references

C-INPUT (every action through `validate` + `Sim::submit`), C-OBS (the helper reads only the public static map layers the
seat was handed, as P-RAID-REACH-1's decision 2), C-RNG (no draw added), C-TICK (no sim change). Owner row 2026-11-22.

## 7. Behaviour to preserve

- Melee seats unchanged (guard `a_melee_seat_on_a_small_camp_walks_in_and_locks_as_before`, and P-RAID-REACH-1's
  `a_melee_seats_approach_is_unchanged`). No change to prices, Sustain, the chest phase or the bans of an elite fight.
- P-RAID-REACH-1's addendum: a lock on an unlabelled stray, or on another camp's elite, starts no fight
  (`a_lock_on_an_unlabelled_regional_mob_does_not_start_a_fight` and
  `a_lock_on_another_camps_elite_of_the_same_kind_does_not_start_this_camps_fight`, `elite_reach_tests.rs`, plus the
  leave-fix2 tests, all green as they are). The "fight already on record" half of the elite lock stays.
- A frame whose map has no layers, a camp without spots or an unknown camp answers "may fight" and locks from where it
  stands (hand-built layer-less tests).

## Decisions (coordinator, 2026-11-23) and how step 1 fits them

1. **The reach rule is the sim's rule.** `may_fight_from(paths, camp, elite, from)` answers yes where the target, standing
   inside its leash by the sim's give-up test (margin 0), can reach the seat. **Chosen form: the melee form**, which step 1
   supports: yes iff `from` lies inside the margin-0 band of a home (the elite spot for an elite, any spot for a normal
   member), or within the mob's melee attack of a point that does. The signature stays (the oracle calls it). Because the
   helper has no weapon argument, the oracle bounds the extension by the longest melee attack, `118 + 23` u (the 23 is
   the cell-centre slack of the oracle's sampling) and pins a lower bound of "every margin-0 band point is yes". A
   ranged-aware extension (a Wand or Bow mob that shoots the seat from inside its leash) needs `MobSeen::weapon`
   and would contradict the oracle's upper bound: that goes back to the oracle author, it is not done silently.
   *What I changed from the brief:* the brief offered "a ranged target also counts"; I did not pin it, for the reason
   above. An implementation of "margin 0 only" (no melee extension) also passes.
2. **The approach aims at the band.** When the seat may not fight from where it stands, it walks to the nearest point it
   may fight from (a territory cell of the camp, from the same public layers, whose centre passes the helper, nearest
   the seat) and locks once there. No such point or no way to it: it locks from where it stands. It never stands within
   lock range of an idle member, unable to walk further, without locking. Pinned by the hand-built stall tests, the
   48-bearing sweep and the live scenes.
3. **A farm fight is bounded.** A seat holding Farm on a camp with neither a hit dealt nor a hit taken for a named time
   gives the camp up (`Step::failed`, the camp pushed on `banned`, as `EMPTY_MS`/`FAILED_BAN_MS` do for "no member in
   sight"). The named time is the implementer's, inside **10 s to 45 s** (the sim's mob gives up 20 s after its last
   contact, `MOB_GIVE_UP_SECONDS`); the ban at least 10 s and finite (the oracle reads it back after 300 s). "Contact" is
   what the percept shows: a camp member's `hp_share` falling, or `RaidPercept::hit_at` newer than the last think. The
   time starts on the first think that holds the goal on the camp even if `start` was not called (the in-crate tests call
   `observe` and `act` only). Inside `exec/farm.rs`.
4. **Note 2.** A lock on this camp's elite counts when the elite stands inside this camp's leash from its elite spot
   (`leash_ok`, margin 0, the same public layers), not within `CAMP_JOIN_RADIUS` of the centre; the "already on record"
   half stays.

## 8. Acceptance checks

- `scripts/gate.sh --quick --base coord/int-88 --oracle packet/P-RAID-REACH-1-fix` passes (after the coordinator's
  `oracle-amend:` below).
- `PATH="$HOME/.cargo/bin:$PATH" cargo test --locked -p rh-headless --test raid_seats`.
- A lab run, release, seeds 1 to 12, 6 000 ticks, eight raid seats, human seats (`balance_lab --human-seats --seeds 1..13
  --max-ticks 6000 --seat 0=raid ... --seat 7=raid`, `--jobs 3`) on THREE trees: before P-RAID-REACH-1 (`14f1a628e`), the
  base (`055d3a22f`) and the head of the fix. Per weapon in hand (`elites.by_kind_weapon`): elite attempts, killed, left,
  open; for the whole run camps cleared, kill, heal and walk seconds, player deaths. Reviewer's suspicion to answer: walk
  seconds rose from 5.1 to 8.0 with P-RAID-REACH-1 because stall time counted as approach; the three-tree table shows it.

## Oracle files (read-only for implementers)

- `crates/rh-policy/tests/raid_reach_fix.rs`
- `crates/rh-policy/src/bots/raid/exec/elite_reach_fix_tests.rs`
- `docs/backlog/packets/P-RAID-REACH-1-fix.md`

### Tests, state at the head (`055d3a22f`), floors

| test | file | head |
|---|---|---|
| `a_wand_seat_at_seed_7_camp_51_hits_the_camp_and_does_not_stall`, `a_bow_...` | raid_reach_fix.rs | RED: first hit never (floor: within 600 ticks; worst stall <= 100 consecutive ticks within 60 u of a full-hp member without a hit; measured pre-packet: first hit 14-25, stall 0-18) |
| `a_wand_seat_at_seed_1_camp_50_...`, `a_bow_...` | raid_reach_fix.rs | RED (first hit 1 220) |
| `a_bow_seat_at_seed_5_camp_51_...` | raid_reach_fix.rs | RED (first hit 779) |
| `a_ranged_seat_beside_an_idle_member_of_a_small_camp_locks` (Wand and Bow, seed 7 camp 51, 5 u from a member, lock within 100 thinks) | reach_fix_tests | RED (walks onto the member) |
| `a_ranged_seat_beside_an_idle_elite_of_a_small_camp_locks` (seed 7 camp 50) | reach_fix_tests | RED |
| `from_every_bearing_a_ranged_seat_on_a_small_camp_ends_in_a_lock` (five small camps, 48 bearings at 300 u, at least 16 tried per camp, lock within 600 thinks; hand-built) | reach_fix_tests | RED (seed 1 camp 50 bearing 0 stands on the mob) |
| `the_helper_says_yes_exactly_where_a_mob_inside_its_leash_can_reach` (seeds 1, 2, 3, 7, every camp, 33 x 33 grid at 64 u: yes at every margin-0 band point; never yes beyond 141 u of the band) | reach_fix_tests | RED (the margin-24 helper refuses margin-0 points) |
| `a_farm_seat_with_no_contact_gives_the_camp_up_in_time` (10 s to 45 s) | reach_fix_tests | RED (never) |
| `a_camp_the_farm_gave_up_is_banned_for_a_while_not_for_good` (not proposed 10 s on, proposed 300 s on) | reach_fix_tests | RED (premise: no give-up) |
| `a_lock_on_this_camps_elite_inside_its_leash_but_far_from_the_centre_starts_the_fight` (lab seed 1 camp 7, elite 631 u from the centre) | reach_fix_tests | RED |
| `a_farm_fight_with_contact_is_not_cut_short` (contact every 5 s, 120 s) | reach_fix_tests | guard (green) |
| `a_melee_seat_on_a_small_camp_walks_in_and_locks_as_before` | reach_fix_tests | guard (green) |
| `a_lock_on_an_elite_outside_the_camps_leash_does_not_start_the_fight` | reach_fix_tests | guard (green) |
| `measure_the_stall_scenes` | raid_reach_fix.rs | `#[ignore]`, measurement |

The live floors are 600 ticks and 100 ticks because the pre-packet seats hit within 25 ticks and stall 0 to 18, and a
seat that walks to the band edge needs seconds, not minutes. The 5-s / 100-thinks floors of the hand-built tests: a seat
walks 12 u a think, the band edge is at most a few hundred u away.

## Existing tests that pin what this packet changes

| test | asserts | state under a correct fix |
|---|---|---|
| `elite_reach_tests.rs::the_helpers_reach_never_exceeds_the_sims_leash` | a yes only where `leash_ok` at margin 24 holds, at every margin-24 resting spot the helper says yes | **conflicts**: any rule at the sim's margin 0 says yes beyond the margin-24 band, so the test goes red. **`oracle-amend:` for the coordinator, see below** |
| `tests/raid_reach.rs` (two live tests) | at least 5 hits from inside the band with "inside" = `leash_ok` at margin 24, and the seat in that band within 600 ticks | **notion of the band must change**: see below |
| `elite_reach_tests.rs`: the two elite approach tests (300 u outside the Gaoler's band), the normal camp's (300 u from member 0, outside every spot's margin-24 band) | a Wand or Bow seat in lock range steps toward the target | stay green with a margin-0 helper (300 u is beyond camp 50's radius 96 by 200 u; camp 51 reaches 166 and the 300 u seat is 134 u from the edge of spot 0's band, which a melee extension may count as reaching: if the implementer's rule says yes there and the seat locks, this test goes red; the move it asserts is only "toward the member", which the walk to the band also satisfies) |
| `elite_reach_tests.rs::a_seat_inside_the_band_locks_and_stands`, `a_farm_seat_inside_the_band_locks_and_stands` | locks, no move, 40 / 60 u from home inside the margin-24 band | stay green (margin-24 band is inside the margin-0 band) |
| `a_melee_seats_approach_is_unchanged`, the leave-fix2 lock arms (`a_ranged_seat_locked_on_the_elite_gets_the_chest_phase`, labelled add), `a_lock_on_an_unlabelled_regional_mob_...`, `a_lock_on_another_camps_elite_...`, `a_lock_on_this_camps_own_elite_from_400_u_...`, `..._labelled_add_from_400_u_...`, `a_give_up_is_not_counted_again_...` | as named | stay green; the own-elite locks stand in the leash (the elite at its home) |
| `crates/rh-headless/tests/raid_seats.rs`, `tests/raid_elite.rs`, `raid_hold.rs`, `fix_tests.rs`, `review_tests.rs` | behaviours and digests of seats that may now walk or give up | may move; say so in the hand-off with the lab run |

### Amendments for the coordinator (`oracle-amend:` commit, files NOT edited here)

1. `crates/rh-policy/src/bots/raid/exec/elite_reach_tests.rs`, `the_helpers_reach_never_exceeds_the_sims_leash` (about
   lines 497-553): **remove the test** (its upper bound is the margin-24 rule the sim does not use; the new
   `the_helper_says_yes_exactly_where_a_mob_inside_its_leash_can_reach` carries both halves with the sim's margin 0 and the
   melee bound). Smaller alternative: line 503 `let margin = rh_sim::constants::MOB_LEASH_MARGIN;` becomes `let margin =
   0.;` and the `sim_ok` expression at about line 538 (`homes.iter().any(|h| leash_ok(data, camp, *h, p, margin))`) must
   accept "within 141 u of such a point", which is what the new test does, so removal is the simplest.
2. `crates/rh-policy/tests/raid_reach.rs`: the band notion of the live oracle must follow the sim's give-up rule. In
   `run`, the two calls `leash_ok(&data, m.camp, m.home, p.pos(), rh_sim::constants::MOB_LEASH_MARGIN)`, at line 187
   (counting `hits_in_band`) and line 223 (setting `in_band_at`): replace `rh_sim::constants::MOB_LEASH_MARGIN,` by
   `0.,`. The premise at line 250 and the doc comment at line 127 may stay or follow (the start, 256 u from a radius-96
   camp, is outside at both margins). Meaning kept: "the seat stands where the Gaoler can chase it without giving up and
   hits it from there"; the reviewer's plain fix failed the old test because the Wand shot from 72-96 u (inside the real
   leash, outside the margin-24 ring). Not validated against an implementation: a correct fix must still land 5 hits from
   inside the margin-0 band on seed 7 camp 50 within the run; if Sustain's leave cuts it short, that goes back to the oracle
   author.

## 9. Validation commands

- `export PATH="$HOME/.cargo/bin:$PATH"`
- `cargo test --locked --release -p rh-policy --test raid_reach_fix` and `cargo test --locked --release -p rh-policy --lib
  reach_fix_tests`
- `cargo test --locked -p rh-policy` (the whole crate), `cargo test --locked -p rh-headless --test raid_seats`
- `scripts/gate.sh --quick --base coord/int-88 --oracle packet/P-RAID-REACH-1-fix`
- Lab: `cargo run --release -p rh-headless --bin balance_lab -- --human-seats --seeds 1..13 --max-ticks 6000 --jobs 3 --seat
  0=raid ... --seat 7=raid` on the three trees.

## 10. Risk and suggested model tier

implementer-opus, reviewer-opus. Risks: (1) `fight_point` scans territory cells per think: keep it cheap (cache per
camp and elite flag; the layers are static) or the seats slow; (2) the walk to the band edge puts the seat within reach of
the camp earlier than before, and deaths may rise (Sustain and the prices decide how long it stays); (3) a Farm
bound that counts "contact" too narrowly cuts real fights short: the guard `a_farm_fight_with_contact_is_not_cut_short`
is the only protection; (4) the melee extension is conservative for ranged camps: a Wand or Bow camp's seat may walk
closer than the mob needs.

## 11. Known traps

- The in-crate tests call `observe` and `act` without `start`: the Farm bound must start itself on the first think.
- `step_toward` answers `Some(to)` whenever the straight line is clear, including at the mob itself: aim it at the band
  cell, not the mob.
- `MobSeen::camp` is a proximity label; use the goal's camp index.
- An elite target's home is `spots[elite_spot]`; a normal member's is any spot (the union, as `patience_plane`).
- The oracle's scenes (`raid_reach_fix.rs`) route the seat with `Prices` (`camp: 50`, elite off, rewards and chests at 0);
  that is a scene control, not a behaviour under test. With the table's prices the seats of seeds 1 and 5 leave for other
  camps.
- Not established: that any implementation passes the live floors (none was written); the ranged-mob reach (a Wand or Bow
  camp) is not pinned; the 5 hits of `raid_reach.rs` under margin 0.

## Notes from previous attempts
