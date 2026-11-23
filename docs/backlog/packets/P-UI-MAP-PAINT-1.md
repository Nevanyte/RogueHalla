# P-UI-MAP-PAINT-1: the map paints only when there is news, and tests each camp against the actors once

- **Serves:** not in the plan: owner direction 2026-11-22 (UI rework lane chat): "make the front end as fluid as possible",
  "Please do a UI optimization sweep". The lane's Web measurement of 2026-11-23: the map marks are the largest part of the
  HUD's per-frame cost, .5 to 1 ms with 30 to 60 actors.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (head `c8e21c0e1`, seventeen wave 4 packets on integration 87).
  No commit since `3d3374797` touches `TacticalMapController*.cs`, `TacticalMapMarks*.cs` or `TacticalMapSurface.cs`.
  The surfaces draw `Session.TerrainMap`, which changes only by a `DestroyedTerrainState` (`CollectionsView.Apply` builds a new
  `MapBlob` each time); the walk map of `LocalMotion` is not drawn.
- **D-rules touched:** D1: legacy has the cut asked for (`packages/client/src/ui/minimap.ts` skips a repaint when `sameAsLast()`
  finds its inputs equal); presentation only, nothing a player sees changes: no `DEVIATIONS.md` row. D2 n/a. D5 none. D6 none.
  D9 untouched (no rule moves).
- Evidence: `.agent-runs/lane-ui-rework/understand/perf-minimap-cost.txt` (its CHECK), `perf-ab-1-windows.txt` (column Map),
  `exp-hud-garbage.txt` section 2 (state F). Choices the design left open: **(oc)**.

All paths below are under `client-unity/UnityProject/Assets/Scripts/UI/` unless stated. The spec's lines were re-read on the base.

## 1. ID and outcome

What is wrong today (lines on the base):

1. `TacticalMapMarks.ContactNear` (`TacticalMapMarks.cs:282-287`) asks "does a non-self actor stand within `CampDropPx` (8,
   `:98`) of this point", in the marks' layer units (what `At` returns, `:265-269`; the Tab layer is drawn k, about 1.3, times
   larger, so 8 layer units are 8 k stage units there and 8 on the minimap). Each call loops `_actors`, converting every actor
   with `At` (a `rectTransform.rect` read each). Six call sites in five methods: `RefreshCampClocks` (`:300`), `RefreshBadgeTexts`
   (a camp loop `:424` and a ward loop `:431`), `RefreshBadgeIcons` (`:448`), and in the mesh pass `DrawBadges` (`:482`) and
   `DrawCamps` (`:495`). With 50 camps and 40 actors that is about 100 tests per actor per `Present` and 50 more per mesh
   rebuild, which also runs on every frame the own marker moves (`FollowSelf`, `:142-148`).
2. The minimap draws no camp text, yet `RefreshBadgeTexts` (`:415-437`) loops every camp and ward on it, `At` and `ContactNear`
   each: `ShowCampTexts` returns at once there (`_ctCount` is 0 because `PlaceCampTexts` returns unless `Large`,
   `TacticalMapMarks.CampTexts.cs:68`) and the ward loop `continue`s on `!Large` after the test (`:432`). Cut 2 of
   P-UI-TABMAP-TEXT-1 is NOT done by that packet.
3. `TacticalMapController.Tick` (`TacticalMapController.cs:444-487`) paints when `Now >= _nextPaint` (`:461`), then sets
   `_nextPaint = Now + 1d / 30d` (`:463`); a snapshot, collections or state event zeroes it (`:599-603`): 20 to 40 paints a
   second, half identical; `TickGesture` (`:485-486`) sits behind the early return. `MapCollectionsChanged`, `MapStateChanged`
   and `SetTrackedQuest` present at once (`RefreshDeliveredMarks`, `:352-356`) and zero the timer: two presents.
