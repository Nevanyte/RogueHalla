# P-RAID-C: RaidBot sustain (drink, disengage, price the heal-up)

- **Serves:** not in the plan: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot,
  per-elite numbers"; plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 1)
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer-opus (bot priority competition: a goal that pre-empts a committed fight)
- **Integration base:** `coord/int-83` @ `84d666014`
- **D-rules touched:** D1 loose proof (owner direction 2026-11-10) · D2 n/a · D5/D6/D9 none (no sim, wire, observation or
  client change; D9: the bot still acts only through `validate` + `Sim::submit`, the drink is `Act::UseConsumable`)

## 1. ID and outcome

`RaidBot` gets sustain. The `Sustain` executor (`crates/rh-policy/src/bots/raid/exec/sustain.rs`, a header-only stub in
the base) (1) drinks a held healing potion when hp is low and no heal is running, (2) leaves a camp fight the death
model says it will lose, as a `GoalKind::Sustain` goal that pre-empts the committed farm fight, and (3) prices low hp
between camps as a candidate in camp-equivalents per minute, so the arbiter can take a small camp now instead of standing
still. Its thresholds are fields of `Prices` (a new `sustain` part), not constants in the executor. When this lands the
lab's RaidBot farms faster and dies less in four- and five-member camps; fighting players, micro, the faction Wish
(P-RAID-C2), elites (P-RAID-E) and quests are not here.

## 2. Dependencies

- `coord/int-83` @ `84d666014` (P-RAID-0, P-RAID-0-fix, P-RAID-H1 merged). `raid_quests.rs` is not in this base (quest
  packets are in flight): the packet does not depend on it.
- **Shared files.** `values.rs` is edited by P-RAID-V (in flight, adds row slices): this packet's edit there is appended
  fields and consts only, and **merges after P-RAID-V**. `exec/mod.rs` (the registry) merges one packet at a time.

## 3. Relevant legacy paths

RaidBot has no legacy counterpart (P-RAID-0 section 3); the legacy bot's potion rule is the reference for the drink:
- `packages/server/src/bots/brain/runner.ts:29-35` `shouldUsePotion`: alive, `hp < 0.65 * maxHp`, a large or small potion
  held, `healingUntil <= now` (no heal running), not stunned or casting, and at least 1000 ms since the last try.
- `packages/server/src/bots/brain/runner.ts:64-67`: the runner sends `useConsumable` after the brain's commands, every
  frame the predicate holds.
- Rust `HumanBot` has **no** potion use at all (`rg UseConsumable crates/rh-policy/src` finds only `raid/refs.rs`,
  `raid/acts.rs` and a test), so the lab's HumanBot seats never drink either.

## Established facts (verified in code on `coord/int-83`)

### F1. Consumables and recovery under `SimConfig::live_content()`

- **The command.** `PublicCommand::UseConsumable` -> `systems/potions.rs:39-92` `use_potion`. Refused with a notice (no
  state change) when: the fighter is not standing, a status blocks actions (`status_runtime::action_blocked`),
  **three heals already run** (`DEFINITION.concurrent = 3`: `potions/mod.rs:27`), hp is full, or no potion is held. It
  does not refuse for being in combat; **damage or attacking does not interrupt the heal** (the hot is a record in
  `World::potion_healing`, cleared only when the fighter is no longer standing: `systems/potions.rs:124-134`).
