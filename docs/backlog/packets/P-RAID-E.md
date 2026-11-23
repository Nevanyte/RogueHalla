# P-RAID-E: RaidBot prices an elite camp, fights the elite and picks a card from its chest

- **Serves:** not in the plan's phases: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot,
  per-elite numbers"); plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 1 and the wave table row "P-RAID-E: Elite
  pricing, elite fight, chest pick" (oracle there: kills a favourable elite and picks a chest card within 5 minutes).
  Bots quest and fight elites in headless human seats only; nothing live changes.
- **Implementer tier:** implementer-opus
- **Reviewer:** reviewer-opus (bot priority competition: a new goal kind that ranks against Farm and Chest, commits to a
  fight and overrides the chest module's pick)
- **Integration base:** `coord/int-85` @ `2eb8caeb2` (gate with `--base coord/int-85` until it is merged)
- **D-rules touched:** D1 loose proof (owner direction 2026-11-10; RaidBot has no legacy counterpart) · D2 n/a · D5/D6/D9
  none (no sim, wire, observation or client change; D9: every act goes through the paced queue, `validate` and
  `Sim::submit`)

## 1. ID and outcome

`RaidBot` takes elite camps. Today it never does: `Farm::eligible` refuses every camp that has an elite
(`exec/farm.rs:82`), `exec/elite.rs` is a header-only stub, and the balance lab's elite table is empty
(F5). After this packet the `Elite` executor (`GoalKind::Elite`, `Target::Camp`) (1) proposes an elite camp as a candidate
priced in camp-equivalents a minute: what the elite and its chest are worth, against the chance the bot dies fighting
this elite with this weapon, this build and this hp, (2) fights the elite and walks to its chest, and (3) picks the offered
card the price table ranks highest instead of the chest module's legacy score. Since augments come only from elite chests,
this is what lets the lab see an augment taken at all. A bot that is weak for the elite (few stacks, a bow or wand, no
potions, a deadly kind) declines it and keeps farming; a bot that is strong enough takes it.

Not here: fight micro (orbwalk, dodging, the faction Wish: P-RAID-C2), a leave from a lost elite fight beyond what the
arbiter already does (decision D2 below), the elite's normal members after the elite is dead (Farm still refuses an elite
camp), quests.

## 2. Dependencies

`coord/int-85` @ `2eb8caeb2` (P-RAID-0, -C, -V, -Q1..Q3, P-BAL-ELITE-BOTS-1 merged). Siblings in flight on the same base:
P-RAID-C-fix owns `exec/sustain.rs`; P-RAID-Q3-fix owns `quest/decide.rs`, the `DEATH_FLOOR`/`SPELL_SLOTS` lines of
`quest/walker.rs`, `quest/dwell.rs`, and is appending fields to `Prices` in `values.rs`. **`values.rs` and the registry
(`exec/mod.rs`) merge one packet at a time**: this packet's edits there are appended fields and one registry line only
(section 5); the coordinator resolves the append point.

## 3. Relevant legacy paths

None: RaidBot has no legacy counterpart (P-RAID-0 section 3). The reference for an elite fight is the elite bench
(`crates/rh-headless/src/elite_bench.rs`, `tools/balance-camps/README.md` "Elite mode"), reference only.

## Established facts (verified on `coord/int-85` @ `2eb8caeb2`; file:line)

### F1. Elite camps in the sim and in the frame

- **Which camps.** The map generator gives each zone two elite spots (`mapgen/zones/camps/rules.rs:6,19`
  `elites_per_zone: 2`); an elite camp has `elite_spot >= 0` and one of its members is the elite
  (`mob_catalog/rosters.rs:90-106` `camp_mobs`: the member at index `elite_spot` is the elite type, the rest the camp's normal
  type). The kind is not drawn: `roll_camp_elites` (`rosters.rs:68-89`) walks a zone's elite camps in order and takes
  `elites_of(zone)[k % 2]`. The fixture map `seed1.map.json` has **10 elite camps in 50 camps** (one per kind, camp indices
  3 wyrmling, 7 ashDrake, 11 executioner, 17 gaoler, 23 ratOgre, 27 poxBehemoth, 31 glacialWarden, 33 avalancheTroll, 42
  bloodSaint, 45 archon; sizes 5, 5, 3, 4, 5, 3, 5, 5, 3, 4 members **including** the elite; a quest-board map swap may
  add a small extra elite camp: seed 5 adds camp 50, gaoler, one member). Generated lab maps follow the same rule.
- **How an elite differs.** Hp: `faction_spec` (`state.rs:1131-1161`) `hp * k * (MOB_HP_MULT * hp_mult_of(kind)) *
  role hp_mult` with `k = ELITE_K 0.65` (normal `MOB_K 0.22`, `constants.rs:294-297`) and under `SimConfig::live_content()`
  (`EliteRule::Rework`) `hp_mult 1.5` (`REWORK_ELITE_HP_MULT`, `constants.rs:602`; a normal member x2.0). Damage:
  `point_damage` (`state.rs:1178-1203`) `raw * k * (MOB_DMG_MULT * dmg_mult_of(kind)) * role * zone * reach`, elite
  `dmg_mult 1.0` (`REWORK_ELITE_DMG_MULT`, `constants.rs:600`) against a normal member's `0.5`, and an elite skips the
  melee/ranged reach discount. So an elite has about 2.2 x a member's hp and about 6 x a member's damage per hit.
  `EliteTuning::per_kind` is **empty** in both tables today (`mob_runtime.rs:462-473`), so `hp_mult_of`/`dmg_mult_of`
  return the global pair for every kind until P-BAL-NUM-ELITE-1.
