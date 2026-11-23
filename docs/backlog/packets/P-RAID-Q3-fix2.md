# P-RAID-Q3-fix2: a pending slot choice no longer outranks Sustain's leave, a pick-up stand is bounded, The Bargain's leg is a price

- **Serves:** not in the plan: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot, per-elite
  numbers"; plan of record `docs/backlog/BALANCE_LAB_PATCH.md`; the notes two reviews left on `RaidBot`'s quest-decision
  executor after it met the sustain executor in one tree)
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer-opus (bot priority and arbiter pricing)
- **Integration base:** `coord/int-85` @ `1bd9cc1dd` (gate with `--base coord/int-85`)
- **D-rules touched:** D1 loose proof (RaidBot has no legacy counterpart) · D2 n/a · D5/D6/D9 none (no sim, wire or
  observation change; the bot acts only through `validate` + `Sim::submit`)

## 1. ID and outcome

Fixes to `quest/decide.rs` (`Decide`) and one line of `quest/walker.rs`, in four parts:

- **A.** A pending slot choice with no answer offered is priced by the walk to its turn-in point plus `answer_minutes`, so
  a lost camp fight on the way lets Sustain's leave rank first.
- **B.** The pick-up stand beside a wanted drop that the sim never offers (action 9) is bounded and banned like the slot
  answer and the hag stand, and the walker is not held back by such a drop.
- **C.** The Bargain's giver-to-hag leg is a `Prices` field, not a literal; the docs say the table row is a switch.
- **D.** `stood` and `refused` are cleared when another goal has the seat.

## 2. Dependencies

`P-RAID-Q3`, `P-RAID-Q3-fix`, `P-RAID-C`, `P-RAID-C-fix` (all in `coord/int-85`). `values.rs` merges one packet at a time:
P-RAID-E appends `elite: ElitePrices` as the LAST field of `Prices`; this packet's stub appends `bargain_leg_minutes` after
`recharging_share`; the coordinator resolves the append point at merge.

## 3. Relevant code (no legacy: the behaviour source is the Rust board; verified on `coord/int-85`)

- **A.** `decide.rs:570-581` (`propose`): the pending slot candidate has `minutes: cx.prices.answer_minutes` (0.01, below
  `MIN_MINUTES` 0.05, `arbiter.rs:15`) at any distance, `death 0`; its rate is `value / 0.05`: `letters` 40, `hold_the_fort` 72,
  `the_burning_crown` 104 (table rows in `values.rs`). Sustain's leave (`exec/sustain.rs:275-283`, `values.rs:93-94`
  `leave_value` 3, `leave_seconds` 6 = 0.1 min) is rate 30, and pre-empts a commitment only when it ranks first
  (`arbiter.rs:113-123`, `goal.rs:60`). `decide.rs:385-393` (`act_slot`): with no action 8 offered, `go` to the turn-in point
  (remembered token, else the giver) returns `Step::committed`, so the walk is kept through a lost fight; a pending choice
  survives death (`attempts/signals.rs:300-306`).
- **B.** `decide.rs:428-449` (`act_pickup`): within `STAND_U` (32 u) of the wanted drop with no action 9 for `drop.index`,
  `go` returns `Step::running("standing")` with no clock and no ban. The sim offers action 9 only when the seat is standing,
  holds fewer than 2 spells and `pickup_reach` holds (`crates/rh-sim/src/quest_view/board.rs:158-170`;
  `crates/rh-sim/src/item_view.rs:124-131`: range, then a `MASK_MOVE` ray on the body map with held walls). `walker.rs:115-121`
  (`drop_near`): any drop within 160 u while a slot is free makes the walker propose nothing (`walker.rs:325,349`).
  `wait_or_give_up` (`decide.rs:412-426`) is the bounded wait (`REFUSED_MS` 1.5 s, then `BAN_MS` 30 s) and `propose`
  (`:583-605`) does not look at `banned` for the pick-up.
- **C.** `decide.rs:607-642`: `reward_value(BARGAIN)` only gates; the value is `bargain_gain`. `:633` adds `straight_minutes(giver,
  target) + 0.5`; before the accept `target` is the giver, so the first term is 0 and the whole leg is the literal 0.5.
  `values.rs:276-284`: the doc of `reward_value` says every quest executor multiplies its value by it.