4. `PresentTactics` (`:532-574`) rebuilds on every new snapshot while the Tab map is open: two `StringBuilder`, a groups `List`,
   two `ToString().TrimEnd`, two `foreach` over `IReadOnlyList`, `Me.Weapon.ToString()` and `ToLowerInvariant`: 28 allocations
   (the evidence's count, not re-measured), for texts that rarely change.

**Outcome.** Units: the marks' layer units. Nothing a player sees changes: this packet changes how often and how cheaply the map
is painted, never what is drawn. Moving marks paint at the server's delivery rate (a snapshot is news: no lower rate; the
default step of 25 ms delivers 40 a second); the own marker follows the drawn character every frame.

1. **One cover test per `Present`.** `ComputeCover()` runs in `Present` after the lists are built, before `RefreshCampClocks()`.
   When `_map` is null, or `_camps` and `_wards` are both empty, it sets both counts to 0, records the sizes of item 2 and
   returns: no actor is converted. Otherwise it reads the layer rect once, turns each non-self actor into a layer point once with
   `AtIn(Rect, int, int, Vector2)` (`At` calls it as `AtIn(rectTransform.rect, _map.W, _map.H, world)`: the same expression),
   then for each camp row i and ward row j stores whether the first actor point, in sample order, has
   `(a - p).sqrMagnitude < CampDropPx * CampDropPx`. Same rule, same units: not pixels, not world units, not times k.
   `CoverTests` counts each distance test made (a row stops at its first hit). `CampCovered(i)` and `WardCovered(j)` read the
   flags (false out of range). The six call sites read them by loop index: a camp site by its `_camps` index (equal to
   `CampGlyphRow.Index`: `Glyphs` adds one row per camp), a ward site by its `_wards` index, NOT `CampWardRow.Index` (the camp's
   index: `Wards` skips camps without a ward). `ContactNear` is deleted, `DrawCamps` and `DrawBadges` become `for` loops, and a
   camp's `At` is computed only after the flag test. Arrays are grown, never shrunk: `ComputeCover` allocates nothing in a steady
   `Present`. `SetMap(null)` clears both counts.
2. **The rect and the map.** The flags depend on the layer rect's size and the map's size. `EnsureCover()` calls `ComputeCover()`
   again when the rect's width or height, or the map's W or H, differs from what the last `ComputeCover` recorded (a null map
   records 0 x 0); it runs first in `LayerChanged` (`:128`) and in `OnPopulateMesh` (`:509`) after the null-map return. **(oc)**
   Lazily there, not in `OnRectTransformDimensionsChange` (may run on an inactive object). A mesh rebuild with an unchanged rect
   and map makes no cover test.
3. **Minimap.** `RefreshBadgeTexts` runs its two loops only when `Large`; the tail hiding unused pooled labels always runs.
   **(oc)** No counter: the reviewer reads it.
4. **Heartbeat.** `const double HeartbeatSeconds = .25d` (private) replaces `1d / 30d`. `TickGesture(_miniGesture);
   TickGesture(_largeGesture);` move to just after the first `FollowDrawn()` (`:460`), before the `_nextPaint` return. A held
   gesture's repeat is offered on every frame; one the client refuses (`Unsend`) is retried on every frame until the client's own
   budget lets it through. Nothing is sent more often than the gesture's 100 ms interval (`MinimapGesture.MoveIntervalMs`): a
   send sets its time.
5. **News.** `MarkNews()` is `_factsDirty = true; _nextPaint = 0; _largeFresh = _miniFresh = false;`. Every write of the paint
   timer to 0 becomes it EXCEPT the conditional write in `Tick` (`if (_factsDirty) _nextPaint = 0;`, `:459`, which stays):
   `MapSnapshotChanged` (`:599`), `MapCollectionsChanged` (`:600`), `MapStateChanged` (`:603`), `SetTrackedQuest` (`:350`),
   `SetShowRoute` (`:358`), `PickSpawn`, `PickBoon`, `Ready` (`:608-610`), `AddRoutePoint` (`:611`), the Undo and Clear lambdas
   (`:149-150`), `Bind` (`:343`). `MarkRepaint()` (`:397`) is `MarkNews(); _lastContactsSnapshot = -1;`. Where a statement also
   zeroes the route's replan timer (the two lambdas in `Initialize`, `AddRoutePoint`, `Bind`: `_nextPath = _nextPaint = 0`), the
   `_nextPath = 0` write STAYS and `MarkNews()` is added beside it, else `RouteSummary` waits up to 250 ms. `PickSpawn`,
   `PickBoon` and `Ready` call `MarkNews()` after the `Try*` call, outside its `if`, taken or refused: a refused pick changes
   `PlanningSelections.Feedback`, which `PresentPlan` shows on the next Tick. **(oc)** More sources: `ApplyLayout` (`:253`) right
   after its early return, and `ApplyViewport` (`:360`) when it writes a different `sizeDelta` (a rect change moves every pooled
   label, which only `Present` places). In `Tick` each surface's map is compared BEFORE the `SetMap` calls (`:447`;
   `!ReferenceEquals(_large.Map, _bound.TerrainMap)`, likewise `_mini`): news if changed.
