# P-BOT-BRAIN-FAULT-LOG-1: a Brain fill seat that falls back to HumanBot says so

- **Serves:** the beta host's `--bot-brain full` (P-BOT-BRAIN-LIVE-1): operators must be able to tell whether the bots
  players meet are the Brain or its fallback. Not a plan phase item; owner-requested deploy readiness.
- **Implementer tier:** implementer-sonnet. **Write scope:** `crates/rh-server/src/{room.rs,metrics.rs}` and this file.
- **D-rules:** D1 n/a (logging only); no sim, tick, wire or determinism change; D5/D6/D9 none.

## 1. Outcome
- First fault of a Brain-driven fill seat in a match logs ONE line:
  `[bots] match=<hex id> seat=<n> brain fell back to the fallback bot: <reason>` (reason is the fault kind). At most one per
  seat per match, never per tick. `seat` is the fill bot's number in the room.
- With `--bot-brain full`, when the match goes Active, ONE line: `[bots] match=<hex id> fill bots: <k> brain`. Flag off: no
  new line.
- `/control/health` metrics gain `brainFallbacks` (process counter of fallen-back seats).
- The fallback decision, bot commands, determinism, wire and tick are untouched.

## 2. Oracle
Room tests in `room.rs` force the Brain seat fault through the existing core-runtime seam (the Brain finds no source
arena and refuses) and read the room's captured log lines: exactly one fallback line per seat after many more ticks, the
start line present with the flag on and absent with it off, and the seat falls back as before. The `eprintln!` itself is
read, not tested; the line formatting and the once-only bookkeeping are.
