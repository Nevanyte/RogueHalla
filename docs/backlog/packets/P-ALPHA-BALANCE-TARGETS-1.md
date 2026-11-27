# P-ALPHA-BALANCE-TARGETS-1 — The cost of quests, targeted farming and elites

## 1. Outcome
Phase 2 item 11, balance of the playable raid. Owner targets a solo elite full clear at about twice the comparable normal camp after minute five, and a 25–50% value premium for a well-chosen quest or faction farm route after walking and fighting.

## 2. Dependencies
The measured alpha at 9174b76a7, the 80-map quest, loot and TTK studies, the paired route lab and 40 Hz combat inputs.

## 3. Earlier sources
The authored build weights, normal camp resistance curve, reworked elite roster, Top 25 reward mechanics and documented farming buff. Keep existing historical replay fixtures intact.

## 4. New paths
Matched solo camp comparison, quest and route opportunity-cost analyses, and the evidence directory docs/evidence/alpha-balance-targets.

## 5. Exclusive write scope
Camp defense, its content registration and tests, earned normal-camp stat payouts and targeted farm decisions where required, quest reward tuning and quest evaluation, compatible gameplay help/identity copies and generated content vectors, headless measurement tools, their documentation, deviation record and this packet. No change to public infrastructure.

## 6. Contracts
C-INPUT, C-OBS, C-RNG and C-TICK. Authoritative rules apply to humans and bots. Independent scripted actors use validated inputs. Restore historical modes and keep their oracles green.

## 7. Behavior to preserve
Opening full-health camp difficulty; valid quest claims, spell grant and use; ordinary farming as a viable fallback. Keep the difference between immediate combat, situational utility and walking cost visible.

## 8. Failing oracle
At minutes 10–15, stock solo elite clears took 8.8 seconds and normal clears 13.7. Unrestricted route value at minute ten trailed nearest. Trade Up awarded 54 same-stat stacks on all 35 observed claims. Reproduce controlled baseline gaps before tuning.

## 9. Validation
Use matched zones, fighters, equipment, run age and seed for camp comparisons. Report deaths, timeouts, conditional clears, sample sizes and variant spread. Use independently measured travel and combat for the farming and quest comparisons. Report held-out performance, zero coverage and conditional utility. Run focused checks, full local gate, matched client identity and a local room smoke check.

## 10. Risks and review
The owner set a target for well-chosen activities. Report any faction, quest, elite or age window not demonstrated to meet it. Valuation of mobility, scouting and timely extraction depends on the situation.

## 11. Traps
A duel win rate is not a percentage return on time. Giving a route an extra stat only in the lab does not demonstrate its value in the game. Compare fresh camp clears separately from re-engagements, and late loadouts separately from early controls.

For elite tuning, use generated maps 1–4 to size the curve and reserve maps 21–30 for its independent test. Score the median elite/normal time ratio within solo pairs that cleared both fresh rosters, at starting minutes 6, 10, 15 and 20. Treat 1.7–2.3 as “about 2×” (15% either side of the owner target). Also publish absolute times, deaths, unfinished pulls and boss-specific results. Both members of each pair use identical farm rolls, base gear and accepted player inputs under the same scripted policy. The normal control is in the boss's zone with the same normal mob type where available. Starting pulls at minute zero check the preserved opening.

The first held-out aggregate passed all four ages. Archon was systematically fast, Avalanche Troll and Pox Behemoth slow, with Blood Saint and Glacial Warden below the aggregate across later ages. For a second tuning step, reserve maps 41–50 for a new test and compare the per-kind 10–20-minute distributions as well as the original overall target. Leave base health, patterns and the opening untouched.
