# P-LAND-FIRE-1: every Fire camp keeps its crater, and craters keep clear of the volcano's three streams

- **Serves:** owner direction for the land-generation lane (`docs/coordination/LAND_GENERATION_REQUIREMENTS.md`
  section 3, Fire item and "Every zone" item; it answers the landmark lane's open question, section 6 fourth item).
  ORDER_OF_OPERATIONS: the signature-map work (ADR 0028) under the owner's map-generation direction; not a sim tick,
  wire or Unity change.
- **Implementer tier:** implementer-opus (rh-sim mapgen: the Fire styler in signature mode, an end-of-attempt guarantee
  that changes which attempts are kept; every option-on map may move)
- **Reviewer:** reviewer-opus
- **Integration base:** stacked on `agent/opus/P-MAPGEN-SIG-SCALE-4` @ `5b49a874b` (the 22 x 22 volcano with three
  streams; reviewed, on the coordinator's staging, not on origin). Gate with `scripts/gate.sh --oracle packet/P-LAND-FIRE-1
  --base agent/opus/P-MAPGEN-SIG-SCALE-4` (and `--oracle-check --base agent/opus/P-MAPGEN-SIG-SCALE-4` on this branch).
- **D-rules touched:** D1: option off byte-identical (G8; the new code runs only with a Fire frame, the guarantees only
  with the option on); no legacy counterpart (DEV-020: legacy has no volcano) · D2 n/a · no sim tick, wire, protocol or
  Unity change; nothing new is serialized or hashed (guarantees are generator rules, not content: `style_zones::guarantee`).
- **Status:** oracle written 2026-11-14 (oracle-author, Opus). Numbers marked PROVISIONAL are not the owner's: the
  implementer measures them and the coordinator takes them to the owner.
- **Sibling packets prepared at the same time (keep out of their files):** P-LAND-FROST-1 (`style_rime/`),
  P-MAPGEN-SIG-HOLY-1 (`landmarks/signature/`, the Prefix).

## The owner's rule (verbatim, `LAND_GENERATION_REQUIREMENTS.md` section 3)

> - **Fire** (`style_scorch/`). Craters keep clear of all three streams. **Every Fire camp keeps its crater**; size falls
>   with distance and none is dropped. This answers your open question (section 6, fourth item).
> - **Every zone.** About today's share of the zone's free ground as fence, bog and ice, within about a fifth, measured
>   after the site is taken out. Barrows, guard houses, spires, headlands and craters keep today's rules and move only
>   where they would collide with the new arrangement.

Standing rules: **guarantees are hard** (an attempt that cannot deliver one is refused and the next is tried; nothing is
served downgraded); **numbers are targets** (a number that cannot be met goes to the owner with the measurement; it is
not changed quietly); option off stays byte-identical; the owner is shown a picture of real output on eight seeds.

## 1. Today's code (base `5b49a874b`)

- `crates/rh-sim/src/mapgen/style_scorch/craters.rs:102-205` `place_craters`: one crater (a `Crater` pen) centred on
  each Fire camp. Option off (no anchor): legacy's freer-share choice among the camp's radius options, as
  `packages/shared/src/zoneStyle/scorch.ts:46-60` (`R_MIN4 = 20`, `R_MAX4 = 26`, `R_MAX4_ELITE = 30`, `RIM_MIN = 8`;
  `:506-541` legacy also leaves a crater out when its rim is short or it stretches a route: option off has 8-10
  craters for 10 camps). Signature mode (`:135-176`): the camp's band (`camp_band`, `:85-94`) picks **one** option
  (near the largest, mid the second, far the smallest; core any that keeps out of the core); if that one option fails
  the rim, route-stretch or core checks the camp **has no crater** (`continue` at `:176`). This is where camps lose
  their crater.
- `craters.rs:208-236` `lava_apron`: every tile within `signature::DEFAULT_DEFINITION.bands[0]` = 2 (Chebyshev) of a
  lava or ember tile owned by a `LavaStream` or `LavaPool` feature; the crater's bowl floor is cut out of it (`:160-162`)
  and its rim kept out by `no_block`. `SignatureContext::intact` (`landmarks/signature/mod.rs:1028-1060`) discards a map
  with any styler tile in that apron. So today's clearance is 2 tiles, already kept on every kept map; but the crater
  is centred on the camp regardless of the stream, so a stream can run through a crater's ring with the ring merely cut
  where it passes.
- `style_zones/guarantee.rs` `guarantees`: Fire returned `&[]` (the stub points it at `style_scorch::guarantees`, still
  empty).
- The radius options: `search_family::scorch::radius4` R = clamp(floor(2 * nearest other Fire camp - 3.2), 20, 26 or 30
  elite); options R, R - 4, R - 8 (`CraterRules::radius_shrink4`), those >= `radius_min4` = 20.

## 2. Baseline measurements (oracle author, base `5b49a874b`, option on and off, no decor, `certified_styled`)

Temporary ignored test (not committed). "Its crater" = the `Crater` feature of zone Fire anchored on the camp's tile.
Lava = LOW lava tiles owned by `LavaStream`/`LavaPool` (equal to the frame's lava outside the cone's box: pinned by
`the_frames_lava_outside_the_cone_is_the_streams_and_pools`).

| Measure | Seeds 1-32 | Seeds 1-64 |
|---|---|---|
| Fire camps per map | 10 | 10 |
| craters per map, option on | 8-10 | 7-10, mean 9.44 |
| craters per map, option off | 9-10 | 8-10, mean 9.88 |
| camps without a crater, option on | 18 camps on 15 maps | 36 camps on 29 maps |
| craters with stream or pool lava inside their disc (radius p[1]/4) | 8 craters on 8 maps (seeds 8, 15, 19, 24, 25, 27, 29, 32) | 15 craters on 13 maps |
| least Chebyshev distance crater tile to stream/pool lava | 3 | 3 (8 craters at 3, 134 at 4; none within 2) |
| craters touching lava (<= 1) / within 2 | 0 / 0 | 0 / 0 |
| least distance camp tile to stream/pool lava (Euclidean) | | 4.0 tiles (7 camps of 640 under 4.5, 15 under 5.5) |
| crater share of free ground on / off (median of per-seed ratios) | 1.001 | 1.002 (per seed 0.73-1.40) |
| attempts (sum) / worst attempt index | 89 / 9 (seed 22) | |

Crater size by band ("size falls with distance" today), seeds 1-64, option on; the radius is exactly the band's option
for every crater:

| Band (camp) | camps | dropped | radii (quarter tiles) | crater tiles mean (min-max) |
|---|---|---|---|---|
| core | 21 | 0 | 26-30 | 168.9 (124-190) |
| near | 224 | 12 | 20-30 | 115.3 (60-191) |
| mid | 68 | 7 | 20-26 | 92.6 (72-143) |
| far | 327 | 17 | 20-22 | 88.1 (55-113) |

Dropped camps lie both beside streams (camp 5-7 tiles from lava: the apron cuts the rim below `rim_min`) and far from
the volcano (map corners and edges, e.g. seed 20 (6, 153), seed 22 (153, 6): the one far-band option fails the rim or
route checks; option off keeps those craters with a smaller or larger option).

## 3. The rule, as the oracle states it (header of `tests/mapgen_land_fire.rs`)

1. **None dropped:** every Fire camp has its crater; every Fire crater stands on a Fire camp.
2. **Clear of the streams:** no stream or pool lava (the Fire frame's LOW lava outside the cone's box) lies in a
   crater's disc (16 * d^2 < p[1]^2 round the camp tile), and no tile a crater owns lies within
   `style_scorch::lava_clearance()` = **2** tiles (Chebyshev) of that lava: today's apron, pinned, not a new number.
3. **Size falls with distance:** a crater's radius is one of its camp's options, at least the smallest crater
   (**`radius_min4` = 20 quarter tiles, a ring 5 tiles out**: today's floor; owner figure to confirm), and at most its
   band's option (core: any option). It may shrink below its band's option (to be kept, or to keep clear) but never
   grows. At least 90% (PROVISIONAL) take exactly their band's option; near-band craters average at least 1.15 times
   the far band's tiles (today 1.31).
4. **Guarantees** registered for Fire, in order: `"crater-per-camp"` (rule 1), `"crater-clear-of-lava"` (rule 2), each
   a pure read of the finished map and the Fire frame (`fn(&GameMapData, &LandmarkFrame) -> bool`).
5. **Amounts:** the median over seeds 1-32 of (crater share of free ground, option on, the site taken out: the anchor's
   core band and the tiles within the clearance of stream/pool lava) / (option off) lies in [0.8, 1.2].
6. **The volcano untouched:** the Fire frame writes the cone then (stream, pool) three times; three Fire `LavaStream`
   and three `LavaPool` features; `mapgen_signature_volcano.rs` stays green unchanged.
7. **Attempts:** worst attempt index <= 12, attempts over seeds 1-32 <= 137 (the stack's figures), and at most **8**
   (PROVISIONAL) attempts refused by a Fire guarantee over seeds 1-32.

## 4. The design (a few lines; the implementer decides the details)

- In signature mode, `place_craters` tries the camp's band option first, then each **smaller** option in order down to
  `radius_min4`, never a larger one; the first that passes today's rim, route-stretch and core checks **and** holds no
  stream or pool lava in its disc is committed. Option off is untouched (the `anchor.is_none()` path).
- The lava to test against comes from the Fire frame (`BaseContext::landmark`: the cells written LOW with the lava
  terrain outside `anchor.footprint`), the same definition the guarantees use; keep one helper for both.
- A camp that no option fits gets no crater from the styler, and `crater-per-camp` refuses the attempt (never a crater
  outside the rules, never a crater smaller than `radius_min4`).
- `style_scorch/guarantees.rs`: the two checks; `guarantees()` a promoted literal (no `const`/`static`: `tests/content.rs`
  reads every upper-case one as a tunable); `lava_clearance()` stays a function.
- Cheaper fits worth trying before refusing (implementer's choice, keep today's rules otherwise): the extra-breach paths
  that already exist (`crossing_beam`), counting the apron-cut rim against `rim_floor_min` the way today's guard does.
- **Option outside this scope (for the coordinator):** if refusals stay high because camps sit 4-5 tiles from a
  stream (no 5-tile crater can hold no lava then), the honest fix is upstream: keep the streams off crater ground in
  `landmarks/signature/volcano.rs` (a stream refused or shortened where its lava would fall within radius_min4 / 4
  tiles of a Fire camp). Cost: the landmark lane's file (P-MAPGEN-SIG-HOLY-1 is open there), more `Signature` refusals
  (SCALE-4's measurement of bending streams: Fire refusals 38 to 81 over seeds 1-64 and a lost seed), and every
  option-on map moves again. Not in this packet's scope; the alternative owner choice is a smaller smallest crater.

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
- **Re-taken by the lane's session, not the implementer** (they pin today's option-on attempts and will go red when
  craters change which attempts are kept): `tests/support/signature_pins.rs` option-on map and Landmarks-phase pins
  (`pinned_on_maps`, `pinned_landmarks`, read by `mapgen_signature_blueprint.rs`, `mapgen_landmark_frame.rs` and the
  other `mapgen_signature_*` files), `mapgen_guarantees.rs` `pinned_labels` (the refusal-label sequence of seeds 1-32)
  and the lake-refusal count `seen == 6` in `a_lake_failure_is_refused_after_records_as_the_frost_guarantee`. The
  implementer reports which of these are red and with what values; it does not edit them.

## 7. Acceptance checks

All of `crates/rh-sim/tests/mapgen_land_fire.rs` (14 tests), the two amended files below, and the whole rh-sim suite
except the pins listed in section 6 as re-taken. Red on the stub (by assertion):

| Test | Assertion on the stub |
|---|---|
| `every_fire_camp_has_its_crater` | "Fire camps without their crater (seeds 1-32): 18 findings" (seed 4 (34, 41), seed 5 (152, 152), ...) |
| `wide_every_fire_camp_has_its_crater` | seeds 33-96 (18 camps on seeds 33-64 alone: seed 35 (42, 135), ...) |
| `craters_keep_clear_of_the_streams_and_pools` | "craters not clear of the streams (seeds 1-32): 8 findings" (seed 8 crater (104, 74) radius 22 holds 3 lava tiles in its disc, ...) |
| `wide_craters_keep_clear_of_the_streams_and_pools` | seeds 48, 53, 54, 55 (3), 61, 65, 72, 77, 92, 96, ... |
| `the_fire_guarantees_are_registered_by_name` | left `[]`, right `["crater-per-camp", "crater-clear-of-lava"]` |
| `the_fire_guarantees_hold_on_every_kept_map`, `crater_per_camp_fails_with_a_crater_removed`, `crater_clear_of_lava_fails_with_a_crater_tile_beside_a_stream`, `crater_clear_of_lava_fails_with_a_crater_disc_over_a_stream` | "no guarantee \"crater-per-camp\" registered for Fire (registered: [])" |
| `mapgen_guarantees.rs` `the_registry_lists_the_fire_frost_and_plague_rules_and_nothing_else` | Fire `[]` against the two rules |
| `mapgen_guarantees.rs` `every_guarantee_holds_on_every_kept_map_of_seeds_1_to_32` | 96 checks, 160 expected |

Green on the stub, guards that must stay green: `the_frames_lava_outside_the_cone_is_the_streams_and_pools`,
`crater_size_falls_with_distance_and_shrinks_only_below_its_bands_option`,
`crater_share_of_free_ground_stays_within_a_fifth_of_todays`, `the_volcano_keeps_its_three_streams_and_pools`,
`the_attempt_bounds_hold_and_the_fire_guarantees_refuse_little`.

## Oracle files (read-only for implementers)

- `crates/rh-sim/tests/mapgen_land_fire.rs` (new; `oracle:` commit `3b171e76c`)
- `crates/rh-sim/tests/mapgen_guarantees.rs` (amended; `oracle-amend:` commit `3f69ba83c`)
- `crates/rh-sim/tests/mapgen_signature_fire_bands.rs` (amended; `oracle-amend:` commit `ca27b6797`)
- `docs/backlog/packets/P-LAND-FIRE-1.md` (this brief)

Stub (the implementer replaces or edits; `stub:` commit `8cc2ddab7`): `style_scorch/guarantees.rs` (new: empty
`guarantees()`, `lava_clearance()` = `bands[0]`), `style_scorch/mod.rs` (`mod guarantees;`, `pub use`),
`style_zones/guarantee.rs` (Fire arm points at `style_scorch::guarantees`).

## 8. Existing oracles amended (each alone in its `oracle-amend:` commit, for the owner)

| File, test | Old | New | Why |
|---|---|---|---|
| `mapgen_guarantees.rs` `the_registry_lists_the_frost_and_plague_rules_and_nothing_else` | renamed `the_registry_lists_the_fire_frost_and_plague_rules_and_nothing_else`; `(Zone::Fire, vec![])` | `(Zone::Fire, vec!["crater-per-camp", "crater-clear-of-lava"])` | the new Fire guarantees |
| `mapgen_guarantees.rs` `every_guarantee_holds_on_every_kept_map_of_seeds_1_to_32` | `checked == 32 * 3` | `checked == 32 * 5` | two Fire checks per map |
| `mapgen_signature_fire_bands.rs` `craters_take_their_bands_radius_option` | outside the core, radius `== ` the band's option | radius one of the camp's options and `<=` the band's option (header note added) | the owner's "none is dropped" means a crater may shrink to be kept; it never grows. The new oracle bounds how often (at least 90% at the band's option, PROVISIONAL) and keeps "size falls with distance" (near mean >= 1.15 x far). This loosens a G4 test on purpose: the owner's newer rule. |

Not amended, still valid: `mapgen_signature_fire_bands.rs` `near_band_craters_are_larger_than_far_band_ones` (a near
crater shrinks at most to the far option of the same set, never below it) and
`no_crater_in_the_core_and_the_crater_count_holds`, `mapgen_signature_fire.rs`
`craters_are_arranged_by_band_round_the_volcano` (count floor on >= off - 2: implied by "none dropped"). Not amended,
will go red after the implementation and are re-taken by the lane's session: section 6.

## 9. Validation commands

```
cd <worktree> && export PATH="$HOME/.cargo/bin:$PATH"
# optimised test builds run the map tests about eight times faster (debug assertions stay on):
export CARGO_PROFILE_DEV_DEBUG=0 CARGO_PROFILE_TEST_DEBUG=0 CARGO_PROFILE_DEV_OPT_LEVEL=2 CARGO_PROFILE_TEST_OPT_LEVEL=2
cargo test -p rh-sim --features trusted-status-probes --test mapgen_land_fire
cargo test -p rh-sim --features trusted-status-probes --test mapgen_guarantees --test mapgen_signature_fire_bands \
  --test mapgen_signature_fire --test mapgen_signature_volcano --test mapgen_land_plague --test mapgen_land_sentenced
cargo test -p rh-sim --all-features --lib style_scorch
scripts/gate.sh --quick
scripts/gate.sh --oracle packet/P-LAND-FIRE-1 --base agent/opus/P-MAPGEN-SIG-SCALE-4
```

## 10. Numbers for the owner (the implementer measures on the real build and reports in the hand-off)

PROVISIONAL figures named in the oracle: `fire_refusals_allowed` = 8 (seeds 1-32), `at_band_option_min` = 0.9; the
stack's `worst_attempt_allowed` = 12 and `attempts_allowed` = 137. Owner figures to confirm: the clearance 2 (today's
apron), the smallest crater `radius_min4` = 20 (5 tiles), the share window [0.8, 1.2].

Report, seeds 1-64, option on (and 1-32 where the oracle measures):
- attempts per seed, the worst, the sum; refusals by reason (labels), the Fire guarantee refusals by rule;
- craters per map (always 10 for 10 camps), craters per band at and below their band option, crater tiles by band;
- the least distance crater tile to stream/pool lava, the count of craters with lava within 1, 2, 3 tiles of their
  ring; the least camp-to-lava distance among camps on kept maps;
- crater share of free ground on/off per seed and its median;
- time per map (option on) before and after.

## 11. The picture

The lane's session renders eight seeds (1-8) of the Fire zone after the build: tiles, stream and pool lava, each
crater's ring, the camp tiles; the owner approves the sheet before a merge is asked for.

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
- **Refusals vs fitting:** the guarantee is the net, not the method; if refusals exceed 8 or the attempt bounds break,
  stop and report the measurement (section 4's upstream option) rather than loosening a test.
