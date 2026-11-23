# P-RAID-Q3-fix: a RaidBot seat cannot freeze on a quest decision, and the decision prices live in `Prices`

- **Serves:** not in the plan: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot, per-elite
  numbers"; plan of record `docs/backlog/BALANCE_LAB_PATCH.md`; this is the two notes of reviewer-opus on P-RAID-Q3)
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer-opus (bot priority and arbiter pricing; the first note is a seat that stops for the rest of a match)
- **Integration base:** `coord/int-85` @ `c348a5b98` (gate with `--base coord/int-85` until it is merged)
- **D-rules touched:** D1 loose proof (owner direction 2026-11-10; RaidBot has no legacy counterpart) · D2 n/a · D5/D6/D9
  none (no sim, wire or observation change; the bot acts only through `validate` + `Sim::submit`)

## 1. ID and outcome

Two fixes to `RaidBot`'s quest decisions (`quest/decide.rs`, landed by P-RAID-Q3).

1. **No QuestDecide goal can be held without progress beyond a bounded wait.** Today a seat can stand at a point where
   nothing it can do will ever change the board, and the arbiter keeps choosing the goal for the rest of the match. After
   this packet a pending slot choice on a token quest (`letters`, `tuber_eats`, `even_skeletons_get_cold`) walks back to
   the token and the answer lands; and a wait that sees no action offered for `REFUSED_MS` bans that def for `BAN_MS`
   (the pattern `act_bargain` already uses at the giver), at the slot answer and at the hag alike.
2. **The decision prices are `Prices` fields, and The Bargain is valued once.** `values.rs`'s header says no executor
   holds a price of its own; `decide.rs` holds six. The Bargain candidate is valued at the chosen trade's gain, not at
   the table's flat 2.7, so the arbiter no longer crosses the map for a 0.33 trade priced at 2.7.

## 2. Dependencies

`P-RAID-Q3` (merged in `coord/int-85`), `P-RAID-V` (`Prices`). Sibling `P-RAID-C-fix` (same base) is scoped to
`exec/sustain.rs` and a test file of its own and must not touch `values.rs`, `values_tests.rs` or `data/raid_values.json`;
this packet owns those three for this round.

## 3. Relevant code (no legacy: the behaviour source is the Rust board; line numbers verified at `c348a5b98`)

### The freeze (note 1)

- `crates/rh-sim/src/schedule.rs:981-989`: `inputs:players(actionOrder)` runs the player inputs; action 3 at the token sets
  `ready` there. Same tick, `quests::live::system` -> `attempts/interactions.rs:282,341-345` (`tick`): at the endpoint, a
  `ready` attempt gets `pending = Some(reward)`. `interactions.rs:352-366` (`objective_for_owner`): `None` once
  `pending.is_some()`; while `ready` (and not pending) it returns the endpoint for index 0; before ready, for `letters`,
  `tuber_eats` and `even_skeletons_get_cold` the arm `_ if index < 8 && a.bits & (1 << index) == 0 => at()` (`:394`) returns
  the token (`o.tokens[index]`). So a frame never shows `owner_ready` with an objective on these three quests: the
  executor's `remember` (`decide.rs:288-303`), which records the turn-in point from exactly that, never records anything
  for them. What the frame does show, **before** ready, is objective 0 = the token (the accepted, unfinished row).
- `attempts.rs:408-421` (`endpoint`): the token for `Letters | TuberEats | EvenSkeletonsGetCold` (the giver is not the end
  point); `quest_view/board.rs:45-73` offers action 8 only when `attempts::at_turn_in` holds (`choices.rs`, near the
  endpoint within `contact_range`, pending, not blocked, gates open): never at the giver for these three quests.
- `decide.rs:368-375` (`act_slot`): no action 8 offered -> `go` to `self.turn_in[def]`, else `row.giver`. `decide.rs:305-313`
  (`go`): within `STAND_U` it returns `Step::running(.., "standing")` and does nothing, with no clock. `decide.rs:526-537`
  (`propose`): a pending row is a candidate with `value = reward_value(def)`, `minutes = ANSWER_MINUTES` (0.01), `death 0`:
  a rate of about `value / 0.05` minutes (`MIN_MINUTES`), for `letters` 2.0 / 0.05 = 40 camp-equivalents a minute, far
  above every other goal. So a seat at the giver of a pending token quest stands there for the rest of the match.
