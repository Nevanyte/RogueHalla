# P-RAID-Q3b: RaidBot quest decisions, part 2 (Two Banners, Trade Up)

- **Serves:** not in the plan: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot, per-elite
  numbers"; plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 1, P-RAID-Q3's second half)
- **Implementer tier:** implementer-sonnet (reviewer-opus reviews: the two quests change the arbiter pricing of a committed
  goal, as P-RAID-Q3-fix2 did). Opus only if the sim-side addition of section 9 is taken (cross-crate).
- **Reviewer:** reviewer-opus
- **Integration base:** `coord/int-87` @ `d52f8a769` (gate with `--base coord/int-87` until it is merged)
- **D-rules touched:** D1 loose proof (RaidBot has no legacy counterpart) · D2 n/a · D5/D6/D9 none (no sim, wire or
  observation change; the bot acts only through `validate` + `Sim::submit`)

## 1. ID and outcome

`RaidBot` accepts **Two Banners** and **Trade Up**, plays each to its end and is never frozen by either. Both are
`GoalKind::QuestDecide` goals in `quest/decide.rs` (extending the Q3 executor as it stands after P-RAID-Q3-fix and -fix2:
bounded waits, the remembered token, a pending choice's walk priced, the Bargain's leg a price).

Why now: a 40-seed lab run of eight test bots (full-length matches) dealt `two_banners` 136 times and `trade_up` 144 times
and accepted neither once: no executor in `raid/` accepts them (`walker.rs` `WALKER` lists eleven quests and neither;
`decide.rs` handles slot, pick-up and Bargain). The same run dealt `feed_or_hunt` 128 times (never accepted),
`watchtowers` 112 (accepted 8) and `letters` 120 (accepted 21): those three are not this packet's (no test here).

## 2. Dependencies

`P-RAID-Q3`, `-fix`, `-fix2`, `P-RAID-TOP25-READ-1`, `P-RAID-V` (`Prices`), `P-RAID-C` (sustain), `P-RAID-E` (`elite` is the
last field of `Prices`): all in `coord/int-87`. P-RAID-S (in review) touches `acts.rs`, `refs.rs`, `spells.rs` and `exec/`:
this packet avoids all four.

## 3. Facts (verified in code on `coord/int-87`; paths in `crates/rh-sim/src/quests/board/` unless stated)

### Two Banners (`two_banners`, Gallows giver, reward Warhorn single-use spell, no route timer, clock Race, `catalog.rs:245-256`)

- **The choice.** At the giver within `contact_range` (64 u, `attempts/choices.rs:83-86`) the frame offers action 2 with
  target 0 ("Carry the Gallows banner") and target 1 ("Carry the rival banner"); `attempts/interactions.rs:97-103` stores
  the branch; branch 0 sets `carrying` at once, branch 1 does not. **Before the choice the view shows no objective**:
  `objective_for_owner` (`interactions.rs:395-396`, `:402`) answers `Some` only once `branch.is_some()` (index 0 = the outpost
  token, or the giver once branch 1 carries). So the seat chooses blind: the outpost's place appears only after the choice,
  and the choice cannot be undone (abandon is action 1; the attempt restarts at the giver).
- **Branch 0.** Objective and end point: the rival outpost (`attempts.rs:412` `endpoint`: `TwoBanners if branch == Some(0)` =
  `tokens[0]`). `interactions.rs:288` (`SmugglersRun | TwoBanners if carrying && at_end`) achieves it within contact range
  of the outpost; the turn-in is at the outpost, not at the giver (`attempts.rs:429` `ends_at_giver`: `branch != Some(0)`),
  so a scared-off giver does not block it. Route: giver -> outpost, one way.
- **Branch 1.** Walk to the outpost, action 3 target 0 within contact range ("Take the rival banner", `choices.rs:45`,
  `interactions.rs:113-119` sets `carrying`), then the objective is the giver (`interactions.rs:395`) and standing there turns
  it in. Route: giver -> outpost -> giver: **branch 0's route plus the walk back**, the same camp reached at the same
  distance. Nothing in the sim makes it shorter, safer or better paid (one reward, Warhorn).
- **The outpost** is `placement.rs:558` `self.camp(self.distant(zone)?, false)`: the first available camp (map order) of the
  zone farthest from the Gallows pole; the token stands on the camp's centre (`placement.rs:290-310`). Positions measured in
  section 5.
- **Death** drops the attempt (`attempts/signals.rs:304-306`, `retain`: only a completed or pending attempt stays): the
  carry is lost with it, on either branch.
