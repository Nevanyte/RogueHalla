# P-RAID-Q1: RaidBot quest walker (accept, walk, interact, stand at the end point)

- **Serves:** not in the plan: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot,
  per-elite numbers"; plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 1, packet P-RAID-Q1)
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `coord/int-82` @ `81c42a70a`
- **D-rules touched:** D1 loose proof (owner direction 2026-11-10; RaidBot has no legacy counterpart, the oracle is a
  behaviour test on the live content world) · D2 n/a · D5/D6/D9 none (no sim, wire or observation change; the bot acts
  only through `validate` + `Sim::submit`)

## 1. ID and outcome

`RaidBot` does the quests of the "walker" kind: it accepts at the giver, walks to the objectives, sends the interaction
the server offers, and stands at the end point until the quest turns in. It is the first quest executor; dwell, decision,
kill, PvP and search quests are later packets (Q2..Q4, -P, -W) and the walker leaves them alone. When it lands, a
`RaidBot` seated as a human on the live content world, started a short walk from a giver, accepts a walker quest on
every seed of the oracle sweep, completes a floor of them before their timers, sends no rejected command, and replays a
seed exactly.

## 2. Dependencies

P-RAID-0 (merged; `coord/int-82`). A parallel packet adds `Prices::reward_value(def)` (P-RAID-V): do not depend on it.

## 3. Relevant legacy paths

None. The behaviour source is the Rust board (all paths below are in `crates/rh-sim/src`, verified on the base):

- `quests/board/attempts/choices.rs:6-122`: the actions a seat is offered (`choices`).
- `quests/board/attempts/interactions.rs:59-237` (`interact`), `:257-333` (`tick`: visits, ready, turn-in),
  `:335-389` (`objective_for_owner`: the objective list in the view).
- `quests/board/attempts.rs:290-312` (`accept`, deadline), `:384-397` (`endpoint`), `:260-273` (`finish`).
- `quests/board/catalog.rs:200-476` (def ids, route timers, counts); `quests/board/placement.rs:535-583` (where tokens are).
- `quest_view/board.rs:9-195` (what the seat's view holds); `quests/live.rs:608-650` (`accept_eligibility`).
- `quests/board/rewards/inventory.rs:180-300` (`pay_claim`, `resolve_choice`).

## Established facts (verified in code on the integration base)

### F1. What the server does for each walker quest

Common rules:
- **Accept.** `PublicCommand::QuestAccept { handle }` (`rh-sim/src/input.rs:120`). The row's `can_accept` is true only
  when the run is active, the giver stands, the seat is within `QUEST_ACCEPT_RANGE` 64 u with a clear sight line,
  the quest is not accepted or completed, and the giver is not scared off (`live.rs:608-650`; `QUEST_ACCEPT_RANGE`
  `constants.rs:392`). A bot seat is refused (`attempts.rs:216-218`, `owner_allowed`); gates locked refuses
  (`attempts.rs:291`).
- **Timer.** At accept, `deadline = now + max(route_timer_ms, route_length * 1500 / PLAYER_SPEED + 15 000)`, for a
  quest with a route timer (`attempts.rs:302-305`); 0 (no timer) otherwise. So the timer is at least 1.5 times the
  walk of the server's own path plus 15 s. The row shows it as `expires_at` (server ms, absolute; 0 before accept and
  for quests with no timer: `quest_view/board.rs:108`). At the deadline the attempt is abandoned by the tick
  (`attempts.rs:470-474`), the row stays and `can_accept` becomes true again (a retry starts a new attempt and timer).
- **Abandon (action 1) is offered for every accepted, unfinished attempt, always** (`choices.rs:35`, `push(1, 0, ..)`).
  The walker never needs it for a stage; it is not a stage action. Abandoning drops all progress (`attempts.rs:176-184`).
