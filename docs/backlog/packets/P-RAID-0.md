# P-RAID-0: RaidBot skeleton (policy, percept, act queue, executor trait, arbiter with farm, roam and chest)

- **Serves:** not in the plan: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot,
  per-elite numbers"; plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 1 and its wave-1 row P-RAID-0;
  backlog README section 15)
- **Implementer tier:** implementer-opus
- **Reviewer:** reviewer-opus (bot priority competition, `crates/rh-policy/AGENTS.md` "Routing")
- **Integration base:** `feature/unity-rework` @ `83edb687f`
- **D-rules touched:** D1 loose proof (owner direction 2026-11-10): no legacy capture; RaidBot is a new policy with no
  legacy counterpart, and its oracle is a behaviour test on the live content world · D2 n/a · D5/D6/D9 none (no sim,
  wire, observation or client change; D9: the bot acts only through `validate` + `Sim::submit`)

## 1. ID and outcome

A third bot policy, `RaidBot`, under `crates/rh-policy/src/bots/raid/`, built to be the balance lab's test player
(owner: "Lab now, live later"). This packet is its skeleton: it reads only `Frame14` (`InputKind::Delivered14`, like
`HumanBot`), builds its own percept, keeps a paced act queue for the public commands the shared bot `Command` enum
does not carry, and chooses goals with one arbiter scored in camp-equivalents per minute. Only three goals exist in
this packet: farm a camp, roam, open a reward chest. When it lands, a `RaidBot` seated as a human on the live content
world walks to a nearby normal camp and clears it, sends only commands the sim accepts, and plays the same match twice
from the same seed. Every later `P-RAID-*` packet edits only its own file (section 4).

## 2. Dependencies

None. Later packets depend on this one (`BALANCE_LAB_PATCH.md` "Packets and waves").

## 3. Relevant legacy paths

RaidBot has no legacy counterpart. It reuses ported modules whose legacy sources are:
- `packages/server/src/bots/brain/brain.ts:142-146`: the bot's seeded streams `derive(cfg.seed, 0/1/2)` for
  decide/motor/lapse and `derive(cfg.seed, 3)` for Knowledge (ported at `crates/rh-policy/src/bots/bot.rs:349-387`).
- `packages/server/src/bots/mobHunt.ts`: camp-hunting rules (`mobWeapon` :481, `useless` :491, `bestEdge` :504,
  `bestSlotVsMob` :519, `retreatPoint` :529), ported as `crates/rh-policy/src/bots/mob_hunt.rs`.
- `packages/server/src/bots/brain/modules/chests.ts`, `modules/planning.ts`, `modules/wishes.ts`: ported as
  `bots/loot.rs`, `bots/planning.rs`, `bots/wish.rs`.
- `packages/server/src/bots/brain/humanizer.ts`: ported as `bots/humanizer.rs`.

## Established facts (verified in code on the integration base)

1. **Quest view by seat kind. True.** The quest stager gives a bot seat an empty view
   (`crates/rh-sim/src/quest_view/mod.rs:203`, `sim.player(viewer).filter(|p| !p.seat.bot)`), and the seat's
   observer then leaves `Observation::quests` as `None` for a bot seat and `Some` for a non-bot seat on a world with
   quests (`crates/rh-sim/src/item_view.rs:570-572`). The delivery projection copies it to State key 10
   (`crates/rh-policy/src/delivery/collections.rs:159-290`, sent at :809-810), and `Frame14` holds it in
   `CollectionView::quests` (`crates/rh-policy/src/delivery/frame.rs:59`, accessor `collections().quests()` :103).
   Measured on `seed1.map.json`, `SimConfig::live_content()` with `lifecycle: Off`, seed 1, at tick 0: human seat
   `Some` with 10 rows and 0 actions; bot seat `None`. The board also refuses a bot's quest actions
   (`crates/rh-sim/src/quests/board/attempts.rs:217`, `owner_allowed`). A board world exists only when
   `SimConfig::quests` is on and the map has camps (`crates/rh-sim/src/quests/live.rs:290-307`).
