# P-STORM-CHURCH-CAST-1

## ID and outcome
P-STORM-CHURCH-CAST-1 serves the D10 Unity visual pass. The Sentenced champion and Executioner wear distinct original ink-painted bodies at play zoom.

## Dependencies
The existing public actor identity, archetype table and Unity sprite presenter. P-STORM-CHURCH-MAP-1 and P-STORM-CHURCH-ART-1 supply the church setting for the browser round.

## Relevant legacy paths
`packages/client/src/render/PlayerView.ts` supplies the presentation reference. The owner selected original Storm Ink designs for this pass.

## New paths
Storm Ink actor sheets under Unity Resources, a presentation-side skin selector, art provenance and a live presenter EditMode test.

## Exclusive write scope
`Assets/Resources/StormInk/Actors`, Storm Ink actor-related resources and metadata, `Assets/Scripts/Presentation/StormInkActors.cs`, `SpriteActorPresenter.cs`, `Assets/Tests/Editor/StormInkChurchCastTests.cs`, their metadata, this packet, and the corresponding art receipt.

## Contracts and references
R-VIS, C-OBS, ADR 0008, ADR 0024. Public faction or mob type picks art within the size and clip cadence of the delivered archetype.

## Behaviour to preserve
Facing, gait, weapon selection, confirmed combat, observation loss, reduced motion and the existing sprite fallback.

## Acceptance checks
The production presenter shows distinct ink artwork for the Sentenced champion and the Executioner on real archetype keys. The art changes with the direction and action. The Web round checks silhouettes in the church.

## Validation commands
`client-unity/UnityProject/Tools/editmode.sh`, `scripts/gate.sh`, and the browser round in `docs/unity/WEB_ROUND.md`.

## Risk and suggested model tier
Sol implementation and Luna visual review. The close reference drawing must retain its silhouette at play zoom.

## Known traps
Several NPCs share an archetype key. A sprite lookup indexed only by archetype would replace unrelated mobs and outfit changes must never alter collision.
