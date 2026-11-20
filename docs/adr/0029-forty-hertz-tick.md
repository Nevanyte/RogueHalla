# ADR0029: A 40 Hz live tick, with 20 Hz kept for legacy and parity

Status: **Accepted** by the owner 2026-11-19 ("okay for all 3": accept the ADR, sign DEV-039, push). Owner direction 2026-11-19: "lets do 40, league works because the auto attacks and spells stop
character movement saving the need to calculate moving attacks and direction unlike our game, 40 tick should be fine"
(project thread, after the assessment "Faster server tick: cost and recommendation"). The owner weighed 80 and 120 Hz
and declined them. Packets P-TICK-0 to P-TICK-6. Deviation DEV-039, signed by the owner 2026-11-19.

**Per-operation checklist.** D1: parity stays byte-exact at 50 ms; every existing capture and golden vector is replayed
unchanged. Loose proof (owner direction 2026-11-10) for the 25 ms behaviour: walk distance per second and weapon timings
at 25 ms match 50 ms within one tick. D2: n/a (no balance number moves). D5: not new game design; a change of the step
length that the owner approved as a responsiveness fix. D9: unchanged (the server stays authoritative; the client's walk
prediction reads the rate instead of assuming it). Conflicts: C-TICK (Accepted, FROZEN) names 20 Hz and +50 ms per step,
and D10 freezes Rust outside named changes. The owner's 2026-11-19 direction is newer than both; this ADR records the
amendment, and C-TICK's row was amended on acceptance.

## Context

Every server-confirmed result waits for the next step: 0 to 50 ms, 25 ms on average, at 20 Hz
(`docs/rework/CONTROLS_AUDIT.md` row 7). The client also draws remote players at least one tick behind
(`client-unity/RoguehallaNet/Net/RenderClock.cs`, target delay `TickMs + jitter`). The game has moving attacks and aimed
projectiles, so the tick is felt more than in games where attacking roots the character.

The step length is a compile-time constant today: `TICK_RATE = 20`, `TICK_MS = 50` in `crates/rh-sim/src/constants.rs`,
used about 215 times in `rh-sim`, and again as `TICK_MS: u32 = 50` in `crates/rh-protocol/src/lib.rs` (snapshot server
time is `tick * 50`), and as `TickMs = 50` in the C# library (`Codec.cs`, `LocalMotion.cs`, `Vision.cs`). Legacy runs at
20 Hz (`packages/shared/src/constants.ts:11`) and about 70 fixture families under `parity/` were captured tick by tick
at 50 ms; `crates/rh-parity/src/lib.rs` refuses a capture whose `tickMs` differs from the sim's.

Rates whose step is not a whole millisecond are ruled out: sim time advances by `server_time += TICK_MS`
(`crates/rh-sim/src/sim.rs`) and is checked by exact equality with `tick * TICK_MS` and by `% TICK_MS == 0`
(`systems/arena_validation.rs`, `abilities/checkpoint.rs`, `cards/fight_clock.rs`). With 1000/30 or 1000/60 the float
sum leaves `tick * TICK_MS` on the third tick. 25 ms and 50 ms are exact, and 50 is a multiple of 25, so legacy timers
that are multiples of 50 ms land on both grids (56 of the 58 literal millisecond constants are).

## Decision

1. **The step length is a per-sim setting.** `SimConfig::tick_ms: u32`, serde default 50, allowed values 50 and 25 only
   (anything else is refused at construction). It reaches save, restore and the state hash. The compile-time
   `TICK_MS`/`TICK_RATE` constants become `LEGACY_TICK_MS`/`LEGACY_TICK_RATE`, used only as the default and by tools that
   speak about legacy; every sim system reads the sim's own value.
2. **Parity, captures and existing fixtures run at 50 ms**, unchanged. No capture is regenerated; no golden vector moves.
3. **Rules counted in ticks keep their wall-clock time** (owner decision 2 in the assessment): `WALK_HINT_MAX_TICKS` 3
   (150 ms), `MOB_SPACING_LOOK_TICKS` 4 (200 ms), bot `decision_interval_ticks`, and the delta limits `MAX_DELTA_AGE` 64
   and `MIN_FORCED_FULL_TICKS` 20 are restated in milliseconds and converted with the sim's rate. At 50 ms each converts
   to today's tick count exactly. `MAX_ACTIONS_PER_TICK` stays 6 per step; the order budget (15 per second, sim time)
   is the real limit. `FESTERING_TICKS` already counts its own 1000 ms period and does not change.
4. **The tick order (the schedule) does not change.** Only the length of a step does.
5. **Wire: protocol 19.** Snapshot server time is `tick * tick_ms`, where `tick_ms = 1000 / Welcome.tick_rate` (Welcome
   already carries `tick_rate: u8`). The Rust, C# and JS decoders read it; no decoder assumes 50.
6. **A snapshot is published after every step** (40 per second at 25 ms). Bandwidth has room: the measured heavy case is
   1.79 KB/s per client against a 20 KB/s budget (`docs/PLAYER_PREDICTION.md`).
7. **The client reads the rate** from Welcome for walk prediction, attack-pause prediction, vision timing and the render
   clock. Walk and ranged-pause vectors are generated at both rates.
8. **The server picks the rate per room** from a setting whose default stays 50 until P-TICK-6 measures 25 ms on full
   maps and flips it. Rolling back is a setting change.

## C-TICK text (applied on acceptance, 2026-11-19)

"Fixed step of `SimConfig::tick_ms` (50 or 25 ms; 50 for legacy, parity and every capture); sim time +`tick_ms` per
step; fixed system order, held as an executable schedule checked against the legacy record."

## Consequences

- About twice the CPU per live room: full-map rooms per core fall from about 28 to 65 to about 14 to 32
  (`docs/evidence/full-map-cost/README.md`, scaled). Re-measured in P-TICK-6 before the flip.
- Replays and checkpoints record their `tick_ms`; one recorded at 50 replays only at 50.
- Two rates must stay green: CI runs parity at 50 and the new 25 ms tests beside it.
- A client of protocol 18 cannot join a protocol 19 server (the existing version refusal).

## Rejected

- **30 and 60 Hz:** not whole milliseconds (above); 60 Hz also triples CPU and leaves little room for wake lateness
  (p99 6.1 ms, `docs/hosting/LOAD_BASELINE.md`, against a 16.7 ms step).
- **80 and 120 Hz:** considered and declined by the owner (2026-11-19).
- **A Cargo feature for the rate:** feature unification would switch the whole workspace, parity included, in one build.
- **Re-capturing parity at 40 Hz:** legacy cannot run at 40 Hz without editing `packages/`, and C-PARITY keeps the
  captures as they are.
