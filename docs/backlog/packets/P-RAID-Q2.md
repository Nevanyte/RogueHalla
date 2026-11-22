# P-RAID-Q2: RaidBot dwell quests (Hold the Fort, Stay A While)

- **Serves:** not in the plan: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot,
  per-elite numbers"; plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 1, packet P-RAID-Q2)
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `coord/int-84` @ `52ec36ccd` (the gate takes `--base coord/int-84` until it is merged)
- **D-rules touched:** D1 loose proof (owner direction 2026-11-10; RaidBot has no legacy counterpart, the oracle is a
  behaviour test on the live content world) · D2 n/a · D5/D6/D9 none (no sim, wire or observation change; the bot acts
  only through `validate` + `Sim::submit`)

## 1. ID and outcome

`RaidBot` does the two dwell quests of the Top 25 board as `GoalKind::QuestDwell`, in `crates/rh-policy/src/bots/raid/quest/dwell.rs`
(a stub today, registered, proposing nothing): **Hold the Fort** (`hold_the_fort`: stand in a Gallows guard house for 45 s
with nobody else in it, then walk back to the giver) and **Stay A While** (`stay_a_while`: send "Meditate" at the monk, who
is the giver, and then neither move nor attack for 30 s). When it lands, a `RaidBot` seated as a human on the live
content world, started a short walk from the giver, accepts the dwell quest, completes it on a floor of seeds, sends no
rejected command, replays exactly, and leaves a guard house that someone else holds.

## 2. Dependencies

P-RAID-Q1 (walker, in `coord/int-84`), P-RAID-V (`Prices::reward_value`: rows `hold_the_fort` 3.6 and `stay_a_while` 2.0
exist in `values.rs`). Not in this packet: decisions, kill, PvP and search quests, and fighting a player who contests the
fort.

## 3. Relevant legacy paths

None. The behaviour source is the Rust board (paths in `crates/rh-sim/src`, verified on the base):

- Numbers: `quests/board/tuning.rs:112-118` (`TOP25.fort_ms` 45 000, `meditate_ms` 30 000, `fort_range` 72,
  `contact_range` = `QUEST_TURN_IN_RANGE` 64); defs `quests/board/catalog.rs:211-222` (`hold_the_fort`) and `:453-464`
  (`stay_a_while`); both have `route_timer_ms: None` and `objective_count` 1 (clock `Dwell`).
- Logic: `quests/board/attempts/interactions.rs:245-252` (action 6), `:256-272` (`inside_fort`), `:296-333` (`tick`: hold and
  meditation), `:334-355` (turn-in); `attempts/choices.rs:86-89` (action 6 offered), `attempts/signals.rs:182-203`
  (`enter_lock`), `:241-253` (`on_hit_landed`); `attempts.rs:222-231` (`progress`), `:293-312` (`accept`), `:410-434`
  (`public_markers`), `:460-475` (`system`: death, deadline), `quests/board/placement.rs:552`; `quest_view/board.rs:200-225`
  (public marks), `rh-policy/src/delivery/collections/field_projection.rs:77-90` (marks to `WireField`).
- Bot side: `rh-policy/src/bots/raid/quest/walker.rs` (the pattern; `slots_taken` `:127`, `SPELL_SLOTS` `:52`, `committed`
  `:401`), `goal.rs`, `exec/mod.rs`, the extension guide in `raid/mod.rs`.

## Established facts (verified in code on the base; seeds measured by a throwaway probe, not committed)

### F1. The rules

