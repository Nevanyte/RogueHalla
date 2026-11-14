# Hand-off: P-LAND-FIRE-1 (implementer-opus, 2026-11-14)

```
PACKET:          P-LAND-FIRE-1 — serves the land-generation lane (LAND_GENERATION_REQUIREMENTS.md section 3, Fire), the
                 signature-map work (ADR 0028)
BRANCH:          agent/opus/P-LAND-FIRE-1 @ (this commit; code at df7ca0fe1)
GATE:            not run by the implementer (the lane's session re-pins and runs the gate). The packet is NOT green:
                 the hard guarantees lose seeds 27 and 30 (and 70) in 16 attempts, which panics every option-on oracle
                 that needs every seed's map (mapgen_signature_volcano among them). See "Red for a number".
CHANGED:         crates/rh-sim/src/mapgen/style_scorch/craters.rs, style_scorch/guarantees.rs, style_scorch/README.md,
                 crates/rh-sim/src/mapgen/style_zones/guarantee.rs (Fire arm doc comment only), this file
D-RULES:         D1: option off byte-identical (g8_option_off_maps_are_todays and the style_scorch parity fixtures
                 green); no legacy counterpart (DEV-020). D2 n/a. No tick, wire, protocol, Unity change; nothing
                 serialized or hashed; CraterRules unchanged (content hash unchanged). No new RNG draw.
DEVIATIONS:      none
SHARED RECORDS:  none
LEGACY DOUBTS:   none
NOT ESTABLISHED: whole rh-sim suite and clippy --all-targets not run (coordinator's HOLD; clippy --lib clean); seeds
                 65-96 beyond seed 70 not measured; the base's time per map not measured on the same run.
RISKS:           G2/G5 broken on this build (seeds without a map); the bare-ring retry changes the look of the craters it
                 rescues (fewer breaches) and is a design choice for the reviewer.
```

## What was built

