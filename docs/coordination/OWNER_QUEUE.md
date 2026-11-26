# Owner queue

Decisions waiting for the owner, one line each, with the default the coordinator applies until an answer arrives. The
coordinator edits this file in place: a row leaves when the owner answers (the answer is recorded in
[MIGRATION.md](../MIGRATION.md) and indexed in [OWNER_DECISIONS.md](../OWNER_DECISIONS.md)). To answer, name the row
number in chat: "Q18 yes".

Last updated 2026-11-23 (Q30 added by the balance patch with integration 88; the balance patch's other open items are listed in the top sections of MIGRATION). Before that: 2026-11-15 (Q28 and Q29 added by the map generator session with integration 54). Before that: 2026-11-14 (evening). The owner delegated the remaining open questions ("I lean on the side of allowing
as much independent decision making as possible"): Q24, Q25 and Q27 were decided by the coordinator and are his to
reverse (MIGRATION "Owner delegation 2026-11-14"). New questions are added here only when the decision is truly his.

## Open

| # | Item | Until then | Record |
|---|---|---|---|
| Q28 | Frost lake, "never behind the mouth": the merged build lets the lake use ground beside and behind the cave (a lake tile behind the mouth's front on 106 of 128 maps, the centroid behind it on 5). A variant that forbids it is built (`agent/opus/P-LAND-FROST-1-front`): 1 and 0 such maps, but lakes of 340-460 tiles on 32 maps instead of 47 and the whole rule on 18 instead of 25, so two of the lane's floors would be lowered. Asked in the map generator session's chat 2026-11-15. | The merged build stands; the look is pinned as a known gap (at most 115 and 8 maps). | MIGRATION "Integration 54" |
| Q29 | Frost, "a lake exists" as a hard rule: every kept map of seeds 1-128 has a lake at the mouth, but a map where the built lake and the fallback both yield nothing would be kept. Making it a refusal could cost a seed its map. | Not a refusal. | MIGRATION "Integration 54" |
| Q30 | Balance patch: which farm rate prices a quest's time. The design workbook assumes 2.9 camps a minute. The test bot clears 0.68 per living minute, and 1.67 per minute spent fighting camps; a Fire seat 1.37, the other four factions 0.40 to 0.66. Every quest verdict moves with that rate (five measured quests read 1.3 to 7.7 times their price at the bot's rate and 1.2 to 2.2 at 2.9). The faction gap is the game's own numbers: a scripted fighter with no bot clears the same normal camps in 13.1 s as Fire and 17.6 to 26.4 s as the others. So either camps are meant to die faster than they do (camp numbers), or the workbook's rate is too high (quest prices), or the factions are meant to farm at the same pace and do not (faction numbers, a deviation from legacy); the bot may also be slow in a way a player is not. | The evaluator prints every verdict at both rates; no quest timer or reward moves; P-BAL-FIRE-1 measures why Fire differs. | MIGRATION "Integration 88 merged (2026-11-23)"; [pilot 2](../evidence/balance-patch-2026-11/pilot-2/README.md) |

## Deferred by the owner

| # | Item | Until then | Record |
|---|---|---|---|
| Q16 | `hosting/technical-demo/README.md:199` names the old content hash; the next deploy needs protocol 18 and a rebuilt client. Owner 2026-11-14: "we can work on this later when all changes are done". | Untouched; raised again at the deploy step. | MIGRATION "Owner answers 2026-11-14" |