- **Leash and reset** are P-RAID-C fact F1b, unchanged for elites: `mob_ai.rs` has no elite branch. A mob gives up after
  `MOB_GIVE_UP_SECONDS` 20 s without combat or when it leaves its leash and walks home; at home (`MOB_HOME_RADIUS` 10 u)
  its hp resets to full (`constants.rs:333-336`). The camp respawns `CAMP_RESPAWN_MS` 180 s after a clear
  (`constants.rs:354`, `mob_life.rs:740-750` `respawn`), the elite included.
- **What the seat sees.** `RaidPercept::camps[i].elite: Option<MobTypeId>` is `WireCamp.elite`
  (`delivery/collections.rs:562`, `percept.rs:65,274`): **the kind of every camp's elite, public from the first frame and
  at any distance**; `CampRoster.elite` is rolled once and never cleared (`mob_runtime.rs:97-103`), so after the elite
  dies the row still names it. `camps[i].alive` counts all members, the elite included. `camps[i].state` is 0 untouched
  since the last respawn, 1 once anyone hurt a member (`mob_life.rs:199-200`), 2 cleared (`:333-336`).
  **Whether the elite is still alive is therefore knowable only when `state == 0` or the elite mob is in view**: the
  mob row has `class == MobClass::Elite` (`percept.rs:149-165` `class_of`), `mob_type`, `hp_share`, `alive`, `camp` (the
  nearest camp within `CAMP_JOIN_RADIUS` 480 u, a label by position), `engaged`. A camp at `state == 1` with the elite
  not in view is ambiguous (killed by another player, or only left by a leash and healed): the executor must keep its own
  memory (the elite seen alive or dead) and must not propose a camp whose elite it cannot show alive.
- **Contributors and the first pick.** The camp's contributor list takes any player who hurts any member
  (`mob_life.rs:205-221`); an elite kill snapshots it (`:318-322`).

### F2. The reward (`reward_chests/mod.rs`, `systems/mob_life.rs`)

- **What a kill pays.** Under `Rework` every standing contributor, the killer included, is paid
  `REWORK_ELITE_SHARE_STACKS` 12 stacks of the camp's stat (`mob_life.rs:570-593`, `constants.rs:591`): one camp-equivalent.
  The other members pay their normal 3 stacks each if killed (`modifiers/farm.rs:37`); a camp clear pays every standing
  contributor one large potion (`systems/camp_heal.rs:72-84`).
- **The chest.** `on_mob_killed` (`reward_chests/mod.rs:215-273`): an elite's death drops the camp's shared chest at the
  camp's chest tile (the centre when that tile is solid, `:282-291`); its three cards are rolled from the pool of the
  contributors standing near the camp (`drop_augment_in`, `:153-186`, reward RNG, elite tier weights `[10, 60, 30]`,
  `rules.rs:12`). **One chest per camp, shared**: every eligible player picks once (`c.pickers`), each option can be taken
  by one player (`c.taken[i]`), so a second picker finds the first one's card gone (`Refusal::Taken`).
  Lifetime `DEFINITION.lifetime_ms` 15 000 ms, birth hold 1 tick (`rules.rs:7-10`), pick range `range` 96 u with line of
  sight (`mod.rs:314-327` `reach`). Under `Rework` the credited killer holds a **5 s first-pick window**
  (`REWORK_ELITE_FIRST_PICK_MS`, `constants.rs:604`; `mod.rs:240-270`); anyone else is refused `NotYours` until it ends.
- **The commands.** `PublicCommand::Choose { handle, pick }` (`rh-protocol/src/command.rs:89`), `pick` 0..=2 or -1 to skip
  (a skip heals `skip_heal_share` 0.15). `choose` (`mod.rs:328-427`) refuses, with an `Event::ChestRefused` (an event, not a
  rejected command: `SeatStats::rejected` does not count it): `Busy` (status blocks actions), `Gone` (no chest, expired or in
  the birth hold), `NotYours` (first-pick window), `Far`/`Blocked` (reach, sight), `Picked` (this seat already chose
  there), `Taken`, **`Owned` for any card the seat already holds** (`mod.rs:381-383`: `build.owns`, base cards included),
  `Needs(base)` for a conditional whose base is not held, `Maxed` for a boost.
- **How the offer reaches the seat.** The observer publishes a chest's cards to any viewer who sees it
  (`observer.rs:92-170`), with `can_choose` (standing, no blocking status, not picked, own or shared, first-pick open or
  mine, born, unexpired, in reach and sight). `perception.rs:340-370` turns that into `ChestRef { key, handle, at,
  expires_at, life_ms, options: Option<Vec<String>>, taken_by, picked }` and shows `options` **only while `can_choose` and
  within `CHEST_PANEL_RANGE` 88 u** (`perception.rs:171`): beyond that the seat sees a chest but not its cards.
  `RaidPercept::facts.chests` carries it (`percept.rs:112`). So the cards are knowable at the chest, not when the camp is
  proposed.