- **Reachable in ordinary play.** `decide.rs:133-135` (`pickup_wanted`) counts only `held.len() < 2`: it ignores accepted,
  unfinished spell quests, while the walker's `slots_taken` (`walker.rs:125-138`) counts them. With one spell held and
  `letters` accepted (two slots spoken for), a drop lying nearby is picked up (two held) and `letters` then earns the third
  spell. If the seat leaves the token before its answer lands (it dies, is knocked back, or the paced act expires: pending
  survives death, `attempts/signals.rs:300-306`), it walks to the giver and stops.
- The hag stand: `decide.rs:474-483` (`act_bargain`): accepted, the frame offers no trade (`offered` empty), so `go` to the
  hag objective; within `STAND_U` it stands, with no bound, unlike the giver's `REFUSED_MS` (`:432-442`). It holds whenever
  the seat's own count (`possible_trades`, `:242-266`: summary stacks of a stat >= 20, or two weapons) says a trade
  exists while the sim offers none (stacks the sim counts differently, a locked gear slot, a stack room refusal).

### The prices (note 2)

- `decide.rs:25-30,49-50`: `COST_STACKS` 20, `GAIN_STACKS` 24 (`TOP25.bargain_cost`/`bargain_stacks`, crate-private to
  `rh-sim`: sim tuning, mirrored by hand; **stay**), `SPARE_WEAPON_WORTH` 1.5 ("a placeholder, no table row"), `NEUTRAL_STAT`
  2.5, `DEATH_FLOOR` 0.02, `ANSWER_MINUTES` 0.01. `:99-100`: the 0.5 factor for a recharging reusable spell. `:139-166`
  (`stat_worth`): the fit offsets +1 / +0.5 / -0.5 / -1 are a second copy of
  `bots/temporal_percept/modules/choice.rs` `base_fit` (private module), so the two can drift.
- `decide.rs:563,592-599` (`propose`): the Bargain candidate's `value = reward_value("the_bargain")` = 2.7 whatever the
  trade nets, while `bargain_trade` (`:201-239`) separately computes the real gain in camp-equivalents (unit =
  `stacks_per_camp * NEUTRAL_STAT` = 30): neutral-to-neutral 20 -> 24 stacks `(24*2.5 - 20*2.5)/30` = 0.333; damage to
  attack damage for a sword `(24*3.5 - 20*2.5)/30` = 1.133; spare weapon to attack damage for a sword `24*3.5/30 - 1.5` =
  1.3. The gain is thrown away: `propose` only tests `is_none()` (`:570`).
- Because the spare weapon is 1.5, any spare weapon is traded for any stat worth at least 1.875 per stack
  (`24*w/30 > 1.5`), which is every neutral stat (2.5 -> gain 0.5).
- `decide.rs:586-591`: `minutes` adds `straight_minutes(giver, target)`, but while the quest is not accepted `target` *is*
  the giver, so the giver-to-hag leg is never priced. The hag is not in the frame before accepting (objectives show only
  for accepted rows: `quest_view/board.rs:43-49`), so the leg is an estimate or nothing (measured 136 u on seed 2). Minor.
- `decide.rs:34` `SPELL_SLOTS: u8` is duplicated with `quest/walker.rs:52` (`usize`); `quest/mod.rs:19` already holds the
  shared `pub(crate) const SPELL_SLOTS: usize = 2` and `spell_slots_taken`, which `dwell.rs:44` uses. `DEATH_FLOOR` 0.02 is
  also in `walker.rs:64` and `dwell.rs:92`.

## 4. Existing tests that pin what this packet changes (none is amended)