2. **Camps and elites in a frame.** `WireCamp` rows are index-aligned with the map's camps: the sim builds
   `MobRuntime::camps` by enumerating `layers().data().camps` (`crates/rh-sim/src/systems/mobs.rs:96-160`, camp index
   `ci`; members get `camp: ci` at :206), `Sim::camp_rosters()` returns them in that order (`sim.rs:181`), the
   observation copies them as is (`observe.rs:952-953`) and delivery maps row `i` to `WireCamp` row `i`
   (`delivery/collections.rs:539-570`). `PlanMap::from_arena` reads the same `layers().data().camps` in the same order
   (`bots/planning.rs:480-507`). So `WireCamp[i]` is `PlanMap.camps[i]`, provided the `PlanMap` is read from
   `MatchStart::arena` (the sim's own arena, after any map admission swap: `MatchStart::of`, `lib.rs:106-121`).
   **There is no elite flag on an actor.** `WireActor` (`crates/rh-protocol/src/actor.rs:186-228`) carries
   `mob_type_code` (`MobTypeId as u8 + 1`, 0 none; `delivery/projection.rs:158`) and no camp index. A camp's elite is
   `WireCamp.elite: Option<u8>` (the elite's `MobTypeId`, `delivery/collections.rs:563`); an actor is an elite when its
   type is an elite type (`mob_catalog::mob_type_by_id(..).elite`, as `bots/perception.rs:258-263` already classifies
   it). Joining an actor to its camp is by position (nearest `PlanCamp`), which the percept must do itself.
3. **Coliseum lock. A time is published, not only the flag.** State key 9 (`WireArena`,
   `crates/rh-protocol/src/state.rs:303-314`, filled at `delivery/collections.rs:705`) carries `gates_locked`,
   `in_coliseum`, `patience_ends_at: Option<f64>` (server ms of the next teleport into the coliseum when one is
   scheduled: `crates/rh-sim/src/systems/coliseum.rs:107-150`, cleared on teleport :180) and `sudden_death_at`.
4. **Harness.** `rh-headless` can seat a policy only at the arena's spawn list (`Env::new`,
   `crates/rh-headless/src/lib.rs:86-200`, placement `spawns[(i*7+seed) % len]`), and its bot-match runner hard-codes
   `bot: true` (`lib.rs:621-640`). The elite bench seats a `bot: false` fighter at a chosen start with
   `Sim::reset_active` (`crates/rh-headless/src/elite_bench.rs:417-433`) but drives it as an external agent
   (`Env::from_external_checkpoint`, raw `Observation`, no `Frame14`). The cheapest harness that delivers `Frame14` to
   a non-bot seat at a chosen start uses rh-policy's own public seat API, exactly what `Env::advance` does
   (`rh-headless/src/lib.rs:366-389`): `Sim::reset_active` with a `Placement { bot: false, at }`, `Seat::new` +
   `Seat::bind_fx`, then per tick `drive_seats_with` → `Sim::step_into` → `feed_seats`
   (`crates/rh-policy/src/lib.rs:382`, `:603`, `:723`). It needs no `rh-headless` and no new dependency (`serde_json`
   is already an rh-policy dev-dependency, for the map file). **The oracle lives in
   `crates/rh-policy/tests/raid_bot.rs`.** Measured in a debug build: one 1,800-tick run of `HumanBot` on this world
   takes 0.1-1.3 s.
5. **Tests that pin what this packet must not touch** (they stay green; none needs an amendment):
   - `crates/rh-policy/src/bots/parity_tests.rs`: HumanBot's hand and cadence against `parity/policy/bot-core.json`.
   - `crates/rh-policy/src/bots/bot/{connected_tests,loot_seat_tests,reset_stream_tests}.rs` and
     `bot.rs` `knowledge_repair_reset_tests`: HumanBot's frame checks, loot seat, `derive(seed, 0..3)` streams.
   - `crates/rh-policy/src/bots/{wish_module_tests,frost_wall_bots_tests,heal_prop_bots_tests,planning_parity_tests}.rs`,
     `humanizer/feature_tests.rs`, `fair_percept14/tests/connected.rs`, `brain_bot/{tests,reconcile_tests}.rs`,
     `temporal_percept/**/*_tests.rs`, `src/delivery_tests.rs`, `tests/first_pick_bots.rs`: the shared modules,
     `Command`/`COMMAND_TYPES` (`bots/types.rs:705`, enum at `:983`) and BrainBot.
   - `crates/rh-headless/tests/{bots,bot_loot,brain_bots,brain_choices,frost_wall_bots,frost_wall_live,source_arena_soak,tournament_learning}.rs`
     and `crates/rh-server/src/room/tests/bot_fill.rs`: HumanBot/BrainBot seats, telemetry keyed by `COMMAND_TYPES`.
   - `crates/rh-policy/tests/quest_carrier_delivery.rs:62-75`: `OBSERVATION_VERSION == 37`,
     `POLICY_CONTRACT_VERSION == 33`.
   - `tools/bot-*-fixtures/`, `parity/policy/*`: source captures behind the parity tests.

## 4. New paths

All under `crates/rh-policy/src/bots/raid/`. The `stub:` commit creates every one of them (the stub files hold their
`//!` line and nothing else, so each later packet owns one file):
- `mod.rs`: module table, `pub use bot::RaidBot`.
- `bot.rs`: `RaidBot` (`Policy`, `InputKind::Delivered14`).
- `percept.rs`: `RaidPercept` from `&Frame14` only.
- `refs.rs`: handles valid for this frame (actors, props, reward chests, quest handles and actions).
- `acts.rs`: the paced act queue for `PublicCommand::{QuestAccept, QuestInteract, UseConsumable, Trade}`.
- `goal.rs`: `RaidGoal`, the `Executor` trait.
- `arbiter.rs`: candidate scoring.
- `exec/mod.rs`, `exec/travel.rs`, `exec/farm.rs`.
- Later packets' files, header only in this packet: `values.rs` (P-RAID-V), `exec/combat.rs` and `exec/sustain.rs`
  (P-RAID-C), `exec/elite.rs` (P-RAID-E), `quest/{mod,walker,dwell,decide,hunt,pvp,search}.rs` (P-RAID-Q1..Q4, -P,
  -W), `spells.rs` (P-RAID-S).
- Oracle: `crates/rh-policy/tests/raid_bot.rs`.

## 5. Exclusive write scope

- `crates/rh-policy/src/bots/raid/**` (created by the stub; the implementer fills them).
- `crates/rh-policy/src/bots/mod.rs`: the `pub mod raid;` line and `pub use raid::RaidBot;` (both already in the stub;
  nothing else changes there).
- `crates/rh-policy/README.md`: one "Entry points" line for `src/bots/raid/` (READABILITY "What to write"; the
  readability check itself needs only the `//!` headers, which the stub files have).

Nothing in `rh-sim`, `rh-protocol`, `rh-server`, `rh-headless/src`, `bots/types.rs`, `bots/bot.rs`, `brain*`,
`temporal_percept/`, `Cargo.toml`, `Cargo.lock`.

## 6. Contracts and references

- C-INPUT, C-OBS, C-POLICY (`docs/CONTRACTS.md`); ADR 0005 (policy seam); `crates/rh-policy/AGENTS.md`.
- `crates/rh-policy/clippy.toml`: no hash-ordered collections, no clock, no platform libm (use `rh_sim::math`).
- `docs/agent/READABILITY.md`: `//!` header on every file, at most 1,500 lines per file.

## 7. Behaviour to preserve

- `HumanBot` and `BrainBot` unchanged (every test in fact 5 stays green, unmodified).
- The shared bot `Command` enum (`bots/types.rs:983`) and `COMMAND_TYPES` (`:705`) gain nothing: they key
  `HumanBot`'s stats (`bot.rs:327-331`). RaidBot's new commands go out as `PublicCommand` through `acts.rs`.
- `POLICY_CONTRACT_VERSION` (33), `OBSERVATION_VERSION` (37), `PROTOCOL_VERSION`, `parity/policy/*`,
  `tools/bot-*-fixtures` unchanged. No sim, wire or observation change.
- Streams: RaidBot keeps HumanBot's tags for the shared modules (`derive(seed as u32, 0)` decide, `1` motor, `2` lapse,
  `3` Knowledge, as `bot.rs:353-386`) and draws its own decisions from new tags: `16` arbiter, `17` act queue. Adding a
  draw on 0-3 that HumanBot does not make is allowed (RaidBot is its own policy), but no draw may depend on a hash-order
  or the clock.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-RAID-0` passes.
- `cargo test -p rh-policy --test raid_bot` passes in a debug build (it takes a few seconds; it is not a release-only
  test, so it needs no `scripts/gate.sh` change).
- Design requirements the oracle does not see, checked by `reviewer-opus` against this brief:
  - `RaidBot` copies HumanBot's input checks (`bot.rs:400-470`: viewer and owner match, inventory and owner combat
    present outside planning, memory scene check) and answers the planning screen through `PlanModule` as
    `bot.rs:504-548` does.
  - `RaidPercept` reads only `&Frame14` (owner hp and max hp, potions, build and boosts, camps joined to `PlanMap`
    camps by index, actors with elites classified by type, the quest board from `WireQuestView` rows, objectives,
    actions, spells, pending and drops; trade; fields; reward chests).
  - `refs.rs` resolves only handles of the current frame; `acts.rs` drops an entry whose handle is gone in the next
    frame and paces sends so the seat stays inside the order budget.
  - `goal.rs`: `RaidGoal` and `trait Executor` with `Running`/`Done`/`Failed`, returning intents (for the
    `Humanizer`) and acts (for the queue).
  - `arbiter.rs`: each candidate scored `(value x success chance - death chance x loss) / time` in camp-equivalents
    per minute, times tier and persona multipliers; the incumbent goal keeps a hysteresis margin; ties broken by a
    stable order (candidate kind, then camp index or handle). Candidates in this packet: Farm, Roam, Chest. Values are
    local constants until P-RAID-V: one camp = 12 stacks = 1 camp-equivalent.
  - `exec/travel.rs` walks through `RoomPathService` within `BOT_PATH_BUDGET_PER_TICK` (`bots/paths.rs:15`, `:74`,
    `walk` :191); `exec/farm.rs` uses `mob_hunt` rules plus a lock/attack loop like HumanBot's
    `compatibility_tactics` (`bot.rs:245-307`); chests through `Loot` (`chest_goal`, `act_chest`, `intents`).
  - Not used: `reflex`, `hazards::intents`, anything in `temporal_percept/`.

## Oracle files (read-only for implementers)

- `crates/rh-policy/tests/raid_bot.rs`
- `docs/backlog/packets/P-RAID-0.md`

What the oracle asserts (seeds 1-8, `crates/rh-sim/src/mapdata/fixtures/seed1.map.json`,
`SimConfig::live_content()` with `lifecycle: ArenaMode::Off` and `min_players: 1`, one seat `bot: false`, Holy, sword,
`RaidBot::new(TierId::Normal, PersonaId::Farmer)`; the scene per seed is the first camp with no elite and exactly three
regional members, the start 256 units from the members' centroid on a straight floor line, as the elite bench picks
its start):
- **Harness precondition** (true before any implementation): the seat is not a bot seat and the world has a quest
  board (the non-bot seat's observation carries a quest view).
- **Control** (true before any implementation): `HumanBot` in the same seat clears the scene camp within 1,800 ticks
  (90 s) on at least 7 of the 8 seeds (measured: 8 of 8, 143-1,011 ticks).
- **Clears a camp:** `RaidBot` gets `Event::CampCleared` for the scene camp within 1,800 ticks on at least 7 of 8 seeds.
- **Only accepted commands:** on every seed, the seat proposed something, `SeatStats.rejected == 0` (a proposal that
  does not resolve to a handle of the frame, or that `Sim::submit` refuses, including `OverBudget`, counts as
  rejected: `lib.rs:871-890`), no fallback decision and no seat fault.
- **Deterministic:** on seed 1, two runs give the same clear tick and the same `Sim::state_hash()` at the end.

The 7-of-8 floor, not 8 of 8: a three-mob camp can kill a fresh fighter without sustain (P-RAID-C adds retreat and
potions); measured with HumanBot on the first 12 normal camps of each of seeds 1-8 (all sizes), 32 of 96 ended in
death or no clear within 90 s, while the 3-member scene camps cleared 8 of 8.

## 9. Validation commands

- `export PATH="$HOME/.cargo/bin:$PATH"; cargo test -p rh-policy --test raid_bot`
- `scripts/gate.sh --quick --oracle packet/P-RAID-0`

## 10. Risk and suggested model tier

Opus: a new policy whose priorities compete every decision tick (`rh-policy/AGENTS.md` routing), its own RNG streams,
and the structure every later `P-RAID-*` packet builds on. The hard parts: keeping every handle frame-local (a handle
from an older frame is refused), staying inside the seat's order budget while the Humanizer and the act queue both
send, and a deterministic arbiter (ordered candidates, no float-order surprises: total_cmp with a stable tiebreak).

## 11. Known traps

- `MatchStart::arena` is the sim's arena after the Top 25 map admission (`map_admission::ensure` may swap the map,
  `crates/rh-sim/src/quests/board/map_admission.rs:17`): build `PlanMap` and paths from it, never from a file.
- Quest givers are mobs too (`MobKind::QuestGiver`, `mob_runtime.rs:22-28`, built with camp `0` at :387-395); they
  are actors of kind 4 in a frame (measured: on seed 1, "camp 0" holds 5 regional mobs and 10 quest givers). A farm target filter that counts every non-player actor near camp 0 attacks a quest giver.
- `PickupHandle`, `RewardChestHandle`, quest handles and actor ids are valid only for the frame that delivered them
  (`Seat::resolve`, `lib.rs:435`, refuses stale ones; `submit_all`, `lib.rs:871-890`, counts it as rejected).
- The order budget is per seat in sim time (`Sim::submit`, `sim.rs:465`; `Reject::OverBudget`, `input.rs:526-527`):
  the Humanizer already throttles its own sends; the act queue must share that budget, not add to it blindly.
- A decision interval other than 1 tick changes how often the bot sees a frame; HumanBot declares 1
  (`bot.rs:323`).
- Mobs give up after 20 s without hitting and heal to full (elite bench doc, `elite_bench.rs:73-75`): a bot that kites
  out of range resets the camp.
- No hash-ordered collection, no `std::time`, no `f64::sin/cos/sqrt` (clippy.toml); use `rh_sim::math`.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
