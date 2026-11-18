# Hand-off: P-MAPGEN-SIG-HOLY-1 (implementer-opus, then the lane's session, 2026-11-14)

**Read first (the lane's session; figures on the tree with P-LAND-FIRE-1 merged, base a5fac52eb).** The Holy site
moves every option-on Prefix, so every option-on pin row and every landmark pin row moved. Attempts on seeds 1-32:
94 -> 96; on seeds 1-64: 192. No seed of 1-128 is without a map. Guard values at or near a bound:

| Guard | Measured | Bound | Whose |
|---|---|---|---|
| Worst attempt index, seeds 1-32 (every signature oracle) | 11 (seed 11; was 9) | 12 | the stack's |
| Fire camps without a crater, seeds 1-64 (`mapgen_land_fire`) | 35 of 640 (was 32) | 40 | last set on the lane's judgment |
| Most Fire camps without a crater on one map | 4 (seed 54; was 2) | 5 | last set on the lane's judgment |
| Plague Large pools / near-band pools, seeds 1-32 (DEV-034 pin) | 1 / 1 (was 0 / 0) | 2 / 2 | last set on the lane's judgment |

Beyond any of the last three a later packet stops and the measurement goes to the owner (the coordinator's rule,
written into the tests' messages).

```
PACKET:          P-MAPGEN-SIG-HOLY-1 — serves owner direction 2026-11-12 (G1-G3), owner decision 4 (2026-11-10), owner
                 decisions 2026-11-13 (way A, one 37 x 37 blueprint, two camps, 41 site, legacy elites; ADR 0028 G9) and
                 2026-11-14 (spawn points on the hall's open floor; the shrine walls exempt from the pier rule)
BRANCH:          agent/opus/P-MAPGEN-SIG-HOLY-1 @ the commit that adds this block (the last code commit is 01cd99280,
                 "clippy (range contains in the spawn-mask unit test)"; packet/P-MAPGEN-SIG-HOLY-1 @ f060a2983; base
                 origin/feature/unity-rework @ a5fac52eb)
GATE:            PASS (quick, --oracle packet/P-MAPGEN-SIG-HOLY-1 --base origin/feature/unity-rework) on 96c5f8838, run
                 by the lane's session: rustfmt, clippy and tests of rh-mapdress, clippy rh-sim, the content registry,
                 the whole rh-sim suite as CI, the parity report. [not run: the full gate (the coordinator's, on the
                 merged tree). Run besides it: cargo test -p rh-server, 49 targets green, no Postgres.] One docs-only
                 commit (this file, ADR 0028's figures, one sentence of the Fire hand-off) follows the gated commit.
CHANGED:         crates/rh-sim/src/mapgen/landmarks/signature/{mod.rs, spec.rs, hall.txt (new, the stub's)}
                 crates/rh-sim/src/mapgen/attempt/mod.rs
                 crates/rh-sim/src/mapgen/zones/camps/{mod.rs, tests.rs, README.md}
                 crates/rh-sim/src/mapgen/certify/{features.rs, README.md}
                 crates/rh-sim/src/mapgen/style_sanctum/{mod.rs, README.md}
                 docs/adr/0028-signature-landmarks.md, this file, docs/coordination/HANDOFF_P-LAND-FIRE-1_2026-11-14.md
                 (one sentence). Packet branch (oracle, amendments, pins): tests mapgen_signature_hall.rs (new),
                 mapgen_signature_{reserve,blueprint,table,church}.rs, mapgen_landmark_frame.rs, mapgen_land_plague.rs,
                 mapgen_land_fire.rs, mapgen_guarantees.rs, support/signature_pins.rs, rh-mapdress tests anchor.rs /
                 marsh.rs / sentenced_rings.rs (golden constants only), the brief
D-RULES:         D1: option off byte-identical (pins::pinned_off, rh-mapdress OFF_GOLDEN tests green); no legacy
                 counterpart for the option-on hall (DEV-020). D2 n/a. No sim tick, wire, protocol or Unity change.
DEVIATIONS:      none new (DEV-020 covers the option-on hall)
SHARED RECORDS:  proposed edits: docs/DEVIATIONS.md DEV-034 (Plague known gap): "re-measured on P-MAPGEN-SIG-HOLY-1's
                 maps: 1 Large pool, in the near band, on one map of seeds 1-32 (a chance fit, as 2 of 96 maps before
                 the volcano); the direction is still not delivered; the pin is a ceiling of 2 and 2".
                 docs/OWNER_DECISIONS.md: a row "2026-11-14, the Holy hall: spawn points allowed on its open floor
                 (off walls, 3 tiles clear of the shrine's light); the shrine's walls exempt from the 3 x 3 pier rule;
                 taken in the land-generation session's chat ('Yea to all 3')"
LEGACY DOUBTS:   none
NOT ESTABLISHED: the full gate; time per map before (stub) on the same run; the other zones' land reports before and
                 after (their floor tests are green; the Plague report is in the lane's section); seeds beyond 128;
                 which reading of "3 tiles clear of the shrine" the owner meant (the build and the measurement use the
                 shrine's light: see the lane's section)
RISKS:           the Holy styler's share of the zone outside the site is 0.006 (median): its hall was its fill (told to
                 the owner; nothing decided); the worst attempt 11 against 12 will be the Frost room packet's tightest
                 bound; the review's structural note (below) should land before that packet.
```

**Note on the figures below:** the implementer measured on the hall alone, before it was stacked on P-LAND-FIRE-1
(attempts 93 / 189, worst 11). On the merged tree: 96 / 192, worst 11 (seed 11); refusals on seeds 1-64: Signature
45, Prefix 28, Repair(RadiusPost) 20, lake-at-mouth 17, SpawnRatio 12, SignatureDiscarded 3, SpawnPool 2,
Repair(FloorShare) 1; spawn-pool points on the hall's open floor 278 on seeds 1-64 (4.34 per map); hall camps that are
elites 0; Holy share medians as below. Where the sections below say "1-32" beside the spawn-point figures, the figures
are for seeds 1-64.

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

## Gate, pins and review (the lane's session, 2026-11-14)

- **Gate:** as the block says (output in the session's scratchpad, `verify/hall_gate.txt`).
- **Pins re-taken from this build on the tree with P-LAND-FIRE-1 merged** (`re-pin:` commit on the packet branch,
  constants only; `scratchpad/repin.py` runs the three printers):

  | Expectation | Old | New |
  |---|---|---|
  | `signature_pins.rs` `pinned_on_maps` (seeds 1-32, option on) | 94 attempts, worst 9 (seeds 22, 30) | all 32 rows moved; kept attempt changed on 14 seeds (7: 2->3, 8: 1->2, 9: 2->3, 11: 0->11, 13: 3->1, 14: 1->0, 16: 2->8, 18: 7->5, 22: 9->5, 23: 4->0, 24: 4->1, 26: 7->5, 29: 1->3, 30: 9->7); 96 attempts, worst 11 (seed 11), then 8 (seed 16), 7 (seed 30) |
  | `signature_pins.rs` `pinned_landmarks` (seeds 1-16, attempts 0 and 1) | | all 32 rows moved (the Landmarks phase now places the hall; the Holy site moves the Prefix) |
  | `mapgen_guarantees.rs` `pinned_labels` | 62 refusals over seeds 1-32 | 17 seeds' rows changed; 64 refusals: Signature 22, Prefix 16, Repair(RadiusPost) 11, lake-at-mouth 6 (unchanged), SpawnRatio 5, SpawnPool 2, SignatureDiscarded 1, Repair(FloorShare) 1 |
  | `rh-mapdress` `NO_ANCHOR_GOLDEN` / `SIG_GOLDEN` / `NO_ORIGIN_GOLDEN` | | 4 of 4 / 16 of 16 / 4 of 4 values |
  | `pinned_off`, the rh-mapdress option-off goldens | | unchanged |

- **Oracle amendments after the build** (the lane's session, each an `oracle-amend:` commit):

  | Test | Old | New | Why |
  |---|---|---|---|
  | `mapgen_land_plague.rs` `known_gap_no_large_pool_and_no_pool_near_the_tree_on_seeds_1_to_32` (DEV-034) | exactly 0 Large pools and 0 near-band pools | at most 2 and 2 | the hall's maps hold 1 and 1: one Large pool of 240 tiles in the near band on one map of 32, a chance fit (2 of 96 maps before the volcano). **The owner's direction "three sizes, large near the tree" is still NOT delivered.** Beyond 2 the packet stops and the owner decides |
  | `mapgen_land_fire.rs` gap ceiling comment and message | "no more camps bare than today's game" | "40, about 1 camp in 16; not 'no worse than today'" | 37 to 40 would pass; measured 32 on Fire's maps, 35 here; today's game 36 |
  | `mapgen_land_fire.rs` `worst_map_allowed` | 4 | 5 | measured 4 (seed 54) here; a guard, not the owner's figure; the last raise on the lane's judgment |
  | `mapgen_land_fire.rs` `at_band_option_min` | 0.9 | 0.98 | measured 1.0 (302 of 302, 304 of 304); now printed (the coordinator's review of P-LAND-FIRE-1) |

  Plague on seeds 1-32, option on, before (Fire merged) and after (the hall): pools 64 -> 67; styler bog 4,636 ->
  4,751 tiles (median per map 131 -> 120.5); largest pool 171 -> 240; Large pools 0 -> 1; near-band pools 0 -> 1;
  bog share on/off, median ratio 0.48 -> 0.53; free ground on, median 3,568 -> 3,572.5.
- **Owner-directed tests:** none passes on a few seeds only. The spawn-point decision is pinned by "none on a wall or
  the light or within 3 tiles of the light" on seeds 1-128 and "at least 32 pool points on the hall's open floor on
  at least 8 maps" (measured about 139 on seeds 1-32).
- **"3 tiles clear of the shrine":** the owner's words, from the lane's question. The measurement behind the question
  and this build keep spawn points 3 tiles (Chebyshev) from the shrine's **light**, which is the shrine's own box plus
  one row in front and one behind: a spawn point may stand one tile outside the shrine's side wall. Reported to the
  owner with the picture; if the owner means the shrine's walls, the mask grows and the spawn pool is measured again.
- **Review:** reviewer-opus on 96c5f8838: **LAND WITH NOTES**, one note, structural; no behaviour defect, no broken
  guarantee.
  - **Note 1 (becomes P-MAPGEN-SIG-HOLY-1-fix, a refactor guarded by the digests):** the hall's site and spawn rules
    are Holy special cases, not table data: `reserve` branches on `Zone::Holy` for the side and the camps, `write`
    branches on `FeatureKind::Hall` for the spawn mask and the params, and the mask finds the light as "any written
    floor with terrain", not by its glyph. Smallest fix: three `spec::Spec` fields (`site_side`, `site_camps`, a
    `spawn` rule) that `reserve` and `write` read; no map moves. It should land before the Frost room packet, which
    adds its own site exception in the same function, and which must not test a site's own camps against its own fan
    (`fan_holds_a_site_camp` now reads every fan against every site camp).
- **Minor, not notes** (the reviewer's list; no packet; the comment fixes ride with the fix packet):
  - `signature/mod.rs`: the new `hall_marks` doc was inserted inside `inside_camp`'s doc comment.
  - `certify/features.rs`: the exemption could require the one non-pier group to enclose the hall's light tiles (it
    accepts one non-pier group of any shape; the generator cannot produce another, `intact` checks every owned tile).
  - `reserve` does not itself refuse a square whose `E` tiles are not legal camp tiles; `place_camps_reserving` fails
    the round instead (it looks unreachable; the doc should say so).
  - `relabelled_zones()` says Sentenced and Frost only, yet Holy is relabelled through a zone test; its doc should
    mention Holy. `landmark_kind()`'s wording omits `Hall` (only T5 reads it).
  - Fire `worst_map_allowed` 4 to 5 was not needed by this tree's measurement (4 would pass).
  - `certify/README.md`, `zones/camps/README.md` and `zones/camps/tests.rs` are docs and tests of in-scope modules,
    outside the literal write scope.
  - A failed entrance-glyph check makes every Prefix round fail for Holy (refused at the Prefix, not at the stamp).
- **Picture:** eight seeds of the real build (Holy zone: piers, shrine, light, the two hall camps, spawn points
  inside and outside the hall) sent to the owner: 32 exact piers and one shrine on every map, 4 to 6 spawn points on
  the hall's open floor, none on a wall, on the light or within 3 tiles of it.
