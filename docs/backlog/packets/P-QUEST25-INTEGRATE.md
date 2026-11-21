# P-QUEST25-INTEGRATE

## ID and outcome

Integrate the approved Top 25 quest board into the current Unity rework, including 40 Hz normal raids and 20 Hz compatibility.

## Dependencies

P-QUEST25-WIRE, P-QUEST25-PLAYTHROUGH, P-TICK-6, P-COLISEUM-HEALS-2 and the accepted card provenance chain on target commit `5567458fb`.

## Relevant legacy paths

`packages/server/src` supplies the legacy gameplay oracles. The Top 25 content and selection rules come from the owner-approved [quest design](../../quests.md).

## New paths

`protocol/fixtures/v23.json` and [integration evidence](../../evidence/quest25-integration-2026-11-21/README.md).

## Exclusive write scope

Integrate the existing quest and target changes in the sim, policy, headless and parity adapters, wire codec and probe, room tests, Unity client and mirrored net library, browser proof, determinism comparator and its schema tests, and their packet and checkpoint records. Regenerate the protocol, archetype and native ability identity fixtures and the three dependent card provenance corpora with their official tools.

## Contracts and references

C-TICK, C-RNG, C-INPUT, C-OBS, C-NET, ADR 0008, ADR 0029, and DEV-040.

## Behaviour to preserve

Ten offers per raid, two per zone. Completion, rewards and reconnection are per player. Existing heal pickups and walking hints remain representable. Quest durations use elapsed simulation time at both supported rates.

## Acceptance checks

Decode shared vectors in Rust, C# and JS. Exercise two independently completing and reconnecting seats at 20 and 40 Hz, replay all 25 quest completions, verify the capture chain, run the repository gate and the GitHub pull request checks.

The determinism comparator must accept an omitted or Boolean `top25_quests` in a replay config, reject invalid types and unknown fields, and compare the complete replay across native architectures.

## Validation commands

`cargo run --locked -p rh-protocol --bin protocol-fixtures`, `cargo run --locked -p rh-parity --bin hitbox-fixtures`, `cargo test --locked -p rh-sim --lib record_ability_hash_tape -- --ignored`, `cargo test --locked -p rh-server --test top25_playthrough`, `bash docs/evidence/quest25-deep-playtest-2026-11-19/replay.sh`, `node tools/provenance/check-card-chain.mjs`, and `scripts/gate.sh --base 5567458fb --coordinator --regen-fixtures --allow-artefacts`. The evidence records the three Docker capture commands.

## Risk and suggested model tier

High. Both branches allocated wire and save identifiers and added gameplay timers.

## Known traps

The established Heal tag is 8. The quest Rebirth Egg uses tag 9. Target fixtures v19–v21 carry historical data that the quest branch had independently versioned. Protocol 22 is allocated on the separate augment branch.