**Common.** Accept as every board quest (`QuestAccept { handle }`, row `can_accept`, 64 u, see P-RAID-Q1 F1). **Neither quest
has a timer**: `expires_at` was 0 after accepting on every seed (`attempts.rs:302-305`, route timer `None`), so nothing expires; an
attempt ends only by turn-in, abandon (action 1, always offered), the owner not standing (**death or a downed seat removes
the attempt**: `attempts.rs:465-468`, `owner_allowed`), or the gates lock (below). Rewards are spells (Turret, Stasis:
`catalog.rs:217,459`; `RewardKind` not `Permanent`), two slots, a third sticks in `pending` (action 8, Q3's).

**The row shows the dwell clock.** `owner_progress` is **whole elapsed seconds of dwell** for both quests (`attempts.rs:226-230`:
`(dwell_ms / 1000) as u8`), 0..45 and 0..30; `owner_ready` flips at the full time. **This contradicts the outline ("row shows
owner_progress and owner_ready only, so keep your own clock"): the server's own clock is readable, and it is the truth**
(it pauses, resets and counts as the server does). The bot needs no clock of its own. What the row does not show: whether
a meditation is under way, who contests, what broke it.

**Hold the Fort.**
- Dwell point: the `guardHouse` map feature of the Gallows zone (`placement.rs:552`), token 0 of the offer. **Unknown before
  accepting** (objectives appear only after); after accepting the view's objective is the token. Measured: **(4656, 2256) on
  every seed that deals it**; the giver is at (4176,2000) or (4208,2096), so **475.7 u (giver (4208,2096)) or 544 u
  (giver (4176,2000))** from the giver to the fort. Action list at the fort: abandon only (no hold action; standing does it).
- Progress: each tick, if the owner is inside the house (`inside_fort`: the guardHouse bbox that holds the token also holds
  the owner's tile; with no map layers, within 72 u) **and no other standing player is inside it** (`standing()`: alive and
  not downed; any player, human or bot, quest or not, friend or foe; mobs do not count) then `dwell_ms += 50`
  (`interactions.rs:299-312`). At 45 000 ms the attempt is ready. **Contested = paused, not reset** (neither branch runs
  while inside and contested). **Leaving the house resets to 0** (`:314-316`), also while contested.
- Turn-in is **at the giver** (`endpoint` is `o.at` for this quest, `attempts.rs:384-395`): once ready the view's single objective
  is the giver; within 64 u with a line the tick sets `pending`, pays, finishes (`interactions.rs:334-355`). The bot walks
  475-544 u back (about 3 s).
- Public: while `dwell_ms > 0` and not ready, **every seat sees a `WireField` of kind `FIELD_KIND_QUEST_FORT` (18) at the
  holder's position** (`attempts.rs:431-433`, `quest_view/board.rs:216`; own mark included: filter it by distance to self).

**Stay A While.**
- Dwell point: **the monk is the giver** (`endpoint` falls through to `o.at`; `objective_for_owner` index 0 is `o.at`:
  `interactions.rs:401`; measured objective = giver on every seed). Givers (2832,1232) or (2864,1136).
- Start: action **6** target 0 ("Meditate"), offered by `choices.rs:86-88` only while the attempt is accepted, not ready, **not already
  meditating**, within 64 u with a line of sight, gates open and the seat not status-blocked. The server accepts it only
  under the same conditions (`interactions.rs:245`), sets `meditation = Some(exact position)` and `dwell_ms = 0`. A refused
  send is silent (no error) and costs an order token.
- It breaks (`meditation = None`, `dwell_ms = 0`, `interactions.rs:318-326`) on any tick the seat is **farther than 64 u of the monk
  or without a line, or its position is not exactly the position recorded at the send (any move, knockback or push),
  or `action_blocked` (a status), or `weapon.action.is_some() || weapon.held` (an attack, a charge, a held button)**; and
  when it takes damage `> 0` (`signals.rs:241-253`, any source, mob or player), and on the coliseum lock (`signals.rs:182-203`).
  **It must be re-sent after a break** (action 6 is offered again exactly when not meditating, so "offered" is the
  signal; the row's progress falling back to 0 is the other one). Other players cannot interrupt it except by damage; standing
  beside the monk does nothing.
- At 30 000 ms the attempt is ready and, in the same tick, `at_end` (the monk) turns it in: **no walk back**
  (`interactions.rs:334-353`), unless the giver is hidden from this owner (a `a_cruel_joke` scare, 60 s: `visible_giver`).
- Public: while meditating (`meditation.is_some()`, not ready) a `FIELD_KIND_QUEST_MEDITATION` mark at the seat's position.

**Both:** any coliseum lock (`gates_locked`) offers no action and freezes the clock; a meditation is cleared by it.

### F2. What the frame gives a seat (`bots/raid/percept.rs`; nothing is missing for the executor)

| Stage | Seat sees | Does not see |
|---|---|---|
| Before accept | row (`def`, `giver`, `can_accept`, `state` 1), `percept.quests` (None for a bot seat) | the fort's position (hold); the quest has no timer |
| Accepted, walking | row `owner_accepted`, `owner_progress`, objective (the fort token; the giver), actions (hold: abandon only) | whether the fort is occupied (unless it sees the player: `percept.players` lists players in sight with `at`, `alive`; and fort marks in `percept.fields` kind 18 for holders) |
| At the monk | action 6 offered (not meditating) / absent (meditating or out of range) | the exact 64 u rule beyond its own distance, status blocks |
| Dwelling | `owner_progress` seconds rising; `owner_ready` | the contestant when out of sight; the guard house bbox (the seat knows only the token: use a radius of about `fort_range` 72 u as the proxy); damage is visible through hp and `percept.me`, `hit_at` |
| Done | the row vanishes (completed rows are removed: `quest_view/board.rs:17-23`); the spell arrives in `quests.spells` | |

**A fair seat cannot know** whether a player it cannot see stands in the fort, or will arrive; the only truth is the clock: **progress
flat while the seat stands inside means contested** (or the seat is outside the bbox).

### F3. Deals (measured on `seed1.map.json`, seeds 1-24, board admitted with `Secrets::from_seed(seed)`)

- `hold_the_fort` is dealt on seeds 2, 3, 4, 7, 8, 9, 12, 13, 14, 19, 24 (giver (4176,2000) on 2, 9, 12, 14, 19, 24;
  (4208,2096) on 3, 4, 7, 8, 13).
- `stay_a_while` on seeds 2, 5, 13, 14, 19, 21, 22, 23, 24 (giver (2832,1232) except (2864,1136) on 22, 24).
- Both on 2, 13, 14, 19, 24. The oracle's sets: hold 2, 3, 4, 9, 12 (both giver variants); stay 2, 5, 13, 22, 24 (both variants).
- Giver to dwell point: hold 475.7 / 544 u, stay 0 u. Start for the scenes: 160 u from the giver on a straight floor line
  (as P-RAID-Q1).

### F4. The control (green today)

`a_scripted_dwell_completes_each_scene`: no policy; every 5 ticks it accepts at the giver, sends action 6 when offered, and else
walks to the first objective (and sends nothing once within 40 u). It **completes both quests on 5 of 5 seeds each, within
120 s**, and the row's progress reaches 44 (hold) and 29 (stay) whole seconds before the turn-in (the detector sees both
the clock and the completion). `a_second_player_in_the_guard_house_stops_the_scripted_hold` (seeds 3, 9): a second human
standing at the fort from tick 0 keeps the scripted hold from ever completing and stays standing (no mob kills it).

### F5. Debug build time

With the stub: `cargo test -p rh-policy --test raid_dwell` takes about 6 s (7 tests, two minutes of sim each is cheap). With the
executor, expect 10-20 s. Not release-only: `scripts/gate.sh` needs no edit.

### F6. Existing tests that pin what this packet touches

- `crates/rh-policy/tests/raid_quests.rs` (P-RAID-Q1 oracle): `raid_bot_accepts_and_completes_walker_quests_before_their_timers`
  asserts every quest RaidBot accepts is on the walker list and floors of at least 6 completions, one on 5 of 6 seeds (seeds 1-6,
  default prices). Seeds 2-5 deal a dwell quest, so dwell candidates could compete there. **Resolved by the coordinator's
  amendment:** that file's bot now uses a walker-only price table (`walker_prices()`: the eleven walker defs at 1.0, everything
  else 0), which reproduces the conditions the floors were measured under, so no later quest executor can move that oracle.
  On the stub: `cargo test -p rh-policy --test raid_quests` 6 of 6 green (about 28 s on a loaded machine).
- `tests/raid_bot.rs`: bot built with `Prices::farm_only()`: dwell proposes nothing worth taking there: stays green.
- `raid/fix_tests.rs`, `review_tests.rs` (`exec::registry()` with an `Alarm`), `values_tests.rs`, `quest/walker_tests.rs`: unit
  tests with `Prices::LOCAL` and scenes without a quest view: green iff the dwell executor proposes nothing when
  `p.quests` is `None`, draws nothing from `cx.rng`, and answers `urgent` false.
- **The replay of the walker oracle:** one more executor adds one `propose` call per strategy tick. It cannot move a walker run
  by itself (no draw, no intent), but it **adds candidates**, so the arbiter's choice can change on seeds that deal a dwell quest
  (above); on seeds that deal none (1, 6) the runs must be bit-identical: the implementer compares
  `raid_bot_replays_a_quest_run_exactly` (seed 1) and the final state hash of seed 1 and 6 before and after.

## 4. New paths

- `crates/rh-policy/tests/raid_dwell.rs` (oracle).

## 5. Exclusive write scope

- `crates/rh-policy/src/bots/raid/quest/dwell.rs` (replaces the stub).
- `crates/rh-policy/src/bots/raid/quest/mod.rs`: a small shared helper only (below).
- ONE line in `exec/mod.rs` `registry()` (a shared file: merges one at a time): **already in the `stub:` commit**, after the walker's
  and before Roam's; the existing lines do not move. The implementer does not touch the file.
- A test file of its own if it wants one (`crates/rh-policy/src/bots/raid/quest/dwell_tests.rs` with `#[cfg(test)] mod` in `dwell.rs`
  is fine, as `walker_tests.rs`).
- Nothing else: not `walker.rs`, `bot.rs`, `arbiter.rs`, `goal.rs`, `acts.rs`, `percept.rs`, `refs.rs`, `values.rs`.

## 6. Contracts and references

C-INPUT, C-OBS, C-POLICY; `crates/rh-policy/AGENTS.md`; `docs/agent/READABILITY.md`; `rh-policy` `clippy.toml`; the extension
guide in `raid/mod.rs`.

## 7. Behaviour to preserve

- The tests of F6. The walker oracle is isolated by its price table (`walker_prices()`), so the dwell executor cannot move it;
  the implementer still compares seed 1's final state hash and `raid_bot_replays_a_quest_run_exactly` before and after, and
  **stops and reports if `raid_quests` goes red for any reason**.
- No draw from `cx.rng`; no intent or act unless the goal is a `QuestDwell`; `urgent` stays false.
- `Target::Quest { def, stage }` names the quest by def id; positions never by a key kept across frames.

## 8. Executor design (what the oracle does not see; the reviewer checks it)

- **Candidates** (`GoalKind::QuestDwell`, `Target::Quest { def, stage: 0 }`): for each row of `hold_the_fort` / `stay_a_while` with
  `state == 1` and `cx.p.quests` present, active, gates open. Value = `cx.prices.reward_value(def)` (0 turns the family off). Minutes =
  the walk (giver, then fort token once known, else the measured 500 u straight, then the giver for the hold) plus the dwell (45 or 30 s
  less the row's `owner_progress`). Success from what can interrupt it: hold, other players in sight (in the fort's radius or
  with a fort mark of kind 18 away from self) cut it; meditation, any other player in sight within weapon range of the monk
  cuts it (it can interrupt only by damage), scaled down with their count. Death as the walker prices it: floor 0.02 (the walker's
  `DEATH_FLOOR`) plus the camps within `CAMP_JOIN_RADIUS` of the dwell point via `farm::death_chance` (the walker's `death_near` is a
  private method; copy the few lines, do not edit `walker.rs`). Candidate gone when the dwell cannot finish.
- **Slots.** `slots_taken(cx)` (`walker.rs:127`) counts `quests.spells.len()` plus every row with `owner_accepted && state == 1`
  whose `definition(def).reward.kind() != Permanent`; before accepting, a spell-reward quest is not proposed when it is `>= 2`
  (`SPELL_SLOTS`, `walker.rs:250`). The count is of **every accepted, unfinished row on the board, whoever's executor owns it**, so the
  walker's accepted quests are in it already. The dwell executor puts the same function in `quest/mod.rs` as
  `pub(crate) fn spell_slots_taken(cx: &Cx<'_>) -> usize` and `pub(crate) const SPELL_SLOTS: usize = 2` (copied verbatim from
  `walker.rs:127-139`), and calls it. `walker.rs` is not edited: it keeps its private copy, and a later packet may switch it to the
  shared one (the two must agree; the coordinator may add a one-line test then). No line moves out of `walker.rs`.
- **Stages** (read from the board each think, as the walker does): not accepted: walk to the giver (STAND 32 u) and send
  `Act::QuestAccept { def }` (throttled as the walker). Accepted hold: walk to the token, stand within about 32 u of it (the control
  stands within 40 u and completes 5 of 5); once `owner_ready`, walk to the giver (the single objective). Accepted meditation: walk to
  the monk, **stop** (`cx.still_since`; no intent after the send), then send `Act::QuestInteract { def, action: 6, target: 0 }` only
  when this frame offers it; do not send a move intent in the think that queues it nor afterwards while the progress rises; resend
  only when the frame offers action 6 again. The act queue paces sends 180-420 ms with a 4 s TTL (a stale send is dropped).
- **Committed.** Accepted and running: `Step::committed` (only a `Sustain` goal may pre-empt, as the walker). Released
  (`Step::failed`, banned for a while so it does not re-accept at once) when it can no longer finish.
- **When to leave (the rule, because the bot cannot see the contestant):** while inside the fort (or at the monk), progress not
  rising for 6 s (hold: contested, or standing outside the bbox: step toward the token once, then leave) releases the goal and bans the
  quest for 60 s; or a player in sight within the fort radius for 3 s with no progress; or `Sustain`-level hp loss. For the meditation:
  two breaks (progress back to 0 from 3 s or more, or damage taken) ban it for 60 s. Never attack to win the fort (not this packet).
- It never abandons (action 1): an abandoned attempt gains nothing, a left one costs nothing and the hold resets on leaving
  anyway.
- Acts go through the queue; no `PublicCommand` is built in the executor.

## 9. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-RAID-Q2 --base coord/int-84` passes.
- `cargo test -p rh-policy --test raid_dwell` passes in a debug build (it is red on the stub: four tests, each on an assertion
  "accepted nothing").
- `raid_bot`, `raid_quests` (with the price-table amendment), the unit tests of F6 stay green.

## Oracle files (read-only for implementers)

- `crates/rh-policy/tests/raid_dwell.rs` (new).
- `docs/backlog/packets/P-RAID-Q2.md`.
- **Oracle amendments for the owner:** `crates/rh-policy/tests/raid_quests.rs`: adds `walker_prices()` (walker defs at 1.0, all else 0)
  and builds its `RaidBot` with it, plus a test that the table lists the walker list. Reason: the walker's floors were measured under
  flat 1.0 pricing with no other executor; this keeps every later quest executor out of that file. Same seeds, floors and assertions.
  `raid_dwell.rs` correction (not a loosened expectation): `raid_bot_accepts_and_completes_each_dwell_quest` priced both dwell rows while asserting
  "accepts only the quest under test", which seed 2 (dealing both) contradicts; it now prices only that quest (`raid_only(def)`).

## The oracle (loose proof)

`crates/rh-policy/tests/raid_dwell.rs`, seeds of F3, Holy sword fighter 160 u from the giver, 2 400 ticks (120 s).
Tests (green today: three; red on the stub: four).

- Green: `dwell_scenes_offer_the_quest_at_the_giver_and_not_at_the_start`; `a_scripted_dwell_completes_each_scene` (5 of 5 per quest, clock
  reached 44 / 29 s); `a_second_player_in_the_guard_house_stops_the_scripted_hold`.
- `raid_bot_accepts_and_completes_each_dwell_quest`: **bot priced `DWELL_ONLY`** (`Prices::farm_only()` plus the two dwell rows: no walker
  quest, camp or chest competes, so the sweep proves the executor and not the arbiter's preference): accepts the scene's quest, accepts
  nothing else, completes it on **at least 4 of 5 seeds per quest**. **Floor chosen from the control** (5 of 5 each): a bot that walks
  and stands as the control does must match it; one seed per quest may be lost to a camp near the route or a different start path.
  **Not measured on a RaidBot yet** (none exists): the coordinator should tighten it from the first implementation's result.
- `raid_bot_takes_only_walker_and_dwell_quests_and_sends_only_accepted_commands`: dwell-only prices, ten seeds: no fault, no fallback,
  `rejected == 0`, `accepted == proposed`, took part in the board; default prices (walker and dwell compete) on seeds 13, 14, 19, 24: every
  accepted quest is on the walker or the dwell list, no rejected command.
- `raid_bot_replays_a_dwell_run_exactly` (hold seed 3, stay seed 5): accepted, completed, seat stats and final hash equal across two runs.
- `raid_bot_does_not_sit_in_a_contested_fort_for_ever` (seeds 3, 9): a second human stands in the guard house from tick 0 (alive at the
  end); the bot accepts the hold, reaches the fort, never completes (the server's pause), and in the last third of the window is inside the fort
  (72 u of the token, the proxy) on **under 90% of its reads**. **What it does not prove:** that the bot is *right* to leave (a bot
  that leaves at once passes; the F1 rule is the executor's design), and a single contested layout (a sentinel that never moves; no
  arrival mid-dwell, no contested meditation: nothing but damage contests it and the oracle has no attacker).
- Streams: no draw from `cx.rng` (the replay test and the unit tests of F6 see it).

## 10. Risk and suggested model tier

Sonnet: one executor in one file, modelled on `walker.rs`. Hard parts: the meditation channel (stop before sending; no move or attack
afterwards, or it silently resets); reading the dwell clock from the row; frame-local handles; the released-goal rule.

## 11. Known traps

- **The outline said the seat has no dwell figure: it has** (`owner_progress`, whole seconds). Use it.
- Action 6 sent while the seat is still moving: accepted by the sim but the position check at the next tick breaks it (the recorded
  position was mid-step). Send only when standing still, no move after.
- A mob hitting the seat (any damage) resets the meditation; Sustain's potion is fine (not a weapon action), an attack is not.
- Hold: leaving the house resets the clock; contest only pauses it. A dwell attempt survives a contest but not leaving.
- Hold turns in at the giver, 475-544 u from the fort: do not stand at the fort after `owner_ready`.
- The seat's own fort mark (kind 18) appears at its own position: ignore marks within a few units of self.
- Rows keep `owner_completed` false; completion is the row vanishing. A downed or dead owner loses the attempt (the row returns to `can_accept`).
- A third spell reward sticks in `pending` with action 8: the slots rule keeps a full seat from accepting.
- Registry order is behaviour: the stub has the line; do not move it.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
