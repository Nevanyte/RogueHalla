# Hand-off: P-LAND-FROST-1 (implementer-opus, 2026-11-14)

```
PACKET:          P-LAND-FROST-1 — serves LAND_GENERATION_REQUIREMENTS section 3 (Frost; "Every zone"), owner decision
                 2026-11-14 ("Yes to both", OWNER_DECISIONS row 10); ADR 0028 G10. Signature option only.
BRANCH:          agent/opus/P-LAND-FROST-1 @ the commit that adds the section "Gate, pins and review" (last code
                 commit b432cc1fb, the review's note 1; packet/P-LAND-FROST-1 @ 7dccffb06; base
                 origin/feature/unity-rework @ b71590c8f)
GATE:            PASS (quick, --oracle packet/P-LAND-FROST-1 --base origin/feature/unity-rework) on b432cc1fb, run by
                 the lane's session: rustfmt, the provenance chain, the staging pins, clippy and tests of rh-mapdress,
                 clippy rh-sim, the content registry, the whole rh-sim suite as CI, the parity report. [not run: the
                 full gate (the coordinator's). Beside it: cargo test -p rh-server, 49 targets green.] One docs-only
                 commit (this file) follows the gated commit. See "Gate, pins and review" for the first run's failure.
CHANGED:         crates/rh-sim/src/mapgen/attempt/mod.rs (the soft retry; its unit tests; the `fans` doc)
                 crates/rh-sim/src/mapgen/landmarks/signature/spec.rs (the cave's fan and FanHold::Soft; FanHold's doc)
                 crates/rh-sim/src/mapgen/landmarks/signature/mod.rs (fan_hold, hard_fan_holds_a_site_camp; docs)
                 crates/rh-sim/src/mapgen/style_rime/{shape.rs, mouth_lake.rs (new), targets.rs, lakes.rs,
                 routes.rs, mod.rs, report.rs (new, test-only), tests.rs, README.md}
                 docs/adr/0028-signature-landmarks.md (decision 14 corrected; decision 15 new)
                 docs/coordination/HANDOFF_P-LAND-FROST-1_2026-11-14.md
D-RULES:         D1: option off byte-identical (every pinned_off user green: mapgen_signature_{frost, frost_lake, scale,
                 reserve, church, hall, fire, bog_tree}, mapgen_land_frost); no legacy counterpart (DEV-020). D2 n/a.
                 No sim tick, wire, content hash or Unity change. D5/D6/D9 untouched.
DEVIATIONS:      none (signature mode is DEV-020's)
SHARED RECORDS:  proposed edits: docs/MIGRATION.md checkpoint: P-LAND-FROST-1 gate-green on targeted tests, the
                 measurements below, the four red digest pins for the lane to re-take; docs/backlog/README.md: the
                 packet's row to "implemented, awaiting review".
LEGACY DOUBTS:   none (option off is legacy's Frost styler and does not move)
NOT ESTABLISHED: the full gate and the full rh-sim suite (not run, per brief); the rh-mapdress goldens (not run);
                 a picture of real output on eight seeds for the owner (not made); that the fallback ever keeps a map
                 (on seeds 1-128 every attempt it fires on is refused, see below); seeds beyond 128.
RISKS:           the ice share 0.809 against the owner's floor 0.8 (one percent of margin, as decided); line_floor 85
                 against 93; whole_floor 21 against 25; the four fallback seeds now take later attempts (seed 62
                 keeps attempt 9, worst stays 10 on seed 16).
```

## What was built

