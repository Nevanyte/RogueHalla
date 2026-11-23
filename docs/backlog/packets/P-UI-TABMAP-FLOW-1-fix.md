# P-UI-TABMAP-FLOW-1-fix: the minimap is current on the frame it comes back

- **Serves:** note 1 of the coordinator's merge review of UI rework wave 1 (`reviewer-opus`, LAND WITH NOTES, 2026-11-23). Same owner direction as P-UI-TABMAP-FLOW-1 (2026-11-22, UI rework lane; not in ORDER_OF_OPERATIONS).
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` (holds P-UI-TABMAP-FLOW-1, P-UI-TABMAP-SHEET-1, P-UI-ABILITY-ROW-1, P-UI-TABMAP-SHEET-1-fix, P-UI-ALLOC-METER-1). Worktree `/Users/nevinmckinnon/Documents/Projects/RH-ui-b`.
- **D-rules touched:** D1 n/a (presentation) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome

P-UI-TABMAP-FLOW-1 stopped painting a map surface that is not on screen. The minimap is hidden while the Tab map is open and while a keyboard-blocking panel is open (`MatchHud.Visibility.cs:48`, `_map?.SetMinimapSuppressed(!visible)`). When it comes back, `RefreshMinimapVisibility` (`UI/TacticalMapController.cs:373`) activates it and only marks a repaint (`MarkRepaint()`, line 380); the paint happens in the next `Tick`.

In the HUD's real frame order that is one frame late. On the frame the Tab map closes: `MatchClient.Update` reads the key and closes the map (the minimap is still suppressed); in `MatchHud.LateUpdate`, `TickFeatures` runs `_map?.Tick()` first, which presents neither surface, and only then `RefreshWorldHudVisibility()` calls `SetMinimapSuppressed(false)`. The minimap is drawn that frame as it was painted before the Tab map opened: marks of where fighters were then. Before P-UI-TABMAP-FLOW-1 the hidden minimap was repainted 30 times a second, so this could not happen.

The tests of P-UI-TABMAP-FLOW-1 build the controller without `MatchHud` and show the minimap before `Tick`, the other order: they pass.

When this lands, the minimap is painted on the call that shows it:

1. In `RefreshMinimapVisibility`, on the hidden-to-shown edge of the minimap: `MarkRepaint()` as today, and when a map is bound (`_bound != null && _bound.Map != null`) the minimap is presented at once and the own marker is placed (`PresentMini(); FollowDrawn();`, the two calls `Tick` makes for a visible minimap, `TacticalMapController.cs:475-476`). The next `Tick` still repaints (the mark stays), so nothing else changes.
2. Nothing is presented while the minimap stays hidden (P-UI-TABMAP-FLOW-1's rule and its test `AClosedMapIsNotPresented` hold), and showing it twice in a row paints once.
3. The same edge for the big map is already right (read on the base, 2026-11-23; the oracle author repeated the read): `TacticalMapController.Sheet.cs` has one show method, `ShowPanel(bool wanted)`. `SetOpen` (`TacticalMapController.cs:399-406`), `ReadUiInput` (`:410-435`) and `Tick` (`:436`) call it and then `RefreshMinimapVisibility`, which on the big map's edge (`open != _largeShown`, `:382`) calls `MarkRepaint()`. `ReadUiInput` runs in the HUD's input read (`Update`) and `Tick` in `LateUpdate`, so a Tab press opens the panel in `Update` and the same frame's `Tick` paints it (`PresentLarge`, `:474`). No gap. The implementer repeats this read and stops and reports only if it finds a gap.

## 2. Dependencies

P-UI-TABMAP-FLOW-1 and P-UI-TABMAP-SHEET-1 (both on `lane/ui-rework/base-3`).

## 3. Relevant legacy paths

None: Unity presentation only, no legacy counterpart.

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/MinimapReturnEditModeTests.cs` (+ `.meta`)

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.cs` (`RefreshMinimapVisibility` only; find it by name, P-UI-TABMAP-SHEET-1 moved code into `TacticalMapController.Sheet.cs`)
- `client-unity/UnityProject/Assets/Tests/Editor/MinimapReturnEditModeTests.cs` with `.meta` (oracle)
- `client-unity/UnityProject/Assets/Tests/Editor/PlayerFlowsEditModeTests.cs`: one amended expectation, in its own `oracle-amend:` commit (below)
- `docs/backlog/packets/P-UI-TABMAP-FLOW-1-fix.md`
- Not in scope: `UI/MatchHud.Features.cs` and `MatchHud.Visibility.cs` (the HUD's frame order stays), `UI/TacticalMapMarks.cs`, `UI/TacticalMapSurface.cs`, `TacticalMapController.Sheet.cs`, `TabMapFlowEditModeTests.cs` (not changed).

## 6. Contracts and references

- UI rework lane rules: `.agent-runs/lane-ui-rework/specs/COMMON.md` (presentation only; nothing allocates in a steady frame; time through `TacticalMapController.SecondsClock` in tests).
- Existing seams: `MinimapPresentCount` (`TacticalMapController.cs:26`, `PresentMini` `:28`), `FollowDrawn` (`:392`), `MarkRepaint` (`:389`), `SecondsClock` (`:22`).

## 7. Behaviour to preserve

- A hidden surface is not presented (`TabMapFlowEditModeTests.AClosedMapIsNotPresented`).
- The edge paint is followed by one more paint on the next `Tick`, because the repaint mark stays: one extra paint per show, never per frame.
- `RefreshMinimapVisibility` is called from `SetMinimapSuppressed` (`:368`), `SetOpen`, `ReadUiInput`, `Tick` and `Bind` (`:343`): the edge is "the minimap root was inactive and is now active", decided inside it, as today's `visible != shown` test does.
- `PresentMini` reads the bound session: guard a null `_bound` and a session without a map.
- `Bind` clears both surfaces' maps (`SetMap(null)`, `:341`) and then calls `RefreshMinimapVisibility`: on that edge the paint presents once on a surface without a map and the next `Tick` repaints it. Accepted. An edge inside `Tick`'s own call is followed by that `Tick`'s normal paint: accepted.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-TABMAP-FLOW-1-fix --base lane/ui-rework/base-3` passes.
- `MinimapReturnEditModeTests` green; `TabMapFlowEditModeTests`, `TacticalMapEditModeTests`, `PlayerFlowsEditModeTests` stay green.

