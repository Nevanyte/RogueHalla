# P-BAL-LAB-1: `balance_lab`, a headless measurement tool (observation-based metrics, no paired mode)

- **Serves:** not in the plan: owner approval 2026-11-22 (plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 2 and
  the P-BAL-LAB-1 row; owner decision row at the top of `docs/OWNER_DECISIONS.md`)
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `feature/unity-rework` @ `83edb687f`
- **D-rules touched:** D1 loose proof (AGENTS.md, owner direction 2026-11-10: no exact capture) · D2 n/a · D5/D6/D9 none
  (headless, `rh-sim` read-only; no protocol, hash or observation change)

## 1. ID and outcome

`balance_lab` plays seeded 8-seat matches under `SimConfig::live_content()` and reports the rates the quest and augment
prices depend on (camps, deaths, elite fights, quest funnel, chest offers and picks, augment holders), as one JSON report
on stdout and a table on stderr. The observer only reads: it takes `&Sim`, the step's events and ordinary observations,
adds no `Event` variant and no sim state, and the match it watches ends in the same state hash as an unwatched one. Paired
mode (grant one card or reward, compare) is P-BAL-LAB-2; the evaluator is P-BAL-EVAL-1; neither is here.

## 2. Dependencies

None. `RaidBot` (P-RAID-H1) will plug into the same `--seat` / `--human-seats` flags later; do not depend on it. The plan's
text mentions a `balance-lab` cargo feature; this packet needs none (see section 5).

## 3. Relevant legacy paths

No legacy counterpart: this is a Rust-side measurement tool. Nothing under `packages/` is read. It is a measurement of the
Rust port's own live world. The patterns copied are `crates/rh-headless/src/elite_bench.rs` (jobs: line 752 `jobs`, scope at
~1014, results folded in input order at ~1047-1059) and `src/bin/camp_bench.rs` (JSON on stdout, table on stderr).

### Facts established (all at `83edb687f`; the implementer re-verifies a line before relying on it)

**Setup.**
- Config as `examples/source_soak.rs:23-30`, with `min_players: 8` (the brief) and `lobby_seconds: 5., fill_seconds: 3.`.
  `min_players` counts only seats with `bot: false` (`systems/lobby.rs:22-30`): with `--human-seats` the lobby starts
  `fill_seconds` after the 8th joins; with bot seats (`bot: true`) the count never reaches 8 and the lobby runs to
  `lobby_seconds` (measured: bot seats are still in `Planning` at tick 200, human seats are `Active`).
- `Env::new` (`lib.rs:86`) admits through `Sim::new` for `ArenaMode::Source` (`lib.rs:130-146`); the sim itself swaps a map that
  cannot host the board (`sim.rs:131-136` -> `quests/board/map_admission.rs:17-49` `ensure`). The lab therefore records the
  map the sim ended up with (`Sim::arena()`, `sim.rs:292`; `Arena.map.layers()` then `legacy_map_hash_u32()`,
  `mapdata/mod.rs:89`, or `mapgen::map_hash::map_hash`, `mapgen/map_hash/mod.rs:99`) and not the map it passed in.
- One generated map per seed: `mapgen::coliseum::fixed_layout()`, `mapgen::pipeline::Pipeline::new(&layout)`,
  `.certified_styled(seed as i64, Options { decor: false, ..Default::default() })` -> `Option<GameMapData>` ->
  `MapExport { format: "rh-map/2", legacy_map_hash_u32, map }` -> `.validate()` -> `.into_arena()` (as
  `rh-server/src/map_pool.rs:116-140`, and `quests/board/attempts/scenarios.rs:48` builds a quest raid the same way). Measured:
  about 30 ms a map in a debug build. `None` (generator refused) is an error for that seed, not a skip.
- Agents: the lab builds its own `AgentSpec`s (`run_bot_match_kind` hard-codes `bot: true`, `lib.rs:635`). Use
  `BotKind::policy` (`lib.rs:571`) for `brawler` and `human`, which draws tier and persona from
  `rh_policy::bots::assign_seats(seed, 8)`. Faction `FactionId::ALL[(i + seed) % 5]` as `run_bot_match_kind` and `source_soak`.
