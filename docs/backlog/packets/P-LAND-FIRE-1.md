# P-LAND-FIRE-1: Fire craters keep clear of the volcano's three streams and stand on their camps; a camp's crater shrinks to fit, and about 1 camp in 20 goes without (the owner's measured gap)

- **Serves:** owner direction for the land-generation lane (`docs/coordination/LAND_GENERATION_REQUIREMENTS.md`
  section 3, Fire item and "Every zone" item; it answers the landmark lane's open question, section 6 fourth item), as
  amended by the **owner's decision of 2026-11-14** (section 0 below). ORDER_OF_OPERATIONS: the signature-map work
  (ADR 0028) under the owner's map-generation direction; not a sim tick, wire or Unity change.
- **Implementer tier:** implementer-opus (rh-sim mapgen: the Fire styler in signature mode, an end-of-attempt guarantee
  that changes which attempts are kept; every option-on map may move)
- **Reviewer:** reviewer-opus
- **Integration base:** `origin/feature/unity-rework` @ `2f33045d6` (the packet branch was rebased onto it; SCALE-4's
  volcano is in it). Gate with `scripts/gate.sh --oracle packet/P-LAND-FIRE-1` (and `--oracle-check` on this branch;
  add `--base origin/feature/unity-rework` if `docs/agent/INTEGRATION_BRANCH` does not resolve to origin's tip).
- **D-rules touched:** D1: option off byte-identical (G8; the new code runs only with a Fire frame, the guarantees only
  with the option on); no legacy counterpart (DEV-020: legacy has no volcano) · D2 n/a · no sim tick, wire, protocol or
  Unity change; nothing new is serialized or hashed (guarantees are generator rules, not content: `style_zones::guarantee`).
- **Status:** oracle written 2026-11-14 (oracle-author, Opus); **amended the same day to the owner's decision of
  2026-11-14** (this revision). The hard-rule build exists (`agent/opus/P-LAND-FIRE-1` @ `9f8e23e38`); the implementer
  changes it as section 4 says.
- **Sibling packets prepared at the same time (keep out of their files):** P-LAND-FROST-1 (`style_rime/`),
  P-MAPGEN-SIG-HOLY-1 (`landmarks/signature/`, the Prefix).

## 0. The owner's decision of 2026-11-14 (the authority for this revision)

The hard-rule build (every camp keeps its crater, refused otherwise) lost seeds 27, 30 and 70 in 16 attempts (its
hand-off `docs/coordination/HANDOFF_P-LAND-FIRE-1_2026-11-14.md` on `agent/opus/P-LAND-FIRE-1`). The owner was asked
three questions, each worded so that "yes" accepts the recommendation, and answered **"Yea to all 3"**. Question 1,
verbatim:

> **Fire craters:** "land the gentler rule now, with craters guaranteed clear of lava, and record 'about 1 camp in 20
> without a crater' as a measured gap."

(The alternatives offered and not taken: move the Fire camps away from the streams and the map corners now; or the hard
rule with its lost maps.) Questions 2 and 3 concern the Holy hall (P-MAPGEN-SIG-HOLY-1), not this packet.

What "yes" means in the generator:
- **Hard (end-of-attempt guarantees):** `crater-clear-of-lava` (as built) and `crater-on-camp` (every Fire crater
  stands on a Fire camp: the second half of the old `crater-per-camp`).
- **Not a guarantee any more:** "every Fire camp has a crater". A camp's crater takes its band's option first, then each
  smaller allowed size (never larger, never under `radius_min4`), skipping a size whose disc holds stream or pool lava,
  retrying a size whose rim is too short as a bare ring; **a camp goes without only when no allowed size fits; no
  attempt is refused for a missing crater; no seed loses its map.**
- **The owner's direction "every Fire camp keeps its crater" is NOT delivered.** The test that stood for it
  (`every_fire_camp_has_its_crater`, and its `wide_` twin) is replaced, **by the owner's decision**, with a measured-gap
  pin, `known_gap_about_one_fire_camp_in_twenty_has_no_crater`: a ceiling near today's figure and a floor of one, so
  that the day the gap closes the test says so and the rule can be promoted to a guarantee.

Standing rules, unchanged: guarantees are hard; numbers are targets (a number that cannot be met goes to the owner with
its measurement); a landmark or zone rule never loses a seed; option off stays byte-identical; the owner sees a picture
of real output on eight seeds before the merge request.

### The measured gap the owner accepted

Prototype variant V1 (`RH_PROTO_FIRE_SOFT=1`) on the scratch branch `agent/opus/P-LAND-FIRE-1-soft-measure` @
`3987501d9` (never merged): the build with the per-camp half of the guarantee removed. Option on, no decor, 16 attempts.

| | seeds 1-32 | seeds 1-64 | seeds 65-128 |
|---|---|---|---|
| seeds with a map | 32 | 64 | 64 |
| attempts sum / worst index | 94 / 9 | 197 / 10 | 194 / 9 |
| Fire camps without a crater | 18 | 32 of 640 | 37 |
| maps with every camp cratered | | 36 | 33 |
| most camps without on one map | | 2 (seeds 10, 29, 54, 55) | 3 (seed 114) |
| Fire guarantee refusals | 0 | 0 | 0 |

Today's game (the packet stub): 36 of 640 on seeds 1-64 (18 on 1-32), 35 maps complete, worst map 3 (seed 50).
Causes on seeds 1-64 (V1): stream or pool lava in every allowed disc 15 (camps 4.0 to 5.39 tiles from lava); rim too
short at every size 14 (frontier and edge camps; four at map corners with 1 to 3 open ring tiles); the runtime refused
the pen's share 3. By band: far 17 of 327, mid 6 of 69, near 9 of 223, core 0 of 21. Other V1 figures, seeds 1-64:
craters below their band's option 3 (near, by 4 quarter tiles), above 0; kept as a bare ring 16; crater share on/off
median 0.952 (1-32) / 0.973 (1-64); near (core + near) over far mean tiles 1.338; least crater tile to lava
(Chebyshev) 4 (3 on seeds 65-128, which the clearance of 2 dry tiles allows).

Not taken, recorded for a later packet if the owner asks: a lowered rim minimum (6) rescues 5 of the 14 rim camps and
needs a `CraterRules` field (content hash moves); letting a camp grow rescues none; the 15 lava camps need a smaller
smallest crater or stream steering; giving every camp a crater means placing Fire camps farther from the streams and
the map corners (the Prefix; every map moves; not measured).

## 1. Today's code (base `2f33045d6`)

- `crates/rh-sim/src/mapgen/style_scorch/craters.rs` `place_craters`: one crater (a `Crater` pen) centred on each Fire
  camp. Option off (no anchor): legacy's freer-share choice among the camp's radius options, as
  `packages/shared/src/zoneStyle/scorch.ts:46-60` (`R_MIN4 = 20`, `R_MAX4 = 26`, `R_MAX4_ELITE = 30`, `RIM_MIN = 8`;
  `:506-541` legacy also leaves a crater out when its rim is short or it stretches a route: option off has 8-10
  craters for 10 camps). Signature mode: the camp's band (`camp_band`) picks **one** option (near the largest, mid the
  second, far the smallest; core any that keeps out of the core); if that one option fails the rim, route-stretch or
  core checks the camp has no crater.
- `craters.rs` `lava_apron`: every tile within `signature::DEFAULT_DEFINITION.bands[0]` = 2 (Chebyshev) of a lava or
  ember tile owned by a `LavaStream` or `LavaPool` feature; the crater's bowl floor is cut out of it and its rim kept
  out by `no_block`. `SignatureContext::intact` discards a map with any styler tile in that apron. So today's clearance
  is 2 tiles, already kept on every kept map; but the crater is centred on the camp regardless of the stream, so a
  stream can run through a crater's ring with the ring merely cut where it passes.
- `style_zones/guarantee.rs` `guarantees`: Fire returned `&[]` (the stub points it at `style_scorch::guarantees`, still
  empty).
- The radius options: `search_family::scorch::radius4` R = clamp(floor(2 * nearest other Fire camp - 3.2), 20, 26 or 30
  elite); options R, R - 4, R - 8 (`CraterRules::radius_shrink4`), those >= `radius_min4` = 20.

## 2. Baseline measurements (oracle author, before the rebase; the stub's figures above confirm them)

| Measure | Seeds 1-32 | Seeds 1-64 |
|---|---|---|
| Fire camps per map | 10 | 10 |
| craters per map, option on | 8-10 | 7-10, mean 9.44 |
| craters per map, option off | 9-10 | 8-10, mean 9.88 |
| camps without a crater, option on | 18 camps on 15 maps | 36 camps on 29 maps |
| craters with stream or pool lava inside their disc (radius p[1]/4) | 8 craters on 8 maps (seeds 8, 15, 19, 24, 25, 27, 29, 32) | 15 craters on 13 maps |
| least Chebyshev distance crater tile to stream/pool lava | 3 | 3 (8 craters at 3, 134 at 4; none within 2) |
| least distance camp tile to stream/pool lava (Euclidean) | | 4.0 tiles (7 camps of 640 under 4.5, 15 under 5.5) |
| crater share of free ground on / off (median of per-seed ratios) | 1.001 | 1.002 (per seed 0.73-1.40) |
| attempts (sum) / worst attempt index | 89 / 9 (seed 22) | |

## 3. The rule, as the oracle states it (header of `tests/mapgen_land_fire.rs`)

1. **Every seed keeps its map:** seeds 1-128 each have a map within 16 attempts with the option on.
2. **On its camp:** every Fire crater stands on a Fire camp, one crater per camp at most.
3. **The measured gap (owner decision 2026-11-14):** over seeds 1-64 the Fire camps without a crater number at least
   1 and at most 40; no map of seeds 1-64 or of seeds 65-128 has more than 3.
4. **Clear of the streams:** no stream or pool lava (the Fire frame's LOW lava outside the cone's box) lies in a
   crater's disc (16 * d^2 < p[1]^2 round the camp tile), and no tile a crater owns lies within
   `style_scorch::lava_clearance()` = **2** tiles (Chebyshev) of that lava (seeds 1-128).
5. **Size falls with distance:** a crater's radius is one of its camp's options, at least `radius_min4` = 20 quarter
   tiles, and at most its band's option (core: any option). It may shrink below its band's option but never grows. At
   least 90% (PROVISIONAL) take exactly their band's option; near-band craters average at least 1.15 times the far
   band's tiles (seeds 1-32).
6. **Guarantees** registered for Fire, in order: `"crater-clear-of-lava"` (rule 4), `"crater-on-camp"` (rule 2), each a
   pure read of the finished map and the Fire frame (`fn(&GameMapData, &LandmarkFrame) -> bool`). A map with a camp
   lacking its crater is refused by no Fire rule.
7. **Amounts:** the median over seeds 1-32 of (crater share of free ground, option on, the site taken out) / (option
   off) lies in [0.8, 1.2].
8. **The volcano untouched:** the Fire frame writes the cone then (stream, pool) three times; three Fire `LavaStream`
   and three `LavaPool` features.
9. **Attempts:** worst attempt index <= 12, attempts over seeds 1-32 <= 137 (the stack's figures), and at most **2**
   attempts refused by a Fire guarantee over seeds 1-32 (measured 0).

## 4. The design now: what the implementer changes on `agent/opus/P-LAND-FIRE-1`

Start from `agent/opus/P-LAND-FIRE-1` @ `9f8e23e38` (the hard-rule build, already on this packet's base): rebase it onto
this packet's tip (the new oracle commits), then:

- **Drop the per-camp half of the guarantee.** `style_scorch/guarantees.rs`: replace `crater-per-camp` with
  `crater-on-camp` (every Fire crater's anchor is a Fire camp's tile; a camp without a crater passes), and register
  `crater-clear-of-lava` then `crater-on-camp`. `crater-clear-of-lava` is unchanged.
- **Keep everything else as built:** `place_craters` (band's option, each smaller allowed size, disc skip, bare-ring
  retry); a camp with no fitting size simply gets no crater. Option off untouched.
- **No prototype knobs** (`proto.rs`, environment reads, logs: none of it comes over), **no new draw** (the raster reads
  hash noise only), no `CraterRules` field.
- **In-crate tests for both rules** in `guarantees.rs` (the build's hand-built-map tests, with the per-camp test
  rewritten for `crater-on-camp`: a camp without its crater passes; a crater one tile off its camp, a Frost crater or a
  Fire barrow are read correctly; the registry order).
- **README** (`style_scorch/README.md`): the two rules and the measured gap (owner decision 2026-11-14).
- Doc comments: the stub's `guarantees.rs` header and the Fire arm of `style_zones/guarantee.rs` name the two rules.

## 5. Exclusive write scope

- `crates/rh-sim/src/mapgen/style_scorch/**` (`craters.rs`, `guarantees.rs`, `mod.rs`, `raster.rs`, `rules.rs` only if
  unavoidable (any field added to `CraterRules` changes the content hash: say so in the hand-off), `tests.rs`, `README.md`)
- `crates/rh-sim/src/mapgen/style_zones/guarantee.rs`: the Fire arm and its doc comment only.

Not in scope: `landmarks/signature/**` (volcano, intact), the Prefix, `pipeline/**`, every file under `crates/rh-sim/tests/`
(oracle files and `tests/support/signature_pins.rs`), `Cargo.lock`, docs other than the styler's README.

## 6. Behaviour to preserve

- Option off byte-identical: `mapgen_signature_fire.rs::g8_option_off_maps_are_todays`, the option-off pins in
  `tests/support/signature_pins.rs` (`pinned_off`), the `style_scorch` parity fixtures (`cargo test -p rh-sim
  --all-features --lib style_scorch`).
- Every other zone's tests (`mapgen_land_plague.rs`, `mapgen_land_sentenced.rs`, `mapgen_signature_frost_lake.rs`, the
  volcano, church, bog tree, reserve, scale and table oracles) green with their current numbers.
- **Re-taken by the lane's session, not the implementer** (they pin today's option-on attempts and go red when craters
  change which attempts are kept): `tests/support/signature_pins.rs` option-on map and Landmarks-phase pins
  (`pinned_on_maps`, `pinned_landmarks`, read by `mapgen_signature_blueprint.rs`, `mapgen_landmark_frame.rs` and the
  other `mapgen_signature_*` files), `mapgen_guarantees.rs` `pinned_labels` (the refusal-label sequence of seeds 1-32)
  and the lake-refusal count `seen == 6` in `a_lake_failure_is_refused_after_records_as_the_frost_guarantee`. The
  implementer reports which of these are red and with what values; it does not edit them.

## 7. Acceptance checks

All of `crates/rh-sim/tests/mapgen_land_fire.rs` (17 tests), the amended files below, and the whole rh-sim suite except
the pins listed in section 6 as re-taken.

### Red on the stub (by assertion)

| Test | Assertion on the stub |
|---|---|
| `craters_keep_clear_of_the_streams_and_pools` | STUB_CLEAR |
| `wide_craters_keep_clear_of_the_streams_and_pools` | STUB_WIDE_CLEAR |
| `the_fire_guarantees_are_registered_by_name` | left `[]`, right `["crater-clear-of-lava", "crater-on-camp"]` |
| `the_fire_guarantees_hold_on_every_kept_map`, `a_camp_without_its_crater_is_refused_by_no_fire_rule`, `crater_on_camp_fails_with_a_crater_moved_off_its_camp`, `crater_clear_of_lava_fails_with_a_crater_tile_beside_a_stream`, `crater_clear_of_lava_fails_with_a_crater_disc_over_a_stream` | "no guarantee \"crater-clear-of-lava\" registered for Fire (registered: [])" |
| `mapgen_guarantees.rs` `the_registry_lists_the_fire_frost_and_plague_rules_and_nothing_else` | Fire `[]` against the two rules |
| `mapgen_guarantees.rs` `every_guarantee_holds_on_every_kept_map_of_seeds_1_to_32` | 96 checks, 160 expected |

### Green on the stub: guards that must stay green

`every_seed_of_1_to_128_keeps_its_map` (the hard-rule build fails it: seeds 27, 30, 70),
`every_fire_crater_stands_on_its_own_fire_camp`, `known_gap_about_one_fire_camp_in_twenty_has_no_crater` (the stub's
36 lies inside the owner's window by design: the pin bounds the build against today's game, it does not demand an
improvement), `the_frames_lava_outside_the_cone_is_the_streams_and_pools`,
`crater_size_falls_with_distance_and_shrinks_only_below_its_bands_option`,
`crater_share_of_free_ground_stays_within_a_fifth_of_todays`, `the_volcano_keeps_its_three_streams_and_pools`,
`the_attempt_bounds_hold_and_the_fire_guarantees_refuse_little`.

### Checked against the decided behaviour

MEASURED_TABLE

## Oracle files (read-only for implementers)

- `crates/rh-sim/tests/mapgen_land_fire.rs` (`oracle:` `259596c3f`; amended by `oracle-amend:` AMEND_FIRE)
- `crates/rh-sim/tests/mapgen_guarantees.rs` (`oracle-amend:` `f497c1a92`, then AMEND_GUAR)
- `crates/rh-sim/tests/mapgen_signature_fire_bands.rs` (`oracle-amend:` `3d574d4c6`, then AMEND_BANDS (comment only))
- `docs/backlog/packets/P-LAND-FIRE-1.md` (this brief)
- `docs/coordination/LAND_GENERATION_REQUIREMENTS.md` (section 3, Fire: the decision of 2026-11-14 added under the
  owner's sentence)

Stub (the implementer replaces or edits; `stub:` commit `e5f3987da`): `style_scorch/guarantees.rs` (empty
`guarantees()`, `lava_clearance()` = `bands[0]`; its header still names the old `crater-per-camp`: the implementer
rewrites it), `style_scorch/mod.rs` (`mod guarantees;`, `pub use`), `style_zones/guarantee.rs` (Fire arm points at
`style_scorch::guarantees`). No new stub was needed for this revision.

## 8. Existing and earlier oracles amended (each in an `oracle-amend:` commit, for the owner)

| File, test | Old | New | Why |
|---|---|---|---|
| `mapgen_guarantees.rs` `the_registry_lists_the_fire_frost_and_plague_rules_and_nothing_else` | `(Zone::Fire, vec![])` (before this packet); then `["crater-per-camp", "crater-clear-of-lava"]` | `(Zone::Fire, vec!["crater-clear-of-lava", "crater-on-camp"])` | the Fire guarantees as decided 2026-11-14 |
| `mapgen_guarantees.rs` `every_guarantee_holds_on_every_kept_map_of_seeds_1_to_32` | `checked == 32 * 3` | `checked == 32 * 5` (message names the new rules) | two Fire checks per map |
| `mapgen_signature_fire_bands.rs` `craters_take_their_bands_radius_option` | outside the core, radius `==` the band's option | radius one of the camp's options and `<=` the band's option | a crater may shrink to be kept or to keep clear; it never grows. Loosens a G4 test on purpose. Header comment now names the measured gap (2026-11-14) instead of "every camp has one". |
| `mapgen_land_fire.rs` `every_fire_camp_has_its_crater` | every Fire camp has its crater, seeds 1-32 | **replaced by the owner's decision** with `known_gap_about_one_fire_camp_in_twenty_has_no_crater` (seeds 1-64: 1 <= camps without <= 40; worst map <= 3 on 1-64 and 65-128) and `every_fire_crater_stands_on_its_own_fire_camp` (seeds 1-128) | the owner accepted about 1 camp in 20 without a crater as a measured gap |
| `mapgen_land_fire.rs` `wide_every_fire_camp_has_its_crater` | seeds 33-96 | removed; seeds 33-128 feed the gap pin and the on-camp test | same |
| `mapgen_land_fire.rs` (new) `every_seed_of_1_to_128_keeps_its_map` | (`land` panicked on a lost seed) | a lost seed is an assertion | "no seed loses its map" |
| `mapgen_land_fire.rs` `the_fire_guarantees_are_registered_by_name` | `["crater-per-camp", "crater-clear-of-lava"]` | `["crater-clear-of-lava", "crater-on-camp"]` | the decision |
| `mapgen_land_fire.rs` `crater_per_camp_fails_with_a_crater_removed` | per-camp refuses a removed crater | replaced by `a_camp_without_its_crater_is_refused_by_no_fire_rule` (removed crater: every Fire rule passes) and `crater_on_camp_fails_with_a_crater_moved_off_its_camp` (anchor moved one tile off any camp, disc still dry: on-camp refuses, clear passes) | the decision |
| `mapgen_land_fire.rs` the two `crater_clear_of_lava_fails_*` | companion rule `crater-per-camp` still holds | companion rule `crater-on-camp` still holds | the decision |
| `mapgen_land_fire.rs` `the_fire_guarantees_hold_on_every_kept_map` | seeds 1-32 | seeds 1-128 | the maps with a bare camp are kept maps too |
| `mapgen_land_fire.rs` `the_attempt_bounds_hold_and_the_fire_guarantees_refuse_little` | Fire refusals <= 8 (PROVISIONAL) | <= 2 (measured 0) | no attempt is refused for a missing crater; the net should not catch |
| `mapgen_land_fire.rs` `wide_craters_keep_clear_of_the_streams_and_pools` | seeds 33-96 | seeds 33-128 | the decision's measured ranges |

## 9. Validation commands

```
cd <worktree> && export PATH="$HOME/.cargo/bin:$PATH"
# optimised test builds run the map tests about eight times faster (debug assertions stay on):
export CARGO_PROFILE_DEV_DEBUG=0 CARGO_PROFILE_TEST_DEBUG=0 CARGO_PROFILE_DEV_OPT_LEVEL=2 CARGO_PROFILE_TEST_OPT_LEVEL=2
cargo test -p rh-sim --features trusted-status-probes --test mapgen_land_fire -- --nocapture
cargo test -p rh-sim --features trusted-status-probes --test mapgen_guarantees --test mapgen_signature_fire_bands \
  --test mapgen_signature_fire --test mapgen_signature_volcano --test mapgen_land_plague --test mapgen_land_sentenced
cargo test -p rh-sim --all-features --lib style_scorch
scripts/gate.sh --quick
scripts/gate.sh --oracle packet/P-LAND-FIRE-1
```

## 10. Numbers for the owner

Owner-accepted (2026-11-14): the gap, about 1 Fire camp in 20 without a crater (ceiling 40 and worst map 3 are the
oracle author's bounds round the measured 32 and 3). PROVISIONAL figures named in the oracle: `at_band_option_min` =
0.9, `fire_refusals_allowed` = 2; the stack's `worst_attempt_allowed` = 12 and `attempts_allowed` = 137. Owner figures
still to confirm: the clearance 2 (today's apron), the smallest crater `radius_min4` = 20 (5 tiles), the share window
[0.8, 1.2].

**Close to a bound:** the worst single map on seeds 65-128 measured exactly 3 (seed 114) against `worst_map_allowed` =
3: a build that leaves one more camp bare on that map fails it. The bound was not raised to make room; if it fails, the
measurement goes to the owner.

The implementer reports, seeds 1-64 (and 1-32 where the oracle measures): attempts per seed, the worst, the sum;
refusals by label; camps without a crater by band and cause; craters below their band option; the least crater tile to
lava; crater share on/off median; time per map (option on) before and after.

## 11. The picture

The lane's session renders eight seeds (1-8) of the Fire zone after the build: tiles, stream and pool lava, each
crater's ring, the camp tiles (the bare camps marked); the owner sees the sheet before a merge is asked for.

## 12. Known traps

- **Commit order changes later craters.** A crater kept that was dropped before claims ground and a route stretch
  (`stretch` is carried from crater to crater); later camps' fits change. Measure over the whole sequence, not one camp.
- **Trying more options must not draw.** The crater raster uses hash noise (`kit::noise_q`), not the RNG streams;
  keep it so (draw order is behaviour). `crater()` takes `&mut ShapeContext`; nothing may be written before `commit`.
- **Two notions of "clear".** The owned-tile clearance is enforced today by the apron cut and by `intact`; the disc rule
  is new. A crater whose disc holds no lava may still lose rim tiles to the apron: that is allowed, `rim_min` still
  applies.
- **`tests/content.rs`** reads every upper-case `const`/`static` (local ones too) as a tunable: no `const` in
  `guarantees.rs`; a new `CraterRules` field changes the content hash (`visit_content`) and every content pin.
- **The frame's lava:** read it from `BaseContext::landmark` / the guarantee's `LandmarkFrame` (cells LOW with
  `DEFAULT_DEFINITION.lava`, outside `anchor.footprint`); do not rebuild the streams from map features.
- **Option off:** every new branch sits behind `anchor.is_some()`; the legacy path's freer-share selection is untouched.
- **The gap pin is not a target to tune.** If it fails above the ceiling, the build got worse than today's game: fix
  the build. If it fails at zero, the owner's direction is delivered: report it, do not edit the pin.
