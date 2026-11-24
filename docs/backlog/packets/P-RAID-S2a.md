# P-RAID-S2a: inventory of the uncast quest spells, and the rules for group A (Hook, Bolas)

- **Serves:** not in ORDER_OF_OPERATIONS: owner-approved balance patch (`docs/backlog/BALANCE_LAB_PATCH.md` wave 5, row P-RAID-S, "quest spell cast rules, 18 spells").
- **Implementer tier:** implementer-sonnet (one crate, no `rh-sim` change, no RNG draw, no state)
- **Reviewer:** reviewer-opus (the bot's priorities)
- **Integration base:** `coord/int-89` @ `7bc23f775`.
- **D-rules touched:** D1 n/a (the quest board is Rust-native) · D2 n/a · D5/D6/D9 none
- **Decisions in `docs/OWNER_DECISIONS.md`:** none found for this subject (searched "spell", "cast").

## 1. ID and outcome

P-RAID-S (and -fix, -fix2) gave `RaidBot` a rule table (`crates/rh-policy/src/bots/raid/spells.rs` `SPELL_RULES`, 19 rows)
with four ruled spells: Cleanse, Smite, Turret, ShoulderCharge. The other **fifteen rows are `When::Never`**, so a quest that
pays one of them reads "not measured" in the lab. This packet (1) records the inventory of those fifteen below, grouped by
the kind of rule each needs (A: aimed at a foe, B: cast on the seat, C: the rest), which is its lasting value, and (2) rules
**two of the four group A spells: Hook and Bolas**, both only at an enemy player the seat is fighting. B and C become
P-RAID-S2b and P-RAID-S2c (not started, no tests written).

## 2. Dependencies

Base `coord/int-89` (has P-RAID-S-fix2: `exec/spell.rs::foe` names a camp's foe only from where the seat may fight it).
Another packet is changing `exec/combat.rs`, `exec/farm.rs`, `exec/elite.rs`: this packet must not touch them (it only
reads `may_fight_from`, `DEFEND_U`, `FIGHT_U` through their existing public names).

## 3. Relevant legacy paths

None (Top 25 board is Rust-native, `docs/quests.md`). Every sim fact was read at `7bc23f775`.

### Part 1: the inventory

Counts, verified: `RewardId` has 25 variants, 7 permanent, so **18 castable RewardIds**; `BlindRageAndStillMind` (RewardId 21)
carries two awards, so there are **19 castable awards and 19 table rows** (the packet P-RAID-S said the same; the patch doc's
"18 spells" counts RewardIds). Four rows are ruled, **fifteen are `Never`**. Wire id = `Award as u8` (RewardId shifts down by
one after 21; see `docs/backlog/packets/P-RAID-S.md` section 3). Sim lines are in
`crates/rh-sim/src/quests/board/rewards/effects.rs` (`E`), `.../effects/terrain.rs` (`T`), `.../casts.rs` (`C`),
`quests/board/special/{wall,egg}.rs` and `quests/board/tuning.rs` (`TOP25`). Every cast needs a charge, a standing seat,
not silenced, not casting; all but Cleanse refuse a stunned, taunted or pulled seat (`C:36-107`). A cooldown-0 spell
(`awards.rs:79-93`) is single use (`C:117-118`).

| Wire id | Spell | What the cast does in the sim (file:line) | Rule today | Proposed rule / decision | Group |
|---|---|---|---|---|---|
| 16 | Cleanse | clears root and stun, any aim; reusable, 30 s (`E:475-485`) | `Impaired`, own position | done | ruled |
| 8 | Smite | 220 true damage to a visible non-player in 450 u (+32 slack), reusable, 20 s (`E:381-389`, `tuning.rs:170,182`) | elite within 450, at the actor | done | ruled |
| 5 | Turret | places a turret at the aim within 400 u, shoots 340 u for 25 s, cap 1, reusable 30 s (`E:348`, `T:210-249`) | elite within 340, own position | done | ruled |
| 3 | ShoulderCharge | dash up to 220 u toward the aim, stuns what it meets 0.5 s, reusable 12 s (`E:598-625`) | leaving a fight, away | done | ruled |
| 2 | Hook | shot (700 u, width 14, speed 175 u/tick) that pulls the first enemy it hits to 42 u from the caster for 0.6 s; reusable 45 s (`E:334,744-758,772-781`, `tuning.rs:148,172`) | `Never` | **S2a:** Fight goal on a targetable player 220..=450 u away, no hostile mob within 48 u of the seat-to-foe segment, seat not stunned/silenced/casting; aim at the foe's position | A |
| 19 | Bolas | shot (550 u, width 12) that roots the first enemy it hits 1.5 s; reusable 18 s (`E:449,782-788`, `tuning.rs:157,174-175,193`) | `Never` | **S2a:** same conditions, foe within 350 u (no lower bound); aim at the foe's position | A |
| 4 | Tornado | after a 0.6 s windup, enemies within 240 u of the aim (aim within 500 u of the seat, line of sight) are pulled toward it, up to 180 u scaled by `clamp(caster max hp / victim max hp - 1, 0, 1)`, so only victims with LESS max hp than the caster move; reusable 14 s (`E:336-347,975-998`, `tuning.rs:150,167-168,180,194`) | `Never` | **leave it:** an elite (high max hp) is not pulled at all, a camp mob is dragged out of its camp to a point we choose and woken, a player pulled toward the seat is no more certainly useful than not; no simple rule | A (unruled) |
| 0 | WardensCage | a ring of wall tiles, radius 160, 6 s, with a gap, at the aim within 400 u; single use (`T:78-200`, `tuning.rs:197-198,207`) | `Never` | **leave it:** it walls the seat's own fight in as often as the foe; the sim moves fighters out of the ring's tiles; no certain use | A (unruled) |
| 7 | Warhorn | taunts every enemy within 280 u with line of sight (players and mobs; mobs wake onto the caster), caster takes 15% less damage and deals 20% more per taunted, 2.5 s; single use (`E:349-380,488-500`, `tuning.rs:166,177-179,181`) | `Never` | S2b: only when at least 2 mobs of the fought camp are engaged within 280 u and no mob of another camp is within 280 u (it wakes everything in reach); needs the percept's `engaged` flag, already there | B |
| 9 | SpellShield | 1.5 s buff that blocks the next spell or Blaze trail and refunds the cooldown when it does; reusable 25 s (`E:390-393,235-269`) | `Never` | S2b: at the start of a fight on a player within 500 u (the only foes that cast spells); cost is 1.5 s of a 25 s cooldown | B |
| 18 | Stasis | 2.5 s immune to everything, but cancels attack and cast and stops the walk; single use (`E:440-448,188`, `tuning.rs:164`) | `Never` | S2b: `HpBelow(0.2)` with a foe in reach (an escape that costs the seat's own 2.5 s); owner-judgement call, the doubt is whether foes keep their aggro | B |
| 21 | BlindRage | +20% attack rate for 6 s; single use (`E:450-453`, `tuning.rs:176,184`) | `Never` | S2b: a fight on an elite or player within 150 u (never a camp mob: single use) | B |
| 22 | StillMind | removes root and stun and blocks both for 3 s; single use (`E:454-465`, `tuning.rs:185`) | `Never` | S2b: `Impaired` after Cleanse in table order (Cleanse first, so StillMind is the second answer) | B |
| 6 | RebirthEgg | plants an egg at the seat's own position (needs 4 standing players, outside the coliseum, not in training, no egg planted); single use (`special/egg.rs:69-90`, `tuning.rs:219-223`) | `Never` | S2c: new percept (standing count, coliseum test); plant when resting in a quiet spot | C |
| 12 | ForgetMeNotSeed | first cast plants a seed at the seat, second recalls it (a displacement to the seed); the plant spends nothing (`E:394-409`, `C:110-125`) | `Never` | S2c: needs memory of where the seed is (the frame does not show it); plant at fight start, recall when leaving a lost fight | C |
| 13 | SwapStick | swaps the active weapon with a player target within 400 u for 10 s (refused on a non-player, on equal weapons, on a locked gear); single use (`E:410-434`, `tuning.rs:171,183`) | `Never` | S2c: needs a weapon-value comparison of `Me.weapons` and `PlayerSeen.weapon`; cast on a better one | C |
| 15 | LanternWard | a vision ward at the aim within 400 u, 75 s, 2 charges, 60 s (`T:210-249`, `tuning.rs:154,158,205-206`) | `Never` | S2c: no combat value; place at a camp being approached (scouting) | C |
| 17 | ScoutFalcon | a flying reveal along the aim, 1200 u, 4 s, reusable 35 s (`T:251-278`, `tuning.rs:156,199,206,214`) | `Never` | S2c: scouting only; leave unless the bot has a use for revealed ground | C |
| 20 | WallBuster | removes terrain walls within 300 u of the aim (a human seat only) (`special/wall.rs:22-47`, `tuning.rs:224`) | `Never` | S2c: needs terrain knowledge in the percept; leave it | C |

Group A is Hook, Bolas, Tornado, WardensCage (4); B is Warhorn, SpellShield, Stasis, BlindRage, StillMind (5); C is
RebirthEgg, ForgetMeNotSeed, SwapStick, LanternWard, ScoutFalcon, WallBuster (6): 15 in all. This packet rules Hook and
Bolas; Tornado and WardensCage stay `Never` for the reasons in the table (listed, not tested).

### Facts the rules rest on (file:line)

- The shot hits **the first enemy on its line**, mobs included: `E:637-671` `first_hit` over every fighter with `enemy()` true
  (`E:627`, `plague_zombies::hostile`), width added to the body box, line of sight required; `E:759-800` flies it
  175 u a tick, 700 u (Hook) or 550 u (Bolas) at most, removes it when it hits, clips or ends. So a mob standing between
  takes the pull or the root (and a sleeping camp mob is woken): the rules hold the cast while one is near the line.
- A shot has **no target actor**: it flies along `aim_end(from, aim, range)` (`E:730-742`), so the aim is a point; the sim
  reads only x, y of the command (id is ignored for these two). `CastAim::Point(foe.at)` is what `Refs::cast` already sends
  (`refs.rs:144`).
- Cost of a miss: `casts.rs:110-125`: the shot is launched (`effects::cast` returns true), the charge is spent and the
  cooldown starts whether it lands or not.
- A pulled or rooted victim: Hook `control(.., Control::Pull)` (`E:781`), Bolas `apply_status(.. Root 1500)` (`E:782-788`);
  both skip a victim in stasis or behind a Spell Shield (`defended`, `E:540`).
- The foe is the existing `Foe` of `exec/spell.rs::foe`: for `K::Fight` with `Target::Actor(key)` the living player of that
  key (`exec/spell.rs:98-107`). This packet adds `targetable`.
- What the seat sees: `PlayerSeen.targetable` (`percept.rs:116`), `Me.effects` bits stunned 1, silenced 4, casting 4096
  (`packages/shared/src/status.ts:38-50`; the sim refuses a non-Cleanse cast stunned, silenced or casting, `C:86-107`).
- No RNG draw, no state: the gate, the table and the scene stay pure.

## 4. New paths

- `crates/rh-policy/tests/raid_spells2a.rs` (the oracle)

## 5. Exclusive write scope

The implementer may change only:
- `crates/rh-policy/src/bots/raid/spells.rs` (two rows; one new `When` variant with a lower bound, see below)
- `crates/rh-policy/src/bots/raid/exec/spell.rs` (the scene and the held-shot conditions)
- `crates/rh-policy/src/bots/raid/acts.rs` only if a new aim is needed (none is: `CastAim::Point`)
- in-crate tests in those files

Not `exec/combat.rs`, `exec/farm.rs`, `exec/elite.rs` (another packet changes them), not `exec/sustain.rs`, `arbiter.rs`,
`refs.rs`, anything under `crates/rh-sim` or `rh-headless`, any existing test (see "Conflicts", section 8).

## 6. Contracts and references

C-INPUT, C-OBS (only the frame), C-RNG/C-TICK (no draw): a rule must not add a draw on the arbiter's stream, and must not make
the spell executor urgent beyond what it is today (`urgent` stays "impaired with a castable Cleanse"). A human seat only
(`bot: false`, `casts.rs:57`).

### The rules (precise)

Both rows go after ShoulderCharge and before the `never` rows, **Hook then Bolas** (one cast a think, first ready rule wins
in table order, `decide_cast`). Both: `FoeKind::Player` only, aim `RuleAim::FoePosition` (`CastAim::Point(foe.at)`).

| | Hook (2) | Bolas (19) |
|---|---|---|
| When | `Foe` is a player, **220 <= distance <= 450** | `Foe` is a player, **distance <= 350** |
| Why | below 220 u the pull (it ends 42 u from the seat) gains nothing; above 450 the foe moves out of a 700 u shot during the 8-14 ticks between ask and send | the root is worth most on a foe that is close enough to be hit or chased; 550 u shot, 350 leaves margin for the same delay |

`When::Foe` has only `within`, so Hook needs a lower bound: add a `When` variant (the implementer's choice of name; the
oracle goes through the executor and never names it).

Shared conditions, in the executor (`exec/spell.rs`), not in the table:
1. The goal is `GoalKind::Fight` on a player (`foe()` already returns a `Player` foe only then); the foe is **alive and
   `targetable`** (add `targetable` to the `Fight` arm of `foe()`; the existing Smite and Turret rules never see a player foe).
2. **No living hostile mob within 48 u of the segment from the seat to the foe** (hostile as `exec/spell.rs::hostile`: not a dummy,
   not a zombie of the seat's faction). The shot would hit it first and a sleeping camp mob would be woken.
3. The seat is **not stunned (bit 1), silenced (bit 4) or casting (bit 4096)**: the send would drop the act and `CastGate`
   would then hold the slot for 2 s.
4. Not for an elite or a camp mob foe, in any goal (the kinds list is `[Player]`).

Why these and not wider: quality bar is "certainly useful". Hook's pull and Bolas's root on a player the seat is already
fighting and may fight from here are; on a camp mob they wake or drag a mob out of its band, and a root on a melee mob does not stop its
attack.

## 7. Behaviour to preserve

- `raid_spells.rs`, `raid_spells_fix.rs`, `raid_spell_reach.rs` stay green, except the one expectation in section 8.
- A seat holding no spell plays as before (`no_spell_seat_plays_as_before`); `review_tests.rs` `urgent` false without a
  castable Cleanse; registry order and the arbiter stream untouched.
- Smite, Turret, ShoulderCharge, Cleanse answer as before in every scene of `raid_spells.rs` and `raid_spells_fix.rs`.

## 8. Acceptance checks

- `export PATH="$HOME/.cargo/bin:$PATH"; CARGO_BUILD_JOBS=4 cargo test -p rh-policy --test raid_spells2a` passes (9 tests).
- `scripts/gate.sh --quick --oracle packet/P-RAID-S2a`.
- No oracle file changed, nothing outside the write scope.

### Conflicts with existing tests (for the coordinator, before an implementer starts)

| Test | Asserts | Conflict |
|---|---|---|
| `crates/rh-policy/tests/raid_spells.rs:182` `this_packet_rules_four_spells_and_leaves_the_rest_not_ruled_yet` | the set of rows with `when != Never` is exactly `{ShoulderCharge 3, Turret 5, Smite 8, Cleanse 16}` | **conflict, unavoidable**: any rule added to the table breaks it. Needs an `oracle-amend:` commit (alone) changing the expected set to those four plus `HOOK 2` and `BOLAS 19`, and its name/message ("the other fifteen are `Never`" becomes thirteen). The owner approves the amend. I was told not to edit an existing test file, so **no `oracle-amend:` commit is on this branch**; until it lands, `scripts/gate.sh` shows this one test red for any correct implementation. |
| `raid_spells.rs:883` `an_unruled_spell_is_never_cast` | live seat holding Hook + Tornado on a quiet seed casts nothing in 200 ticks | none expected: no enemy player is in the scene, so the Hook rule has no foe. Its message ("not ruled yet") becomes stale for Hook. |
| `raid_spells.rs:159` (one row per award, 19 rows), `:322` (single use never at a camp mob), `:345` (registry 9, last has no kinds) | table shape | none: no row added, Hook and Bolas are reusable, the executor is unchanged in shape. |
| `raid_spells_fix.rs`, `raid_spell_reach.rs`, `review_tests.rs:270-` | fix rules, reach, `urgent` | none (rules do not touch the Elite or Farm scenes; `urgent` unchanged). |
| `spells.rs` in-crate `the_hand_copied_ranges_equal_the_sims_numbers` | `SMITE_RANGE`, `CHARGE_RANGE`, `TURRET_RANGE` equal the sim's | none; if the implementer adds `HOOK_RANGE` or `BOLAS_RANGE` copies it should extend that test (in-crate, in scope). |

## The oracle, test by test (`crates/rh-policy/tests/raid_spells2a.rs`)

Hand-built percepts through `Spell::ride_along` (as `raid_spells_fix.rs`), seat at (4000, 4000), one spell in wire slot 1,
enemy player (faction Fire) `d` u east, goal `Fight` on that player.

| Test | Kind | Asserts |
|---|---|---|
| `hook_is_cast_at_a_fought_player_from_a_gap` | RED on the base | a Hook cast, `CastAim::Point` at the foe's position, at 230, 330, 440 u |
| `hook_is_not_cast_at_a_player_in_melee_or_beyond_the_shot` | guard | nothing at 60, 150, 210, 460, 650 u |
| `bolas_is_cast_at_a_fought_player_within_reach` | RED on the base | a Bolas cast at the foe's position at 60, 200, 340 u |
| `bolas_is_not_cast_at_a_player_beyond_reach` | guard | nothing at 360, 450, 600 u |
| `neither_shot_is_cast_at_an_elite_or_a_camp_mob` | guard | seat in camp 0's band (`in_camp_0`), goal Elite (elite) or Farm (regional) at 150 and 300 u: nothing, both spells |
| `neither_shot_is_cast_without_a_fight_on_a_targetable_player` | guard | goal Sustain, Farm, a Fight on another key; a dead foe; a foe with `targetable = false`: nothing |
| `neither_shot_is_asked_while_the_seat_is_stunned_silenced_or_casting` | guard | effects 1, 4, 4096 with a foe at 300 u: nothing |
| `a_shot_is_held_while_a_mob_stands_on_its_line_and_asked_when_the_line_is_clear` | RED on the base (clear cases) | a regional or elite mob on the line, or 30 u off it: nothing; 150 u off, dead, a dummy, or behind the seat: the cast |
| `cleanse_still_comes_before_a_shot` | guard | rooted seat holding Cleanse (slot 0) and Hook (slot 1): one cast, Cleanse |

Result on the base: **3 red** (the three RED rows above), **6 green** (guards, vacuous on the base: no spell is ruled,
so nothing is asked; they become real once the rules exist, and a naive rule without the player-only kind, the line check,
the targetable check or the cast-state check fails one of them). Not run against an implementation (the author does not
implement); the numbers in the tests were chosen well inside the edges of each band.

**Live test: none, on purpose.** `raid_spells.rs`'s live harness seats one RaidBot alone; a live Hook or Bolas needs a
second, enemy-faction seat that stands still in view, *and* the arbiter choosing a `Fight` goal on it (the goal choice
depends on tier, persona, weapon and values that no test of this packet pins), so the scene would be either a non-deterministic
sweep or a test of the arbiter, not of the rule. The sim side (a shot that roots or pulls its first enemy) is
`crates/rh-sim`'s own tests (`quests/board/rewards/effects/tests.rs`). Said plainly: **no test shows the sim accepting these two
casts from a bot seat**; the act shape is the same `Act::Cast` / `CastAim::Point` that ShoulderCharge's live path
(`Refs::cast`, `refs.rs:119-165`) already sends.

## Oracle files (read-only for implementers)

- `crates/rh-policy/tests/raid_spells2a.rs`

(No `stub:` commit: the tests use only types that exist on the base, so each fails on an assertion. No `oracle-amend:`
commit: see the conflict row for `raid_spells.rs:182`, waiting on the coordinator and owner.)

## 9. Validation commands

- `export PATH="$HOME/.cargo/bin:$PATH"; CARGO_BUILD_JOBS=4 cargo test -p rh-policy --test raid_spells2a --test raid_spells --test raid_spells_fix --test raid_spell_reach`
- `scripts/gate.sh --quick --oracle packet/P-RAID-S2a`

## 10. Risk and suggested model tier

Sonnet implements (one crate, a table row each, one executor condition); reviewer-opus reads it for the bot's priorities.
Risks: a shot is a skillshot (it can miss a moving foe and still spends the cooldown), the mob-on-the-line check is a 2-D
segment test (walls and the foe's body are not modelled), and the amend of `raid_spells.rs:182` must be approved first.

## 11. Known traps

- Do not copy a rule from the spell's name: Tornado does not pull what is bigger than the caster (`E:975-998`).
- An npc key is new every frame; a player key is stable. A shot needs the foe's position, not an actor id (`CastAim::Point`).
- The shot hits the first enemy on the line, mobs included.
- A stunned or silenced seat's cast is dropped at send time; the gate would hold the slot 2 s: do not ask it.
- Table order is priority: Hook before Bolas, both after ShoulderCharge; `decide_cast` returns one cast a think.

## Notes from previous attempts