- Ticks: `Sim::tick_ms()` (`sim.rs:304`) is 50 under `live_content()` as measured, so 20 ticks per second, minute 5 = tick 6000.

**Seat-gated behaviour: what changes when a seat is `bot: false`** (`grep seat.bot crates/rh-sim/src`; the lab report must
state these in its `seating` block or the table header). A seat with `bot: true`:
- sees no quest view at all (`quest_view/mod.rs:203`, empty `QuestView`); the HUD roster still tells everyone who is a bot
  (`observe/hud.rs:166-174`). Measured: observing a bot seat gives 0 quest rows, a human seat 10 rows (`RowState::Preview`/`Open`);
- cannot accept, interact or cast quest things: attempts need `!seat.bot` (`quests/board/attempts.rs:217`, `:599`; a bot's
  existing attempt is dropped at `:567`), quest spells (`quests/board/rewards/casts.rs:55`), quest loot pickup
  (`rewards/inventory.rs:312`), deployments (`rewards/effects/terrain.rs:454`, `checkpoint.rs:7`), the egg
  (`special/egg.rs:87,307`) and the wall (`special/wall.rs:29`);
- takes no part in quests by source `takesPart` (`quests/live.rs:345,354`: `standing_human`);
- cannot take a world item through the item path (`item_view.rs:140`);
- counts as a "fill bot" for kill accounting: a bot victim gives no `damaged_humans` entry (`systems/ledger.rs:1122,1142`),
  `credit::kills` gives `bot_kills` and no GV for killing a bot (`systems/credit.rs:61-66`), and the "Kill a player" quest signal
  discounts bot kills (`quests/board/attempts/signals.rs:173`);
- does not count toward the lobby start (`systems/lobby.rs:22`), `started_humans` (`systems/arena_rules.rs:138`,
  `systems/planning.rs:138`) or the result `bot` flag (`systems/settle.rs:64,359`);
- Bounty Hunter's "a living fill bot may be marked when no real bounty exists" (`quests/board/catalog.rs:227`) is gone when
  nobody is a bot.
So with `--human-seats` every seat can quest and every kill is a human kill; without it the quest block is all zeros by
construction, not by measurement. The plan states the same limit ("results describe bots allowed to quest").

**Metric sources** (quests are the only ones that need an `Observation`; all others come from events and `&Sim`):

