# P-MAPGEN-SIG-HOLY-1-fix: the landmarks' site, camp, spawn and fan rules are table data

- **Serves:** the review note of P-MAPGEN-SIG-HOLY-1 (reviewer-opus, LAND WITH NOTES, note 1, 2026-11-14;
  `docs/coordination/HANDOFF_P-MAPGEN-SIG-HOLY-1_2026-11-14.md` "Gate, pins and review") and ADR 0028 (the per-landmark
  table `spec::specs` is the landmark contract). Owner direction in force: a new landmark fact is one table line. Lands
  before the Frost lake's reserved-room packet, which adds the next per-site exception in the same functions.
- **Implementer tier:** implementer-sonnet (routing row: a map-generator refactor inside `rh-sim` alone that changes no
  generated map and no draw, proven by a digest guard in its oracle).
- **Reviewer:** reviewer-opus.
- **Integration base:** `agent/opus/P-MAPGEN-SIG-HOLY-1` @ 4abed9ba4 until the hall is merged, then
  `origin/feature/unity-rework` (`docs/agent/INTEGRATION_BRANCH`). The packet branch `packet/P-MAPGEN-SIG-HOLY-1-fix`
  is stacked on 4abed9ba4: gate with `--base agent/opus/P-MAPGEN-SIG-HOLY-1`.
- **D-rules touched:** D1 none (no generated map moves; the digest guards below are the proof) · D2 n/a · D5/D6/D9 none
  (no draw, wire, save, state hash or content hash change; the signature tables stay out of `content_hash`).

## 1. ID and outcome

A refactor of the signature-landmark step (`crates/rh-sim/src/mapgen/landmarks/signature/`). Today four per-landmark
rules live in code as zone or kind branches; after this packet they are fields of each `spec::Spec` entry, and the code
reads them:

1. **Site side** (`Spec::site_side`): `reserve` reads the entry; no `z == Zone::Holy` branch for the side. Holy's is
   `d.hall_site_side` (41), the other four `d.site_side` (34).
2. **Site camps** (`Spec::site_camps: Option<SiteCamps { glyph, elite }>`): which glyph pins the site's camp or camps and
   whether the first is the zone's elite. Sentenced and Frost: `E`, elite (with `Placement::Pin`, the one camp is the
   pinned `inside_camp` tile); Holy: `E`, not elite (with `Placement::Centre`, the camps are the glyph's landed tiles,
   grid order: today two); Fire and Plague: none. `reserve` reads it; no zone branch for the camps or the elite flag
   (`matches!(z, Zone::Sentenced | Zone::Frost)`, `hall_zone`, `hall_marks` go).
3. **Spawn rule** (`Spec::spawn: SpawnRule`): `Box` (the four: the whole box and its apron closed to the spawn pool) or
   `OpenFloor { clear_of, by }` (the hall: closed are the tiles it writes, the ring of `bands[0]` round the box, and every
   tile within `by` tiles, Chebyshev, of a tile of glyph `clear_of`; `clear_of: b'L'`, `by: d.shrine_clear` = 3). `write`
   reads it; no `FeatureKind::Hall` branch; the light is found **by its glyph** (the landed tiles of `clear_of`), not as
   "any written floor with terrain".
4. **Fan** (`Spec::fan: Option<Fan { depth, side }>`): Fire's is `(d.fan_depth, d.fan_side)` = (14, 14), the others
   none. `SignatureContext::fan(r)` reads the entry of `r.zone`; the Prefix (`attempt/mod.rs`) records
   `context.sites.fan(r)` for each site. `SignatureContext::fan_holds_a_site_camp` tests every fan against every site camp
   **except a site's own camps against its own fan** (no site with a camp has a fan today, so nothing moves; the Frost
   room needs it).
5. The reviewer's minor items: the `hall_marks` doc that sits inside `inside_camp`'s doc comment (it goes with
   `hall_marks`, or is moved); `relabelled_zones()`'s doc mentions Holy (relabelled through `relabel_sites`'s zone test;
   the list itself stays `[Sentenced, Frost]`, its length is pinned); `landmark_kind()`'s wording (it omits `Hall`, only
   T5 reads it: say so); `reserve`'s doc says what happens when a site's camp tile is not a legal camp tile
   (`place_camps_reserving` fails the round; it looks unreachable).

