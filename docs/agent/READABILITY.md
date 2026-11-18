# Readability standard

Owner direction 2026-11-16: the project in its current state should be as human readable as possible. This file says
what that means here, how a file is reorganised without changing what the program does, and how that is proven. It
governs the readability packets (`P-READ-*`) and, through the gate's readability step, every later change.

A reader new to the repository should be able to go **root README → component README → module header → code** and
know at each step where to look next.

## The four rules (enforced)

`python3 scripts/readability_check.py` runs in every gate (quick and full). It checks tracked files only.

| Rule | Applies to | Requirement |
|---|---|---|
| `readme` | each `crates/*/`, and `client-unity/RoguehallaNet`, `client-unity/UnityProject`, `hosting`, `tools`, `scripts`, `parity`, `protocol`, `docs` | a `README.md` |
| `header` | every non-test `crates/*/src/**/*.rs` | the first non-blank line starts with `//!` |
| `header` | every non-test `client-unity/**/*.cs` (not the synced copy in `UnityProject/Assets/RoguehallaNet/`) | a `/// <summary>` before the first type declaration |
| `size` | the same files | Rust at most **1,500** lines, C# at most **800** (whole file: inline tests count) |

Test files are `tests/` directories, files named `tests.rs` or `*_tests.rs`, and C# under a `*Tests/` or `*.Tests/`
directory.

**The allowlist** `scripts/readability-allowlist.txt` records what does not meet a rule yet, and only shrinks:

```
size    <path> <max-lines>   # over the limit today; may not grow above <max-lines>
header  <path>               # no header yet
readme  <dir>                # no README yet
frozen  <path> <reason>      # another tool pins this path: never split, rename or move it (a header is fine)
exempt  <glob> <reason>      # out of the size rule on purpose (headers still apply)
```

An entry that is no longer needed fails the check ("remove this entry"), so finished work cannot leave the list stale.
A new file that breaks a rule fails the check: new code is born readable. Only the coordinator adds `frozen` or `exempt`
lines; anyone removes `size`, `header` and `readme` lines by fixing what they name.

## What to write

**README of a component** (at most about 80 lines): one paragraph on what it is for; where it sits (what calls it,
what it calls); its entry points, three to eight files with one line each; how to test it (the exact command); its own
rules, linking its `AGENTS.md` if it has one. No status, history, packet IDs or measurements: those live in
`docs/MIGRATION.md` and `docs/backlog/`.

**Rust module header** (`//!`, one to five lines): what this module is for and who uses it. If it ports legacy code,
name the source (`packages/server/src/...`). If it holds a rule that matters for determinism (draw order, tick order),
say so in one line. Plain words, present tense. A file pulled in with `include!("...")` is a fragment, not a module:
rustc rejects `//!` there, so its header is a plain `//` comment saying which file includes it and why.

**C# summary**: one or two sentences on the type's job, and for a MonoBehaviour what it draws or listens to.

## Reorganising a file without changing behaviour

A readability packet moves code; it never changes it. The only edits allowed besides moves are `mod`, `use` and
`pub use` lines, visibility (`pub(super)`, `pub(crate)`), `//!`/`///` comments, and `#[cfg(test)] mod tests;`.

1. **Split by what a reader looks for.** One concept per file, named for it (`room/admission.rs`, `room/tick.rs`),
   not `part2.rs` or `helpers.rs`. Aim for 200 to 600 lines per new file, never over 800. The module root reads as a
   table of contents: header, `mod` lines, re-exports, then the main type.
2. **Keep every path callers use.** The old module re-exports what moved (`pub use self::admission::Admission;`), so
   nothing outside the file's own directory changes. Follow the crate's existing layout (`foo.rs` plus `foo/child.rs`,
   or `foo/mod.rs`, whichever the crate already uses).
3. **Inline tests move first.** `#[cfg(test)] mod tests { … }` becomes `#[cfg(test)] mod tests;` with the body in
   `foo/tests.rs`. The name `tests.rs` keeps the gate's oracle patterns working. Often this alone brings a file under
   the limit.
4. **Never reorder** fields of a struct or variants of an enum (serde, wire and state-hash order), statements inside a
   function, arms of a `match`, entries of an array or table, or a `#[derive]` list. Moving whole items between files is
   safe in Rust; `macro_rules!` is the exception (it must stay defined before its first use).
5. **Frozen files stay where they are.** Files listed `frozen` are named by mutation runners, patches, path-scanning
   tests or the gate. In `crates/rh-sim/src/{replay,checkpoint,observe,content,statics,arenas,constants}.rs` and
   `crates/rh-policy/src/lib.rs`, the constants and enums `tools/determinism/compare.py` reads by regex stay in that
   file even when the rest of it is split.
6. **C#**: split a large class with `partial class` files named `Type.Concern.cs` (the pattern `MatchHud.*.cs` already
   uses). Edit `client-unity/RoguehallaNet/`, then run `bash client-unity/sync-net.sh`; never edit the synced copy.
   Unity creates `.meta` files for new assets when the Editor imports them; commit them.

## Proof

- `scripts/gate.sh --quick --oracle packet/<ID>` is green, which includes the readability step with the packet's
  allowlist lines removed.
- No fixture, golden vector, parity capture, pin or test expectation changed (the gate's oracle check enforces it).
  A red parity, hash, golden or digest test means a move changed behaviour: undo it, do not refresh anything.
- `git diff --color-moved=zebra --color-moved-ws=allow-indentation-change <base>` shows the change as moved blocks.
  The hand-off lists every line that is not a move (it should only be `mod`, `use`, visibility and comment lines).
- Reviewer: `reviewer` (Sonnet); `reviewer-opus` for anything in `rh-sim` or `rh-protocol`.

## Out of scope for this sweep

- `crates/rh-policy/src/bots/temporal_percept/**`, the dormant legacy Brain (not activated; ported line by line).
- `crates/rh-protocol/src/samples.rs`: the golden-vector table, one long list by design.
- `docs/evidence/` and `docs/rework/` stay where they are (70 code and tool files and over 800 documents link into
  them); `docs/README.md` indexes them instead.
- `packages/` (legacy, read-only), `parity/` and `protocol/fixtures/` (generated).
