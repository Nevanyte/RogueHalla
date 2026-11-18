# P-READ-B11: Split three Unity views

- **Serves:** owner direction 2026-11-16, the readability sweep ("Yes, both"); standard `docs/agent/READABILITY.md`.
- **Tier:** `implementer-sonnet`. Reviewer: reviewer (Sonnet).
- **Write scope:** the files named below, the new child files beside them, and their parent `mod` declarations; nothing outside their directories.
- **Base:** `coord/int-56` @ `a8d46a9f5`.

## What

Split each file named below by the standard's move-only rules, so each is under the limit (1,500 lines Rust, 800 C#) and every new file is under 800. Move inline tests out first. Split along the seams a reader looks for and say in the hand-off why you chose them. `partial class` files named `Type.Concern.cs`; serialized fields stay in the file that declares the type (Unity serialization). Commit the `.meta` files Unity creates.

## How (all P-READ packets)

Follow `docs/agent/READABILITY.md` exactly: what to write, the move-only rules, frozen files, the comparator's
constants, and the proof. This packet changes no behaviour. Its oracle is `scripts/readability-allowlist.txt` with the
lines listed below removed (committed on `packet/P-READ-B11`); the gate's readability step is red until the work is done.
Never edit the allowlist (it is the oracle file) or add lines to it.

- Branch: `agent/sonnet/P-READ-B11` from `packet/P-READ-B11`; first command in your worktree:
  `git checkout -B agent/sonnet/P-READ-B11 packet/P-READ-B11`.
- Iterate with `scripts/gate.sh --quick --base coord/int-56`; finish with
  `scripts/gate.sh --quick --base coord/int-56 --oracle packet/P-READ-B11`. Prefix cargo and gate commands with
  `export PATH="$HOME/.cargo/bin:$PATH"`.
- A red parity, golden, hash, digest, pin or test-expectation step means a change altered behaviour: undo it. Never
  refresh a fixture, pin or expectation in a P-READ packet.
- Hand-off: the AGENTS.md report, plus the `readability: OK (...)` line, and for a split the output of
  `git diff --stat coord/int-56` and every changed line that is not a move (expected: `mod`, `use`, `pub use`, visibility,
  `#[cfg(test)] mod tests;`, comments).

## Allowlist lines this packet removes (its oracle)

```
size client-unity/UnityProject/Assets/Scripts/Presentation/WishFxPresenter.cs 895
size client-unity/UnityProject/Assets/Scripts/UI/HubCosmeticsView.cs 805
size client-unity/UnityProject/Assets/Scripts/View/ArenaView.cs 899
```
