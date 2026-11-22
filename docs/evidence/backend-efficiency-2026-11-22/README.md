# Room ticks on the shared tokio workers: measured, no dedicated runtime proposed (2026-11-22)

Report only (audit finding 7 of `docs/backlog/BALANCE_LAB_PATCH.md`; the coordinator's brief: "measure `step_to_conn` and
`step_to_written` from `/metrics` under `rh-load` before proposing a dedicated runtime; do not build it"). Nothing was
changed.

## The question

Each room is a tokio task (`crates/rh-server/src/director/spawn.rs:201`, `tokio::spawn(run_room(..))`) whose step runs
synchronously inside the task (`crates/rh-server/src/room/task.rs:55-97`). Connection writers are tasks on the same
multi-threaded runtime (`#[tokio::main]`, one worker per core). If steps occupied the workers, a connection writer would
wait behind them, and `step_to_conn_us` (step start to the writer taking the batch, `crates/rh-server/src/metrics.rs:233-238`,
recorded at `conn.rs:266`) would grow with the number of rooms faster than the steps themselves do.

## Setup

- Revision `83edb687f` (integration head), `cargo build --release --locked` (the current profile: `debug = 1`, no LTO).
- Apple M5 Pro, 18 cores, 64 GB, macOS 26, native arm64; loopback WebSockets; `rh-load` and `rh-server` on one host.
- **The host was heavily shared**: load average 80-90 throughout (another session's full gate and implementers, a game
  client). Absolute tails here include OS scheduling delay of an oversubscribed machine.
- `rh-load sweep --levels 4,8,16 --players-per-match 8 --duration 30 --ramp-per-sec 4`, a fresh server per level:
  - `base`: the slice on `parity/maps/seed1.map.json`, movement-only clients (aggression 0);
  - `fp`: the staging configuration `--first-playable` (`hosting/staging-mvp/bootstrap.sh.in:114`: embedded map,
    fill bots 4), aggression 0.5.
- Histograms are the server's own (`/control/health` metrics, final, whole run); quantiles are bucket upper bounds (at
  most 25% above the true value), max is exact. Raw reports: `rh-load` JSON per level (scratch, not committed).

## Results

| Config | Rooms | Server CPU (cores) | step µs p50/p99/max | wake lateness µs p50/p99/max | step→conn µs p50/p99/max | step→written µs p50/p99/max | client snapshot interval p99 / max ms | verdict |
|---|---|---|---|---|---|---|---|---|
| base | 4 | 0.05 | 55 / 223 / 1 460 | 1 279 / 3 583 / 7 121 | 159 / 511 / 3 498 | 159 / 511 / 3 501 | 27 / 33 | healthy |
| base | 8 | 0.09 | 55 / 255 / 1 557 | 1 535 / 4 095 / 8 679 | 159 / 895 / 4 588 | 191 / 895 / 4 593 | 29 / 40 | healthy |
| base | 16 | 0.16 | 55 / 223 / 22 602 | 1 279 / 3 583 / 15 994 | 159 / 767 / 22 861 | 159 / 767 / 22 866 | 29 / 66 | healthy (8 ticks missed) |
| fp | 5.4 | 0.08 | 95 / 319 / 2 894 | 1 535 / 4 095 / 11 810 | 255 / 767 / 3 210 | 255 / 895 / 3 213 | 28 / 39 | healthy |
| fp | 10.6 | 0.15 | 95 / 383 / 2 983 | 1 279 / 3 583 / 18 977 | 255 / 1 023 / 6 790 | 319 / 1 023 / 6 795 | 28 / 46 | healthy |
| fp | 21.8 | 0.28 | 95 / 447 / 28 608 | 1 279 / 6 143 / 33 569 | 255 / 2 559 / 39 630 | 319 / 2 559 / 45 171 | 28 / 50 | healthy |

(The tick is 25 ms; the client interval p50 was 25 ms at every level.)

## Reading

1. **The rooms are nowhere near occupying the workers.** At 22 rooms in the staging configuration the whole server used
   0.28 of one core (step p50 95 µs, p99 447 µs) on an 18-worker runtime. Steps cannot be what a writer waits behind for
   milliseconds.
2. **The tail grows with load, and it tracks the scheduler, not the steps.** step→conn p99 goes 0.8 → 1.0 → 2.6 ms
   from 5 to 22 rooms, and its max (39.6 ms) sits beside a 28.6 ms step max and a 33.6 ms wake-lateness max in the same
   run: a thread of the process was descheduled, which on this host (load average about 90 on 18 cores) is the OS's
   doing. A dedicated runtime for rooms would share the same CPUs and the same scheduler.
3. **Nothing the player sees moved.** Client snapshot interval p99 stayed at 27-29 ms and no level was degraded by the
   harness's thresholds.

**Recommendation: do not build a dedicated room runtime now.** Re-measure where it could matter: on the deployed
instance class, whose runtime has as many workers as vCPUs (2 on the t4g.small of ORDER_OF_OPERATIONS item 18), read
`stepToConnUs` and `stepToWrittenUs` from `/control/health` during a playtest. Reconsider if step→conn p99 there
exceeds a few milliseconds while server CPU is well below the vCPU count, which is the signature of in-process sharing
that these runs did not show.

## Not established

- A quiet host. The absolute tails are inflated by the shared machine; the comparison across levels is the finding.
- Densities above 22 rooms, TLS, real networks, or the 2-vCPU instance.
- The worst step in the full rework world (`docs/backlog/BALANCE_LAB_PATCH.md` quotes 0.1-3.3 ms mean); the
  first-playable world measured here stepped at 95 µs p50.
