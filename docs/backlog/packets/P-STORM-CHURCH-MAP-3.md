# P-STORM-CHURCH-MAP-3 — keep both church doors open in the enclosure oracle

## ID and outcome

The approved rear church opening gives the Sentenced elite camp a second exit. `mapgen_signature_core` still models a
one-door church: it closes only the front door before asserting the camp is sealed, so it now fails by finding the
intended rear route to the spawn region. Extend that structural oracle to find and check both three-tile openings on
opposite church wall edges, then prove that the camp is sealed only after closing both.

This is a test expectation update for owner-approved map geometry. It does not change gameplay or generation behavior.

## Dependencies

- P-STORM-CHURCH-MAP-1: approved rear door at row 3 opposite the front door.
- P-MAPGEN-SIG-SCALE-2: existing church enclosure and one-door connectivity oracle.

## Relevant legacy paths

None. The signature church has no legacy counterpart (DEV-020).

## New paths

- `docs/backlog/packets/P-STORM-CHURCH-MAP-3.md`

## Exclusive write scope

- `crates/rh-sim/tests/mapgen_signature_core.rs`
- `docs/backlog/packets/P-STORM-CHURCH-MAP-3.md`

## Contracts and references

- ADR 0028 G8: map option off remains unchanged.
- `docs/landmarks/blueprints/church.txt`: front `D` row 16, rear `R` row 3.
- `crates/rh-sim/tests/mapgen_signature_church.rs`: generated maps preserve both wall-edge openings under all orientations.
- `crates/rh-sim/tests/mapgen_signature_core.rs`: path and feature ownership checks for church enclosure.

## Behaviour to preserve

- Keep the existing front-side door helper semantics and graveyard symmetry axis.
- On each checked signature map, both opposite church edges expose exactly three contiguous, unowned floor tiles.
- Each opening reaches the first spawn's connected region with the map intact.
- The far elite camp and its clearing are sealed from the first spawn after both openings are closed in the test map.
- All clearing floor tiles remain connected to the camp after both doors are closed.
- No option-off or runtime code changes.

## Acceptance checks

- The old assertion is reproduced before the change: shutting only the front door leaves the camp connected to the spawn through the rear door.
- The new oracle independently finds the rear run from the wall edge opposite the anchor's front, checks its width and floor ownership, and checks both doors against the spawn-connected region.
- Closing both wall openings blocks the spawn while retaining interior camp clearing reachability.
- The Sentenced core test and related signature/church tests pass; option-off pins remain untouched.
- Full project gate passes.

## Validation commands

- `cargo test -p rh-sim --features trusted-status-probes --test mapgen_signature_core`
- `cargo test -p rh-sim --features trusted-status-probes --test mapgen_signature_church --test mapgen_signature_blueprint`
- `cargo fmt --all -- --check`
- `scripts/gate.sh --quick --oracle packet/P-STORM-CHURCH-MAP-3`

## Risk and suggested model tier

Low. This only strengthens the geometry oracle to recognize the second approved opening; no simulation or generation code changes.

## Known traps

- The front `D` remains the camp/front axis door; the rear `R` is on its opposite wall. Do not move either mark or the graveyard gate's anchor entrance.
- Closing only one opening is no longer a valid enclosure test.
- Keep the rear door structural check separate from `FrontRun(E)`, which continues to name the yard gate.
