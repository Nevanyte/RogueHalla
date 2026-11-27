# P-ALPHA-QUEST-BOARD-CONFIG-1: one typed Top 25 board setting

- **Serves:** ORDER_OF_OPERATIONS M-full-loop quest integration, 2026-11-26 checkpoint correction.
- **Implementer tier:** implementer-opus
- **Reviewer:** reviewer-opus
- **Integration base:** `18cdbd9c8`
- **D-rules touched:** D1 loose proof for a config-only change; D2 existing gate; D5 full-loop; D6/D9 none.

## 1. ID and outcome
Replace the two interacting Top 25 board booleans with one typed mode: off, legacy placement, spread placement. All existing disabled and legacy JSON config values and digest words remain unchanged. Spread has a distinct human-readable value and preserves the two enabled digest tags.

## 2. Dependencies
P-QUEST25-INTEGRATE and P-ALPHA-QUEST-RESTORE-1.

## 3. Relevant legacy paths
The original K1 board is represented by `SimConfig::quests`; the Top 25 deal is a native extension. No `packages/` change.

## 4. New paths
This packet brief.

## 5. Exclusive write scope
`crates/rh-sim/src/statics.rs`, `src/sim.rs`, `src/hash.rs`, `src/replay.rs` (doc comment), `src/quest_view/board.rs`, sim tests constructing config; all Rust config consumers in `crates/rh-server`, `crates/rh-headless`, and `crates/rh-load`; this packet. No fixture or capture edits.

## 6. Contracts and references
C-RNG and C-PARITY: an off or legacy config retains its old digest words. C-OBS: spread planning previews remain private to the viewer. `Sim::check_layout` rejects a skipped serialized field, so the typed value must always serialize.

## 7. Behaviour to preserve
Off writes no digest word and JSON `false`; legacy writes `quests=top25` and JSON `true`; spread writes `quests=top25` then `quests=spread_givers`, with JSON `"spread"`. Live content selects spread. No quest deal or gameplay rule changes.

## 8. Acceptance checks
The strict layout check accepts a live sim. Each mode has a distinct state hash, survives JSON restore with the same hash, and preserves the explicit old digest words. Existing Top 25 tests and checkpoint/hash pins remain green.

## 9. Validation commands
`cargo test --locked -p rh-sim --lib hash::tests::the_layout_check_accepts_the_real_state -- --exact`; `cargo test --locked -p rh-sim --test top25_board`; `cargo test --locked -p rh-sim --lib hash::tests::quest_board_modes_keep_the_old_digest_words -- --exact`; `cargo clippy --locked -p rh-sim -p rh-server -p rh-headless -p rh-load --all-targets -- -D warnings`; coordinator's full gate.

## 10. Risk and suggested model tier
The single JSON spread encoding differs from the brief-lived two-boolean spread snapshot. Existing off and legacy encodings stay stable. Strict restore and live-room tests cover the new value. Implementer-opus and reviewer-opus.

## 11. Known traps
`HashSer` omits tuple length but includes string lengths. A two-element tuple of the old tag strings produces exactly the old enabled word stream. Do not use `skip_serializing_if`, because strict layout treats absent fields as an error.