6. **Paint once.** `PresentLarge` and `PresentMini` (`:27-28`) set their fresh flag, but only when the surface's own map
   (`_large.Map`, `_mini.Map`) is not null: a present made without a map shows no camp, so it is not fresh (this stays beside the
   map compare of item 5: each is cheap and neither depends on the other). The three handlers and `SetTrackedQuest` call
   `MarkNews()` BEFORE `RefreshDeliveredMarks()` (`SetTrackedQuest` calls it after today). In the paint branch `bool news =
   _factsDirty` is read before it is cleared, and `bool replanned` is set when `PlanAhead` ran in this branch (the route points
   are new). Each surface's skip test is evaluated AT ITS OWN present statement (`:482-483`), after `PresentPlan` or
   `PresentTactics` have run, so news marked inside them counts: `if (IsOpen && !(news && _largeFresh && !replanned))
   PresentLarge();`, likewise the minimap. A branch run by the timer alone (`news` false) presents every visible surface.
   **(oc)** The minimap's show edge in `RefreshMinimapVisibility` (`:383-387`) follows the same rule (marks, presents; the next
   Tick does not present again without news); with a null surface map that present is not fresh, so the Tick after the first
   show presents.
7. **Things that change with time and no delivery.** Camp clocks, ward countdowns, chest seconds and the plan timer are functions
   of `Session.ServerTimeMs`, the newest snapshot's time: they change only with a snapshot. The own marker (`FollowDrawn`) and
   the walk ping (`Update`, `:130`) are not paints; the route replan keeps its 250 ms (`_nextPath`). Accepted: the planning
   screen's 600 ms pending-choice expiry shows up to a heartbeat late when nothing is delivered; a theme change and the fog's
   on/off (`TacticalMapSurface.Present`: no event) wait for the heartbeat. A layout or viewport change IS news.
8. **Tab-held texts.** `PresentTactics` moves, same name and signature, to the new partial `TacticalMapController.Tactics.cs`.
   It rebuilds a text only when its signature changed, with reused builders and lists; the texts stay letter for letter.
   Contacts: two ints per non-self actor in sample order, `faction + 1` (0 when none) and the kind (255 when
   `PlayerWords.IsNamed` is false), kept in a reused `int[]` with a stored count and compared element by element with the last
   shown; nothing is packed into shared bits (a faction byte is 0 to 255). Quests: the strings it appends, in order, plus the
   offer count, compared with `string.Equals`. Both stored counts (contacts, quests offer count) start at -1 and `Bind` sets both
   to -1 again, so the first pass after a bind always writes the quests text, the fallback line "No quest givers or traders
   delivered." included (`_questsText` is created empty and `Bind` does not write it). Weapon: a static 256-entry `string[]` of
   `((WeaponId)i).ToString().ToLowerInvariant()` by `(byte)Me.Weapon` ("fists" without `Me`), and `SetWeapon(key)` only when
   `_counterGuide.ActiveWeapon != key` (a wheel click is still undone by the next snapshot, as today). `IReadOnlyList` loops use
   `for`; rows are separated, not terminated. "Cleared" is an invalid state, not an empty list: `Bind` (`:334`) and
   `MapStateChanged` (`:605`) write `_contactsText` themselves, and each sets the stored contacts count to -1 in that statement,
   so the first compare after either always rebuilds (a bound session with no contacts reads "No other contacts in view."). The
   legend `ApplyLayout(true)` check (`:564-565`) stays. **(oc)** The signature decides, not a forced flag;
   `_lastContactsSnapshot` still gates the block. If the reflection write of oracle test 13 allocates, the controller gets ONE
   public read-only `int TacticsRebuilds` (in `Tactics.cs`, +1 per pass that rebuilds a text) and the test reads it.

