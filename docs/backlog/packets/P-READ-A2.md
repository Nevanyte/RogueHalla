# P-READ-A2: The front door: root README, docs index, and six component READMEs

- **Serves:** owner direction 2026-11-16, the readability sweep ("Yes, both"); standard `docs/agent/READABILITY.md`.
- **Tier:** `implementer-sonnet`. Reviewer: reviewer (Sonnet).
- **Write scope:** `README.md`; `docs/legacy/LEGACY_GAME_README.md` (new); `docs/README.md`, `client-unity/RoguehallaNet/README.md`, `client-unity/UnityProject/README.md`, `protocol/README.md`, `scripts/README.md`, `tools/README.md` (new); the files that link into an anchor of the root README (`git grep -n 'README.md#'`), link targets only..
- **Base:** `coord/int-56` @ `907ab240e`.

## What

1. **Root `README.md` becomes a front page** of at most about 150 lines: what RogueHalla is (a multiplayer arena roguelike moving from the legacy TypeScript game to a Rust server and a Unity Web client); a repository map (the `AGENTS.md` table, each row linking its README); "start here" links (`AGENTS.md`, `docs/MIGRATION.md`, `docs/README.md`, `docs/agent/READABILITY.md`); how to run the Rust server and the Unity client locally (from `CONTRIBUTING.md` and the crate/client READMEs; commands only, checked to exist); how to run the legacy game (one line, linking the moved text). Move every other section of today's README **verbatim** into `docs/legacy/LEGACY_GAME_README.md` under a one-paragraph note that it describes the legacy TypeScript game, and fix the two links into its anchors.
2. **`docs/README.md`**: an index of `docs/` (each folder and each top-level file, one line). Say plainly that `docs/evidence/` is an append-only archive of proof (read by tools, not meant for reading) and `docs/rework/` holds the Unity rework design notes. Point at the five sources of truth `AGENTS.md` names.
3. **Five more READMEs** in the standard's shape: `client-unity/RoguehallaNet` (the engine-free library; `sync-net.sh` copies it into Unity: run `bash client-unity/sync-net.sh --check` afterwards and make sure the README is not copied, or if the sync copies it, that `--check` stays green), `client-unity/UnityProject` (rendering, UI, audio, FX; no game rules), `protocol`, `scripts` (one line per script, the gate first), `tools` (group the many `*-fixtures` capture tools by family in a table; do not write one line per tool if there are dozens).

## How (all P-READ packets)

Follow `docs/agent/READABILITY.md` exactly: what to write, the move-only rules, frozen files, the comparator's
constants, and the proof. This packet changes no behaviour. Its oracle is `scripts/readability-allowlist.txt` with the
lines listed below removed (committed on `packet/P-READ-A2`); the gate's readability step is red until the work is done.
Never edit the allowlist (it is the oracle file) or add lines to it.

- Branch: `agent/sonnet/P-READ-A2` from `packet/P-READ-A2`; first command in your worktree:
  `git checkout -B agent/sonnet/P-READ-A2 packet/P-READ-A2`.
- Iterate with `scripts/gate.sh --quick --base coord/int-56`; finish with
  `scripts/gate.sh --quick --base coord/int-56 --oracle packet/P-READ-A2`. Prefix cargo and gate commands with
  `export PATH="$HOME/.cargo/bin:$PATH"`.
- A red parity, golden, hash, digest, pin or test-expectation step means a change altered behaviour: undo it. Never
  refresh a fixture, pin or expectation in a P-READ packet.
- Hand-off: the AGENTS.md report, plus the `readability: OK (...)` line, and for a split the output of
  `git diff --stat coord/int-56` and every changed line that is not a move (expected: `mod`, `use`, `pub use`, visibility,
  `#[cfg(test)] mod tests;`, comments).

## Allowlist lines this packet removes (its oracle)

```
readme client-unity/RoguehallaNet
readme client-unity/UnityProject
readme docs
readme protocol
readme scripts
readme tools
```
