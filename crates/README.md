# crates

The Rust workspace (`members = ["crates/*"]` in the root `Cargo.toml`): the authoritative simulation, the wire format,
the live server, persistence, bots and the tools that prove them against the legacy game.

| Crate | Role |
|---|---|
| [`rh-sim`](rh-sim/README.md) | The game: pure, deterministic, fixed 20 Hz. Start here for any rule. |
| [`rh-protocol`](rh-protocol/README.md) | Binary wire format and the golden vectors shared with C# and JS. |
| [`rh-server`](rh-server/README.md) | The live WSS server: director, rooms, public HTTP, account API. |
| [`rh-persist`](rh-persist/README.md) | PostgreSQL profile store, session leases and match results. |
| [`rh-placement`](rh-placement/README.md) | Placement planner and durable request ledger; not deployed. |
| [`rh-policy`](rh-policy/README.md) | The policy contract and the bots. |
| [`rh-headless`](rh-headless/README.md) | Bot-vs-bot matches with no waiting; determinism sweep. |
| [`rh-parity`](rh-parity/README.md) | Replays legacy captures through the Rust core; the main oracle. |
| [`rh-load`](rh-load/README.md) | Load and soak harness for `rh-server`. |
| [`rh-mapdress`](rh-mapdress/README.md) | Cosmetic map dressing (props) from a rule file. |
| [`rh-alloc-count`](rh-alloc-count/README.md) | A counting allocator for allocation tests. |

Build and test everything with `cargo test --workspace` (the Postgres tests need `DATABASE_URL`, or set
`RH_SKIP_PG_TESTS=1`). The rules for working here are in [AGENTS.md](../AGENTS.md); `rh-sim` and `rh-policy` also reject
hash-ordered collections, the wall clock and platform libm (their `clippy.toml`). Do not edit `Cargo.lock` by hand.
