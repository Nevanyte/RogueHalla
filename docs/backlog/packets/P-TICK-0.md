# P-TICK-0: record the 40 Hz decision (ADR 0029, C-TICK amendment, DEV-039)

- **Serves:** not in the plan: owner approval 2026-11-19 ("lets do 40"); supports ORDER_OF_OPERATIONS phase 3, item 16a
  (the D9 play loop's responsiveness)
- **Implementer tier:** coordinator (docs only)
- **Reviewer:** owner (accepts the ADR, signs DEV-039)
- **Integration base:** feature/unity-rework
- **D-rules touched:** D1 parity stays byte-exact at 50 ms · D2 n/a · D5 not new design (owner-approved responsiveness
  fix) · D9 unchanged · D10 superseded for this change by the owner's 2026-11-19 direction

## Plan for the P-TICK set

| ID | Title | Area | Tier | Reviewer | Depends on | Parallel group |
|---|---|---|---|---|---|---|
| P-TICK-0 | Record the decision | docs | coordinator | owner | none | A |
| P-TICK-1a | Per-sim `tick_ms` in the sim core, save, restore, hash | `rh-sim` core | implementer-opus | reviewer-opus | P-TICK-0 accepted | B |
| P-TICK-1b | Every `rh-sim` system reads the sim's `tick_ms`; tick-counted rules in ms | `rh-sim` systems | implementer-opus | reviewer-opus | P-TICK-1a | C |
| P-TICK-2 | Bots and headless read the rate | `rh-policy`, `rh-headless` | implementer-sonnet | reviewer-opus | P-TICK-1b | D |
| P-TICK-3 | Protocol 19: snapshot time from the Welcome rate | wire (Rust, C#, JS) | implementer-opus | reviewer-opus | P-TICK-1b | D |
| P-TICK-4 | Unity library reads the rate; vectors at both rates | `client-unity/RoguehallaNet` | implementer-sonnet | reviewer | P-TICK-3 | E |
| P-TICK-5 | Server: room rate setting, pacer, rewind window | `rh-server` (outside `wire`) | implementer-sonnet | reviewer-opus | P-TICK-3 | E |
| P-TICK-6 | Measure at 25 ms, then flip the live default | config, evidence | coordinator | reviewer-opus (merge) | P-TICK-2, 4, 5 | F |

Groups run in order A to F; packets inside one group have disjoint write scopes. P-TICK-3 touches the protocol and
decoders and merges alone (shared-file rule). Nothing changes for players before P-TICK-6.

## 1. ID and outcome
The owner chose a 40 Hz live tick. This packet records it: ADR 0029 (Proposed, then Accepted by the owner), the C-TICK
row in `docs/CONTRACTS.md` amended to the text in the ADR, and a `docs/DEVIATIONS.md` row DEV-039 the owner signs.

## 2. Dependencies
- none

## 3. Relevant legacy paths
- `packages/shared/src/constants.ts:11-13`: `TICK_RATE = 20`, `TICK_MS = 1000 / TICK_RATE`, `PATCH_MS = TICK_MS`

## 4. New paths
- `docs/adr/0029-forty-hertz-tick.md`

## 5. Exclusive write scope
- `docs/adr/README.md` (index row), `docs/DEVIATIONS.md` (proposed row), `docs/backlog/README.md` (P-TICK rows),
  `docs/backlog/packets/P-TICK-*.md`; after acceptance, the coordinator edits the C-TICK row of `docs/CONTRACTS.md`

## 6. Contracts and references
- C-TICK, C-PARITY, C-DET; ADR 0007 (determinism), ADR 0008 (Unity client)

## 7. Behaviour to preserve
- No code changes.

## 8. Acceptance checks
- The owner marks ADR 0029 Accepted and signs DEV-039; the coordinator applies the C-TICK text.

## Oracle files (read-only for implementers)
- none (docs)

## 9. Validation commands
- `scripts/gate.sh --quick` (doc links and the readability check)

## 10. Risk and suggested model tier
Low. Coordinator.

## 11. Known traps
- C-TICK is FROZEN: do not edit its row before the owner accepts the ADR.

## Notes from previous attempts