| Metric | Source (type, field or event, file:line) |
|---|---|
| quests: dealt | per seat, `rh_sim::observe(&sim, seat, events).quests: Option<QuestView>` (`observe.rs:764`, field `:348`), `QuestView.rows` (`quest_view/mod.rs:54-83`, rows `:125` area): count distinct `QuestRow.handle` / `def` per seat seen in any `RowState` (`:35`). Preview rows are dealt but not accepted |
| quests: accepted, pickup minute | `QuestRow.owner_accepted` false -> true; minute = `sim.server_time()` at the flip (`Sim::tick()` x `tick_ms()`) |
| quests: completed, seconds to complete | `QuestRow.owner_completed` (`:79`), seconds from the accept flip. `owner_ready` (`:81`) is "objective met, turn-in left", not completion |
| quests: ended by timer / death / abandoned | attempt gone without `owner_completed`: `QuestRow.expires_at` (`:67`) <= `sim.server_time()` -> timer; else `Event::Death { id: seat }` that tick (`events.rs:199`) -> death; else abandoned. `RowState::Expired` (`:39`) is the row-level hint. Documented heuristic: same-tick expiry and death count as timer |
| quests: attempts | `owner_accepted` false -> true flips (a re-accepted offer is a second attempt) |
| quest spells: casts per reward | `QuestView.spells: Vec<QuestSpell { slot, reward, charges, ready_at }>` (`:92`): `charges` drop or `ready_at` jump forward = one cast of `reward`. Drops (`QuestView.drops`, `QuestDrop`) are separate |
| augments: offered | `Observation.reward_chests: Vec<RewardChestView>` (`observe.rs:354`; `reward_chests/view.rs:87`: `handle`, `options: Vec<RewardOptionView { id: OptionId, taken, taken_by }>`, `can_choose`, `picked`). Offered = each `(chest handle, option)` first seen with `can_choose` true for that seat. `OptionId` is `Card`, `Boost` or `Kill` (`:62`) |
| augments: picked | `Event::ChestTaken { chest, by, pick: i8 }` (`events.rs:67`, `Visibility::ServerOnly` but present in `Env::last_events()`); `pick` indexes the options last seen. Cross-check: the seat's build grows |
| augments: held by the winner | `Event::Victory { id: Option<PlayerId>, draw }` (`events.rs:214`), then the winner's `sim.player(id).build` (`state.rs:592`); `Build::cards()` yields native cards too (`cards/mod.rs:385-437`). **`Observation.build` / `BuildView.cards` are the 80 catalog counts only** (`reward_chests/view.rs:107-117`, `Build::counts`): read native cards from `&Sim`, not from the observation |
| augments: native counters at minutes 5, 8, 12 | `rh_sim::cards::native::readouts(&sim, seat)` (`cards/native/readout.rs:73`; rows `Readout { card, kind, value, max }`). Empty unless the world is `AugmentRule::Rework` (it is under `live_content()`) |
| world: camps cleared, credited | `Event::CampCleared { camp: u32 }` (`events.rs:46`; `camp` indexes `Sim::camp_rosters()`, `sim.rs:181`, `CampRoster` `mob_runtime.rs:97`). Credit = the seat with the most `Event::Hit` damage (`events.rs:163`, `victim` a mob id) on that camp's mobs; `Mob.camp` (`mob_runtime.rs:115`) from `Sim::mobs()` (`sim.rs:165`). **A cleared mob may already be gone from `sim.mobs()` when the event is read**: keep an insert-only `BTreeMap<mob id, (camp, mob_type, elite)>` refreshed from `sim.mobs()` every step |
| world: kill / heal-up / walk seconds | heuristic as the brief: kill = first `Hit` on the camp to `CampCleared`; heal-up = cleared until `Player.vitals.hp >= 0.95 * max_hp` (`observe` `SelfView.hp/max_hp`, or `sim.player(id)`) or the next hit; walk = the rest of the interval to the seat's next camp. Heal comes from `Event::Heal { id, amount }` (`events.rs:189`) |
| world: stat stacks at minutes 5, 8, 12 | `sim.player(id).farm.count(mob_type)` (`state.rs:596`, as `elite_bench.rs:176-181`) per `mob_catalog` normal type; boosts `Build::boosts()` (`cards/mod.rs:440`). Minute m = tick `m * 60 * 20` at 50 ms |
| world: deaths per player per minute by cause | `Event::Death { id, x, y, by }` (`events.rs:199`) for a player id (`sim.player(id).is_some()`; mob deaths share the variant, `systems/mob_life.rs:366,432`). Cause: `by: Some(p)` -> `player`; `by: None` -> `elite` / `mob` if the victim's latest `Event::Hit.by` resolves to a mob id (elite if `Mob.elite`), else `hazard`. **Unverified:** whether `Hit.by` carries the mob id for a mob attacker (`systems/ledger.rs:890` `e.credit_id()`); the implementer checks with one assertion and reports `hazard` honestly if mobs leave `by: None` |
| world: elite attempts and outcomes by kind and weapon | Attempt = a seat's first `Hit` on a mob with `Mob.elite` (`mob_runtime.rs:114`), kind `Mob.mob_type`, weapon `Event::Hit.weapon` (`events.rs:163`, `Option<WeaponId>`) or `Player::active_weapon()` (`fighter.rs:168`). Outcome: elite `Event::Death` with `by: Some(seat)` = killed; seat `Death` = died; neither before the match ends = open |
| match length | `Progress.tick` / `Env::advance` (`lib.rs:366`), `terminated`/`truncated` |
| share reaching the coliseum | **no source in this packet** (needs phase/position reads; `Phase` and arena zone geometry exist but the plan's "share reaching the coliseum" is not in this packet's metric list). Reported as absent, not invented |
| `content_hash` | `rh_sim::content::content_hash()` (`rh-server/src/room.rs:712` uses it), written `{:016x}` |
| final state hash | `Sim::state_hash()` (`source_soak.rs:67`), the purity oracle's comparison |

