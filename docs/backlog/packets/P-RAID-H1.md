# P-RAID-H1: `RaidBot` can be seated in headless human seats, and the balance lab runs it

- **Serves:** not in the plan: owner approval 2026-11-22 (`docs/backlog/BALANCE_LAB_PATCH.md` section 1 "Headless" and its
  wave-2 row P-RAID-H1; owner decision row at the top of `docs/OWNER_DECISIONS.md`: "Lab now, live later")
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `feature/unity-rework` @ `c92d606e9`
- **D-rules touched:** D1 loose proof (owner direction 2026-11-10; the behaviour is the headless harness, no legacy
  counterpart) · D2 n/a · D5/D6/D9 none (no `rh-sim`, `rh-policy`, wire or observation change; D9: the seats still act only
  through `validate` + `Sim::submit`)

## 1. ID and outcome

`RaidBot` (merged by P-RAID-0) can be seated by `rh-headless`, in human seats, and `balance_lab` can run it. After this
packet `balance_lab --human-seats --seat 0=raid ... --seat 7=raid --seeds 1..4` plays eight RaidBots through a real match
(lobby, planning, active). Concretely: `BotKind::Raid` (parsed from `"raid"`, building `RaidBot::new(tier, persona)` with the
tier and persona `Human` draws from `assign_seats`), a way to seat a bot-only match with `bot: false` seats beside
`run_bot_match_kind`, and `raid` as a lab seat policy (flag, seating block, README). Not here: the headless CLI
(`src/main.rs` has no policy flag; leave it), `rh-server`, any change in `rh-policy` or `rh-sim`.

## 2. Dependencies

P-RAID-0 (merged: `crates/rh-policy/src/bots/raid/`, `RaidBot` re-exported at `bots/mod.rs:82`) and P-BAL-LAB-1 (merged:
`balance_lab`). Both are in the base.

## 3. Relevant legacy paths

None: `RaidBot` and the lab are new Rust-side tools. Existing Rust anchors (all at `c92d606e9`):
- `crates/rh-headless/src/lib.rs:549-595` `BotKind` (`Brawler | Human | Brain`), `parse` (`:562`), `policy` (`:572`: `Human`
  and `Brain` share one `assign_seats(seed, players)[i]` draw, then build `HumanBot` or `BrainBot`).
- `crates/rh-headless/src/lib.rs:599-675` `run_bot_match_config` (fixes `Brawler`) -> `run_bot_match_kind` (`:622`), which
  builds `AgentSpec { bot: true, .. }` at `:635`.
- `crates/rh-headless/src/balance_lab.rs`: `SeatPolicy { Brawler, Human }` (`:20`), `parse_args` (own match on the policy
  text, `:121-125`), `match_setup` (maps `SeatPolicy` -> `BotKind` -> `kind.policy`, `bot: !opts.human_seats`, `:140-170`),
  `seat_policy` (`:290`) and the seating label in `run` (`:313-317`).

## Established facts

1. **Callers.**
   - `run_bot_match_kind`: `run_bot_match_config` (lib) and `tests/tick_ms_bots.rs:16` (kinds `Brawler` and `Human`, pins
     decisions per bot-second at 25 and 50 ms). Nothing else outside `lib.rs` calls it.
   - `run_bot_match_config` -> `Brawler`: `src/main.rs:152` (the CLI, the gate's release smoke on `seed1.map.json` and the
     determinism sweep path). `run_bot_match` / `run_bot_match_with` (15 callers across `tests/` and bins, all `Brawler`,
     `bot: true`, many pin state hashes or digests: `tests/bots.rs`, `tests/determinism_sweep.rs`, the digest guards).
   - `BotKind::policy`: `balance_lab.rs:159`, `tests/{brain_bots,frost_wall_bots,frost_wall_live}.rs` (their own agent
     lists, `bot` per test), `tests/rework_at_25ms.rs:113`. `BotKind::parse`: no caller outside `lib.rs` (definition only).
   - So `bot: true` in `run_bot_match_kind` is pinned by every hash above: the new entry point sits beside it and
     `run_bot_match_kind` delegates to it with `true`.
2. **How the lab seats.** It does not delegate to `BotKind::parse`: `parse_args` matches `"human"` and `"brawler"` itself
   (`balance_lab.rs:121-125`, error text `(human|brawler)`), `SeatPolicy` has two variants, `match_setup` maps them to
   `BotKind` and calls `kind.policy`, `bot: !opts.human_seats`. So `raid` does **not** reach the lab without a change:
   new `SeatPolicy::Raid`, a `"raid"` arm in `parse_args`, the `BotKind::Raid` mapping, and a `"raid"` label in `run`'s
   seating block (`:313`). `match_setup`'s result for existing policies must not change.
