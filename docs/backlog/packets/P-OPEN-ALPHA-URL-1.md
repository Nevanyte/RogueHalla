# P-OPEN-ALPHA-URL-1 — Clear the alpha dependency advisory

## ID and outcome

P-OPEN-ALPHA-URL-1 serves Phase 4, item 19, and Phase 5, item 21. The Linux PostgreSQL server dependency graph must pass the RustSec advisory check for R7 before public alpha.

## Dependencies and references

Candidate game `968126954` with handoff `700e73f88`. The parked P-CI-ADVISORIES-1 policy at `b068e94a3` already specifies the minimal coordinator update. RustSec RUSTSEC-2026-0592 is fixed by `url` 2.5.9.

## Relevant legacy paths

None. This is a Rust dependency maintenance change.

## Exclusive write scope

This packet brief and the coordinator-owned `Cargo.lock` only. Preserve the existing locked dependency graph apart from `url` and the decoder dependency updated by its patch.

## Behaviour to preserve

Preserve the PostgreSQL connection and authenticated account flow, simulation parity, wire format and content identity. The advisory scan does not establish reachability of the vulnerable function through player input.

## Failing oracle and acceptance

Use the parked `deny.toml` outside the worktree and cargo-deny 0.22.1. On the unpatched candidate, the resolved workspace must report RUSTSEC-2026-0592 for `url` 2.5.8. After the patch, advisory, bans and source checks must all pass under that policy. It records the existing Chrono maintenance exception and checks the build graph rather than unused lock entries. Verify normal PostgreSQL-backed account API use and the coordinator's final release gate.

## Validation

```sh
cargo update -p url --precise 2.5.9
cargo deny --all-features --locked --config /path/to/parked-deny.toml check advisories bans sources
cargo test --locked -p rh-server --features postgres --test account_api
```

Use the project pgtest helper for the account suite. D1 and D2 are inapplicable to this dependency update. D6, D9 and the legacy oracles are preserved.
