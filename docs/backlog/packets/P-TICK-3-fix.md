# P-TICK-3-fix: the Rust encoder writes snapshot time and delta limits at the connection's rate

- **Serves:** not in the plan: owner approval 2026-11-19 (ADR 0029); supports ORDER_OF_OPERATIONS phase 3, item 16a
- **Implementer tier:** implementer-opus (wire, rh-protocol + rh-server)
- **Reviewer:** reviewer-opus
- **Integration base:** claude/project-thread-baoxuk (into feature/unity-rework by pull request)
- **D-rules touched:** D1 golden vectors unchanged at 20 Hz · D2 n/a · D5/D6/D9 none

## 1. ID and outcome
reviewer-opus note on P-TICK-3: only the decoders read the rate. The encoder compares against the 20 Hz default
(`delta/encoder.rs:206`, `delta/snapshot_data.rs:186`, `snapshot_server_ms(tick)`), and its forced-full and age limits
stay in 20 Hz ticks (`encoder.rs:70,76`, `diff.rs:287`, `write.rs:214`, `rh-policy` `CollectionPublisher::resync_due`,
`rh-server/src/outbox.rs`). A 25 ms room would write `tick*25` as an override on every frame, which `decode_at(.., 40)`
refuses. When this lands `ConnEncoder` (and `SnapshotData`, the `SnapshotDelta` writer, the policy forced-full rule and
the outbox) take the connection's `tick_ms`, set where the Welcome is sent, and use `snapshot_server_ms_at`,
`max_delta_age(tick_ms)` and `min_forced_full_ticks(tick_ms)`. At 50 ms every frame stays byte-identical. Rust
re-encoding a snapshot decoded at 40 Hz round-trips. `profile_supported` still refuses 25 ms (P-TICK-5 lifts it).

## 2. Dependencies
- P-TICK-3 merged (152fc4b9)

## 5. Exclusive write scope
- `crates/rh-protocol/src/**` (encoder side), `crates/rh-protocol/tests/**` (new tests only)
- `crates/rh-server/src/wire.rs`, `crates/rh-server/src/wire/**`, `crates/rh-server/src/outbox.rs`
- `crates/rh-policy/src/delivery/collections.rs` (forced-full rule)
- Stale doc comments the review listed: `diff.rs:259,263`, `server_msg.rs:169,172`, `command.rs:179`; and the three
  Rust test names that still say protocol 20 (`frost_wall_wire.rs:131`, `heal_prop_wire.rs:72`, `map_public.rs:329`)

## 7. Behaviour to preserve
- `protocol/fixtures/v21.json` unchanged (regenerate and diff: no change); all three decoders pass it.

## 8. Acceptance checks (implementer writes them red first)
- A 25 ms `SnapshotData` encodes with no time override, and `decode_at(.., 40)` returns `tick*25`.
- A 40 Hz encoder forces a full after 1000 ms (40 ticks) and accepts a delta base 65..128 ticks old; at 20 Hz 20 and 64.
- A snapshot decoded at 40 Hz re-encodes (`encode/record.rs`) to its own bytes.
- `scripts/gate.sh --quick --keep-going --base claude/project-thread-baoxuk` passes.

## 10. Risk
High (wire, cross-crate). Opus.
