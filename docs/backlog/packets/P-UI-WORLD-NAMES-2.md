# P-UI-WORLD-NAMES-2: names above actors keep clear of each other and of bars, and glide instead of hopping

- **Serves:** not in ORDER_OF_OPERATIONS (UI rework lane). Owner direction 2026-11-22 in the lane's chat: "Currently
  there is a unity bug where the stat above a camp shimmers and glitches as a camp is moving around it" and "Please do
  a UI optimization sweep"; Web round 1 (2026-11-23): two actor names run together (the world-label half is
  P-UI-WORLD-LABELS-1). Second of two packets: P-UI-WORLD-NAMES-1 built the library and moved the name pass.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** the lane branch `lane/ui-rework/base-3` (head bf3574e47), worktree
  `/Users/nevinmckinnon/Documents/Projects/RH-ui-x`. A packet branch stays linear: never merge into `packet/P-UI-WORLD-NAMES-2`.
- **D-rules touched:** D1: legacy draws every name at a fixed point with no avoidance; this differs, so ONE
  `DEVIATIONS.md` row is proposed in the hand-off (text at the end of this file; the owner signs it). DEV-024 (mob
  name groups, signed 2026-11-13, `docs/OWNER_DECISIONS.md` row "DEV-024 mob name groups") stays: a pack still shows
  one name. D2 n/a. D5/D6 none. D9: drawing only, no key bound.

Paths: `Pres/` = `client-unity/UnityProject/Assets/Scripts/Presentation`, `Lib/` = `client-unity/RoguehallaNet/Net/Ui`
(Unity copy `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui`), `Tests/` =
`client-unity/UnityProject/Assets/Tests/Editor`. Line numbers are those of the lane base (`lane/ui-rework/base-3`).

## 1. ID and outcome

Presentation only. Every drawn name is one rigid BOX that keeps clear of the boxes before it and of health bars, and
glides to its place instead of hopping.

**What is wrong today** (all in `Pres/SpriteActorPresenter.Names.cs`, the name pass P-UI-WORLD-NAMES-1 moved verbatim):
four movers with no memory: `OverheadIndicators.Update` (resets the name every frame), `MergeMobNames` (`:85`),
`StackFighterNames` (`:145`, orders by DISTANCE to the local fighter, so the order changes as the player moves) and
`ClearOfBars` (`:63`, elites only, name only). Lone mobs and training dummies are in no rule ("Chain Warden" and
"Gaoler" touch, picture 40); `LiftName` (`OverheadIndicators.cs:165`) moves the name alone, so it lands on its own
star and marks.

**The library already on the base** (P-UI-WORLD-NAMES-1, `Lib/OverheadBoxLayout.cs`, `Lib/OverheadCluster.cs`; not in
this packet's scope, relied on as is): boxes are visited by ascending `(Class, Key)`; each rises only as far as needed
to clear the earlier boxes (at their Target) and, if `avoidFixed`, the fixed rectangles (`Place`, `:150`; `Raise`,
`:176`); a `Solve` with dt <= 0, NaN or > `ForgetSeconds` clears the memory table, solves every box as first sight
(`LabelLift.Start`) and records no entry; a good dt records an entry for every box and steps it with
`LabelLift.Step` (rise `RiseUnitsPerSecond` 8/s, fall `FallUnitsPerSecond` 3/s, hold `HoldSeconds` .45 s, deadband
.03). Constants: `Gap` .10, `PadEnter` .16, `PadKeep` .40, `MaxLift` 2.5, `ForgetSeconds` 1; classes `ClassLocal` 10,
`ClassFighter` 20, `ClassElite` 30, `ClassMob` 40; keys `SerialKey`, `CampKey` (`CampKeyBase` 1<<32), `FighterKey`
(`FighterKeyBase` 1<<33). `OverheadCluster.AddParts(layout, stack, eliteStar, worldPerLocal, nameWidth, nameHeight)`
turns an overhead stack into parts and holds `EliteStarSize` .3, `EliteStarPlate` 1.4, `EliteStarRise` .02.
`Art.LayoutKey` (`Pres/SpriteActorPresenter.ActorArt.cs:48`) is given once per art in `Present`'s non-continue branch
(`SpriteActorPresenter.cs`, `a.LayoutKey = ++_layoutSerial;`) and never touched by `TryContinue`.

**What is true when this packet lands**, seven rules:

1. **Boxes.** Once per presented frame, in `End`, every drawn name is a box of the presenter's one
   `OverheadBoxLayout`: an actor stamped this frame (`art.Stamp == _stamp`), not Dead, with an `Ind`,
   `art.Root.activeInHierarchy` (a hidden fighter pushes nobody; a label never active in the hierarchy measures at a
   tenth of its size: see Known traps) and `Ind.NameActive` after the pack merge: the local fighter, fighters, elites,
   a pack's one name, lone mobs, dummies. Origin: `Ind.NameBasePosition`. Parts: `Ind.AddClusterParts(layout)` hands
   `_stack`, the elite flag `Update` last saw, `_name.transform.lossyScale.y`, `NameWorldWidth` and `NameWorldHeight`
   to `OverheadCluster.AddParts`; a pack's box is its name alone at the pack anchor. `avoidFixed` is false for the
   local fighter's box only.
2. **Fixed rectangles.** `HpBack.bounds` of every stamped, not Dead actor whose `Root` is `activeInHierarchy` and
   `HpBack` is enabled. A box's own bar is among them and never hits it (a mob's name ends about .26 above its own
   bar by estimate, more than `Gap`; oracle test 7 checks it, and
   `NamePackEditModeTests.AHurtMobsNameSitsAboveItsOwnBarAndNeverOverlapsIt` pins the old rule). The local fighter's
   box ignores all bars: a mob's bar never lifts the owner's plate.
