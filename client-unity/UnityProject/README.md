# UnityProject

The Unity 6 (6000.0.85f2) Web client: rendering, UI, audio and effects. It holds **no game rules**. Every outcome (hits,
damage, rewards, deaths, results) is drawn from what the server reports; click-time feedback is presentation only.

## Where it sits

It talks to `crates/rh-server` through the engine-free library in [`../RoguehallaNet`](../RoguehallaNet/README.md), copied
into `Assets/RoguehallaNet/` by `client-unity/sync-net.sh` (never edit that copy). The client and server must agree on
the protocol version and content hash, or the server refuses the join.

## Where things are

| Path | What is there |
|---|---|
| `Assets/Scripts/Net/` | `MatchClient` and the WebSocket and native transports. |
| `Assets/Scripts/UI/` | The HUD (`MatchHud.*.cs`), launch screen, hub, drawers and inspector views. |
| `Assets/Scripts/View/` | The world views: actors, map, effects. |
| `Assets/Scripts/Presentation/` | Pure presentation helpers (outlines, shadows, camp labels, FX models). |
| `Assets/Scripts/BakedContent.cs` | The server content hash, written by `Tools/build-web.sh`. A build artefact: do not commit changes. |
| `Assets/Editor/` | Editor menus and importers, including `RhBuild` (the Web build). |
| `Assets/Tests/` | Unity EditMode tests. |
| `Tools/` | Build, proof and look-check scripts (`build-web.sh`, `editmode.sh`, `playable-web.cjs`). |

## Build and run

1. Start the server with the embedded small map: `cargo run -p rh-server -- --first-playable` (the server flags are listed at the top of `crates/rh-server/src/main.rs`).
2. `client-unity/UnityProject/Tools/build-web.sh` bakes the server's content hash from `/info` and builds to `Build/Web`.
3. Serve `Build/Web` and open it with `?server=ws%3A%2F%2F127.0.0.1%3A7777%2Fws`.

Close the Unity Editor before a batch build. Font assets are prepared once with
`Roguehalla.Editor.RhBuild.PrepareUiResources`. Further notes: [client-unity/README.md](../README.md).

## Test it

```sh
client-unity/UnityProject/Tools/editmode.sh                  # Unity EditMode suite (exit 3: no Editor available)
node client-unity/UnityProject/Tools/playable-web.cjs        # browser proof; needs the matching server and Web build
```

## Rules

[client-unity/AGENTS.md](../AGENTS.md). Third-party assets need provenance and licence recorded in
[`../THIRD_PARTY_ASSETS.md`](../THIRD_PARTY_ASSETS.md) before they are added.