## Existing tests that pin this code (verified by reading)

- `TabMapFlowEditModeTests.AClosedMapIsNotPresented` (`TabMapFlowEditModeTests.cs:248`): 60 frames with the map closed do not present the big surface; the minimap on screen keeps painting; with the map open the hidden minimap's `MinimapPresentCount` is unchanged over 60 frames; on the planning screen likewise. Must stay green: the fix presents only on the edge.
- `TabMapFlowEditModeTests.TheMapIsPaintedOnTheFrameItOpens` (`:277`): the big map is painted on the frame it opens (contact, contacts rail, title, hint). The big-map edge is untouched.
- `TabMapFlowEditModeTests.TheOwnMarkerFollowsTheDrawnPositionEveryFrame` (`:303`): own marker follows the drawn position on both surfaces (the minimap after `SetOpen(false)` and two frames).
- `TabMapFlowEditModeTests.AMinimapGestureIsReleasedOnceWhenTheMinimapHides` (`:163`): `EndMinimapGesture` runs on the shown-to-hidden edge only.
- `TacticalMapEditModeTests.MinimapVisibilityTracksActiveMapSuppressionAndUnbind` (`:107`): the minimap root's `activeSelf` after `SetOpen`, `SetMinimapSuppressed` and `Bind(null)`; and the test above it (`:100-104`) closes the map and `Tick`s, then expects `MinimapMarks().ContactCount == 1`.
- `PlayerFlowsEditModeTests.DeliveredMapPointsReplaceWithoutActorFabricationOrNameDeduplication` (`PlayerFlowsEditModeTests.cs:318`): reads both surfaces through a local `ReadBoth`; amended here (below).
- `HudLegacyLayoutEditModeTests` (four uses of `MinimapRoot`): layout of the minimap root only; not touched by the paint.
- `MinimapCaptureEditModeTests.PhysicalRightReleaseProjectsOverlayPositionAndFlushesChangedPointExactlyOnce` is red when its class runs alone, on the base too (order-dependent, known): do not chase it.

No test conflicts with the packet.

## Amended expectation (own `oracle-amend:` commit, for the owner)

`PlayerFlowsEditModeTests.DeliveredMapPointsReplaceWithoutActorFabricationOrNameDeduplication`: its third `ReadBoth` ("disconnect removes current point cues immediately") cannot fail for the big map: the quest arrives while the map is closed, so the big map was never painted with it, and after `_transport.Drop()` the session is Reconnecting, `MapAllowed` is false and the big map cannot open. The amendment:
- `ReadBoth` gets a fifth parameter `bool bigMustOpen = true`; after `SetOpen(true); Tick()` it asserts `_hud.TacticalMap.IsOpen` when the flag is set. The first two calls keep it set; the third passes false.
- Before the third `ReadBoth`, the test delivers `QuestState(handle)` as today, then `SetOpen(true); Tick();` and asserts `IsOpen`, `large.QuestGiverCount == 1` and `large.QuestObjectiveCount == 1`: the big map holds a cue to lose. It then calls `_transport.Drop()` with the map still open and, BEFORE any `Tick`, asserts both large counts are 0 and `large.MerchantCount` is 0 (`MapStateChanged` calls `RefreshDeliveredMarks`, which presents the open big map; `TacticalMapMarks.Present` clears everything when the state is not InMatch). The third `ReadBoth(0, 0, 0, ..., false)` then runs as today; it covers the minimap only, and its message says so.

