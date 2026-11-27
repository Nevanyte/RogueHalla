# P-ALPHA-RAID-POLICY-1: select RaidBot for the eight-seat alpha

- **Serves:** ORDER_OF_OPERATIONS M-full-loop browser match with eight bot-filled seats.
- **Implementer tier:** implementer-opus
- **Reviewer:** reviewer-opus
- **Integration base:** `4d8bf090e`
- **D-rules touched:** D1 loose legacy reference; D2 real-room test verdict; D5 full loop; D6/D9 unchanged.

## 1. ID and outcome
The alpha may opt in to RaidBot for fill seats with `--bot-brain raid`. The local operator wrapper applies the selected eight-seat gameplay settings to each origin's existing server command while preserving its private options and environment. This packet does not deploy or restart a service.

## 2. Dependencies
P-ALPHA-BOT-SEATS-1, the existing RaidBot policy, and a release-mode real-room qualification.

## 3. Relevant legacy paths
`packages/server/src/bots/BotManager.ts` seats bot policies through the normal room input path and targets eight by default. RaidBot is a new test policy; its evaluation is through native room evidence, not exact legacy identity.

## 4. New paths
`hosting/open-alpha/alpha-gameplay.py`, `hosting/open-alpha/test_alpha_gameplay.py`, and this packet.

## 5. Exclusive write scope
`crates/rh-server/src/bot_fill.rs`, `crates/rh-server/src/room/bots.rs`, `crates/rh-server/src/room/tests/bot_fill.rs`, `hosting/open-alpha/README.md`, the two wrapper files, this packet, and the P-ALPHA-BOT-SEATS-1 brief. Server code is owned by the coordinator in the shared worktree.

## 6. Contracts and references
C-INPUT/C-OBS: RaidBot gets only its seat's accepted frames and submits through normal validation. `crates/rh-headless/src/lib.rs` carries the existing RaidBot factory; `crates/rh-server/src/bot_fill.rs` parses the opt-in selector.

## 7. Behaviour to preserve
HumanBot remains the default fill policy and fault fallback. Keep the existing binary, bind addresses, human minimum, room count, database, origin, capture, control and timing settings for each service.

## 8. Acceptance checks
Release-mode `alpha_raid_bot_plays_and_survives_on_real_fill_seats` uses a Source Arena room with seven bot seats and one human, full publication and input budgets, movement, mob kills and starter survival over the pre-coliseum window. The wrapper replaces only the two gameplay values and adds the two map flags, rejecting unsafe or ambiguous commands. Operator validation compares the effective command and room behavior after any future launch.

## 9. Validation commands
`cargo test --release -p rh-server --lib alpha_raid_bot_plays_and_survives_on_real_fill_seats --locked -- --ignored --nocapture`; `python3 -m unittest hosting/open-alpha/test_alpha_gameplay.py -v`; `scripts/gate.sh --quick` after integration.

## 10. Risk and suggested model tier
RaidBot was previously measured chiefly in human seats for quests; real bot-seat qualification is necessary. The room test is bounded to 150 seconds and does not establish late match outcomes or hosted capacity. Implementer-opus, reviewer-opus.

## 11. Known traps
The Nov 26 live and stage units used `--fill-bots 4`; editing a README alone does not change their launch. The wrapper must be installed with the reviewed binary and prefixed to each origin's complete existing command. Do not copy staging credentials or origin arguments into the repository.
