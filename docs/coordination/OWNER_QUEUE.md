# Owner queue

Decisions waiting for the owner, one line each, with the default the coordinator applies until an answer arrives. The
coordinator edits this file in place: a row leaves when the owner answers (the answer is recorded in
[MIGRATION.md](../MIGRATION.md) and indexed in [OWNER_DECISIONS.md](../OWNER_DECISIONS.md)). To answer, name the row
number in chat: "Q18 yes".

Last updated 2026-11-15 (Q28 and Q29 added by the map generator session with integration 54). Before that: 2026-11-14 (evening). The owner delegated the remaining open questions ("I lean on the side of allowing
as much independent decision making as possible"): Q24, Q25 and Q27 were decided by the coordinator and are his to
reverse (MIGRATION "Owner delegation 2026-11-14"). New questions are added here only when the decision is truly his.

## Open

| # | Item | Until then | Record |
|---|---|---|---|
| Q28 | Frost lake, "never behind the mouth": the merged build lets the lake use ground beside and behind the cave (a lake tile behind the mouth's front on 106 of 128 maps, the centroid behind it on 5). A variant that forbids it is built (`agent/opus/P-LAND-FROST-1-front`): 1 and 0 such maps, but lakes of 340-460 tiles on 32 maps instead of 47 and the whole rule on 18 instead of 25, so two of the lane's floors would be lowered. Asked in the map generator session's chat 2026-11-15. | The merged build stands; the look is pinned as a known gap (at most 115 and 8 maps). | MIGRATION "Integration 54" |
| Q29 | Frost, "a lake exists" as a hard rule: every kept map of seeds 1-128 has a lake at the mouth, but a map where the built lake and the fallback both yield nothing would be kept. Making it a refusal could cost a seed its map. | Not a refusal. | MIGRATION "Integration 54" |

## Deferred by the owner

| # | Item | Until then | Record |
|---|---|---|---|
| Q16 | `hosting/technical-demo/README.md:199` names the old content hash; the next deploy needs protocol 18 and a rebuilt client. Owner 2026-11-14: "we can work on this later when all changes are done". | Untouched; raised again at the deploy step. | MIGRATION "Owner answers 2026-11-14" |
