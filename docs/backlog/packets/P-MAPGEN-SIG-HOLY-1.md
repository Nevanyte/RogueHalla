# P-MAPGEN-SIG-HOLY-1: the Holy hall is one fixed drawing, placed in the Landmarks phase as the fifth signature landmark

- **Serves:** owner direction 2026-11-12 (core rules G1 to G3: one landmark per zone placed before any obstacle; the
  landmark is the zone's anchor and steps 3 and 4 read it); owner decision 4 (2026-11-10: "Holy keeps its temple");
  owner decisions 2026-11-13 (way A, one blueprint, 37 x 37, two camps inside, a 41 site, legacy's elite rule; ADR
  0028 G9, requirements L1 and L6 of `docs/coordination/LAND_GENERATION_REQUIREMENTS.md`) and 2026-11-14 (spawn points;
  the shrine walls). The last landmark of ADR 0028 G9.
- **Implementer tier:** implementer-opus (rh-sim mapgen: the Prefix's sites and camp placement, the Landmarks phase,
  the spawn mask, the certifier's Hall rule, the Holy styler in signature mode; every option-on map moves)
- **Reviewer:** reviewer-opus
- **Integration base:** `origin/feature/unity-rework` @ `2f33045d6`. Oracle check: `scripts/gate.sh --oracle-check
  --base origin/feature/unity-rework`.
- **D-rules touched:** D1: option off byte-identical (G8; `pins::pinned_off` stays green); no legacy counterpart for
  the option-on hall (DEV-020) · D2 n/a · no sim tick, wire, protocol or Unity change (the Hall anchor's entrance grows
  from 0 to 7 tiles, under `MAX_ANCHOR_ENTRANCE` 16; the Unity art, P-UNITY-LANDMARK-5, already reads it).
- **Status:** brief, stub and oracle written 2026-11-14 (oracle-author, Opus) on `packet/P-MAPGEN-SIG-HOLY-1`. This
  brief replaces the 2026-11-12/13 brief; the owner's decisions it recorded are kept verbatim below.

## Owner decisions (verbatim, with dates; they govern everything below)

**2026-11-12 evening (chat):** way A is chosen: "please move it to landmark phase and standardize the interior". Way B
(keep the hall in step 3) is not built.

**2026-11-13 (owner decision "Yes, apply them"; requirements L1 and L6; ADR 0028 G9):** the hall is one blueprint: it
"stays an open colonnade, walkable from every side" (piers, not a walled building with doors), one size, one symmetric
interior: a lit central nave, the shrine at the back, the front toward the coliseum. Placed in one of eight
orientations; nothing about it varies between maps but its place within its site rule, its rotation and its mirror.
Guarantees are hard, numbers are targets: a site that cannot hold the hall discards the attempt; a size that breaks the
attempt bounds goes back to the owner with the measurement.

**2026-11-13 (chat, midday):** "The hall is **37 x 37** with **two Holy camps inside on the nave's centre line** (a
41-tile site: the hall plus a 2-tile apron each side). The approved grid is `docs/landmarks/blueprints/hall.txt` [...];
the two camps stand at the blueprint's `E` tiles and never move. Legacy's elite rule is kept for Holy: the zone's
elites are chosen as today among all ten camps (in the probe the hall camps were never the elites), so the certifier's
`elite-selection` needs no Holy case."

**2026-11-14 (afternoon, land-generation session's chat; asked three questions, each worded so that "yes" accepts the
recommendation, the owner answered "Yea to all 3"):**
- (2) **Holy hall, spawn points:** "allow them on the hall's open floor as today, kept off the walls and 3 tiles clear
  of the shrine."
- (3) **Holy hall, shrine walls:** "exempt them from the rule that every hall wall piece is a 3 x 3 pier."
- (Question 1 was the Fire craters, P-LAND-FIRE-1.)

Standing rules (same session, unchanged): guarantees are hard (an attempt that cannot deliver one is refused; nothing is
served downgraded); numbers are targets (a number that cannot be met goes back to the owner with its measurement); a
landmark or zone rule never loses a seed; option off stays byte-identical; the owner sees a picture of real output on
eight seeds before the merge request. "This is just map generation optimization": build, re-pin, gate, review, push,
request the merge; no further approval round.

## 1. Outcome

With `pipeline::Options::signature` on, the Holy hall is the approved drawing `docs/landmarks/blueprints/hall.txt` (37 x
37, front = the bottom edge, mirror symmetric about column 18; `#` 315 wall tiles: 32 piers of 3 x 3 and the shrine's
27; `L` the shrine's 3 x 3 healing light; `D` the shrine's three-tile door; `*` six nave light marks; `E` the two camps
at (18, 15) and (18, 27)), placed by the generic stamp from a fifth table entry in the Landmarks phase, filling the inner
rect (37 x 37) of a Holy site of 41 a side reserved in the Prefix like the other four, its front toward the coliseum.
Two Holy camps stand on the landed `E` tiles; the zone's elites are legacy's choice among all ten Holy camps. The Holy
anchor (kind `Hall`) comes from the Landmarks phase with the other four. The Holy styler places no hall of its own.
Spawn points may stand on the hall's open floor, never on its walls or light and 3 tiles clear of the light. The
certifier's `feature-whole` rule for halls accepts the shrine's one wall group and still requires every other wall
group to be an exact 3 x 3 pier.

## 2. Dependencies
In the base: P-MAPGEN-SIG-BLUEPRINT-1/-2, LANDMARK-FRAME-1, GUARANTEES-1, SCALE-1..4 (the table, the stamp, the frame,
the sites, the fan). **Sibling:** the Frost lake's reserved-room packet also changes `landmarks/signature/mod.rs`
(`fan`, `fan_holds_a_site_camp`) and `attempt/mod.rs`; the lane's session orders the two (whichever lands second
rebases; both touch `fan_holds_a_site_camp`'s camp loop).

## 3. Relevant paths (on `2f33045d6`; line numbers after the stub)

No legacy path (DEV-020). Option off is legacy: the Holy styler ports `packages/server/src/mapgen/zoneStyle/sanctum.ts`.
- `crates/rh-sim/src/mapgen/landmarks/signature/mod.rs`: `Definition` (the stub's `hall_grid`, `hall_site_side` at the
  end), `ReservedSite` (`:202`, the stub's `extra`, `elite`), `reserved_zones` (`:211`, four zones), `relabel_sites`
  (`:229`, Sentenced and Frost; its `debug_assert!` on stray camps), `fan_holds_a_site_camp` (`:324`, reads `r.camp`
  only), `from_definition` (the entrance-glyph `debug_assert!` at `:577`), `reserve` (`:656`: one `site_side`; the
  `camp_zone` rule), `inside_camp` (`:769`), `place` (the inner rect from `bands[0]`), `write` (`:931`: `MapFeature::p`
  empty; the box apron into `near1` and `boxes`), `finish` (`:990`: adds the largest Holy `Hall` as the Holy anchor),
  `intact` (`:1021`), `doors_intact` (`:1104`), `aprons_apart` (`:1147`: picks the hall by the Holy anchor's footprint),
  `stamp` (`:1232`).
- `.../signature/spec.rs:106` `specs`: four entries; `.../signature/blueprint.rs`: `EntranceRule::FrontRun`,
  `Placement::Centre` (37 in 37: no spare tile).
- `crates/rh-sim/src/mapgen/attempt/mod.rs:190-270` `assemble_with`: `ReservedSite` -> `zones::camps::Reserve`, the fan
  check, `place_camps_reserving`.
- `crates/rh-sim/src/mapgen/zones/camps/mod.rs:472-737`: `Reserve` (one `camp`), `place_camps_reserving` (the pinned
  camp is label 1, never moves, and is the first elite: `:725` `let first = if pinned.is_some() { base } else
  { by_far[0] }`; legacy's rule: farthest from (CX, CY), then the farthest of the rest at least `elite_min_sep` 16 away).
- `crates/rh-sim/src/mapgen/pipeline/mod.rs:406` (`relabel_sites` on repair's territory copy), the spawn preparation
  (`spawns::prepare_and_pick`, the pool input masks `near1`/`boxes`); `crates/rh-sim/src/mapgen/spawns/mod.rs:254`
  `pick_spawn_pool` (`pool_min` 24).
- `crates/rh-sim/src/mapgen/certify/features.rs:268-296` (`feature-whole`: every 8-connected wall group a hall owns is
  an exact 3 x 3) and `:334` (Hall params: 5, axis 0/1, pitch 6).
- `crates/rh-sim/src/mapgen/style_sanctum/mod.rs:163-349` `shape` (today's hall: scan, piers, route guard,
  `place_feature` with jobs `[Cover, Rest]`, params `[axis, 6, radius, piers, bays]`); `surface` (`:418`: lights on
  bays and legacy shrines).
- The prototype (read, never merge): `agent/opus/P-MAPGEN-SIG-HOLY-1-measure` @ `471dd92b9` (base `5b49a874b`; `git
  diff 5b49a874b agent/opus/P-MAPGEN-SIG-HOLY-1-measure`; the generator did not change between `5b49a874b` and
  `2f33045d6`). Its env knobs (`proto` module), diagnostics and `HALL_*` variables are measurement only.

## 4. The design (the plan of 2026-11-14, corrected against the code)

1. **A fifth table entry** (`spec::specs`, last, so `reserved_zones()` = Fire, Sentenced, Plague, Frost, Holy):
   `zone Holy, kind Hall, drawing Some(d.hall_grid)`; legend `#` `Write::Wall` owned by `Hall`, `Legality::Wall`; `L`
   `Write::Floor(light)` owned by `Hall`, `Legality::Floor` (light: the catalog's kind keyed "light"; compute it as the
   prototype did, do not add a field to `TerrainCodes`: `mapgen_signature_table.rs` builds that struct literally);
   `.` plain; `*`, `D`, `E` marks on plain floor; `features: [Hall, job Cover, anchor Centre]`; `Placement::Centre`;
   `EntranceRule::FrontRun(b'E')` (the nave, grid (15..=21, 36)); `doorway: false`; `by_tile`, `any_terrain` empty.
   Pinned by `the_holy_entry_is_the_hall_drawing_with_its_legend`.
2. **The site** (`reserve`): side `d.hall_site_side` (41) for Holy, `site_side` (34) for the others; Holy's line is the
   back-edge rule of Fire, Sentenced and Frost (centroid distance + `back_slack`; the prototype's default). Its camps:
   the drawing's `E` tiles landed centred in the inner rect with `front = axis(front(inner centre tile))`, unmirrored
   (both `E` lie on the mirror axis, so the later mirror draw cannot move them), `camp` = the back `E` (row 15),
   `extra` = [the front `E` (row 27)], `elite` false; a square whose `E` tiles are not legal camp tiles is not a
   candidate. Sentenced and Frost: `elite` true, `extra` empty. Pinned by
   `the_holy_site_carries_the_two_hall_camps_and_pins_no_elite` and the amended `mapgen_signature_reserve.rs`.
   **Correction to the plan:** the plan's "Each site says whether its camps are the zone's elites" is the stub's
   `ReservedSite::elite`; the plan's "per-landmark site size" is `Definition::hall_site_side` (the stub's), not a
   `Spec` field (either works; the oracle reads the Definition).
3. **Camps** (`zones::camps`): `Reserve` carries every pinned camp of its site (`camp` then `extra`) and whether the
   first is the elite; every pinned camp is a fixed label (1, 2 for Holy), never moves, counts in spacing and halo
   checks; with `elite` false the zone's elites are legacy's rule among all ten (`by_far[0]`, then the separation rule).
   Pinned by `the_holy_elites_follow_legacys_rule_among_all_ten_camps` and `two_holy_camps_stand_on_the_landed_e_tiles`.
4. **Relabel:** `relabel_sites` also relabels the Holy site: each tile of its rect takes the label of the nearer of its
   two camps (squared distance; `camp` on a tie, as the prototype's `min_by_key`). Pinned by
   `relabel_sites_gives_each_holy_site_tile_its_nearer_hall_camp`. Its stray-camp `debug_assert!` must allow the
   site's own second camp.
5. **The fan:** `fan_holds_a_site_camp` marks the halos of every site camp (`camp` and `extra`). Covered by the
   existing `mapgen_signature_volcano.rs` `the_fire_fan_holds_no_camp_halo_and_no_legacy_building` (every camp).
6. **The anchor and membership:** the anchor comes from `stamp` (centre the inner rect's centre tile = grid (18, 18)
   landed under every orientation; footprint the landing box = the inner rect; entrance the nave sorted row-major);
   `finish` adds no anchor for a styler hall (with the drawn hall there is none) and keeps the frame's. The plan's
   item 6 (do not give Holy `members: [Hall]` because membership is by kind over the whole map) is the implementer's
   call: with the option on the Holy styler places no hall, so the drawn hall is the map's only `Hall`
   (`the_hall_is_the_blueprint_filling_the_holy_sites_inner_rect` asserts exactly one); `members: [Hall]` would bring
   it under `intact`'s owned-tile and apron checks (wanted: G5). Either way `aprons_apart` must still see the hall
   (today by the Holy anchor's footprint) and `intact` must not discard the hall's own tiles. The oracle does not pin
   `members`.
7. **The Hall feature:** jobs `[Cover]` (the entry's one job; today's styler gives `[Cover, Rest]`; the certifier
   allows both), params `[axis, 6, 18, 32, 0]` (`write` fills `p` for a `Hall`: axis 0/1, pitch `R.hall_pitch`, radius
   18, 32 piers, 0 bays; the prototype's), so `certify` `feature-params` holds. Pinned (length 5, axis 0/1, pitch 6,
   piers 32) by `the_art_reads_32_piers_one_shrine_with_its_door_and_its_light`.
8. **`style_sanctum/` in signature mode:** places no hall (`shape` returns no plan when `BaseContext::landmark` is
   the Holy frame) and no light on the hall's tiles; legacy shrines (`Landmark` features) keep their stamps. The hall's
   box holds exactly the shrine's nine light tiles (pinned).
9. **Spawn points (owner decision 2):** the hall's written tiles (walls and light) and every tile within Chebyshev 3 of
   a light tile are kept from the spawn pool and its halo (`near1`); the rest of the box is open to it; the box stays in
   the keep mask (`boxes`). The prototype also kept the 2-tile ring round the box (`near1`) and measured with it; keep
   it (it is not pinned: the owner's words name only the walls and the shrine). Pinned by
   `spawn_points_keep_off_the_hall_walls_and_three_tiles_clear_of_the_light` (and its wide twin) and
   `spawn_points_stand_on_the_halls_open_floor`.
10. **The certifier (owner decision 3):** in `certify/features.rs`, a `Hall` feature that is the drawn hall (the Holy
    anchor's feature: zone Holy, kind Hall, box = the Holy anchor's footprint) may have exactly one wall group that is
    not an exact 3 x 3 (the shrine); every other group must be one. **Correction to the prototype:** its relaxation
    (`!sizes.is_empty()` for the signature hall) accepts any broken pier; that does not ship. Pinned by
    `a_hall_pier_that_is_not_an_exact_three_by_three_is_refused` (a pier missing its middle, a pier with a wall beside
    it). No new feature kind, no wire change.
11. **A real check, not a `debug_assert!`,** in `from_definition` that an entry's entrance glyphs all write one thing
    (`:577`): in a release build a table that breaks it must refuse (e.g. that entry gets no drawing). No test (the
    table is not injectable); the reviewer reads it.
12. Draw order: the Holy stream's one reservation draw in the Prefix, then the stamp's one mirror draw. No other draw.
    Emptied source files stay as one-line placeholders with their `mod` line; no file is deleted.

## 5. Exclusive write scope
- `crates/rh-sim/src/mapgen/landmarks/signature/**` (table entry, `reserve`, `relabel_sites`, `fan_holds_a_site_camp`,
  `write`'s Hall params and spawn mask, `finish`, `intact`/`aprons_apart` as needed, `from_definition`'s check).
  `signature/hall.txt` stays byte-equal to the approved file (not edited).
- `crates/rh-sim/src/mapgen/attempt/mod.rs` (`assemble_with`: the `Reserve` list), `crates/rh-sim/src/mapgen/zones/
  camps/mod.rs` (`Reserve`, `place_camps_reserving`: several pinned camps, the elite flag).
- `crates/rh-sim/src/mapgen/spawns/mod.rs` and `crates/rh-sim/src/mapgen/pipeline/mod.rs`: the spawn mask only.
- `crates/rh-sim/src/mapgen/certify/features.rs`: the Hall `feature-whole` rule only.
- `crates/rh-sim/src/mapgen/style_sanctum/**`: signature mode only.
- `docs/adr/0028-signature-landmarks.md`: the hall decision (G9 complete), the spawn and certifier exemptions.
- Unit tests inside these source files may be adjusted where they construct the changed structs (`Reserve`).
- Not: the wire, Unity, `style_rime/`, `style_scorch/`, `Cargo.lock`, `docs/landmarks/blueprints/**`, any oracle file
  below, the digest pins (section 10).

## 6. Contracts and references
ADR 0028 G1-G10; C-PARITY (option off byte-identical); rh-sim determinism (ordered iteration, the signature stream
only, draw order is behaviour); `tests/content.rs` (no new `const`: the new numbers are `Definition` fields;
`DEFAULT_DEFINITION` is a NON_GAMEPLAY row). The volcano brief (`P-MAPGEN-SIG-SCALE-4.md`) for the stamp, the frame and
the mirror tie.

## 7. Behaviour to preserve
- Option off byte-identical (`pins::pinned_off`, every `*_option_off_*` guard).
- The other four landmarks land as today where the Prefix leaves them (their oracles unchanged); every map certifies
  clean (strict, every zone); every seed keeps a map; aprons apart; each landmark placed alone equals it placed with
  the others (`mapgen_signature_table.rs`).
- The other zones' owner-set floors (`mapgen_land_sentenced.rs`, `mapgen_land_plague.rs`) and every signature oracle's
  attempt bounds (12 / 137 over seeds 1-32): a failure is a number for the coordinator, not an edit.

## 8. Baseline and measurements (seeds 1-64, option on, 16 attempts; prototype raw output in the land-generation
session's scratchpad folder `hall/`)

| run | maps | attempts 1-32 / 1-64 | worst index | refusals (1-64) | Holy spawns in the hall box per map |
|---|---|---|---|---|---|
| today (`base2.txt`: `HALL_OFF=1`) | 64 / 64 | 89 / 192 | 10 | Signature 38, Prefix 24, SpawnRatio 26, lake-at-mouth 16, SpawnPool 9, RadiusPost 8, SignatureDiscarded 5, FloorShare 2 | 2.44 (styler hall) |
| hall, spawns kept off box and apron | 38 / 64 | | | all 26 lost at `SpawnPool` (Holy pool 19-23 < 24) | 0 |
| **chosen** (`inside2.txt`: spawns on the open floor) | 64 / 64 | 93 / 189 | 11 | Signature 45, Prefix 28, RadiusPost 20, lake-at-mouth 17, SpawnRatio 11, SignatureDiscarded 3, SpawnPool 1 | 4.34 |
| chosen, seeds 1-128 (`inside128.txt`) | 128 / 128 | 93 / 360 (1-128) | 11 | RadiusPost 48, Prefix 50 (13.9%), no Holy site in 66 Prefix rounds | 4.37 |

Other chosen-run figures: hall feature 324 tiles on every map; 2 hall camps per map, elites among them 0 of 128 (1 of
256 on 1-128); Holy camp nearest-neighbour mean 18.1 (today 20.2), spread 31.4 (min 28.2); zone floor ratio max 1.147
(rule 1.15); Holy floor / others' mean 1.035; Prefix rounds per accepted attempt 1.89.

## 9. Numbers in the oracle (targets: measured value, headroom)
- Drawing, site: 37 x 37; 41 site; `#` 315, `L` 9, `D` 3, `E` 2, `*` 6, `.` 1034: the owner's (no headroom).
- Worst attempt index over seeds 1-32 <= **12** (prototype 11; the stack's, equal to every other signature oracle's
  bound on this base, so 1 of headroom); attempts over seeds 1-32 <= **121** (prototype 93, +30%). Not the owner's.
- Spawn-pool points on the hall's open floor over seeds 1-32 >= **32**, on >= **8** maps (prototype 4.34 per map,
  about 139 over 32 maps): a floor saying the permission is used, not a target.
- Spawn rule: 0 points on a hall wall or light, or within Chebyshev 3 of the light (owner's, hard).
- Seeds with a map: 32 of 32 and 96 of 96 (33-128) (owner's: no seed lost).

## 10. Acceptance checks and oracle files

Hard (all in `crates/rh-sim/tests/mapgen_signature_hall.rs`; seeds 1-32 unless named; the wide tests seeds 33-128):

| test | asserts | on the stub |
|---|---|---|
| `the_holy_entry_is_the_hall_drawing_with_its_legend` | design 1 | red: no Holy entry |
| `guard_the_drawing_and_the_site_are_the_owners` | the drawing, counts, mirror symmetry, `E`, `L`, nave; `hall_site_side` 41 = 37 + 2 x `bands[0]` | green (guard) |
| `the_holy_site_carries_the_two_hall_camps_and_pins_no_elite` | design 2 on the accepted prefix | red: no Holy site |
| `relabel_sites_gives_each_holy_site_tile_its_nearer_hall_camp` | design 4 | red: Holy not relabelled |
| `the_hall_is_the_blueprint_filling_the_holy_sites_inner_rect` | the one `Hall` feature equals the drawing in the inner rect under an orientation (mirror tie: every matching orientation lands the same tiles; the Holy frame's landing must be one); every front direction the sites produce occurs | red: no Holy site |
| `two_holy_camps_stand_on_the_landed_e_tiles` | ten Holy camps; exactly two camps in the box, Holy, on the landed `E` | red |
| `the_holy_anchor_is_the_box_the_centre_tile_and_the_nave` | anchor kind, footprint, centre (18, 18), feature anchor point and box, front, entrance (7 nave tiles, row-major); `Placed::anchors()` holds the five zones, Holy's = the map's; the Holy frame's landing, features `[(Hall, box)]`, marks `D`, `E`, `*` | red |
| `the_art_reads_32_piers_one_shrine_with_its_door_and_its_light` | 32 exact 3 x 3 wall groups + one shrine with a 3-tile straight door on its front side; the box's light tiles = the 3 x 3 block in the shrine; jobs `[Cover]`, params | red |
| `spawn_points_keep_off_the_hall_walls_and_three_tiles_clear_of_the_light` | owner decision 2 (pool and spawns) | red |
| `spawn_points_stand_on_the_halls_open_floor` | the permission is used (section 9) | red |
| `guard_every_map_keeps_its_landmarks_and_certifies_clean` | four landmarks, a Holy anchor, zero certifier failures | green |
| `a_hall_pier_that_is_not_an_exact_three_by_three_is_refused` | seeds 1-8, two doctored piers -> `feature-whole` | red |
| `the_holy_elites_follow_legacys_rule_among_all_ten_camps` | legacy's elite rule | green (guard; red if the hall camps are pinned elites) |
| `wide_every_seed_33_to_128_keeps_a_map_that_certifies_clean` | no seed lost, certified clean | green |
| `wide_the_hall_its_camps_the_art_and_the_spawn_rule_hold` | the hard rules on 33-128 | red |
| `guard_the_worst_attempt_stays_within_the_bound`, `guard_the_attempts_used_stay_within_the_bound` | section 9 | green (stub: 89 attempts, worst 10 on 1-32 by the prototype's baseline) |
| `guard_option_off_is_todays_generator` | `pins::pinned_off` | green |
| `report_seeds_1_to_64` (ignored) | the report of section 12 | - |

Most red tests fail at the shared helper's first assertion (`holy_site`: "no Holy site in the prefix"), the hall's
precondition, as the volcano oracle's `orientation` did; each then checks its own rule.

### Existing tests amended (`oracle-amend:` commits; old -> new, the reason: the hall becomes the fifth reserved, drawn landmark)
1. `crates/rh-sim/tests/mapgen_signature_reserve.rs`: `styled()` Fire, Sentenced, Plague, Frost -> + Holy (5 sites, Holy
   last); `every_styled_zone_reserves_a_site_on_its_rule`: a site camp for Sentenced | Frost -> Sentenced | Frost |
   Holy (the Holy site also on the back-edge rule, side >= 34); `no_camp_halo_touches_a_site_but_its_own_camp`: a
   camp's own site is the one whose `camp` or `extra` it is (was `camp` only); `the_church_and_cave_camp_is_an_elite_in
   _the_far_half_of_its_site`: the sites with a camp -> the Sentenced and Frost sites with a camp (the hall's camps are
   not elites). Red on the stub (no Holy site). Nothing loosened: the Holy site gets every check the others get.
2. `crates/rh-sim/tests/mapgen_signature_blueprint.rs` `placed_landed_rederives_every_written_cell`: the landed zones
   Fire, Sentenced, Plague, Frost -> + Holy, so the generic re-derivation covers the hall. Red on the stub.
3. `crates/rh-sim/tests/mapgen_landmark_frame.rs` `member_kinds(Holy)`: `[]` -> `[Hall]` (the frame tests then require
   the Holy frame to write the hall). Green on the stub (no Holy frame).
4. `crates/rh-sim/tests/mapgen_signature_table.rs` `guard_every_shipped_drawing_is_the_approved_file`: Holy's approved
   file added (else the Holy entry panics "no approved file named here"); the `membership()` doc: the hall's members are
   the implementer's. Green on the stub.
5. (stub commit, compile only) `crates/rh-sim/tests/mapgen_signature_church.rs`: its `ReservedSite` literal gains
   `extra: Vec::new(), elite: camp.is_some()`.

Left unchanged on purpose: `mapgen_signature_core.rs` `g3_the_finished_map_carries_the_zone_anchors` (the hall is the
largest Holy `Hall`, its box the footprint, its anchor point the centre: holds), `mapgen_signature_aprons.rs`,
`mapgen_signature_fire.rs`, `_frost.rs`, `mapgen_guarantees.rs` (Holy lists no guarantee), every attempt bound.

### Digest pins that go red on the build (NOT this packet's; the lane's session re-takes them after the build)
Every option-on map moves. Expected red: `mapgen_signature_blueprint.rs` `option_on_maps_are_unchanged` and
`the_landmarks_phase_is_unchanged` (`tests/support/signature_pins.rs` `pinned_on_maps`, `pinned_landmarks`; print the
new values with `cargo test -p rh-sim --features trusted-status-probes --test mapgen_signature_blueprint
print_signature_pins -- --ignored --nocapture`), `mapgen_guarantees.rs` `pinned_labels` (the refusal labels over
seeds 1-32; the failing assertion prints the actual list), and `rh-mapdress` `anchor.rs` `NO_ANCHOR_GOLDEN`,
`marsh.rs` `SIG_GOLDEN`, `sentenced_rings.rs` `NO_ORIGIN_GOLDEN` (the failing assertions of `cargo test -p
rh-mapdress` print the actual values). Every `OFF_GOLDEN` and `pinned_off` stays green. The implementer lists which went
red and does not edit them.

## Oracle files (read-only for implementers)
- `crates/rh-sim/tests/mapgen_signature_hall.rs` (new)
- `crates/rh-sim/tests/mapgen_signature_reserve.rs`, `crates/rh-sim/tests/mapgen_signature_blueprint.rs`,
  `crates/rh-sim/tests/mapgen_landmark_frame.rs`, `crates/rh-sim/tests/mapgen_signature_table.rs` (amended)
- `docs/backlog/packets/P-MAPGEN-SIG-HOLY-1.md` (this brief)
- Stub files (editable, behaviour-free): `crates/rh-sim/src/mapgen/landmarks/signature/mod.rs` (`Definition::hall_grid`,
  `hall_site_side`; `ReservedSite::extra`, `elite`), `crates/rh-sim/src/mapgen/landmarks/signature/hall.txt` (do not
  edit: pinned byte-equal), `crates/rh-sim/tests/mapgen_signature_church.rs` (the literal only).

## 11. Validation commands
```
cd <worktree> && export PATH="$HOME/.cargo/bin:$PATH"
export CARGO_PROFILE_DEV_DEBUG=0 CARGO_PROFILE_TEST_DEBUG=0 CARGO_PROFILE_DEV_OPT_LEVEL=2 CARGO_PROFILE_TEST_OPT_LEVEL=2
cargo test -p rh-sim --features trusted-status-probes --test mapgen_signature_hall
cargo test -p rh-sim --features trusted-status-probes --test mapgen_signature_hall report_seeds_1_to_64 -- --ignored --nocapture
cargo test -p rh-sim --features trusted-status-probes --test mapgen_signature_reserve --test mapgen_signature_blueprint \
  --test mapgen_landmark_frame --test mapgen_signature_table --test mapgen_signature_church --test mapgen_signature_volcano \
  --test mapgen_signature_core --test mapgen_signature_aprons --test mapgen_land_sentenced --test mapgen_land_plague
cargo test -p rh-sim --features trusted-status-probes --no-fail-fast
cargo test -p rh-mapdress && cargo test -p rh-server --test map_anchor_seeds --test map_anchors
cargo fmt --all -- --check && cargo clippy -p rh-sim --all-targets --features trusted-status-probes -- -D warnings
scripts/gate.sh --quick --oracle packet/P-MAPGEN-SIG-HOLY-1
```

## 12. What to measure and report (the implementer, in the hand-off; "before" = the stub's report, section 13)
1. Attempts per seed 1-64 (and 65-128): totals, worst index, seeds lost; against 12 / 121 here and 12 / 137 elsewhere.
2. Refusals by reason; `Repair(RadiusPost)` (prototype 20 on 1-64, today 8) and Prefix rounds with no Holy site
   (prototype 35 on 1-64) with a throwaway counter, not committed.
3. Spawn-pool size and Holy's share per map; points on the hall's open floor (prototype 4.34 per map); `SpawnPool` and
   `SpawnRatio` refusals.
4. **Holy's own land, for the owner** (no rule beyond "every zone: about today's share, within about a fifth"): the
   report's Holy share (the Holy styler's tiles over the zone's free ground) option on and off, whole zone and with the
   site (on) or the styler hall's box and apron (off) taken out, before and after. Expect the styler's share outside
   the site to fall to the lights and props: the styler's hall was its main fill and now is the landmark. The numbers go
   to the owner; nothing in this packet decides them.
5. The other zones' owner floors (`mapgen_land_sentenced.rs`, `mapgen_land_plague.rs` reports) before and after.
6. Time per map, before and after. 7. Eight seeds (1-8, option on) listed with their attempts for the owner's picture.

## 13. Before (the stub, measured by the oracle author with `report_seeds_1_to_64`)
Run on `packet/P-MAPGEN-SIG-HOLY-1` at the stub (optimised test build): identical to the prototype's baseline
(`base2.txt`): all 64 seeds; attempts 89 / 192 (seeds 1-32 / 1-64), worst index 9 (seed 22) / 10 (seed 64); refusals
Signature 38, SpawnRatio 26, Prefix 24, lake-at-mouth 16, SpawnPool 9, RadiusPost 8, SignatureDiscarded 5,
FloorShare 2. **Holy's land (medians over seeds 1-64):** the Holy styler fills 0.212 of Holy's free ground with the
option on and 0.212 with it off (seed 1: 1044 / 4882 on, 1016 / 4882 off); with the styler hall's box and its 2-tile
apron taken out (off) 0.004. Today's Holy share is the styler's hall: once the hall is the landmark (its site taken
out), the styler's share of the rest of the zone is expected near 0.004, far under "about today's share, within about
a fifth". This goes to the owner with the build's numbers; the packet does not change it.

## 14. Known traps
- `Reserve` and `ReservedSite` are built in struct literals: `attempt/mod.rs` (two), `zones/camps` unit tests, the
  church test (done in the stub). A new field elsewhere breaks an oracle file: do not add one to `ReservedSite` or
  `TerrainCodes`.
- The mirror draw still happens (one draw), though the symmetric drawing lands the same tiles both ways.
- `place_camps_reserving` checks each pinned camp's halo legality and spacing; with two pinned camps 12 apart (rows 15
  and 27) the camp gap (9) holds; the halo must not meet another site (the Holy halo stays inside the Holy site).
- `relabel_sites`' stray-camp `debug_assert!` fires on the Holy site's second camp unless taught both.
- The pinned camps' territories: the prototype saw `Repair(RadiusPost)` rise 8 -> 20 (1-64); the relabel is what keeps
  it there.
- `intact`'s `doors_intact`: the hall's entrance writes nothing and no hall feature owns it; it must hold floor.
- `aprons_apart` finds the hall by the Holy anchor's footprint and the `Hall` box: they are equal (the corner piers
  reach all four edges).
- Spawn mask: write the hall's tiles and the light's Chebyshev-3 square into `near1` before the spawn pool is drawn;
  the box in `boxes`. Keeping the whole box in `near1` (as for the other landmarks) loses 26 of 64 seeds.
- The certifier relaxation must name exactly one non-pier group; a "Hall whose box is the anchor's footprint" test
  alone (the prototype's) accepts broken piers.
- `finish`: never add the styler-hall anchor beside the frame's Holy anchor (two Holy anchors).
- Every option-on map moves: the digest pins go red (section 10); not yours.

## Notes from previous attempts
- Prototype `agent/opus/P-MAPGEN-SIG-HOLY-1-measure` @ `471dd92b9` (never merged): the measurements of section 8.
- The 2026-11-12/13 brief's probe (`probe/holy-site`, discarded) measured the options that led to the 37/41 decision.