- **The carry is not publicly marked.** `public_markers` (`attempts.rs:458-461`) marks `SmugglersRun`, `HoldTheFort` and
  `StayAWhile` only (the crown and the bounty have their own fields); `TwoBanners` is not there.
- **The Race clock does nothing to a second seat.** `ClockKind` (`catalog.rs:162-168`) is read only to hash the catalog
  (`catalog.rs:495`): no system reads `def.clock`. Board mode keeps one attempt per owner and offer; `accept_eligibility`
  (`quests/live.rs:608-650`) refuses only an already-accepted or completed owner; the view sends `done_by: None`
  (`quest_view/board.rs:112`). The catalog text says it: "your choice does not close either branch for others". Two seats
  holding Two Banners walk it independently; neither loses the quest because the other finished (`live.rs:369` `done_by` is
  the legacy offer-level completion, not the board's).
- **Reward and slots.** Warhorn is a spell: accepting takes a slot (`quest/mod.rs` `spell_slots_taken`, the walker's rule: no
  spell quest with both slots spoken for); with both slots full the reward waits as a pending slot choice answered at the
  turn-in point (the outpost on branch 0: inside the camp; the giver on branch 1) by the existing stage 0 of `decide.rs`.

### Trade Up (`trade_up`, Sanctum (Holy) giver, reward `TradeUp` permanent, timer 95 s, `catalog.rs:465-475`)

- **Deadline.** `attempts.rs:326-329`: `max(95 s, route_length x route_pace / PLAYER_SPEED + route_slack_ms)` after the
  accept; `route_length` (`attempts.rs:352-388`) is giver -> trader 0 -> 1 -> 2 (no leg back: `TradeUp` is in the exclusion
  list at `:380`). The row's `expires_at` carries it. The attempt is abandoned at the deadline (`attempts.rs:495-497`),
  progress and all; a ready or pending attempt is not (`:489`).
- **Three traders**: tokens at a Fire, a Plague and a Frost contact (`placement.rs:598-602`), each walking a 72 u patrol on a
  12 s period, phases 3 s apart (`interactions.rs:418-455` `walk_traders`); `trader_at` (`:408`) is the walked position. The
  objective is the **next** trader's current position (`interactions.rs:388`: `TradeUp if index == a.progress`).
- **Barter**: action 3 with `target == progress` within contact range of that trader (`choices.rs:65`,
  `interactions.rs:191-198`): progress + 1; at 3 the quest is achieved with points 3.
- **Cash out**: action 4 target 0 whenever `progress > 0` and the seat is within contact range of the *last bartered* trader
  (`choices.rs:91-93`, `interactions.rs:199-212`; `endpoint` for `TradeUp` = `trader_at(progress - 1)`, `attempts.rs:413`):
  points = progress, achieved. A cash-out needs no wait.
- **Turn-in**: standing at the last trader sets `pending` (`interactions.rs` `tick`); the reward is
  `trade_up_stacks[points - 1]` = [12, 30, 54] stacks of the Penitent mob (`rewards/permanent.rs:176-185`, `tuning.rs:159`),
  paid once per run (a permanent).
- **Not public**: the stage stacks. The reward text says only "Escalating permanent stats; cash out at any step"
  (`catalog.rs:471`); `QuestPublicRules` (`tuning.rs:231-250`) holds `fort_ms`, `meditate_ms`, `contact_range`, `fort_range`,
  `bargain_cost`, `bargain_stacks`, `route_pace`, `route_slack_ms` and **not** `trade_up_stacks` (`pub(crate)` in `TOP25`).
  See section 9.
- **Death** drops the attempt (`signals.rs:304`); an expired attempt resets for free (the quest can be taken again).

### What RaidBot does today

- `Decide` (`raid/quest/decide.rs`) proposes a pending slot choice (stage 0), a dropped spell (stage 1), The Bargain (stage 2).
  Helpers: `go`, `walk_minutes`, `wait_or_give_up` (a stand with nothing offered: `REFUSED_MS` 1.5 s then `BAN_MS` 30 s),
  `throttled` (`RESEND_MS`), `remember`, `observe` clearing stale clocks, a pending choice's candidate `minutes` = walk +
  `answer_minutes`.
- A **committed** step keeps the goal while its score is positive; only a Sustain goal pre-empts it (`goal.rs`,
  `arbiter.rs`), and only when it ranks first (`leave_value` 3 over `leave_seconds` 6 s = rate 30 a minute, `values.rs`).
  So a committed walk whose rate is above 30 holds the seat through a lost fight: the new stages price their walk (and a
  cash-out at its own minutes) so a leave can win.
- The walker's death term for a route is `death_near(points)` over the living camps within `CAMP_JOIN_RADIUS` of a point
  (`quest/walker.rs:200-222`, `death_chance` with `matchup_share`), floored at `prices.death_floor`; the walker applies it to
  `let_him_cook` only. The percept holds **every** camp of the map (`percept.rs:172`, `:263`), so the bot knows each camp's
  position, zone and living members before it knows the quest's outpost.
- Quest numbers come from `rh_sim::quests::public_rules()` (`quest/mod.rs` `stand_u`, `walker.rs` `timer_spend_minutes`,
  `decide.rs` `bargain_cost`, `bargain_stacks`): P-RAID-TOP25-READ-1. No new hand copy.
- `Prices` (`values.rs`) rows `two_banners` 3.0 and `trade_up` 5.6 (the highest quest row) exist. `elite` is the last field.

## 4. Corrections to the previous brief (made on this base)

- Line numbers moved since `coord/int-84`; section 3 has them on `coord/int-87`.
- "The two branches differ by camp death": **they do not**. Branch 1's route contains branch 0's; both reach the same camp
  token at the same distance; the carry is neither marked nor paid differently. Priced honestly, branch 0 weakly dominates:
  equal reward, shorter walk, equal death at the camp, and the walk back on top for branch 1. The coordinator's tie-break
  ("within the hysteresis it takes branch 0") therefore decides every seed.
- "The outpost's place is in the view": **not before the choice** (`interactions.rs:395-396`); the walk price at the choice is
  an estimate.
- The Race clock is a label, not a rule (section 3).
- Trade Up's 12 / 30 / 54 stacks are not in `public_rules()` (section 9).

## 5. Facts established by measurement

MEASURED_PLACEHOLDER

## 6. Existing tests that pin what this packet changes

None is amended. Preserve all of them:

- `tests/raid_decide.rs`: slot-choice scenes (seeds 5, 4), the pick-up (seed 4), the three Bargain scenes (seeds 2, 4, 6, 9,
  floor 3 of 4, on `bargain_prices()`: every other quest worth 0, so `two_banners` and `trade_up` at 0 propose nothing there),
  the no-improvement scene and the replay: each asserts a Q3 decision and that no command is rejected.
- `tests/raid_decide_fix.rs`: a seat pushed off a pending token quest moves >= 60 u in the last 30 s.
- `quest/decide_tests.rs`, `decide_fix_tests.rs`, `decide_fix2_tests.rs` (58 unit scenes of slot, pick-up, Bargain, waits,
  prices) on `Prices::LOCAL` and hand-built frames: a frame with no `two_banners` / `trade_up` row proposes none of the new goals.
- `tests/raid_quests.rs`, `raid_dwell.rs`, `raid_sustain.rs`, `raid_sustain_fix.rs`, `raid_bot.rs` (`farm_only()`: quest family off).
- `quest/public_rules_tests.rs`: source scans that `decide.rs`, `walker.rs`, `dwell.rs` declare no hand copy of a quest number
  and read `public_rules()` (read it before editing: it pins `decide.rs`).
- `values_tests.rs` pins the `rewards` and `augments` row slices against `data/raid_values.json` (no row moves here).

## 7. Exclusive write scope

- `crates/rh-policy/src/bots/raid/quest/decide.rs`: the new stages, their pure rules and helpers.
- `crates/rh-policy/src/bots/raid/quest/mod.rs`: only a shared helper both stages need (and the `mod` line of an in-crate
  test module the implementer adds; the oracle adds none).
- `crates/rh-policy/src/bots/raid/values.rs`: **append** fields before `elite`, only those the two quests need, each with a
  range test of its own (in a new test file or module of the implementer's, never in `values_tests.rs`'s existing tests); no
  existing row or field changes.
- `crates/rh-policy/src/bots/raid/acts.rs` or `refs.rs`: only if a new act is unavoidable (`Act::QuestInteract { def, action,
  target }` already sends any `(action, target)` the frame offers, so none is expected; the implementer says why if one is).
- Not touched: `quest/walker.rs`, `quest/dwell.rs`, `exec/*`, `arbiter.rs`, `spells.rs`, `goal.rs`, `data/raid_values.json`,
  anything under `crates/rh-sim`, every oracle file, `Cargo.*`.

## 8. Behaviour to preserve

- `Decide` draws nothing from `cx.rng`; a seat with nothing to decide proposes nothing; `Prices::farm_only()` and tables that
  price both quests at 0 propose none of the new goals.
- The existing stages' proposals and acts on the frames of the existing tests.
- No hand copy of a quest number; stand and reach are `stand_u()` and `public_rules().contact_range`.
- The acts sent are only what the frame offers (`QuestInteract` copied from `view.actions`), as the Bargain's trade is.

## 9. Decisions, and what the code contradicts

The coordinator's decisions (2026-11-23), kept:

- **Two Banners: choose by price.** Branch value = reward value times the chance to arrive, less the death term of the walk
  and of the camp at the outpost, over the minutes each branch takes. The choice and both routes are bounded like every
  other decision (a seat can never freeze on it: the choice ends within seconds of the seat standing at the giver). If the
  two branches cost the same within the incumbent's hysteresis it takes branch 0 (the carry is held at once).
- **Trade Up: cash out by price.** After each barter go on only while the next stage's extra stacks, times the chance to
  reach the next trader before the timer, outweigh the death term and the extra minutes; otherwise cash out. Never let the
  timer expire while holding progress: cashing out outranks a leave only when the trader is within reach.
- **No price changes** to existing rows; `values.rs` gains at most the fields these quests need, appended before `elite`.
- **Not here:** Feed or Hunt, Watchtowers, Letters.

What the code says about them:

1. **Branch 1 can never beat branch 0 on price** (section 4). The choice is branch 0 on every seed, unless the price makes
   neither worth walking, in which case the seat does not accept (it never stands undecided: it has not accepted). The
   oracle's "price follows the camp" test is written that way: a table that makes any camp fight deadly makes the seat
   decline the quest; it does not switch to branch 1. If the coordinator wants branch 1 to ever win, the sim must give it a
   reason (a different outpost, a shorter route): out of scope.
2. **The choice is blind**: the outpost is not in the view before the choice, so the death term at the choice is an estimate
   from the camps the percept holds; after the choice the real outpost is in the view and the walk is re-priced from it.
3. **Trade Up's stage stacks (12 / 30 / 54) are not in `public_rules()`** and the reward text does not say them. No new hand
   copy is allowed. **Proposed as a SEPARATE line (not this packet's write scope; anything under `crates/rh-sim` makes it
   cross-crate, `implementer-opus` + `reviewer-opus`):** add `trade_up_stacks: [u64; 3]` to `QuestPublicRules` and
   `public_rules()` (`tuning.rs:231-262`), extending the exhaustive destructuring in `board/public_rules_tests.rs`. Until
   then the implementer chooses and says which: (a) wait for that addition, or (b) price the stages without stack counts:
   `reward_value("trade_up")` split over the stages by a `Prices` field appended before `elite` (`trade_up_stage_share:
   [f64; 3]`, range test: strictly increasing, last <= 1). The oracle pins behaviour, not the numbers, so it holds for either.

## 10. Acceptance checks

- `scripts/gate.sh --quick --base coord/int-87 --oracle packet/P-RAID-Q3b` passes.
- **The quick gate of an rh-policy packet does not run rh-headless tests**: also run
  `cargo test --locked -p rh-headless --test raid_seats` and `cargo test --locked -p rh-policy` (every suite of section 6).
- `python3 scripts/readability_check.py` (no arguments) passes (`decide.rs` is 702 lines; stay under 1 500 or tell the
  coordinator first if a new `quest/*.rs` file is needed, since `quest/mod.rs` then changes).

## Oracle files (read-only for implementers)

ORACLE_FILES_PLACEHOLDER

## 11. Risk and suggested model tier

Sonnet. Hard parts: the choice is blind; a cash-out that outranks a leave only within reach; timers that fit the route on some
seeds only (the oracle sweeps seeds with floors); the stage stacks are not published (section 9.3).

## 12. Known traps

- A trader patrols 72 u at 12 u/s: the cash-out must be sent while the last trader is within contact range (64 u), not after
  a walk to a stale point.
- After the barter at the last trader (progress 3) the quest is achieved and turns in by standing there: nothing to send.
- `Step::committed` holds the seat against everything but a Sustain leave: a committed stand with a high rate holds it through
  a lost fight (fix2's lesson).
- A pending Warhorn choice with both slots full: stage 0 answers it; do not accept with both slots spoken for.
- The observation holds no branch: it is inferred from the objective (the outpost until carried on branch 1; the giver after).

## Notes from previous attempts
<!-- Coordinator appends on escalation. -->
