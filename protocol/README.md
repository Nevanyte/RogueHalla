# protocol

The golden vectors for the wire format: JSON files that pin exact bytes, shared by the Rust, C# and JS decoders. There is
no code here. The format itself lives in `crates/rh-protocol` (`PROTOCOL_VERSION` in `src/lib.rs`).

## What is in `fixtures/`

| Files | What they pin |
|---|---|
| `v5.json` ... `v18.json` | The messages of each protocol version: server and client frames, malformed frames that must be refused, and chains of snapshots and deltas. The newest file matches the current `PROTOCOL_VERSION`. |
| `pathfinding_v*.json`, `server_walk_v*.json`, `ranged_pause_v2.json` | Rust-produced movement vectors that the C# walk prediction (`client-unity/RoguehallaNet/Net`) must reproduce. |
| `frost_wall_*_v1.json`, `sight_v1.json`, `archetypes_v1.json` | Vectors for the Frost wall, vision and hitbox archetypes. |
| `scene-candidate/` | Scene-candidate frames (`full14.json`, `client14.json`). |

## Who reads them

- Rust: the tests in `crates/rh-protocol/tests/`.
- C#: `client-unity/RoguehallaNet.Tests` (the codec, pathfinding and walk tests).
- JS: the browser probe decoder, checked by `tools/protocol-js/check-vectors.mjs`.

## Rules

These files are oracles. Never edit one by hand. Regenerate them only in a wire-format packet, with the documented
command, and make all three decoders pass in the same branch:

```sh
cargo run -p rh-protocol --bin protocol-fixtures
node tools/protocol-js/check-vectors.mjs
dotnet test client-unity/RoguehallaNet.Tests
```

`scripts/gate.sh` refuses a change under `protocol/fixtures/` unless it is given `--regen-fixtures`. Rules for wire
changes: [AGENTS.md](../AGENTS.md) and [CONTRIBUTING.md](../CONTRIBUTING.md).