1. **The room** (`spec.rs`): the cave's entry carries `fan: Some(Fan { depth: d.cave_fan_depth, side:
   d.cave_fan_side })` (24, 0) with `fan_hold: FanHold::Soft`; the volcano's stays `Hard`.
2. **The soft retry** (`attempt/mod.rs`, the Prefix block of P-MAPGEN-SIG-SCALE-4): the first try is today's with every
   fan reserved (`fan_holds_a_site_camp`, then `place_camps_reserving`). When it fails and a soft fan was reserved, the
   round is tried once more with the soft fans left out: `hard_fan_holds_a_site_camp`, then `place_camps_reserving` on
   the same `rng` (no reset; a first try that failed at the site-camp check drew nothing). On success `fans` holds only
   the hard fans (the given-up room is not recorded); on failure the round fails as before. A hard fan is never given
   up. Readers in `signature/mod.rs`: `fan_hold(r)` and `hard_fan_holds_a_site_camp` (one private `fan_holds_with`).
3. **The Frost styler, signature mode** (`style_rime/mouth_lake.rs`, `shape.rs`, `routes.rs::ensure_wet`,
   `lakes.rs::Lake::from_set`): the prototype's build, ported without its knobs: the near shore at the gap, else the
   free tile nearest the mouth (the prototype's `RH_PROTO_FROST_TARGETS` branch, now always on); the mouth lake grown
   best-first to 400 tiles on the line; the dry way round one flank (planned and cut out of the lake when the lake left
   none); one second lake from today's second seed, to `min(100, 75% of the mouth lake)` (also the prototype's targets
   branch, now always on); the spawn routes round both lakes; re-ice and refill to size after headlands and spires,
   kept to one body. Its figures are `mouth_lake::build_rule()` (lake_target 400, second_target 100,
   second_most_percent 75, aspect 1.25, noise 0.45, way_lake_cost 8, route_rounds 3); the rule's numbers are
   `targets::lake_rule()`. No `const`, no env read, no `proto` code, no debug print (`rg "RH_PROTO|proto_"
   crates/rh-sim/src` finds nothing).
4. **The three checks** (`targets.rs`): `mouth_lake_holds`, `dry_way_holds`, `second_lake_holds`, the oracle's
   definitions as pure reads; not registered (Frost's list stays `["lake-at-mouth"]`; `guarantees.rs` unchanged).
   `Line` lives here and is shared with the build.
5. **The fallback (new)** (`shape.rs`): when `mouth_lake::build` gives no mouth lake the plan can keep (no line, or
   the built mouth lake under `lake_min` = 40, which the plan drops together with the smaller second lake, so the zone
   would end with no ice), the styler takes that map back to the signature growth it had before this packet, on a
   fresh route book: `grow_lakes_at` (the mere seeded within reach of the mouth) else `grow_lakes`, then `fill`, then
   `keep_off_routes` after a seeded mere; `lake-at-mouth` decides the map as before. It is "today's lake growth" read
   as today's *signature* growth (the brief's trap names `lakes::grow_lakes`, which is that path's own fallback; seeding
   at the mouth gives the guarantee its best chance). It draws nothing (the growth reads the noise only).

   **Where it fires (seeds 1-128, attempts tried by the oracle):** 7 attempts, the built mouth lake 2-36 tiles: seed 13
   attempt 0 (22 tiles), 33/0 (12), 62/5 (7), 74/0 (36), 91/0 (3), 110/4 (4), 119/0 (2). Every one of them is refused:
   33/0, 62/5, 74/0 and 119/0 by `lake-at-mouth` (the lake today's growth lays is not within reach of the mouth: the
   ground there is boxed in, which is why the build came out small), 13/0 and 110/4 by `Repair`, 91/0 by
   `SignatureDiscarded`. So **no kept map is a fallback map**: seeds 33, 62, 74 and 119 (the prototype's four maps with
   no lake) now keep attempts 4, 9, 2 and 1, each with a built mouth lake (152, 322, 399, 43 tiles). Without the
   fallback those attempts kept a map with no Frost ice, which `lake-at-mouth` passes vacuously; the unit test below
   is red on 33, 74 and 119 without it.

## Draws added

- Prefix: on a retry only, `place_camps_reserving` continues the camp stream; nothing else. No zone stream touched.
- Frost styler: none (the build and the fallback read the noise only). The headlands and spires draw as before, at the
  places the new lakes give them, so every option-on Frost map moves (allowed). Option off draws nothing new.

## Tests

| Target | Result |
|---|---|
| `mapgen_land_frost` | 13 of 13 green |
| `mapgen_signature_site_rules` 11, `_frost` 7, `_frost_lake` 5, `_fire` 11, `_volcano` 20, `_hall` 18, `_reserve` 11, `_core` 8, `_aprons` 2, `_church` 13, `_scale` 10, `_bog_tree` 9, `mapgen_landmark_frame` 8, `mapgen_land_fire` 16, `mapgen_land_plague` 27, `mapgen_land_sentenced` 11, `content` 21 | all green |
| `--lib style_rime` 15 (+1 ignored report), `--lib landmarks::signature` 12, `--lib attempt` 9 | green |
| `cargo fmt --all -- --check`; `cargo clippy -p rh-sim --lib --tests --features trusted-status-probes -- -D warnings` | clean |
| `mapgen_signature_blueprint` `option_on_maps_are_unchanged`, `the_landmarks_phase_is_unchanged` | red, expected (digest pins; the other 8 green) |
| `mapgen_guarantees` `the_refusal_labels_of_seeds_1_to_32_are_todays_relabelled` (seed 1 now `["Signature"]`, pinned `["Guarantee { zone: Frost, rule: \"lake-at-mouth\" }"]`), `a_lake_failure_is_refused_after_records_as_the_frost_guarantee` (2 `lake-at-mouth` refusals on seeds 1-32, pinned 6) | red, expected (the other 5 green) |
| rh-mapdress goldens | not run |

New unit tests: `attempt::soft_fan::a_soft_fan_given_up_at_the_site_camp_check_leaves_the_round_as_without_it` (a soft
whole-map fan gives every attempt of seeds 1-8, attempts 0-3, the same frame, rng included, as a table without the
cave's fan: the given-up first try drew nothing and the retry is the round without it);
`attempt::soft_fan::a_hard_fan_is_never_given_up_and_a_kept_room_is_recorded`;
`style_rime::tests::a_built_lake_under_lake_min_falls_back_to_todays_growth` (with the guarantees off no attempt-0 map
of seeds 1-128 ends without unowned Frost ice; checked red on [33, 74, 119] with the fallback disabled).

Printing the red pins' new values (for the lane's session): `cargo test -p rh-sim --features trusted-status-probes
--test mapgen_signature_blueprint print_signature_pins -- --ignored --nocapture` (the table for
`tests/support/signature_pins.rs`); `mapgen_guarantees`' two tests print the new labels and count in their failure
messages.

## Measurements (seeds 1-128, option on, no decor, 16 attempts; the oracle's printed lines and `style_rime::report`)

| Value | Bound | This build | Prototype (decision) |
|---|---|---|---|
| seeds with a map | 128 (hard) | 128 | 128 |
| worst kept attempt (index, seed) | at most 12 (hard) | 10, seed 16 | 10, seed 16 |
| kept maps with a lake at the mouth / passing `lake-at-mouth` / with stray ice | 128 / 128 / 0 (hard) | 128 / 128 / 0 | 124 / 128 / 0 |
| mouth lakes of 340-460 tiles | floor 40 | 47 | 46 |
| median mouth lake (tiles) | floor 280 | 314 | 305.5 |
| exactly 3 dry tiles mouth to shore (gap) | floor 97 | 113 | 111 |
| centroid within 4 of the line | floor 85 | 93 | 90 |
| a dry way 3 wide | floor 110 | 128 | 124 |
| one smaller second lake apart | floor 92 | 105 | 104 |
| the whole rule at once | floor 21 | 25 | 25 |
| ice share on / off, median seeds 1-32 | 0.8-1.2 (owner) | 0.809 | 0.810 |
| spires, headlands, seeds 1-32 | at least 25, 72 | 38, 123 | 38, 123 |
| the room kept / given up (kept maps) | kept at least 100, given up at least 1 | 113 / 15 | 114 / 14 |
| Fire camps without a crater, seeds 1-64 (all; most on one map) | at most 40; at most 5 | 18 of 640; 2 (seed 64; seeds 65-128: 2, seed 94) | 18; 2 |
| Plague Large pools, near-band pools, seeds 1-32 (DEV-034) | at most 2, 2 | 0, 0 | — |

Also: the mouth-lake target (size, line and gap together, `mouth_lake_holds`) on 29 kept maps. Mouth lake size min /
p10 / median / max 41 / 154 / 314 / 400. Attempts on seeds 1-32 / 1-64 / 1-128: 97 / 182 / 345 (prototype 97 / 174 /
334; the difference is the four fallback seeds taking later attempts). Refusals by label over seeds 1-128: Prefix 68,
Repair 50, Signature 48, SignatureDiscarded 16, SpawnRatio 19, SpawnPool 6, `lake-at-mouth` 10; over seeds 1-32: Prefix
21, Repair 16, Signature 15, SignatureDiscarded 3, SpawnPool 3, SpawnRatio 5, `lake-at-mouth` 2. Frost camp nearest
neighbour mean 18.51, min 9.0 tiles (prototype 18.56 / 9.0; today 19.57 / 9.0). Time: the report's 128 seeds with every
refused attempt and one Prefix replay per seed took 23.5 s sequentially in the optimised test build (about 0.18 s per
kept map, about 68 ms per attempt); the oracle's binary 6.7 s on eight threads in the debug test build.

Per seed (`seed attempt mouth-lake-size [other lakes] mouth-lake-target dry-way second-lake fans-recorded`; fans 2 =
the room kept, 1 = given up):

```
1 1 371 [100] 0 1 1 2;2 2 400 [100] 0 1 1 2;3 1 351 [100] 0 1 1 2;4 1 228 [100] 0 1 1 2;5 0 236 [100] 0 1 1 2;
6 0 179 [100] 0 1 1 2;7 1 386 [41] 0 1 0 2;8 2 353 [100] 1 1 1 2;9 0 298 [100] 0 1 1 2;10 2 378 [83] 1 1 1 2;
11 0 400 [100] 0 1 1 2;12 1 342 [42] 1 1 0 2;13 2 223 [100] 0 1 1 2;14 1 273 [57] 0 1 0 1;15 3 301 [73] 0 1 1 2;
16 10 279 [100] 0 1 1 2;17 1 362 [40] 1 1 0 2;18 3 332 [100] 0 1 1 2;19 0 400 [6] 0 1 0 1;20 4 400 [28] 0 1 0 1;
21 4 319 [41] 0 1 0 2;22 0 357 [100] 0 1 1 2;23 0 349 [100] 1 1 1 2;24 0 387 [31] 0 1 0 2;25 5 303 [100] 0 1 1 2;
26 0 353 [33] 0 1 0 2;27 0 162 [100] 0 1 1 2;28 5 400 [100] 1 1 1 2;29 6 337 [100] 0 1 1 2;30 4 175 [100] 0 1 1 2;
31 1 244 [86] 0 1 1 2;32 5 272 [100] 0 1 1 2;33 4 152 [70] 0 1 1 1;34 5 118 [88] 0 1 1 2;35 6 308 [100] 0 1 1 2;
36 0 294 [100] 0 1 1 2;37 0 204 [100] 0 1 1 2;38 0 347 [100] 1 1 1 2;39 3 43 [] 0 1 0 1;40 2 288 [100] 0 1 1 2;
41 0 260 [100] 0 1 1 2;42 0 290 [41] 0 1 0 2;43 4 347 [100] 1 1 1 2;44 0 376 [100] 1 1 1 2;45 2 309 [24] 0 1 0 2;
46 0 325 [100] 0 1 1 2;47 0 359 [100] 1 1 1 2;48 0 347 [100] 0 1 1 2;49 1 332 [100] 0 1 1 2;50 1 400 [100] 0 1 1 2;
51 0 356 [100] 1 1 1 2;52 1 257 [100] 0 1 1 2;53 2 400 [100] 0 1 1 2;54 1 247 [100] 0 1 1 2;55 1 276 [100] 0 1 1 2;
56 0 380 [100] 0 1 1 2;57 0 161 [100] 0 1 1 1;58 2 339 [100] 0 1 1 2;59 2 400 [29] 1 1 0 2;60 1 264 [73] 0 1 1 2;
61 5 211 [100] 0 1 1 1;62 9 322 [23] 0 1 0 2;63 0 400 [100] 1 1 1 2;64 1 339 [100] 0 1 1 2;65 0 363 [100] 1 1 1 2;
66 0 324 [100] 0 1 1 2;67 1 269 [38] 0 1 0 2;68 0 400 [100] 1 1 1 2;69 0 347 [63] 1 1 1 2;70 2 341 [100] 1 1 1 2;
71 0 373 [100] 1 1 1 2;72 1 400 [100] 0 1 1 2;73 4 190 [100] 0 1 1 2;74 2 399 [55] 1 1 0 2;75 0 303 [100] 0 1 1 2;
76 1 116 [87] 0 1 1 2;77 0 295 [100] 0 1 1 2;78 1 346 [100] 1 1 1 2;79 0 347 [100] 1 1 1 2;80 1 400 [100] 1 1 1 2;
81 1 137 [49] 0 1 0 2;82 2 255 [100] 0 1 1 2;83 3 161 [100] 0 1 1 1;84 2 280 [32] 0 1 0 2;85 4 285 [100] 0 1 1 2;
86 4 325 [100] 0 1 1 2;87 0 400 [100] 1 1 1 2;88 0 178 [73] 0 1 1 2;89 1 150 [100] 0 1 1 2;90 3 160 [100] 0 1 1 2;
91 2 366 [100] 0 1 1 2;92 4 369 [100] 1 1 1 2;93 0 221 [100] 0 1 1 2;94 4 339 [81] 0 1 1 2;95 0 322 [100] 0 1 1 2;
96 6 130 [57] 0 1 0 2;97 0 226 [87] 0 1 1 2;98 2 105 [78] 0 1 1 2;99 0 295 [100] 0 1 1 1;100 2 220 [100] 0 1 1 1;
101 3 342 [100] 1 1 1 2;102 1 280 [82] 0 1 1 2;103 3 41 [] 0 1 0 2;104 4 321 [100] 0 1 1 2;105 1 139 [100] 0 1 1 1;
106 0 154 [100] 0 1 1 1;107 0 355 [100] 0 1 1 2;108 5 273 [100] 0 1 1 2;109 1 226 [100] 0 1 1 2;110 6 326 [100] 0 1 1 2;
111 0 86 [64] 0 1 1 1;112 6 341 [100] 1 1 1 1;113 7 275 [100] 0 1 1 2;114 0 333 [100] 0 1 1 2;115 0 392 [100] 1 1 1 2;
116 1 184 [100] 0 1 1 2;117 0 400 [100] 0 1 1 2;118 2 362 [100] 1 1 1 2;119 1 43 [] 0 1 0 2;120 1 323 [100] 0 1 1 2;
121 0 379 [100] 1 1 1 2;122 2 297 [100] 0 1 1 2;123 0 292 [100] 0 1 1 2;124 0 290 [100] 0 1 1 2;125 1 336 [27] 0 1 0 1;
126 0 271 [19] 0 1 0 2;127 0 300 [100] 0 1 1 2;128 1 159 [95] 0 1 1 2;
```

The per-seed ice-share ratios and spire/headland counts are not in this table (the oracle prints only their median and
totals); `style_rime::report` reprints the table: `cargo test -p rh-sim --features trusted-status-probes --release
--lib style_rime::report -- --ignored --nocapture`.

## What the brief got wrong or left open

- The fallback "leaves no map without a lake at the mouth" holds, but not by keeping fallback maps: on seeds 1-128 the
  maps today's growth gives on those attempts are all refused (4 by `lake-at-mouth`), and the seeds move to later
  attempts. That costs 11 attempts on seeds 1-128 (345 against 334) and nothing on the worst attempt.
- The trap names `lakes::grow_lakes`; I built today's signature path (`grow_lakes_at`, then `grow_lakes` if it finds no
  seed), which is what "today's lake growth" was in signature mode before this packet.
- Two prototype behaviours the measurement depended on were behind `RH_PROTO_FROST_TARGETS` (the free-tile fallback for
  a blocked near shore and the second lake capped at three quarters of the mouth lake); they ship unconditionally.
- The room kept on 113 and given up on 15 (scratch check: 114 / 14): the seeds whose kept attempt moved.
- The hand-off of P-MAPGEN-SIG-HOLY-1-fix said the table means "no zone or feature kind decides" the site rules and did
  not say that `from_table`'s pin-glyph check is debug-only or why `reserve` and `streams` are public. Corrected in ADR
  0028 decision 14: Plague's line rule and its deepest-square fallback in `reserve` and the volcano's streams in
  `place` are still decided by zone or kind; the check is a `debug_assert!` (debug builds only); `reserve` and
  `streams` are public for the test seam. The soft fan is decision 15.
- `fan_holds_a_site_camp`'s doc said "no site with a camp has a fan today"; the cave now has both (its own camp is
  excepted from its own room). Updated.

## Gate, pins and review (the lane's session, 2026-11-15)

**Read first.** Every option-on map moves again (the cave's soft room in the Prefix; the lake built first). Attempts
on seeds 1-32: 96 -> 97; worst attempt 11 (seed 11) -> 10 (seed 16). No seed of 1-128 is without a map. Values at
or near a bound on this build (seeds 1-128 unless said):

| Guard | Measured | Bound | Whose |
|---|---|---|---|
| Ice share against option off, median, seeds 1-32 | 0.809 | 0.8 to 1.2 | the owner's ("within about a fifth") |
| Centroid within 4 of the line and ahead of the mouth | 91 | at least 85 | the lane's floor |
| The whole lake rule at once | 25 | at least 21 | the lane's floor |
| Worst kept attempt index | 10 | at most 12 | the stack's |
| Maps with a mouth-lake tile behind the mouth's front / with the centroid behind it | 106 / 5 | at most 115 / 8 | a known gap, with the owner |
| Fire camps without a crater, seeds 1-64 / most on one map | 18 of 640 / 2 | 40 / 5 | last set on the lane's judgment |
| Plague Large pools / near-band pools, seeds 1-32 | 0 / 0 | 2 / 2 | last set on the lane's judgment |

- **Gate:** PASS (quick, `--oracle packet/P-LAND-FROST-1 --base origin/feature/unity-rework`) on the commit named in
  the block below, run by the lane's session in this worktree's own build folder: path checks, rustfmt, the card
  provenance chain, the staging pins, clippy and tests of rh-mapdress, clippy rh-sim, the content registry, the whole
  rh-sim suite as CI, the parity report. Not run: the full gate (the coordinator's). Beside it: `cargo test -p
  rh-server`, 49 targets green (on 4ba89dbe7, before the two review fixes, which move no map).
  History: the first quick gate (on e38811243) FAILED on one test, `mapgen_signature_table.rs`
  `each_landmark_placed_alone_equals_it_placed_with_the_others`: "only 19 accepted Landmarks phases" against a
  sample floor of 20 of 32; the whole suite with `--no-fail-fast` showed that test alone. The lane's session widened
  its sample (below); the gate then passed on 4ba89dbe7 and again after the review fixes.
- **Pins re-taken from this build** (`re-pin:` commit on the packet branch, constants only, with `repin.py`):

  | Expectation | Old | New |
  |---|---|---|
  | `signature_pins.rs` `pinned_on_maps` (seeds 1-32, option on) | 96 attempts, worst 11 (seed 11) | all 32 rows moved; 22 seeds keep another attempt; 97 attempts, worst 10 (seed 16), then 6 (seed 29), 5 (seed 32) |
  | `signature_pins.rs` `pinned_landmarks` (seeds 1-16, attempts 0 and 1) | | 29 of 32 rows moved (the cave's room is reserved in the Prefix) |
  | `mapgen_guarantees.rs` `pinned_labels` | 64 refusals over seeds 1-32 | 25 seeds' rows changed; 65 refusals: Prefix 21, Signature 15, Repair(RadiusPost) 15, SpawnRatio 5, SignatureDiscarded 3, SpawnPool 3, lake-at-mouth 2, Repair(FloorShare) 1 |
  | `mapgen_guarantees.rs` the lake-refusal count | 6 | 2 |
  | `rh-mapdress` `NO_ANCHOR_GOLDEN` / `SIG_GOLDEN` / `NO_ORIGIN_GOLDEN` | | 4 of 4 / 16 of 16 / 4 of 4 values |
  | `pinned_off`, the rh-mapdress option-off goldens | | unchanged |

- **Existing tests amended** (each an `oracle-amend:` commit on the packet branch):

  | Test | Old | New | Why |
  |---|---|---|---|
  | `mapgen_signature_site_rules.rs` `the_table_holds_todays_site_camp_spawn_and_fan_rules` | the cave has no fan | the cave's fan from the definition (24, 0), soft | the owner's decision |
  | `mapgen_signature_site_rules.rs` `an_entrys_fan_is_reserved_with_its_numbers` | doctored table removes no fan | it also removes the cave's fan | so that it still records only its two doctored fans |
  | `mapgen_signature_table.rs` `each_landmark_placed_alone_equals_it_placed_with_the_others` | seeds 1-16 (32 phases), floor 20 accepted | seeds 1-32 (64 phases), floor 30; 40 accepted, the count printed | with the Holy site and the cave's room 19 of 32 were accepted, one under the floor; the floor only guards the sample's size, and the comparison now runs on more phases than it ever did |
  | `mapgen_land_frost.rs` the line target (review note 2) | centroid within 4 of the line through the mouth (93 maps) | and ahead of the mouth (91 maps); renamed `the_mouth_lake_centroid_stands_ahead_of_the_mouth_on_the_line_on_most_maps`; floor 85 unchanged | the old reading counted a centroid behind the mouth |
  | `mapgen_land_frost.rs` `known_gap_the_mouth_lake_reaches_behind_the_mouths_front` (review note 2) | (new) | at most 115 maps with a mouth-lake tile behind the mouth's front (106), at most 8 with the centroid behind it (5); fails when the first falls to 5 or fewer, so that a packet closing the gap tightens it | "in front of the mouth" was not tested |

- **Owner-directed tests:** the owner's rule for the lake is a set of TARGETS by his decision of 2026-11-14 ("Yes to
  both"): each part is a counted floor above today's value and stated against its measurement; none passes on a
  handful of seeds. His own number, the ice share window, is kept exactly (0.8 to 1.2) and the build sits at 0.809:
  a later packet that moves it under 0.8 stops and goes to him.
- **What the owner has in front of him (2026-11-15), no answer yet:** this build lets the lake use ground beside and
  behind the cave: on 106 of the 128 kept maps some tile of the mouth lake lies behind the mouth's front, on 5 the
  lake's centroid does (seeds 3, 11, 19, 54, 83). Three variants were built and measured on seeds 1-128 and shown to
  him on eight seeds (today / this build / a rounded lake):

  | | today | this build ("stretched") | rounded (scratch `agent/opus/P-LAND-FROST-1-shape` @ 100c37f1c) | never behind the mouth (scratch `agent/opus/P-LAND-FROST-1-front` @ 18c5b383d) |
  |---|---|---|---|---|
  | all 13 oracle tests | | yes | no (3 red) | no (2 red) |
  | lakes of 340-460 tiles | 30 | 47 | 22 | 32 |
  | median lake | 254 | 314 | 281 | 287 |
  | the whole rule | 0 | 25 | 18 | 18 |
  | ice share, seeds 1-32 | 0.64 | 0.809 | 0.833 | 0.836 |
  | a lake tile behind the mouth / the centroid behind | not measured | 106 / 5 | 2 / 0 | 1 / 0 |
  | spires / headlands, seeds 1-32 | | 38 / 123 | 18 / 112 | 41 / 109 |

  This build is the behaviour whose numbers he approved and the only one that passes every floor, so it is the one
  sent; "never behind the mouth" is ready on its scratch branch and needs two of the lane's floors lowered (lakes of
  340-460 from 40 to about 30; the whole rule from 21 to about 15), which only his word allows. The straight
  diagonal sides of the lakes are in today's maps too (the lane's reading of the pictures: route bands, camp halos
  and the zone's edge cut them; not measured).
- **Review:** reviewer-opus on 4ba89dbe7: **LAND WITH NOTES**, two notes; it re-ran `mapgen_land_frost` (13 of 13,
  the hand-off's numbers exactly) and read the Prefix's retry, the table, the styler, the oracle and the re-pins.
  - **Note 1, fixed (the last code commit):** `style_rime/shape.rs` clamped the plan's `grown` to `lake_min` after the
    regrowth that follows shelves and spires. It now records the lake's area before them, as with the option off
    (at least `lake_min` by the filter above it). No map moves: every pin and golden is green unedited.
  - **Note 2, fixed (the two `mapgen_land_frost.rs` rows above):** the oracle did not test "in front of the mouth"
    and its line count accepted a centroid behind the mouth.
  - **Raised, not a note, with the owner:** "every kept map has a lake at the mouth" is observed on seeds 1-128, not
    guaranteed: if the built lake and the fallback both yield nothing (or the regrowth after shelves empties the
    lake), `lake-at-mouth` passes a map with no Frost ice and the map is kept. Registering "a Frost lake exists" as a
    refusal would close it and could cost a seed: his trade.
- **Minor, not notes** (the reviewer's list; no packet): the brief's acceptance table names
  `a_kept_room_keeps_camp_halos_and_legacy_buildings_out` (the test is `..._the_legacy_stages_walls_out`); this
  file's BRANCH line named a commit that is on no branch (corrected below); `soft_fan::a_hard_fan_is_never_given_up_
  and_a_kept_room_is_recorded` asserts only one kept room over 8 seeds (the oracle counts 113); the unit test of the
  retry covers a first try that failed the site-camp check, not one that failed in placement (pinned by the digests
  only); `targets.rs` and `mouth_lake.rs` keep their numbers out of the content hash because they are read in
  signature mode only (the existing convention).
- **Picture:** eight seeds of this build beside today's and beside the rounded variant, sent to the owner
  (2026-11-14 and 2026-11-15): the gap of 3 dry tiles, a dry way and a second lake on all eight, no stray ice; the
  whole rule on seed 8 only; seed 3's lake wraps the cave's side (its centroid is behind the mouth).
