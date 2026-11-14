# Fire / Scorch stage-six callbacks

This leaf ports `scorch.shape`, `scorch.surface`, and `scorch.props` from the unchanged TypeScript source. It runs the guarded native phase contexts directly and returns an explicit typed `Plan`. Forge landmark search/stamping is an earlier stage and remains owned by `landmarks::recipes::search_family::scorch`.

`Callbacks::new` borrows the admitted Forge template/radius definition, common recipe scale, accepted feature kit, fixed coliseum layout/rules, and sole terrain/prop catalog. Callback-specific numeric policy is in `Definition`; its single field list also generates the content visitor. The coordinator must include that visitor in final content identity and wire the callback into the stage-six dispatcher. There is no local prop, geometry, or fixed-layout catalog.

The shared-seam proposal extracts the existing Forge `radius4` and radial `Frame` arithmetic unchanged into crate-private helpers. The original landmark recipe delegates to them; its complete source-state and trace fixtures still pass. Common `camp_nodes_from` is the separate Warren seam, using the same original body and fixed-distance length gate. The test-only runtime prefix restoration seam is the previously reviewed Holy proposal; it is not a production state mutator.

## Signature mode: craters round the volcano (P-LAND-FIRE-1)

With the Fire frame (`pipeline::Options::signature`) the lava river is not drawn (the volcano's three streams replace
it) and each camp's crater size comes from its band round the volcano (`craters.rs` `camp_band`): near the largest
radius option, mid the second, far the smallest, core any that keeps out of the core. Owner rule: "Craters keep clear
of all three streams. Every Fire camp keeps its crater; size falls with distance and none is dropped." So the band's
option is tried first, then each smaller option down to `radius_min4` (never a larger one); an option whose disc
(16 * d^2 < r4^2 round the camp tile) holds stream or pool lava is skipped, and so is one the rim, route-stretch, core
or pen-share checks refuse. A camp no option fits has no crater, and the attempt is refused by the guarantees in
`guarantees.rs` (`style_zones::guarantees(Zone::Fire)`, run by the pipeline on the finished map):
`"crater-per-camp"` and `"crater-clear-of-lava"` (no stream or pool lava in a crater's disc, no crater tile within
`lava_clearance()` = 2 tiles of it). Stream and pool lava is read from the frame (`guarantees::stream_lava`: cells
written LOW with the lava terrain outside the cone's box), one definition for the styler and the guarantees. No draw:
trying more options reads hash noise only. Option off (no frame) is untouched.

## Capture and parity

`tools/map-fixtures/style-scorch.ts` admits only Linux x64 Node 22.23.4. It observes six unchanged `tryAttempt` callers across seeds, attempts, wrapping inputs, and decor modes. It compares the complete original and observed final map (or null) and hash, then captures the genuine pre-Fire input, including scratch and wrote buffers. Every current actual case returns a map; the null comparison path is present but this corpus does not claim an actual null case.

Fifteen direct inputs exercise open/empty maps, a depth-band failure after exactly one random draw, absent and isolated camps, frontier crescents, footprint refusal and partial guards, route crossings, an additional breach for excess route stretch, the strict ford merge boundary, protected windows, prop protection/cap exhaustion, and a genuinely source-stamped rotated/mirrored Forge. These call the actual source callbacks and runtime. No expected operation script or post-callback state is used as native input.

Native replay compares the complete plan after each phase, tiles, terrain, props, feature records/order/payload, owners, wrote/reserved arrays, all counters and scratch buffers, plus each stream's recorded draws and four-word continuation. Actual captures include the caller's post-shape repair; direct captures intentionally execute only callbacks. Inventory, nested keys, raw duplicate keys, canonical RLE, source-file inventory, dimensions, and scalar narrowing are strict. Malformed plans and Forge boxes are refused before either continuation callback writes; malformed prefix restoration is atomic.

The source-captured pre-Fire state is a prerequisite input. This leaf does **not** prove that all earlier native stages/callbacks produce that prefix. The later integrated generator must execute every native stage continuously and compare the resulting full map. No styled-map pin, complete game, or performance claim follows from these tests.

## Reproduction

Use the cached Linux amd64 Docker image `c86ecead9e55` with `--pull=never`, source mounted read-only, private writable output, and executable temporary dependency storage. Install/run Node 22.23.4 inside that private environment. Capture with `RH_STYLE_SCORCH_OUTPUT=/output/style-scorch.json node --import tsx tools/map-fixtures/style-scorch.ts`; add `--check` for byte-for-byte regeneration.

Focused native checks are `cargo test -p rh-sim --all-features --lib style_scorch`, the original `search_family` regression filter, rustfmt on owned Rust files, and strict package Clippy. Coordinator-owned module registration and dependencies are necessary for the isolated scratch to compile. They are excluded from this leaf's manifest.

## Custom policy admission

The supported crater jitter magnitude is finite `0..=16` cells. This callback admits shared Forge radii from 3 through 64 quarter-cells; the lower bound excludes zero-distance ring vectors at a half-cell radius. Together with the upper bound of 64 quarter-cells (16 cells), primary raster endpoints remain within 32 cells of their camp; adjacent endpoint differences and interpolation steps remain at most 64. The integer interpolation numerator is bounded by 8256, and each gap has bounded work. The second rim tile remains within 33 cells. This is an admission limit, not a change to the source default of 1.1.

River candidate windows must have `length_min >= 1` so every accepted window has an anchor in the path. Tests refuse both reported overflow/empty-window configurations before callbacks write, execute the admitted jitter endpoints and minimum window bound on genuine source-captured input, and exhaustively compare gap interpolation within the admitted envelope against source Number arithmetic. These custom runs prove admission and arithmetic safety; default source parity remains covered by the unchanged canonical fixture.