3. **A `RaidBot` in the real lifecycle (probe, uncommitted; eight `RaidBot::new(tier, persona)` with the `assign_seats(seed, 8)`
   draw in the lab's setup: `ArenaMode::Source` via `SimConfig::live_content()`, `min_players 8`, `lobby 5 s`, `fill 3 s`,
   generated map per seed, `bot: false`, `max_ticks` 6000, debug build, a loaded machine).**

   | | seed 1 | seed 2 | seed 3 |
   |---|---|---|---|
   | phase change | Planning at tick 1, Active at 161 | Planning 1, Active 145 | Planning 1, Active 162 |
   | ended by | truncation at 6000 (nobody won) | 6000 | 6000 |
   | camps cleared (6000 ticks) | 14 | 14 | 19 |
   | camps cleared by tick 900 / 2400 | 2 / 6 | 6 / 9 | 4 / 9 |
   | player deaths, whole run | 0 | 3 | 5 |
   | player deaths in minute 0 (ticks 1-1200) | 0 | 1 | 1 |
   | seats with a fault | 0 | 0 | 0 |
   | `fallback_decisions` / `rejected` (all seats) | 0 / 0 | 0 / 0 | 0 / 0 |
   | proposals per seat over 6000 ticks | 247-401 | 64-521 | 55-396 |
   | wall time, debug | 22 s | 19 s | 9 s |

   Every match reaches `Active`; planning is answered (the 8 seats leave `Planning` after 145-162 ticks, i.e. when the
   lobby timers say; telemetry `choose` is 0-3 per seat, `plan` is 0: `PlanModule` produced no fault and no rejected
   command). No seat faults or falls back. Seats do not sit still (move and attack counts in the hundreds), but proposals
   differ five- to ten-fold between seats (seed 3, seat 8: 55 proposals, 17 moves in 6000 ticks): the quietest seats are
   not stuck by a fault, only slow, and this packet does not judge it. **No fault or stall was found: no finding to
   surface.** Camps cleared per match is a first look at RaidBot-in-a-match, not a pinned number. The matches do not end
   in 6000 ticks (5 minutes), so the lab's later-minute samples exist (a HumanBot match ends in 900-3400 ticks).
4. **What the oracle can read.** `Env::seat_stats()` (`lib.rs:504`) returns `(PlayerId, SeatStats { decisions, proposed,
   accepted, rejected, fallback_decisions }, Option<Fault>)` per seat; a fault or a fallback decision is visible there, and a
   command the sim refuses is `rejected` (`rh-policy/src/lib.rs:337-343`). `Env::seat_telemetry()` (`:487`) gives
   `PolicyStats.commands` by name (`move`, `attack`, `choose`, `plan`, ...). `Env.sim.players()` gives each `Player.seat.bot`
   (`state.rs:610`), `env.sim.phase()` the lifecycle phase, `Event::CampCleared` the clears. All of these exist; no probe is
   needed beyond them.

## 4. New paths

- `crates/rh-headless/tests/raid_seats.rs` (oracle)

## 5. Exclusive write scope

- `crates/rh-headless/src/lib.rs`: **shared file, merges one at a time.** `BotKind::Raid` arms (`parse`, `policy`),
  and `run_bot_match_seated` (the stub adds the function and the `Raid` variant; the implementer makes `bot_seats: false`
  seat `bot: false` and builds `RaidBot::new(tier, persona)`).
- `crates/rh-headless/src/balance_lab.rs`: only the seat policy: the `SeatPolicy::Raid` variant arms, the `"raid"` arm in
  `parse_args` (and its error text), `match_setup`'s `BotKind` mapping, the seating label in `run`.
- `tools/balance-lab/README.md`: the `--seat I=human|brawler|raid` flag text and a RaidBot command line
  (`--human-seats --seat 0=raid ... --seat 7=raid`); say that `raid` seats are meant for `--human-seats` (a `bot: true`
  seat has no quest view).
- `crates/rh-headless/README.md`: one line only if `python3 scripts/readability_check.py` asks.
- Nothing else. No `rh-policy`, `rh-sim`, `Cargo.toml`, `Cargo.lock`, `src/main.rs` or `scripts/gate.sh`; the oracle is not
  release-only (see section 8).

## 6. Contracts and references

C-INPUT (seats act through `validate` + `Sim::submit`), C-POLICY; `crates/rh-headless/AGENTS.md` (same seed and inputs, same
state hash); `docs/agent/READABILITY.md`.

## 7. Behaviour to preserve

