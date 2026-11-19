# P-STORM-CHURCH-MAP-2 — align signature blueprint oracles with the approved rear door

## ID and outcome

P-STORM-CHURCH-MAP-1 added the approved centered `R` mark at church grid row 3, columns 13–15, opposite the existing
front `D` on row 16. The signature blueprint oracle still described only the former front door and pinned the old
signature-on map and Landmarks-phase digests. Add the rear mark to the oracle's authored legend and coordinate checks,
then re-take the documented signature-on pins. Keep the other four drawings, the church's front gate entrance rule,
and every option-off pin fixed.

This packet changes test expectations only. It does not change map generation or gameplay.

## Dependencies

- P-STORM-CHURCH-MAP-1: approved two-door church blueprint and generator legend.
- `crates/rh-sim/tests/mapgen_signature_blueprint.rs`'s existing generated drawing and digest tests.

## Relevant legacy paths

None. The signature church is not part of the legacy game (DEV-020).

## New paths

- `docs/backlog/packets/P-STORM-CHURCH-MAP-2.md`

## Exclusive write scope

- `crates/rh-sim/tests/mapgen_signature_blueprint.rs`
- `crates/rh-sim/tests/support/signature_pins.rs`
- `docs/backlog/packets/P-STORM-CHURCH-MAP-2.md`

The church blueprint and runtime legend were already changed in P-STORM-CHURCH-MAP-1. This packet does not touch
capture/parity fixtures, option-off pins, or other mapgen implementation.

## Contracts and references

- ADR 0028 G8: option-off generation remains byte-identical.
- DEV-020: signature landmarks are option-on generated world geometry with no legacy counterpart.
- `docs/landmarks/blueprints/church.txt`: `R` rear opening on row 3, `D` front opening on row 16.
- `crates/rh-sim/src/mapgen/landmarks/signature/spec.rs`: church `R` mark is plain, unowned floor.
- `crates/rh-sim/tests/mapgen_signature_church.rs`: seeds 1–32, eight orientations, and both landed wall-edge openings.
- `crates/rh-sim/tests/mapgen_signature_blueprint.rs`: authored drawing parser/mark oracle, all-orientation landing/extent oracle, and documented `print_signature_pins` helper.
- `crates/rh-sim/tests/support/signature_pins.rs`: shared current signature-on and option-off digests.

## Behaviour to preserve

- Church drawing remains 29 x 29. `D` remains the three-tile front doorway and controls the Sentenced front-axis rule; `R` is the three-tile rear passage and does not replace the churchyard's `FrontRun(E)` anchor entrance.
- The authored reader checks both door marks at their exact rows, all orientations preserve the 29 x 29 footprint, and landed marks match the drawing.
- Frost cave, Plague tree, Fire volcano, and Holy hall drawings, marks, extents and all their per-drawing checks remain unchanged.
- Re-take only `pinned_on_maps` and `pinned_landmarks` through the existing helper. Preserve `pinned_off` byte-for-byte.
- Keep the existing option-off and independent signature structure tests green.

## Acceptance checks

- The current failure is observed before implementation: the church reader rejects `R`, its expected legend omits `R`, and signature-on map/Landmarks pins are stale.
- Add `R` as a plain named mark to the test legend and pin `(13–15, 3)` separately from `(13–15, 16)`; explicitly prove that `Mark(R)` is the rear opening while `FrontRun(E)` remains the yard entrance.
- Re-pin signature-on arrays using `cargo test -p rh-sim --features trusted-status-probes --test mapgen_signature_blueprint -- --ignored --nocapture print_signature_pins`; do not change `pinned_off`.
- Demonstrate the prior oracle's historical purpose: at pre-door commit `b7d07a453`, its existing `mapgen_signature_blueprint` tests pass with the old one-door pins.
- Current blueprint, mapgen signature, style refusal, Sentenced lanes, server anchor and mapdress tests pass. Existing plain-map and all other landmark controls remain green.
- Full project gate passes with no parity capture edits.

## Validation commands

- `cargo test -p rh-sim --features trusted-status-probes --test mapgen_signature_blueprint`
- `cargo test -p rh-sim --features trusted-status-probes --test mapgen_signature_blueprint -- --ignored --nocapture print_signature_pins`
- `cargo test -p rh-sim --features trusted-status-probes --test mapgen_signature_church --test mapgen_signature_core --test mapgen_signature_table --test mapgen_landmark_frame --test mapgen_land_sentenced`
- `cargo fmt --all -- --check`
- `scripts/gate.sh --quick --oracle packet/P-STORM-CHURCH-MAP-2`

## Risk and suggested model tier

Low. One owner-approved, non-gameplay blueprint opens three plain floor tiles; oracle changes are limited to the church's
rear mark and dependent signature-on digests. The option-off pin prevents the drawing from changing disabled maps.

## Known traps

- `FrontRun(E)` names the graveyard gate and remains the `ZoneAnchor` entrance; the rear church door is a separate `R` mark.
- The `mapgen_signature_blueprint` map digests include generated landmarks and move because the approved church geometry changed. The digest-neutrality assertion must state that it is a current baseline pin, not a claim that the new signature map is unchanged from the one-door drawing.
- The existing option-off pins and other four drawings are controls; never regenerate them from the option-on helper.
- No parity captures or golden files are regenerated by this packet.