Not in this packet: the certifier's hall exemption requiring the one non-pier wall group to enclose the light tiles
(optional in the note; not pinned, `certify/features.rs` is out of scope).

When this lands: every generated map, option on and off, is byte-identical to 4abed9ba4; the default table holds today's
values; a doctored table entry changes exactly the rule it names.

## 2. Dependencies

`agent/opus/P-MAPGEN-SIG-HOLY-1` @ 4abed9ba4 (the hall, in a merge request). No other.

## 3. Relevant legacy paths

None: signature landmarks have no legacy counterpart (DEV-020; reached only through `pipeline::Options::signature`).
The behaviour to keep is the hall packet's own, at 4abed9ba4:

- `crates/rh-sim/src/mapgen/landmarks/signature/mod.rs:330-351` `fan`: Fire only (`r.zone != Zone::Fire` returns
  `None`), from `d.fan_depth`, `d.fan_side`.
- `mod.rs:357-369` `fan_holds_a_site_camp`: every site camp (`camp`, `extra`) against every fan.
- `mod.rs:702-813` `reserve` (line numbers of 4abed9ba4): `hall_zone = z == Zone::Holy` picks `d.hall_site_side` (703-710);
  `camp_zone = matches!(z, Zone::Sentenced | Zone::Frost)` (741) calls `inside_camp`; the Holy square's camps from
  `hall_marks()` (`E` tiles) and `hall_camps` (743-776); `elite` false for the hall's camps, `camp.is_some()` otherwise
  (794-798). One draw per zone per round (`pick(rng, candidates.len())`, 791), only when there are candidates; Plague's
  fallback (`deepest`) draws nothing.