3. **Class and key.** Class: `ClassLocal` for the local fighter, `ClassFighter` for other `!Mob` actors, `ClassElite`
   for `Mob && Elite`, `ClassMob` for every other mob name (pack, lone mob, dummy). Key: fighters (the local one
   included) `OverheadBoxLayout.FighterKey(pair.Key)`, elites and dummies `SerialKey(Art.LayoutKey)`. Every other mob
   name keys by `Art.LayoutNameKey`, which the merge writes: `CampKey(Camp)` when the item's `Camp` > 0, whatever the
   pack size (a pack that falls to one member keeps its memory), else `SerialKey` of the lowest `LayoutKey` among the
   pack's members (a lone camp-less mob: its own); a `long[] _packKey` beside `_packSize`, resized with it, holds that
   minimum. The merge also writes `public bool LayoutPack` on EVERY stamped named mob on EVERY `End`: true on the
   carrier of a pack of two or more, false on every other, so no flag outlives its frame; it tells `LayoutNames` a box
   is the name alone.
4. **Apply.** `art.Ind.LiftCluster(LayoutLift)` moves every ENABLED element of the cluster (all that `Update` places
   from the stack, plus the elite star and plate; `OverheadExtras.Shift(float dy)` does the pips, top pip, stat marks,
   star and plate) up by `lift - _shift` in world space and sets `_shift`. `Update` sets `_shift` to 0 right after its
   last cluster write (the `_extras.EliteStar` call, `OverheadIndicators.cs:338`), `HideAll` (`:223`) resets it, and
   `MoveName(x, y)` (`:163`) takes a base point and puts the name at `y + _shift`. `NameBasePosition` is the name's
   position minus `_shift`: the merge, the box origin and `FinishNames` read it, never `NameWorldPosition`. Rings,
   status glyphs, the bubble, the cue, bars and disabled elements never move. `LiftName`, `ClearOfBars`,
   `BarClearance`, `StackFighterNames` and their calls go (`ClearOfBars` is also called in
   `Pres/SpriteActorPresenter.Overheads.cs:77`, `PlaceSignatureCues`), with what only they used: `_stackItems`,
   `_lift`, `_stackIds`, `_packNameIds` (its `Clear` and `Add` in the merge) and the merge's `fighters` counter.
   `_nameRects` stays: `LayoutNames` fills it. `NameStack` stays in the library, unused by the presenter. `EliteStar`
   (`OverheadExtras.cs:236`) reads `OverheadCluster.EliteStarSize` and `EliteStarPlate` instead of `.3f` and
   `size * 1.4f`; `Update` reads `EliteStarRise` at its `+ .02f` (`OverheadIndicators.cs:338`).