Metrics with **no source**: none of the brief's list lacks one, with two caveats the report must state: the camp walk / kill /
heal-up split and quest end causes are heuristics, and `hazard` vs `mob` death cause depends on the unverified `Hit.by`
point above. "Encounters while questing" (plan, workbook input) and "share reaching the coliseum" are not in this packet's
list; they are not built.

**Same-tick competitions and boundaries to record in the report's notes:** a death and a quest expiry on one tick; a camp
cleared on the tick its last mob's `Death` event is emitted (mobs may leave `sim.mobs()` the same step); `Event::Death` for
a seat and its `Resolved` come in one step; observing after `Phase::Ended` returns empty quest rows (measured: 0 at the final
tick), so the observer stops sampling on the step that ends the match; an empty/full inventory does not change any metric here;
no RNG draw: the observer never draws and never submits (purity test).

**Measured cost (debug build, this machine, one 8-seat match, `max_ticks` 6000, generated map):**

| Run | Ticks | Wall time |
|---|---|---|
| seed 1, human seats, no observer | 1193 (ended) | 1.9 s |
| seed 2, human seats | 930 | 1.6 s |
| seed 2, bot seats | 1029 | 1.5 s |
| seed 3, human seats | 1335 | 1.9 s |
| seed 3, bot seats | 3410 | 3.5 s |
| seed 1, human seats, `rh_sim::observe` on all 8 seats every tick (+ no other work) | 1193 | 3.6 s (observing 0.9 s) |

About 1 ms a tick unobserved, about 3 ms a tick with all 8 seats observed every tick. Matches of 8 HumanBot seats end after
roughly 900 to 3400 ticks (everyone dead or one left), so the minute-5 (tick 6000) samples exist only for the longer matches;
the report counts samples per minute rather than assuming 8. Oracle size: seeds 1..4, `max_ticks` 2400 (never reached on
these seeds with human seats): about 3 matches x 5 distinct runs x 2 to 7 s in a debug build, well inside two minutes
(the test caches runs in `OnceLock`s).

## 4. New paths

- `crates/rh-headless/src/balance_lab.rs` (observer and aggregation; submodules under `src/balance_lab/` if it grows; the
  readability rule is 1,500 lines, 200 to 600 per file is the aim, and every `src/**/*.rs` starts with a `//!` header)
- `crates/rh-headless/src/bin/balance_lab.rs` (thin: `balance_lab::parse_args`, `run`, stdout JSON, stderr table; exit 2 on a usage error)
- `tools/balance-lab/README.md` (short: run, flags, how to read the report, caveats)
- `crates/rh-headless/tests/balance_lab.rs` (oracle, below)

## 5. Exclusive write scope

- the new files above, plus the oracle's own file, which is read-only for the implementer
- `crates/rh-headless/src/lib.rs`: one `pub mod balance_lab;` line (shared file, merges one at a time; the stub commit already adds it)
- `crates/rh-headless/Cargo.toml`: **no edit needed.** `src/bin/*.rs` is auto-discovered (`camp_bench`, `tournament` are not
  listed either; only `rh-headless` and `scalebench` are `[[bin]]`). If the implementer finds a reason, it is a shared file: say so.
- `tools/README.md`: add `balance-lab/` to the existing "Benchmarks and measurements" row (line 38) if the readability rule asks
  (`python3 scripts/readability_check.py`)