- `mod.rs:826-830` `hall_marks` (its doc is inserted inside `inside_camp`'s, 816-825).
- `mod.rs:995-1082` `write`: `if p.kind == FeatureKind::Hall` (1010) the open-floor mask, light found as
  `tile == TILE_FLOOR && kind != 0` (1029), `self.rules.shrine_clear` (1026); `hall_params` chosen by
  `p.kind == FeatureKind::Hall` (1073).
- `mod.rs:1326-1354` `hall_camps`: the glyph's tiles where the drawing lands centred, unmirrored, in the inner rect.
- `mod.rs:230-232`, `245-249` `relabelled_zones` and `relabel_sites` (`|| r.zone == Zone::Holy`).
- `crates/rh-sim/src/mapgen/attempt/mod.rs:243-265` the Prefix's fan reader and the round failure;
  `crates/rh-sim/src/mapgen/landmarks/stage/mod.rs:121-129` the legacy stage's fan reader (`prefix.fans`; unchanged).

Owner decisions (`docs/OWNER_DECISIONS.md`): row 14 (2026-11-14 afternoon: spawn points on the hall's open floor, 3 tiles
clear of the shrine), row 67 (2026-11-13: hall 37, two Holy camps inside, legacy elite rule for Holy). This packet keeps
both exactly.

## 4. New paths

- `crates/rh-sim/tests/mapgen_signature_site_rules.rs` (oracle).

## 5. Exclusive write scope

- `crates/rh-sim/src/mapgen/landmarks/signature/**` (`spec.rs`, `mod.rs`, `volcano.rs` if `Piece` changes, the other
  files of the directory only as the refactor needs).
- `crates/rh-sim/src/mapgen/attempt/mod.rs`: the fan reader and the round failure only (the block under the comment
  "P-MAPGEN-SIG-SCALE-4 (owner decision 2026-11-14): the Fire fan in front of the volcano's site"); the stub's
  `with_signature_table` stays as it is.
- `crates/rh-sim/src/mapgen/landmarks/stage/mod.rs`: the fan reader only, if at all.
- `docs/coordination/HANDOFF_P-MAPGEN-SIG-HOLY-1-fix_<date>.md` (the hand-off).

Nothing else: no test file, no `certify/`, no `zones/camps/`, no pins.

## 6. Contracts and references

- ADR 0028 (decisions 10, 12, 13; G6 one stream per zone; G9), `docs/adr/0028-signature-landmarks.md`.
- C-PARITY (option-off maps untouched; `pinned_off`), the determinism rules of `AGENTS.md` (draw order is behaviour).
- `crates/rh-sim/AGENTS.md`.

## 7. Behaviour to preserve

**The packet's guard (green on the stub; any pin that moves is a failed attempt; no re-pin):**

- `crates/rh-sim/tests/support/signature_pins.rs` `pinned_on_maps` (read by
  `tests/mapgen_signature_blueprint.rs::option_on_maps_are_unchanged`: seeds 1-32, option on: accepted attempt,
  `map_hash`, sorted-entrance JSON digest), `pinned_landmarks` (read by
  `mapgen_signature_blueprint.rs::the_landmarks_phase_is_unchanged`: seeds 1-16, attempts 0-1, the Landmarks phase's
  anchors, stage layers and masks and terrain, refusals included: this is the one that sees `near1`/`boxes`, so the
  spawn mask), `pinned_off` (read by the `guard_option_off_*` tests of `mapgen_signature_{church,frost,frost_lake,
  reserve,scale,bog_tree,hall,fire}.rs`).
- `crates/rh-sim/tests/mapgen_guarantees.rs` `pinned_labels`, read by
  `the_refusal_labels_of_seeds_1_to_32_are_todays_relabelled` (every refused attempt's label, seeds 1-32).
- `crates/rh-mapdress/tests/anchor.rs` `OFF_GOLDEN`, `NO_ANCHOR_GOLDEN`; `crates/rh-mapdress/tests/marsh.rs`
  `SIG_GOLDEN`, `OFF_GOLDEN`; `crates/rh-mapdress/tests/sentenced_rings.rs` `NO_ORIGIN_GOLDEN`.

**Existing tests that read what this packet changes (all stay green, none is edited):**

- `tests/mapgen_signature_hall.rs`: `the_holy_entry_is_the_hall_drawing_with_its_legend` (the Holy entry's fields via
  `specs`), `guard_the_drawing_and_the_site_are_the_owners` (`d.hall_site_side == 41`, line 828),
  `the_holy_site_carries_the_two_hall_camps_and_pins_no_elite` (Holy: two camps, `elite` false; church and cave one,
  elite true; volcano and tree none), `two_holy_camps_stand_on_the_landed_e_tiles`,
  `spawn_points_keep_off_the_hall_walls_and_three_tiles_clear_of_the_light`,
  `spawn_points_stand_on_the_halls_open_floor`, `the_holy_elites_follow_legacys_rule_among_all_ten_camps`; a
  `ReservedSite { .. }` literal at line 902.
- `tests/mapgen_signature_church.rs`: a `ReservedSite { .. }` literal at line 740; `relabelled_zones()`'s length.
- `tests/mapgen_signature_volcano.rs`: `(d.fan_depth, d.fan_side) == (14, 14)` (line 1193) and the fan's geometry
  (lines 1577-1608).
- `tests/mapgen_signature_blueprint.rs` (699-), `tests/mapgen_signature_table.rs` (69, 389, 410, 438),
  `tests/mapgen_landmark_frame.rs` (477): iterate `specs(&DEFAULT_DEFINITION)` and read its existing fields.
- In-module tests of `signature/mod.rs` (implementer's scope, not oracle files; their assertions stay, a setup may be
  adapted): `the_hall_keeps_spawns_off_its_walls_and_clear_of_its_light_only` builds a `Piece` literal and calls
  `write` directly, with its light identified only by terrain: if `Piece` or `write` changes to carry the `clear_of`
  tiles, that test's setup passes them (the 1 x 1 light at (44, 44)); its expected masks do not change.
  `the_holy_relabel_takes_the_nearer_camp_and_the_back_one_on_a_tie`, `no_other_camp_stands_in_a_relabelled_site_on_real_prefixes`.

**Struct literals in existing tests that constrain the stub and the implementation:**

- `ReservedSite { zone, rect, camp, extra, elite, stream }` (church.rs:740, hall.rs:902, mod.rs tests): add no field.
- `Definition { .., ..DEFAULT_DEFINITION }` updates only (no full literal in a test), but `tests/content.rs` reads
  upper-case `const`/`static` items as tunables: keep every number in `Definition` (`site_side`, `hall_site_side`,
  `shrine_clear`, `fan_depth`, `fan_side` stay public fields, read by oracle files), the table a function.
- `FeatureSpec { kind, job, anchor }` literals in volcano.rs:737, hall.rs:773, blueprint.rs:723/746: add no field to
  `FeatureSpec` (if the Hall params need a table home, put it on `Spec`).
- No test builds a `Spec { .. }` literal: the four new `Spec` fields are free.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-MAPGEN-SIG-HOLY-1-fix --base agent/opus/P-MAPGEN-SIG-HOLY-1` passes.
- Every test of `tests/mapgen_signature_site_rules.rs` passes; every guard of section 7 passes unchanged.
- `rg -n "Zone::Holy|FeatureKind::Hall|hall_zone|hall_marks" crates/rh-sim/src/mapgen/landmarks/signature/mod.rs`
  finds no branch in `reserve`, `write` or `fan` (the reviewer reads it; `relabel_sites` and the table may still name
  Holy, see section 11).
- `cargo clippy -p rh-sim --lib --tests --features trusted-status-probes -- -D warnings` and `cargo fmt --all -- --check`
  clean; no dead code left (`hall_marks`, the free `fan_holds_a_site_camp` go or are used).

## Oracle files (read-only for implementers)

- `crates/rh-sim/tests/mapgen_signature_site_rules.rs`
- `docs/backlog/packets/P-MAPGEN-SIG-HOLY-1-fix.md`

The seam the oracle uses (in the `stub:` commits; the implementer may change their bodies, not their signatures):
`spec::{SiteCamps, SpawnRule, Fan}` and the `Spec` fields `site_side`, `site_camps`, `spawn`, `fan` (the stub's
`specs` fills placeholders: `0`, `None`, `Box`, `None`); `SignatureContext::from_table(fixed, d, table)` (implemented:
`from_definition` passes `specs(d)`); `AttemptContext::with_signature_table(d, table)` (implemented);
`SignatureContext::reserve` and `streams` made `pub`; `SignatureContext::fan(&self, r)` and
`SignatureContext::fan_holds_a_site_camp(&self, reserved)` (stub: today's free functions).

The oracle's tests, each red on the stub for its own assertion (run on the stub, 11 of 11 red):

| Test | Asserts | Red on the stub because |
|---|---|---|
| `the_table_holds_todays_site_camp_spawn_and_fan_rules` | `specs(d)` holds (zone, site_side, site_camps, spawn, fan) = today's rules, read from `d`, on the default definition and on one with all five numbers moved | placeholders `(0, None, Box, None)` |
| `a_sites_side_is_its_entrys_site_side` | Plague entry given 33 and Holy given 43 reserve 33- and 43-tile sites, the other three 34 (seeds 1-12, at least 6 reservations; measured 11 of 12 with 43) | `reserve` reads `d.site_side`/`d.hall_site_side`: Plague 34, Holy 41 |
| `a_sites_elite_pin_is_its_entrys` | Frost `elite: false` and Holy `elite: true` flip exactly those flags; the sites, camps and stream states otherwise equal the default table's (no draw moves) | elite comes from the zone: (true, false) |
| `a_centred_sites_camps_are_its_camp_glyphs_landed_tiles` | Holy given `*`: its camps are exactly the six landed `*` tiles in grid order, same site as with `E`; Fire given `O`, elite: six distinct camps in its inner rect, `elite` true | camps read from `E` and the Holy zone only: two camps |
| `the_hall_given_the_box_rule_closes_its_whole_box` | Holy given `SpawnRule::Box`: every box and apron tile in `near1`, the box in `boxes` | `write` branches on `FeatureKind::Hall`: 1000 open tiles on seed 1 |
| `the_open_floor_distance_is_the_entrys` | Holy given `by: 1`: within 1 of the light closed, unwritten floor 2-3 from it left as before the write | `write` reads `shrine_clear` (3) |
| `the_light_is_found_by_its_glyph_not_by_its_terrain` | Holy's `*` doctored to write the light terrain: floor beyond `shrine_clear` of every `L` tile stays open, including tiles near a `*` | the light is "any written floor with terrain": `*` surroundings closed |
| `a_cave_given_the_open_floor_rule_opens_its_plain_floor` | Frost given `OpenFloor { '=', 1 }`: box kept, written tiles, ring and tiles within 1 of `=` closed, the rest of its plain floor open | the cave gets the box rule |
| `an_entrys_fan_is_reserved_with_its_numbers` | Fire given (9, 3) and Plague given (4, 0): `AttemptState::fans` is exactly those two (geometry of ADR 0028 decision 10), no camp in a fan, no legacy building in a fan (seeds 1-8, at least 4) | fan is Fire-only from `d`: one fan (14, 14) |
| `no_fan_without_an_entrys_fan` | all fans removed: no fan recorded | Fire's fan from `d` |
| `a_sites_own_camp_in_its_own_fan_does_not_fail_the_round` | a Fire site with a camp in its own fan: false; a Sentenced camp there: true; both: true | every camp tested against every fan: true |

The oracle was checked for passability on a throw-away prototype of the refactor (not committed): all 11 green, and
`mapgen_signature_{blueprint,hall,volcano,reserve,church}.rs`, `mapgen_guarantees.rs` and the `signature` unit tests
green with it (so the guards hold under one implementation of these rules).

## 9. Validation commands

- `cargo test -p rh-sim --features trusted-status-probes --test mapgen_signature_site_rules`
- `cargo test -p rh-sim --features trusted-status-probes --test mapgen_signature_blueprint --test mapgen_guarantees --test mapgen_signature_hall --test mapgen_signature_volcano`
- `cargo test -p rh-mapdress`
- `scripts/gate.sh --quick --oracle packet/P-MAPGEN-SIG-HOLY-1-fix --base agent/opus/P-MAPGEN-SIG-HOLY-1`

(Prefix every cargo call with `export PATH="$HOME/.cargo/bin:$PATH"`.)

## 10. Risk and suggested model tier

implementer-sonnet; reviewer-opus. Single crate, single module, no draw or map change, digest-guarded: the routing row
for a map-generator refactor. The risk is a silent behaviour move in the spawn mask (only `pinned_landmarks` and the
map digests see it) or in the candidate filter (a changed candidate count moves the `pick` and every later map).

## 11. Known traps

- **Draw order.** `reserve` draws one `pick` per zone per round in `reserved_zones()` order, only when the zone has
  candidates; Plague's fallback draws nothing. The candidate list (and its order: row-major squares) must not change for
  any zone: a Pin entry's squares are filtered by `inside_camp`, a Centre entry with site camps by the glyph tiles
  landing on the map (`hall_camps`), others not at all. Do not filter the volcano or the tree by camps.
- **Pin vs Centre.** The church's and the cave's camp is NOT read from the drawing's `E`: it is `inside_camp`'s tile
  (`site_depth` from the far edge), and the drawing is pinned on it. Only Centre entries read the glyph's landed tiles.
  `inside_camp` reads `self.rules.site_side`; with the entry's `site_side` equal to it nothing moves, but read the entry
  (the oracle gives Plague, a Centre entry, its own side; a Pin entry's side is not exercised).
- **The elite flag.** `ReservedSite::elite` is `camp.is_some() && entry.elite`: a Pin site with no camp is not elite.
- **The spawn mask.** The hall's mask is an OR of three sets; the default result must equal today's bit for bit
  (`pinned_landmarks` digests the masks). The volcano's streams and pools keep their by-tile mask whatever the entry's
  rule; the church's graveyard is a second feature of a `Box` entry.
- **The light by glyph.** `write` sees a `Piece` (cells as tile and terrain), not glyphs: the `clear_of` tiles must be
  carried from `stamp` (the landed tiles of the glyph, `landing.tile`), e.g. in `Piece`. The Holy `L` is the only floor
  with terrain the hall writes today, so the default maps cannot differ.
- **The Hall params.** `write` also picks `hall_params` by `p.kind == FeatureKind::Hall`; the outcome asks for no Hall
  branch in `write`: give the params a table home on `Spec` (not `FeatureSpec`, whose literals tests build). Not pinned by
  a test (only the default can be observed): the reviewer reads it.
- **The fan exemption.** Today no site with a camp has a fan, so "own camps excepted" moves nothing; the round-failure
  check in `attempt/mod.rs` must read the same fans the Prefix records.
- `tests/content.rs` reads upper-case `const`/`static` items as tunables: no new `const` table; numbers stay in
  `Definition`. The church oracle pins `relabelled_zones()`'s length (2): leave the list, fix its doc.
- `relabel_sites` still names `Zone::Holy` (`|| r.zone == Zone::Holy`). Reading "a site with camps" from the entry
  instead is in the spirit of the note but not required and not pinned; if done, Sentenced and Frost must keep the
  one-camp rule (with one camp the nearest-camp rule is the same).

## Notes from previous attempts