5. **`End(dt)`.** `public void End(float dt)` is the whole existing `End()` body: `_frameLaunches.Clear()`,
   `MergeMobNames()` (which ends with `FinishNames`), the layout branch, then the unchanged eviction loop (`_remove`,
   `Evict`) last on every path, Hidden and already solved included. The merge no longer clears `_nameRects`: the
   layout and `ClearNameLayout` own the list. The branch: (a) `WorldLabelVisibility.Hidden`: the merge's hidden
   branch also calls `ClearNameLayout()` (memory, stored rectangles, every `LayoutStamp` and `_solvedStamp` set to
   -1), no solve; (b) `_solvedStamp != _stamp`: the private `void LayoutNames(float dt)`, then `_solvedStamp =
   _stamp`; (c) a stamp already solved (a second `End`, no `Begin`): the private `void ApplyLifts()` only, which
   re-applies `LiftCluster` from each actor's stored `LayoutLift` and skips `Solve`. The merge and `FinishNames` run
   every time. `_stamp` starts at 0, so `_solvedStamp` and `Art.LayoutStamp` (`= -1`) start at -1: a first `End` with
   no `Begin`, and an art never laid out, must not read as solved. `LayoutNames` builds the boxes, calls
   `Solve(dt, UiMotion.Reduced)` once, stores per box actor `LayoutStamp = _stamp`, `LayoutLift`, `LayoutTarget` and
   the camp rectangles, then `ApplyLifts`; no stamp guard here, nothing allocated in a steady frame.
   `TryNameLift(uint id, out float lift, out float target)` is true when the actor's `LayoutStamp == _stamp`. All
   three P-UI-MAP-OPEN-WORLD-1 gates stay (the merge's `Hidden` branch, `Present`'s bar-loop bound, the top of
   `Update`).
6. **Time.** `public void End()` only computes dt and calls `End(dt)`. It is called by `View/ArenaView.cs:282`
   (`_actors.End()`) and `Presentation/ShowcaseEnvironmentStudy.cs:140`; neither changes. dt is 0 while a bool
   `_haveEndClock` is false (or `_endClock` starts NaN and a NaN gives 0), else `Now() - _endClock`; then `_endClock
   = Now()` and the bool is set: the first `End` gives dt 0. `Now()` is the private static in
   `Pres/SpriteActorPresenter.Fade.cs:38` (`FadeClock` or `Time.unscaledTime`), never `Time.unscaledDeltaTime`.
   **The frozen clock, measured by the oracle author** (scratch EditMode test, uncommitted, Unity 6000.0, EditMode,
   2026-11-23): `Time.unscaledTime` read the same value on two reads with 3,000,000 square roots and a GameObject
   create and destroy between (0.9344762 and 0.9344762), and `Application.isPlaying` is false. `Time.unscaledDeltaTime`
   is NOT 0 (it read 0.9252325 both times: a stale constant, not a delta), which is why the clock difference of
   `Now()` is used and `unscaledDeltaTime` is never read. Both are constant, so `End()` is as written above, with no
   `isPlaying` guard: existing tests that call `End()` get dt 0 (first sight, every frame), and one that sets
   `FadeClock` gets its difference.
7. **Camp seam.** `FighterNameRects(List<Rect>)` keeps its signature (`View/ArenaView.cs:164` assigns it as a method
   group) and only appends what `LayoutNames` stored: EVERY part of EVERY box at its TARGET
   (`OverheadBoxLayout.PartRect(i, part, true, ...)`), grown `NameRectPad` on both sides in x, lone mobs and elite
   stars included. `public const float NameRectPad = .03f;` in `Names.cs` is the literal of today's
   `FighterNameRects` (`Names.cs:42`); the stub carries it. `FighterNameRects` mutates and clears nothing.

## 2. Dependencies

P-UI-WORLD-NAMES-1 (merged on the lane base): the library and the `Names.cs` move. P-UI-ALLOC-METER-1/2
(`Tests/AllocationMeter.cs`, on the base). Nothing else.

## 3. Relevant legacy paths

Legacy has no rule that places a label against another (`.agent-runs/lane-ui-rework/understand/design-world-names-legacy.md`
L1, L2):
- `packages/client/src/render/PlayerView.ts:65` (`LABEL_Y = -BODY_LIFT - FIGURE_RADIUS - 18`) and `:171` (the name is a
  `Text` child of the player container at `(0, LABEL_Y)`, 11 px, stroke 2).
