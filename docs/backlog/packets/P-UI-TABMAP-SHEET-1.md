# P-UI-TABMAP-SHEET-1: the Tab map is one sheet with a header, and only what belongs shows around it

- **Serves:** not in the plan: owner direction 2026-11-22, chat of the UI rework lane ("When tab is clicked map layout is weird.")
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `lane/ui-rework/base-2` (integration head `feature/unity-rework` plus the first wave: P-UI-HUD-SEAMS-1, P-UI-HUD-REGIONS-1, P-UI-MOTION-1 and its fix, P-UI-CAMP-LABEL-STABLE-1 and its fix, P-UI-TABMAP-FLOW-1)
- **D-rules touched:** D1 presentation: legacy's map screen is a square map flanked by two columns on a dim (`planning.css`, `mapRoster.css`); this keeps that and adds one backing panel and a full-width header, so propose a DEVIATIONS row in the hand-off · D2 n/a · D5/D6/D9 none
- **Worktree:** `/Users/nevinmckinnon/Documents/Projects/RH-ui-b`
- Evidence: `.agent-runs/lane-ui-rework/understand/tab-map.txt` (Q1 to Q3, defects 3, 4, 5, 11, 12) in the main checkout.

## 1. ID and outcome

P-UI-TABMAP-SHEET-1. When the player holds Tab (or opens the map in toggle mode) on a wide window the map is one panel: a backing "sheet" holds a full-width header (heading on the left, the HUD's phase line in the centre, the hint on the right) and under it the left rail, the square map with its legend, and the right rail. The "Close · Tab" button shows only in toggle mode. Around the sheet only what belongs is visible: the phase pill, the "Quests · J" launcher and the edge-threat glows are veiled, and the world marks (markers, indicators, quest carrier flags) are hidden; the three top-right buttons and the fps/ping pill stay. One method decides whether the panel shows, and the map's rect has one authority (the layout).

What is wrong today (all on the base):
- Tab switches on a full-screen dim at alpha .72 (`TacticalMapController.cs:263`) with three separate panels on it (`ContactsPage` `:153`, the map frame `:110`, `WeaponsPage` `:161`), a heading strip over the map only (`MapTitle` `:100`) and a "Close · Tab" button at the right end of that strip (`:105`).
- Parts of the HUD stay visible at 28 percent under the dim: the phase pill (`MatchHud.TopCentre.cs:25`), the "Quests · J" launcher (`MatchHud.PlayerFlows.cs:39`), the edge-threat glows (`MatchHud.Features.cs:120`); so do world marks (`ArenaView.cs:163-167`).
- Four places each decide whether the panel shows and each write `WorldLabelVisibility.Hidden`: `SetOpen` (`TacticalMapController.cs:397`), `ReadUiInput` (`:426`), `Tick` (`:441`), `ReleaseTransient` (`:686`).
- The map's rect has two authorities: `MapScreenLayout.Map` (`MapScreenLayout.cs:50`, frame minus 4 each side) is never used; the controller insets `PublicMap` by 4 (`TacticalMapController.cs:112`) and `TacticalMapSurface.Rebuild` adds an `AspectRatioFitter` once a map arrives (`TacticalMapSurface.cs:70`), which stretches it back over the whole frame.
- The hint line (`:521`, "Delivered terrain and contacts · release Tab to return · right-click to walk") is unwrapped and can run into the heading on a narrow frame.

P-UI-TABMAP-FLOW-1 is on the base and changed `UI/TacticalMapController.cs` (gesture release, paint on open, the own marker, the clock seam `SecondsClock` `:22`). Keep everything that packet did: `TabMapFlowEditModeTests` stays green without a change.

## 2. Dependencies

The base above. Nothing else. P-UI-QUEST-TRACKER-1 (same wave) leaves at once when the map opens; nothing here depends on it. P-UI-TOAST-LANES-1 amends `HudTextScaleEditModeTests` this wave: not this packet's concern.

## 3. Relevant legacy paths

- `packages/` legacy `planning.css`, `mapRoster.css`: the square map flanked by two columns on a dim (the structure this keeps). The sheet and header are an intended difference (DEVIATIONS row proposed).

## 4. New paths

- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.Sheet.cs` (+ `.meta`): the new code of the controller (the class becomes `partial`; `TacticalMapController.cs` is 711 lines, limit 800). Needs a `/// <summary>` before its first type.
- `client-unity/UnityProject/Assets/Scripts/View/ArenaView.MapOpen.cs` (+ `.meta`): **created by the stub commit with a no-op `SetWorldMarksVisible`**; the implementer fills it in (the file is not an oracle file).
- `client-unity/UnityProject/Tools/tab-map-sheet.test.cjs` (oracle).
- `client-unity/UnityProject/Assets/Tests/Editor/TabMapSheetEditModeTests.cs` (+ `.meta`) (oracle).

## 5. Exclusive write scope

- `client-unity/RoguehallaNet/Net/Ui/MapScreenLayout.cs` and its synced copy `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/MapScreenLayout.cs` (`bash client-unity/sync-net.sh`).
- `client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.cs`, new `TacticalMapController.Sheet.cs` with `.meta`, `UI/TacticalMapSurface.cs`.
- `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Visibility.cs`; `UI/MatchHud.Features.cs`: only the three `ApplyPhaseStrip(` calls of `TickMetrics` (`:191`, `:192`, `:197`).
- `client-unity/UnityProject/Assets/Scripts/View/ArenaView.MapOpen.cs` (stubbed, with `.meta`).
- `client-unity/UnityProject/Assets/Scripts/Presentation/WorldMarks.cs`, `WorldIndicators.cs`, `QuestCarrierFlags.cs`: only if the gate-at-draw route is needed for one of them (see 6.5).
- `client-unity/UnityProject/Tools/hud-shots.cjs`, `vision-shots.cjs`: the function `tabMapRect` only (`hud-shots.cjs:70`, `vision-shots.cjs:34`).
- Not in scope (other packets of this wave own them): `UI/TacticalMapMarks.cs`, `UI/UiMotion.cs`, `MatchHud.TopCentre.cs`, `MatchHud.Notices.cs`, `MatchHud.cs`, `MatchHud.ActionBar.cs`, `MatchHud.QuestRewards.cs`, `MatchHud.PlayerFlows.cs`, `HudLayout.cs`, `Net/MatchClient.cs`, `Tools/hud-seams.test.cjs`, `View/ArenaView.cs` (798 lines: add no line), `Presentation/SpriteActorPresenter.cs`.
- Amended expectations of existing tests: only where one is red on the finished oracle because of this packet's rect or look changes, each test method in its own `oracle-amend:` commit (none is known to be needed; see 7).

## 6. Contracts, numbers and API (what to build)

### 6.1 Layout: `MapScreenLayout.cs` (pure; stub already has the fields, all zero)

New constant `SheetPad = 12f` and two new fields `Sheet` (the backing panel) and `Header` (the heading strip). `Title` stays and equals `Header`. Tab mode, wide branch (stage width `StackBelow` (900) or more), replacing `MapScreenLayout.cs:86-99`:
```
col      = clamp(round(250 * ts), 190, 290)                                   (unchanged)
fixedH   = SheetPad*2 + TitleHeight + Gap + Gap + legend + FramePad*2         (120; was 96)
sideMax  = floor(min(h - ChromeClear - Pad - fixedH,
                     w - Pad*2 - SheetPad*2 - col*2 - Gap*2 - FramePad*2))
mapSide  = clamp(sideMax, MapMin, MapMax) ; frameW = mapSide + FramePad*2
innerW   = col*2 + Gap*2 + frameW ; innerH = TitleHeight + Gap + frameW + Gap + legend
Sheet    = (floor((w - innerW - SheetPad*2) / 2),
            max(ChromeClear, floor(ChromeClear + (h - ChromeClear - Pad - innerH - SheetPad*2) / 2)),
            innerW + SheetPad*2, innerH + SheetPad*2)
Header   = (Sheet.X + SheetPad, Sheet.Y + SheetPad, innerW, TitleHeight)       the full inner width
rowY     = Header.Bottom + Gap
Left     = (Header.X, rowY, col, frameW + Gap + legend)
Frame    = (Left.Right + Gap, rowY, frameW, frameW)
Legend   = (Frame.X, Frame.Bottom + Gap, frameW, legend)
Right    = (Frame.Right + Gap, rowY, col, frameW + Gap + legend)
Body     = (Header.X, Header.Y, innerW, innerH)
```
Text scale 1:

| Stage | Sheet | Header | Left | Frame | Legend | Right |
|---|---|---|---|---|---|---|
| 1422 x 800 | (121, 44, 1180, 744) | (133, 56, 1156, 34) | (133, 102, 250, 674) | (395, 102, 632, 632) | (395, 746, 632, 30) | (1039, 102, 250, 674) |
| 1280 x 960 | (12, 86, 1256, 820) | (24, 98, 1232, 34) | (24, 144, 250, 750) | (286, 144, 708, 708) | (286, 864, 708, 30) | (1006, 144, 250, 750) |

Stacked Tab layout (width under 900) and the planning layout keep every rect they have (`MapScreenLayout.cs:69-85`, `:102-132`); there `Sheet = Body` and `Header = Title`, and the sheet is not drawn.

### 6.2 The sheet and the header: controller (new code in `TacticalMapController.Sheet.cs`, class made `partial`)

- A new rect `MapSheet`, the **first child of the panel after `Scrim`** (path `TacticalMap/MapSheet`), placed at `L.Sheet` (`Put(rect, MapRect)`), with one surface: fill `UiTheme.With(theme.Surface, .97f)`, radius `theme.CornerRadiusLarge`, border `theme.BorderStrong`, not a raycast target. Active only in Tab mode on the wide layout (`!L.Stacked`).
- While the sheet shows, the rails `ContactsPage` (`:153`) and `WeaponsPage` (`:161`) lose their own frame and raised look: fill `theme.SurfaceRaised`, radius `theme.CornerRadius`, no border. In the stacked Tab layout and on the planning screen they look as today (`theme.Surface`, `CornerRadiusLarge`, `theme.Border`). `MapFrame` keeps its sunken surface and border everywhere.
- `MapTitle` is placed at `L.Header`. In it, in Tab mode:
  - `MapHeading` (left): as today ("TACTICAL MAP"; "LOBBY MAP" in the lobby phase).
  - `MapPhase` (new label, centred in the header, 13 units, `theme.TextPrimary`, path `TacticalMap/MapTitle/MapPhase`): the line the HUD's phase pill shows, handed over by the HUD: new `public void SetPhaseLine(string text)` on the controller (null or empty shows nothing; written only when it changes; the label's text always equals the line, whether or not the label is active). **The stub already has `SetPhaseLine` as an empty method: fill it.** `MatchHud` calls it wherever it applies a line to the phase pill: the three `ApplyPhaseStrip(...)` calls of `TickMetrics` (`MatchHud.Features.cs:191,192,197`) go through a new private helper in `UI/MatchHud.Visibility.cs` that calls `ApplyPhaseStrip` and then `_map?.SetPhaseLine(text)`.
  - `MapHint` (right): hold mode "Right-click to walk · release Tab to close"; toggle mode "Right-click to walk · Tab closes". (`TabMapFlowEditModeTests` requires "release Tab" in the hold hint and "Tab closes" without "release Tab" in the toggle hint, `TabMapFlowEditModeTests.cs:281,298,299`.)
  - `CloseMap`: shown in toggle mode only (`ToggleMode`); in hold mode releasing Tab is the way out. The hint's right inset is 92 while the Close button shows, else 0 (today `:103` fixes 92).
  - The hint's text, the Close button's visibility and the hint's inset are decided in `PresentTactics` (`:518`; it writes the hint today and a paint is forced when the map opens), NOT in `ApplyLayout`: `ApplyLayout` is cached on stage size, mode and text scale and would go stale when the player changes the toggle setting.
  - Room in the header, measured with the labels' preferred widths (`Wh` heading, `Wp` phase, `Wt` hint plus its inset, gap 16): the phase line shows only when `Header.W / 2 - Wp / 2 - 16 >= Wh`; the hint shows only when it clears what is left of it: with the phase showing `Header.W / 2 - Wp / 2 - 16 >= Wt`, without it `Wh + 16 + Wt <= Header.W`. Decided in `PresentTactics` too.
