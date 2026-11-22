# rh-policy: the policy contract and the bots

The seat contract: how a scripted bot, a fill bot or (later) a learned policy drives one player. A policy sees only its
own filtered observation and proposes commands; those go through the same `validate` + `Sim::submit` path as a
human's, so no policy gets extra actions. Each policy declares its decision cadence in ticks, identical live and
headless. A policy that panics, proposes garbage or misses its deadline is replaced by its fallback and the fault is
reported, so a match never stalls. No model runtime lives here.

## Where it sits

It calls `rh-sim` and `rh-protocol`. `rh-server` seats it in live rooms (fill bots) and `rh-headless` seats it in
bot-vs-bot runs.

## Entry points

- `src/lib.rs`: the `Policy` trait, seats, and `drive_seats` (with `drive_seats_with` and `drive_seats_queued`
  variants). The constants and enums that `tools/determinism/compare.py` reads stay in this file.
- `src/scripted.rs`: simple scripted policies such as `Brawler`.
- `src/bots/bot.rs`: `HumanBot`, the live fill bot (a partial port of the legacy bot in `packages/server/src/bots/`).
  `src/bots/mod.rs` lists its modules (reflex, fight, kite, loot, planning, perception, humanizer, and more).
- `src/bots/raid/`: `RaidBot`, the balance lab's test player (one arbiter in camp-equivalents per minute; one file per
  goal kind under `exec/` and `quest/`).
- `src/bots/brain.rs` and `src/bots/brain_bot.rs`: the ported legacy Brain, not wired to live bots.
- `src/delivery/`: the shared public projection that delivers observations to a policy.
- `data/brain_tuning.json`: the Brain's tier and persona numbers.

## Test

```sh
cargo test -p rh-policy
```

The parity tests inside `src/bots/` (`*_parity_tests.rs`) compare against source captures in `parity/policy/`.

## Rules

Local rules: [AGENTS.md](AGENTS.md). `clippy.toml` bans hash-ordered collections, the clock (one exception: the seat's
decision budget) and platform math. The dormant `src/bots/temporal_percept/` is ported line by line; leave its layout alone.
