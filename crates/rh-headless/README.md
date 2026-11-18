# rh-headless: bot-vs-bot matches with no waiting

The headless runner. It drives the same authoritative `Sim` and the same policy seats (`rh-policy`) as the live server,
at the same fixed 20 Hz step, but with no network and no waiting, so a match takes milliseconds. It is the engine of
the determinism sweep, the bot tournament and the benches, and the seam any later trainer would drive
(`docs/adr/0005-policy-seam.md`). The same seed and inputs give the same final state hash on every platform.

## Where it sits

It calls `rh-sim` and `rh-policy`. `rh-server` depends on it (it loads arena maps with `rh_headless::load_arena` and
uses `Env` in tests), and `rh-parity` compiles `src/digest.rs` into its `parity-bench`.

## Entry points

- `src/lib.rs`: the environment (`Env`, reset and step over a `Sim`); external agents act through `Env::step`, built-in
  policies sit in seats, both through the same input path.
- `src/main.rs`: the `rh-headless` command (runs N matches, prints one JSON line; `--digest`, `--replay-out`,
  `--verify-replay`; the file header lists every flag).
- `src/digest.rs`: the behaviour fingerprint (`outcomeDigest`), independent of the state hash.
- `src/bin/determinism_sweep.rs`: the cross-platform determinism check CI runs on x86-64 and arm64.
- `src/tournament.rs`, `src/tournament_cli.rs`, `src/bin/tournament.rs`: the bot tournament.
- `src/tune.rs`, `src/bin/tune.rs`: the port of legacy `tune.ts` and the offline seeded search over Brain numbers.
- `src/bin/scalebench.rs`: N fighters past the normal player cap (needs `--features bench`).
- `examples/`: `first_playable.rs`, `full_map_cost.rs`, `hitbox_compare.rs`, `source_soak.rs`.

## Run and test

```sh
cargo run --release -p rh-headless -- --matches 20 --players 8 --seed 1 --map parity/maps/seed1.map.json
cargo run --release -p rh-headless --bin determinism_sweep
cargo test -p rh-headless
```

The soak tests (`tests/source_arena_soak.rs`, `examples/source_soak.rs`) are `#[ignore]`d and slow; run them only when
a change touches match flow.

## Rules

Local rules: [AGENTS.md](AGENTS.md). Headless runs never use client prediction.
