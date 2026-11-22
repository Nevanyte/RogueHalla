# P-STORM-CHURCH-MAP-1 — add the rear doorway to the signature church

## ID and outcome

P-STORM-CHURCH-MAP-1 adds a centred three-tile rear doorway to the Sentenced church while retaining the current three-tile front door, five-tile churchyard gate, lane axis and footprint. The owner-approved Storm Ink visual-overhaul plan (2026-11-19) calls for a straight, walkable route through the nave. The rendering and furnishings are in the sibling Unity packet. This packet changes only the option-on signature map.

The rear opening uses the new `R` mark on row 3, columns 13–15. The existing `D` mark stays exactly on row 16, columns 13–15 because Sentenced lanes derive their axis from that single front doorway.

## Dependencies

- Existing signature landmark and blueprint implementation, including P-MAPGEN-SIG-SCALE-2.
- Owner authorization to implement the rear doorway in the Storm Ink visual-overhaul plan.

## Relevant legacy paths

None. The legacy game has no signature church (`DEV-020`).

## New paths

- `docs/backlog/packets/P-STORM-CHURCH-MAP-1.md`
- The rear-door oracle and blueprint assertions in `crates/rh-sim/tests/mapgen_signature_church.rs`.

## Exclusive write scope

- `crates/rh-sim/src/mapgen/landmarks/signature/church.txt`
- `crates/rh-sim/src/mapgen/landmarks/signature/spec.rs`
- `crates/rh-sim/tests/mapgen_signature_church.rs`
- `docs/landmarks/blueprints/church.txt`
- `docs/landmarks/blueprints/README.md`
- `docs/DEVIATIONS.md` (DEV-020 scope note)
- `docs/backlog/packets/P-STORM-CHURCH-MAP-1.md`

The repository coordinator updates any backlog index or shared status record.

## Contracts and references

- ADR 0028 G1–G10, especially G8 (signature option off remains byte-identical) and G9 (one fixed approved drawing).
- `docs/landmarks/blueprints/README.md` identifies the current approved church drawing.
- `crates/rh-sim/src/mapgen/style_gallows/lanes.rs` reads the single front `D` run and the church footprint to place Sentenced lanes.
- `crates/rh-sim/src/mapgen/landmarks/signature/site.rs` requires the smallest eight-connected church-wall piece to satisfy `R.piece_min`.
- `crates/rh-sim/tests/mapgen_signature_church.rs` pins the drawing, its orientations, anchor and generated maps.

## Behaviour to preserve

- Keep `D` as the only three-tile front doorway mark used by the lane frame. The `R` rear-door mark is plain floor owned by no feature.
- Preserve the church and churchyard feature boxes, anchor, five-tile main yard entrance, camp halo, map-option-off bytes, RNG draws, attempt bounds and certification.
- The rear opening is three tiles wide, centred on the back wall, opposite `D`. The two wall runs remain large enough for the certifier's minimum connected-piece rule.
- All eight generated orientations retain the same front-to-rear passage through the nave.

## Acceptance checks

- The generator and documentation copies of the drawing remain byte-equal.
- The oracle pins both three-tile door runs at their approved rows, and for seeds 1–32 pins their landed wall-edge openings under generated orientations.
- Signature church generation, certification, option-off identity, the unaffected signature landmarks, Sentenced lanes and map-anchor checks pass.

## Validation commands

- `cargo test -p rh-sim --features trusted-status-probes --test mapgen_signature_church`
- `cargo test -p rh-sim --features trusted-status-probes --test mapgen_signature_core`
- All existing `mapgen_signature_*`, `mapgen_style_refusal`, and `mapgen_land_sentenced` test targets.
- `cargo test -p rh-server --test map_anchor_seeds`
- `scripts/gate.sh --quick --oracle packet/P-STORM-CHURCH-MAP-1` after packet registration by the coordinator.

## Risk and suggested model tier

Medium. This moves only the option-on map geometry and does not add a gameplay feature beyond a short traversable opening. Use an implementation/review pair familiar with mapgen certification and the parallel Unity feature scope.

## Known traps

- Do not add the rear doorway as a fourth `D` mark. `style_gallows::lanes::frame` intentionally requires exactly three `D` tiles on one line.
- The anchor entrance remains the churchyard gate on row 28. The rear door is not the map anchor entrance.
- The church feature's 8-connected wall group is allowed to split into two large halves, but each must exceed `R.piece_min`.
- The current C# church art helper discovers one opening. Its multi-door handling and one-roof interpretation are owned by the Unity packet.
- Generated map hashes or refusers may change with signature mode on. Let the existing documented pin/check machinery identify and regenerate any generated evidence. Never hand-edit capture or golden files.
