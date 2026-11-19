# P-STORM-CHURCH-DRESS-2 — refresh remaining signature-derived dressing digests

## ID and outcome

The Storm Ink rear church doorway changes signature-on map morphology and therefore the map hash. A previous narrow
follow-up re-pinned the anchor-less signature fallback digest, but the full `rh-mapdress` suite found two more
signature-only golden groups derived from current signature maps. Refresh those values using each test's documented
`print_goldens` helper; do not change any option-off or unrelated goldens.

## Dependencies

- P-STORM-CHURCH-MAP-1: rear doorway in the signature church.
- P-STORM-CHURCH-DRESS-1: anchor-less dressing digest refreshed and historical old-map fallback verified.

## Relevant legacy paths

None. Map dressing is cosmetic and has no legacy counterpart (DEV-021).

## New paths

- `docs/backlog/packets/P-STORM-CHURCH-DRESS-2.md`

## Exclusive write scope

- `crates/rh-mapdress/tests/marsh.rs` (`SIG_GOLDEN` only; keep `OFF_GOLDEN` and non-Plague signature zone outputs fixed if helper confirms they are unchanged)
- `crates/rh-mapdress/tests/sentenced_rings.rs` (`NO_ORIGIN_GOLDEN` only)
- `docs/backlog/packets/P-STORM-CHURCH-DRESS-2.md`

No runtime, rules, captured fixture, option-off golden, or unrelated golden changes are in scope.

## Contracts and references

- DEV-021: map dressing has no legacy counterpart and is cosmetic.
- `crates/rh-mapdress/tests/marsh.rs`: `every_zone_other_than_plague_is_dressed_as_before` failed for signature seed 1 Fire; `print_goldens` prints `SIG_GOLDEN` and `OFF_GOLDEN`.
- `crates/rh-mapdress/tests/sentenced_rings.rs`: `a_file_without_origin_dresses_signature_maps_exactly_as_today` failed at seed 1; `print_goldens` prints `NO_ORIGIN_GOLDEN`.
- `P-STORM-CHURCH-DRESS-1`: anchor-less signature fallback pins were updated while the old one-door revision and option-off pin were held fixed.

## Behaviour to preserve

- Re-pin only signature-derived hash groups demonstrably changed by the new church geometry/map hash.
- Keep `OFF_GOLDEN` in `marsh.rs` fixed and verify every option-off test remains green.
- Preserve runtime dressing algorithms and all unrelated test values.
- The Plague water/mud ring values remain behaviorally checked by structural tests in `marsh.rs`; its aggregate signature digest may change only because the map identity changed.
- The pre-door mapdress fallback remains independently verified at pre-door revision `b7d07a453` by P-STORM-CHURCH-DRESS-1.

## Acceptance checks

- Both current failures are observed before implementation.
- Regenerate only the named constants with their documented ignored helper tests; inspect helper output and leave option-off goldens unchanged.
- `cargo test -p rh-mapdress` passes.
- Re-run the pre-door detached-worktree anchor fallback test and the current option-off anchor guard.
- `cargo fmt --all -- --check` passes.

## Validation commands

- `cargo test -p rh-mapdress --test marsh every_zone_other_than_plague_is_dressed_as_before -- --exact`
- `cargo test -p rh-mapdress --test sentenced_rings a_file_without_origin_dresses_signature_maps_exactly_as_today -- --exact`
- `cargo test -p rh-mapdress --test marsh -- --ignored --nocapture print_goldens`
- `cargo test -p rh-mapdress --test sentenced_rings -- --ignored --nocapture print_goldens`
- `cargo test -p rh-mapdress`
- `cargo fmt --all -- --check`

## Risk and suggested model tier

Low. Only aggregate test digests of signature-on maps move; runtime rules and output code do not change.

## Known traps

- `marsh.rs` helper prints both signature and option-off values; copy only `SIG_GOLDEN`.
- `sentenced_rings.rs` helper output is its own independent `NO_ORIGIN_GOLDEN`; `marsh.rs` uses different projections and constants.
- A changed hash for these complete signature maps does not mean individual non-Sentenced zones' dressing semantics changed. Structural assertions and option-off tests remain the oracle for those behaviors.
