# P-READ-A5: C# summaries in the net library

- **Serves:** owner direction 2026-11-16, the readability sweep ("Yes, both"); standard `docs/agent/READABILITY.md`.
- **Tier:** `implementer-sonnet`. Reviewer: reviewer (Sonnet); comments only.
- **Write scope:** the files named below under `client-unity/RoguehallaNet*/`, `///` comment lines only; the synced copy under `client-unity/UnityProject/Assets/RoguehallaNet/` only through `bash client-unity/sync-net.sh`.
- **Base:** `coord/int-56` @ `907ab240e`.

## What

As P-READ-A4, for the library. Edit `client-unity/RoguehallaNet/`, then run `bash client-unity/sync-net.sh` so the Unity copy follows; `sync-net.sh --check` must pass.

## How (all P-READ packets)

Follow `docs/agent/READABILITY.md` exactly: what to write, the move-only rules, frozen files, the comparator's
constants, and the proof. This packet changes no behaviour. Its oracle is `scripts/readability-allowlist.txt` with the
lines listed below removed (committed on `packet/P-READ-A5`); the gate's readability step is red until the work is done.
Never edit the allowlist (it is the oracle file) or add lines to it.

- Branch: `agent/sonnet/P-READ-A5` from `packet/P-READ-A5`; first command in your worktree:
  `git checkout -B agent/sonnet/P-READ-A5 packet/P-READ-A5`.
- Iterate with `scripts/gate.sh --quick --base coord/int-56`; finish with
  `scripts/gate.sh --quick --base coord/int-56 --oracle packet/P-READ-A5`. Prefix cargo and gate commands with
  `export PATH="$HOME/.cargo/bin:$PATH"`.
- A red parity, golden, hash, digest, pin or test-expectation step means a change altered behaviour: undo it. Never
  refresh a fixture, pin or expectation in a P-READ packet.
- Hand-off: the AGENTS.md report, plus the `readability: OK (...)` line, and for a split the output of
  `git diff --stat coord/int-56` and every changed line that is not a move (expected: `mod`, `use`, `pub use`, visibility,
  `#[cfg(test)] mod tests;`, comments).

## Allowlist lines this packet removes (its oracle)

```
header client-unity/RoguehallaNet.Probe/Program.cs
header client-unity/RoguehallaNet/Net/AccountApiService.cs
header client-unity/RoguehallaNet/Net/AccountConnectionPolicy.cs
header client-unity/RoguehallaNet/Net/AccountFullProfile.cs
header client-unity/RoguehallaNet/Net/AccountHubFlow.cs
header client-unity/RoguehallaNet/Net/AccountSettlement.cs
header client-unity/RoguehallaNet/Net/Collections.cs
header client-unity/RoguehallaNet/Net/FootTracker.cs
header client-unity/RoguehallaNet/Net/MatchPhase.cs
header client-unity/RoguehallaNet/Net/OwnedSeatCheckpoint.cs
header client-unity/RoguehallaNet/Net/OwnedSeatCheckpointCodec.cs
header client-unity/RoguehallaNet/Net/Ui/LobbyActivity.cs
header client-unity/RoguehallaNet/Net/Ui/MinimapGesture.cs
header client-unity/RoguehallaNet/Net/Ui/OverheadStack.cs
header client-unity/RoguehallaNet/Net/Ui/TutorialFlow.cs
header client-unity/RoguehallaNet/Net/Ui/UiInputGate.cs
header client-unity/RoguehallaNet/Net/Ui/WeaponRange.cs
```