- **Which potion.** The large ("Camp") potion is used first when held (`potions.rs:56,69-79`, "legacy
  `const large = s.potionLarge > 0`"); else the small one.
- **How much.** `DEFINITION` (`potions/mod.rs:27-34`): `small_share 0.25`, `large_share 0.4` of **max hp**, delivered
  over `heal_ms 5000` in `10` equal steps of `step_ms 500`, the first one 500 ms after the use (`start_hot`,
  `potions.rs:94-122`). Through `heal::Kind::IgnoreLockout`, so the 3 s combat heal lockout (`HEAL_LOCKOUT_MS`,
  `constants.rs:154`) does not apply; `heal_taken_mult` and the heal factor of statuses still do (`systems/heal.rs`
  `apply_with`). Hots stack: a second use while the first runs adds a second hot (up to 3). The wire shows the count in
  `potions_healing` and the deadline in `healing_until`.
- **Cooldown.** None beyond the three-concurrent cap. The order budget applies (`ORDER_BURST`, `ORDER_PER_SECOND`); the
  act queue keeps `ACT_RESERVE = 6` tokens back (`acts.rs:682`) and waits 180-420 ms before a send (`ACT_GAP_MS`).
- **Stock.** `DEFINITION.cap = 5` per size (`potions/mod.rs:22`); `grant` refuses past it (`potions.rs:11-34`).
- **How a player comes to hold potions.** Not at the start (`state.rs:706-715`: all zero). (a) **Every standing
  contributor of a camp clear is paid one large potion**, whether or not it is near the camp (`systems/camp_heal.rs:
  72-84`, legacy `campHeal.payCampHeal`); (b) a **chest** on the ground holds one small potion (`initial_world/mod.rs:
  215-230`, `systems/items.rs:415-441`: picked up through the pickup command; a full bag refuses); (c) the **small-potion
  trader** (`traders/runtime.rs:344-356`, barter pick 1; pick 2 spends one small potion on a weapon). So a farming bot
  earns a large potion per camp it clears, and the sustain question is whether it drinks it.
- **Passive recovery** (`systems/regen.rs`, `constants.rs:381-385`): `SimConfig::live_content()` sets `regen: true`,
  `regen_in_combat: Off` (`statics.rs:640,646`; owner decision 2026-11-12 "DEV-017 regen in combat", `OWNER_DECISIONS.md`
  L115): **0.8 % of max hp per second (`PLAYER_REGEN_SHARE_OUT = 0.008`) only after 3 s with no hit dealt or taken
  (`PLAYER_REGEN_QUIET_MS`), nothing in combat.** Full from empty in 125 s: the "heal-up seconds" the lab counts are
  mostly this. A potion is 10 s of regen (small: 31 s; large: 50 s) in 5 s.
- **Heal props** exist only in the grand coliseum (`systems/heal_pickups.rs:85-125`: a Large-potion hot for the nearest
  damaged player with no heal running, then a respawn timer; none in training or outside the active phase). Not a
  farming-phase source. **Lifesteal** is a card effect (`heal::Kind::Lifesteal`), not a base kit.
- **Faction** mechanics for healing: Holy gets extra heal from heal zones (owner decision 2026-11-12, `OWNER_DECISIONS.md`
  L96); the executor does not price it (P-RAID-C2's Wish).

### F1b. Mobs: when a camp resets (`systems/mob_ai.rs`)

- A mob **gives up** (`give_up`, :198-205; mode `Return`) when its position leaves the camp's leash (`fight`, :267:
  `leash_ok(.., margin 0)`: `mapdata/patience.rs:37-57`, the camp's radius capped at `MOB_LEASH_CAP = 24 tiles`, and
  the position must be on the camp's territory) or when it has had **no combat for `MOB_GIVE_UP_SECONDS = 20 s`**
  (`b.last_combat_at`). It then walks home and, **on arriving within `MOB_HOME_RADIUS = 10 u`, its hp is reset to full**
  (`home`, :190-191) and it idles. A camp the bot leaves therefore heals back to full and is still alive; a clear needs a
  fresh fight. The members that chase follow the bot until the leash (`MOB_LEASH_MARGIN = 24 u` for their own steps).
- So "leave the leash" is the disengage that ends the fight; "walk a short way away" only drags the members along (they
  keep hitting). `kite::away_point` (`bots/kite.rs:15-33`, a step away from a threat turned until the walk is clear, or
  `None` when cornered) is the heading; the leash edge: `PlanCamp` (`planning.rs:458`) has only `x, y, zone, elite_spot`, no radius or territory, so the
  executor can only use the circle `rh_sim::constants::MOB_LEASH_RADIUS` (24 tiles, the cap) round `RaidPercept::camp(i).at`;
  a camp with a smaller territory radius lets go sooner.

### F2. What the percept gives the executor today (`raid/percept.rs`)

- Present: `p.me.hp`, `max_hp`, `hp_share()`, `potion_small`, `potion_large`, `potions_healing`, `healing_until:
  Option<f64>` (server ms, the wire's inventory block; `percept.rs:339-342,514-517`), `potions()` (the sum of the three,
  **including `potions_healing`, which is the count of running hots and not a stock**: `percept.rs:354-358`), `effects`
  (status bits), `at`, `weapons`, `slot`; `p.camps[i]` (`alive`, `at`, `zone`, `elite`); `p.mobs` (`at`, `hp_share`,
  `alive`, `class`, `camp` label by position, `engaged`, `weapon`). `p.now` is server ms.
- **Missing or wrong for sustain:** (1) no field says whether the owner was hit this frame beyond `p.hit_at:
  Option<f64>`; (2) `Refs::potions` (`refs.rs:82`: `p.me.potions() > 0`) counts running hots as potions, so the queue
  keeps an `Act::UseConsumable` alive and sends it while only a heal is running: the sim then answers `Notice::Busy` or
  `Full` (a harmless refusal, no `Rejected` command: the order goes through `Sim::submit` and the notice is an event),
  but it spends order budget: the executor must gate on `potion_small + potion_large > 0` and `healing_until` itself
  (it has both), not on `potions()`; (3) no member count of "who fights me now" beyond `mobs` (`engaged`, distance): the
  farm's `members_left(p, camp)` (`exec/farm.rs:45-53`) and `death_chance(hp, share, members, &prices.death)`
  (`farm.rs:39-42`) are the model's inputs and are public; the duel `share` is `planning::matchup_share(me, zone_faction(
  zone), false)` (as `Farm::share`, cached per pair: the executor keeps its own cache).
- Neither a `percept.rs`, `acts.rs`, `bot.rs` nor `arbiter.rs` change is needed (the seam suffices: `Executor::urgent`,
  `propose`, `act`, `ride_along`, `Act::UseConsumable`, `GoalKind::Sustain::preempts_commitment`). The one thing `bot.rs`
  already does: it forces a strategy and think tick on **a hp edge below 0.5** (`bot.rs:406`, `low_hp_prev`); the
  executor's own `urgent()` adds the death-model edge and the drink edge.

### F3. Probe results (scratch, not committed; `coord/int-83`, release build)

Setup: `balance_lab --seeds 1..5 --jobs 4 --human-seats --seat 0=raid ... --seat 7=raid` (4 seeds, 71,287 ticks, eight
RaidBots in human seats, matches run 15 to 16 minutes). From the table:

- **Camps cleared: 133** in 4 seeds (about 33 a seed). Per-camp cycle: **kill 13.1 s (n 133), heal 11.1 s (n 123), walk
  19.7 s (n 123)**.
- **Stacks per player** (farm stacks / players alive at the sample): minute 5 **411 / 16 = 25.7**, minute 8 633 / 14 =
  45.2, minute 12 420 / 7 = 60. Players alive fall from 26.6 player-minutes in minute 0 to 15.2 in minute 5 and 7.0 in
  minute 12.
- **Deaths: 29** (player 24, mob 5, elite 0, hazard 0). By minute: 0:4, 1:4, 2:0, 3:3, 4:5, 5:1, 7:1, 9:2, 11:4, 13:1,
  16:3, 18:1. Twelve of the 29 fall in the first four minutes.
- No elite attempts and no quests (this base has none).

**Per-seat trace of seed 1** (19,551 ticks; scratch example `crates/rh-headless/examples/sustain_probe.rs`, since
removed; it reads `Sim` state and events each tick):

- **Potions held: every seat stores them and none ever drinks.** Stored (camp-clear rewards, `PotionNotice::LargeStored`):
  5 for seats p1, p2, p5, p7, p8, 3 for p6, 2 for p3, 0 for p4; **drinks: 0 for all eight seats**, over the whole match.
  Time-averaged stock on the long-lived seats: p1 4.24, p5 4.65, p7 4.64 (the cap is 5: `DEFINITION.cap`); the bag is
  full for most of the match, so later clears' potions are refused (`CampBagFull`).
- **How low hp gets:** minimum hp share over the match is 0.00 to 0.01 on seven of eight seats (they die or nearly).
  Seconds below 65 % hp: p8 168 s of 426 s alive (39 %), p7 124 s of 668, p5 92 s of 970, p2 72 of 304, p1 51 of 970.
  p8's hp histogram spends 20 s under 20 %.
- **Deaths on seed 1** (6 real deaths; the two at tick 19,551 are the match end): `stock` is the potion stock the last
  tick before dying (a death drops the bag): **p2 died holding 5, p8 holding 5, p7 holding 5, p6 holding 3, p3 holding
  2, p4 holding 0**. So **five of six deaths had a potion that could have been drunk with a 5 s heal of 25-40 % of max
  hp and were not**; every one of them was below 65 % for most of the fight.
- **Standing still below full hp** (no hit taken for 3 s, i.e. waiting for the 0.8 %/s regen): p5 175 s of 970 (18 %), p7
  128 s of 668 (19 %), p2 34 s, p1 27 s, p8 14 s; plus time standing hurt inside a fight (p5 111 s, p8 66 s, p7 58 s).
  The seats move 45 to 90 % of their active time. This is the lab's "heal-up seconds" (11 s a camp on average).
- **Camp sizes and what the farm refuses.** Seed 1's 41 normal camps: **11 of 3 members, 18 of 4, 12 of 5** (elite camps:
  3 of 3, 2 of 4, 5 of 5). `Farm::propose` at full hp, `Prices::LOCAL`, walk 0.3 min (rate in ce per minute; roam pays
  0.1): a **4-member camp is negative for every faction pair except Holy vs Plague/Holy (+0.89) and Holy vs Sentenced
  (+0.01)**; a **5-member camp is negative for every pair except Holy vs Plague (+0.30) and Holy vs Holy (+0.92)**; a
  3-member camp is positive for every pair except Sentenced vs Fire/Sentenced/Frost (about -0.7). Reason: the model
  prices the duel loss `(1 - share) * n(n+1)/2 * fight_weight` against hp held (`farm.rs:32-42`); with `share` about
  0.95 (`matchup_share`) a five-member clear needs ratio 1.4-2.2 of the hp held, death chance 0.3 to 0.98, times
  `death_loss` 6, against a value of 1.25. **The model has no term for a potion**, so a bot holding five potions (200 %
  of max hp of healing) still refuses 73 % of the normal camps on its map. (Scratch table at the end of
  `trace1.txt`; reproduce with `farm::death_chance` and `matchup_share`.)

### F4. Player-attributed deaths (seed 1 trace)

Of the lab's 29 deaths 24 read "player", although `RaidBot` proposes no player fight. The chain on seed 1:

- The attribution is **the sim's, not the lab's fallback**: `Event::Death.by` is already `Some(player)` on four of the six
  deaths (p2, p3, p6, p8 all `by = PlayerId(7)`); `KILL_CREDIT_SECONDS = 5` (`constants.rs:162`) credits a kill to the last
  player who hit the victim within 5 s, even when mobs landed the last blows. p2 died with its last three hits from camp
  mobs (Bow, 12 each, 12 to 39 ticks before) and one `PLAYER7` Wand hit 50 ticks (2.5 s) before: credited to p7. The two
  `by = None` deaths (p4, p7) read mob or hazard in the lab. (`observer.rs:298` falls back to the last hit, only when
  `by` is `None`; here it rarely mattered.)
- Real player-on-player damage exists, though: **232 hits from a player on another player in the match** (Wand 193 hits /
  2,925 damage, Fists 35 / 645, Bow 3, Shield 1); player damage was 164 to 1,068 per seat against mob damage of 32 to
  2,218. Geometry of those 232 hits (a living non-zombie mob within 500 u of the attacker, the angle between the
  attacker-to-mob and attacker-to-victim directions): **116 (50 %) have a mob in the line of fire BEYOND the victim**
  (a shot at a camp member that hit the bot standing in front of it), 39 (17 %) a mob in line nearer than the victim (a
  piercing or area wand), 76 (33 %) a mob within 500 u but off the line (area damage or a turned shot), 1 with no mob
  nearby. So the player kills are **mostly friendly fire at a shared camp**: two RaidBots fight the same camp (the lab
  puts eight bots on one map), the wand and bow shots meant for a member hit the other bot, and the 5 s credit rule
  turns the mob's finishing hit into a player kill. Wands dominate: p7 carried a Wand (193 hits).
- **What this means for P-RAID-C2:** do not add "avoid fighting players" as the fix for these deaths. The fix is in
  where and when the bot fights a camp another bot is already fighting (a camp with a player at it is a candidate that
  costs friendly fire and a shared clear), and in not standing in front of the other shooter's line; the lab's "player
  deaths" are a measure of camp crowding as much as of player hunting. Attribution caveat: one seed, six deaths; not a
  count over seeds.

## 4. New paths

- `crates/rh-policy/tests/raid_sustain.rs` (the oracle).

## 5. Exclusive write scope

- `crates/rh-policy/src/bots/raid/exec/sustain.rs` (the executor; the stub commit replaces its one line).
- `crates/rh-policy/src/bots/raid/exec/mod.rs`: **one registry line**, `Box::new(sustain::Sustain::default())`, placed
  **first** in `registry()` (the stub commit already puts it there; the implementer keeps it). First because `Sustain`
  is the first `GoalKind` (the arbiter's tie order), because it draws nothing from the arbiter stream in `observe`,
  `urgent` or `propose`, so the order moves **no draw**; the only order-sensitive thing is `ride_along` merge priority,
  and `Sustain::ride_along` returns an act, not an intent. A line later in the list would change nothing either; first
  keeps the file's pattern (declaration order).
- `crates/rh-policy/src/bots/raid/values.rs`: appended only: the `SustainPrices` struct and its `LOCAL`/`OFF`, the one
  `sustain` field of `Prices`, and `farm_only()` setting it `OFF`. **Merges after P-RAID-V** (shared file: it adds row
  slices to `Prices`); append the field last, touch nothing else.
- `crates/rh-policy/src/bots/raid/exec/farm.rs`: **ONE change, in `Farm::propose`**: when `cx.prices.sustain.on`, the hp
  the death model prices with is the hp share held plus the potions held (`potion_large` x `Size::Large.share()` 0.4,
  `potion_small` x 0.25: `rh_sim::potions::DEFINITION`), not the bare hp share. **This file is outside the coordinator's
  outline and needs their yes before the implementer starts.** Without it the farm's model has no term for a potion and
  refuses every 4- and 5-member camp however many potions are held (F3: 73 % of the normal camps on seed 1), so oracle
  test 3 cannot pass. The alternative if the coordinator refuses: `Sustain::propose` pushes its own `GoalKind::Farm`
  candidates for such camps (the arbiter ranks them against `Farm`'s; `Farm` still executes the goal), duplicating
  `Farm::propose`'s pricing. A prototype of the one-line change plus a bare ride-along drink at `drink_below` (scratch,
  not committed) measured on the oracle's scenes: 5-member camp cleared on 6 of 8 seeds and 4-member on 6 of 8, no death
  among the 5-member runs and one among the 4-member (seed 6), against 0 of 8 for the control.
- If the executor needs something else the seam lacks (a percept field, an `Act`, `bot.rs`, `arbiter.rs`): **stop and report**
  to the coordinator; do not edit them.

## 6. Contracts and references

C-INPUT (the drink is a paced `Act::UseConsumable` through `validate` + `Sim::submit`), C-OBS (the executor reads the
percept only), C-TICK and determinism (no clock, no hash order; `urgent` and `propose` draw nothing), `crates/rh-policy/
AGENTS.md` ("priorities compete every decision tick", a commitment is the exception: `GoalKind::preempts_commitment`).
Owner decisions kept to: DEV-017 (regen in combat Off, `OWNER_DECISIONS.md` L115) and the faction home-ground benefits
(L96), unchanged.

## 7. Behaviour to preserve

- `crates/rh-policy/tests/raid_bot.rs` (four tests; header and `raid()` use `Prices::farm_only()`; it clears the
  three-member scene on at least 7 of 8 seeds in 90 s, sends only accepted commands with `rejected == 0` and replays one
  seed exactly). **`Prices::farm_only()` sets sustain `OFF`**, so those four tests replay byte for byte (an `OFF`
  Sustain proposes nothing, `urgent()` is false and draws nothing): they are the proof the registry line moves no draw.
- `raid/fix_tests.rs`, `raid/review_tests.rs` (both build scenes at full hp with no potions: `Sustain::urgent` must be
  false there and draw nothing, `review_tests.rs:276+` asserts the arbiter stream is untouched by `any_urgent`; that test
  also pushes a stand-in `Alarm` executor of kind `Sustain` AFTER the registry, the real `Sustain` first in the list takes
  the dispatch only for goals it proposed: the real one proposes nothing at full hp, so the test is unchanged),
  `crates/rh-headless/tests/raid_seats.rs` (eight RaidBots in a real match: no fault, repeats exactly; with `Prices::LOCAL`
  sustain is on there, so its replay changes but must stay deterministic and fault-free), `parity` and `HumanBot` tests
  (untouched: HumanBot has no Sustain).
- Pinned replays this packet can move: only seats built with `Prices::LOCAL` (the default of `RaidBot::new`): the lab,
  `raid_seats.rs`. The implementer proves the farm scenes still clear by running `raid_bot.rs` with `Prices::LOCAL`
  swapped in for `farm_only()` in a scratch copy and reporting clears and `rejected` (not committed).

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-RAID-C` passes (the full gate is the coordinator's).
- `crates/rh-policy/tests/raid_sustain.rs` passes in a debug build in about a minute or two; `raid_bot.rs`,
  `raid_seats.rs` stay green.

## The oracle (loose proof) and its choices

`crates/rh-policy/tests/raid_sustain.rs`, the harness of `raid_bot.rs` (a non-bot seat, `Sim::reset_active`, the seat API)
with the potion stock set as declared setup (a checkpoint with `potion_large` set, accepted by `Sim::restore`, as
`potion_fact_tests.rs` does). Scenes: a normal camp of 4 or 5 regional members, the fighter 100 u from the camp
centroid, Holy with a Sword. Controls use `Prices::farm_only()` (sustain off); the treatment is `Prices { sustain:
SustainPrices::LOCAL, ..base }`. Debug build, 43 s on this machine for the four tests (run in parallel threads).

1. `a_potion_is_drunk_within_a_second_of_hp_crossing_the_drink_threshold`: `reckless()` (`death_loss` 0, so the farm
   takes the fight), 4-member camp, 3 large potions, seeds 1..=8. Control: crosses below `drink_below` holding a potion on
   at least 7 of 8 seeds and never drinks (measured: 8 of 8, 0 drinks). Treatment: on at least 7 seeds that cross, a
   `PotionNotice::Used/LargeUsed` within 20 ticks (1 s) of the crossing tick (the first tick below the threshold with
   stock and no heal running); no drink at hp above `drink_below + 0.05`; at most 3 drinks.
2. `a_fight_the_death_model_says_is_lost_is_left_within_a_second`: `reckless()`, 4-member camp, no potion, seeds 1..=14.
   Control dies on seeds 4, 6, 12 (measured), each about 100 to 220 ticks after the death model (`farm::death_chance` on
   the sim's own hp and member hp, at `SustainPrices::LOCAL.disengage_death`) said the fight was lost, and moving toward
   the nearest member (-36, -117, -46 u in the second after). Treatment: on every seed where the fight turns, the fighter
   is at least 30 u farther from the nearest member one second later; it survives on at least 2 of the control's death
   seeds; it dies on fewer seeds than the control.
3. `four_and_five_member_camps_the_base_bot_refuses_are_cleared_with_potions`: default prices, 3 potions, seeds 1..=8.
   Control clears at most 1 of 8 (measured 0 of 14 for both sizes: it walks away) and the treatment clears at least 5 of 8
   (prototype: 6 of 8) with at most `max(control deaths, 1)` deaths. **Needs the `farm.rs` change above.**
4. `sustain_sends_only_accepted_commands_and_replays_a_seed_exactly`: no fault, `rejected == 0`, `accepted == proposed`,
   no fallback decisions, at least one drink, and one seed run twice gives the same drinks, clear tick, stats and hash.

State of the oracle on the stub (all four red on assertions, not compile errors): 1 "no potion use by tick 164" (seed 1
crossed at tick 144), 2 "moved -36 u away" on seed 4, 3 "cleared on 0 of 8 seeds", 4 "the seat drank".

**Choices to read, not guesses:** the floors (7/8, 2 survivals of 3, 5/8, 30 u) sit below what a prototype or the control
shows; floor 2 of 3 on test 2 is the thinnest (the sustain-on trajectory is unmeasured: the prototype had no disengage),
and test 2's turn is judged by the death model at `SustainPrices::LOCAL.disengage_death`, so changing that field's
value in `values.rs` moves the scene (it is read at test time, not hard-coded). `reckless()` zeroes `death_loss`: the
disengage candidate must therefore not take its rank from `death_loss` (a leave is worth its `disengage_death`, not a loss
price). The oracle does not pin `LOCAL` numbers: `drink_below 0.65` is legacy `shouldUsePotion` (a large potion adds 0.4,
so a drink at 0.65 lands at 1.0 with no waste), `disengage_death 0.5` is where the farm's own score turns negative for a
4-member camp at share 0.95 (`0.5 * 6` against a value of 1), `ready_hp 0.85` is a starting point the implementer may
change inside the oracle.

## Oracle files (read-only for implementers)

- `crates/rh-policy/tests/raid_sustain.rs`
- (`docs/backlog/packets/P-RAID-C.md`, this brief)

The `stub:` commit (implementer's to replace: `exec/sustain.rs`, `exec/mod.rs`, `values.rs`) is not an oracle file. No
existing test or capture is amended: `raid_bot.rs`, `fix_tests.rs`, `review_tests.rs` stay as they are (section 7).

## 9. Validation commands

- `cargo test -p rh-policy --test raid_sustain` and `--test raid_bot`; `cargo test -p rh-policy --lib raid`;
  `cargo test -p rh-headless --test raid_seats`.
- The lab, for the numbers in the report: `cargo run --release -p rh-headless --bin balance_lab -- --seeds 1..5 --jobs 4
  --human-seats --seat 0=raid ... --seat 7=raid`.

## 10. Risk and suggested model tier

implementer-sonnet: one executor, one price struct, one registry line, with an oracle that fails by assertion. The hard
part is the pre-empting goal: `Sustain` must win only when the fight is lost, leave a committed farm exactly once (no
flip-flop with Farm re-proposing the same camp next tick: farm `ban`s are its own and Sustain must hold the goal through
the disengage), and not starve itself on the order budget. reviewer-opus reads the pre-emption.

## 11. Known traps

- `Me::potions()` counts running hots: do not gate on it (F2).
- A potion use while a hot runs is accepted by `Sim::submit` and refused by the sim with a notice; it still costs one
  order token. Gate on `healing_until`/`potions_healing` before queueing.
- `ActQueue::push` ignores an equal act already waiting, drops acts older than `ACT_TTL_MS = 4 s`, and sends at most one
  per 180-420 ms: "within one second of crossing the threshold" has about 0.5 s of slack for the queue.
- `urgent()` fires on the edge only. `any_urgent` asks every executor every frame and draws nothing; a level-triggered
  `urgent` makes every frame a strategy tick and shifts the arbiter stream's draw count (changes replays).
- Mob labels (`MobSeen::camp`) are by position; a member chasing the bot reads as another camp or none (`farm.rs`
  `keepable`). Count the fight's members as `farm.rs` does (`engaged` within `DEFEND_U`, camp members within `FIGHT_U`).
- A camp the bot leaves heals to full when its members get home (F1b); `Farm` re-proposes it with its full `members_left`.
  Sustain must not drift into "leave, heal, return at once, die again": the heal-up candidate and the farm candidate's
  own death term must share the hp the bot holds.

## Notes from previous attempts