- **D.** `stood` (`:57`) and `refused` (`:55`) survive another goal's turn: back within 32 u after a while, `now - since >=
  REFUSED_MS` holds at once. Token quests with a pending choice and no remembered token: `decide.rs:386-390` walks to the
  giver, `endpoint` is never the giver (`attempts.rs:408-421`).

## 4. Existing tests that pin what this packet changes

None is amended. Preserve all of them:

- `quest/decide_fix_tests.rs`: `a_pending_choice_is_not_given_up_while_the_server_offers_the_answer` (answer offered, resends);
  `a_seat_walking_to_a_far_turn_in_point_is_not_given_up_for_being_slow` (fresh `Decide`, pending `letters`, giver 200 u+ away,
  no token known: the goal must be proposed for 20 s. **This forbids D's second half**, see "Decided: D, token skip");
  `a_pending_choice_on_a_token_quest_walks_back_to_the_token_not_the_giver`; `the_answer_minutes_and_the_death_floor_are_read_from_the_prices`
  (seat AT the giver, pending, nothing offered: `answer.minutes == 0.02` exactly: so the walk is priced 0 within `STAND_U`, or
  at zero distance, and `answer_minutes` is added to it); the Bargain value tests (`bargain_value`: 34/30, 1.3, 0.3, table 0).
- `quest/decide_tests.rs`, `tests/raid_decide.rs` (`bargain_prices` table; Bargain scenes), `tests/raid_decide_fix.rs`
  (live seat pushed off a pending token quest, must move >= 60 u in the last 30 s), `tests/raid_sustain.rs`,
  `raid_sustain_fix.rs`, `quest/walker_tests.rs`, `tests/raid_quests.rs`, `tests/raid_dwell.rs`, `tests/raid_bot.rs`
  (`farm_only`: quest family off).
- `values.rs` header: "no executor or arbiter file holds a price of its own".

## 5. Facts established by measurement

Measured on the base (the oracle's tests, on the stub):

1. A: on `coord/int-85` a pending `letters` choice (token remembered 200 u+ away) is proposed with `minutes` 0.01 whatever the
   distance, and the arbiter keeps the walk against a Sustain leave candidate of value 3 over 0.1 minute (the hand-built
   stand-in for a lost fight; no live fight scene was built, see "Not established").
2. B: a seat within 32 u of a wanted drop with no action 9 stays `running` and proposed for the whole run (the test runs
   15 s); and the walker proposes a fresh quest in no frame with that drop within 160 u.
3. C: `Prices::bargain_leg_minutes` does not move the candidate on the stub (it is unread); the literal 0.5 does.
4. D: `stood`/`refused` survive: a seat that waited 1.2 s, was away (observe and propose only) for 5 s and is back gives up on its
   first tick.

## 6. Exclusive write scope

- `crates/rh-policy/src/bots/raid/quest/decide.rs`
- `crates/rh-policy/src/bots/raid/values.rs`: the one appended field `bargain_leg_minutes` (stub) and the doc edits of
  `reward_value` and of the `the_bargain` row. No other edit.
- `crates/rh-policy/src/bots/raid/quest/walker.rs`: the body of `drop_near` (`:115-121`) and its doc, nothing else. No new
  shared item in `quest/mod.rs` is needed (see B decision).
- Not touched: `exec/sustain.rs`, `exec/elite.rs`, `exec/farm.rs`, `exec/mod.rs`, existing tests, `data/raid_values.json`,
  `Cargo.*`, the sim.

## 7. Contracts and references

C-INPUT, C-OBS, C-POLICY; `crates/rh-policy/AGENTS.md`; `docs/agent/READABILITY.md` (`//!` header, 1 500 lines);
`values.rs` header; `docs/OWNER_DECISIONS.md` row above.

## 8. Behaviour to preserve

- No draw is added or moved: `Decide` draws nothing from `cx.rng`; a seat with nothing to decide proposes nothing.
- `Prices::farm_only()` and the test tables propose what they propose today (a pending answer and the Bargain are still
  gated by `reward_value(..) > 0`; the quest family is off under `farm_only`).
- The answer offered: `answer_minutes` alone, no walk, no ban. A seat far from its turn-in point is never banned for being slow.
- The Bargain's candidate stays the chosen trade's gain (no cap).

## 9. Decisions

- **A: the minutes.** `minutes = walk_minutes(seat, turn-in point) + answer_minutes` when no action 8 is offered and the seat
  is farther than `STAND_U` from the point; `answer_minutes` alone within `STAND_U` or while offered (the existing test pins
  0.02 exactly at the giver). Reason: with the walk priced, `letters` (value 2.0) falls under rate 30 at 0.067 minutes (a
  walk of about 4 s); the 72- and 104-valued rewards stay above 30 for walks up to 2.4 and 3.5 minutes, which is a decision about
  how much a reward is worth, not a defect. The oracle pins `letters` with the arbiter and a hand-built leave.
