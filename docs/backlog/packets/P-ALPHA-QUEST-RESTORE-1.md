# P-ALPHA-QUEST-RESTORE-1 — Five known offers, ten raid givers

Serves phase 2 item 13 and the first playable raid. Integration base `dbaa701be`. Owner direction, November 26.

## 1. ID and outcome
Show one known quest per home zone during planning. Publish both distinct quests per zone at raid start, with their givers spaced apart. Give every quest giver one painted archivist body, identify available quests by name, and show a ready quest's actual turn-in.

## 2. Dependencies
The accepted Top 25 board and its per-seat actions, the admitted map, the room's authoritative phase, and Unity's existing actor and world-marker presenters.

## 3. Relevant legacy paths
`packages/shared/src/quests.ts` handles giver spacing. `packages/server/src/sim/quests.ts` and `packages/client/src/ui/questPlanning.ts` distinguish planning previews from active quests. The owner selected simultaneous publication of the other five at raid start.

## 4. New paths
Storm Ink single-sprite resources for the quest archivist, apothecary, barter merchant and trade barrel, plus their importer metadata.

## 5. Exclusive write scope
The Top 25 board deal, placement, map admission, quest observer, attempt interactions, Warhorn and their regression tests under `crates/rh-sim`. The configuration field and exhaustive config literals in `rh-sim`, `rh-server`, `rh-load` and `rh-headless`. Source-room planning and playthrough assertions. Unity's SpriteActorPresenter, ShowcaseActorLook, WorldMarks, MatchHud.QuestRewards and their actor, marker, HUD and merchant tests. The four named sprite resources, this brief, the normal-raid paragraph in `docs/quests.md` and DEV-040.

## 6. Contracts and references
C-INPUT, C-OBS, C-RNG and DEV-040. Planning omits five definitions and positions from the client view. The raid publishes those five using the existing wire schema.

## 7. Behaviour to preserve
The server draws two different entries from each zone's pool of five without changing the private draw order. Placement still requires the home region, reachable dry terrain and clearance around camps and spawns. Historical config without the spread switch retains its original placement and ten-row planning view. K1 retains its own lifecycle.

## 8. Acceptance checks
A two-player room receives five named offers in planning, then ten unique offers in the raid. The layout test checks both givers in each zone, their separation, and restored positions. Repeated meditation preserves elapsed time. A Warhorn cast without a target keeps its charge. A deferred reward points to its real endpoint. The keyboard and mouse submit the same offered action. Quest giver facing and identity select the same sprite. Unaccepted offers in the same zone display their distinct names. Merchants and their stock use painted resources.

## 9. Validation commands
`cargo test --locked -p rh-sim --test top25_board`, `cargo test --locked -p rh-sim --lib quests::board::`, `cargo test --locked -p rh-server --test top25_playthrough --test runtime_admission`, the Unity EditMode suite and the full gate. The previously failing timing and charge tests both failed against the release base.

## 10. Risk and review
A far-side giver can lengthen a quest route. One zone's first contact stays near its region pole. The second maximizes straight-line separation among eligible connected sites. No client action completes an objective locally.

## 11. Known traps
The old design deliberately published ten during planning. A pending reward can be claimed somewhere other than its starting giver. Merchant art comes from a separate presenter from quest givers. The reveal happens when the raid starts, regardless of player proximity.