- `crates/rh-headless/README.md`: one line for `src/balance_lab.rs`, `src/bin/balance_lab.rs` beside lines 20-23
- No change in `rh-sim` or `rh-policy`. No `Cargo.lock` change (no new dependency; the coordinator owns `Cargo.lock` anyway).
- **No feature.** The elite bench needs `elite-bench` for `rh_sim::trusted_build` (`Cargo.toml:30`, `camp_bench.rs:23-25`); this
  lab applies no trusted build, reads only public items (`Sim::{players,mobs,camp_rosters,arena,state_hash}`, `rh_sim::observe`,
  `cards::native::readouts`, `Event`), so the bin and the oracle build in the ordinary gate (`cargo test --workspace`,
  debug). `scripts/gate.sh` is not edited and the release-only list (`gate.sh:161`) is not extended.

## 6. Contracts and references

- C-INPUT: the lab submits nothing itself (`env.advance(&[])`; the seats' policies submit through `validate` + `Sim::submit`).
- C-OBS: the lab reads observations as each seat sees them; the one thing it reads beyond that is `&Sim` for build natives,
  farm stacks, mobs and camps, which is allowed because it is a server-side tool, never shipped to a client. Do not add it to
  `rh-server`.
- C-RNG: nothing is drawn. `rh-sim` unchanged, so no hash, checkpoint, wire or observation version moves.
- `crates/rh-headless/AGENTS.md`: same seed and inputs give the same state hash on every platform.
- `docs/agent/READABILITY.md`: headers, README line, size.

## 7. Behaviour to preserve

- Observer purity: the final `Sim::state_hash()` of a watched seed equals an unwatched plain `Env` run of the same setup.
- The report is a pure function of (options, content): no wall clock, no thread id, no path, no `HashMap` iteration order.
  Per-seed records are folded in seed order, never in finishing order; `--jobs` only changes speed.
- `elite_bench`, `camp_bench`, `run_bot_match_kind` and every existing test are untouched.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-BAL-LAB-1` passes (the full gate is the coordinator's).
- `cargo test -p rh-headless --test balance_lab` passes in a debug build in about two minutes or less.
- `cargo run --release -p rh-headless --bin balance_lab -- --seeds 1..3 --jobs 2 > a.json` twice gives equal bytes (checked by the oracle through the library).
- `python3 scripts/readability_check.py` clean.

### Report shape the oracle reads (and a contract for later packets)

Compact `serde_json::to_string` of derived-`Serialize` structs (so the key order is the declaration order; a `json!` map
would sort keys). Top-level keys, in this order, none reused as a nested key name:
`schema` (`"balance-lab/1"`), `content_hash` (16 hex digits), `seeds`, `seating`, `maps`, `world`, `elites`, `quests`,
`rewards`, `augments`. The oracle requires at least:

```
seeds:   [{ "seed": u64, "ticks": u32, "terminated": bool, "truncated": bool, "state_hash": "16 hex" }]   // seed order
seating: [{ "seat": 0..7, "policy": "human"|"brawler", "bot": bool }]                                     // 8 entries
maps:    [{ "seed": u64, "map_hash": "non-empty string" }]                                               // the ADMITTED map
world:   { "camps_cleared": n, "player_deaths": { "total": n, "by_cause": { "player": n, "elite": n, "mob": n, "hazard": n } } }
quests:  { "by_quest": [{ "quest": str, "dealt": n, "accepted": n, "attempts": n, "completed": n,
                          "ended_timer": n, "ended_death": n, "abandoned": n }] }
augments:{ "by_card": [{ "card": str, "offered": n, "picked": n, "held_by_winner": n }] }
```

`elites`, `rewards` and every other field (per-minute stacks, camp seconds, native growth, spell casts) are the implementer's
design, documented in `tools/balance-lab/README.md`; extra keys are allowed anywhere.

## Oracle files (read-only for implementers)

- `crates/rh-headless/tests/balance_lab.rs`

(The `stub:` commit holds `crates/rh-headless/src/balance_lab.rs`, `crates/rh-headless/src/bin/balance_lab.rs` and the
`pub mod` line; the implementer replaces the stub bodies. `parse_args`, `match_setup`, `Options` and the type names below are
the seam the oracle calls and are fixed.)

### The API the oracle calls (declared by the stub)

```rust
pub const SCHEMA: &str = "balance-lab/1";
pub enum SeatPolicy { Brawler, Human }                       // --seat I=brawler|human
pub enum MapChoice { Generate, File(String) }               // --map generate | PATH
pub struct Options { pub seeds: Range<u64>, pub jobs: usize, pub max_ticks: u32, pub map: MapChoice,
                     pub seats: Vec<(usize, SeatPolicy)>, pub human_seats: bool }   // Default: 1..2, 1, 24_000, Generate, [], false; unlisted seats play Human
pub fn parse_args(args: &[String]) -> Result<Options, String>   // seeds are half-open A..B; --seeds 1..4 is seeds 1, 2, 3
pub fn match_setup(opts: &Options, seed: u64) -> Result<(EnvConfig, Vec<AgentSpec>), String>   // the config + seats of one seed
pub struct SeedRun { pub seed: u64, pub ticks: u32, pub terminated: bool, pub truncated: bool, pub state_hash: u64 }
pub fn play_seed(opts: &Options, seed: u64) -> Result<SeedRun, String>   // observed play of one seed
pub struct Output { pub json: String, pub table: String }
pub fn run(opts: &Options) -> Result<Output, String>                    // all seeds, `opts.jobs` threads
```

`match_setup` is real in the stub (it is the shared config the purity test needs: the oracle builds its unwatched `Env` from
it); `play_seed` and `run` are stubs. The implementer may change `match_setup`'s internals but not its result for a given
`(opts, seed)`.

## 9. Validation commands

- `export PATH="$HOME/.cargo/bin:$PATH"; cargo test -p rh-headless --test balance_lab`
- `cargo run --release -p rh-headless --bin balance_lab -- --seeds 1..9 --jobs 4 --human-seats > report.json 2> table.txt`
- `scripts/gate.sh --quick --oracle packet/P-BAL-LAB-1`

## 10. Risk and suggested model tier

implementer-sonnet: one new tool in a leaf crate, no `rh-sim` edit, a clear failing oracle. Hard parts: the observation
state machine for quests (flips between samples), the camp-credit heuristic, and keeping observation cost down (sample
every tick at about 3 ms a tick for 8 seats; the implementer may sample every 2 ticks only if no metric depends on a
single tick, and must say so). Exact quest and chest numbers on a seed are not pinned: that is deliberate (loose proof).

## 11. Known traps

- The obvious `serde_json::json!` produces sorted keys; the schema wants declaration order.
- `Observation.build` has no native cards (see the augments row); using it makes every native card look never held.
- Do not put `--human-seats` behaviour in `SimConfig`: it is `AgentSpec.bot`, which `Env::new` passes to `Sim::add_player`.
- Observing an ended match returns empty quest rows: do not read a `Done` row's disappearance as "abandoned".
- `Event::CampCleared` has only the camp index; mobs leave `sim.mobs()` quickly, so credit must use a remembered map.
- Do not use `HashMap`/`HashSet` in the report path (output order); `BTreeMap` only. The lab is not under the `rh-sim` clippy
  rule, but byte identity depends on it.
- A thread that panics inside `std::thread::scope` must turn into an `Err` for the seed, not an output that differs by `--jobs`.
- The seat flag grammar: `--seat 3=human`, repeat for several seats; a seat index outside 0..8 or an unknown policy is a usage
  error; `RaidBot` is not a policy name yet.

## Existing tests that pin what this packet changes

None. The packet adds files and one `pub mod` line; it changes no constant, no `rh-sim` behaviour and no existing test. Searched:
`rg "balance_lab|balance-lab" crates tools scripts docs/agent` finds nothing before this packet.

## Notes from previous attempts