## 2. Dependencies

Base `lane/ui-rework/base-3`. P-UI-ALLOC-METER-1 and -2 (`Assets/Tests/Editor/AllocationMeter.cs`) are on the base. Beside this
packet: P-UI-HUD-GARBAGE-1 and the specs of 2026-11-23 touch no map file. After it, on the same files: P-UI-TABMAP-TEXT-2 (one
statement in `TacticalMapMarks.Present` and `TacticalMapMarks.CampTexts.cs`) and last P-UI-TEXT-FIT-1 (label statements in
`Initialize` and in `TacticalMapMarks.cs`). This packet merges first; leave their statements as they read.

## 3. Relevant legacy paths

- `packages/client/src/ui/minimap.ts`: skips a repaint when `sameAsLast()` finds its inputs equal; `minimapMarkers.ts:87-88` is
  the contact-drops-camp rule that `CampDropPx` already cites (unchanged by this packet). Not re-run in this packet: nothing a
  player sees changes.

## 4. New paths

- `UI/TacticalMapMarks.Cover.cs` (+ `.meta`), `UI/TacticalMapController.Tactics.cs` (+ `.meta`): each with `/// <summary>`;
  the second takes `PresentTactics` out of the 732-line controller; no file passes the 800-line gate. No library class (the loop
  is 15 lines over a `Rect`): no `sync-net`, no dotnet test.
- Tests: `Assets/Tests/Editor/MapCoverEditModeTests.cs`, `MapPaintEditModeTests.cs` (+ `.meta`).

```csharp
public sealed partial class TacticalMapMarks    // Cover.cs
{
    public int CoverTests { get; private set; }               // cumulative; a test reads differences
    public bool CampCovered(int campRow); public bool WardCovered(int wardRow);
    void ComputeCover(); void EnsureCover(); void ClearCover();   // Present; LayerChanged and mesh; SetMap(null)
    static Vector2 AtIn(Rect layer, int mapW, int mapH, Vector2 world);   // the expression of At; At calls it
}
```

## 5. Exclusive write scope

- `UI/TacticalMapMarks.cs`: `SetMap` null branch (+ `ClearCover();`), `LayerChanged` (+ `EnsureCover();` first), `Present`
  (+ `ComputeCover();`), `At`, `ContactNear` (deleted) and its six call sites, `RefreshBadgeTexts` (item 3), `DrawCamps` and
  `DrawBadges` (loops), `OnPopulateMesh` (+ `EnsureCover();`); new `TacticalMapMarks.Cover.cs` with `.meta`.
- `UI/TacticalMapController.cs`: `Tick`, the writers of item 5 (the Undo and Clear lambdas in `Initialize` included),
  `PresentLarge`, `PresentMini`, `RefreshMinimapVisibility` (show edge), `ApplyLayout` (one statement), `ApplyViewport`, `Bind`,
  `MapStateChanged`; `PresentTactics` leaves for new `TacticalMapController.Tactics.cs`.
- Not in scope: `TacticalMapMarks.CampTexts.cs` (no cover call), `.ZoneLabels.cs`, `TacticalMapSurface.cs`,
  `TacticalMapController.Sheet.cs`, `PerfMarks.cs`, `PerfRecorder.cs`, `Tools/*`, `MatchHud*.cs`. No recorder section, no key, no
  file outside the map's own files. Never edit `CardDisplayCatalog.cs` or `BakedContent.cs`.

## 6. Contracts and references

- Presentation only (R-AUTH): no rule, no wire, no `crates/`. Readability gate: a new non-test C# file has a `/// <summary>` before
  its first type and at most 800 lines; do not add lines to `View/ArenaView.cs` or `Presentation/SpriteActorPresenter.cs`.
- Motion and time: unscaled; nothing that allocates in a steady frame.
- `MatchHud` binds and unbinds sessions many times: never leave a handler on an old session.

