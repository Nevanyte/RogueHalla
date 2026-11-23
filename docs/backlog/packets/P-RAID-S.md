# P-RAID-S: RaidBot casts the quest spells it holds, by a rule table

- **Serves:** not in ORDER_OF_OPERATIONS: owner-approved balance patch (`docs/backlog/BALANCE_LAB_PATCH.md` wave 5, row P-RAID-S).
- **Implementer tier:** implementer-sonnet (one crate, no `rh-sim` change, no RNG draw, no state)
- **Reviewer:** reviewer
- **Integration base:** `coord/int-87` @ `c5b51f46a`.
- **D-rules touched:** D1 n/a (the quest board is Rust-native; no legacy to match) · D2 n/a · D5/D6/D9 none
- **Decisions in `docs/OWNER_DECISIONS.md`:** none found for this subject (searched "spell", "cast").

## 1. ID and outcome

Today no RaidBot code sends a cast: `crates/rh-policy/src/bots/raid/spells.rs` is a one-line placeholder, and the lab's
`quests.spell_casts` (`rh-headless/src/balance_lab/observer.rs:606`) is empty in every run, so no spell reward can be valued.
After this packet a `RaidBot` seated as a human (`bot: false`) casts the quest spells it holds by a rule table: this packet
is the **seam and four proven rules**; the other fifteen spells get a row that says `When::Never` ("not ruled yet"), and a
later packet (P-RAID-S2) fills rows one at a time, so the table's shape matters more than its coverage.

## 2. Dependencies

Base `coord/int-87`. Touches nothing `rh-sim`. `values.rs` stays untouched (what a spell is worth once cast is P-RAID-Q3c's).

## 3. Relevant legacy paths

None (Top 25 board is Rust-native, `docs/quests.md`). The oracle is the current Rust code at the base; every sim fact below was
read at `c5b51f46a`.

### Facts verified at the base (file:line)

**The sim's cast gate** (`crates/rh-sim/src/quests/board/rewards/casts.rs`)
- `casts.rs:36-125` `cast`: refused silently (return false, no event, no reason) unless phase Active, slot 1 or 2, finite
  x and y (`:44-50`); the seat stands, is **not `seat.bot`**, not in stasis (`:57`); the slot holds a spell (`:64-67`) with
  `charges > 0` after `recharge` (`:69-72`); not silenced; not (stunned, taunted or pulled) unless the spell is Cleanse;
  no cast in progress (`statuses.cast`); not within `TELEPORT_GRACE_MS` of a teleport; Spell Shield not while shielding
  (`:86-107`). Then `effects::cast` may still return false (`:110-114`: no valid target, nowhere to put it) and nothing is spent.
- A spell with cooldown 0 (`awards.rs:cooldown_ms`) is single use: the slot empties on cast (`casts.rs:117-118`). Reusable:
  `charges -= 1`, `ready_at = now + cooldown` when it was full (`:119-123`). `recharge` runs every tick (`casts.rs:22-33`),
  so the view's `charges` is current; `ready_at` is the **next recharge time** (stale-or-now when full), not a gate: a spell
  is castable exactly when `charges >= 1`. (The coordinator's design said "has a charge and `ready_at` has passed": the code
  gates on the charge alone. The oracle follows the code.)
- Slot mapping: with a quest deal, ability key Q is spell slot 1 and E is slot 2; Wish is never a spell
  (`crates/rh-sim/src/systems/abilities/mod.rs:99-103`). The wire `QuestSpell.slot` is **0-based** (`quest_view/board.rs:127-133`):
  wire slot 0 = Q, 1 = E. `Act::Cast.slot` here is that wire slot.
- Command: `PublicCommand::Cast { slot: rh_sim::abilities::Slot, x: f32, y: f32, id: Option<PlayerId>, t, seq }`
  (`crates/rh-protocol/src/command.rs:132`, `bots/bot.rs:855-866`). `id` is the target actor handle; the sim keeps it only
  if the seat saw that actor (`input.rs:371-390`).

**What each of the four rules needs** (`rewards/effects.rs`, `board/tuning.rs`; none of these ranges is in
`rh_sim::quests::public_rules()`, which holds only the eight quest numbers, so each is a **hand copy** the row must name)
- **Cleanse** (`effects.rs:475-485`): clears Root and Stun, any aim. Cooldown 30 000 ms (`tuning.rs:155`). It is the one spell a
  stunned seat may cast; taunted and pulled are also allowed for it. Reusable.
- **Smite** (`effects.rs:372-380`): `target.filter(|t| focused(cx, ix, *t, TOP25.smite_range, false))`. `focused(.., player=false)`
  requires the target to be **a non-player** (`effects.rs:631-635`: `cx.w.is_player(j) == player`), alive, hostile, visible,
  within `smite_range` 450 (`tuning.rs:170`) plus `RANGE_SLACK` 32 (`abilities/mod.rs:250`). **A Smite on a player is refused.**
  Cooldown 20 000 ms. Reusable. **This contradicts the coordinator's design ("Smite on the foe when it is an elite or a
  player")**: the rule here is elites only (a camp mob is a legal target for the sim, but the table spends Smite on elites).
- **ShoulderCharge** (`effects.rs:598-625`): dashes up to `charge_range` 220 (`tuning.rs:186`) toward the aim (`aim_end`: from the
  caster toward the aim point, clipped by walls, at most 220), stunning what it meets. Refused only when it would move
  under 1 u. Cooldown 12 000 ms. Reusable.
- **Turret** (`effects.rs:354`, `terrain.rs:210-249`): placed at the aim point within `deploy_range` 400 of the caster
  (`tuning.rs:208`), room for the body and line of sight; a seat's own position works. The turret shoots within
  `turret_range` 340 (`tuning.rs:203`), 25 s (`turret_ms`), cap 1 (a second replaces the first). Cooldown 30 000 ms. Reusable.
  The rule uses **340** (what the turret reaches), not 400.

**The seat sees** (`crates/rh-policy/src/bots/raid/percept.rs`)
- Held spells: `RaidPercept.quests: Option<WireQuestView>`, `.spells: Vec<WireQuestSpell { slot, reward, charges, ready_at }>`
  (`rh-protocol/src/state/quest.rs:60-65`). `None` for a bot seat.
- Its own statuses: `Me.effects: u16` (`percept.rs:20-33`), the sim's `statuses.effects` bits (`packages/shared/src/status.ts:38-50`:
  STUNNED 1, ROOTED 2, SILENCED 4, CASTING 4096, ...). **The frame shows rooted, stunned, silenced and casting. It does
  not show taunted or pulled**: those are quest-control rows (`effects.rs:293-317`), not status instances, and no bit carries them.
  So "impaired" in the scene is **rooted or stunned** (bits 1 and 2). A taunted or pulled seat that holds Cleanse is
  invisible to the bot; that is a known gap, not tested.
