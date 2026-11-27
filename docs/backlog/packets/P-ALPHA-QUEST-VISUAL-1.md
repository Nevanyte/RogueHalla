# P-ALPHA-QUEST-VISUAL-1

## ID and outcome
Phase 2 item 11. Make the 25 raid quests readable and navigable. Exercise their completion and reward outcomes in the authority, and review the delivered states in the actual Unity player. Review text sizing across the main game screens.

## Dependencies
The playable alpha at `948e0b063`, the Top 25 source rules, viewer-scoped wire collections, and the Unity Web test build.

## Relevant legacy paths
Legacy Top 25 rules in `packages/server` and UI in `packages/client`. Existing parity and loose outcome checks in `crates/rh-sim/src/quests/board/attempts/scenarios.rs` and `crates/rh-server/tests/top25_playthrough.rs`.

## New paths
An offline quest outcome capture, Unity visual verification harness and evidence under `docs/evidence/alpha-quest-visual`.

## Exclusive write scope
Top 25 authoritative quest and reward corrections with matched tests, quest presentation and map overlays, focused readable sizing on shared player UI, browser/Unity verification tools and scoped evidence. Keep unrelated economy, art, hosted routes and infrastructure intact.

## Contracts and references
C-TICK, C-INPUT, C-OBS, C-RNG and D9. Rust validates every completion, award and cast. Markers and callouts use public knowledge delivered to the viewer.

## Behaviour to preserve
Five known planning offers and ten distinct raid offers, quest interaction focus, active reward slots, explicit abandonment and text-size settings. Hidden objectives stay hidden.

## Acceptance checks
Exercise the authoritative intended result of each offered reward, then capture the corresponding browser state. Record unexpected outcomes, mechanics without a visual cue and any scripted starting conditions. At desktop and 720p, quest offers, journal, route guidance and common player screens have legible text, with long copy accessible and actions visible. Exercise enlarged text and genuine browser planning and raid entry.

## Validation commands
`cargo test --locked -p rh-sim --lib quests::board::`, `cargo test --locked -p rh-server --test top25_playthrough`, `dotnet test client-unity/RoguehallaNet.Tests`, the Unity EditMode suite and the full repository gate. Browser and all-quest receipt commands are recorded with the evidence.

## Risk and suggested model tier
Sol for the authority, Luna for focused map review. A correct wire replay exercises rendering independently from real gameplay input. Use production admission for a separate browser smoke.

## Known traps
A ready marker can differ from the initial giver. Some objectives have no fixed coordinates until accepted or while a moving target is unseen. A permanent reward can affect a later hit without producing an instant visual effect. Layout tests must retain cases with crowded docks and enlarged text.