- **Other actions.** Action 3 is "contact" (take, collect, deliver, scare), with `target` the objective index. The
  server fills `actions` only when the seat is within `CONTACT_RANGE` = `QUEST_TURN_IN_RANGE` 64 u of that
  objective with a clear line (`choices.rs:46-47,87-92`; `interactions.rs:5`), not blocked by a status
  (`action_blocked`) and not while the gates are locked. The action's `at` holds the position.
- **Ready and turn-in.** An attempt is `ready` when its objective is met; `owner_ready` shows it. While ready, the view's
  objective list is exactly one point, the end point (`interactions.rs:347-349`). Turn-in needs no command: at
  the end point (within 64 u, line of sight) the tick sets `pending` (`interactions.rs:319-331`), and
  `rewards::inventory::pay_claim` (`:187-250`) grants the reward and `finish` marks it completed in the same pass.
  From `pending` on, the view has no objectives and no stage actions (`choices.rs:27-29`, `interactions.rs:342-344`).
- **Completion is not shown as a flag.** A completed offer is removed from the seat's rows
  (`quest_view/board.rs:17-23`, `!is_completed`), and `owner_completed` is always false (`:118`). Spell rewards
  arrive in `QuestView.spells` (`:122-134`); permanent rewards arrive in no list.
- **Two spell slots.** A spell reward with no free slot does not finish: the attempt stays `pending`, the view gains
  action 8 (reward slot choice) and `pending` holds its handle (`board.rs:48-73`; `inventory.rs:223-229`). The walker
  does nothing for a quest in `pending` with action 8 offered (Q3's). So a seat completes at most two spell-reward
  quests without a choice; the walker quests with a permanent reward (`let_him_cook` Chef's Kiss,
  `even_skeletons_get_cold` Warm Coat, `tuber_eats` Breaker's Helm: `catalog.rs:137-158`) use no slot. The other
  walker rewards are spells.
- **Public marks.** Smuggler's Run marks the carrier on every map (`quest_view/board.rs:198-225`); no other walker
  quest does. Nothing here is hidden from other seats' bots.

Per quest (`objectives` = the view's objective list for the quest, `acts` = non-abandon actions; "tokens" are the
offer's placed points, `placement.rs:535-583`):

| def id | tokens | stages as the server sees them | end point |
|---|---|---|---|
| `watchtowers` (timer 38 s) | 2 | objectives = unclaimed towers (`:386`); walking within 64 u with a line claims one (`:272-277`); no action; ready when all claimed (`:278-280`) | giver |
| `barrow_run` (45.2 s) | 3 (def count says 2, progress goes to 3: `:278`) | as watchtowers | giver |
| `pilgrims_path` (56 s) | 3 or 4 (measured: 3 at giver (2864,1136), 4 at (2832,1232)) | as watchtowers | giver |
| `ember_walk` (50 s) | 3 | per token: objective at the token (`:386`); within 64 u, action 3 target i claims it (`interactions.rs:134-139`); progress 3 -> ready | giver |
| `let_him_cook` (95 s) | 3: Holy contact, Frost contact, **a Plague camp centre** (`placement.rs:546-550`) | as ember_walk (action 3 target 0..2, any order) | giver |
| `smugglers_run` (53 s timer def; measured 80 s) | 1 | action 3 target 0 at the token (`:118-124`) sets `carrying`; objective becomes the giver (`:380`); back at the giver `tick` achieves (`:271`) | giver |
| `lost_brother` (59 s) | 1 | action 3 target 0 at the token: `carrying` and ready at once (`:118-124`) | giver |
| `tuber_eats` (56 s) | 1 | action 3 target 0 at the token = delivery, ready at once (`:104-110`) | **the token** (`attempts.rs:387`): standing there turns it in; the route has no leg back to the giver (`attempts.rs:360-365`) |
| `even_skeletons_get_cold` (45.2 s) | 1 | as tuber_eats | the token |
| `letters` (no route timer: Event clock) | 1 | as tuber_eats | the token. `expires_at` is 0; the attempt is deleted when the coliseum lock starts (`signals.rs:185-196`), the walker's clock is `arena.patience_ends_at` (`percept.arena`) when set |
| `a_cruel_joke` (50 s) | 3 contacts: the three nearest other givers by chain from this giver (`interactions.rs:24-40`) | objectives = the unscared contacts (`:368-370`); within 64 u of contact i, action 3 target i scares it (`:140-167`); progress 3 -> ready. The scared giver is hidden from this seat for 60 s: `can_accept` is false for its quest and a quest that ends at it cannot turn in until it returns (`:16-22`, `live.rs:641-645`) | giver |

Chain order is free: claims are by bit, any order works. `acts` lists carry the position the action applies to.

### F2. Quests that are NOT this packet, and why

- Decisions: `two_banners` (action 2), `the_bargain` (5, 7), `trade_up` (3 with a running target, 4; moving traders).
- Dwell: `hold_the_fort`, `stay_a_while` (action 6). Kill: `the_warden`, `elite_slayer`, `a_bone_to_pick`, `rime_hunt`.
- PvP, event: `bounty_hunter`, `gatecrasher`, `the_burning_crown`. Search: `whispers_in_the_brush`.
- **`feed_or_hunt`: moved out of the walker (the brief listed it).** Its creature is a live regional camp member
  ("Warren Forager": `targets.rs:189-199`, display name only at `:252`; nothing makes it passive). Feeding needs the seat
  within 64 u of it, three times (`interactions.rs:168-188`), with three separate "gather" contacts first; a walker
  that never fights would be killed by the camp or give up. It belongs to the hunt/camp packet (Q4). The measured deals
  show its action 3 target 3 offered at the giver on some seeds, so a walker that sends every offered action 3 would
  wander into it: the executor must filter by def.
- `let_him_cook` stays in, with a flagged risk: its third objective stands in a living Plague camp. The walker's
  death term must count a camp whose centre is within 480 u (`percept::CAMP_JOIN_RADIUS`) of an objective, priced with
  `farm::death_chance` over the camp's members alive, and may decline the quest when that is high.

### F3. What the percept gives the executor today (`bots/raid/percept.rs`)

`RaidPercept::quests: Option<WireQuestView>` (`:138`, copied at `:304`) holds everything a walker needs: rows
(`handle`, `def`, `giver`, `can_accept`, `owner_accepted`, `owner_progress`, `owner_ready`, `expires_at`; `state` 1 =
open), `objectives` (`quest`, `at`), `actions` (`quest`, `action`, `target`, `at`), `spells`, `pending`, `drops`.
`None` for a bot seat; `RaidPercept::arena` holds `patience_ends_at`. `now` is server ms (compare with `expires_at`).
`Refs::build` (`refs.rs:30-70`) keeps (handle, action, target) of the frame and `Refs::act` resolves
`Act::QuestAccept { def }` (needs `can_accept`) and `Act::QuestInteract { def, action, target }` (needs the exact triple in
this frame) to the frame's handle; the queue drops an act the frame no longer offers (`acts.rs:149-153`). Nothing is
missing for the walker. Gaps to know: the quest handle is per frame (key by def); completion is seen only as the row
vanishing (and `spells`); there is no "elapsed" figure, `expires_at - now` is the time left.

### F4. A seat on a board world, and the deals (measured)

The P-RAID-0 harness seats a human (`bot: false`) with `Sim::reset_active`, `Seat::new`, `drive_seats_with` ->
`Sim::step_into` -> `feed_seats`, on `seed1.map.json` with `SimConfig::live_content()` (`lifecycle: Off`,
`min_players: 1`), the board admitted by `map_admission::ensure(arena, Secrets::from_seed(seed))`. The board deals 10
quests per seed (2 of 5 per zone). Measured on seeds 1-24 by placing a human at each walker giver and accepting
(throwaway probe, not committed): **the givers' places are the map's fixed slots**, a quest's giver is one of one to
three points (Scorch (1040,1840) or (1072,1744); Warren (1360,3952) or (1360,4048); Frost (3312,3952..4112); Holy
(2864,1136) or (2832,1232); Gallows (4176,2000) or (4208,2096)), so zones are 2 000-3 500 u apart.

Round trips (giver -> every objective -> giver, straight lines) and measured timers, from that probe:

| def | straight round trip | timer |
|---|---|---|
| ember_walk | 1 412 u (8 s) | 50 s |
| watchtowers | 1 912-1 921 u | 38 s |
| barrow_run | 2 884-2 892 u | 45 s |
| pilgrims_path | 3 239 / 3 301 u | 56 s |
| a_cruel_joke | 4 096-4 103 u | 51 s |
| even_skeletons_get_cold | 4 033-4 288 u (one way about half) | 45 s |
| tuber_eats | 6 559-6 607 u (one way about 3 300 u) | 56 s |
| letters | 6 390-6 645 u (one way about 3 300 u) | none |
| lost_brother | 6 522-6 743 u | 73-75 s |
| smugglers_run | 7 065-7 120 u | 80 s |
| let_him_cook | 9 443-9 479 u | 100 s |

At about 170 u/s the walk is 8-56 s against a timer of 38-100 s: every timer leaves 1.3-3 times the straight walk
(the timer is built as 1.5 times the server's path walk plus 15 s).

Deals: **every seed 1-24 deals 3-6 walker quests** (excluding `feed_or_hunt`: at least 3); the seeds below are used in the
oracle; see "The oracle".

### F5. Existing tests that pin what this packet touches (none is amended by this packet's oracle)

- `crates/rh-policy/tests/raid_bot.rs` (P-RAID-0 oracle): RaidBot in a human seat on this same world, 8 seeds, 256 u from a
  three-member normal camp: asserts it clears the camp within 1 800 ticks on 7 of 8 seeds, `rejected == 0`, replays
  exactly. **CONFLICT FOR THE COORDINATOR (found by arithmetic, not run): registering the walker adds `QuestWalk`
  candidates in exactly that world. With `reward_value = 2.0`, the nearest walker quest scores about
  (2.0 x ~0.9 - death x 6) / (0.2 to 0.8 min) = 2-9 per minute, against the scene's camp at
  `camp * 3/4 x 0.97 / (0.03 + 3 x 10 s / 60) = about 1.4 per minute` (`exec/farm.rs`, `Prices::LOCAL`,
  `arbiter.rs:rate`). The walker would win the first strategy tick on most seeds and the bot would walk to a giver
  instead of the scene camp, turning `raid_bot_clears_a_nearby_normal_camp_within_90_s` red.** The brief said "the
  P-RAID-0 oracle stays green"; that holds only if the walker's price keeps farm ahead at 256 u from a camp, which then
  also changes how many quests the oracle below completes. Options for the coordinator: (a) keep `reward_value` flat at
  2.0 and approve an `oracle-amend` of `raid_bot.rs` (for example a scene whose seat has no quest to take;
  prices swapped by `RaidBot::with_prices`, which does not reach `reward_value`, so not enough on its own);
  (b) set the walker's flat value low enough that a near camp beats a far giver and a near giver beats a far camp (the
  oracle below then has no camp near its start by construction, see "The oracle"); (c) wait for P-RAID-V. The oracle
  below passes under (b) or (c) and under (a). **This packet's implementer must run `raid_bot` and stop and report if
  it goes red.**
- `crates/rh-policy/src/bots/raid/arbiter.rs:155` and `exec/farm.rs:256` unit tests: unchanged (no edit to either file).
- `HumanBot`/`BrainBot` tests and parity captures (listed in `P-RAID-0.md` fact 5): untouched.

## 4. New paths

- `crates/rh-policy/tests/raid_quests.rs` (oracle).

## 5. Exclusive write scope

- `crates/rh-policy/src/bots/raid/quest/walker.rs` and `quest/mod.rs` (stub-created).
- ONE line in `crates/rh-policy/src/bots/raid/exec/mod.rs` `registry()` (a shared file: merges one at a time). The
  `stub:` commit already holds it.
- Nothing else: not `bot.rs`, `arbiter.rs`, `goal.rs`, `acts.rs`, `percept.rs`, `refs.rs`. If the walker cannot be
  written without one of those, stop and say exactly what is missing.

## 6. Contracts and references

C-INPUT, C-OBS, C-POLICY; `crates/rh-policy/AGENTS.md`; `docs/agent/READABILITY.md` (`//!` header, 1 500 lines);
`rh-policy` `clippy.toml` (no hash collections, no clock, no platform libm: `rh_sim::math`); `docs/quests.md`.

## 7. Behaviour to preserve

- The P-RAID-0 oracle (see F5: conflict); `HumanBot`, `BrainBot`, every pinned capture untouched.
- Streams: the walker draws nothing from `cx.rng` unless it must; a draw is behaviour (registry order fixes it).
- `Target::Quest { def, stage }` names the quest by def id (never a handle or an npc id).

## 8. Executor design (what the oracle does not see; the reviewer checks it)

- Candidates: `GoalKind::QuestWalk`, `Target::Quest { def, stage }`, priced in camp-equivalents per minute: value =
  `reward_value(def)` (a local `fn reward_value(def: &str) -> f64` returning a flat 2.0, with a comment that P-RAID-V
  replaces it with `cx.prices.reward_value(def)`); success from the time left (`expires_at - now`, or the lock time
  for `letters`) against the walk still to do (`travel::eta_minutes`, falling back to `straight_minutes`), 0 when the
  walk cannot finish; death a function of the route (flat floor 0.02 as `farm::death_chance`, plus the camp term of F2 for
  `let_him_cook`), loss `cx.prices.death_loss`, minutes = the walk left plus the stand-still.
- Stages (`stage` is the executor's own index): 0 go to the giver and accept (`Act::QuestAccept`); 1 per objective walk
  (`exec/travel.rs`) and, when the frame offers action 3 for a quest of this packet's list, send it (`Act::QuestInteract`
  copied from the frame's `actions`; resend only after the act is dropped or the frame no longer offers it); 2 when
  `owner_ready`, walk to the single objective and wait; Done when the row is gone from the view (completed) or
  `pending` holds it with action 8 offered (Q3's). A visit quest sends nothing: walking claims.
- It abandons (action 1) only if a quest cannot finish in the time left AND waiting out the timer costs more than
  re-accepting; the brief finds that **abandoning is never cheaper** (an expiry resets the attempt for free, the row
  returns to `can_accept`, and an expired attempt costs nothing more than the abandon), so the walker never sends
  action 1; it drops the goal and lets the arbiter re-rank.
- With action 8 offered or an action 9 drop in the frame the walker does nothing for that quest (decision packet).
- It never attacks and never targets a player or an npc. Camp farming competes through the arbiter.
- Acts go through the queue; no `PublicCommand` is built in the executor.

## 9. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-RAID-Q1` passes.
- `cargo test -p rh-policy --test raid_quests` passes in a debug build (see "The oracle" for its run time); it is not
  release-only, so `scripts/gate.sh` needs no edit.
- `cargo test -p rh-policy --test raid_bot` stays green (see F5).

## Oracle files (read-only for implementers)

- `crates/rh-policy/tests/raid_quests.rs`
- `docs/backlog/packets/P-RAID-Q1.md`

## The oracle (loose proof)

`crates/rh-policy/tests/raid_quests.rs`. Seeds 1-6 on `seed1.map.json`, `SimConfig::live_content()` with
`lifecycle: Off`, `min_players: 1`, one `bot: false` Holy seat with a sword. Per seed the scene is the cheapest dealt
walker quest (order: ember_walk, watchtowers, barrow_run, pilgrims_path, a_cruel_joke, even_skeletons_get_cold,
tuber_eats, lost_brother, smugglers_run, letters, let_him_cook), with the start 160 u from its giver on a straight
floor line (measured scenes: ember_walk on seeds 1, 2, 5, 6; barrow_run on 3; pilgrims_path on 4). 3 600 ticks (180 s).
A second `ItemObserver` of the same viewer reads the sim's observation every 4 ticks: a quest is **accepted** when its
row shows `owner_accepted`, **completed** when an accepted row later leaves the view (F1: the board never sets
`owner_completed`). Cheapest-quest time: 8-19 s straight against a 38-56 s timer.

- **Control, passes today:** `raid_quests_scene_is_a_human_seat_offering_an_acceptable_quest_at_the_start` (human seat;
  at the giver the frame offers the quest with `can_accept`; at the start point, 160 u away, it does not) and
  `a_scripted_straight_walk_completes_the_scenes_quest` (no policy: accept, offered action 3, straight `Move` to the
  nearest objective; completes the scene's quest on **6 of 6** seeds within 180 s, measured, one spell held at the end).
  The second one validates the completion detector.
- `raid_bot_accepts_and_completes_walker_quests_before_their_timers`: on every seed RaidBot accepts a quest, every quest it
  accepts is on the walker list (it does not touch dwell, decision, kill, PvP, search quests or `feed_or_hunt`), and the
  sweep totals at least 6 completions with at least one on 5 of the 6 seeds. **How the floor was chosen:** the
  scripted straight walk does one quest per seed (6 total); a bot that walks the same routes at the same speed and
  keeps the whole window (180 s) must at least match it; one seed is allowed to be lost to a camp or a different
  choice of quest. The floor is **not measured on a RaidBot** (none exists): the coordinator should tighten it from the
  first implementation's total.
- `raid_bot_sends_only_commands_the_sim_accepts_on_the_quest_board`: every seed, no fault, no fallback decision,
  `rejected == 0`, `accepted == proposed`, and a quest was accepted.
- `raid_bot_replays_a_quest_run_exactly` (seed 1): the same accepted and completed sets, seat stats and final state hash.

Red on the stub: the three RaidBot tests, each on an assertion ("accepted no quest"). Debug build: about 12 s for the
file with the stub (the scripted control 0.8 s); expect under a minute with the executor.

## 10. Risk and suggested model tier

Sonnet: one executor in one file, modelled on `exec/farm.rs`; the hard parts are frame-local handles (resolve by def
and action triple every frame), the act queue's pacing (a send every 180-420 ms and a TTL of 4 s), and pricing against farm
(F5).

## 11. Known traps

- A frame with `quests == None` (a bot seat, or no board): propose nothing.
- Row `handle` and `actions[].quest` change; key by `def`. Do not assume two walker quests are not offered together.
- `actions` always holds action 1 (abandon) for an accepted attempt: never send "the first action".
- `feed_or_hunt` action 3 may be offered at the giver: filter by def.
- `owner_progress` counts claimed anchors (barrow_run reaches 3 though the def says 2); use `owner_ready`, not a count.
- A visit quest's objectives shrink as tokens are claimed; when empty and `owner_ready` is false the quest is bugged or the
  gates are locked: do not busy-loop.
- The end point of `tuber_eats`, `even_skeletons_get_cold` and `letters` is the delivery token, not the giver.
- `a_cruel_joke` hides a giver for 60 s (`can_accept` false on its quest): a re-proposal must not try to accept it.
- Turn-in and every contact are refused while `gates_locked` (the coliseum): the arbiter and the executor must give up then.
- Rewards: the third spell reward has no slot and sticks in `pending` (action 8): not a failure of the walker.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
