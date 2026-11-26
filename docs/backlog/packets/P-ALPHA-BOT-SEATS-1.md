# P-ALPHA-BOT-SEATS-1: eight-seat alpha launch recipe

- **Serves:** ORDER_OF_OPERATIONS M-full-loop browser match with eight seats and bot fill.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `dbaa701be`
- **D-rules touched:** D1/D2 n/a (no game rules); D5 bot fill is in the full loop; D6/D9 none.

## 1. ID and outcome
The open-alpha launch recipe preserves the reviewed human minimum and fills a Source Arena lobby to eight participants. Pooled maps include signature landmarks. This changes the recipe only; an operator must build and launch a reviewed release separately.

## 2. Dependencies
The existing bot fill manager, runtime flags and map pool.

## 3. Relevant legacy paths
`packages/server/src/bots/BotManager.ts` caps fill at `MAX_PLAYERS`; `packages/server/src/bots/brain/types.ts` sets `BOT_FILL_DEFAULTS.fillTo` to eight.

## 4. New paths
This packet brief only.

## 5. Exclusive write scope
`hosting/open-alpha/README.md` and this packet.

## 6. Contracts and references
`AGENTS.md` C-INPUT/C-OBS; `crates/rh-server/src/bot_fill.rs` closing top-up; `crates/rh-server/src/runtime.rs` parses `--fill-bots` and `--signature-landmarks`.

## 7. Behaviour to preserve
One room, loopback Source/control listeners, PostgreSQL and bot learning off. This packet makes no change to live policy type: the default fill policy is HumanBot.

## 8. Acceptance checks
The recipe names `--fill-bots 8` and `--map-pool --signature-landmarks`, retains the reviewed human minimum, and the existing room test proves the manager seats seven bots with one human. The operator checks the actual process argv and observes an eight-participant lobby before calling the launch verified.

## 9. Validation commands
`cargo test -p rh-server --lib the_fill_policy_schedules_arrivals_over_the_countdown --locked`; review the recipe command against `crates/rh-server/src/runtime.rs` and the release receipt.

## 10. Risk and suggested model tier
The recipe is inert until an operator deploys it. One room of eight costs more per match than four; measure capacity before launch. Implementer-sonnet.

## 11. Known traps
The Nov 26 production receipt uses `--min-players 2 --fill-bots 4`; `/info` omits these flags and does not prove lobby count. `RaidBot` is a headless balance-lab policy and is not activated by `--bot-brain full` (which selects BrainBot).
