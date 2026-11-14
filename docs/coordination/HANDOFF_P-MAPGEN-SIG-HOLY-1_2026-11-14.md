# Hand-off: P-MAPGEN-SIG-HOLY-1 (implementer-opus, 2026-11-14)

```
PACKET:          P-MAPGEN-SIG-HOLY-1 — serves owner direction 2026-11-12 (G1-G3), owner decision 4 (2026-11-10), owner
                 decisions 2026-11-13 (way A, one 37 x 37 blueprint, two camps, 41 site, legacy elites; ADR 0028 G9) and
                 2026-11-14 (spawn points on the hall's open floor; the shrine walls exempt from the pier rule)
BRANCH:          agent/opus/P-MAPGEN-SIG-HOLY-1 @ 938eaca48 (code; this file is the next commit)
GATE:            not run by the implementer (targeted tests, cargo fmt --check and
                 cargo clippy -p rh-sim --lib --tests --features trusted-status-probes -- -D warnings: green)
CHANGED:         crates/rh-sim/src/mapgen/landmarks/signature/{mod.rs, spec.rs}
                 crates/rh-sim/src/mapgen/attempt/mod.rs
                 crates/rh-sim/src/mapgen/zones/camps/{mod.rs, tests.rs, README.md}
                 crates/rh-sim/src/mapgen/certify/{features.rs, README.md}
                 crates/rh-sim/src/mapgen/style_sanctum/{mod.rs, README.md}
                 docs/adr/0028-signature-landmarks.md
                 docs/coordination/HANDOFF_P-MAPGEN-SIG-HOLY-1_2026-11-14.md
D-RULES:         D1: option off byte-identical (pins::pinned_off, rh-mapdress OFF_GOLDEN tests green); no legacy
                 counterpart for the option-on hall (DEV-020). D2 n/a. No sim tick, wire, protocol or Unity change.
DEVIATIONS:      none new (DEV-020 covers the option-on hall)
SHARED RECORDS:  proposed edits: docs/DEVIATIONS.md DEV-034 (Plague known gap): seeds 1-32 now show 1 Large pool, in the
                 near band (see "Red tests" below); the lane's session re-pins the digests listed below.
LEGACY DOUBTS:   none
NOT ESTABLISHED: time per map before (stub) was not measured on the same machine run; the other zones' land reports
                 before/after were not compared (their floor tests are green); seeds beyond 128; the owner's picture.
RISKS:           the Holy styler's share of the zone outside the site is 0.006 (median) — its hall was its fill (owner's
                 call, numbers below); mapgen_land_plague's known-gap pin flipped (1 Large near-band pool).
```

## What was built (against the brief's design items)

1. `spec::specs` fifth entry (Holy, `Hall`, `d.hall_grid`, legend `#` wall / `L` light floor owned by the `Hall`, `.`
   plain, `*` `D` `E` marks; `[Hall, Cover, Centre]`; `Placement::Centre`; `FrontRun(b'E')`; `doorway: false`;
   `members: [Hall]`). The light's code is looked up by key (`spec::light_code`), not a `TerrainCodes` field.
2. `reserve`: the Holy side is `Definition::hall_site_side` (41), the back-edge line (centroid + `back_slack`); a
   candidate square needs its two hall camps (`hall_camps`: the drawing's `E` tiles landed centred in the inner rect,
   front `axis(front(inner centre))`, unmirrored); `camp` = back `E`, `extra` = [front `E`], `elite` false.
   Sentenced/Frost: `elite` = camp present (true).
3. `zones::camps::Reserve` now carries `camps: Vec<usize>` and `elite: bool` (was `camp: Option<usize>`); pinned camps
   are fixed labels 1, 2, ..., count in spacing and halos, never move; `elite` false gives legacy's elite rule among all
   ten. Two pinned camps of one site closer than the camp gap fail the round (never at 12 tiles apart).
4. `relabel_sites`: the Holy site's tiles take the nearer of its camps (squared distance, `camp` on a tie);
   `relabelled_zones()` unchanged ([Sentenced, Frost], pinned by the church oracle). `stray_camp` takes the site's own
   camps (the debug_assert allows the second hall camp).
5. `fan_holds_a_site_camp`: halos of every site camp (`camp` and `extra`) against every fan. Nothing else in the fan
   code changed.
6. The anchor comes from the stamp; `finish` writes the frames' anchors only (the styler-hall anchor branch is gone);
   `aprons_apart` reads membership from the table (the hall is a member), the anchor-footprint lookup is gone.
