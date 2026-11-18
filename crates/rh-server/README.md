# rh-server: the live match server

The authoritative server. tokio and axum serve WebSocket (WSS) connections; each match is a room, one tokio task that
owns its `Sim`, its seats and its pacer. The server is a thin adapter between sockets, the wall clock and hosting on one
side and the deterministic core (`rh-sim`) on the other. Client input is untrusted: decode, `validate`, then
`Sim::submit`. Admission refuses a protocol or content-hash mismatch.

## Where it sits

It calls `rh-sim`, `rh-protocol`, `rh-policy` (fill bots), `rh-headless` (arena loading), `rh-mapdress` (map dressing)
and, with the `postgres` feature, `rh-persist`. Its clients are the Unity build and `rh-load`.

## Entry points

- `src/main.rs`: the command line; the file header lists every flag.
- `src/lib.rs`: the module list.
- `src/director.rs`: admission, room placement, session resume, and off-tick handling of results.
- `src/room.rs` (and `src/room/`): one match task. `src/pacer.rs` is its fixed-step clock.
- `src/conn.rs`, `src/outbox.rs`, `src/wire.rs`: the per-connection path (frame guards, slow-client isolation,
  sim-to-wire conversion).
- `src/lobby.rs`, `src/bot_fill.rs`, `src/map_pool.rs`: lobby rules, bot fill, map making and dressing.
- `src/control.rs`: the loopback-only `/control/*` API. `src/api.rs` and `src/api/`: the account API.
- `src/bin/wirebench.rs`, `src/bin/densitybench.rs`: benchmarks (need `--features bench`). `web/probe.html`: the browser probe.

## Run and test

```sh
cargo run --release -p rh-server -- --bind 127.0.0.1:7777 --self-signed --control 127.0.0.1:7778
cargo test -p rh-server
RH_SKIP_PG_TESTS=1 cargo test -p rh-server                     # what the gate sets when there is no database
DATABASE_URL=... cargo test -p rh-server --features postgres   # adds pg_e2e, lease_conformance and the account flows
```

Features: `postgres` (durable results and the account API), `bench`.

## Rules

Local rules: [AGENTS.md](AGENTS.md). Admission, auth, the account API and the control surface are security work.
Persistence stays off the tick.
