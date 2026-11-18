# rh-mapdress: cosmetic map dressing

A pure rule engine. Given a generated map and a rule file, `dress` returns props (trees, rocks and so on) and a
ground-variant plane in the shapes the map blob of the wire protocol reserves. It is **cosmetic only**: nothing here
is read by the simulation, a bot or the map certifier. There is no RNG state: every stage is seeded from the map hash
and the stage name by integer hash noise, so the output is byte-identical everywhere.

## Where it sits

It reads `rh-sim` map types and is called by `rh-server` (`src/map_pool.rs`), which dresses each map once, off the tick,
and sends the result to clients inside the map blob.

## Entry points

- `src/lib.rs`: `dress(map, map_hash, rules)` and the stages in output order: keep-clear mask, legacy props, signature
  rings, clusters with clearings, edge dressing, filler. The output is cut to `MAX_PROPS`, so later stages lose first.
- `src/rules.rs`: parsing and validation of the rule file; every refusal names the rule it is about. Distances are tiles.
- `src/mask.rs`: the keep-clear mask and tall-prop corridors.
- `src/hash.rs`: the integer hash noise.
- `rules/zones.json`: the shipped rule file.

Positions are world units as `u16` (1 tile = 32 units); a prop belongs to the tile `floor(x / 32), floor(y / 32)`.

## Test

```sh
cargo test -p rh-mapdress
```

`tests/` has one file per concern (`anchor.rs`, `marsh.rs`, `landmark_rules.rs`, `rules_file.rs`, `properties.rs`, ...);
`tests/fixtures/` holds their inputs.

## Rules

The crate is deterministic code: its `clippy.toml` restricts what it may use, as in `rh-sim`. It has no `AGENTS.md`; the
root [AGENTS.md](../../AGENTS.md) applies.