- **What `exec/chest.rs` and `Loot` do today.** `Chest::propose` (`chest.rs:68-85`) offers any chest `Loot::chest_goal`
  names (the chest hunter) at the flat `prices.chest` 1.5 ce, unless `Loot::fighting` is true (a fight within 220 u,
  `loot.rs:626-633`, `chest_goal` `:680-703`): **no chest goal while any member is awake near the chest**. The pick itself
  is `Loot::pick_intent` (`loot.rs:529-595`): `choose_option` (`temporal_percept/modules/choice.rs:362`, the legacy
  `card_score`, `:280-350`: tier, risk, weapon fit, Marked Man and risky cards refused below a risk) as an
  `IntentBody::Choose { msg: ChooseMsg { chest: key, pick, replace: None } }` with a `React` timing, asked again only for
  another card inside `ASK_WINDOW_MS` 4 s and at most `MAX_ASKS` 3 times (`loot.rs:70-71`). It rides along **every** goal
  (`Chest::ride_along`, `chest.rs:101-110`), so a chest picked after an elite kill is already picked today (F5) with
  the legacy score, not `Prices::augment_value`. A `Choose` intent is on the `Menu` channel, where a later ride-along
  intent replaces an earlier one (`brain.rs:141-160` `merge_intents`; `bot.rs:506-510` merges each executor's extras in
  registry order): **an executor registered after `Chest` that emits its own `Choose` overrides Loot's.**
- **A pick pending.** A chest waits at most its 15 s; there is no modal state and no frozen seat: the seat is free to walk
  and fight while a chest is open (unlike the quest slot choice of P-RAID-Q3-fix). Every wait of this executor is bounded by
  the chest's `expires_at` or by its own `Prices` field (section 5).

### F3. What `Prices` and the models offer for an elite

- `Prices::augment_value(card)` (`values.rs:249`): one row per native and interactive card (33 rows,
  `augment_rows`), `augment_default` for a card with no row (the conditional and base cards a chest may also offer), 0 under
  `farm_only()` (rows cleared, default 0). Nothing reads it yet.
- `Prices::death_loss` 6, `death: DeathModel`, `pick_success` 0.9, `pick_seconds` 3, `chest` 1.5, `camp` 1, `stacks_per_camp`
  12, `roam_per_minute` 0.1, `sustain.potion_hp(large, small)` (hp share the potions held return).
- `farm::death_chance(hp, share, members, &DeathModel)` and `planning::matchup_share_of_kind(me, region, true, Some(kind),
  rule)` exist (`planning.rs:464-483`; the rule is `cx.knowledge.map(|k| k.elite_rule())`, `knowledge.rs:125`). **They are
  not enough to price an elite**, measured (`shares` scratch test, `Prices::LOCAL.death`): the share depends only on the
  zone (the two kinds of a zone give equal numbers because `per_kind` is empty: executioner and gaoler 0.548 for Holy,
  wyrmling and ashDrake 0.536, ratOgre and poxBehemoth 0.657, glacialWarden and avalancheTroll 0.544, bloodSaint and
  archon 0.687), it ignores the weapon, the stacks, the cards and the kind's role and abilities, and with `n = 1`
  `death_chance(1.0, ..)` gives 0.12 to 0.30 for every kind whatever the bench says (2.2 percent for Executioner, 46.7 for
  Avalanche Troll). The model that does know is the elite bench's table (next bullet).
