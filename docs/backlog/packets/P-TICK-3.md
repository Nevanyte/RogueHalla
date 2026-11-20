# P-TICK-3: protocol 21, snapshot time from the rate sent in Welcome

- **Serves:** not in the plan: owner approval 2026-11-19 (ADR 0029); supports ORDER_OF_OPERATIONS phase 3, item 16a
- **Implementer tier:** implementer-opus (wire format, cross-crate: Rust, C#, JS)
- **Reviewer:** reviewer-opus
- **Integration base:** feature/unity-rework
- **D-rules touched:** D1 golden vectors (required for a wire change) · D2 n/a · D5/D6 none · D9 unchanged

## 1. ID and outcome
Today a snapshot's server time is `tick * 50` on every side (`crates/rh-protocol/src/lib.rs` `TICK_MS: u32 = 50`,
`snapshot_server_ms`; C# `Wire.TickMs` in `Codec.cs`, `ServerMsg.Snapshot.cs`, `Session.cs`; the JS decoder in
`crates/rh-server/web/probe.html` and `tools/protocol-js`). When this lands the protocol is version 21: server time is
`tick * tick_ms` with `tick_ms = 1000 / Welcome.tick_rate` (Welcome already carries `tick_rate: u8`; only 20 and 40 are
accepted). The delta limits (`delta.rs` `MAX_DELTA_AGE` 64, `MIN_FORCED_FULL_TICKS` 20) are scaled with the rate so
they keep their wall-clock time, and every decoder bounds them the same way. `public_ground.rs`'s literal `tick_rate: 20`
reads the room's rate. The server still sends 20 everywhere until P-TICK-5 and P-TICK-6.

## 2. Dependencies
- The "Cut delay on other players" thread's walk-hint wire change (protocol 19) merges first, agreed 2026-11-19: this
  packet then takes the next free protocol number and rebases onto it. Merged as PR #74 (protocol 19). The coliseum
  thread's heal props (P-COLISEUM-HEALS-2) take protocol 20 first (agreed 2026-11-20); this packet then takes 21 or
  the next free number. P-COLISEUM-HEALS-2 merged as PR #76 (deaf125b, protocol 20): this packet takes **21**.
- P-TICK-1b merged; no other protocol or fixture packet in flight (shared-file rule)

## 3. Relevant legacy paths
- `packages/shared/src/constants.ts:13`: `PATCH_MS = TICK_MS` (one state patch per tick)

## 4. New paths
- `protocol/fixtures/v21.json` (by `cargo run -p rh-protocol --bin protocol-fixtures`), with snapshots at both rates

## 5. Exclusive write scope
- `crates/rh-protocol/src/**` (`PROTOCOL_VERSION`, `TICK_MS`, `snapshot_server_ms`, `delta.rs` and `delta/`),
  `crates/rh-protocol/tests/**`, the fixtures binary
- `crates/rh-server/src/wire/**`, `crates/rh-server/src/outbox.rs`, `crates/rh-server/src/public_ground.rs`,
  `crates/rh-server/web/probe.html`
- `crates/rh-policy/src/delivery/collections.rs` (the forced-full rule only)
- `client-unity/RoguehallaNet/Protocol/**`, `client-unity/RoguehallaNet/Net/Session.cs` (the two `Wire.TickMs` uses),
  then `bash client-unity/sync-net.sh`
- `tools/protocol-js/**`, protocol-version pins in `tools/unity-browser-reload-proof` and `client-unity/UnityProject/Tools`
- `protocol/fixtures/v21.json`
- Existing tests that pin protocol 20: owner approval per the v17 precedent (P-WIRE17-MAP-1); the coordinator asks
  before dispatch.

## 6. Contracts and references
- C-NET and the AGENTS.md wire rule (bump, regenerate, three decoders, one branch); ADR 0010 (snapshot delta), ADR 0029

## 7. Behaviour to preserve
- At `tick_rate` 20 every frame's bytes equal protocol 20's except the version field; the v20 fixture stays as history.
- Content hash unchanged (the rate is a room setting, not content).

## 8. Acceptance checks
- `scripts/gate.sh --quick --regen-fixtures --oracle packet/P-TICK-3` passes; the coordinator runs the full gate.
- `v21.json` vectors at 20 and 40 decode to the same server times in Rust, C# and JS; a Welcome with `tick_rate` 30 is
  refused by all three.

## Oracle files (read-only for implementers)
- To be written by oracle-author on `packet/P-TICK-3` (Rust vector test, C# test in `RoguehallaNet.Tests`, JS check).

## 9. Validation commands
- `cargo run -p rh-protocol --bin protocol-fixtures`, `cargo test -p rh-protocol`, `dotnet test
  client-unity/RoguehallaNet.Tests`, `bash client-unity/sync-net.sh --check`, the JS checks in `tools/protocol-js`

## 10. Risk and suggested model tier
High: wire format across three languages. Opus.

## 11. Known traps
- `ServerMsg.Snapshot.cs` compares an override against `Tick * Wire.TickMs` by bit pattern; the comparison must use the
  session's rate or every 40 Hz snapshot looks overridden.
- A decoder that sees a snapshot before Welcome has no rate: keep refusing that order.

## Carried from the P-TICK-1a review
- `crates/rh-server/src/wire.rs` `profile_supported` does not refuse `tick_ms != 50`; refuse it there or pace the room
  from the setting (with P-TICK-5).

## Notes from previous attempts
