# RoguehallaNet

The engine-free C# library behind the Unity client (`netstandard2.1`, C# 9). It holds everything the client does that is
not drawing: decoding and encoding the wire format, the client's copy of server state, clock sync, pathfinding, and the
mirrored walk prediction. It has no Unity types, no reflection and no threads, so it runs under plain `dotnet test`.

## Where it sits

- The Rust server speaks the wire format (`crates/rh-protocol`); this library decodes it byte for byte, checked against the
  shared golden vectors in `protocol/fixtures/`.
- `client-unity/UnityProject` calls this library and draws the result. Unity gets a **copy** of `Protocol/` and `Net/`
  under `UnityProject/Assets/RoguehallaNet/`, made by `client-unity/sync-net.sh`. Edit here, never in the copy.
- `RoguehallaNet.Tests` (NUnit) and `RoguehallaNet.Probe` (a console client over `ClientWebSocket`) sit beside it.

## Entry points

| File | What it does |
|---|---|
| `Protocol/Codec.cs` | Wire encode and decode; `ProtocolVersion` moves with `PROTOCOL_VERSION` in Rust. |
| `Protocol/Messages.cs`, `Protocol/State.cs` | The message and state shapes. |
| `Protocol/SnapshotTracker.cs` | Applies Full snapshots and deltas against the last one received. |
| `Protocol/MapBlob.cs` | Decodes the map the server sends. |
| `Net/Session.cs` | One connection's lifecycle: join, input, snapshots, resync. |
| `Net/LocalMotion.cs` | Local walk prediction, mirrored from Rust and always corrected by the server. |
| `Net/Pathfinder.cs` | Client-side click-to-walk pathfinding. |
| `Net/RenderClock.cs`, `Net/SnapshotBuffer.cs` | Interpolation timing for remote actors. |

## Test it

```sh
dotnet test client-unity/RoguehallaNet.Tests
bash client-unity/sync-net.sh          # after any change under Protocol/ or Net/
bash client-unity/sync-net.sh --check  # exit 1 if the Unity copy is out of date
```

`sync-net.sh` copies only `*.cs` files under `Protocol/` and `Net/`, so this README is not copied. `dotnet test` also
fails when the Unity copy is stale.

## Rules

[client-unity/AGENTS.md](../AGENTS.md): this library is the source of truth; walk prediction changes only together with
the Rust side and its vectors; the codec and `PROTOCOL_VERSION` change in the same branch. The longer API notes are in
[client-unity/README.md](../README.md).