- `quest/decide_tests.rs` (P-RAID-Q3's): `stat_worth(stat, &Me)` with two arguments at `:242-266` (values 3.5, 1.5, 2.5, 2.0,
  3.0 from `Prices::LOCAL`); `bargain_trade(&Prices::LOCAL, ..)` at `:268-330` (sword's spare weapon -> `(7, 6)`; damage
  stacks -> `(5, 6)`; `(7, 3)` for a sword -> `None`; weapon vs stack choice by cost); `spell_worth` 0.5 halving at `:93-106`
  (1.0 for Cleanse at 0 charges); `slot_answer` cases. **None reads a Bargain candidate's value.** Consequence: the new
  price fields must hold today's numbers in `Prices::LOCAL`, and `stat_worth(stat, &Me)` (two arguments) must keep
  compiling and returning the `LOCAL` numbers. The oracle therefore adds `stat_worth_at(prices, stat, &Me)` (the stub), and
  the two-argument `stat_worth` stays as the one-line `stat_worth_at(&Prices::LOCAL, ..)` wrapper.
- `tests/raid_decide.rs` (scenes on the live sim, seeds 2, 4, 6, 9 for the Bargain, floor 3 of 4): the Bargain scenes
  complete when the arbiter picks the Bargain; with the candidate valued at the gain (1.13 to 1.3 for a stack or weapon trade
  on a sword) the pick must still happen. The reference is not measured here (see "Not established"); if a scene goes red
  because the lower value loses to another goal, stop and tell the coordinator rather than amending it.
- `tests/raid_bot.rs:177`: `Prices::farm_only()`, quest family off, a registered `Decide` proposes nothing.
- `tests/raid_dwell.rs:55,273-279` (`DWELL_ONLY`, `HOLD`, `STAY`: `..Prices::farm_only()`), `tests/raid_quests.rs:238-256`
  (`walker_prices`: `..Prices::LOCAL`), `fix_tests.rs:126,415`, `values_tests.rs:198` (`..Prices::LOCAL`): struct-update
  syntax, so new fields with `LOCAL` numbers change nothing for them. `values_tests.rs` pins only the `rewards` and
  `augments` row slices against the JSON (`:143-165`).
- `quest/walker_tests.rs`, `quest/dwell_tests.rs`: `DEATH_FLOOR` is 0.02 in both today and stays 0.02 as a field.

## 5. Facts the brief establishes by measurement

(Filled by the oracle commit: see section 10.)

## 6. Exclusive write scope

