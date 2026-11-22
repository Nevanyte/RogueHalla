# P-BUILD-PROFILE-1: fat LTO, one codegen unit and mimalloc measured for the server; none adopted

- **Serves:** REQUIREMENTS R-OPT-1/R-OPT-2 and ORDER_OF_OPERATIONS phase 4, item 18 (cheap always-on host); owner
  direction 2026-11-22 (the backend audit he asked for, finding 3 of `docs/backlog/BALANCE_LAB_PATCH.md`), dispatched by
  the coordinator session to the backend-efficiency lane.
- **Implementer tier:** none (a measurement packet: the lane built and timed the variants; no source changes)
- **Reviewer:** reviewer
- **Integration base:** `feature/unity-rework` @ `83edb687f`
- **D-rules touched:** D1 n/a (codegen only; determinism checked below) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome

The root `Cargo.toml` release profile (`Cargo.toml:17-18`) is `debug = 1` only: no LTO, default codegen units, the
system allocator. The audit proposed `lto = "fat"`, `codegen-units = 1` and mimalloc as `rh-server`'s global allocator.
This packet measured all three against the current build (section "Measured") and **adopts none of them**:

- **Server CPU under load did not move.** At 16 matches x 8 humans in the staging configuration the server used 0.219
  cores (baseline), 0.221 (LTO) and 0.218 (LTO + mimalloc), inside a run-to-run spread of 0.206-0.231.
- **LTO and one codegen unit** retire 1.8-2.7% fewer instructions on the sim and the replication benchmarks and cut
  the stripped server binary by 24%. Every release build becomes several times slower: 7 min 40 s against 1 min 20 s
  for the same three packages here. The gate builds release binaries (`scripts/gate.sh:156-162`), and so does CI
  (`determinism.yml:76` builds every release test binary of three crates). R-OPT-2: complexity without demonstrated
  value is removed.
- **mimalloc** shows no gain beyond LTO's: 0.7 points of instructions on the replication benchmark, nothing on server
  CPU. Peak RSS under load rose from 46 to 63 MB. It would add a C dependency and a `Cargo.lock` change.
- **Determinism held** in every variant: `determinism_sweep ci` wrote byte-identical `manifest.json` and `replay.json`,
  and the 20-match headless state-hash fold was identical (`2d27e224062d1176`).
- `panic = "abort"` was never tried. The room crash guard (`crates/rh-server/src/room/task.rs:9`) and the bot policy
  guards (`crates/rh-policy/src/lib.rs:424,493`) depend on `catch_unwind`.

**Option for the owner (not built):** if the smaller image or the 2-3% fewer sim instructions are wanted for the
shipped binary alone, add `[profile.dist]` (`inherits = "release"`, `lto = "fat"`, `codegen-units = 1`) to `Cargo.toml`.
Then switch `hosting/Dockerfile:28-30` from `--release`/`target/release` to `--profile dist`/`target/dist`. `hosting/` is
a protected path, and that build runs with `CARGO_BUILD_JOBS=2`, so the image build gets several minutes longer.
`release` stays as it is.

## 2. Dependencies

None.

## 3. Relevant legacy paths

None (build configuration).

## 4. New paths

None.

## 5. Exclusive write scope

- This brief only.

## 6. Contracts and references

- C-NUM / ADR 0007: Rust does not contract floating point into FMA, and LTO changes no IEEE semantics. Checked below
  with identical sweep output and state hashes, not assumed.
- R-OPT-2: the evidence below.

## 7. Behaviour to preserve