- **The bench evidence** (`tools/balance-camps/elite_after.txt`, `README.md` "Elite numbers", duelist fighter, Rework
  elites hp 1.5 dmg 1.0, 900 samples a column): death rate by kind and by banked camps (12 stacks a camp) 0 / 6 / 12 / 18:
  wyrmling 83.3 / 66.7 / 25.6 / 6.7, ashDrake 78.9 / 42.2 / 22.2 / 12.2, executioner 65.6 / 8.9 / 2.2 / 1.1, gaoler 93.3 /
  73.3 / 23.3 / 3.3, ratOgre 80.0 / 33.3 / 8.9 / 5.6, poxBehemoth 53.3 / 20.0 / 14.4 / 11.1, glacialWarden 81.1 / 61.1 /
  17.8 / 1.1, avalancheTroll 93.3 / 87.8 / 46.7 / 20.0, bloodSaint 12.2 / 7.8 / 6.7 / 6.7, archon 50.0 / 10.0 / 6.7 / 5.6;
  overall 69.1 / 41.1 / 17.4 / 7.3 (the `bot_sample` column, bots' own builds, 66.9). By weapon at banked_12: sword 8.7,
  bow 47.5, wand 25.2, shield 6.7, axe 6.0, fists 14.3 (overall 17.7 decided). **Stacks are the dominant term** (a seat with
  2 camps' stacks is nearer the 41 to 69 percent columns than the 17 percent one), which is why the lab's minute-5 bots
  mostly should not take an elite, and why potions matter (F4).
- The seat knows its stacks: `RaidPercept::summary.mob_rows[..].stacks` summed (`percept.rs:145`, the same read as
  `quest/decide.rs:244-250`), its weapon `me.weapons[me.slot]`, its hp and potions (`me`).

### F4. The fight: what `Farm` does with an elite camp, measured

- `Farm` cannot fight it as it is: `eligible` refuses `camp.elite.is_some()` (`farm.rs:82`) and `pick`/`keepable` take only
  `MobClass::Regional` (`farm.rs:96,118`), so even a camp with its elite stays unfought by Farm and the elite mob is never a
  target. Its pricing (`members_left` counts Regional rows, `farm.rs:45-53`) knows nothing of an elite.
- A scratch edit (not committed) let Farm take elite camps (eligible, and `Regional || Elite` targets, nearest first)
  with `death_loss 0` (a table that takes the fight; sword, Holy, `SimConfig::live_content()`, the oracle's harness, a start 128 to
  256 u from the camp, release build, 3 to 6 seeds, 1 800 to 3 000 ticks; stacks granted by `trusted_build::apply`):
  - **Executioner is the robust kill.** Sword, 36 stacks, 2 large potions, sustain on: killed on **6 of 6** seeds, 0 deaths, the elite
    dead at tick 935 to 1 462 (47 to 73 s), minimum hp share 0.09 to 0.63; the chest's card taken 9 to 12 ticks after the
    kill on five of six (82 on one; the sixth seed died hp 0.09 before the pick window and took none) by the legacy Loot pick.
    Also 3 of 3 with 24 stacks and 3 potions, with 144 stacks and none, and 3 of 3 in both orders below.
  - **Unfavourable ones do not win:** with sword, 24 stacks, no sustain, no potions: wyrmling died 3 of 3, glacialWarden 3 of
    3, avalancheTroll 2 of 3, archon 2 of 3. Wand, 24 stacks, no sustain: executioner **died 3 of 3** (the same kind a sword
    kills 3 of 3), poxBehemoth 3 of 3. Bow, 12 stacks, no potions: executioner died 2 of 3.
  - **Potions decide it.** Sword, 24 stacks: avalancheTroll killed 2 of 3 and died 0 with 3 potions and sustain on, died
    2 of 3 with none; poxBehemoth and archon follow the same way.
  - **Target order is not settled by the data.** Elite first (adds only when the elite is not in reach) killed
    executioner, poxBehemoth, archon 3 of 3 each with 2 potions, but poxBehemoth died 3 of 3 with none; nearest first (as
    Farm) killed poxBehemoth 3 of 3 with none. The elite has about 6 x a member's damage for 2.2 x its hp, which argues
    for it first; the implementer chooses and the oracle does not assume either.
  - Other camps nearby are fought too: a camp that is neither the elite's nor untouched changes the picture, so the
    oracle's decline scene asserts about the one elite camp only.
- **What Sustain does for an elite fight (the leave):** the drink rides along any goal (`sustain.rs` `ride_along`,
  hp below `drink_below` 0.65 with a potion). The **disengage does not cover an Elite goal**: `Sustain::in_fight` and
  `view` accept only `GoalKind::Farm | Sustain` goals and `nearest` takes only `MobClass::Regional` rows
  (`sustain.rs:139, 168, 180` on this base), so the elite itself, the main damage, is invisible to the leave and the leave is
  priced with the non-elite share. `exec/sustain.rs` belongs to P-RAID-C-fix this round, so this packet does not touch it
  (decision D2). The executor's own leave is the arbiter's: a committed goal is kept only while its own score stays
  positive (`arbiter.rs:113-120`); the death term below rises as hp falls, so a lost fight turns the score negative and the
  arbiter hands the seat to another goal. **This executor must not grow a second leave rule** (no `Step::failed` on a
  hp threshold of its own).

### F5. Probe measurements (scratch, uncommitted)

- **Lab, this base** (`balance_lab --human-seats --seeds 1..4 --max-ticks 6000`, eight `raid` seats, 3 seeds, 18 000
  ticks): 67 camps cleared, **0 elite attempts** (the `elites` table is empty), 0 deaths by an elite, 3 chests offered and
  all three Trophy chests of player kills (`picks seen {"kill": 3}`), 0 card picks, 0 augments held. As expected: Farm
  refuses elite camps, nothing else goes to one. Matches reach minute 5 with 735 farm stacks over 20 players (36.8 each).
- **Scene, a stub-level "treat it as a farm camp"** is the F4 data above.
- **No draw:** `Elite` proposes and rides along without `cx.rng`.

## 4. New paths

- `crates/rh-policy/tests/raid_elite.rs` (the oracle).

## 5. Exclusive write scope

- `crates/rh-policy/src/bots/raid/exec/elite.rs` (the executor, its pure pricing and pick functions, its unit tests; the
  stub commit replaces its one line). It may add `exec/elite_tests.rs` if the file passes its size rule.
- `crates/rh-policy/src/bots/raid/exec/mod.rs`: **one registry line**, `Box::new(elite::Elite::default())`, placed
  **after `Chest`** (the stub already has it there; keep it). After, because the executor's own `Choose` intent must be
  merged after Loot's to replace it (F2); `propose` and `observe` draw nothing, so the position moves no draw.
- `crates/rh-policy/src/bots/raid/values.rs`: **appended only**: the `ElitePrices` struct with its `OFF` and `LOCAL`, one
  `elite: ElitePrices` field of `Prices` appended after the existing fields (after `sustain`, and after whatever
  P-RAID-Q3-fix appends: the coordinator resolves the append point), `LOCAL.elite = ElitePrices::LOCAL`, and
  `farm_only()` setting `elite: ElitePrices::OFF`. The stub commit already holds all of it; the implementer changes
  numbers and may add fields of `ElitePrices` (never of `Prices`).
- `crates/rh-policy/src/bots/raid/exec/chest.rs`: **none by default.** The priced pick is the executor's own ride-along
  `Choose`, which replaces Loot's (F2). If the implementer finds Loot's pick and the executor's fighting each other (the
  same card asked twice, a wasted ask), the one allowed change is in `Chest::ride_along` (`chest.rs:101-110`): skip Loot's
  pick for a chest whose offer the priced pick already answered. Say so in the hand-off.
- `crates/rh-policy/src/bots/raid/exec/farm.rs`: **visibility only, if decision D1 is yes**: `lock_range` and `dist`
  become `pub(super)`. No logic change.
- **Questions, not scope** (stop and ask rather than edit): a percept field (`percept.rs`), an `Act`, `bot.rs`,
  `arbiter.rs`, `refs.rs`, `goal.rs`, `exec/sustain.rs`. The facts above say none is needed.

### The candidate (the plan's form, each term with its source)

For each camp with `camps[i].elite == Some(kind)`, `alive > 0`, not banned, and the elite known alive (F1), the executor
proposes `Candidate { goal: Elite/Camp(i), value, success, death, loss, minutes }` with:

| Term | Value | Source |
|---|---|---|
| `value` | `elite.share + elite.pool_pick` | `share`: one camp-equivalent, the 12 stacks every contributor is paid (F2); `pool_pick`: the expected best of three pool cards **before the offer is known** (the cards are readable only at the chest, F2): `ElitePrices::LOCAL.pool_pick` 2.6 = the mean best of three over the 33 `augment_rows` (computed on the table; `farm_only` zeroes it). The members' stacks and the clear potion are not counted: the executor kills the elite only. |
| `death` | `clamp(prior(kind, stacks/stacks_per_camp) x weapon_factor(weapon) / held, 0.02, 0.98)` | `prior`: the bench table of F3, `ElitePrices::kind_death`, linear in banked camps between the columns 0, 6, 12, 18, held at the ends; `stacks`: the sum of `summary.mob_rows[..].stacks` (0 when the frame has none); `weapon_factor`: the weapon's banked_12 rate over 17.7 (sword 0.49, bow 2.68, wand 1.42, shield 0.38, axe 0.34, fists 0.81) from `ElitePrices::weapon_factor`, the weapon in hand; `held = hp_share + sustain.potion_hp(large, small)` as `Farm` counts it (`farm.rs:153-157`). A kind with no row falls back to `farm::death_chance(held, matchup_share_of_kind(.., Some(kind), rule), 1 + add_weight x adds, &prices.death)`. |
| `success` | `(1 - death) x prices.pick_success` | `pick_success` 0.9: the chest may expire or be taken (`values.rs`). |
| `loss` | `prices.death_loss` | existing. |
| `minutes` | `walk + elite.fight_seconds / 60 + (chest walk + prices.pick_seconds) / 60` | `walk`: `eta_minutes` / `straight_minutes` as Farm; `fight_seconds` 50: measured, executioner at 36 stacks and sword 47 to 73 s (F4); the chest walk is a short constant of `ElitePrices` (the chest lies at the camp). |

So an elite pays `(3.6 x 0.9 x (1 - d) - 6 d) / minutes`: at d = 0.1 about 2.3 ce over 1.2 minutes (1.9 a minute, above a
three-member camp's 0.9), at d = 0.44 negative (the Avalanche Troll with a sword and 36 stacks, no potion). The numbers
are `ElitePrices` fields; nothing numeric lives in `exec/elite.rs`. A term not knowable from the frame stands in as
follows: the pool's mean (offer unknown), the bench table (the real fight model), `fight_seconds` (a measured median).
**The table goes stale when P-BAL-NUM-ELITE-1 changes the per-kind numbers**: re-run `camp_bench --elite` and refresh
`kind_death` then (decision D3).

### The fight and the pick

- Walk to the camp (`exec/travel.rs` `walk`), then lock and attack as `Farm::act` does (`farm.rs:196-280`: swap to
  `best_slot_vs_mob`, `Lock` inside `lock_range`, approach otherwise), `Step::committed` while a member is within
  `FIGHT_U`. Target order: the implementer's choice (F4 measured no clear winner). The elite is `MobClass::Elite`; adds are
  `Regional`. Wait at the camp at most `EMPTY_MS` 3 s with nothing in sight, then ban the camp for `FAILED_BAN_MS` 20 s as
  Farm does, and **bound the fight itself** by a `Prices` field (`give_up_seconds`) so a fight that cannot be won (a kiting
  elite, a bow out of its range) ends in `Step::failed` and a ban.
- After the elite dies the goal walks to the chest (the chest of `facts.chests` nearest the camp, not picked, unexpired),
  stays within reach until the card is picked or the chest expires (at most 15 s) and ends `Done`. It holds the goal
  through the pick although members still stand near, because `Chest::propose` is blind while any member is awake within
  220 u (F2).
- **The priced pick** (`ride_along`, every goal, every think): for a chest with `options: Some`, take the card
  `best_card(prices, options, taken_by, held)` names: the highest `Prices::augment_value` among options not taken, not
  already held (a held card is refused `Owned`; held = the frame's `build` cards plus the cards this seat took itself),
  ties to the lowest index, `None` when the table prices nothing (`farm_only`) or no card is pickable, in which case
  Loot's pick (and its skip) stands. Send it as `IntentBody::Choose` with Loot's timing and ask-limit discipline (a refusal
  is an event; at most three asks, `ASK_WINDOW_MS` 4 s; never a wait past the chest's expiry).

## 6. Contracts and references

C-INPUT (every act through the paced queue / the Humanizer, `validate` + `Sim::submit`), C-OBS (the percept only: the
elite's kind is public in `WireCamp`, the stacks are the seat's own summary), C-TICK and determinism (no clock, no hash
order, no draw in `observe`/`propose`/`urgent`; ordered iteration over `camps`/`mobs`/`chests`), `values.rs` header rule
("no executor or arbiter file holds a price of its own": every number of F3 and of the candidate table is an
`ElitePrices` field), `crates/rh-policy/AGENTS.md` ("priorities compete every decision tick"; a commitment is the
exception, `GoalKind::preempts_commitment` is Sustain's alone). Owner decision row "Balance patch, test bot, per-elite
numbers" (`docs/OWNER_DECISIONS.md:10`): nothing here reaches a live room.

## 7. Behaviour to preserve

Existing tests that pin what this packet touches (none is amended):

| Test | What it asserts | Why it holds |
|---|---|---|
| `tests/raid_bot.rs` (4 tests) | `Prices::farm_only()`: clears the 3-member scene on 7 of 8 seeds, `rejected == 0`, replays one seed exactly | `farm_only().elite == OFF`: `Elite` proposes nothing, rides along nothing, draws nothing |
| `tests/raid_sustain.rs` (4), `raid_sustain_fix` if merged | `farm_only()` controls and `{ sustain: LOCAL, ..farm_only() }` treatments | struct update keeps `elite: OFF` |
| `tests/raid_quests.rs`, `raid_dwell.rs`, `raid_decide.rs` | quest scenes; `raid_quests` uses `..Prices::LOCAL` (elite ON): their routes pass no elite camp within a fight's reach and no elite chest exists | run and report; if a scene moves, it is the executor, not the scene |
| `raid/fix_tests.rs`, `raid/review_tests.rs` (`:270` pushes a stand-in `Alarm` after `exec::registry()`) | arbiter and registry behaviour on hand-built frames with `elite: None` camps | no elite camp in the frames: `Elite` proposes nothing |
| `raid/values_tests.rs` | the reward and augment rows against `data/raid_values.json` (`:143-165`) | rows untouched |
| `crates/rh-headless/tests/raid_seats.rs` | eight RaidBots with `Prices::LOCAL` in a real match: no fault, replays exactly | elite is ON there, so its replay changes, and it must stay deterministic and fault-free. **Not run by the quick gate: run it.** |
| `parity/`, `HumanBot` tests | untouched | HumanBot has no `Elite` |

## 8. Acceptance checks

- `scripts/gate.sh --quick --base coord/int-85 --oracle packet/P-RAID-E` passes (the full gate is the coordinator's).
- `cargo test --locked -p rh-headless --test raid_seats` passes (the quick gate does not run it).
- `cargo test --locked -p rh-policy --test raid_elite` passes in a debug build (about 2 minutes: see the oracle section)
  and `--test raid_bot --test raid_sustain --test raid_quests --test raid_dwell --test raid_decide` stay green.
- A lab comparison on seeds 1 to 6 at 6000 ticks against this base, reported in the hand-off: elite attempts and kills
  by kind and weapon, card picks and which cards, camps cleared, deaths by cause (elite deaths in particular), farm stacks at
  minute 5. The command is in section 9. The hand-off says whether bots decline elites at minute 5 (expected: most do, by F3)
  and whether any took one (the lab's `elites` and `augments` tables).

## 9. Validation commands

- `export PATH="$HOME/.cargo/bin:$PATH"; cargo test --locked -p rh-policy --test raid_elite` and `cargo test --locked -p
  rh-policy --lib raid`.
- Lab: `cargo build --release --locked -p rh-headless --bin balance_lab`; `SEATS=(); for i in 0 1 2 3 4 5 6 7; do
  SEATS+=(--seat "$i=raid"); done; ./target/release/balance_lab --human-seats --seeds 1..7 --max-ticks 6000 "${SEATS[@]}"`
  (JSON on stdout, tables on stderr).

## 10. Risk and suggested model tier

implementer-opus, reviewer-opus. Hard parts: (1) the commitment: the goal must hold through the fight and the pick but never
longer than its bounds, and must not re-propose a camp whose elite is dead or unseen (F1); (2) the pricing must be the
table's, not constants in the executor, and `farm_only` must switch the whole executor off; (3) the pick must beat Loot's
without fighting it (registry order, one `Choose` per chest, three asks); (4) no draw moves for a seat with no elite in play
(oracle test 4). One packet, not two: the pricing and fight is about 250 lines, the pick about 60, and the oracle's one
scene needs both; split only if the implementer's second attempt shows the pick is the red part.

## 11. Known traps

- **`MobSeen::camp` is a label by position** (`percept.rs:92`): an add chasing the bot reads as another camp or none, and
  the elite itself may be labelled by a neighbour once it chases (as Farm's `keepable` handles for members).
- **The elite alive is not in the camp row** (F1): `state`, `alive` and the mob rows are all there is; a camp left at
  `state == 1` heals back to full when its members get home, and still has its elite.
- **A cleared camp, an expired chest and a chest taken by another player all look like "chest gone"**: end `Done`, never
  wait for them.
- **`Chest::propose` is blind while a member is awake near the chest** (F2): the goal holds itself through the pick.
- **Loot asks a chest at most three times, 4 s** (`loot.rs:70-71`); a refusal (`Owned`, `Needs`) is an event and a spent
  ask: filter the card before sending, do not retry the same card.
- **The first-pick window**: another player's kill gives the killer 5 s alone (`NotYours`); a seat that is a contributor
  but not the killer sees `can_choose == false` until it ends: wait it out, bounded.
- **The registry order is behaviour** (`exec/mod.rs`): `Elite` after `Chest`; `propose` draws nothing.
- **Do not reuse `Farm::share` or `death_chance` alone for an elite** (F3): the share is a zone number, kind-blind.
- **Sustain does not cover this goal's leave** (F4); do not paper over it with a threshold of the executor's own.

## Oracle (loose proof) and its choices

`crates/rh-policy/tests/raid_elite.rs`, the harness of `raid_sustain.rs` (a non-bot seat, `Sim::reset_active`, the seat API
`drive_seats_with` / `Sim::step_into` / `feed_seats`, the seed1 fixture map admitted by the quest board per seed). The
build is declared setup: `rh_sim::trusted_build::apply` grants normal-mob stacks (the dev-dependency of
`crates/rh-policy/Cargo.toml` already enables `trusted-build-setup`), a checkpoint sets `potion_large` as `raid_sustain`
does. The treatment table is `Prices::LOCAL` with the quest rewards zeroed (`reward_default 0`, `reward_rows &[]`): quests
are not under test and would take the seat away. The control table is `Prices::farm_only()` (elite OFF).

Four tests, all red on the stub by assertion (the stub's `death_prior` answers 0.5, `best_card` None, `Elite` proposes
nothing), 20 s in a debug build on the stub (the real executor adds the fights: expect about 2 minutes):

1. `kills_a_favourable_elite_and_picks_a_card_within_five_minutes`: executioner camp, Holy, sword, 36 stacks, 2 large
   potions, seeds 1..=6, 6000 ticks. Floors: elite dead on at least 4 of 6, a card picked on at least 3, at most 1 death,
   the card within 400 ticks of the kill, `rejected == 0`, no fallback, no fault. Rests on F4: the Farm probe killed 6 of 6,
   picked 5 of 6, 0 deaths; the floors sit below it for a different order of targets and the sustain/leave gap.
2. `declines_an_unfavourable_elite_camp_and_takes_a_favourable_one`: the pure `death_prior` (executioner/sword/36
   stacks/held 1.8 under 0.15; troll/sword/24/held 1 over 0.35; wand over 1.2 x sword; more stacks, more hold: lower);
   wyrmling, glacialWarden and avalancheTroll with sword, 24 stacks, no potion, seeds 1..=4: the camp's `state` stays 0
   (nobody hurt it) in 1800 ticks, on every seed; the favourable executioner camp is touched on at least 4 of 6 seeds
   (so the decline is a choice). The 0.15 and 0.35 bounds are the bench table at its own numbers (executioner 3 camps
   about 0.37 x 0.49 / 1.8 = 0.10; troll about 0.91 x 0.49 = 0.44).
3. `the_pick_is_the_card_the_price_table_ranks_highest`: `best_card` on hand-built offers (best, held, taken, tie, no row,
   `farm_only` and nothing pickable give `None`); `pool_pick` within 10 percent of the rows' mean best of three; in the
   scene every card taken is among the best priced of those offered, not held (conditional cards left out) on at least 3
   picks, and at least one offer has a better card than its first (the legacy pick took the first of equals in all five
   offers of F4: three of the five offers had a strictly better priced card elsewhere); `farm_only()` never touches the camp.
4. `an_elite_run_replays_exactly_and_draws_nothing_without_an_elite`: seed 2 twice: same kill tick, offer and pick, stats and
   hash; and a three-member normal camp with the executor on or off: same stats, hash and clear tick.

**Not pinned:** a leave from a lost elite fight (D2), target order, the elite's adds after the kill, any run of
`raid_seats`. Test 2's decline rests on the kinds measured with sword at 24 stacks (F4), not on every kind. The
third test's offers depend on the reward stream: a change that moves a draw before the kill changes the offers, and the
discriminating floor of 1 could then need a look.

## Coordinator's answers (2026-11-23)

- D1 yes: `lock_range` and `dist` in `exec/farm.rs` are `pub(super)` (visibility only; the stub commit has it).
- D2: this packet adds no leave. P-RAID-C-fix has merged into the preview since this branch was cut (five lines in
  `Sustain::observe`), so `exec/sustain.rs` is free again, but it stays out of this packet's scope. `P-RAID-E-leave` is
  the follow-up; this packet's lab comparison sizes it.
- D3: the per-kind death table is an `ElitePrices` table naming its source file; the elite-number packet's hand-off will
  list "refresh `ElitePrices::kind_death`".
- D4: one packet. D5: left as is.

## Oracle files (read-only for implementers)

- `crates/rh-policy/tests/raid_elite.rs`
- `docs/backlog/packets/P-RAID-E.md` (this brief)

The `stub:` commit (the implementer's to replace: `exec/elite.rs`, `exec/mod.rs`, `values.rs`) is not an oracle file. No
existing test or capture is amended.

## NOT ESTABLISHED

- The elite bench's rates are for a scripted Duelist, which never retreats and does not read dodges (README caveat): a
  RaidBot with sustain should die less, so the table is a ceiling on the death chance and makes the bot too shy. The
  lab's first elite attempts calibrate it (`ElitePrices` numbers, not code).
- The weapon factor assumes weapon and kind multiply (bench banked_12 by weapon, over all kinds). Measured only
  qualitatively (sword kills executioner, wand does not, F4).
- The scenes measure one fixture map, one faction (Holy), one weapon each, 3 to 6 seeds per cell; "favourable" for another
  faction is not measured.
- `pool_pick` is the mean over all 33 rows; the real pool depends on the contributors' factions (native cards offered to
  one faction only, `reward_chests/pools.rs` `Offer`), the tier weights and cards held.
- Whether a RaidBot in a full match fights an elite at all at minute 5 (stacks 25 to 37 per player: F3 says mostly no): the
  lab comparison reports it; the oracle forces the build.
- Behaviour against two bots at one elite camp (shared chest, `Taken` cards, friendly fire): not scened.

## Decisions for the coordinator (default in force)

- **D1: `farm.rs` visibility.** May `lock_range` and `dist` become `pub(super)` so `Elite` shares them (no logic change)?
  Default: **yes**, visibility only; the alternative is eight duplicated lines in `elite.rs`.
- **D2: the leave from a lost elite fight.** Sustain's disengage does not see an Elite goal or an elite mob (F4) and
  `sustain.rs` is P-RAID-C-fix's this round. Default: **this packet adds none**; its own death term turns the score negative
  as hp falls, the drink works, and the oracle does not test a leave. A follow-up `P-RAID-E-leave` (after C-fix merges) widens
  `Sustain::in_fight`, `view` and `nearest` to `GoalKind::Elite` and `MobClass::Elite` and prices the leave with the elite's
  share; the lab comparison of this packet says how many elite fights end in a death to size it.
- **D3: the bench table in `Prices`.** The per-kind death table copies `elite_after.txt` and goes stale when
  P-BAL-NUM-ELITE-1 lands. Default: **keep it as an `ElitePrices` table with the file's name in its comment**, and the
  elite-number packet's hand-off lists "refresh `ElitePrices::kind_death`" as a step. The alternative (derive the rows from
  `camp_bench --elite` at build time) is a tool, not a packet.
- **D4: one packet.** Default: **one**; see section 10.
- **D5: farm stacks of the elite's adds.** The adds stay alive after the kill and Farm refuses the camp for good (F1):
  they cost the bot hp and pay nothing. Default: **left as is**; `P-RAID-E-leave` or P-RAID-C2 decides whether Farm may
  take the remains of an elite camp once the elite is dead.

## Coordinator's additions (2026-11-23, after the oracle)

- **Base.** The coordinator merged the preview `coord/int-85` @ `1bd9cc1dd` into this branch (`b51a146c3`): it brings
  P-RAID-C-fix, P-RAID-Q3-fix and the UI lane's wave 2. `scripts/gate.sh --oracle-check --base coord/int-85` passed
  again on that head (red in `raid_elite` only).
- **`Prices`.** `elite` is the LAST field of `Prices`, after the decision prices P-RAID-Q3-fix appended
  (`spare_weapon` to `recharging_share`). P-RAID-Q3-fix2 is in flight and appends one field before it; the
  coordinator resolves that at merge. The implementer adds fields to `ElitePrices` only.
- **`raid_seats`.** `cargo test --locked -p rh-headless --test raid_seats` is an acceptance step for the implementer
  and the reviewer (the quick gate does not run it), as section 8 says. With `Prices::LOCAL` the elite executor is
  live in those matches: they must stay fault-free and repeat exactly.
- **`exec/sustain.rs`** stays out of scope although P-RAID-C-fix has merged: the leave from a lost elite fight is
  `P-RAID-E-leave`. If the lab comparison shows elite fights ending in deaths the leave would have prevented, say so
  with the numbers; do not widen Sustain here.
