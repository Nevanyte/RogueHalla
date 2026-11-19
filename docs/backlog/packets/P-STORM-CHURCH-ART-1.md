# P-STORM-CHURCH-ART-1: Storm Ink Sentenced church first playable pass

## ID and outcome

Give the Sentenced church a clear rear-to-front nave, dual three-tile thresholds, a single fading roof, a slate and rune floor,
walk-through oak pews, a raised side pulpit, garnet lancets and candles. Keep the graveyard and gate treatment. The approved
church concept study is the visual target for the overhead camera. Both doorways belong to the one roofed church footprint.

## Dependencies

- `P-UNITY-LANDMARK-1` and `P-UNITY-LANDMARK-3` (the roof/interior framework and existing church art).
- The current church blueprint in `docs/landmarks/blueprints/church.txt`, including rear doorway `R` at row 3, columns 13–15.
- Original generated cutouts: `client-unity/UnityProject/Assets/Resources/StormInk/Church/OakPew.png` and
  `GothicPulpit.png`.

## Relevant legacy paths

None. This is presentation and map footprint art; DEV-020 applies. No game rule changes.

## New paths

- `docs/backlog/packets/P-STORM-CHURCH-ART-1.md`
- `client-unity/UnityProject/Assets/Resources/StormInk/Church.meta`
- `client-unity/UnityProject/Assets/Resources/StormInk/Church/OakPew.png` and `.meta`
- `client-unity/UnityProject/Assets/Resources/StormInk/Church/GothicPulpit.png` and `.meta`

## Exclusive write scope

- `client-unity/RoguehallaNet/Net/Ui/LandmarkInterior.cs` and its synchronized Unity copy.
- `client-unity/RoguehallaNet.Tests/ChurchLandmarkDrawingTests.cs` and `LandmarkInteriorTests.cs`.
- `client-unity/UnityProject/Assets/Scripts/View/Regions/Landmarks/ChurchLandmark.cs`.
- `client-unity/UnityProject/Assets/Tests/Editor/ChurchLandmarkEditModeTests.cs`.
- The generated church cutouts and this packet.

The church data blueprint and Rust map tests are owned by the map packet; do not edit them in this packet.

## Contracts and references

- Client rendering remains presentation-only; delivered map tiles still control movement and collision.
- The church's 204 floor tiles remain the interior, while both three-tile door runs stay outside the interior mask.
- The church art is generated once from the landmark's delivered tiles and is fitted to all eight footprint orientations.
- Doorway openings are paired as one building for interior flood fill and roof grouping. A one-door building remains supported.
- `client-unity/AGENTS.md`, `docs/backlog/packets/P-UNITY-LANDMARK-1.md`, and `P-UNITY-LANDMARK-3.md`.

## Behaviour to preserve

- One landmark and one roof over the church's walls and interior; the roof fades for an actor inside.
- Graveyard, yard, and main gate stay outside the church roof and interior.
- Both thresholds remain traversable floor. Furnishings add no colliders and do not change map occupancy.
- No per-frame drawing, allocation, or game outcome logic is added.

## Acceptance checks

- Engine-free tests prove the 204 interior tiles and exclude both door runs in all eight orientations; the one-door tests remain green.
- Unity EditMode tests cover one roof, both door surrounds, door fade exclusion, pew and pulpit placement, glass and candle objects,
  and all eight landmark orientations.
- Review browser gameplay at normal zoom for the intended slate, stained glass, furnishing scale, roof openings, and walk-through
  aisle. Visual quality is not proven by structure-only tests.
- Run `bash client-unity/sync-net.sh --check` after syncing the engine-free library.

## Validation commands

- `/Users/nevinmckinnon/.dotnet/dotnet test client-unity/RoguehallaNet.Tests`
- `bash client-unity/UnityProject/Tools/editmode.sh`
- `scripts/gate.sh --quick --oracle packet/P-STORM-CHURCH-ART-1`

## Risk and suggested model tier

Sonnet. The main risks are orientation errors in door or furniture placement and image scale differing from the target camera.
The Unity EditMode and gameplay visual review are required to catch them.

## Known traps

- `LandmarkInterior` is engine-free source; always sync it into `UnityProject/Assets/RoguehallaNet/`.
- The roof must cut over both thresholds while retaining one building silhouette.
- EditMode can change `Assets/Resources/Showcase/NunitoRegular.asset`; do not commit that generated change.
- Unity must not be opened while the coordinator is running EditMode in this worktree.
