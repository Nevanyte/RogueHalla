# P-HOST-BOT-BRAIN-1: the beta host can seat the ported Brain as its fill bots

- **Serves:** owner decision 8 (MIGRATION, 2026-11-10: wire the ported Brain to live bots); DEV-027's condition for
  `--bot-brain full` going live was met by P-BOT-BRAIN-THEMED-1/-2
- **Implementer tier:** implementer-sonnet (`hosting/`: `hosting/AGENTS.md`)
- **Reviewer:** reviewer
- **Integration base:** feature/unity-rework
- **D-rules touched:** none (launch configuration; no game, wire or parity change)
- **Protected paths:** `hosting/` (merged only on the owner's yes). Nothing deploys; no `aws` command.

## 1. ID and outcome
Measured on the release commit with the host's flags (`--min-players 1 --fill-bots 8`): with HumanBot fill bots the seven
bots die inside a minute and matches end at 41 to 112 s; with `--bot-brain full` no match ended within 600 s in 12
runs and one room held its tick at 3 to 6 percent of a core. The server flag exists (`crates/rh-server/src/main.rs:1093`,
`bot_fill.rs` `bot_brain_flag`); the launcher cannot pass it.

When this lands, the config takes an optional `bot_brain` (`"off"` default, or `"full"`). `"full"` adds `--bot-brain full`
(two arguments) to the Source unit only; `"off"` or absent renders byte-identical units. Any other value or a non-string
is refused by config validation. The Brain falls back to HumanBot on a fault. `rh-server` does not refuse the flag on the
training runtime (it parses it for every runtime), but training runs `--fill-bots 0`, so it never receives it.

## 2. Dependencies
none.

## 5. Exclusive write scope
- `hosting/technical-demo/{bootstrap.py,README.md,test_offline.py}` (`config.example.json` only if the other optional
  keys are listed there: they are, so it gains the key)
- No Rust, no `stack.yaml`.

## 8. Acceptance checks
- Offline tests: default and `"off"` renders equal the goldens; `"full"` adds exactly ` --bot-brain full` on Source and
  nothing on training; `"on"`, `true`, `1`, `""` refused; the rendered user data embeds the key.
- `scripts/gate.sh --quick --allow-protected` passes.

## Oracle files
Tooling with no oracle author: the first `test:` commit is the failing offline tests.

## 10. Risk and suggested model tier
Sonnet. Risk: a silently different default launch; pinned by the golden.