## 7. Behaviour to preserve

- Nothing a player sees changes. The cover rule (`< CampDropPx` in layer units, first actor in sample order, self excluded) is the
  rule of `ContactNear` exactly.
- Tests that pin counts or the timer still hold, unedited (each verified by reading on the base):
  - `TabMapFlowEditModeTests.AClosedMapIsNotPresented` (each `Frame()` delivers a snapshot, so each is news): closed, the big
    surface's `LargePresentCount` stays flat over 60 frames and `MinimapPresentCount` rises; open, the reverse; on the planning
    screen the big count rises and the minimap's stays flat.
  - `TabMapFlowEditModeTests.TheMapIsPaintedOnTheFrameItOpens`: a contact delivered while closed shows on the Tab map, the contacts
    text and the hint on the frame it opens (`Frame(5, false)`, no snapshot).
  - `TabMapFlowEditModeTests.TheOwnMarkerFollowsTheDrawnPositionEveryFrame`: the own marker sits on the drawn point after
    `Frame(5, false)`.
  - `TabMapFlowEditModeTests` gesture tests (`HoldingRightOnTheBigMapSteers`, `AMinimapGestureIsReleasedOnceWhenTheMinimapHides`,
    `ReleaseTransientEndsAHeldBigMapGesture`): sends of the held right button at the 100 ms cadence.
  - all of `MinimapReturnEditModeTests` (`TheMinimapIsCurrentOnTheCallThatShowsIt`, `TheSameHoldsWhenTheTabMapCloses`,
    `TheOwnMarkerIsPlacedToo`, `AHiddenMinimapIsStillNotPresented`, `ShowingWithoutAMapDoesNothing`, ...): the show edge presents
    the minimap at once.
  - `TacticalMapEditModeTests` through `SetNextPaint` (`NewSnapshotInvalidatesPaintThrottleAndClearsBothObservedMapSurfaces`,
    `DisconnectAndRebindClearMarksAndRefreshSingleObservedContactRail`; `_nextPaint` stays a private double): a far-future timer is
    cleared by a new snapshot, a disconnect, a rebind.
  - `PlayerFlowsEditModeTests.DeliveredMapPointsReplaceWithoutActorFabricationOrNameDeduplication` (`:318`): a state event presents
    at once.
  - `ContactRailWordsEditModeTests` (the contacts text in game words and fixed order; `AnUnchangedSnapshotAllocatesNothing` calls
    `PresentTactics` by reflection: keep the name and the `void` signature).
  - `DeclutterEditModeTests` (camp marks dropped under a contact, `CampClocksShown`, `CampBuffGlyphsDrawn`, wards), `CampTextEditModeTests`,
    `ZoneNames*EditModeTests`, `TabMap*EditModeTests`, `TacticalMapEditModeTests.CampMarkersComeFromTheMapCampsAndTheDeliveredRowsAndAreDroppedUnderAContact`.
- Tests find `PresentTactics`, `_lastContactsSnapshot`, `_nextPaint`, `_counterGuide`, `ApplyLayout`, `PresentHeader` by name: keep
  names and `void` signatures; `RefreshOwnZone`, `PresentHeader`, `_title.text` stay before the gate.
- `QuestPresentation.ShowsOffer` (`MatchHud.QuestRewards.cs:588`): keep the call as it reads; if it is gone, stop.
- `client-unity/UnityProject/Tools/hud-seams.test.cjs` pins `MatchHud.*` members, not these files; no `MatchHud` file changes.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-MAP-PAINT-1 --base lane/ui-rework/base-3` passes.
- The two new classes and the classes of "Behaviour to preserve" are green.

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/MapCoverEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/MapCoverEditModeTests.cs.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/MapPaintEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/MapPaintEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-MAP-PAINT-1.md`

The `stub:` commit (not an oracle file; the implementer replaces it) adds `CoverTests`, `CampCovered`, `WardCovered`, empty private
`ComputeCover()` and `EnsureCover()` in `UI/TacticalMapMarks.Cover.cs`, and `ContactNear` counting into `CoverTests`.

## 9. Validation commands

