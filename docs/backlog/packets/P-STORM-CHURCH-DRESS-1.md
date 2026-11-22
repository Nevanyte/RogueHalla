# P-STORM-CHURCH-DRESS-1 — re-pin signature-map dressing after the rear church door

## ID and outcome

The rear church doorway changes the terrain and feature ownership of signature-on Sentenced maps. `rh-mapdress`'s
anchor-less signature test still expects the pre-door map digest in `NO_ANCHOR_GOLDEN`; refresh only those four
signature-on values using the test's existing `print_goldens` helper. Preserve `OFF_GOLDEN` and the byte-identical
option-off behavior. Retain an independent reproducible check that the pre-door single-entrance map still dresses to
the old fallback output.

## Dependencies

- P-STORM-CHURCH-MAP-1: the rear doorway is in the signature church blueprint.
- Existing P-DRESS-ANCHOR-1 golden test and helper.

## Relevant legacy paths

None; map dressing is presentation-only and has no legacy counterpart (DEV-021).

## New paths

- `docs/backlog/packets/P-STORM-CHURCH-DRESS-1.md`

## Exclusive write scope

- `crates/rh-mapdress/tests/anchor.rs` (`NO_ANCHOR_GOLDEN` only; leave `OFF_GOLDEN` untouched)
- `docs/backlog/packets/P-STORM-CHURCH-DRESS-1.md`

No mapdress runtime, rules, capture, or other golden files are in scope.

## Contracts and references

- DEV-021: map dressing has no legacy counterpart and is cosmetic.
- `crates/rh-mapdress/tests/anchor.rs`: `a_signature_map_stripped_of_its_anchors_is_dressed_as_today` checks current signature maps through the no-anchor fallback; `print_goldens` is its documented re-pin helper.
- `P-STORM-CHURCH-MAP-1`: rear door on signature maps; option-off maps and churchyard anchor remain unchanged.

## Behaviour to preserve

- Keep all four `OFF_GOLDEN` values byte-for-byte unchanged.
- Keep mapdress's no-anchor fallback algorithm unchanged.
- Re-pin only the four `NO_ANCHOR_GOLDEN` values corresponding to seeds 1–4 on current signature-on maps.
- Confirm the historical one-door map generated at the pre-door revision still dresses to the previous fallback result, using a detached temporary worktree at pre-door commit `b7d07a453` and the unchanged old expected values. The historical comparison passed all four seeds with no map dump or checked-in fixture.

## Acceptance checks

- Before the new values are installed, `a_signature_map_stripped_of_its_anchors_is_dressed_as_today` fails at the old map digest, confirming the stale oracle.
- Run `cargo test -p rh-mapdress --test anchor -- --ignored --nocapture print_goldens` and update only `NO_ANCHOR_GOLDEN` from its output.
- `with_the_option_off_the_dressing_is_todays_byte_for_byte` passes with `OFF_GOLDEN` unchanged.
- `a_signature_map_stripped_of_its_anchors_is_dressed_as_today` passes for all four signature seeds under old rules and the shipped rules without marsh rings.
- The pre-door detached-worktree fallback comparison passes with the original four `NO_ANCHOR_GOLDEN` values.
- `cargo test -p rh-mapdress` and `cargo fmt --all -- --check` pass.

## Validation commands

- `cargo test -p rh-mapdress --test anchor a_signature_map_stripped_of_its_anchors_is_dressed_as_today -- --exact`
- `cargo test -p rh-mapdress --test anchor -- --ignored --nocapture print_goldens`
- `cargo test -p rh-mapdress --test anchor with_the_option_off_the_dressing_is_todays_byte_for_byte -- --exact`
- `cargo test -p rh-mapdress --test anchor a_signature_map_stripped_of_its_anchors_is_dressed_as_today -- --exact`
- `cargo test -p rh-mapdress`
- `cargo fmt --all -- --check`

## Risk and suggested model tier

Low. Only signature-on cosmetic test values move. The option-off oracle and runtime behavior remain fixed.

## Known traps

- The existing no-anchor test clears anchor metadata from maps generated with the current signature blueprint. Its expected values legitimately change when the church drawing changes.
- `OFF_GOLDEN` values are protected and must not be regenerated.
- `print_goldens` emits both constants; copy only the `NO_ANCHOR_GOLDEN` values.
- The historical fallback check should use the pre-door source revision in a detached worktree, not a large serialized map fixture.
