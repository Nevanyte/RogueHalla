# P-RAID-ROUTE-TTK-1 — Faction farming incentives and field time-to-kill

## 1. Outcome and plan item

Phase 2 item 11, measured balance. Compare the ten intended farm builds with choosing the nearest eligible camp and a random nearby camp. Measure actual player kills and full normal camp clears by active raid age. Record open and interrupted contacts, the number of contributing maps, survivor selection and cases where the targeted build loses.

## 2. Dependencies

The local playable alpha at c1d65623d, the live content and RaidBot profile, public camp reward information, the path estimator, the balance lab, the trusted build setup for offline benches and the existing PvP bench.

## 3. Earlier implementation

The two builds per faction and weights come from `packages/server/src/bots/brain/buildRoute.ts:30`. The legacy farm-kit mirror at `packages/server/bench/farmKitDuel.ts:28` exchanged sides to compare two inventories. Section 8 of `testing/FARM_ROUTE_PROOF.md` records those results and the original build weapons are in `testing/farm_route_proof.py:59`.

## 4. New paths

The offline route and field-TTK runners in rh-headless, an earned-farm mirror module, analysis tools, this packet and the results under docs/evidence.

## 5. Exclusive write scope

The rh-policy offline farm selector, its public-observation adapter and tests; the rh-headless measurement runners, PvP bench and tests; their feature declarations; tools/balance-lab analysis files; docs/evidence/raid-routing-ttk and this brief.

## 6. Contracts

C-INPUT, C-OBS and C-RNG govern the decision path. The benchmark only reads game events and uses a separate deterministic route selector. Each route sees the same public candidate information. A trusted offline duel fixture installs earned farm before any combat command. D1 uses the existing loose balance proof and source version of the build weights. D2 uses generated raids and input-driven combat. D5, D6 and D9 are unaffected.

## 7. Behavior to preserve

Ordinary room configurations, map generation, camp payouts, duel pacing, default policy decisions, protocol and content identity stay at the playable baseline.

## 8. Acceptance and oracles

The baseline lab did not measure victim contacts through death or export matched, elapsed-time farm kits for these routing alternatives. A matched seed fixes faction, build, map, focal seat and competitors. Snapshot fields distinguish surviving seats from eliminations and runs ending early. A timing control must exercise at least one incomplete contact and a completed episode. A combat control must distinguish a strong earned kit on either side. Performance claims use the paired sample and state its unit.

### Follow-up fixed before inspecting the holdout

Against the first eleven complete maps from seeds 1–40 the unconstrained route frequently spent longer moving than the nearest farmer. Test one bounded variant with the same build weights on seeds 41–100. It may bypass the closest eligible camp only when the alternative costs at most four additional seconds of estimated walking. Compare it to the earlier choices on exactly those same seeds. The primary minute-ten comparison and the equal-stack comparison both count one independent map per seed. The screen of nine single-stat alternatives uses a fixed 12-stack increment at 72 and 180 baseline stacks.

## 9. Validation

Run both release measurement binaries, the mirror control and the analysis on their outputs. Run the full project gate with PostgreSQL and Unity. Save the exact commands, sample sizes, and measurement limitations alongside the findings.

## 10. Risk and review

Simultaneous elimination, spectator caps, blocked or contested camps and return engagements can bias a median. Report the method and counts alongside the results. Review the experiment independently of the measured winner.

## 11. Known traps

Camp age bands start when the raid starts. Cross-band contacts go into the band of first contact. Camp TTK ends at a full camp clear. Player-versus-player TTK requires player damage and a credited player kill. Mirror outcomes measure farm combat value under their declared kit and duel conditions. A different number of spins or attack orientations does not create more independent maps.
