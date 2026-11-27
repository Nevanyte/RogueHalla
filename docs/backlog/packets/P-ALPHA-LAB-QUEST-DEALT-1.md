# P-ALPHA-LAB-QUEST-DEALT-1: count each delivered offer once per seat

- **Serves:** ORDER_OF_OPERATIONS M-full-loop quest measurement; balance-lab accounting after P-ALPHA-QUEST-RESTORE-1.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `ca207f3af`
- **D-rules touched:** D1/D2 n/a (observer only); D5 full-loop evidence; D6/D9 none.

## 1. ID and outcome
The balance lab counts a quest as dealt once when a viewing seat first receives its row, including the five offers added at raid start after the five planning previews. It preserves seat ownership and the production observer's stable handle bindings.

## 2. Dependencies
P-ALPHA-QUEST-RESTORE-1's five planning previews and ten raid offers, and the existing balance-lab attempt probe.

## 3. Relevant legacy paths
The lab is native tooling; `packages/server/src/bots/BotManager.ts` is not its source. `crates/rh-sim/src/quest_view/board.rs` publishes five planning previews and all ten raid offers under the new live config.

## 4. New paths
This packet brief.

## 5. Exclusive write scope
`crates/rh-headless/src/balance_lab/observer.rs`, `crates/rh-headless/tests/balance_lab_attempts.rs`, this packet.

## 6. Contracts and references
C-OBS: the lab reads per-seat observations. `rh_sim::ItemObserver` is the production per-viewer binding that keeps quest handles unique across publication changes. `Sim::quest_attempts` remains the authority for acceptance/completion counts.

## 7. Behaviour to preserve
The existing attempt partition and accepted ≤ dealt checks remain. No sim, policy, quest rules, serialization, protocol or content changes. The lab still skips observation on a terminated final step.

## 8. Acceptance checks
`balance_lab_attempts` passes with the original assertions and a check that planning-to-raid publication adds ten distinct offers per viewing seat. Jobs 1 and 4 reports remain byte-identical. No duplicate deal is counted on later samples.

## 9. Validation commands
`cargo test --locked --release -p rh-headless --test balance_lab_attempts`; `cargo test --locked --release -p rh-headless --test balance_lab`; `cargo clippy --locked -p rh-headless --all-targets -- -D warnings`.

## 10. Risk and suggested model tier
Persistent observation changes handle identity for all sampled item rows. The balance-lab report guards and deterministic multi-job comparison must stay green. Implementer-sonnet, reviewer.

## 11. Known traps
A fresh `rh_sim::observe` creates a disposable `ItemObserver` every tick. Handles 1–5 assigned in planning can be reused for different offers after raid publication, so deduplication by handle alone undercounts. Quest definitions happen to be unique in the current Top 25 deal, but that is not the seat's delivered offer identity.