- `Brawler`, `Human`, `Brain` seats, `BotKind::policy` for them, and `run_bot_match_kind` (still every seat `bot: true`):
  every existing `rh-headless` test and digest (`tests/bots.rs`, `tests/determinism_sweep.rs`, `tests/balance_lab.rs`,
  `tests/tick_ms_bots.rs`, `tests/brain_bots.rs`, the smoke run) stays green and byte-identical.
- `match_setup(opts, seed)` for `brawler` and `human` policies: same agents, same config.
- No RNG draw added to any existing policy; `Raid` uses the same `assign_seats` draw as `Human` (not a second draw).

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-RAID-H1` passes.
- `cargo test -p rh-headless --test raid_seats` passes in a debug build (about one minute; capped by `max_ticks`).
- `cargo test -p rh-headless --test balance_lab` still passes with the amended expectation below.
- `python3 scripts/readability_check.py` clean.

## Existing tests that pin what this packet changes

| Test | Asserts | Verdict |
|---|---|---|
| `crates/rh-headless/tests/balance_lab.rs`, `the command line parse and refusals` (loop at ~`:290-300`) | `parse_args(["--seat","1=raid"])` is a usage error ("`RaidBot` is not a policy name yet", P-BAL-LAB-1 section 11) | **Conflict, amended** by the `oracle-amend:` commit: the entry becomes `["--seat","1=nope"]` (still a refusal of an unknown policy). Owner approval needed; nothing else in the file changes |
| `tests/tick_ms_bots.rs` | decisions per bot-second for `Brawler`, `Human` via `run_bot_match_kind` | unaffected: `run_bot_match_kind` unchanged in behaviour |
| `tests/bots.rs`, `tests/determinism_sweep.rs`, digest guards | `Brawler` state hashes through `run_bot_match*` | unaffected |
| `tests/brain_bots.rs`, `frost_wall_*` | `BotKind::{Human, Brain}.policy(..)` | unaffected: `policy` arms for them unchanged |

## Oracle files (read-only for implementers)

- `crates/rh-headless/tests/raid_seats.rs`
- `crates/rh-headless/tests/balance_lab.rs` (the one amended line, `oracle-amend:` commit)
- `docs/backlog/packets/P-RAID-H1.md`

What `raid_seats.rs` asserts (seeds 1-3, the lab's setup, `max_ticks` 2400 unless noted):
1. `BotKind::parse("raid") == Some(BotKind::Raid)`; `brawler`, `human`, `brain` still parse; `nope` does not.
2. Control (green on the stub): `run_bot_match_kind` seats every player `bot: true` for `Brawler`, `Human` and `Raid`, and
   `run_bot_match_seated(.., bot_seats: true)` ends in the same state hash as `run_bot_match_kind`.
3. Eight `RaidBot` in human seats, through `balance_lab::match_setup` with `human_seats` and `--seat 0..8=raid`: every seat
   `bot == false`; the match reaches `Active`; no seat faults, `fallback_decisions == 0`, `rejected == 0`, every seat proposed
   something; at least 6 camps cleared over the three seeds (measured 24 by tick 2400); the same seed twice gives the same
   state hash and the same seat stats. The seats really are `RaidBot` with `Human`'s draw: the final state hash at tick 900
   equals the one of a hand-built run of eight `RaidBot::new(tier, persona)` from `assign_seats`, and equals
   `run_bot_match_seated(.., Raid, bot_seats: false)` on the same arena and config.
4. The lab: `parse_args` accepts `--seat 0=raid` ... `7=raid`, `run` reports `"policy":"raid"` and `"bot":false` for all eight
   seats and `world.camps_cleared >= 1` over seed 1 (1200 ticks); an unknown policy is still refused.

## 9. Validation commands

- `export PATH="$HOME/.cargo/bin:$PATH"; cargo test -p rh-headless --test raid_seats --test balance_lab`
- `scripts/gate.sh --quick --oracle packet/P-RAID-H1`

## 10. Risk and suggested model tier

implementer-sonnet: three small edits in one leaf crate with a clear oracle. The one care: the new entry point must not
change `run_bot_match_kind` (its `bot: true` is pinned by every Brawler hash).

## 11. Known traps

- `bot: false` seats count toward `min_players`; a bot-only match with `min_players` 8 and `bot: true` seats waits for the full
  `lobby_seconds` (P-BAL-LAB-1 facts). `run_bot_match_seated` takes the config as given and does not set `min_players`.
- `lib.rs` is shared with other headless packets: merge one at a time.
- `tools/balance-lab/README.md` currently says "`--seat I=human|brawler`"; keep its other text.
- `RaidBot` on a `bot: true` seat has no quest view (`quest_view/mod.rs:203`): it farms only. The lab's README should say so.
- Probe outcome in fact 3 is a single-machine, debug-build measurement of RaidBot on three seeds: not a statement about its
  quality.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