- `bin/unity-class.sh <worktree> MapCoverEditModeTests,MapPaintEditModeTests,TabMapFlowEditModeTests,MinimapReturnEditModeTests,TacticalMapEditModeTests,ContactRailWordsEditModeTests,DeclutterEditModeTests,CampTextEditModeTests,PlayerFlowsEditModeTests`
- Plus `ZoneNames*EditModeTests`, `TabMap*EditModeTests`.
- No dotnet test, no `sync-net`.

## 10. Risk and suggested model tier

implementer-sonnet; reviewer. The risk is a surface that stays stale: a news source missed (a write of the timer left as a bare
`_nextPaint = 0`), `MarkNews()` after `RefreshDeliveredMarks()` (paints twice), or a signature that starts valid (the first
opening reads an empty quests text). Oracle tests 10, 14 and 15 are the guards.

## 11. Known traps

- `MarkNews()` before `RefreshDeliveredMarks()`, never after: after clears the fresh flag and paints twice.
- `ApplyLayout(true)` inside `PresentTactics` marks news: the surface's skip test runs after it, so that branch presents, and the
  next Tick runs the branch without presenting. A steady frame must not re-lay out (test 8, open).
- A ward site reads `WardCovered(loop index)`, not `CampWardRow.Index`.
- Never `Equals` two `StringBuilder`s (it compares capacity).
- A text label never active in the hierarchy measures a tenth of its size: no code here caches a size measured from one.

## Tests, as written (oracle)