- `packages/client/src/render/MobView.ts:22` (the same `LABEL_Y`) and `:108` (the mob's `Text` child), `MobView.ts:247`
  (12 px elite, 11 px otherwise; read by `OverheadIndicators.Update`).
- Verified with `rg`/`sed` on 2026-11-23. This differs from legacy on purpose; see the proposed DEVIATIONS row.

## 4. New paths

- `Tests/WorldNamesRig.cs`, `Tests/WorldNamesEditModeTests.cs`, `Tests/WorldNamesTimeEditModeTests.cs`, each with its
  `.meta` (oracle; each under 800 lines).
- No new product file: the rule lives in `Names.cs`.

## 5. Exclusive write scope

Implementer, under `Pres/`:
- `SpriteActorPresenter.Names.cs` (to about 350 lines; it holds the rule; the stub already adds `End(float)`,
  `NameRectPad`, `TryNameLift`, `LayoutNames`).
- `SpriteActorPresenter.ActorArt.cs` (`public long LayoutNameKey; public bool LayoutPack; public int LayoutStamp = -1;
  public float LayoutLift, LayoutTarget;`).
- `SpriteActorPresenter.Overheads.cs` (delete the `ClearOfBars(art)` line, `:77`).
- `OverheadIndicators.cs`, `OverheadExtras.cs`.
- `SpriteActorPresenter.cs` is NOT touched.

Not in scope (a behaviour this packet needs and these lack, or a wrong number, is reported `blocked`, not added): the
library files `OverheadBoxLayout.cs`, `OverheadCluster.cs`; `View/ArenaView*.cs`, `CampRespawnLabels.cs`,
`WorldIndicators.cs`, `WorldMarks.cs`, `NameStack.cs`, `NameMerge.cs`, `LabelLift.cs`, `OverheadStack.cs`,
`UI/UiMotion.cs`; every oracle file; every file COMMON protects (`CardDisplayCatalog.cs`, `BakedContent.cs`, the wire,
`crates/`, `packages/`, `docs/DEVIATIONS.md` ...). Readability gate: `ArenaView.cs` (798) and
`SpriteActorPresenter.cs` (772) gain no line; a new non-test C# file has a `/// <summary>` and at most 800 lines.

## 6. Contracts and references

R-AUTH / D9: no game rule, no key. DEV-024 (pack names) kept. `UiMotion.Reduced` read through `UiMotion` only.
Unscaled time through `Now()` (`FadeClock` seam). Nothing allocates in a steady frame (test 14).

## 7. Behaviour to preserve

- Renderer and object names (pinned by many tests and `ContrastLabStage.ClassOf`): change none.
- The three P-UI-MAP-OPEN-WORLD-1 `WorldLabelVisibility.Hidden` gates.
- A pack still shows exactly one name, anchored by `NameMerge.PackNames`; hover and lock still win.
- Eviction still runs last on every `End` path.
- `FighterNameRects` signature (a method group in `ArenaView.cs:164`).
- `PlaceSignatureCues` still reads lifted bounds (`StormInkSignatureCueEditModeTests`).

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-UI-WORLD-NAMES-2 --base lane/ui-rework/base-3` prints `GATE PASS`.
- Every new test of the three oracle files is green; every existing test class listed below is still green except the
  one amended expectation.
- `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` (through the wrapper) if a `MatchHud` member moved
  (none expected).

## Oracle files (read-only for implementers)

The files of the `oracle:` commit (new, each with its `.meta`; a stub-only change is in the `stub:` commit):
- `client-unity/UnityProject/Assets/Tests/Editor/WorldNamesRig.cs` (stateless helpers: `Stage`, `Actor`, `Scene`, `Frame`,
  `Settle`, `LineBox`, `SlidePx`, `MeasurePicture`; 317 lines)
- `client-unity/UnityProject/Assets/Tests/Editor/WorldNamesEditModeTests.cs` (tests 1 to 7 and 12; 387 lines)
- `client-unity/UnityProject/Assets/Tests/Editor/WorldNamesTimeEditModeTests.cs` (tests 8 to 11, 13, 14; 245 lines)
- the three `.meta` files of those
- `docs/backlog/packets/P-UI-WORLD-NAMES-2.md` (this file)

The `oracle-amend:` commits, one existing test each (listed for the owner; the implementer does not touch them):
- `client-unity/UnityProject/Assets/Tests/Editor/NameLegibleEditModeTests.cs` (the set-up line `:357` to `Is.False`)
- `client-unity/UnityProject/Assets/Tests/Editor/NamePackGroup2EditModeTests.cs` (a rename and its comment; green before and after)

## 9. Validation commands

- `.agent-runs/lane-ui-rework/bin/unity-class.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-x WorldNamesEditModeTests,WorldNamesTimeEditModeTests`
- the stay-green classes below, by the same script.
- `.agent-runs/lane-ui-rework/bin/in.sh /Users/nevinmckinnon/Documents/Projects/RH-ui-x scripts/gate.sh --quick --oracle packet/P-UI-WORLD-NAMES-2 --base lane/ui-rework/base-3`
  once at the end (about ten minutes; restore the two StormInk prop assets afterwards with `git checkout --`).

## 10. Risk and suggested model tier

implementer-sonnet; reviewer. Hard part: a layout whose lifts persist across frames (memory keys), a cluster that moves
as a unit with an `_shift` offset, and zero allocation in a steady frame. How it looks is NOT established (see the end).

## 11. Known traps

1. Commits: (1) the new `OverheadIndicators` and `OverheadExtras` members (`NameBasePosition`, `AddClusterParts`,
   `LiftCluster`, `Shift`, `_shift`), nothing calling them yet; (2) the rule in `Names.cs`, the fields, the
   deletions; (3) `unity-class.sh` on the new classes and those below, then the gate.
2. A reflection `Invoke` allocates: use the delegate. If the meter flags `NameWorldWidth` or `NameWorldHeight`, cache
   the pair per (text, font size) in `OverheadIndicators` as `NameDrawnBounds` caches its mesh update, filled only
   while the label is `activeInHierarchy` (a size measured from a label never active is never kept); if it flags code
   that moved unchanged, report `blocked`.
3. Renderer and object names are pinned by many tests and `ContrastLabStage.ClassOf`: change none. COMMON stands.
4. **A text label that has never been active in the hierarchy measures at one tenth of its size** (measured
   2026-11-23, `understand/exp-zone-names.txt`): a `TextMeshProUGUI`/`TextMeshPro` under an inactive parent has had no
   `Awake`; `preferredWidth` and `preferredHeight` return a tenth of the true values. So product code never keeps
   or caches a size measured from a label that is not active in the hierarchy (`activeInHierarchy`, not `activeSelf`);
   test 11's variant keeps the box gate honest.
5. `unscaledDeltaTime` is a stale constant in EditMode (measured, 0.925): read `Now()` differences only.
6. A first `End` with no `Begin`, an art never laid out, and a second `End` on one stamp must not read as solved or
   skip (tests 12 and the stub's `-1` start values).
7. The tests are a separate assembly with no `InternalsVisibleTo`: the oracle reads private members by reflection
   (`_active`, `LayoutKey`, `LayoutNames`) and the public API otherwise; do not rename them. `Art` is a private class:
   its fields are read by reflection, so `LayoutNameKey`, `LayoutPack`, `LayoutStamp`, `LayoutLift`, `LayoutTarget`
   must be PUBLIC fields of it, and `LayoutNames(float)` a private instance method of the presenter.

## Existing tests that pin this code (read on the base; verified)

Amended, each its own `oracle-amend:` commit, listed for the owner (the implementer does not touch them):
- `Tests/NameLegibleEditModeTests.cs` `AnEliteNameDoesNotIntersectItsCampBadgeOrThePackMembersBars` (`:343`), the
  set-up line `:357` `Union(Bars(eliteRoot)...).Overlaps(nameBox), Is.True, "set-up: today the member's bar is on the
  elite's name (shot 09)"`: the elite now clears the bar inside `End`, and `nameBox` is measured after it. Amended to
  `Is.False`, message "the layout already lifts the elite clear of the member's bar" (red on the stub). The rest of
  the test (badge, `FighterNameRects` through `CampRespawnLabels.Draw`, name clear of the badge and of every other
  bar, stays within 3 units, badge within 3 of home) stays.
- `Tests/NamePackGroup2EditModeTests.cs` `FightersStandingTogetherHaveTheirNamesStackedNearestToTheLocalFighterLowest`
  (`:225`): name and comment only: ids 1 to 4 arrive in order, the local one is id 1, so class then actor id gives the
  old order. New name `FightersStandingTogetherHaveTheirNamesStackedByClassAndActorIdTheLocalFighterLowest` (green on
  the base; the rename is the whole change). Its assertions: the four names do not intersect, y rises with the index,
  a name with nothing near stays on its line (1e-4), five names are active (never merged).

Stay green, assertions unchanged (each class that names `SpriteActorPresenter` is in P-UI-WORLD-NAMES-1's list):
- `NamePackFix4EditModeTests.APackNameThatWouldLandOnTheLocalFightersNameIsLiftedClearAndTheFightersNameStays`
  (`:186`: the pack's name, slid onto the fighter's, overlaps unstacked, then after `Mixed(1, 700f, Pack)` the pack name
  does not intersect the fighter's, is above it and within 2 units; the fighter's name keeps its place within .02).
  Green only because `End()` passes dt 0 (every frame first sight). Its other cases assert counts, `NameShown`, the
  Tab map hide and return.
- `NamePackGroup2EditModeTests` pack and elite cases (idle frames; the engaged member's bar ends below `Gap` under its
  own name; the camp label cases).
- `NamePackEditModeTests` (`AHurtMobsNameSitsAboveItsOwnBarAndNeverOverlapsIt` `:152`: name.min.y >= bar.max.y; the pack
  cases `NameShown`).
- `HudVisualFix2EditModeTests` (`NameShown` of one name per pack, far pair and mixed pair show both, adjacent elites show
  both: `:286-326`).
- `MapOpenWorldEditModeTests` (`NoOverheadObjectIsDrawnWhileTheMapIsOpen`; `EverythingIsBackOnTheFirstFrameAfterTheMapCloses`
  `:181`: every object, position and text equal before and after the map, every lift 0).
- `NameLayoutKeyEditModeTests` (`LayoutKey` given once, kept by a renewed handle: reflection on a public `LayoutKey`).
- `StormInkSignatureCueEditModeTests` (`PlaceSignatureCues` reads the lifted bounds; run it).
- `MobContinuityEditModeTests`, `SpriteActorSignatureEditModeTests`, `ActorOutlineEditModeTests` (no `PresentIndicators`),
  `SpriteActorBarrierEditModeTests`, `StormInkFireRoamEditModeTests` (one actor), `OverheadIndicatorsEditModeTests`,
  `NameLegibleTableEditModeTests`, `NameLegibleHubCardEditModeTests`, `UiLabFixesEditModeTests`, `DeclutterEditModeTests`, `LegacyFactsEditModeTests`
  (`Update` only, `_shift` stays 0), `NamePackHoverEditModeTests`, `AbilityRowEditModeTests` (its own local
  `NameShown`), `VisionPresentationEditModeTests`, `ArenaPresentationEditModeTests`.

## Tests and where each is red on the stub

Run on the stub (`unity-class.sh ... WorldNamesEditModeTests,WorldNamesTimeEditModeTests`, 2026-11-23): 15 tests, all 15 red,
each on the first assertion that asks for the layout. Every test first states a set-up that holds on the base (the alone
boxes, grown by `PadEnter`, overlap: tests 1, 4, 7 assert it with a measured slide) and then asks `TryNameLift` for a
lift. Tests that read no slid scene (2, 3, 5, 6, 9, 11, 12, 13, 14) open with `TryNameLift` true and lift > 0.

| # | Test (class) | First red assertion on the stub |
|---|---|---|
| 1 | `ThePictureNamesNoLongerTouch` (Names) | "set-up: the pack's name takes part in the layout": `TryNameLift` true, was False |
| 2 | `AdjacentElitesDoNotLandOnEachOthersStars` (Names) | "set-up: the second elite takes part": True, was False |
| 3 | `EveryKindOfNameTakesPart` (Names) | "set-up: the second fighter takes part": True, was False |
| 4 | `ASmallPaceMovesNothingAndAReleaseWaitsThenGlides` (Names) | "set-up: the pack's name takes part": True, was False |
| 5 | `TheOrderDoesNotFollowThePlayer` (Names) | "set-up: the second fighter takes part": True, was False |
| 6 | `AClusterMovesAsOneAndTheStarGoesWithItsName` (Names) | "set-up: the crowned fighter takes part": True, was False |
| 7 | `OtherActorsBarsLiftANameButNeverTheLocalFightersOwn` (Names) | "set-up: 31 takes part in the layout": True, was False |
| 12 | `EndTwiceOnOneFrameChangesNothing` (Names) | "set-up: the pack's name takes part": True, was False |
| 8 | `ARenewedHandleKeepsItsLiftAndItsPlace` (Time) | "set-up: the lone Pardoner takes part": True, was False |
| 9 | `ReducedMotionTakesTheTargetAtOnceAndKeepsTheHold` (Time) | "set-up: the second fighter takes part": True, was False |
| 10 | `TheTabMapForgetsAndNamesReturnAtTheirLift` (Time) | "set-up: the pack's name takes part": True, was False |
| 11 | `AHiddenNameKeepsItsLiftForAHalfSecondAndForgetsAfterMoreThanASecond` (Time) | "set-up: the second fighter takes part": True, was False |
| 11b | `ANameFirstSeenHiddenIsMeasuredWhenItAppears` (Time) | "set-up: the revealed fighter takes part": True, was False |
| 13 | `EndWithoutADtDerivesItFromThePresenterClock` (Time) | "set-up: the second fighter takes part": True, was False |
| 14 | `LayoutNamesAllocatesNothing` (Time) | "set-up: fighter 2 takes part": True, was False |

Existing classes still green on the stub (run together with the new ones: 290 tests, 275 passed, the 15 failures being the
tests above): `NameLegibleEditModeTests`, `NamePackGroup2EditModeTests`, `NamePackFix4EditModeTests`,
`NamePackEditModeTests`, `NamePackHoverEditModeTests`, `HudVisualFix2EditModeTests`, `MapOpenWorldEditModeTests`,
`NameLayoutKeyEditModeTests`, `StormInkSignatureCueEditModeTests`, `MobContinuityEditModeTests`,
`SpriteActorSignatureEditModeTests`, `ActorOutlineEditModeTests`, `SpriteActorBarrierEditModeTests`,
`StormInkFireRoamEditModeTests`, `OverheadIndicatorsEditModeTests`, `DeclutterEditModeTests`, `UiLabFixesEditModeTests`,
`LegacyFactsEditModeTests`, `AbilityRowEditModeTests`, `NameLegibleTableEditModeTests`, `NameLegibleHubCardEditModeTests`,
`VisionPresentationEditModeTests`, `ArenaPresentationEditModeTests`. After the two `oracle-amend:` commits
(run of `NameLegibleEditModeTests` and `NamePackGroup2EditModeTests`: 66 tests, 65 passed): the amended
`AnEliteNameDoesNotIntersectItsCampBadgeOrThePackMembersBars` is red on the stub on its amended set-up line ("the layout already
lifts the elite clear of the member's bar", expected False, was True) and goes green when the elite's name is lifted clear of
the member's bar inside `End`; the renamed `...StackedByClassAndActorIdTheLocalFighterLowest` is green.

## Notes from the oracle author

- **Scratch run (every line can run).** One run per new class on copies of the three files (not committed, deleted) in which
  `Assert.That` and `Assert.Fail` went through a catch-and-collect helper (an `AssertionException` is kept and the test
  carries on; the list is written to a file and the test fails at its end). Result: 15 tests, 7 to 72 collected entries
  each, no exception of any other kind, no test aborted early. Every entry is an assertion about the missing behaviour:
  `TryNameLift` false, a lift of 0 where one is wanted, line boxes that still overlap, a cluster that did not rise with its
  name, `FighterNameRects` without the lone mob, dummy or star rectangles (or changing between two calls), the badge on
  the lines, the first frame of a contact not climbing at the rise rate. A few lines hold on the stub (a name that has no
  neighbour stays, the held/zero checks of test 9 with a lift of 0) and are only reached after a red one.
- **Frozen clock (Outcome 6), measured.** In an EditMode test `Time.unscaledTime` is the same on two reads with 3,000,000
  square roots and a GameObject between (0.9344762 and 0.9344762); `Application.isPlaying` is false;
  `Time.unscaledDeltaTime` is NOT 0 (0.9252325 both times, a stale constant). So `End()` is as written (the difference of
  `Now()`, no `isPlaying` guard) and never reads `unscaledDeltaTime`.
- **Measured geometry (for the implementer's expectations; no test pins a value).** Line boxes (preferred size times lossy
  scale, centred on the label): fighter and ordinary mob 0.42 high, elite 0.46; the mesh bounds are shorter (0.23 to 0.32)
  than the line box, so a box built from `NameDrawnBounds` instead of the line box fails tests 1, 3 and 11b. A name's line
  box ends 0.256 (elite) to 0.274 (fighter, mob, dummy) above its own bar: more than `Gap` (0.10), so a box never meets its
  own bar. Elite bar 1.50 wide, fighter 0.87, mob and dummy 1.12.
- **Placement of tests.** The spec puts test 4 in `WorldNamesEditModeTests` (done) although it runs frames: it reads the
  library's rates only. Test 11's variant lives in `WorldNamesTimeEditModeTests` with test 11.
- **Nearest-test choices.** Test 3 asks each lift to exceed the one before by 1e-3 (strict increase with a floor). Test 7
  puts the three targets and their three hurt brutes in one slid scene (groups 2,560 px apart in x) and reads the
  first-sight "own bar" set-up from a separate earlier frame with a dummy; the brutes' names are in no assertion. Test 12
  puts the pack under a Gaoler (elite, same spot) and the fighter cluster (bounty 40, marks) beside the local fighter, 1,760
  px apart, in one frame. Test 14 calls the private `LayoutNames(float)` through a delegate; on the stub (an empty method)
  `AssertNone` passes by itself, so the test is red only through its set-up lifts. Tests 9 and 11 use two arenas in one
  test (one per condition) rather than reusing one presenter.
- **Camp badge (test 1)** is drawn through the real `CampRespawnLabels.Draw` wired to `FighterNameRects`; on the stub the
  badge lands on the line boxes because the old rectangles are the shorter mesh bounds.
- **Rig contract the implementer must not break:** `_active` (private field), `Art.Root` and `Art.LayoutKey` (public
  fields, read by reflection), the private instance method `LayoutNames(float)`, object names ("Name", "Elite star",
  "Elite star plate", "Bounty crown", "Bounty number", "Secondary weapon backer", "Mark pip 1" to "4", "Stat mark backer 1"
  and "2", "Health and barrier track"), and `NameShown`.

- **Correction (oracle author, called back, 2026-11-23): test 14's fighters stand 3 px apart, not 6.** The set-up asks
  fighters 2 to 10 for a lift above 0. At 6 px the ninth fighter (id 9) stands 48 px = 1.5 units from the local box, outside
  `PadEnter` plus the box width (1.2 + .16 = 1.36), and box 1 has already risen above its top, so the library rightly leaves it
  at lift 0 (a scratch run on the implementation listed that one assertion and nothing else). The set-up line was wrong, not
  the implementation. At 3 px every fighter is within .84 units of the local box, so each overlaps it at lift 0 and must rise
  (capped at `MaxLift`, still above 0). Only the spacing literal and its comment changed; no assertion was touched.

## Not established

How it looks. `Gap`, `PadEnter`, `PadKeep`, `MaxLift`, the `LabelLift` rates and the name line height are first
guesses from geometry; no test pins a value; the lifts and heights quoted are estimates from font metrics. The lane
judges the look in its next Web round, once this rule is merged on the lane branch (real renders: the picture-40
camp, two fighters with marks and a bounty at a lift above 1.2, a hurt mob under an elite, a fighter in melee with
hurt creatures, allies circling an elite, engaged mobs, a mob walking out of its camp's reach, a hovered or locked
pack, a fighter first seen hidden then shown), and may tune the constants. Left over, none built here (remedy in
brackets, P-UI-WORLD-NAMES-3): a mob that walks beyond its camp's reach changes its key and steps once (a re-key call
in the library); the pack name's base position steps when a member dies or hover/lock changes the carrier (an anchor
glide); other actors' bars lift names too eagerly (only elites' and hurt creatures' bars count); contact from below
rides through the neighbour (rate or `PadEnter`); a tall fighter cluster lifts a yielding name by its whole height
(up to about two units, `MaxLift` caps it); a delta of exactly 0 on a new stamp in play would restart every lift;
two same-look elites swapping art through `MobContinuity.Adopt`; a faded name still reserving space.

## Proposed DEVIATIONS row (for the hand-off; the owner signs it)

Overhead names and their clusters keep clear of each other. Legacy: each name is a child of its actor's container at
a fixed point, depth by y, with no rule that places a label against another
(`design-world-names-legacy.md` L1, L2; `PlayerView.ts:65,171`, `MobView.ts:22,108`). Unity draws names above every
body, so now a name rises with its cluster over earlier names and other actors' bars.

## Later packets

P-UI-WORLD-LABELS-1 (prompt and giver labels become boxes keyed by handle); P-UI-WORLD-NAMES-3 (re-key call, anchor
glide, bar filter).