- Everything: no source, manifest or lock file changes.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-BUILD-PROFILE-1` passes (path checks, fmt, readability).
- A reviewer reads the table and the method and agrees the verdict follows from them.

## Oracle files (read-only for implementers)

- None: the coordinator's brief allows a measured before/after table in place of a failing oracle.

## 9. Validation commands

To reproduce, from a checkout of `83edb687f` (`export PATH="$HOME/.cargo/bin:$PATH"`):
- Baseline: `cargo build --release --locked -p rh-server -p rh-headless -p rh-load --bins`.
- LTO: the same with `CARGO_PROFILE_RELEASE_LTO=fat CARGO_PROFILE_RELEASE_CODEGEN_UNITS=1` and its own `CARGO_TARGET_DIR`.
- LTO + mimalloc: add `mimalloc = { version = "0.1", default-features = false }` to `crates/rh-server/Cargo.toml`. Add
  `#[global_allocator] static GLOBAL: mimalloc::MiMalloc = mimalloc::MiMalloc;` to `crates/rh-server/src/main.rs` and
  `src/bin/wirebench.rs`. Build `-p rh-server --bin rh-server --bin wirebench` with the LTO variables.
- Per variant, interleaved by round: `/usr/bin/time -l <bin>/rh-headless --matches 20 --players 8 --map
  parity/maps/seed1.map.json` and `/usr/bin/time -l <bin>/wirebench --map parity/maps/seed1.map.json --scenario heavy
  --ticks 12000`; record "instructions retired" and "cycles elapsed".
- `rh-load run --server-bin <bin>/rh-server --matches 16 --players-per-match 8 --duration 30 --ramp-per-sec 4
  --fill-bots 4 --aggression 0.5 --seed 7 --server-args " --first-playable"` (the leading space keeps the value from
  being read as a flag), two rounds per variant; `server.cpuCoresSteadyMean` and `rssMbPeak` from the report.
- `determinism_sweep ci --out <dir>` per variant, then `cmp` of `manifest.json` and `replay.json` (rustc on PATH; the
  separate `provenance.json` names the binary and differs by design).

## 10. Risk and suggested model tier

None: nothing changes.

## 11. Known traps

- On a shared host, wall time cannot resolve a few percent. At load average about 95, six interleaved wall-time
  rounds gave overlapping ranges, so cycles and instructions were used instead. The cycle noise floor shows in the table:
  the `rh-headless` of "LTO + mimalloc" is the same binary as "LTO" (mimalloc was linked into `rh-server` and `wirebench`
  only) and read -1.6% against -4.6%.

## Measured (R-OPT-2)

Revision `83edb687f`. Apple M5 Pro, 18 cores, 64 GB, macOS 26, native arm64. Rust 1.97.0. Release builds, `debug = 1`.
The host was shared throughout with another session's full gate, implementers and a game client. Load average was
80-95 during builds, about 48 during the timing rounds and 35-50 during the load runs.

| Measure | Baseline (`release`) | Fat LTO, 1 codegen unit | Same + mimalloc |
|---|---|---|---|
| `rh-headless` 20 matches x 8 bots, seed1: instructions (median of 6) | 38.10 G | 37.07 G (-2.7%) | 37.07 G (same binary) |
| same: cycles (median of 6) | 8.16 G | 7.78 G (-4.6%) | 8.03 G (-1.6%; same binary: noise) |
| `wirebench --scenario heavy --ticks 12000`: instructions | 35.22 G | 34.60 G (-1.8%) | 34.34 G (-2.5%) |
| same: cycles | 6.15 G | 5.87 G (-4.6%) | 5.94 G (-3.4%) |
| server CPU, `rh-load` 16 x 8 first-playable, 30 s steady (2 runs) | 0.214, 0.224 cores | 0.214, 0.227 | 0.206, 0.231 |
| server peak RSS under that load (median) | 46.0 MB | 44.8 MB | 62.8 MB |
| `rh-server` binary, stripped (the Dockerfile strips) | 11.5 MB | 8.8 MB (-24%) | 9.0 MB |
| release build, fresh: `-p rh-server -p rh-headless -p rh-load --bins` | 1 min 20 s wall, 418 s CPU | 7 min 40 s wall, 634 s CPU | (+4 min for `rh-server` and `wirebench` alone) |
| `determinism_sweep ci` `manifest.json`, `replay.json` | reference | byte-identical | (same `determinism_sweep` binary as LTO) |
| headless state-hash fold, 20 matches | `2d27e224062d1176` | identical | identical |

## Notes from previous attempts
