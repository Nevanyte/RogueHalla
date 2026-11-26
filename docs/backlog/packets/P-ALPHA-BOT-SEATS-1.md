# P-ALPHA-BOT-SEATS-1: eight-seat alpha launch recipe

- **Serves:** ORDER_OF_OPERATIONS M-full-loop browser match with eight seats and bot fill.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `dbaa701be`
- **D-rules touched:** D1/D2 n/a (no game rules); D5 bot fill is in the full loop; D6/D9 none.

## 1. ID and outcome
The open-alpha launch recipe preserves the reviewed human minimum and fills a Source Arena lobby to eight participants. Pooled maps include signature landmarks. The first local revision selected the opt-in full Brain; P-ALPHA-RAID-POLICY-1 supersedes that policy choice with RaidBot after real-room qualification. An operator must build and launch a reviewed release separately.

## 2. Dependencies
The existing bot fill manager, runtime flags and map pool.

## 3. Relevant legacy paths
`packages/server/src/bots/BotManager.ts` caps fill at `MAX_PLAYERS`; `packages/server/src/bots/brain/types.ts` sets `BOT_FILL_DEFAULTS.fillTo` to eight.

## 4. New paths
This packet brief only.

## 5. Exclusive write scope
`hosting/open-alpha/README.md`, `crates/rh-headless/tests/brain_bots.rs` and this packet.

## 6. Contracts and references
`AGENTS.md` C-INPUT/C-OBS; `crates/rh-server/src/bot_fill.rs` closing top-up; `crates/rh-server/src/runtime.rs` parses `--fill-bots` and `--signature-landmarks`.

## 7. Behaviour to preserve
One room, loopback Source/control listeners, PostgreSQL and bot learning off. The server's default fill policy remains HumanBot. The matched-seed Brain comparison is evidence about an earlier candidate, not the final alpha selection.

## 8. Acceptance checks
The alpha launch wrapper selects `--fill-bots 8` and `--map-pool --signature-landmarks`, retains the reviewed human minimum, and the existing room test proves the manager seats seven bots with one human. A matched-seed full-map test compared HumanBot with full Brain as the prior policy alternative at the live 25 ms step and legacy 50 ms step. P-ALPHA-RAID-POLICY-1 qualifies the final RaidBot choice. The operator checks the actual process argv and observes an eight-participant lobby before calling the launch verified.

## 9. Validation commands
`cargo test -p rh-server --lib the_fill_policy_schedules_arrivals_over_the_countdown --locked`; `cargo test --release -p rh-headless --test brain_bots early_survival_on_live_bot_seats --locked -- --ignored --nocapture`; review the recipe command against `crates/rh-server/src/runtime.rs` and the release receipt.

## 10. Risk and suggested model tier
The recipe is inert until an operator deploys it. One room of eight costs more per match than four; the selected policy's room CPU and replay must be checked on the release build before launch. Implementer-sonnet.

## 11. Known traps
The Nov 26 production receipt uses `--min-players 2 --fill-bots 4`; `/info` omits these flags and does not prove lobby count. `--bot-brain full` selects BrainBot, not RaidBot. The Brain early-survival comparison is a bot-seat test, not evidence for final match outcome or a hosted eight-seat capacity verdict.
