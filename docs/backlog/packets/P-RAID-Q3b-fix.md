# P-RAID-Q3b-fix: a quest walk past an idle camp is not a fight; the Trade Up cash-out stand outlasts the trader's patrol

- **Serves:** not in the plan: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot, per-elite
  numbers"; plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 1). Closes the two notes of reviewer-opus on
  P-RAID-Q3b.
- **Implementer tier:** implementer-sonnet (single crate, `rh-policy`, bot pricing only; no `rh-sim`, no wire). reviewer-opus
  reviews (it changes the price of every decision and dwell quest, and of one committed goal's stand).
- **Reviewer:** reviewer-opus
- **Integration base:** `coord/int-88` @ `60f432c19` (gate and oracle check with `--base coord/int-88`)
- **D-rules touched:** D1 loose proof (RaidBot has no legacy counterpart) · D2 n/a · D5/D6/D9 none (no sim, wire or
  observation change; the bot acts only through `validate` + `Sim::submit`)

## 1. ID and outcome

`RaidBot` prices the death of a quest's walk and stand once, in one helper in `quest/mod.rs`, and the three copies of the old
term go. A living camp that is idle near a point adds nothing (the sim never makes an idle camp attack a passer-by); a camp
that is already awake near the point (a living mob of it is `engaged` in the frame) still counts; what a quest's own
objective does by design is a small measured table. Trade Up goes back to the Q3b brief's rule (while going on, the
candidate is the next stage's worth; `trade_go_on` reads the new term), and its cash-out stand at the remembered trader
point outlasts the trader's patrol and is not followed by a ban while progress is held.

Why: P-RAID-Q3b's reviewer found `death_near` counts every living camp whose centre lies within `CAMP_JOIN_RADIUS` (480 u) of
a point as a fight the seat joins (trader 0: 0.36 for a Holy seat, 0.61 for others, from one five-member camp 256 u away at
full hp; no camp within 200 u of any trader on eight seeds). At progress 1 or 2 the inflated term feeds `trade_go_on`, so a
camp within 480 u of the next trader makes the seat cash out at 12 stacks instead of 54 (the Q3b implementer worked around it
at progress 0 only, by pricing the whole route while going on: a departure from the brief); a hurt seat can accept at the
floor death and price the held quest negative right after. And the cash-out stand (`END_STAND_MS`, 4 s) is shorter than the
time the patrolling trader stays out of contact range (about 6.7 s), so it fails and bans Trade Up for 30 s with the seat
holding progress and the timer running (never run in a test or lab run).

The pilot (40 full-length matches, eight test bots; `docs/evidence/balance-patch-2026-11/pilot/README.md` on
`feature/unity-rework`): of the fourteen quests played, twelve never killed a seat; Let Him Cook's attempts ended in a death
7 times of 189, Hold the Fort's 9 of 32.

## 2. Dependencies

`P-RAID-Q3b` (`decide.rs` Trade Up and Two Banners, `Prices::trade_up_stage_share`), `P-RAID-Q1`/`-Q2` (walker, dwell),
`P-RAID-V` (`Prices`), `P-RAID-E` (`elite` is the last field of `Prices`): all in `coord/int-88`.

## 3. Facts (verified on `coord/int-88`; paths in `crates/rh-sim/src/` unless stated; bot paths in `crates/rh-policy/src/bots/raid/`)

### 3.1 What the code does today: the three copies

All three are the same sum: for each living camp (`camp.alive > 0`) whose centre lies within `CAMP_JOIN_RADIUS`
(`percept.rs:18`, 480 u) of the point(s), `death_chance(hp_share, matchup_share(me, zone_faction(camp.zone), false),
members_left(camp), &prices.death)` (`exec/farm.rs:40-54`); the camps combine as `1 - prod(1 - d)`; the result is floored at
`prices.death_floor` (0.02 on `Prices::LOCAL`, `decide_fix_tests.rs:481`).

| Copy | Inputs | Where it is applied |
|---|---|---|
| `quest/walker.rs:211` `death_near(cx, points: &[Vec2])`, own `shares` cache | any point of the slice within 480 u | **Only `let_him_cook`** (`walker.rs:290-296`): points = the view's objectives after accepting, else the Plague camp nearest the giver (`walker.rs:270-282`). Every other walker quest takes `prices.death_floor` (comment `walker.rs:285-289`: counting a camp near every quest was measured to drop accepted `ember_walk`s). So **the coordinator's "eleven quests" is one quest in code**: ten walker quests are already at the floor. |
| `quest/dwell.rs:252` `death_near(cx, point: Vec2)`, own `shares` cache | one point: Hold the Fort's fort token (accepted) or the giver (before); Stay A While's giver | **Both dwell quests**, `dwell.rs:346` (`death: self.death_near(cx, spot)`, `spot` set at `dwell.rs:286-335`). |
| `quest/decide.rs:614` `death_near(&mut self, cx, points)`, inline `shares` | slice | Two Banners' outpost (`banners_terms`, `decide.rs:700`: the objective in the view, else `outpost_guess`) and Trade Up's next trader (`trade_read`, `decide.rs:820`; floor when no trader is shown). Trade Up's accept (progress 0 not accepted) and cash-out candidates use the floor (`decide.rs:854`, `:872`); the going-on candidate values the whole route (`trade_banked(.., 3)`, `decide.rs:879`) and carries `read.death`. |

The hp scaling of the existing term: `death_chance` is `clamp(death_per_ratio * (hp_needed / hp.max(0.05) - safe_ratio),
0.02, 0.98)` with `hp` = `Me::hp_share()` (`percept.rs:41`): the chance grows with 1 / hp share (a hurt seat is likelier to
die to the same danger) from `safe_ratio` up, per camp, and is clamped at 0.02 below and 0.98 above.

### 3.2 What can hurt a seat on each quest's route or objective (question 1)

The sim's quest system never deals damage to a camp, wakes or spawns a mob on a quest step: `quests/board/attempts/` has no
`aggro`, spawn or mob damage (the crown's burn, `signals.rs:309-324`, is on a quest the bot does not play; the kill targets
`targets.rs` are quests the bot does not play). An idle mob is woken only by damage to it (`systems/mob_life.rs:234` ->
`mob_ai::aggro` `mob_ai.rs:75-102`), a `Block` event (`mob_ai.rs:211-222`), a camp mate's death to a player
(`mob_life.rs:386`, `:452` -> `wake_mates` `mob_ai.rs:105-126`), or a quest reward spell's effect (`quests/board/rewards/
effects.rs:494`, Warhorn's taunt: a cast, not a quest step). `mob_ai::think` skips idle brains (`mob_ai.rs:236`). So a walk
past, or a stand beside, an idle camp is not a fight, by the sim's rules.

| Quest | Objective (`placement.rs:545-610`) | What can hurt the seat | By design? |
|---|---|---|---|
| `ember_walk` | three tiles of terrain 4 in the home zone (`placement.rs:559`) | Terrain 4 is burning ground ("Lava", `terrain.rs:42`, `:57`): standing on it burns every non-Fire seat (`systems/terrain/mod.rs:98-140`), and the three tokens stand on it. Damage from the ground, not a camp. | Yes, a hazard, not priced here (not a fight; no measured death) |
| `watchtowers`, `barrow_run`, `pilgrims_path` | features (towers, barrows, pilgrim stops) | Walk only: awake mobs, hazard ground on the way, players | No |
| `a_cruel_joke` | three other givers' places | Walk only (the contacts are givers, not camps) | No |
| `even_skeletons_get_cold`, `tuber_eats`, `letters`, `lost_brother` | one contact in another zone (`placement.rs:554-557`, `:596`) | Walk only; a contact is a dry site nearest the zone's pole, not a camp | No |
| `smugglers_run` | contact in another zone, then the giver; the carry is publicly marked (`attempts.rs:439-461` `public_markers`) | Players hunting the marked carrier; awake mobs | No (the marker is a player danger) |
| `let_him_cook` | Holy contact, Frost contact, **a Plague camp's centre** (`placement.rs:561-565`) | The seat stands at the middle of an idle camp: no step wakes it, and the measured outcome is 7 deaths in 189 attempts (3.7 %) | **Yes, by measurement** (row `let_him_cook` 7/189) |
| `hold_the_fort` | a guard house of the home zone (`placement.rs:552`); the hold is publicly marked on every map (`attempts.rs:439-461` `public_markers`) | Players who see the mark and contest it (`interactions.rs:304-310` pauses on a contestant); mobs that wander by | **Yes, by measurement** (row `hold_the_fort` 9/32 = 28 %: the stand is long, shown on every map, and a player fight is what ends it) |
| `stay_a_while` | the giver (monk) | Players near the monk (`dwell.rs` `MONK_DANGER_U` is the bot's own danger read); any damage breaks it | No row (unmeasured; the default) |
| `the_bargain` | a contact in the home zone (`placement.rs:584`) | Walk only (its candidate already prices at the floor, `decide.rs:1181`) | No |
| `two_banners` | the first available non-target camp of the zone farthest from the Gallows pole; the token stands on **the camp's centre** (`placement.rs:558`, `:290-310`) | The seat stands at the middle of an **idle** camp, as in Let Him Cook's third contact: nothing in the carry or the turn-in wakes it. Not a fight by design. | No (see section 4, OPEN: the existing test prices the outpost's camp) |
| `trade_up` | three traders patrolling 72 u on a 12 s period (`interactions.rs:418-455`) at a Fire, a Plague and a Frost contact (`placement.rs:598-602`: dry sites by the zone poles, no camp within 200 u of any trader on eight seeds) | Walk only | No |

### 3.3 What else hurts a walking seat, which the camp term was standing in for (question 2)

- **A mob that is already awake**: a camp whose members fight someone else (a player, another bot) or that a camp mate's
  death to a player woke. In the percept a mob row carries `engaged` (`percept.rs:100`, from `ACTOR_FLAG_ENGAGED`,
  `actor.rs:49`: "its attack cooldown runs or its bow is drawn") and `camp` (a label: the nearest camp within 480 u,
  `percept.rs:96-99`). This is what the helper counts. Limit: an awake mob that is chasing and not yet attacking has the flag
  clear; the reading is one frame and blind to those.
- **Players** (hunters of a marked carrier, contestants of a fort, anyone in PvP): not a camp; `dwell.rs` reads them by
  `others_near` for its success chance already; nothing here changes that.
- **Hazard ground** (burn terrain 4 for non-Fire seats, `terrain.rs:56`): on a route, not priced by the camp term; unchanged.
- **A camp the walk crosses while the seat carries something**: the carry changes no rule for mobs (no sim system reads
  `carrying` for mobs); the only change a carry brings is the public marker (players, above).

### 3.4 The cash-out stand (note 2)

- `decide.rs:903` `act_trade`: with progress > 0 and going on false, the seat walks back to `last_trader` (the trader's
  position at the barter, `decide.rs:937`), then at `stand_u()` (32 u) calls `stand_at_end(TRADE_UP, ..)`
  (`decide.rs:793-805`): after `END_STAND_MS` (4 s, `decide.rs:53`) with no turn-in it pushes a 30 s ban (`BAN_MS`,
  `decide.rs:59`) and fails.
- The sim: a trader's position is `from + offset * fraction` with `phase = ((now + 3000 * i) % 12000) / 6000` and
  `fraction = 1 - |phase - 1|` (`interactions.rs:436-437`), a 72 u offset along the first clear axis (`:439-445`). The cash-out
  is offered only within `contact_range` (64 u) of the trader's CURRENT position (`choices.rs:91-93`; `endpoint` for Trade Up
  is `trader_at(progress - 1)`, `attempts.rs:413`). A seat standing 32 u from the remembered point on the side away from the
  patrol, with the barter made at fraction 0, is out of contact for `fraction > 0.444`: 6.67 s of every 12 s.
- The patrol's period (12 s), its phase step and its length are **not** in `public_rules()` (`tuning.rs:231-262`: `fort_ms`,
  `meditate_ms`, `contact_range`, `fort_range`, `bargain_cost`, `bargain_stacks`, `route_pace`, `route_slack_ms`). The
  implementer writes the period in the bot with the sim line beside it and guards it against the sim's source the way
  `spells.rs:254` does (`include_str!` of the sim file, parsed, loud on a format change; and in the same test style as
  `decide_b_tests.rs` `sim_stacks`). No sim change.

## 4. The coordinator's decisions (2026-11-23), kept, and where the code says otherwise

1. **One helper in `quest/mod.rs`** prices the death of a quest's walk and stand. The walker, the dwell executor and the
   decision executor all call it; the three `death_near` copies and their `shares` caches go (`matchup_share` caching, if it
   is kept, lives with the helper). An idle camp near a point adds nothing. What it counts: a camp that is already awake near
   the point (a living mob of it with `engaged` in the frame; the camp comes from the mob's `camp` label) priced by the
   existing per-camp `death_chance`, and what the quest's own objective does by design.
2. **By-design table, from measurement**: a `Prices` field appended before `elite` (file's append rule), working name
   `quest_death_share: &'static [(&'static str, f64)]`: the share of attempts that end in a death at full health, by quest id,
   default `death_floor` for a quest with no row. Rows: `let_him_cook` 7 / 189, `hold_the_fort` 9 / 32, each with its source
   in a comment (the pilot README path and the counts), and a range test of its own (every row within `death_floor..=0.98`,
   ids unique and in the catalog). Hand-edited in a reviewed packet after a lab run; nothing rewrites it. It is not mirrored in
   `data/raid_values.json` (`values_tests.rs` pins only the reward and augment rows); say so in a comment.
3. **Health still matters**: the term at hp share `h` is the row's share (or the default) scaled by the seat's hp as the
   existing term is, 1 / `h.max(0.05)`, clamped at 0.98 and floored at the row's share (so at full health the term equals the
   row, a hurt seat's is higher for the same quest, and never below the floor). The oracle pins the three properties
   (equal to the row at full hp within 15 %; strictly higher at 30 % hp; never below `death_floor`), not the formula.
4. **Trade Up returns to the brief's rule**: while going on, the candidate's value is the next stage's worth
   (`trade_banked(prices, progress + 1)`, i.e. the reward row times the next stage's share), its death the new helper's term
   for the next trader's point, its minutes the walk (the P-RAID-Q3b departure, `decide.rs:876-886`, is reverted; the accept
   at progress 0 and the cash-out candidate keep their forms), and `trade_go_on` reads the new term. The seat must still never
   accept and then stand until the timer runs out: the six Trade Up seeds of `raid_decide_b.rs` (the six that showed that
   freeze with the first form) stay green, unchanged.
5. **The cash-out stand**: for a Trade Up cash-out with progress above 0, the stand at the remembered trader point is
   bounded by at least one patrol period (12 s; the oracle waits 13 s of out-of-range time) and is not followed by a ban while
   progress is held (no `BAN_MS` push for Trade Up; a Two Banners stand keeps today's 4 s and ban). The seat gives up the
   cash-out only when the time left is under the walk (the walk to the remembered point; at the point, never).
6. **Two Banners keeps its outpost pricing** (decision as given). **OPEN, for the coordinator before the implementer
   starts**: the code says the outpost's camp is *another idle camp*, not a fight by design (`placement.rs:558`, a
   non-target camp; section 3.2), and Let Him Cook's third contact, also a camp's centre, measured 3.7 %. Under the rule
   "an idle camp adds nothing" the outpost would be priced at the default and the existing test
   `raid_decide_b.rs` `a_table_that_makes_every_camp_deadly_makes_the_seat_decline_two_banners` (a `DeathModel` that makes any
   camp fight deadly must make the seat decline on all six seeds) would turn red: that test pins that the outpost IS priced
   by the camp fight. **This oracle follows the decision as given: the outpost's camp keeps today's term** (the old per-camp
   `death_chance` over camps within 480 u of the outpost, passed to the helper as a stand inside a camp), so that test stays
   green and is not amended; Two Banners' pricing is not red in this packet and a guard pins the kept term. If the
   coordinator rules the other way, the amendment is an `oracle-amend:` of that one test (second half), listed for the owner,
   and the guard below goes with it.
7. **No change to what a FIGHT costs**: `exec/farm.rs`, `exec/elite.rs`, `exec/sustain.rs` and the death model are out of
   scope.

## 5. Existing tests that pin what this packet changes (listed; none amended)

- `tests/raid_decide_b.rs` (Q3b oracle): `a_table_that_makes_every_camp_deadly_makes_the_seat_decline_two_banners`
  (asserts the outpost's camp is priced by `DeathModel`: **kept green by decision 6**); `a_deadly_table_banks_no_more_than_
  the_normal_table_and_never_loses_progress` (deadly Trade Up banks <= normal, no progress lost: with the new term the deadly
  table prices Trade Up at the default, so deadly == normal, which still passes); `raid_bot_plays_trade_up_banks_a_stage_and_
  never_loses_progress_to_its_timer` and `raid_bot_accepts_two_banners_chooses_within_a_bounded_time_and_completes_it` (the
  six seeds each; the freeze guards); the two lost-fight guards (Sustain's leave; the death term is not what leaves).
- `tests/raid_dwell.rs`: `raid_bot_accepts_and_completes_each_dwell_quest` (a floor of 4 of 5 seeds per quest on `DWELL_ONLY`;
  **Hold the Fort's death term rises from the camp-priced value to its row, 0.28**, so a Hold seed near a camp can move either
  way: the implementer reports the per-seed numbers if a floor moves and does not edit the test),
  `raid_bot_does_not_sit_in_a_contested_fort_for_ever`, `raid_bot_takes_only_walker_and_dwell_quests_...`.
- `tests/raid_quests.rs`: `raid_bot_accepts_and_completes_walker_quests_before_their_timers` (the walker table prices
  `let_him_cook` at 1.0; with the row 0.037 and no camp term it can only become likelier to accept).
- `quest/decide_fix_tests.rs:562-603` pins `death_floor` read from `Prices` for a pick-up (unchanged: the pick-up keeps the
  floor); `quest/walker_tests.rs`, `dwell_tests.rs`, `decide_*_tests.rs` hand-build frames with `camps: Vec::new()` and none
  asserts a camp death term. A dwell unit test that asserts a Hold candidate's death at the floor would move to the row: none
  does (grep `death` in the five files).
- `quest/public_rules_tests.rs`: source scans that `decide.rs`, `walker.rs`, `dwell.rs` hold no hand copy of a quest number and
  read `public_rules()`: **read it before editing** (it pins the three files; the patrol's 12 s lives in the helper's own file or
  in `decide.rs` with its guard, and the scan decides which: do not edit the scan).
- `values_tests.rs` pins only the `rewards` and `augments` slices of `data/raid_values.json`: no row moves.

## 6. Exclusive write scope

- `crates/rh-policy/src/bots/raid/quest/mod.rs`: the helper (and its `mod` lines for the implementer's own tests).
- `crates/rh-policy/src/bots/raid/quest/walker.rs`, `quest/dwell.rs`: each only to call the helper (delete `death_near`, its
  `shares` field and the unused imports).
- `crates/rh-policy/src/bots/raid/quest/decide.rs`: the call sites, Trade Up's stage value and the cash-out stand (and the
  patrol period with its guard).
- `crates/rh-policy/src/bots/raid/values.rs`: one appended field before `elite`, its two `LOCAL` rows, `farm_only` keeps
  `..Self::LOCAL`; a range test of its own in a new test file or module of the implementer's (never in `values_tests.rs`).
- Not touched: `exec/*`, `arbiter.rs`, `spells.rs`, `goal.rs`, `acts.rs`, `refs.rs`, `data/raid_values.json`, anything under
  `crates/rh-sim`, every oracle file, `Cargo.*`.

## 7. Behaviour to preserve

- `Decide`, `Walker` and `Dwell` draw nothing from `cx.rng`; a seat with nothing to decide proposes nothing; `Prices::
  farm_only()` and any table that prices a quest at 0 propose none of it.
- A quest not named in the table is priced at `death_floor` (scaled by hp as in decision 3).
- Two Banners' outpost term (decision 6), the Bargain, the pick-up and the pending slot choice are unchanged.
- No hand copy of a quest number (the walker's, dwell's and decide's `public_rules()` reads).

## 8. Acceptance checks

- `scripts/gate.sh --quick --base coord/int-88 --oracle packet/P-RAID-Q3b-fix` passes.
- `cargo test --locked -p rh-headless --test raid_seats` and `cargo test --locked -p rh-policy` (every suite of section 5).
- `python3 scripts/readability_check.py` passes (`decide.rs` is 1 207 lines; a new `quest/*.rs` file is allowed if one
  stays under 1 500).
- A lab run, release, seeds 1 to 12, 6000 ticks, eight raid seats, human seats, base against head (run at `--jobs 3`),
  reporting per quest dealt, accepted, completed, timer ends and deaths, and for the whole run camps cleared and player
  deaths. More quests accepted is expected; more deaths while questing would mean the idle-camp reading is wrong somewhere,
  and the implementer says where (which quest, which seat, what woke what).

## Oracle files (read-only for implementers)

- `crates/rh-policy/tests/raid_walk_risk.rs`
- `docs/backlog/packets/P-RAID-Q3b-fix.md`

(table of the tests, their state on the base and their floors: section 9, filled with the measurements)

## 9. The oracle's tests

`crates/rh-policy/tests/raid_walk_risk.rs`: hand-built percepts driven through the public executors (`Walker`, `Dwell`,
`Decide`), as `raid_hold.rs` does; no stub is needed (nothing names a symbol that does not exist: the `Prices` row table is
not named, only behaviour). The death term is read off `Candidate::death`, the smallest public entry. Holy seat, five-member
idle camp 256 u from the point, `Prices::LOCAL`. Measured on the base: **8 red by assertion, 3 green guards**.

| Test | Base | Floor and the measurement it stands on |
|---|---|---|
| `an_idle_camp_near_let_him_cooks_points_prices_it_at_its_row` | red | within 15 % of 7/189 = 0.037, before and after accepting, with and without a Plague camp (base: 0.02 without, 0.125 with) |
| `hold_the_fort_is_priced_at_its_row_with_or_without_an_idle_camp` | red | within 15 % of 9/32 = 0.281 (base: 0.02 unaccepted; 0.36 with the Fire camp) |
| `an_idle_camp_adds_nothing_to_a_quest_without_a_row` | red | Stay A While equals `death_floor` 0.02 with an idle camp (base 0.362); `ember_walk` the same (green on the base alone) |
| `a_hurt_seat_is_likelier_to_die_to_the_same_walk` | red | at 30 % hp strictly above 100 % hp for Let Him Cook, Hold the Fort, Stay A While; at most 0.98, at least the floor (base: Let Him Cook 0.02 both) |
| `trade_up_goes_on_past_an_idle_camp_at_progress_one` | red | seat at trader 1, progress 1, idle camp 256 u away: barter sent (target 1) and the candidate is worth 5.6 x 30/54 = 3.11 within 2 % (base: no act, candidate 1.24 = the cash-out's) |
| `trade_up_going_on_is_worth_the_next_stage_not_the_whole_route` | red | no camp: progress 1 candidate 3.11 and barter, progress 2 candidate 5.6 and barter, within 2 % (base: 5.6 at progress 1, the whole route) |
| `a_cash_out_stand_waits_out_the_patrol_and_is_not_banned` | red | barter at fraction 0, back at the point 30 u away 2.9 s later (out of contact 2.9-9.2 s), frames every 0.5 s for 13 s: never `Failed`, the cash-out is sent (computed from the patrol, 6.3 s after arriving; asserted no sooner than 5 s; not measured on an implementation) and `propose` still offers Trade Up (not banned). Base: fails at 4.0 s and bans |
| `an_awake_camp_near_the_points_still_counts` | green | an `engaged` member of the camp: Stay A While >= 0.25 (0.362), Let Him Cook >= 0.1 (0.125), accepted or not |
| `guard_two_banners_keeps_pricing_the_camp_at_its_outpost` | green | outpost with a five-member Fire camp: death >= 0.25 (0.362) |
| `guard_the_patrol_mirrored_here_is_the_sims` | green | `interactions.rs` still holds the three patrol lines this file mirrors |
| `the_rows_sit_above_the_default_and_in_the_pilots_order` | red on the base (the rows do not exist) | Let Him Cook > 1.3 x the default, Hold > 3 x Let Him Cook, at full hp, no camp |

**Not in this file (named, not run by the oracle author):** the six Trade Up seeds and six Two Banners seeds are guarded by the
existing `raid_decide_b.rs` tests (section 5), unchanged; the implementer runs them. A live scene "a seat offered Let Him Cook
under default prices accepts on a seed where the base declines or outbids it" was **not built**: the base's Let Him Cook term
against a Holy seat is at most 0.125 for one camp in the hand-built scene, which leaves the quest well above the roam rate
(3.7 ce against 0.75 ce of death over minutes), so no accept/decline flip is expected on the fixture map; the pricing
assertions above are the oracle for it. Not searched seed by seed.

## 10. Risk and suggested model tier

Sonnet. The hard parts: the helper's input from the percept (`mobs` with `engaged` and `camp` label against `camps`); Hold the
Fort's row moves a dwell candidate by 1.7 ce (check the dwell scenes); the stand's bound must not become a freeze (it is
bounded by the time left and the timer's own expiry).

## 11. Known traps

- The `camp` label of a mob is where it stands now (nearest camp within 480 u), not its home (`percept.rs:96-99`).
- `death_chance` already clamps at 0.02; the floor and the row are applied after, not before, the hp scaling.
- `last_trader` is set only when the barter is sent (`decide.rs:937`): the stand's point is the trader's position AT the
  barter, which the patrol then walks away from.
- `Step::committed` holds the seat against everything but a Sustain leave; a stand that outlasts the patrol is held the same
  way, so it must end by itself (the time left, the deadline, the row gone).

## Notes from previous attempts
<!-- Coordinator appends on escalation. -->
