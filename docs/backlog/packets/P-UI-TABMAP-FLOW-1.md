# P-UI-TABMAP-FLOW-1: the Tab map steers, opens painted, and costs nothing while closed

- **Serves:** not in the plan: owner direction 2026-11-22, chat of the UI rework lane ("When tab is clicked map layout is weird ... make the front end as fluid as possible")
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** feature/unity-rework (the UI rework lane's integration branch; `docs/agent/INTEGRATION_BRANCH` names the Rust one and is untouched)
- **D-rules touched:** D1 n/a (presentation only) · D2 n/a · D5/D6 none · D9 untouched (the same move orders, sent by the same path `MatchClient.OrderMapMove`, `Net/MatchClient.cs:525`)

All paths are under `client-unity/UnityProject/Assets/` unless they start with `docs/`. Line numbers are those of `Scripts/UI/TacticalMapController.cs` on the base branch (the stub commit adds about 10 lines at the top).

## 1. ID and outcome

Holding the right mouse button on the big (Tab) map steers the player exactly as it does on the minimap (press sends a move, a held drag keeps sending moves toward the cursor on the gesture's cadence, release sends the final point). The map does not touch the camera while open; a surface that is not on screen is not painted; the map is already painted on the frame it opens; the player's own marker follows the drawn character every frame instead of stepping at 30 Hz.

This packet changes behaviour only. It changes no rect, frame, colour or text of the map: the look of the Tab map is the later packet P-UI-TABMAP-SHEET-1. Eight JS capture tools copy the minimap and Tab-map rect maths (`Tools/hud-shots.cjs` and others): leave `ApplyLayout`, `MapScreenLayout` and `HudLayout.MinimapSize` alone.

The defects (evidence: `.agent-runs/lane-ui-rework/understand/tab-map.txt` defects 1, 2, 6, 7):

1. **Holding right on the big map does not steer.** `RefreshMinimapVisibility` (`TacticalMapController.cs:359-366`) runs at the end of every `ReadUiInput` (:403) and every `Tick` (:419). While the map is open the minimap is not visible, so it calls `ReleaseMinimap()` (:364) every frame, which does `_miniGesture.Abort(); _largeGesture.Abort(); RestoreCamera();` (:643). `LargePress` (:608-613) sends the first move; the next frame the steering state is gone, so a drag sends nothing and the release (`LargeRelease` :618-623, `MinimapGesture.Release` returns null when not held, `RoguehallaNet/Net/Ui/MinimapGesture.cs:63-76`) sends nothing. The minimap steers correctly.
2. **A second camera authority.** The same per-frame `ReleaseMinimap()` calls `RestoreCamera()` (:644-649), which re-follows the player (`_client.Arena.Follow(_client.Cam, motion.DrawnX, motion.DrawnY)`) every frame the minimap is hidden (map open, minimap suppressed, results). The camera has one owner each frame, `MatchClient` (`Net/MatchClient.cs:643-644` follows the peek point while `IsPeeking`); `RestoreCamera` exists to end a minimap peek.
3. **Both map surfaces are fully presented at 30 Hz whether or not they are on screen** (:442 `_large.Present(...); _mini.Present(...)`, and `RefreshDeliveredMarks` :338-342), with camp-by-actor work in `TacticalMapMarks.Present` (`Scripts/UI/TacticalMapMarks.cs:137`, `ContactNear` :308).
4. **The map opens with last time's content** for up to one paint interval: `SetOpen` (:367-375) and the Tab path in `ReadUiInput` (:391, :400-402) do not reset `_nextPaint` (throttle at :421-423), and the title, hint, contacts, quests and weapon guide (`PresentTactics` :490-525, run only `if (IsOpen)` at :425) are only written on a paint.
5. **The own marker steps.** Marks are painted from the newest snapshot at 30 Hz (`TacticalMapMarks.Present` sets `_self` from `session.Me` at :150-151); the player's own marker lags the character on screen, which is drawn at `Session.Motion.DrawnX/DrawnY` every frame.

Outcome when this lands:

1. Big map: right press sends a move (as today); holding and dragging keeps sending moves toward the cursor on the gesture's own cadence (`MinimapGesture.MoveIntervalMs` = 100); releasing sends the final point. Exactly the minimap's behaviour.
2. Gestures are released on transitions only: the minimap's gesture when the minimap goes from visible to hidden; the big map's gesture when the big map closes (Tab released, toggled off, results, window blur, disable). Never both every frame.
3. `RestoreCamera()` runs only when a minimap peek was in progress and ends or is aborted. With the map open and no peek the controller never moves the camera.
4. A surface that is not on screen is not presented: the big map only while its panel is active (`IsOpen`, which is also true on the planning screen), the minimap only while it is visible. The route planning, the saved route and the minimap ring keep working as today.
5. The frame the big map becomes visible it is already painted: marks, title, hint, rails and weapon guide are current on that frame. (The minimap likewise when it becomes visible again.)
6. The own marker of the visible surface follows `Session.Motion.DrawnX/DrawnY` every frame (when `Motion.Initialized`; otherwise the snapshot position as today). Every other mark keeps the 30 Hz paint.
7. Nothing else changes: the same orders on the wire, the same key handling (hold by default, toggle mode from the setting), planning screen, spawn picks, route editing, quest and trader marks, declutter rules.

## 2. Dependencies

None. Base `feature/unity-rework`. Keeps the `Session.CueSent` subscription and press pulses of P-UI-INPUT-CUES-1 in `MatchHud` (not touched here).

## 3. Relevant legacy paths

Presentation-only packet; legacy has no Tab-map gesture code to match beyond: legacy hold-to-open map `packages/client/src/ui/mapHold.ts` (cited by `TrainingHudVisibilityEditModeTests`), unchanged here. The reference behaviour is the Unity minimap itself (`MinimapGesture`, `RoguehallaNet/Net/Ui/MinimapGesture.cs`).

## 4. New paths

- `Tests/Editor/TabMapFlowEditModeTests.cs` and its `.meta` (the oracle).

## 5. Exclusive write scope

- `Scripts/UI/TacticalMapController.cs` (675 lines on the base; stay under 800)
- `Scripts/UI/TacticalMapMarks.cs` (676 lines; stay under 800), only for the own marker and for presenting on demand
- `docs/backlog/packets/P-UI-TABMAP-FLOW-1.md` is the oracle author's; the implementer does not edit it.

Not `Scripts/UI/TacticalMapSurface.cs`, not `MinimapGesture.cs` (library), not `MapScreenLayout.cs`, not `Net/MatchClient.cs`. No change under `client-unity/RoguehallaNet` (so no `sync-net.sh`).

## 6. Contracts and references

- Presentation only (R-AUTH, ADR 0008): every outcome is drawn from server observations; map moves go through `MatchClient.OrderMapMove` (`Net/MatchClient.cs:525`) like a world right-click, with the order budget (`RoguehallaNet/Net/OrderBudget.cs`).
- Lane rules (`.agent-runs/lane-ui-rework/specs/COMMON.md`): motion on unscaled time, nothing that allocates in a steady frame, `MatchHud` binds and unbinds sessions many times (never leave a handler on an old session), HUD graphics use theme colours only, readability gate (a summary on the first type, at most 800 lines per non-test file, no lines added to `View/ArenaView.cs` or `Presentation/SpriteActorPresenter.cs`).
- `docs/OWNER_DECISIONS.md`: searched for the Tab map, minimap and gestures; no decided row governs this behaviour.

## 7. Behaviour to preserve

- Same orders on the wire; same key handling (`UiInputGate`, hold by default, toggle mode from the setting); planning screen (spawn picks, route editing by left click, READY); route planning, saved route and the minimap ring (`UpdateMinimapRing`); quest and trader marks; declutter rules; the minimap ping on both surfaces (`SubmitMapMove` pings both marks).
- `WorldLabelVisibility.Hidden` (static) is true exactly while the panel is active and false after `ReleaseTransient`.
- `ReleaseTransient()` (blur, disable, results, session change, `SetToggleMode`) still ends everything, including a held gesture on either surface.
- `MatchClient` reads `Hud.TacticalMap.IsPeeking` / `PeekAt` / `PointerBlocked` / `IsOpen` (`Net/MatchClient.cs:397, 643, 658`) and `MatchHud` reads `PointerBlocked` / `ReleaseTransient` (`UI/MatchHud.Features.cs:51, 328`); `UI/UiLayoutReview.cs:45,64` calls `SetOpen` and reads `IsPeeking`/`PeekAt`. Keep every public member.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-TABMAP-FLOW-1` prints `GATE PASS` (the whole EditMode suite, about 2000 tests).
- The eight tests of `TabMapFlowEditModeTests` pass; the existing classes listed under "Existing tests that pin this code" pass (two of them changed by the `oracle-amend:` commit, below).

## Existing tests that pin this code

All stay green and unchanged except the two in `TacticalMapEditModeTests` named under "Conflict with the spec".

- `Tests/Editor/TacticalMapEditModeTests.cs` (14 tests, builds a controller with `CreateController`): snapshots invalidate the paint throttle and clear both surfaces' marks (`NewSnapshotInvalidatesPaintThrottle...`, `DisconnectAndRebindClearMarks...`: assert `LargeMarks().ContactCount` and `MinimapMarks().ContactCount` after `SetOpen(true); Tick()`), minimap visibility tracks open/suppress/unbind (`MinimapVisibilityTracks...`), marks model tests that build `TacticalMapMarks` alone (camps, badges, wards, chests, quest marks, eggs, labels), pointer projection, phase ownership.
- `Tests/Editor/MinimapCaptureEditModeTests.cs` (9 tests, real mouse and keyboard devices, recording transport, `MatchClient` with `Arena` and `Cam`): Tab-map right click sends one ordinary move and pings both maps; left click plans a route stop and never moves; held repeats obey the order budget but press and release do not; right click is ignored while planning; physical left release cancels a peek and restores the owner camera (expected position `ArenaView.WorldPoint(Drawn) + up*1.5 + back*30`); physical right release projects the overlay position and flushes the changed point exactly once; lost mouse capture cancels a queued drag; typing or a modal aborts before release reconciliation. These call `ReadUiInput` but never `Tick` after set-up.
- `Tests/Editor/TabMapVisualFix3EditModeTests.cs`: `SetOpen(true); Tick(); ApplyLayout(true)` then rects of the weapon-counter footer, quests heading (reads `_counterGuide`, `_questHeading` by reflection) at several windows. Needs the weapon guide laid out after the first open-frame paint.
- `Tests/Editor/HudVisualFix4EditModeTests.cs` (`HubLobbyTextAndTabMapFix4EditModeTests`, :221-313): `WorldLabelVisibility.Hidden` false when closed, true after `SetOpen(true); Tick()`, false after `SetOpen(false); Tick()`, false after `ReleaseTransient()`.
- `Tests/Editor/DeclutterLegendEditModeTests.cs`: map key lists camp buff and elite camp rows; IN VIEW heading below the last key row (unbound-session controller; `Tick` returns early).
- `Tests/Editor/TrainingHudVisibilityEditModeTests.cs` with `TrainingHudRig.cs` (real keyboard `Hold(Key.Tab)`/`Let`): arena Tab opens the map and releasing restores the HUD; practice opens no map; no ordering of drawer, Tab and wish hides a HUD panel (the "minimap" panel is `Hud.TacticalMap.MinimapRoot` active in hierarchy).
- `Tests/Editor/HudLegacyLayoutEditModeTests.cs`: minimap is a square 12 from the bottom-right corner (`MinimapRoot` rect at several windows), no button strip, chest panel above the minimap.
- `Tests/Editor/TrainingConsoleIntegrationEditModeTests.cs:354`: arena `Hud.TacticalMap.IsOpen` after Tab.
- `Tests/Editor/HudVisualFix2EditModeTests.cs` (zone labels draw after the marks mesh and the fog) and `Tests/Editor/MapScreenEditModeTests.cs` (marks zone icons, layout): build `TacticalMapSurface`/`TacticalMapMarks` alone; unaffected unless the marks class changes shape.
- Library `RoguehallaNet.Tests`: `MinimapGestureTests`, `MapAnchorsTests` (the library is not edited).

### Conflict with the spec: two existing tests assume the hidden minimap is painted (owner to approve)

The spec says the surface not on screen is not presented (outcome 4) and also that the existing tests stay green unchanged. They cannot both hold: in `TacticalMapEditModeTests`, `NewSnapshotInvalidatesPaintThrottleAndClearsBothObservedMapSurfaces` and `DisconnectAndRebindClearMarksAndRefreshSingleObservedContactRail` call `SetOpen(true); Tick();` and then assert `MinimapMarks().ContactCount == 1` (and later `== 0` after a newer snapshot while the map is still open). With the minimap hidden by the open map and not presented, the first assertion would read 0, and the later `0` would read a stale 1. The `oracle-amend:` commit (alone, one commit) changes only those two tests so each minimap assertion is made while the minimap is on screen (a `Tick()` with the map closed before opening, and `SetOpen(false); Tick()` before the minimap's later read), keeping what they prove (the minimap shows the same current delivered set; a snapshot invalidates the throttle; disconnect and rebind clear both). It passes on the stub (which presents both surfaces always) and on the implementation, and it requires outcome 5's second half (the minimap is painted the frame it shows again). If the owner does not approve the amend, the spec must change (present the hidden minimap on contact change only) before an implementer starts.

## Oracle files (read-only for implementers)

- `Tests/Editor/TabMapFlowEditModeTests.cs`
- `Tests/Editor/TabMapFlowEditModeTests.cs.meta`
- `Tests/Editor/TacticalMapEditModeTests.cs` (changed by the `oracle-amend:` commit only; see above)
- `docs/backlog/packets/P-UI-TABMAP-FLOW-1.md`

(The stub commit's `Scripts/UI/TacticalMapController.cs` edit is the implementer's to build on: see "Seams from the stub".)

## Seams from the stub (kept by the implementer)

`TacticalMapController` gets, in the `stub:` commit, no behaviour change:

- `public static Func<double> SecondsClock` (default: `Time.realtimeSinceStartupAsDouble`) and `static double Now => SecondsClock()`. Every use of `Time.realtimeSinceStartupAsDouble` in the controller goes through `Now`. The tests drive it so gesture cadence and the paint throttle are deterministic. New time reads in the controller must use `Now` too.
- `public int LargePresentCount`, `public int MinimapPresentCount` (read-only), incremented by `PresentLarge()` / `PresentMini()`, which wrap every `_large.Present(...)` / `_mini.Present(...)` call (`RefreshDeliveredMarks` and `Tick`). Route every present of a surface through them so the counts stay true.

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> TabMapFlowEditModeTests,TacticalMapEditModeTests,MinimapCaptureEditModeTests,TabMapVisualFix3EditModeTests,HubLobbyTextAndTabMapFix4EditModeTests,DeclutterLegendEditModeTests,TrainingHudVisibilityEditModeTests`
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-TABMAP-FLOW-1` (once, at the end; about ten minutes)

## 10. Risk and suggested model tier

implementer-sonnet; reviewer. One file of logic (`TacticalMapController`), a state machine of who releases what and when. Risk: breaking the peek camera hand-over (`MatchClient.cs:643`), the planning screen (panel active but `_mapOpen` false), or the static `WorldLabelVisibility.Hidden`.

## 11. Known traps

- `IsOpen` is `_panel.gameObject.activeSelf` and is also true on the planning screen: planning must keep working (spawn picks, route editing by left click) and the big surface is presented there.
- A surface's `OnDisable` calls its `Aborted` hook (`TacticalMapSurface.cs`, `void OnDisable()`), wired to `ReleaseMinimap` for the minimap (:178) and `ReleaseLarge` for the big map (:107). Hiding the minimap (`SetActive(false)` in `RefreshMinimapVisibility`) therefore fires `ReleaseMinimap` once by itself; that hook must end only the minimap's gesture (and restore the camera only for a peek), not the big map's.
- `ReleaseTransient()` (blur, disable, results, `Bind`, `SetToggleMode`) must still end everything, including a held gesture on either surface.
- `WorldLabelVisibility.Hidden` is set in four places (`SetOpen` :371, `ReadUiInput` :401, `Tick` :416, `ReleaseTransient` :650): keep it true exactly while the panel is active, false after `ReleaseTransient`.
- `Reconcile` (:568-588) calls `ReleaseMinimap()` when `Mouse.current == null` and `RestoreCamera()` on a peek whose left button is no longer pressed; keep the physical-release reconciliation the `MinimapCaptureEditModeTests` pin.
- The own-marker update must not allocate and must not trigger a full repaint (mark the mesh dirty only). `TacticalMapMarks._self` is also read by `TryQuestActorPoint` (:236) and `SelfMarkerRect` (:74-82, "what a label must stay clear of"): keep them consistent with what is drawn (mesh pass :609-615).
- `TacticalMapSurface.Present` also drives the vision fog on its surface: a surface presented again after being hidden must show the current fog.
- The big map's panel becomes active inside `ReadUiInput`, and `Tick` follows in the same frame: reset `_nextPaint` on the hidden-to-visible transition (and `_lastContactsSnapshot`, since `PresentTactics` returns early when the snapshot count is unchanged, :495-496) rather than painting twice.
- A test that leaves `WorldLabelVisibility.Hidden`, `TacticalMapController.SecondsClock`, the map-toggle preference or the input devices changed breaks later tests: the oracle restores all of them.

## Oracle: what each new test asserts, and why it is red on the stub

`Tests/Editor/TabMapFlowEditModeTests.cs`, class `Roguehalla.Client.Tests.TabMapFlowEditModeTests` (rig: real controller, `MatchClient` with `Arena` and `Cam`, a recording transport that decodes sent move commands, real mouse and keyboard devices, a 16x16 map, the clock seam; points A (100,100), B (300,120), C (400,400) are world coordinates handed to the surface's `Pressed`/`Dragged`/`PointerReleased` hooks, the same way `MinimapCaptureEditModeTests` does):

1. `HoldingRightOnTheBigMapSteers`: open; right press at A sends exactly one move at A; drag to B with the button held and 200 ms of frames sends a move at B; drag to C and release sends a final move at C.
2. `AMinimapGestureIsReleasedOnceWhenTheMinimapHides`: a right hold on the minimap steers (set-up); opening the map ends it (a later drag on the hidden minimap sends nothing); a big-map right hold started afterwards is not touched by later frames; closing the map ends it and the minimap's stays ended.
3. `TheOpenMapDoesNotMoveTheCamera`: with the map open and no peek, a camera the test placed elsewhere is where it was after ten frames; `Hidden` is true while open.
4. `EndingAMinimapPeekRestoresTheCameraOnce`: a left-hold peek keeps the peeked camera while held; releasing returns the camera to the owner position, and ten later frames do not move it; a second peek ended by opening the Tab map returns the camera once and ten later frames do not move it.
5. `AClosedMapIsNotPresented`: through `LargePresentCount`/`MinimapPresentCount`: sixty closed frames do not present the big surface (the minimap is presented, the route summary updates); sixty open frames do not present the minimap; on the planning screen the big surface is presented and the minimap is not.
6. `TheMapIsPaintedOnTheFrameItOpens`: with the map closed, toggle mode is switched on and a contact snapshot arrives and is consumed; on the frame the map opens (5 ms later, no new snapshot) the contacts rail names the contact, the hint is the toggle-mode text and not the previous opening's, the title is not empty and the large marks hold the contact.
7. `TheOwnMarkerFollowsTheDrawnPositionEveryFrame`: with no new snapshot, `Session.Motion` is hard-cut to a new drawn position between two frames 5 ms apart (inside the paint interval); the visible surface's `SelfMarkerRect` centre sits on the drawn point (rect maths of `TacticalMapMarks.At`, within 0.5 px), twice on the Tab map, once on the minimap after closing it.
8. `ReleaseTransientEndsAHeldBigMapGesture` (added to the spec's seven: a guard for trap "ReleaseTransient still ends everything"): the held gesture on the big map is alive (a drag is sent), then `ReleaseTransient()` ends it, `Hidden` is false, panel closed, and nothing more is sent.

Red on the stub (run `unity-class.sh <worktree> TabMapFlowEditModeTests`: 8 tests, 0 passed, 8 failed, each on an assertion):

| Test | Fails on |
|---|---|
| HoldingRightOnTheBigMapSteers | "holding right and dragging keeps sending moves toward the cursor ... sent: nothing" |
| AMinimapGestureIsReleasedOnceWhenTheMinimapHides | "the big map's gesture is not touched by later frames (sent: nothing)" |
| TheOpenMapDoesNotMoveTheCamera | "with the map open and no peek the controller never moves the camera" (camera at the player instead of where the test put it) |
| EndingAMinimapPeekRestoresTheCameraOnce | "the open map does not re-follow the player every frame afterwards" |
| AClosedMapIsNotPresented | "sixty frames with the map closed do not present the big surface" (expected 6, was 66) |
| TheMapIsPaintedOnTheFrameItOpens | "the contacts rail shows the new contact on the first frame" (rail reads "No other contacts in view.") |
| TheOwnMarkerFollowsTheDrawnPositionEveryFrame | "the Tab map's own marker follows the drawn x" |
| ReleaseTransientEndsAHeldBigMapGesture | "set-up: the held gesture is alive on the big map (sent: nothing)" |

## Notes from the oracle author

- The spec's tests 1-7 are written as described. Differences: (a) an eighth guard test; (b) the tests need a clock seam and two counters (see "Seams from the stub"), which the spec allowed ("a counter ... in the stub: commit"); the clock seam is the one addition beyond a counter, needed because the gesture cadence (100 ms) and the paint interval (33 ms) are read from `Time.realtimeSinceStartupAsDouble` and a test must not sleep; (c) the open frame in test 6 uses `SetOpen(true)` then a frame rather than a real Tab key: `ReadUiInput` and `Tick` then run in the same order as on a Tab press, and the Input System's `wasPressedThisFrame` is unreliable outside play mode in a bare fixture (the `TrainingHudRig` needs extra settings for it); (d) quest marks are not exercised in test 6: a contact, the hint's toggle-mode input and the rails stand for them.
- The spec's "Existing tests ... stay green, unchanged" conflicts with outcome 4 for two tests of `TacticalMapEditModeTests` (see "Conflict with the spec"). Resolved by one `oracle-amend:` commit for the owner to approve.
- Test 7 hard-cuts `Session.Motion` with `OnSnapshot(... DisplaceSeq ...)` while `Session.Me` (the snapshot position) stays at (48,48), so a marker that follows `Me` fails and one that follows `Motion.DrawnX/DrawnY` passes.
