# P-RAID-S-fix2: a quest spell aimed at a camp's foe is cast only from where the camp's mob can reach the seat

## 1. ID and outcome

- **Serves:** `docs/backlog/BALANCE_LAB_PATCH.md`, wave 5, P-RAID-S (quest spell cast rules); the single note of the
  reviewer-opus merge review of integration 88. Headless human seats only.
- **Implementer tier:** implementer-sonnet. **Reviewer:** reviewer-opus (its live test is a reach scene).
- **Integration base:** `coord/int-88` @ `3618186b1`. Straight line; do not merge the base in.
- **D-rules touched:** D1 loose proof (RaidBot has no legacy counterpart; the leash is the sim's own) · D2 n/a ·
  D5/D6/D9 none (no wire, no observation field, no `rh-sim` change, no RNG draw).

**The defect (verified by reading).** `fn foe` in `crates/rh-policy/src/bots/raid/exec/spell.rs:75-104` names, on an
Elite or Farm goal, the camp's elite (or nearest member) as the spell scene's foe at ANY position of the seat
(`:77-90`; the only filter is `hostile` and `m.camp == Some(*c)`). The `When::Foe` rows of
`crates/rh-policy/src/bots/raid/spells.rs` (Smite `:117-124`, `within: SMITE_RANGE` 450 u at `:91`; Turret `:125-133`,
`within: TURRET_RANGE` 340 u at `:95`; both `kinds: ELITE_ONLY`, `:97`) hold as soon as `f.distance <= within`
(`spells.rs:161-164`). Meanwhile `Reach::engage` (`exec/combat.rs`) may still be walking the seat to the camp's band:
the reach packets (P-RAID-REACH-1, P-RAID-REACH-1-fix) promise a seat's damage lands from where the mob can reach it
(`may_fight_from`, `exec/combat.rs:67`; `locks_from_here`, `exec/combat.rs:~95-112`, which also accepts a mob that is
`engaged` within `DEFEND_U` = 96 u, `exec/farm.rs:19`).

**Why it matters (sim fact, verified).** `mob_ai.rs` `fight` (`crates/rh-sim/src/systems/mob_ai.rs:267`): a mob whose OWN
position fails `leash(.., margin 0)`, or that has had no combat for `MOB_GIVE_UP_SECONDS` (20 s), gives up and walks home
(`give_up`). A mob woken by a Smite from outside its band chases to the band's edge, gives up and heals; the 220-damage
cast and its 20 s cooldown are wasted, and the elite executor's `fight_since` clock starts early. Small camps bite: seed 7
camp 50 (radius 96, 11 cells) has a band about 96 u deep, so a seat walking toward it is inside Smite's 450 u long before
the band.

**The one behaviour change.** In `foe()`, a camp foe (Elite or Farm goal) is kept only when
`combat::may_fight_from(cx.paths, camp, goal.kind == K::Elite, cx.p.me.at)` holds, OR that foe is `engaged` and within
`DEFEND_U` of the seat (the test `locks_from_here` applies, but for every weapon: a spell's range does not depend on the
weapon in hand). A player foe (Fight goal) is unchanged. A map without layers, an unknown camp or a camp without spots
answers "may fight" (`combat.rs`), so layer-less hand-built frames are unchanged. Also: the one-line comment fix on `foe`
("its elite while one stands" means the elite labelled with the camp).

Checked for the rule's soundness: a melee seat can still cast (a melee seat walks into the band and fights from 40 u, which
is inside it), and `may_fight_from` is the same function the seat's own approach uses, so a seat that fights is, by
construction, in a place where this rule lets it cast. The rule does not touch the other rows (`Impaired`, `Leaving`,
`Crowd`, `HpBelow`) because only `foe` is changed.

## 2. Dependencies

`coord/int-88` @ `3618186b1` (P-RAID-S, P-RAID-S-fix, P-RAID-REACH-1, P-RAID-REACH-1-fix). None else.

## 3. Relevant legacy paths