- **B: the walker.** The walker cannot read `Decide`'s ban (separate executors, no shared state in `Cx`, and a static is banned by
  clippy). A shared field would be larger than the problem, so the oracle pins a stateless rule inside `drop_near`: a drop
  within `STAND_U` (32 u) of the seat for which the frame offers no action 9 (`drop.index`) does not hold the walker. While
  Decide is still walking (beyond 32 u) or the pick-up is offered, the drop holds it as today. The 1.5 s before the ban also
  frees the walker; that is harmless (the arbiter ranks the candidates). If the implementer finds the rule wrong, they
  stop and report; they do not add a field without the coordinator.
- **B: the bound.** `act_pickup` uses `wait_or_give_up(def, ...)` for the standing case (no action 9 for the wanted drop) and
  `propose` skips a banned def for the pick-up: given up within 5 s, off for at least 10 s.
- **C: the switch.** The row stays a switch, documented, not a cap `gain.min(reward_value(BARGAIN))`: the owner decision in
  `P-RAID-Q3-fix` ("Decided on the implementer's stop") keeps the candidate at the gain, because the table's 2.7 is the
  reward gross of its 20-stack cost; a cap would bring it back as a ceiling. The oracle pins both: a table row of 0.01 still
  yields the full gain, and the docs of `values.rs` say "switch".
- **C: the leg.** `Prices::bargain_leg_minutes` (0.5) replaces `+ straight_minutes(giver, target) + 0.5`: not yet accepted,
  `minutes = walk(giver) + bargain_leg_minutes`; accepted, no leg. `decide.rs` holds no `+ 0.5`.
- **D, the clear:** `stood` and `refused` are cleared when `cx.goal` is not a `QuestDecide` goal at `observe` (or `start`; the
  oracle calls `observe` and `propose` under a Roam goal).
- **D, token skip: not in this packet.** A token quest's pending choice with no remembered token is not skipped:
  `a_seat_walking_to_a_far_turn_in_point_is_not_given_up_for_being_slow` pins today's proposal of that candidate, and
  an existing test is not edited here. Coordinator's call (default in force: leave as is; the bounded wait already ends it).

## 10. Acceptance checks

- `scripts/gate.sh --quick --base coord/int-85 --oracle packet/P-RAID-Q3-fix2` passes.
- **The quick gate of an rh-policy packet does not run rh-headless tests**: also run
  `cargo test --locked -p rh-headless --test raid_seats`, and `cargo test --locked -p rh-policy` (all suites: raid_decide,
  raid_decide_fix, raid_sustain, raid_sustain_fix, raid_quests, raid_dwell).
- `python3 scripts/readability_check.py` (no arguments) passes.

## Oracle files (read-only for implementers)

- `crates/rh-policy/src/bots/raid/quest/decide_fix2_tests.rs` (in-crate; its `mod` line is in the stub commit)
- `docs/backlog/packets/P-RAID-Q3-fix2.md`

Stub commit: `values.rs` (`bargain_leg_minutes: 0.5`, unread), `quest/mod.rs` (the `mod` line).
Green on the stub by design (6 guards, all named `guard_...`): the offered answer costs `answer_minutes`; the offered pick-up is
not banned; the walker proposes with no drop; the walker stays held by an offered or a distant drop; an accepted Bargain has no
leg; the table row is a switch, not a cap. The other 10 tests are red on an assertion (measured: a letters choice 689 u away
has minutes 0.01; `hold_the_fort` 0.636 min of walk unpriced; the arbiter keeps `QuestDecide` over the Sustain leave; the pick-up
stand is never given up; stale clocks give up at the first tick back; a leg of 2 minutes moves nothing; `values.rs` has no
"switch"; `decide.rs` has `+ 0.5`).

## 11. Risk and suggested model tier

Sonnet. Traps: the existing test at the giver (`answer.minutes == 0.02` exactly) forbids pricing a walk when standing; the
Sustain leave must be ranked over a committed incumbent; the new pick-up ban must not fire while action 9 is offered.

## 12. Known traps

- `act_slot` clears `stood` when walking (`go` far), so the stale-`stood` test drives `observe`/`propose` alone for the
  away period, as an arbiter that gave the seat to another goal does.
- The leave's score and the pending choice's score both carry the tier and persona multipliers; the oracle uses
  `TierId::Normal` and `PersonaId::Farmer`.
- `drop_near` is also read by `walker.act` (`walker.rs:349`).

## Not established

- No live scene shows a seat leaving a lost fight for a pending choice: the death model is represented by a hand-built Sustain
  candidate (value 3, 0.1 minute: `leave_value`, `leave_seconds`), not produced by `Sustain`. `raid_seats` and the
  suites above are the implementer's regression net.
- That a real blocked drop (`MASK_MOVE` ray) is reproduced: the frames omit action 9; the sim's refusal is as cited.
- The rate of a 72- or 104-valued choice still outranks the leave for any walk under 2.4 and 3.5 minutes: by decision.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