This test is green on the base and stays green on the stub and after the fix: it guards the closed-surface rule of P-UI-TABMAP-FLOW-1 against a change that skips an open surface on a state change. It is an amendment that tightens, not one that changes behaviour.

## Oracle files (read-only for implementers)
- `client-unity/UnityProject/Assets/Tests/Editor/MinimapReturnEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/MinimapReturnEditModeTests.cs.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/PlayerFlowsEditModeTests.cs` (the amendment only)
- `docs/backlog/packets/P-UI-TABMAP-FLOW-1-fix.md`

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/unity-class.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-b MinimapReturnEditModeTests,TabMapFlowEditModeTests,TacticalMapEditModeTests,PlayerFlowsEditModeTests`
- `.agent-runs/lane-ui-rework/bin/in.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-b scripts/gate.sh --quick --oracle packet/P-UI-TABMAP-FLOW-1-fix --base lane/ui-rework/base-3`

## 10. Risk and suggested model tier

Low: one method, a few lines. Sonnet. The risk is an unguarded `_bound` or a session without a map (see the oracle's case 5).

## 11. Known traps

- The edge is decided inside `RefreshMinimapVisibility` (visible != shown), not at the callers.
- Guard a null `_bound` and a null `_bound.Map` before `PresentMini()`. A session in a match always has a map (a Welcome carries one and Reconnecting keeps it), so the `_bound.Map` guard is not reachable from a test of a session in a match; case 5(b) covers a session with no Welcome.
- The test assembly cannot see `internal` members.
- Several packets amend `PlayerFlowsEditModeTests.cs` and P-UI-TABMAP-MARKS-1 also edits `TacticalMapController.cs` (one call in `ApplyLayout`; disjoint hunks): each merges one at a time.
- A packet branch's history stays linear.

## Oracle tests (`MinimapReturnEditModeTests`)

The rig copies `TabMapFlowEditModeTests` (its `RecordingTransport`, `SetUp`, `TearDown`, clock seam, `Frame()`, `Snapshot()`, `_actors`). A contact is `new WireActor { Id = 3, X = 80, Y = 80, Faction = 2, HpPct = 100, Status = 1 }`. The minimap tests observe a contact that appears (`MiniMarks().ContactCount`), never a contact's position; the own marker through `SelfMarkerRect`.

1. `TheMinimapIsCurrentOnTheCallThatShowsIt`: suppress, one contact, `Snapshot(); Tick();`, count 0 and root inactive (set-up); un-suppress with no `Tick`: count 1. Red on the base (the marks are those painted before the minimap was hidden).
2. `TheSameHoldsWhenTheTabMapCloses`, in the HUD's order: `SetOpen(true); Tick();`, `SetMinimapSuppressed(true)` (what `RefreshWorldHudVisibility` does after that `Tick`), the contact, `Snapshot(); Tick();` (`ReadUiInput` and `Tick` of the next frame's start), `SetOpen(false)` (the key closes the map; root still inactive), `Tick()` (`TickFeatures` runs the map first: still inactive, `MinimapPresentCount` unchanged), then `SetMinimapSuppressed(false)` with no `Tick`: root active, contact count 1, `MinimapPresentCount` rose by exactly 1. These are the calls of `ReadUiInput`, `TickFeatures` and `RefreshWorldHudVisibility`. Red on the base (count 0).
3. `TheOwnMarkerIsPlacedToo`: suppress; displace the presented position (`motion.OnSnapshot(... X = 260, Y = 90, DisplaceSeq = 1)`, `DrawnX` is 260: set-up); `Tick()`; un-suppress with no `Tick`: `SelfMarkerRect.Value.center` is the point the drawn position gives (`rect.xMin + DrawnX / (16 * 32) * rect.width`, `rect.yMax - DrawnY / (16 * 32) * rect.height`, within .5), not the snapshot's own position (48, 48). Red on the base.
4. `AHiddenMinimapIsStillNotPresented`: suppress; `c0 = MinimapPresentCount`; ten `Frame()` calls: still `c0`; un-suppress: `c0 + 1` on that call; one `Frame()`: `c0 + 2`; a second un-suppress: `c0 + 2`. The first half is green on the base and guards the fix; the `c0 + 1` line is red on the base.
5. `ShowingWithoutAMapDoesNothing`: (a) `Bind(null)`, suppress, un-suppress, `Tick()`; (b) a second `Session` on its own `RecordingTransport`, connected and opened but given no Welcome (asserted: `Map` null, `State` not InMatch), the same three calls; `Assert.DoesNotThrow`, `MinimapPresentCount` stays 0, minimap root inactive. Green on the base; it guards the edge's surroundings.

## Notes from the oracle author
<!-- filled with the run results in the oracle commit -->

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