`MapCoverEditModeTests` (rig R2: `DeclutterEditModeTests` copied; map 16 x 16, rect 512, so a camp's world x is its layer x):
1. `TheFlagDecidesEveryConsumer` (guard, minimap and Tab) 2. `TheRuleIsInLayerUnits` (guard) 3. `OneCoverTestPerCampAndActorPerPaint`
(red) 4. `AMeshRebuildMakesNoCoverTest` (red) 5. `ARectChangeRecomputesOnce` (guard) 6. guards `TheFlagsLeaveWithTheMatch`,
`ANullMapOrNoCampMakesNoCoverTest`, `ComputeCoverAllocatesNothingInASteadyPresent`.

`MapPaintEditModeTests` (rig R1 = `TabMapFlowEditModeTests` rig with a Welcome map carrying one camp at (256, 256); R3 =
`TabMapHeaderNoGarbageEditModeTests` build for one test): 7. `ASnapshotEveryFiftyMsPaintsOncePerSnapshot` 8.
`WithNoDeliveryItPaintsFourTimesASecond` 9. `TheHeartbeatIsAQuarterSecond` 10. news (`ACollectionsEventPaintsOnce`,
`AStateEventPaintsOnce`, `ATrackedQuestPaintsOnce`, `ANewMinimapSizePaintsOnTheNextTick`, `AChangedStagePaintsOnTheNextTick`,
`ARefusedPickShowsItsFeedbackOnTheNextTick`; guards `ARouteStopPaintsOnTheNextTick`, `ShowingTheRoutePaintsOnTheNextTick`,
`OpeningTheMapPaintsOnTheNextTick`) 11. `ClockTextsFollowTheServerTime` (guard) 12. `AGestureTicksOnAFrameWithoutAPaint` 13.
`UnchangedContactsAllocateNothingOnANewSnapshot` 14. guards (`AChangedContactChangesTheLineOnThatTick`,
`ABoundMapRebuildsTheContactsText`, `AQuestRowChangeChangesTheQuestsText`, `TheFirstOpeningReadsTheFallbackQuestLine`,
`AWeaponChangeReachesTheGuide`, `ADestroyedTerrainReachesTheVisibleSurface`) 15. `TheMinimapShowsItsCampsAfterTheFirstTick`
(guard, the real order).

## Notes from the oracle author

Run on the stub (`unity-class.sh MapCoverEditModeTests,MapPaintEditModeTests`): 37 tests, 19 green (the standing guards), 18 red,
each on an assertion about the missing behaviour:

- `MapCoverEditModeTests`: `OneCoverTestPerCampAndActorPerPaint` (expected 340 cover tests, the stub makes 920 = 46 * 20),
  `AMeshRebuildMakesNoCoverTest` (expected 0, 340).
- `MapPaintEditModeTests`: `ASnapshotEveryFiftyMsPaintsOncePerSnapshot` (both cases: expected 40, base 80),
  `WithNoDeliveryItPaintsFourTimesASecond` (both: 12 to 24, base 100), `TheHeartbeatIsAQuarterSecond` (both: a paint 240 ms
  after one), `ACollectionsEventPaintsOnce` (both: one present too many), `AStateEventPaintsOnce`, `ATrackedQuestPaintsOnce` (both),
  `ANewMinimapSizePaintsOnTheNextTick`, `AChangedStagePaintsOnTheNextTick`, `ARefusedPickShowsItsFeedbackOnTheNextTick` (the status
  still reads "Choose spawn + boon, then Ready..."), `AGestureTicksOnAFrameWithoutAPaint` (nothing sent 105 ms after the press),
  `UnchangedContactsAllocateNothingOnANewSnapshot` (4000 GC.Alloc samples in 200 calls).
- Green on purpose (guards): cover tests 1 (both cases), 2, 5 and the three of 6; paint tests `ARouteStopPaintsOnTheNextTick`,
  `ShowingTheRoutePaintsOnTheNextTick`, `OpeningTheMapPaintsOnTheNextTick`, `ClockTextsFollowTheServerTime`, the six guards of
  test 14 (`ADestroyedTerrainReachesTheVisibleSurface` in both cases) and `TheMinimapShowsItsCampsAfterTheFirstTick`.

**Scratch run (not committed).** One scratch copy of each new class, with every `Assert.That`, `Assert.DoesNotThrow`,
`AllocationMeter.AssertNone` and `StringAssert.StartsWith` replaced by a catch-and-collect helper that failed the test at its
TearDown with the list. Result: every entry in the list was one of the red assertions above (the `ARefusedPick...` test lists
both its spawn and its boon assertion; every other red test lists exactly its one), so every later line of each test ran without an
exception. The scratch also held four probes of values the fixed code will have to produce, all green on the base: after a
300 ms wait the status reads "That spawn is already confirmed." and then "That boon is already confirmed."; a gesture sends B on a
painted frame 155 ms after the press; `Snapshots.Sample` into a reused list, `WeaponCounterGuide.ActiveWeapon`, the legend
`GetComponentInChildren<TMP_Text>()` and `ShowcasePalette.GetFactionName` allocate nothing in 200 calls (so the allocation test
of `PresentTactics` can be met); the reflection write of `_lastContactsSnapshot` with a pre-boxed -1 allocates nothing (its control
passes), so `TacticsRebuilds` is NOT needed and the stub does not add it.

**Differences from the spec, and what the tests pin.**
- Premises are asserted inside `Step` of the cover class (the sampled actors are those delivered, in delivery order, within .05):
  they held on every step.
- The own actor (id 1) stands on camp 0 in `OneCoverTestPerCampAndActorPerPaint`; the codec accepts it and the sample returns it.
- `ARefusedPickShowsItsFeedbackOnTheNextTick` covers the spawn and boon picks; `Ready` is "by review" as in the spec.
- `AStateEventPaintsOnce` closes the transport; the session goes to Reconnecting (the Welcome carries a session token), the minimap
  stays shown, so the Tick after the event presents it on the base (+2) and must not on the fixed code (+1).
- The scratch copies, the helper and the probes are deleted; none is committed.

**Existing test red on the base, not on this packet.** `MinimapCaptureEditModeTests.PhysicalRightReleaseProjectsOverlayPositionAndFlushesChangedPointExactlyOnce`
fails on `lane/ui-rework/base-3` itself ("release flushes just the final changed point": expected 1, was 0): measured with the
two new test files and the stub removed from the tree. It is not touched by this packet and stays red whatever the implementer does;
the implementer's gate comparison should treat it as a base failure. The other 158 tests of the classes listed under "Behaviour to
preserve" (TabMap*, ZoneNames*, CampText, Declutter*, TacticalMapEditModeTests, MinimapReturn, ContactRailWords, PlayerFlows,
MapScreen) are green on the stub.

No `oracle-amend:` is needed: no existing expectation changes.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