- `crates/rh-policy/src/bots/raid/quest/decide.rs`: the whole executor (the bounded waits, `remember`, the price reads, the
  Bargain's value).
- `crates/rh-policy/src/bots/raid/values.rs`: **append** the new `Prices` fields (and `StatFit`) to the struct and `LOCAL`
  (the stub adds them with today's numbers; `farm_only` inherits them through `..Self::LOCAL`). No edit to an existing field.
- `crates/rh-policy/src/bots/raid/quest/walker.rs`: only the `DEATH_FLOOR` const and its uses (`:64,204,215,284`, read
  `cx.prices.death_floor`) and the private `SPELL_SLOTS` (`:52,120,252`, use `super::SPELL_SLOTS`). Optional: if the
  implementer judges it not worth it, leave both and say so; the oracle only scans `decide.rs` for them.
- Not touched: `quest/dwell.rs` (it also holds `DEATH_FLOOR` at `:92`; coordinator's call whether a later packet moves it),
  `data/raid_values.json` (decision below), `values_tests.rs`, `decide_tests.rs`, `tests/raid_decide.rs`, the sim, `Cargo.*`.

## 7. Contracts and references

C-INPUT, C-OBS, C-POLICY; `crates/rh-policy/AGENTS.md`; `docs/agent/READABILITY.md` (`//!` header, 1 500 lines);
`rh-policy` `clippy.toml`; the extension guide in `crates/rh-policy/src/bots/raid/mod.rs`; `values.rs` header ("no executor
or arbiter file holds a price of its own").

## 8. Behaviour to preserve

- `decide` draws nothing from `cx.rng` and `Prices::farm_only()`, `DWELL_ONLY`, `walker_prices()` propose exactly what they
  propose today: the Bargain candidate is still gated by `reward_value("the_bargain") > 0` (a table that prices the quest at
  zero turns it off), and a pending slot answer by `reward_value(def) > 0`. The gain replaces only the candidate's `value`.
- A seat with nothing to decide proposes nothing and so moves no draw.
- The mechanics that stay local: the action codes, `BARGAIN_TO_HUD`, the 0.5 share of `lost_brother`, `STAND_U`, `RESEND_MS`,
  `BAN_MS`, `REFUSED_MS`, `COST_STACKS` 20 and `GAIN_STACKS` 24 (a later cross-crate packet reads them from the sim).
- The slot choice still answers at once when action 8 is offered (no ban while the sim is answering), and a seat walking to a
  far point is not banned for being slow: a ban needs the seat standing within `STAND_U` with nothing offered.

## 9. Decisions

- **The decision prices join `Prices`, not the JSON mirror.** `data/raid_values.json` mirrors rows (a def or card id and its
  workbook value) so the lab can replace estimates by a table; `values_tests.rs` pins exactly those. The decision prices are
  scalar tuning with no workbook row (the code itself calls the spare weapon "a placeholder, no table row"), like
  `SustainPrices` and `DeathModel`, which are Rust-only too. So the JSON is untouched, and so are `values_tests.rs`'s
  existing tests. The new fields are pinned by `LOCAL`-number equality in the oracle (they hold today's numbers) and by doctored
  `Prices` changing `bargain_trade`, `stat_worth_at` and the candidate.
- **The fit offsets are a `StatFit` struct** (`matched` 1.0, `fists` 0.5, `wand_attack` -0.5, `mismatched` -1.0), not
  four loose fields. The ability-power rule "0 for a Fire, Sentenced or Plague fighter" stays code: it is which fighters, not a price.
- **Field names (the oracle compiles against them; the stub adds them):** `Prices::spare_weapon` (1.5), `neutral_stat` (2.5),
  `stat_fit: StatFit`, `death_floor` (0.02), `answer_minutes` (0.01), `recharging_share` (0.5).
- **The Bargain candidate's value** is the gain of `bargain_trade(prices, me, &possible_trades(cx))`'s chosen trade (the gain
  `bargain_trade` already computes; expose it, e.g. `bargain_gain`), in camp-equivalents, gated by `reward_value(BARGAIN) > 0`.

## 10. Acceptance checks

- `scripts/gate.sh --quick --base coord/int-85 --oracle packet/P-RAID-Q3-fix` passes.
- **The quick gate of an rh-policy packet does not run rh-headless tests**: run, for the implementer and the reviewer,
  `cargo test --locked -p rh-headless --test raid_seats` (and `cargo test --locked -p rh-policy --test raid_decide`, the
  Bargain scenes of seeds 2, 4, 6, 9 with a floor of 3).
- `python3 scripts/readability_check.py` passes (no arguments).

## Oracle files (read-only for implementers)

(Completed by the oracle commit.)

## 11. Risk and suggested model tier

Sonnet: one executor file plus appended price fields. The hard parts: remembering the token from the *accepted* frame (a
fresh `Decide` that never saw it has only the giver), a ban that does not fire while the answer is being sent, and a
Bargain value that stays above the roam floor so the scenes of `raid_decide.rs` still complete.

## 12. Known traps

- A pending row has no objective at all (`objective_for_owner` is `None`): the only token knowledge is the accepted frame
  before ready, kept per def. A seat handed a pending token quest (a bot restarted, or a fresh `Decide`) knows no token: it
  can only wait at the giver and then give up (the bounded wait), never loop.
- `owner_ready` and `pending` are different frames; `ready` can be seen for a non-token quest (objective = endpoint).
- The bans are per def and expire (`BAN_MS`): the choice stays pending in the sim, so after the ban the goal may return, which
  is intended (it must not hold the seat in between).
- The hag's objective is the contact, 136 u from the giver on seed 2; `go` stands within `STAND_U` = 32.

## Not established

Filled by the oracle commit.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
