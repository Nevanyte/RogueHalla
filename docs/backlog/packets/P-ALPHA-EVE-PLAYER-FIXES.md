# P-ALPHA-EVE-PLAYER-FIXES

## ID and outcome

Phase 4, item 18. Address four issues reported in the open alpha on November 27.
Leaderboard entries occupy readable, separate rows. Nearby players can open either trader with F.
The held weapon guide and confirmed melee swing remain legible on the Coliseum floor.
The signature Holy Hall keeps its authored interior clear of generic outdoor dressing.

## Dependencies

The Unity Web client and the current protocol 24 Source, including delivered trader rows,
movement collision tiles, weapon shapes and signature map anchors.

## Relevant legacy paths

- `packages/client/src/ui/traderPanel.ts`, `packages/shared/src/traders.ts` and
  `packages/client/src/scenes/RaidScene.ts` establish proximity, sight and interaction priority.
- `packages/client/src/ui/hub/leaderboardPanel.ts` renders standings.
- `packages/client/src/render/FxView.ts` presents confirmed attack events.

## New paths

This packet brief.

## Exclusive write scope

- `client-unity/RoguehallaNet/Net/Pathfinder.Geometry.cs` and its synchronized Unity copy.
- Unity scripts `MatchHud.QuestRewards.cs`, `HubProgressView.cs`, `HitZonePresenter.cs`,
  `WeaponRangePresenter.cs` and `RegionProps.cs`.
- Their EditMode coverage in `PlayerFlowsEditModeTests.cs`, `HubProgressViewEditModeTests.cs`,
  `HitZonePresenterEditModeTests.cs`, `WeaponRangePresenterEditModeTests.cs` and
  `StormInkHolyHallEditModeTests.cs`, plus this brief.

## Contracts and references

C-INPUT, C-OBS, R-AUTH and ADR 0008. The client uses the existing movement ray for
presentation and opens an existing trade dialog. Rust validates a separately submitted
trade. Rendered weapon arcs use the delivered geometry. The Hall change applies to
decorative placements on maps with a valid signature Hall.

## Behaviour to preserve

Pickups and existing quest interactions retain their input priority. The click and F
paths select a specific delivered merchant. Preserve collision, public bush concealment,
combat timing, replay data, the reduced-motion choice and older map presentations.

## Acceptance checks

- Open the Soul Broker and Wandering Villager from their actual interaction lane. Verify
  selection with another merchant present, no purchase on open, the 112-unit boundary
  and rejection behind a movement-blocking tile. Retain the quest interaction suite.
- Lay out twenty leaderboard entries at 960×600, including long titles. Verify actual row
  separation and pooling when the list changes.
- Verify both weapon guide edges and swing contrast beyond the short brush animation.
  Exercise normal and reduced motion.
- Exercise rotated signature Hall footprints with shipped rocks, flowers, grass, pavers
  and public cover. Check the exterior and existing anchor fallback.

## Validation commands

- `bash client-unity/sync-net.sh --check`
- `dotnet test client-unity/RoguehallaNet.Tests`
- `bash client-unity/UnityProject/Tools/editmode.sh`
- `scripts/gate.sh`

## Risk and suggested model tier

Sol for rendering and map placement, Luna for leaderboard layout. Validate the Unity player
with its existing Rust content identity when preparing a release.

## Known traps

The Coliseum can emit a swing with no melee windup. A single visible wall face differs
from a clear movement ray. Merchant rows may move between updates. A hidden label
inside a pooled leaderboard card can still reserve layout space.