Legacy has no RaidBot or quest spells; the sim's own rules are the reference (read, not changed):
`crates/rh-sim/src/systems/mob_ai.rs:267` (give-up test), `:62` (`last_combat_at` set on wake). Bot side:
`crates/rh-policy/src/bots/raid/exec/spell.rs:75-104` (`foe`, to change), `:107-148` (`scene`), `:190-204` (`ride_along`);
`exec/combat.rs:67` (`may_fight_from`, `pub`), `exec/combat.rs:~95-112` (`locks_from_here`); `exec/farm.rs:19` (`DEFEND_U`,
`pub`); `spells.rs:117-133, :161-164`. Not established: `mob_ai.rs` "around line 191" of the brief (a wake path) was not
read; the give-up at `:267` is what the argument needs.

## 4. New paths

- `crates/rh-policy/tests/raid_spell_reach.rs` (live, release; the oracle)
- The implementer may add in-crate tests inside `exec/spell.rs` (Turret, and the engaged exemption).

## 5. Exclusive write scope

- `crates/rh-policy/src/bots/raid/exec/spell.rs` and that module's own in-crate tests.

Nothing in `rh-sim`, `rh-headless` or `tools/`; no existing test or capture.

## 6. Contracts and references

C-INPUT (casts still go through `validate` + `Sim::submit`), C-OBS (`may_fight_from` reads only the public static map
layers the seat was handed, as the reach packets' decision 2), C-RNG (no draw added), C-TICK (no sim change).
`docs/OWNER_DECISIONS.md` row "Balance patch, test bot, per-elite numbers" (2026-11-22) governs the bot; no decided
subject is changed.

## 7. Behaviour to preserve

- Large-camp Smite: `raid_spells.rs::smite_is_cast_at_an_elite_the_seat_is_fighting_and_the_sim_takes_it` (guard, green on
  the base; it must stay green: a seat that fights an elite for 3 s inside 450 u casts within 1 s).
- The pure table tests (`decide_cast` on hand-built scenes) in `raid_spells.rs`: unchanged, `foe` is not on their path.
- Player foes (Fight goal), Cleanse, ShoulderCharge and every other row unchanged.

## 8. Acceptance checks

- `scripts/gate.sh --quick --base coord/int-88 --oracle packet/P-RAID-S-fix2` passes (after the coordinator's amendment
  below).
- `cargo test --release --locked -p rh-policy --test raid_spell_reach --test raid_spells --test raid_spells_fix`.
- Turret: not covered by the live scene (a second spell changes the scene's fight; not cheap). The implementer adds an
  in-crate test in `exec/spell.rs`: a layer-bearing frame, the seat outside the band with an elite 300 u away on a Turret
  seat casts nothing; the same seat inside the band casts it.

## Oracle files (read-only for implementers)

- `crates/rh-policy/tests/raid_spell_reach.rs`
- `docs/backlog/packets/P-RAID-S-fix2.md`

### The oracle's tests, measured on the base (`3618186b1`, release)

Scene: lab seed 7, camp 50 (the Gaoler: radius 96, area 11, elite spot 0 at (4304, 752)), the seat started at (4423, 525)
(256 u from the Gaoler, outside the band, inside Smite's 450 u: the trace of `tests/raid_reach.rs`), 36 banked stacks, two
large potions, the Smite award, `raid_reach_fix.rs`'s elite prices. The seat's own commands are logged; the band is
`leash_ok(data, 50, elite spot, seat, 0.)`.

| test | base | what it measured |
|---|---|---|
| `a_wand_seat_casts_smite_at_the_camp_50_elite_only_from_where_it_can_reach` | RED, assertion 1 | one Smite sent at tick 6, 231 u from the elite, outside the band; the seat is in the band for 209 ticks later (Smite on its 20 s cooldown by then, so no cast from inside) |
| `a_bow_seat_...` | RED, assertion 1 | the same cast at tick 6 (231 u); 157 ticks in band |
| `a_sword_seat_...` | RED, assertion 1 | casts at tick 6 (231 u) and tick 421 (170 u), both outside the band; 181 ticks in band |

Each test asserts (1) no Smite is sent while the seat is outside the band with no elite within `DEFEND_U` (96 u) of it, and
(2) at least one is sent from inside the band or with an elite beside it, so a seat that never casts cannot pass. "Engaged
within `DEFEND_U`" is read in the test as "an elite alive within 96 u of the seat" (the percept's engaged flag is a subset,
so the test's exemption is the looser one).

NOT established: that the oracle is GREEN on a correct fix. The author must not implement, and a throwaway edit of
`spell.rs` to check it was declined by the sandbox. Risk for the implementer to report: assertion 2 needs the fixed seat to
reach the band with Smite off cooldown and the elite alive; on the base the seat is in the band for 157-209 ticks (8-10 s)
within the 2 400-tick run, and the cooldown is 20 s from the first cast, which no longer happens at tick 6. If assertion 2
fails for a reason other than the rule (the seat leaves, dies, or the elite is never within 450 u in the band), the
coordinator decides; do not loosen the test.

## Existing tests that pin what this packet changes

| test | asserts | state under a correct fix |
|---|---|---|
| `raid_spells.rs::smite_is_cast_at_an_elite_the_seat_is_fighting_and_the_sim_takes_it` (live, six seeds, Executioner camp) | a seat with an Elite goal and an elite within 450 u for 60 ticks has spent Smite within 20 more | named guard: must stay green. Not run against a fix; if a seed's fight happens outside the band the test goes red and this is a conflict for the coordinator |
| `raid_spells.rs` hand-built table tests (`smite_is_cast_at_an_elite_in_reach...`, `turret_is_cast_on_the_seat...`, ...) | `decide_cast` on hand-built `SpellScene`s | unchanged (`foe` is not on their path) |
| **`raid_spells_fix.rs::cleanse_smite_and_turret_answer_as_before` (`:242-285`)** | via `Spell::ride_along`, seat at (4000, 4000) on the seed-1 fixture map, an Elite goal on camp 0, an elite 300 u away (`engaged: false`, `camp: Some(0)`): Smite is asked (`:270`) and Turret is asked (`:282`) | **CONFLICTS.** Measured: the fixture has layers, camp 0 has five spots and no elite spot, and (4000, 4000) is outside every spot's margin-0 band, so `may_fight_from` is false and the engaged exemption does not apply (300 u > 96 u). Under the rule both asserts go red. **`oracle-amend:` for the coordinator and owner: see below.** |

### Amendment for the coordinator (existing file NOT edited here)

`crates/rh-policy/tests/raid_spells_fix.rs`, `cleanse_smite_and_turret_answer_as_before`: the Smite and Turret halves
(lines 262-285) must give the seat a place inside camp 0's band, or an engaged elite within 96 u. The smallest change that
keeps the meaning ("Smite at an elite in reach, Turret with an elite in its reach"): move the seat for those two halves to
a point inside the margin-0 band of one of camp 0's spots on the fixture map (read the spot from
`arena.map.layers().data().camps[0].spots` and use `ME` = spot 0), with the elite 300 u from it; the percept's `me.at` comes
from `frame` (`:72`, uses `ME`) so the const `ME` or `frame` takes the position. The Cleanse half is unaffected. The
implementer cannot edit the file; the amendment must be committed alone as `oracle-amend:` before dispatch, with the
owner's approval.

## 9. Validation commands

- `export PATH="$HOME/.cargo/bin:$PATH"`
- `cargo test --release --locked -p rh-policy --test raid_spell_reach` (red on the base: three failures)
- `cargo test --release --locked -p rh-policy --test raid_spells --test raid_spells_fix`
- `scripts/gate.sh --oracle-check --base coord/int-88` (on `packet/P-RAID-S-fix2`)

## 10. Risk and suggested model tier

Low code risk (one filter in one function); implementer-sonnet, reviewer-opus. The risk is behavioural: a seat that fights
from just outside the band by the engaged exemption, or an elite fight that never casts. A headless lab run is not
required; the live scene is the proof.

## 11. Known traps

- `may_fight_from` takes `elite: bool`: pass `goal.kind == K::Elite` (an Elite goal's foe may fall back to a normal member
  when the elite is dead; the helper then reads the union of spots only when the elite flag is false, so pass the flag of
  the foe actually picked, `m.class == MobClass::Elite`, if the two differ; the implementer decides and says which).
- `cx.paths` is `&mut RoomPathService` in `Cx`; the helper takes `&RoomPathService` (`&*cx.paths`).
- Do not change `scene`'s `hostile_distances` or `leave_away`: only the foe is gated.
- The player-foe arm must stay as it is.
