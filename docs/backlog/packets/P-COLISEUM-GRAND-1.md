# P-COLISEUM-GRAND-1: the grand coliseum (octagon, four L walls), live in the map pool

- **Serves:** not in the plan: owner request 2026-11-19 (Nevin, with a drawing: "about 2x as large", octagon; "8 sides
  sorry for confusion"), relayed by the coliseum lane. Owner approval of the design below is pending (the lane proposed it).
- **Implementer tier:** implementer-opus (cross-crate: `rh-sim` + `rh-server`)
- **Reviewer:** reviewer-opus
- **Integration base:** `feature/unity-rework` @ `24a2ce99`
- **D-rules touched:** D1 loose proof (no legacy counterpart; geometry pinned by tests, legacy layout pinned by digest) ·
  D2 n/a · D5/D6/D9 none (the server picks a map; no rule, wire format or client prediction changes)

## 1. ID and outcome

The coliseum in the middle of every pooled map becomes the owner's new drawing: an octagonal outer wall 36 tiles across
(twice the legacy 18), four L-shaped walls, one per quadrant, corners pointing at the centre and arms running outward,
leaving a 12-tile-wide open cross through the middle and a 4-tile corridor between the arms' ends and the outer wall;
eight player teleport spots (four at the cross arm ends, four in the pockets behind the L corners); gates 4 wide on the
four straight sides as today. The server's map pool (`--map-pool`) generates around it by default; `--legacy-coliseum`
turns it off. The legacy layout (`coliseum::fixed_layout()`) is untouched, so every existing test, fixture, capture and
parity pin is byte-identical. The four "Heals" chests at the centre of the drawing are NOT in this packet (a later packet,
pending the owner's answer).

## 2. Dependencies

None (base `24a2ce99`). `P-COLISEUM-GATES-1` is being written beside it (`/home/claude/RH-colgates`): if it touches
`mapgen/coliseum.rs` or the gates, the two merge one at a time.

## 3. Relevant paths (legacy and port)

Legacy (the reference for the legacy coliseum only; the grand one has no legacy counterpart):
- `packages/shared/src/coliseum.ts:17-18` `COL_HALF = 9`, `COL_CHAMFER = 13`; `:23` `INNER_SPAWN_R = 5.5`; `:77` the
  footprint test (`ax <= HALF-0.5 && ay <= HALF-0.5 && ax+ay <= CHAMFER`); `:110` wall ring / floor; `:159-162` the
  inner spawn tiles (floor of `C + R*cos/sin`); `:168` pillar variants (`PILLAR_LAYOUTS.length * 2`, plain and transposed).
- `packages/shared/src/mapgen.ts:243` the attempt's first draw picks the pillar variant (`floor(rng() * pillars.length)`);
  `:260` border, pillar and wall ring become walls; `:978` `innerSpawns` exported from `innerTiles`.
- `packages/server/src/sim/arena.ts:530` `coliseumSpots`: for each of n points on a ring of `COLISEUM_RING_RADIUS`
  (`packages/shared/src/constants.ts:191`, 5 tiles) the nearest unused inner spawn.

Port:
- `crates/rh-sim/src/mapgen/coliseum.rs`: `FixedDefinition`, `DEFAULT_DEFINITION`, `fixed_layout()`,
  `fixed_layout_from()` (footprint, wall ring, gates, lanes, inner tiles, pillar masks with the 90-degree rotation
  `(x, y) -> (CX+CY-1-y, x-CX+CY)` about the map centre and the transposed variant), `visit_content`.
- `crates/rh-sim/src/mapgen/attempt/mod.rs:211` one draw picks `pillar_variant` among `f.pillars.len()` masks
  (`pillars.len()*2` of the definition); `:321-330` walls from border, pillar, wall ring; gates opened.
- `crates/rh-sim/src/mapgen/records/mod.rs:250` `inner_spawns` from `inner_tiles`.
- `crates/rh-sim/src/systems/coliseum.rs:13-43` `RING_RADIUS = 5 * TILE`, `spots`: nearest unused inner spawn per ring
  point (needs `inner_spawns.len() >= n`; eight spawns cover `MAX_PLAYERS`).
- `crates/rh-sim/src/content/hash.rs:19-25` `content_hash_of_rules` folds `coliseum::default_definition()`;
  `content.rs:464-495` `MapDefinitions` (`coliseum: &DEFAULT_DEFINITION`); `content/definitions.rs:127-128` the fold.
- `crates/rh-server/src/map_pool.rs:79-117` `StyledMaker { signature }`, `generate` builds `coliseum::fixed_layout()`;
  `:350-357` `MapPool::start(configured, signature)`; `runtime.rs:27-30, 193-198` `--map-pool`,
  `--signature-landmarks` (needs `--map-pool`); `main.rs:225-237` starts the pool.

Owner decisions searched (`docs/OWNER_DECISIONS.md`, "coliseum", "content hash"): none on the coliseum's shape. Row 91
(2026-11-12, "landmark definitions out of the content hash") is the precedent used for the content identity below.

## 4. The design (the lane's, proposed to the owner; checked by the oracle author)

`pub const GRAND_DEFINITION: FixedDefinition` beside `DEFAULT_DEFINITION`, built by the same `fixed_layout_from`:

| field | value | why |
|---|---|---|
| half | 18.0 | 36 tiles across (legacy 18) |
| chamfer | 25.0 | straight sides 16 tiles, chamfer sides about 14: an octagon, near regular |
| gate_width, lane_depth, lane_margin | 4, 3, 1 | as legacy |
| ring_road, distance_cap | 3, 6 | as legacy |
| inner_spawn_radius | **13.0** (lane proposed 14.0) | see "Checks" |
| inner_spawn_phase | 0.0 | spawns on the axes and diagonals |
| pillar_source_center | 32 | shift `CX - 32 = 48` |
| pillars | one orbit, turns 4, generators `(25,19)..=(25,25)` then `(19,25)..=(24,25)` (13, in this order) | the top-left L; the rotations give the other three |

`pub fn grand_layout() -> FixedLayout { fixed_layout_from(&GRAND_DEFINITION) }`; `fixed_layout()` stays legacy.

Resulting geometry (all pinned by the oracle): footprint x, y 62..=97 (`co_rect {62,62,36,36}`), row widths 16, 18, ...,
34, then 36 (1076 tiles); gates N `(78..=81, 62)`, E `(97, 78..=81)`, S `(78..=81, 97)`, W `(62, 78..=81)`; the Ls
(corner; vertical arm; horizontal arm): TL `(73,73)`, `x=73 y=67..=73`, `y=73 x=67..=72`; TR `(86,73)`,
`x=86 y=67..=73`, `y=73 x=87..=92`; BR `(86,86)`, `x=86 y=86..=92`, `y=86 x=87..=92`; BL `(73,86)`, `x=73 y=86..=92`,
`y=86 x=67..=72`. Both pillar variants are the same mask (an L is symmetric about the diagonal), so the attempt's one
variant draw is kept and picks between two identical masks. Inner spawns `(93,80) (89,89) (80,93) (70,89) (66,80)
(70,70) (80,66) (89,70)`.

Checks of the lane's arithmetic (prototype on this base):
- Cross arms 12 tiles wide (floor 74..=85 between the L corners at 73 and 86): **holds**.
- 4-tile corridor (rows 63..=66) between an arm end (row 67) and the outer wall ring (row 62): **holds**.
- Every L tile on `co_floor`; `lane_in` tiles clear of the Ls: **holds**.
- Inner spawns on floor, off the Ls: radius 14 **puts the four arm-end spawns `(94,80) (80,94) (65,80) (80,65)` inside the
  gate lanes** (`lane_in` is 3 deep: rows/columns 63..=65 and 94..=96), i.e. a teleported player stands in the gate mouth.
  **Adjusted to radius 13**: arm-end spawns at the arm-end line (`(80,66)` is the corridor row next to the arm ends),
  pocket spawns at 9.19 tiles diagonally (`(70,70)` inside the TL pocket x, y 67..=72). The eight spawns are symmetric
  about the centre in tile-centre terms (13.5 tiles from the centre on the axes).
- Certification: legacy 32/32 and grand 32/32 on seeds 1..=32 with the server's options (`decor: false`), and 32/32 for
  both with `signature: true`. **No certifier or repair tuning was needed**; `crates/rh-sim/src/mapgen/**` beyond
  `coliseum.rs` stays out of scope unless the real build differs from the prototype (then stop and report).

Server: `StyledMaker` gains `grand_coliseum: bool` (stub in place); `Default` stays legacy (`false`) so every existing
pool, anchor, dress and tool caller keeps its maps. `Settings` parses `--legacy-coliseum` (a switch like
`--signature-landmarks`: no value, once, needs `--map-pool`, refused by `training`) and `Settings::styled_maker()` (stub
in place) returns `StyledMaker { signature: --signature-landmarks, grand_coliseum: !--legacy-coliseum }`;
`main.rs` hands it to `MapPool::start(configured, maker)` (stub in place). `StyledMaker::generate` builds
`coliseum::grand_layout()` when `grand_coliseum`, else `fixed_layout()`. `hosting/` is not changed: the hosted command
lines that pass `--map-pool` get the grand coliseum with no edit.

### Content identity (decision: the grand definition stays OUT of `content_hash`, for the owner)

`content_hash` is one build-wide constant the client compares at admission; it cannot depend on a server switch. Folding
`GRAND_DEFINITION` would move it (`GOLDEN_CONTENT_HASH` in `crates/rh-sim/tests/content.rs`, `parity/abilities/hash-tape.json`,
`protocol/fixtures/archetypes_v1.json`, two browser-proof scripts, and the Unity client's `BakedContent` rebuild) for a
definition no client reads: the map, its coliseum rect, gates and inner spawns travel in the Welcome map blob and in the
replay (`replay.rs:255`, a replay holds its whole map). That is the case of owner decision row 91 (2026-11-12,
generator landmark definitions out of the content hash) and of the signature-landmark rows in `tests/content.rs`
`NON_GAMEPLAY`. So: `DEFAULT_DEFINITION` stays folded exactly as today (hash unchanged, `hash_golden` stays green), and
`GRAND_DEFINITION` gets a `NON_GAMEPLAY` row "not folded into content_hash until the owner decides" (the `oracle-amend:`
commit). If the owner wants it folded, a follow-up packet adds `coliseum_grand` to `MapDefinitions` and moves the hash
and every pinned copy in one branch.

## 5. Exclusive write scope

- `crates/rh-sim/src/mapgen/coliseum.rs` (replace the `GRAND_DEFINITION` stub with the table above; `grand_layout` as is).
- `crates/rh-server/src/map_pool.rs` (`StyledMaker::generate` reads `grand_coliseum`; docs).
- `crates/rh-server/src/runtime.rs` (`Settings.legacy_coliseum` field, `--legacy-coliseum` parsing and refusals,
  `styled_maker`; its own `tests` module may gain lines but no existing assertion changes).
- `crates/rh-server/src/main.rs` (only if the log line should name the coliseum).
- Only if the real build fails certification where the prototype did not: stop and report; do not tune
  `crates/rh-sim/src/mapgen/**` without the coordinator. `systems/coliseum.rs` (`RING_RADIUS`) is not needed: `spots`
  takes the nearest unused spawn, and the oracle shows 8 and 2 players land on distinct grand spawns.
- Not in scope: `content.rs`, `content/hash.rs` (decision above), `hosting/`, `client-unity/`.

## 6. Contracts and references

C-PARITY (legacy layout and every existing fixture byte-identical), C-RNG and the determinism rules (the attempt's draw
count and order are unchanged: one `rng()` picks the pillar variant, over 8 masks in the legacy layout and 2 identical
masks in the grand one), R-AUTH (the server alone picks the map; the client renders the blob), C-INPUT unaffected.

## 7. Behaviour to preserve

- `coliseum::fixed_layout()` byte-identical (digest `0x9c5f_d97d_3883_c908` of its serde_json bytes, pinned).
- `content_hash()` unchanged (`GOLDEN_CONTENT_HASH` `0x7a54_2a13_187f_034c`).
- `StyledMaker::default()` and every existing test's maker produce the legacy maps (stub commit sets
  `grand_coliseum: false` in the existing struct literals: `map_pool/tests.rs`, `tests/map_anchors.rs`,
  `tests/dress_bake.rs`, `tests/map_anchor_seeds.rs`).
- `Settings::sim()` is the same with and without `--legacy-coliseum` (the switch changes maps, not rules).

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-COLISEUM-GRAND-1` green; the coordinator's full gate on the merge.
- `cargo test -p rh-sim --lib mapgen::coliseum_grand_tests` (7 tests) and
  `cargo test -p rh-server --test coliseum_grand` (2 tests) green; `cargo test -p rh-sim --test content` green.

## Oracle files (read-only for implementers)

- `crates/rh-sim/src/mapgen/coliseum_grand_tests.rs` (`oracle:`): geometry (octagon, gates, the four Ls, cross,
  corridor, eight spawns reachable in both variants), legacy digest, styled pipeline certifies at least 15 of seeds
  1..=16 with the grand coliseum exactly on the map, arena `spots` for 8 and 2 players.
- `crates/rh-server/tests/coliseum_grand.rs` (`oracle:`): `--map-pool` defaults to grand, `--legacy-coliseum` and its
  refusals, `StyledMaker` serves the chosen coliseum.
- `docs/backlog/packets/P-COLISEUM-GRAND-1.md` (`brief:` / `oracle:`).
- `crates/rh-sim/tests/content.rs` (`oracle-amend:`): one added `NON_GAMEPLAY` row for `mapgen/coliseum.rs`
  `GRAND_DEFINITION` (no existing expectation changes; listed for the owner's approval).

Stub commit (the implementer may edit these): `crates/rh-sim/src/mapgen/coliseum.rs` (`GRAND_DEFINITION` = a copy of
`DEFAULT_DEFINITION`, `grand_layout`), `crates/rh-sim/src/mapgen/mod.rs` (`mod coliseum_grand_tests`),
`crates/rh-server/src/map_pool.rs` (field, `PartialEq`, `MapPool::start(configured, maker)`),
`crates/rh-server/src/runtime.rs` (`styled_maker` returning `grand_coliseum: false`), `crates/rh-server/src/main.rs`,
and the compile-only `grand_coliseum: false` in `src/map_pool/tests.rs`, `tests/map_anchors.rs`, `tests/dress_bake.rs`,
`tests/map_anchor_seeds.rs`.

## 9. Validation commands

- `CARGO_TARGET_DIR=... cargo test -p rh-sim --lib mapgen::coliseum_grand_tests`
- `cargo test -p rh-server --test coliseum_grand`
- `cargo test -p rh-sim --test content`
- `scripts/gate.sh --quick --oracle packet/P-COLISEUM-GRAND-1`

## 10. Risk and suggested model tier

implementer-opus / reviewer-opus: cross-crate (rh-sim and rh-server) and the live map choice. The code is small (a
definition table, a branch in `generate`, one switch); the risk is in what it changes for players.

Risks:
- **The live arena changes for every pooled room** (default on). Bots (`HumanBot`, Brain) read `inner_spawns` and the
  map; a bigger coliseum with walls inside may change fight pacing; not measured here.
- **The Unity client** draws the coliseum from the map blob; any art, dressing (`rh_mapdress`) or camera framing that
  assumes the 18-tile legacy coliseum is not checked by this oracle. A content-hash move (if the owner asks for the fold)
  means a `BakedContent` rebuild.
- `DEVIATIONS.md` row (proposed, for the owner): "DEV-<n> | world | Legacy coliseum: a chamfered 18-tile square with
  pillar orbits (`packages/shared/src/coliseum.ts:17-23, 168`). | Pooled maps (default; `--legacy-coliseum` off) use the
  grand coliseum: a 36-tile octagon, four L walls, an open cross, eight spawns at radius 13
  (`coliseum::GRAND_DEFINITION`). Legacy layout untouched. | Owner request 2026-11-19 (drawing). |
  `mapgen::coliseum_grand_tests`, `rh-server/tests/coliseum_grand.rs` | reversible (flag) | proposed".
- The packet changes the default behaviour of `--map-pool` without a hosting edit; a hosted environment that must stay
  on the legacy coliseum needs `--legacy-coliseum` added to its command line (a `hosting/` edit, owner approval).

## 11. Known traps

- Do not change `fixed_layout_from`: it builds both layouts, and the legacy digest pins its legacy output. If the grand
  layout needs a shape `fixed_layout_from` cannot express, stop and report.
- Generator order in the orbit is behaviour (the mask is the same set, but keep the brief's order: it is the
  definition's identity if it is ever folded).
- `inner_spawn_radius` 14 looks right on paper and lands four spawns in the gate lanes; the oracle pins 13.
- `StyledMaker::default()` must stay legacy: `map_pool/tests.rs` `the_real_generator_serves_distinct_playable_maps`,
  `signature_landmarks_only_set_the_generator_option`, `tests/map_anchors.rs`, `tests/dress_bake.rs` and
  `tests/map_anchor_seeds.rs` compare against `coliseum::fixed_layout()` maps.
- `--legacy-coliseum` without `--map-pool` is refused, like `--signature-landmarks`.

## Notes from previous attempts