7. `write`: a `Hall` gets params `[axis, R.hall_pitch, radius, piers, 0]` (axis 1 when the front at the anchor is
   vertical; radius (side - 1) / 2 = 18; piers counted as exact 3 x 3 wall pieces = 32).
8. `style_sanctum::shape` returns the empty plan when the zone's frame is a `Hall` (no hall, no bays, no bay lights).
9. Spawn mask (`write`, Hall only): box -> `boxes`; ring of `bands[0]` round the box, the written walls and light, and
   the Chebyshev square of `Definition::shrine_clear` (new field, 3) round every light tile -> `near1`.
10. `certify/features.rs`: the `Hall` whose box equals a Holy `Hall` anchor's footprint must have exactly one non-pier
    wall group (the shrine); every other hall and the gallows keep their rule.
11. `from_definition`: an entry whose entrance glyphs write different things keeps no drawing in every build
    (`entrance_door` returns `Err`), so it refuses every option-on attempt at that landmark.

No spawns/ or pipeline/ change was needed (the mask is written in the Landmarks phase, as the prototype did). No env
knob, no `proto` code.

## Draws added

- Prefix, per round that reaches the Holy zone in `reserve`: one `pick` on the Holy signature stream (the fifth
  stream of `streams()`, `hash_seed(seed, attempt, Holy slot, 5)`), after the four existing zones' draws. None when
  there is no candidate (the round fails first: Holy has no deepest-square fallback).
- Landmarks phase: the stamp's one mirror draw on the Holy stream's continuation, after Frost's.
- Camp placement: no new draw; with the Holy site's two pinned camps the zone's `first_candidate` draw and the Lloyd
  steps run as for any pinned zone (the draw sequence moves because the inputs moved).
- Option off: nothing new (no sites, no Holy stream).

## Tests

| file | result |
|---|---|
| mapgen_signature_hall | 18 passed, 1 ignored (report) |
| mapgen_signature_reserve | 11 passed |
| mapgen_signature_blueprint | 8 passed, 2 FAILED (pins, expected), 1 ignored |
| mapgen_signature_table | 8 passed |
| mapgen_landmark_frame | 8 passed |
| mapgen_signature_core / _aprons / _fire / _frost / _volcano / _church | 8 / 2 / 11 / 7 / 20 / 13 passed |
| mapgen_signature_bog_tree / _fire_bands / _frost_lake / _scale, mapgen_land_decor_attempts | 9 / 5 / 5 / 10 / 1 passed |
| mapgen_guarantees | 6 passed, 1 FAILED (labels pin, expected) |
| mapgen_land_sentenced | 11 passed |
| mapgen_land_plague | 26 passed, 1 FAILED (known-gap pin, NOT in the expected list; see below) |
| lib: landmarks::signature, zones::camps, style_sanctum, certify, spawns, pipeline::style_refusal | 47 passed |
| rh-mapdress | 3 FAILED (goldens, expected); OFF_GOLDEN tests green |
| rh-server map_anchor_seeds, map_anchors | 1 / 5 passed |

New unit tests: `zones::camps::tests::a_site_pins_two_camps_and_its_elite_flag_chooses_the_elite_rule`;
`signature::tests::{the_holy_relabel_takes_the_nearer_camp_and_the_back_one_on_a_tie,
the_holy_anchor_is_the_frames_and_the_hall_is_intact, a_hall_wall_turned_to_plain_floor_is_not_intact,
the_hall_keeps_spawns_off_its_walls_and_clear_of_its_light_only, an_entrance_whose_glyphs_write_different_things_is_refused}`;
`certify::features::hall_tests::only_the_drawn_halls_one_shrine_is_exempt` (no shrine, a second non-pier group, a
hall off the anchor's footprint: each one `feature-whole`).

### Red tests (not edited)

- `mapgen_signature_blueprint.rs` `option_on_maps_are_unchanged`, `the_landmarks_phase_is_unchanged`: print with
  `cargo test -p rh-sim --features trusted-status-probes --test mapgen_signature_blueprint print_signature_pins -- --ignored --nocapture`.
- `mapgen_guarantees.rs` `the_refusal_labels_of_seeds_1_to_32_are_todays_relabelled` (first difference seed 3; the
  failing assertion prints the list).
- `rh-mapdress` `anchor.rs` `a_signature_map_stripped_of_its_anchors_is_dressed_as_today` (NO_ANCHOR_GOLDEN),
  `marsh.rs` `every_zone_other_than_plague_is_dressed_as_before` (SIG_GOLDEN), `sentenced_rings.rs`
  `a_file_without_origin_dresses_signature_maps_exactly_as_today` (NO_ORIGIN_GOLDEN): the assertions print the values.
- **Not in the brief's list:** `mapgen_land_plague.rs` `known_gap_no_large_pool_and_no_pool_near_the_tree_on_seeds_1_to_32`
  (a pinned measured fact, DEV-034): (large, near) = (1, 1), was (0, 0). The moved option-on maps now hold one Large
  Plague pool (240 tiles) in the tree's near band on seeds 1-32 (the Plague report: near/mid/far pieces 1 / 35 / 31;
  on sizes Small 50, Medium 16, Large 1). The test says to restore tests 4 and 5 and update DEV-034 when this happens;
  that is the lane's call (one pool on 32 maps is not the owner's direction delivered). Not edited.

