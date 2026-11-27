# P-QUEST-ELITE-BALANCE-FIELD-1 — Quest and elite costs and rewards

## 1. Outcome and plan
Phase 2 item 11, balance measurement. Measure quest opportunities and payouts, elite encounters and loot, and how much combat power a newly acquired reward buys versus normal farming on the playable alpha.

## 2. Dependencies
The local alpha and the matched field setup at d09cca6fc. Eight RaidBots on certified generated maps with one human-eligible seat, 25 ms steps and the raid clock.

## 3. Earlier sources
The Top 25 catalog and reward audit, the accepted elite rework, the existing balance lab, the fixed-loadout elite and PvP benches, and the farm-kit mirror supply the definitions and comparison controls. The authored price workbook is design intent.

## 4. New paths
Quest and elite field observers, opt-in reward matchup diagnostics, their analysis and evidence.

## 5. Exclusive write scope
The rh-headless offline measurement binaries and PvP mirror; the tools/balance-lab analysis and usage; docs/evidence/quest-elite-balance and this packet. Runtime balance, policies and reward records are outside this measurement packet.

## 6. Contracts
C-INPUT, C-OBS and C-RNG apply to all seat decisions. The observer may read authoritative hit and settlement events after each step. Synthetic inventories are trusted offline fixtures. D1/D2 use actual source mechanics and generated raids; D5/D6/D9 unchanged.

## 7. Behavior to preserve
Preserve the farm-only matchup protocol, ordinary play, reward numbers, content identity, and prior routing/TTK measurements.

## 8. Oracle
Before this pass the field TTK observer deliberately excluded elite rosters, the existing balance lab assumed a uniform seat class for quests, and the farm mirror accepted only stack/clear inventories. Report all 25 quests including zero counts, separate elite kills and full clears, confirm a legal reward with a combat effect differs from a control, and keep conditional rewards at their tested scope.

## 9. Validation
Compare field match trajectories with the stock 80-seed baseline, run focused release tests, lint, and the full local gate. Check all report denominators, raw samples, artifact links and controls.

## 10. Risk and review
A single human-eligible policy does not cover human quest choices. A stock RaidBot may never attempt certain objectives. Combat comparisons may not activate a card or reward's economic, movement or group benefit. Report these cases separately.

## 11. Known traps
The duration starts after planning. A quest's goal, final turn-in and equipped reward are separate events. A player hitting an elite boss does not clear its escort automatically. A single-use spell and a reusable spell have different lifetime value. The settlement step can unlock gates immediately after a death.