- `style_scorch/guarantees.rs`: `guarantees()` = `crater-per-camp`, `crater-clear-of-lava` (promoted literal, no
  const); `lava_clearance()` unchanged (2). `stream_lava(frame, w)` is the one definition of stream and pool lava (frame
  cells LOW with `DEFAULT_DEFINITION.lava` outside the anchor's footprint), used by the styler and the check;
  `disc_holds` the disc test (16 d^2 < r4^2). `crater-per-camp` also refuses a Fire crater standing on no Fire camp
  (rule 1's second half). Five in-crate tests on hand-built 40 x 40 maps (boundaries: Chebyshev 2 vs 3, r4 40 vs 41,
  lava inside the cone's box, non-LOW and non-lava cells, Frost craters, a Fire barrow on the camp).
- `craters.rs` `place_craters`, signature mode only: the band's option first, then each smaller option (never larger,
  never under `radius_min4`); a size whose disc holds stream/pool lava is skipped; a size the rim/route checks refuse is
  tried once more as a **bare ring** (`crater(.., bare = true, ..)`: no breach toward neighbouring camps; the route
  guard's and stretch check's crossing beams, and a frontier crater's fallback breach, still apply) before the next
  smaller size; a pen the runtime refuses (`place_feature` Ok(None), share below `rim_keep_permille`) moves on to the
  next size instead of leaving the camp craterless (`commit` now returns whether it placed). The option-off path passes
  `bare = false` and ignores the bool: unchanged.
- No new draw: every new try reads hash noise (`kit::noise_q`) only; a refused pen is rolled back by the runtime.

## Test results (targeted, seeds as each oracle states)

Green:
- `mapgen_land_fire`: `the_fire_guarantees_are_registered_by_name` (1 of 14).
- `mapgen_guarantees`: `the_registry_lists_the_fire_frost_and_plague_rules_and_nothing_else`,
  `every_guarantee_holds_on_every_kept_map_of_seeds_1_to_32`, the lake and pool doctored-map tests (5 of 7).
- `mapgen_signature_fire`: 10 of 11, `g8_option_off_maps_are_todays` among them (option off byte-identical).
- `cargo test --lib style_scorch` (13, the parity fixtures and the 5 new guarantee tests); `cargo fmt --check`;
  `cargo clippy -p rh-sim --lib --features trusted-status-probes -- -D warnings`.
- On every kept map (measured, seeds 1-64): every camp has its crater, every crater is clear (see the numbers), size
  rule held, share in the window. These are what the red oracle tests would assert once every seed has a map.

Red by design, to re-pin (the coordinator's):
- `mapgen_guarantees::the_refusal_labels_of_seeds_1_to_32_are_todays_relabelled` (first difference seed 1: now
  `[crater-per-camp]`, pinned `[lake-at-mouth]`).
- `mapgen_guarantees::a_lake_failure_is_refused_after_records_as_the_frost_guarantee`: lake refusals 5, pinned 6.
- Not run (HOLD), expected red: `tests/support/signature_pins.rs` option-on pins (`mapgen_signature_blueprint.rs`,
  `mapgen_landmark_frame.rs`), the `rh-mapdress` goldens.

Red for a number, with the measurement (all one cause: seeds with no map in 16 attempts):
- Seeds 27 and 30 (of 1-32) and seed 70 (of 33-96) get no map in `attempt_limit()` = 16 attempts with the option on;
  every one of their attempts that reaches the styler is refused by `crater-per-camp` or by an earlier rule.
- So every test that loads all seeds' maps panics before asserting: `mapgen_land_fire` 13 of 14 (`seed 27: no map in
  16 attempts`; the wide tests on seed 70), `mapgen_signature_volcano` 15 of 20 (`seed 27: no map with the option on`,
  the G2/G5 helper at line 298), `mapgen_signature_fire_bands` 4 of 5, `mapgen_signature_fire`
  `volcano_on_the_back_edge_with_streams_toward_the_front` ("seed 27: no certified map"). Expect the same in every
  other option-on oracle that takes seeds 1-32.
- `the_attempt_bounds_hold_and_the_fire_guarantees_refuse_little` would fail on its own figures too: 36 Fire refusals
  on seeds 1-32 against 8; attempts 139 on the 30 kept seeds (171 counting the two lost at 16 each) against 137.

This contradicts hard rule 1 of the dispatch ("`mapgen_signature_volcano.rs` stays green") and G2/G5 ("a landmark is
never a reason to lose a seed"). I stopped here rather than bend the generator or a test: the remaining refusals are
not fixable inside `style_scorch/` without breaking the oracle's own size rule (see below).

## The numbers (seeds 1-64, option on, no decor, temporary ignored test, not committed)

| Measure | Base (brief) | First build (shrink, disc skip) | Final (plus bare-ring retry) |
|---|---|---|---|
| seeds with a map, 1-64 | 64 | 60 (lost 18, 25, 27, 30) | 62 (lost 27, 30) |
| attempts 1-32: sum over kept / worst index | 89 / 9 | 143 / 11 (28 kept) | 139 / 11, seed 24 (30 kept) |
| attempts 1-64: sum over kept / worst index | 192 / 10 | 330 / 15 | 310 / 15 (seeds 48, 55) |
| Fire guarantee refusals 1-32: crater-per-camp / crater-clear-of-lava | 0 / 0 | 53 / 0 | 36 / 0 |
| same, 1-64 | 0 / 0 | 95 / 0 | 67 / 0 |
| craters per kept map / camps without a crater | 7-10 / 36 | 10 / 0 | 10 / 0 |
| craters below their band's option | 0 | 5 of 600 (near, by 4) | 1 of 620 (near, by 4 quarter tiles) |
| least Chebyshev crater tile to stream/pool lava | 3 | 4 | 4 (none within 1, 2 or 3) |
| least camp-to-lava distance on kept maps (Euclid) | 4.0 | 5.83 | 5.83 |
| crater share on/off, median 1-32 / 1-64 | 1.001 / 1.002 | 0.982 / 0.991 | 0.986 / 1.013 (per seed 0.854-1.345) |
| time per seed, all attempts, option on (opt-level 2, loaded machine) | not measured | 0.315 s | 0.328 s |

Other refusals (final, 1-32 / 1-64): Signature 31 / 61, SpawnRatio 23 / 44, Prefix 19 / 46, SpawnPool 10 / 19,
Repair(RadiusPost) 8 / 15, lake-at-mouth 5 / 14, SignatureDiscarded 5 / 9, Repair(FloorShare) 4 / 5.

Crater tiles by band (final, 1-64): core 19 craters, mean 166.8 (124-190); near 218, 115.5 (70-191); mid 63, 91.8
(72-143); far 320, 88.8 (68-109). Near (core + near) over far: 1.35 (floor 1.15). At the band's option: 619 of 620
(99.8%, floor 90%).

## Why the camps fail (first build, every attempt of seeds 1-32 that reached the styler)

117 camps found no crater: 71 the rim too short on every allowed size (rim < `rim_min` = 8 after breaches and
`keep_arcs` pruning; **never** the route stretch), 40 stream or pool lava in the disc of every allowed size (camps 4-5
tiles from lava), 6 the runtime's pen share. By band: far 67, near 33, mid 16, core 1. A far camp is locked to its
smallest option (often its only one, R = 20); near map corners and the zone's frontier its 5-tile ring rarely keeps 8
open tiles once breached toward its neighbours.

What each lever buys (seeds 1-32, Fire refusals / seeds lost; one variable at a time, measured, not committed unless
said):

| Variant | Fire refusals | Lost seeds |
|---|---|---|
| first build | 53 | 4 |
| + far and mid camps may grow to a larger option (breaks the oracle's "never grows") | 44 | 3 |
| + retry a short rim with one breach | 41 | 3 |
| + retry a short rim as a bare ring (**committed**) | 36 | 2 |
| bare ring, disc rule off in the styler (the guarantee still refuses): per-camp / clear-of-lava | 19 / 17 | 2 |

The last row is the upstream alternative's best case: keeping the streams off crater ground in
`landmarks/signature/volcano.rs` removes at most the 17 lava refusals, leaving about 19 per-camp refusals (rim) on
seeds 1-32, still above 8, plus the extra `Signature` refusals SCALE-4 measured for bent streams (Fire 38 to 81 over
1-64, one seed lost) and every option-on map moving again. It is not enough on its own.

## What the next packets should know (owner decisions needed)

- The figure that matters is not the 8 refusals but the lost seeds: with hard guarantees and 16 attempts, Fire must
  succeed far more often per attempt. The dominant cause is today's rim rule on the smallest crater, not the streams.
  Choices, roughly by expected effect: (1) a lower `rim_min` for signature-mode craters, or counting the lava apron's
  cut as rim (a `CraterRules` field: content hash moves); (2) let a far camp whose smallest crater fails take a larger
  option (measured: modest, 53 to 44; contradicts the amended `craters_take_their_bands_radius_option`); (3) the
  upstream stream steering for the 40% lava share; (4) a larger attempt budget for signature maps.
- The bare-ring retry is in the branch and can be dropped by deleting the `if found.is_none()` block in
  `place_craters` (back to 53 / 4 lost). It changes only craters that would otherwise be missing.
- `stream_lava` and `disc_holds` are `pub(super)` in `style_scorch::guarantees`; a Frost or Holy packet can copy the
  shape. The registry's Fire arm already pointed at `style_scorch::guarantees` (stub); only its doc changed.

## What the brief got wrong

- "Green on the stub ... must stay green": `the_volcano_keeps_its_three_streams_and_pools`,
  `crater_share_of_...`, `crater_size_falls_...` and `the_attempt_bounds_hold...` all depend on every seed keeping a
  map; with the guarantees hard they turn red for lost seeds, whatever the styler does inside its scope.
- Section 4 "counting the apron-cut rim against `rim_floor_min`": `rim_floor_min` (10) exceeds `rim_min` (8), so it
  would refuse more craters, not fewer; the guard's floor applies only to extra breaches. Not used.
- The baseline "least crater tile to lava 3" is now 4 on kept maps; the disc rule and the camp distance (>= 5.83 on kept
  maps) are what move it.