- The dim behind the sheet (`Scrim`, `:263`) is `UiTheme.With(theme.OutlineDark, .86f)` in Tab mode (was .72); planning stays .84.

### 6.3 One place decides the panel

One private method (`ShowPanel(bool wanted)` or similar) replaces the four `SetActive` sites and is the only writer of `WorldLabelVisibility.Hidden`. `wanted` keeps today's rule at each site (`SetOpen`: `IsPlanning || _mapOpen`; `ReadUiInput`: same; `Tick`: `_bound.LastResult == null && (IsPlanning || _mapOpen)`; `ReleaseTransient`: false). The map opens and closes at once, as today: a held map answers the key on the frame it is pressed, and the HUD around it hides on that same frame. `IsOpen` stays `_panel.gameObject.activeSelf`. No `HudFade`, no `CanvasGroup` write on the panel (its group belongs to `BlockPlanningMapInput` in `MatchHud.PlayerFlows.cs:110`).

### 6.4 The map's rect has one authority

- `TacticalMapSurface.LayoutOwnsRect` (public bool, default false; **the stub already has it**). When true, `Rebuild` (`TacticalMapSurface.cs:70`) adds no `AspectRatioFitter`. The controller sets it on the large surface right after it adds the component (`TacticalMapController.cs:113`), before the first `SetMap`. The minimap surface is unchanged.
- The controller places `PublicMap` from `L.Map` (frame minus `FramePad` on each side) in `ApplyLayout`. The live `PublicMap` rect equals `L.Map` at every stage, also after a map has been delivered.

