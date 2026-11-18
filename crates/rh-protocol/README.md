# rh-protocol: the binary wire format

The messages between the Rust server and the clients: one message per WebSocket binary frame, little-endian, no
reflection. The version is `PROTOCOL_VERSION` in `src/lib.rs`. The crate has no dependencies and does not know the
simulation: `rh-server` converts between sim types and wire types (`crates/rh-server/src/wire.rs`). Decoders refuse
malformed, truncated or non-finite input with an error; they never repair it.

## Where it sits

Called by `rh-server` (encode server messages, decode client ones), `rh-policy` and `rh-load`. The C# mirror in
`client-unity/RoguehallaNet/Protocol/` and the JS probe (`crates/rh-server/web/probe.html`) decode the same bytes and are
held to the same golden vectors in `protocol/fixtures/v<PROTOCOL_VERSION>.json`.

## Entry points

- `src/lib.rs`: the crate header is the frame layout and a per-version change log; also `PROTOCOL_VERSION`, size limits and re-exports.
- `src/codec.rs`: `Writer` and the bounds-checked `Reader`, varints.
- `src/encode.rs`: encoders and decoders for the messages, and the Welcome header.
- `src/delta.rs`: per-connection snapshot deltas with append-only field masks.
- `src/state.rs`, `src/quant.rs`, `src/map.rs`, `src/vision.rs`, `src/archetype.rs`: observer-owned collections,
  quantization of remote actors, the map blob, and the vision and hitbox tables a Welcome carries.
- `src/samples.rs`: the golden-vector table (one long list by design).
- `src/bin/fixtures.rs`: `protocol-fixtures`, which writes the golden vectors.

## Test and regenerate

```sh
cargo test -p rh-protocol                          # tests/vectors.rs fails if encoding and the committed vectors differ
cargo run -p rh-protocol --bin protocol-fixtures   # regenerate the vectors (only in a packet that changes the wire)
dotnet test client-unity/RoguehallaNet.Tests       # the C# decoder
node tools/protocol-js/check-vectors.mjs           # the JS decoder
```

## Rules

Local rules: [AGENTS.md](AGENTS.md). A change to the bytes needs the Rust, C# and JS decoders and the vectors updated in
one branch.