- Actors: `RaidPercept.mobs: Vec<MobSeen { key, at, hp_share, alive, class: MobClass::{Regional, Elite, Zombie, Dummy}, camp, .. }>`
  and `.players: Vec<PlayerSeen { key, at, targetable, .. }>`. Keys resolve to this frame's handle through `Refs::actor`
  (`refs.rs:130-136`). An npc key is new every frame.
- Leaving a lost fight: the running goal is `GoalKind::Sustain` with `Target::Camp(_)` (`exec/sustain.rs:386`); the retreat walks
  away from `Sustain::nearest(cx)` (`exec/sustain.rs:387-407`, `away_point`). The goal carries no direction: the scene
  computes it as the unit vector from the nearest living hostile mob to the seat.

**The executor seam**
- `Executor::ride_along` (`goal.rs:265-290`, called every think for every executor in registry order, `bot.rs` think()), used by
  `exec/sustain.rs:524-533` (potion). An executor never emits a `PublicCommand`; acts go to the paced `ActQueue`
  (`acts.rs`: a pause of 180-420 ms before an act is due, `release` sends one per frame, drops an act older than
  `ACT_TTL_MS` 4000 or whose `Refs::act` is `None`) and become commands in **`Refs::act` (`refs.rs:95-143`)**, not in `bot.rs`
  (the coordinator's design named `bot.rs`; `bot.rs` needs no edit, `refs.rs` does). `Refs::act` is an exhaustive `match`
  on `Act`: a new variant needs an arm.
- Think cadence: an executor's `ride_along` runs on a think tick (tier cadence, 1.2-2 s mean, or every frame while committed or
  `urgent`) (`bot.rs` think()). The plan's oracle is "within 500 ms": an `urgent()` that answers true when the seat is
  impaired and holds a ready Cleanse makes that frame a think tick (`goal.rs:239-244`), then the act queue's pause (<= 420 ms)
  decides. `review_tests.rs:270-` requires every registry executor's `urgent` false (and no draw) in a seat that holds no
  spell: keep it so.
- Registry: `exec/mod.rs:17-27`; the registry order is behaviour and the arbiter stream is lent in that order; a new executor
  registered LAST that draws nothing moves no existing draw.