### 6.5 What shows around the map: `UI/MatchHud.Visibility.cs` and `View/ArenaView.MapOpen.cs`

While `_map.IsOpen` (Tab map or planning screen) the chrome stage stays on screen (the three top-right buttons and the fps/ping pill). The connection banner is drawn under the map's dim and shows faintly: accepted, unchanged. `RefreshWorldHudVisibility` (`MatchHud.Visibility.cs:33`, runs every frame from `TickFeatures`) gains one call to a new method that applies this list on a change of `_map.IsOpen`:
- **Veiled** (a `CanvasGroup` on the element: alpha 0, `blocksRaycasts` false, `interactable` false while the map is open; alpha 1 and both true otherwise; the element's own `SetActive` logic is left alone): the phase pill (`_phasePill`), the journal launcher (`_journalButton`), the edge-threat view (`_edgeThreats.Root`).
- **World marks hidden:** `ArenaView.SetWorldMarksVisible(bool)` (**stubbed, no-op**), in `View/ArenaView.MapOpen.cs` (add no line to `ArenaView.cs`), hides the roots of `WorldMarks` ("World markers"), `WorldIndicators` ("World indicators") and `QuestCarrierFlags` ("Quest carrier flags") by `SetActive`. Find the roots with `transform.Find` by those names (the helper `Child()` at `ArenaView.cs:215` creates a new object when none exists). Read the three presenters first (`Presentation/WorldMarks.cs`, `WorldIndicators.cs`, `QuestCarrierFlags.cs`): if one of them misbehaves with an inactive root (it reads `activeInHierarchy`, or loses pooled state), gate that one on `WorldLabelVisibility.Hidden` at its draw entry instead, as `CampRespawnLabels.DrawRows` does, and say so in the hand-off. `MatchHud` reaches the view through `_client.Arena`. (Read by the oracle author: none of the three reads `activeInHierarchy`; `QuestCarrierFlags` toggles `SpriteRenderer.enabled` per flag, which an inactive root does not disturb.)
- Already hidden today and unchanged: plate, champion panel, action row, minimap, toasts, quest box, hunt panel (through `WorldHudVisible` / `PanelHudVisible`, `MatchHud.Features.cs:38`, `MatchHud.Visibility.cs:12`), world names, camp rows and badges (`WorldLabelVisibility.Hidden`).

### 6.6 The capture tools' copy of the map maths

`hud-shots.cjs:70` and `vision-shots.cjs:34` each hold `function tabMapRect(w, h, s)`, a JS copy of the wide Tab layout that the browser proofs use to click on the Tab map. It becomes (window `w` x `h` CSS px, stage scale `s`; the result stays `{ x, y, size }` in CSS px):
```
uiW = w / s, uiH = h / s, col = 250
fixedH = 12*2 + 34 + 12 + 12 + 30 + 4*2                                          // 120
side   = min(820, max(240, floor(min(uiH - 44 - 12 - fixedH, uiW - 24 - 24 - col*2 - 24 - 8))))
frameW = side + 8, innerW = col*2 + 24 + frameW, innerH = 34 + 12 + frameW + 12 + 30
sheetX = floor((uiW - innerW - 24) / 2)
sheetY = max(44, floor(44 + (uiH - 44 - 12 - innerH - 24) / 2))
x = (sheetX + 12 + col + 12 + 4) * s, y = (sheetY + 12 + 34 + 12 + 4) * s, size = side * s
```
Nothing else in those two files changes (their `minimapRect` copies stay).

## 7. Behaviour to preserve, and the existing tests that pin this code

Existing tests that touch this code (read; each is green on the stub, run before the oracle commit):
- `TabMapFlowEditModeTests` (`Assets/Tests/Editor`): hint contains "release Tab" in hold mode (`:281`) and "Tab closes" without "release Tab" in toggle mode (`:298-299`); finds `TacticalMap/MapFrame/PublicMap`, `TacticalMap/MapTitle/MapHint`, `MapHeading`, `RouteSummary`, `TacticalInformation/ContactsPage/...` by path (`:105`, `:413-416`); gesture release, paint on open, own marker. Must stay green with no change; the new paths keep these names.
- `TabMapVisualFix3EditModeTests`: the weapon-counters footer ends above the "QUESTS & TRADERS" heading at 1280x720 and 1920x1080 (measured in the rail's local space); a cleared camp's clock stays clear of the own ring. The rail height changes (24 shorter): the footer test is the one to watch.
- `DeclutterLegendEditModeTests`: the Tab key lists "Camp buff" and "Elite camp" and `InViewHeading` starts at or below the last key row; passes through `Assert.Pass` when the compact layout hides the key.
- `TacticalMapEditModeTests`: contacts text, delivered marks, membership removal, projection (paths `TacticalMap/MapFrame/PublicMap/...`, `ContactsPage/DeliveredContacts`). 
- `MinimapCaptureEditModeTests`: `LargeSurface()` is `TacticalMap/MapFrame/PublicMap`; pointer positions are computed on screen from the surface rect (it may read the live rect and so shift with the new maths).
- `HudVisualFix4EditModeTests` (`:295-313`): `WorldLabelVisibility.Hidden` is true while the map is open, false after it closes, and a map closed under the controller does not leave names hidden (the four sites, now one).
- `TrainingHudVisibilityEditModeTests`: no ordering of drawer, Tab map and wish leaves an HUD panel hidden.
- `HudLegacyLayoutEditModeTests` builds a `TacticalMapController` but is a layout test of the HUD pieces.
- `PlayerFlowsEditModeTests` `PlanningQuestReviewPreservesMapChoicesAndFocus...` (`:393`): the planning map's `CanvasGroup` is made non-interactable under the journal and restored.
- `MapScreenTests.cs` (dotnet): `MapScreenLayoutTests` (`:134`): `TabLayoutFitsTheStageWithoutOverlapAtEverySize` (Title, Frame, Legend, Left, Right inside the stage and not overlapping), `MapIsAnExactSquareInsideItsFrame...`, `WideTabBodyIsCentredAndMapGrowsWithTheStage` (Body centred within 1.01, `Body.Y >= 44`, `Body.Bottom <= h - 12`, map grows, 240..820), `TabColumnsKeepLegacyWidthAndScaleWithMapTextSize` (250 wide, 290 at 1.5), planning, stacked, degenerate. Verified with a reference implementation of 6.1: all stay green, no amendment.
- Node: `Tools/hud-seams.test.cjs` pins `MatchHud` member names in files; this packet adds a private helper in `MatchHud.Visibility.cs` and removes no member. Run `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` if a `MatchHud` member moves.

Any conflict between the above and the new oracle goes to the coordinator, not to an edit of an existing test. No existing test was found to conflict; no `oracle-amend:` commit exists.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-TABMAP-SHEET-1 --base lane/ui-rework/base-2` prints `GATE PASS`.
- All new tests below green; the classes of section 7 green.

## Oracle files (read-only for implementers)
- `client-unity/RoguehallaNet.Tests/MapScreenTests.cs` (new tests `TheTabSheetWrapsHeaderMapAndRails`, `TheSheetNumbersAtTwoStages`, `StackedAndPlanningKeepTheirRects` in class `MapScreenLayoutTests`; existing tests unchanged)
- `client-unity/UnityProject/Tools/tab-map-sheet.test.cjs`
- `client-unity/UnityProject/Assets/Tests/Editor/TabMapSheetEditModeTests.cs` (tests: `TheLiveRectsEqualTheLayout` x3 windows, `TheSheetIsBehindEverythingAndOnlyOnTheWideTabLayout` x3, `TheSheetIsHiddenOnTheStackedLayoutAndOnThePlanningScreen`, `TheHeaderCarriesTitlePhaseAndHint`, `TheHeaderDropsWhatDoesNotFit`, `TheRailsAreUnframedOnlyOnTheSheet`, `TheDimIsDarkerOnTheTabMapAndPlanningKeepsItsOwn`, `TheHudHandsItsPhaseLineToTheMap`, `OnlyTheChromeSurvivesTheMap`, `WorldMarksAreHiddenWhileTheMapIsOpen`, `PlanningStillBlocksTheMapUnderTheJournal`)
- `client-unity/UnityProject/Assets/Tests/Editor/TabMapSheetEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-TABMAP-SHEET-1.md`

Stub commit (not oracle; the implementer edits these): `MapScreenLayout.cs` and its synced copy (`SheetPad`, `Sheet` and `Header` as zero rects), `UI/TacticalMapSurface.cs` (`LayoutOwnsRect`), `UI/TacticalMapController.cs` (`SetPhaseLine` empty), `View/ArenaView.MapOpen.cs` + `.meta` (`SetWorldMarksVisible` empty).

## Red state on the stub (run by the oracle author)

dotnet (`MapScreenLayoutTests`): `TheTabSheetWrapsHeaderMapAndRails` red on "the sheet starts below the chrome" (Sheet is zero); `TheSheetNumbersAtTwoStages` red on "1422x800 Sheet x" (expected 121, was 0); `StackedAndPlanningKeepTheirRects` red on "800x700 Sheet is Body x" (the Title/Frame/Legend/Left/Right literals before it pass). The seven existing tests of the class are green. With a reference implementation of 6.1 all ten are green.

node `tab-map-sheet.test.cjs`: both `tabMapRect` tests red on "1920x1080 x: 522.45 is not 538.65"; the controller test red on "exactly one assignment ... actual 4".

Unity `TabMapSheetEditModeTests` (15 cases, 14 red, 1 green guard):
- `TheLiveRectsEqualTheLayout` (3): "the panel has MapSheet" expected not null.
- `TheSheetIsBehindEverythingAndOnlyOnTheWideTabLayout` (3) and `TheSheetIsHiddenOnTheStackedLayoutAndOnThePlanningScreen`: second child is "MapTitle", expected "MapSheet".
- `TheHeaderCarriesTitlePhaseAndHint`: hold-mode hint is the old 76 character text, expected "Right-click to walk · release Tab to close".
- `TheHeaderDropsWhatDoesNotFit`: "the panel has MapTitle/MapPhase" expected not null.
- `TheRailsAreUnframedOnlyOnTheSheet`: ContactsPage on the sheet fill is `Surface` (20,27,37), expected `SurfaceRaised` (32,42,54). Its stacked and planning parts (the base look) pass on the stub (checked by running them alone).
- `TheDimIsDarkerOnTheTabMapAndPlanningKeepsItsOwn`: Tab dim .72, expected .86 (the planning .84 part passes on the stub).
- `TheHudHandsItsPhaseLineToTheMap`: "the map header has a phase label" expected not null.
- `OnlyTheChromeSurvivesTheMap`: "phase pill is veiled by a CanvasGroup" expected not null (its chrome-untouched check runs first and passes on the stub).
- `WorldMarksAreHiddenWhileTheMapIsOpen`: "World markers test mark is hidden while the map is open" expected False.
- `PlanningStillBlocksTheMapUnderTheJournal`: GREEN on the stub, as intended (a guard).

Existing classes run on the stub (`TabMapFlow`, `TabMapVisualFix3`, `TacticalMap`, `MinimapCapture`, `HudVisualFix4`, `DeclutterLegend`, `MapScreen`, `TrainingHudVisibility`, `PlayerFlows`, `HudLegacyLayout`): 116 of 117 green. **One is red on the base too, before this packet: `MinimapCaptureEditModeTests.PhysicalRightReleaseProjectsOverlayPositionAndFlushesChangedPointExactlyOnce` ("release flushes just the final changed point": expected 1, was 0)**. Confirmed by stashing the stub and running that class alone on `lane/ui-rework/base-2`. It is not this packet's to fix; the implementer must not count it as caused by the packet, and the gate's full run will show it if it is real on the base.

## 9. Validation commands

- `bash client-unity/sync-net.sh` after editing the library; `dotnet test client-unity/RoguehallaNet.Tests --filter MapScreenLayoutTests`.
- `node --test client-unity/UnityProject/Tools/tab-map-sheet.test.cjs`.
- Unity: `unity-class.sh <worktree> TabMapSheetEditModeTests,TabMapFlowEditModeTests,TabMapVisualFix3EditModeTests,TacticalMapEditModeTests,MinimapCaptureEditModeTests,HudVisualFix4EditModeTests,DeclutterLegendEditModeTests`.

## 10. Risk and suggested model tier

Sonnet: presentation only, one pure layout function with exact numbers, one partial class. Hard parts: the cached `ApplyLayout` versus mode-dependent text (decide in `PresentTactics`), the `AspectRatioFitter` timing, and keeping `IsOpen` true on the frame of `SetOpen`.

## 11. Known traps

- The panel `TacticalMap` is stage-sized and stage-scaled: every number is in HUD units from the panel's top-left (`Put(rect, MapRect)`).
- `IsOpen` is the panel's `activeSelf` and many tests assert it on the frame of `SetOpen`: `ShowPanel` must activate and deactivate the panel in the same call.
- The route controls (`RouteSummary`, `UndoRoute`, `ClearRoute`, `:295-298`) are placed from `L.Left`: they follow the shorter column without a change; check that they still sit inside it at 1422 x 800 (column 674 high).
- `compact = lh < 430` (`:275`) still decides whether the MAP KEY shows. The left column is 24 shorter than before at the same stage: the key may now hide at a stage where it showed. Accepted; do not change the threshold.
- A veil is a `CanvasGroup` alpha, never `SetActive`: `SyncTopCentre` and `RefreshJournalLauncher` keep deciding whether those elements are active, and must not be fought.
- `UiTheme.With` is the way to an alpha variant of a theme colour (a test checks that HUD graphics use theme colours). Text through `HudUi.Label`; call `MythicTextScale.Refit()` after resizing a label's box.
- New time reads in the controller use `Now` (`SecondsClock`). The test assembly is separate and has no `InternalsVisibleTo`: a seam a test needs must be public.
- `MapScreenLayout` is a library file: edit `client-unity/RoguehallaNet/`, run sync-net, commit both copies.
- Capture tools copy layout maths: seven scripts under `Tools/` copy `HudLayout.MinimapSize`; only `tabMapRect` of two of them changes here.
- TearDown of the tests restores `WorldLabelVisibility.Hidden`, `TacticalMapController.SecondsClock`, `UiMotion.ReducedOverride` and `UiDisplayPreferences` toggles.

## Notes from the oracle author

- The spec's "`Stages` list gains 1280 x 960 and 1866 x 800" is done as a separate list `SheetStages` in the new test, so the existing tests keep their inputs (no existing test changes).
- Oracle test 3 (`StackedAndPlanningKeepTheirRects`) pins literals computed by hand from the base formulas and confirmed green on a stub that returns `Sheet = Body`, `Header = Title`.
- Test 7 (`WorldMarksAreHiddenWhileTheMapIsOpen`): a delivered quest giver, chest ring and carrier flag need a whole world state; the test instead puts one active enabled `SpriteRenderer` under each of the three root objects by hand (found by name) and asserts on those. It pins the behaviour of `SetWorldMarksVisible` (roots hidden/shown) and not the presenters. It runs `Awake` by reflection when the roots do not exist.
- Test 5 reads the phase line through `TickMetrics` (private, reflection) with the lobby phase and no arena ("FORMING MATCH"), because the metrics tick is throttled by the real clock.
- The header room rule (test 4) restates the packet's rule with the labels' preferred widths, as the spec asks. It checks the label that shows, not whether an empty phase label is active (an empty `MapPhase` may stay active). At 640x480 the toggle-mode hint with no phase line must be hidden (Wh + 16 + Wt over 392); a long phase line must be hidden.
- Test 8 reads "no border" as: no child named `Frame` on the rail (what `HudUi.Surface` draws when it is given a border); the implementer re-surfaces the rails with `HudUi.Surface(rail, theme.SurfaceRaised, theme.CornerRadius, null, true, false)`.
- A dim test (`TheDimIsDarkerOnTheTabMapAndPlanningKeepsItsOwn`) is added for the .86 / .84 alphas of 6.2.
- Test 9 (`PlanningStillBlocksTheMapUnderTheJournal`) is green before and after: it guards `BlockPlanningMapInput` / `RestorePlanningMapInput` (`MatchHud.PlayerFlows.cs:110,125`) against this packet's panel changes.
