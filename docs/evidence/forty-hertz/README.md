# 40 Hz (25 ms step) measurements, P-TICK-6 (2026-11-20)

ADR 0029; R-OPT-2. Measured on the merged tree (`feature/unity-rework` @ 1dd79a93, PRs #72, #77, #78) in a 4-vCPU cloud
container, release builds, nothing else running. Same machine and build for both rates.

## Full-map step cost (`crates/rh-headless/examples/full_map_cost.rs`, 8 HumanBots, `TICK_MS=50|25`, 6000 steps max)

`Env::advance` per step (bot observations and decisions plus `Sim::step`), not sockets or encoding.

| map | step | mean µs | p50 µs | p99 µs | max µs | share of step budget | rooms per core at mean |
|---|---|---|---|---|---|---|---|
| first-playable 48x40 | 50 ms | 161 | 120 | 423 | 1,800 | 0.32% | 310 |
| first-playable 48x40 | 25 ms | 127 | 77 | 404 | 2,038 | 0.51% | 198 |
| generated-1 160x160 | 50 ms | 1,786 | 2,304 | 3,382 | 6,027 | 3.6% | 28 |
| generated-1 160x160 | 25 ms | 1,314 | 1,023 | 4,733 | 5,963 | 5.3% | 19 |
| generated-2 160x160 | 50 ms | 3,267 | 2,567 | 9,975 | 14,271 | 6.5% | 15 |
| generated-2 160x160 | 25 ms | 2,798 | 2,497 | 7,698 | 9,361 | 11.2% | 8.9 |
| generated-3 160x160 | 50 ms | 2,277 | 2,172 | 4,940 | 8,783 | 4.6% | 22 |
| generated-3 160x160 | 25 ms | 1,797 | 1,637 | 4,803 | 8,905 | 7.2% | 13.9 |

A step costs a little less at 25 ms (less movement per step), so CPU per second is about 1.6x, not 2x. Full-map rooms
per core fall from 15-28 to 9-19. Worst p99 at 25 ms is 7.7 ms against a 25 ms step: room for wake lateness (p99 6.1 ms,
`docs/hosting/LOAD_BASELINE.md`). Note the 6000-step cap is 150 s of match at 25 ms and 300 s at 50 ms, so the matches
end at different points.

## Server density bench (`densitybench --rooms 4 --humans 8 --secs 20 --tick-ms 50|25`, seed-1 map, paused clock)

| | 50 ms | 25 ms |
|---|---|---|
| steps (4 rooms x 20 s) | 1,600 | 3,200 |
| step p50 / p99 / max µs | 55 / 191 / 304 | 39 / 159 / 2,495 |
| dropped / late steps | 0 / 0 | 0 / 0 |
| bytes per human per step | 72.5 | 63.5 |
| bytes per human per second | 1,451 | 2,540 |
| CPU s per match-second (wall) | 0.00217 | 0.00379 |
| matches per vCPU (estimate) | 230 | 132 |

Bandwidth per client grows about 1.75x (2.5 KB/s typical here). The measured heavy case (1.79 KB/s at 20 Hz,
`docs/PLAYER_PREDICTION.md`) scales to about 3.1 KB/s, well under the 20 KB/s budget.

## Not measured

- Click-to-confirmed latency in the browser proof (`tools/latency`): needs a browser build of the Unity client and a
  running server; not available in this container. Expected change from the step alone: the average wait for the next
  step falls from 25 ms to 12.5 ms, the render floor from 55 to 30 ms.
- Real sockets, TLS and a cloud instance (the density bench has none of them).

## Verdict

Both gates in P-TICK-6 pass: step p99 well under 25 ms with room for lateness, heavy bandwidth under 20 KB/s.