## Measurements (optimised test build, `decor: false`, 16 attempts per seed; macOS arm64)

| measure | build | bound / before |
|---|---|---|
| seeds with a map 1-32 / 33-128 | 32 / 96 | 32 / 96 (owner's) |
| attempts seeds 1-32 | 93 | <= 121; stub 89 |
| worst attempt index 1-32 | 11 (seed 11) | <= 12; stub 9 |
| attempts 1-64 / 65-128 / 1-128 | 189 / 171 / 360 | stub 1-64: 192 |
| worst index 1-64 / 65-128 | 11 (seed 11) / 8 (seed 81) | stack's 12 |
| pool points on the hall's open floor, 1-32 | 4.34 per map on 1-64 (278); 4.37 on 1-128; every map 3-6 | >= 32 on >= 8 maps (1-32) |
| hall camps that are elites | 0 of 128 on 1-64 | prototype 0 |

Refusals (all attempts before each seed's map):

| label | 1-64 | 65-128 | stub 1-64 |
|---|---|---|---|
| Signature | 45 | 26 | 38 |
| Prefix | 28 | 22 | 24 |
| Repair(RadiusPost) | 20 | 28 | 8 |
| Guarantee Frost lake-at-mouth | 17 | 11 | 16 |
| SpawnRatio | 11 | 11 | 26 |
| SignatureDiscarded | 3 | 5 | 5 |
| SpawnPool | 1 | 4 | 9 |
| FloorShare | 0 | 0 | 2 |

Prefix rounds without a Holy site (throwaway counter in `reserve`, not committed; my harness runs each seed's attempts
once): 29 of 414 Holy reservations reached on seeds 1-64, 52 of 803 on 1-128. (The brief's 35 / 66 came from the
prototype's harness, which also re-runs the accepted attempt's prefix; not the same count.)

Holy camps (tiles, mean over maps, seeds 1-128): nearest-neighbour mean 18.1 on, 20.3 off; mean pairwise distance 43.0
on (min map 37.1), 44.2 off (min 38.0). (The prototype's "spread" is defined differently; the nearest-neighbour figure
matches its 18.1 / 20.2.)

Spawn pool (seeds 1-128): 25.6 points per map; Holy 7.35 per map; 4.37 per map on the hall's open floor; zero on a hall
wall or light or within 3 of the light (oracle, 1-128).

Holy's land (the oracle's `holy_share`, medians over seeds 1-64): option on 0.004 of Holy's free ground, 0.006 with the
Holy site taken out; option off 0.212, 0.004 with the styler hall's box and apron taken out (stub: 0.212 on, 0.212
off). The styler's own fill outside the site is now its legacy shrines' lights (0-18 tiles per map): far under "about
today's share, within about a fifth" if today's share is read as the styler hall's. Owner's call; this packet decides
nothing about it.

Time (seeds 1-128): 0.141 s per seed option on (all attempts until the map, mean 2.8 attempts), 0.030 s per seed
option off. No stub timing on the same run.

Seeds 1-8, accepted attempt index (for the owner's picture): 1:1, 2:0, 3:1, 4:0, 5:0, 6:0, 7:3, 8:2.

## What the brief got wrong or left open

- Section 3 says `spawns/mod.rs` and `pipeline/mod.rs` change for the spawn mask: not needed, the mask is the
  Landmarks phase's `near1`/`boxes` (as the prototype). Neither file changed.
- The prototype's `*` was a plain glyph; the oracle wants it a mark (it is one here).
- `relabelled_zones()` stays [Sentenced, Frost] (the church oracle pins its length); the Holy relabel is by zone.
- The attempt README has no section on the signature reservation, so it was not edited.