**Wire reward ids** (the id in `QuestSpell.reward` is `Award as u8`, `quest_view/board.rs:129`). `Award` is `pub(crate)`; the public
list is `rh_sim::quests::board::catalog::{DEFINITIONS, RewardId, RewardKind}` (`catalog.rs:100-158`, used the same way by
`quest/decide.rs:66-100`). `RewardId as u8 == Award as u8` up to 21 (WallBuster 20, BlindRageAndStillMind 21 = Award BlindRage);
**Award 22 is StillMind (the bundle's second award) and every RewardId after 21 is one lower than its Award**
(FeedOrHuntDamage RewardId 22 / Award 23, WarmCoat 23 / 24, BreakersHelm 24 / 25). The sim has **19 non-permanent awards**
(10 reusable: Hook 2, ShoulderCharge 3, Tornado 4, Turret 5, Smite 8, SpellShield 9, LanternWard 15, Cleanse 16, ScoutFalcon 17,
Bolas 19; 9 single use: WardensCage 0, RebirthEgg 6, Warhorn 7, ForgetMeNotSeed 12, SwapStick 13, Stasis 18, WallBuster 20,
BlindRage 21, StillMind 22) and 7 permanent (CrownPower 1, TheBargain 10, TradeUp 11, ChefsKiss 14, FeedOrHuntDamage 23,
WarmCoat 24, BreakersHelm 25). The patch doc's "18 spells" counts RewardIds (BlindRage and StillMind as one).

## 4. New paths

- `crates/rh-policy/src/bots/raid/exec/spell.rs` (the executor; no goal kinds)
- `crates/rh-policy/tests/raid_spells.rs` (the oracle)

## 5. Exclusive write scope

The implementer may change only:
- `crates/rh-policy/src/bots/raid/spells.rs` (the rule table, scene, decision, retry gate)
- `crates/rh-policy/src/bots/raid/exec/spell.rs` (new)
- `crates/rh-policy/src/bots/raid/exec/mod.rs` (the registry line, last)
- `crates/rh-policy/src/bots/raid/acts.rs` (`Act::Cast`, `CastAim`)
- `crates/rh-policy/src/bots/raid/refs.rs` (the `Act::Cast` arm of `Refs::act`: **added to the coordinator's list**, the real
  seam; `bot.rs` is not needed)
- `crates/rh-policy/src/bots/raid/percept.rs` only if a fact the scene needs is not exposed (none found missing)
- `crates/rh-policy/src/bots/raid/mod.rs` (doc list)

Not `values.rs`, `exec/sustain.rs`, `exec/elite.rs`, `arbiter.rs`, `bot.rs`, anything under `crates/rh-sim`, any
existing test.

## 6. Contracts and references

C-INPUT (the cast goes through `validate` + `Sim::submit` like a human's), C-OBS (only what the frame shows), C-RNG/C-TICK
(no draw, no tick change), R-AUTH. The seat must be a human seat (`bot: false`): the sim refuses a bot seat's cast
(`casts.rs:57`). The headless lab seats RaidBots that way with `--human-seats`.

## 7. Behaviour to preserve

- A seat holding no spell plays exactly as before: the golden state hash in `raid_spells.rs`
  (`no_spell_seat_plays_as_before`, captured on the stub, i.e. today's behaviour) must stay equal. This is the guard that the
  new executor draws nothing from the arbiter stream and moves no existing draw. (It is a golden of today's whole-seat
  behaviour: another packet merged before this one lands that moves RaidBot behaviour moves it; the coordinator re-captures
  only on a documented reason.)
- Every existing test, capture and vector. `raid_decide.rs` (seed 4) seats a RaidBot holding Cleanse and Forget-Me-Not for
  1 200 ticks (`:30-31`, `SLOT_CASES`): it must not start casting there unless the seat is rooted or stunned.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-RAID-S` passes; the new tests in `tests/raid_spells.rs` pass.
- No oracle file changed, nothing outside the write scope.

## The design (coordinator's decisions, with what the code changed)

1. **Seam and four rules; fifteen rows `Never`.**
2. **`Act::Cast { slot: u8, reward: u8, aim: CastAim }`** in `acts.rs`; `CastAim` is `OwnPosition | Point(Vec2) | Actor(String)`
   (an actor is the rendered key `p<id>` / `n<id>`, resolved through `Refs::actor` at send time like `Act`'s other keys).
   `Refs::act` sends it only if, in the frame at send time, that wire slot still holds that reward with `charges >= 1` and the
   seat is not silenced or casting (bits 4 and 4096), nor stunned unless the reward is Cleanse; otherwise `None` (dropped,
   no order token spent). Wire slot 0 goes out on `Slot::Q`, 1 on `Slot::E`. Aim: `OwnPosition` sends the seat's
   own x, y; `Point(p)` its x, y; `Actor(key)` sends `id: Some(handle)` (the sim reads the target by id; x, y need only be finite, send the seat's own); an actor that is
   not in the frame resolves to nothing (dropped).
3. **Rules are data** in `spells.rs`. Public shapes (the stub has them):
   ```rust
   pub enum FoeKind { Player, Elite, CampMob }
   pub struct Foe { pub key: String, pub kind: FoeKind, pub at: Vec2, pub distance: f64 }
   pub struct SpellScene {
       pub me: Vec2, pub impaired: bool, pub foe: Option<Foe>,
       pub hostile_distances: Vec<f64>,      // distance of each living hostile actor, any order
       pub leave_away: Option<Vec2>,         // Some(unit vector away) while the goal is a leave
       pub hp_share: f64,
   }
   pub struct HeldSpell { pub slot: u8, pub reward: u8, pub charges: u8, pub ready_at: f64 }
   pub enum When { Never, Impaired, Foe { kinds: &'static [FoeKind], within: f64 },
                   Crowd { at_least: u32, within: f64 }, Leaving, HpBelow(f64) }
   pub enum RuleAim { OwnPosition, FoeActor, FoePosition, AwayAlongLeave }
   pub struct SpellRule { pub reward: u8, pub when: When, pub aim: RuleAim, pub why: &'static str }
   pub const SPELL_RULES: &[SpellRule];      // 19 rows, one per non-permanent award
   pub const CAST_RETRY_WAIT_MS: f64;        // the bounded wait
   pub fn decide_cast(scene: &SpellScene, held: &[HeldSpell], now: f64) -> Option<Act>;
   pub struct CastGate;                      // the one piece of memory
   impl CastGate { pub fn pass(&mut self, ask: Option<Act>, held: &[HeldSpell], now: f64) -> Option<Act> }
   ```
   `SpellScene` is built once per think from the percept: `impaired` = `me.effects & (1|2) != 0`; `foe` = the foe of the running fight
   (goal `Elite`/`Farm` on a camp or a `Fight` on a player: the nearest living hostile actor in the goal's neighbourhood; an
   elite mob is `Elite`, a regional or zombie mob `CampMob`, a player `Player`); `leave_away` as above.
   `decide_cast` is pure: at most one cast; the first ready rule in table order wins, "ready" = the held spell has
   `charges >= 1` and its row's `when` holds. A rule with `Foe` needs `scene.foe`; its `within` is checked against
   `foe.distance`.
4. **The four rules**: Cleanse `Impaired`, `OwnPosition`, why "frees a rooted or stunned seat"; Smite `Foe { kinds: [Elite], within: 450 }`,
   `FoeActor`, why "220 damage to an elite in reach (the sim refuses a player target)"; ShoulderCharge `Leaving`, `AwayAlongLeave`,
   why "a dash of up to 220 u away from a lost fight"; Turret `Foe { kinds: [Elite], within: 340 }`, `OwnPosition`, why "a turret
   shoots the elite that is hurting us (340 u reach)". Ranges are hand copies of `TOP25.smite_range`, `charge_range`,
   `turret_range`: each row's comment names the sim line (`tuning.rs:170,186,203`). Row `reward` is the wire id (`Award as u8`).
   A single-use spell is never spent on a camp mob: no single-use row has `FoeKind::CampMob` in `kinds`. (No single-use row
   has a rule yet, so the oracle's test of it is vacuous today by construction and fires from P-RAID-S2 on.)
5. **`exec/spell.rs`**: no goal kinds (`kinds()` empty), registered last, draws nothing, `urgent` true only when impaired with a
   castable Cleanse held. It builds the scene, calls `decide_cast`, passes the result through its `CastGate`, and returns
   the act from `ride_along`. `CastGate`: after it lets an `Act::Cast` through on a slot it lets the same slot through again only
   when the frame shows the charge spent (`charges` below the charges at the ask) or the cooldown started (`ready_at` later
   than the ask's `now`), the slot's spell changed or emptied, or `CAST_RETRY_WAIT_MS` has passed since the ask. The sim drops a
   refused cast silently; a seat must not resend it every think.
6. **No price changes.**
7. A seat in a live match must be human (`bot: false`).

## The oracle, test by test (`crates/rh-policy/tests/raid_spells.rs`)

Hand-built (pure, `SpellScene` and `HeldSpell` written in the test): the award list and table coverage (list taken from
`DEFINITIONS`/`RewardKind`, the wire id shift after 21 explained in the test), the four-rules-only test, each rule's boundary
(Cleanse impaired or not, Smite 400 u in / 520 u out / player / camp mob, Turret 300 in / 500 out, ShoulderCharge along `+x`),
slot naming, no charge, one cast a think, single-use never at a camp mob (vacuous today by construction, with a positive
control), no spell / executor shape (`registry().len() == 9`, last has no kinds), and the three `CastGate` tests (one ask over
the wait; a spent or recharged slot asks at once, which means **`CastGate::pass` must be called every think, with `None` when
nothing is asked, so it sees the frame**; per slot and per spell).
Live: Cleanse rooted within 10 ticks on seeds 1-4 (Q key; charge spent; root gone), Hook + Cleanse (E key only, slot 1 spent),
a spent Cleanse sends nothing when rooted again (a new seat), Hook + Tornado never cast, Smite at an Executioner camp's
elite over seeds 1-6 (measured on the stub: the seat fights it in reach on all six, 280-1244 ticks; floor 5; Smite spent
within 20 ticks of 60 ticks of fight; Q key, with an actor id).
Guards that already pass on the stub and must stay green: `an_unruled_spell_is_never_cast`, `no_spell_seat_plays_as_before`
(golden `NO_SPELL_HASH`), the award-list test. The golden is the state hash after 600 ticks of a no-spell RaidBot on seed 1
at the arena centre.

Not tested (said plainly): the refusal gate live (a cast the sim refuses silently needs a taunt or pull no test can stage);
`Crowd`, `HpBelow` and `FoePosition` (no rule uses them yet); a stunned seat not sending ShoulderCharge; that `urgent()` stays
false without a Cleanse (`review_tests.rs` pins it).

## Existing tests that pin what the packet touches

| Test | Asserts | Conflict |
|---|---|---|
| `crates/rh-policy/src/bots/raid/review_tests.rs:270-` (`an_urgent_executor_makes_the_arbiter_decide_now...`) | the registry's executors all answer `urgent` false and draw nothing in a no-spell scene | none if the new executor's `urgent` is false without a castable Cleanse |
| `crates/rh-policy/tests/raid_decide.rs` (`SLOT_CASES`, seed 4 holds Cleanse + Forget-Me-Not, 1200 ticks) | slot choice, pick-up and Bargain decisions of a seat holding spells | none unless the seat is rooted or stunned; one scripted `PublicCommand::Cast` at `:554` is the test's own |
| `crates/rh-policy/tests/raid_decide_fix.rs`, `raid_quests.rs` | quest decisions and walks, `view.spells.len()` | none (no cast expected) |
| `crates/rh-policy/src/bots/raid/quest/*_tests.rs` | `Act::QuestAccept/Interact/Pickup` values | none; `Act` gains a variant, a test `match`ing `Act` exhaustively would not compile: none found |
| any `Act` `match` | `refs.rs:97` only | stub adds the `Cast` arm (compile-only) |

No conflict found; nothing for the coordinator to settle, and no `oracle-amend:` commit.

## Oracle files (read-only for implementers)

- `crates/rh-policy/tests/raid_spells.rs`

(The stub commit touches `spells.rs`, `acts.rs`, `refs.rs`, `exec/spell.rs`, `exec/mod.rs` (a `pub mod` line, not the registry)
and is the implementer's.)

## 9. Validation commands

- `export PATH="$HOME/.cargo/bin:$PATH"; cargo test -p rh-policy --test raid_spells`
- `scripts/gate.sh --quick --oracle packet/P-RAID-S`

## 10. Risk and suggested model tier

Sonnet: one crate, a table and one ride-along executor; no RNG, no state, no wire. The risk is the rate of sends (budget
tokens) and the timing of the first cast; both have a test.

## 11. Known traps

- The sim drops a refused cast silently, and `effects::cast` can refuse after the gate (no target): a seat that resends every
  think burns order-budget tokens (`ORDER_BURST`, `OrderBudgetMirror`).
- Smite refuses players. SwapStick refuses non-players. Do not copy a rule from the spell's name.
- `RewardId as u8` is not the wire id after 21 (see section 3).
- An npc's key is new every frame: build the scene and resolve its key in the same frame (`Refs::actor`).
- The act queue paces an act 180-420 ms; the frame at send time must still show the spell (the `Refs::act` check) or it is dropped.
- Taunted and pulled are invisible to the seat: do not claim a Cleanse rule for them.
- Cleanse and ShoulderCharge are castable while the seat is stunned only for Cleanse: a stunned seat must not send ShoulderCharge.

## Notes from previous attempts
