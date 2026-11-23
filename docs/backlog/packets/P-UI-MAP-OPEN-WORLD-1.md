# P-UI-MAP-OPEN-WORLD-1: nothing of the world's own UI shows around the open map

- **Serves:** owner direction 2026-11-22 in the UI rework lane's chat ("When tab is clicked map layout is weird"), seen in Web round 1 of the lane (2026-11-23): with the Tab map held, an actor's health bar is still drawn in the margin beside the sheet while names and camp badges are hidden. Not in ORDER_OF_OPERATIONS.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** lane branch `lane/ui-rework/base-3` (client only; the integration branch is `feature/unity-rework`)
- **D-rules touched:** D1 legacy hides nothing in the world under its map (an HTML overlay at `rgba(8, 10, 14, .5)`, `packages/client/src/ui/planning.css:57-63`; names, bars and numbers stay drawn; only the weapon-range guide is hidden, `packages/client/src/scenes/RaidScene.ts:1629-1638`, DEV-016). The lane is already past legacy. Propose ONE `DEVIATIONS.md` row in the hand-off that says everything the open Tab map and the planning screen hide in the world, against that legacy behaviour. Hidden by earlier packets, named by no row today: the world names, the camp respawn labels and ward beacons, and the roots "World markers", "World indicators" and "Quest carrier flags". Hidden by this packet: the overhead cluster (rings, glyphs, bounty, Wish icon, badge, marks, TAXED cue, barrier bubble, Fire roam glow), the health and barrier bars, and the camp patience outlines. · D2 n/a · D5/D6 none · D9 untouched (the gate is a drawing flag; no outcome is decided).

## 1. ID and outcome

While the Tab map or the planning screen is open (`WorldLabelVisibility.Hidden` is true), no overhead UI of any actor is drawn: not the health and barrier bar (nor its outline), not the overhead cluster, not the name, and not the camp patience outlines. The actors themselves and their footprints stay, under the existing 86 percent dim (the Tab map's scrim alpha is unchanged). On the first frame after the flag is false everything is drawn as if the map had never opened.

What is wrong today. `TacticalMapController.ShowPanel` (`client-unity/UnityProject/Assets/Scripts/UI/TacticalMapController.Sheet.cs:58-62`) is the only writer of the static `WorldLabelVisibility.Hidden` (`Scripts/Presentation/WorldLabelVisibility.cs`): true while the panel is active (Tab map and planning screen). `MatchClient.Update` calls `Hud.ReadUiInput` (the key opens or closes the map) before `Present()` reaches `Arena.Present`, so the flag is current on the frame the key changes. Its readers today: `SpriteActorPresenter.MergeMobNames` (`Scripts/Presentation/SpriteActorPresenter.cs:188-194`, hides every name each frame) and `CampRespawnLabels` (`Presentation/CampRespawnLabels.cs:185`). Nothing else reads it.

An actor is one "Observed actor" object under the `ArenaView` object (`SpriteActorPresenter.Create`): it has no root of its own that the open edge could switch off. `SetActive(false)` on it would hide the body too, and `Present` sets `a.Root.SetActive(!hideAll)` every frame. So `ArenaView.SetWorldMarksVisible` (`Scripts/View/ArenaView.MapOpen.cs:11-18`, called by `MatchHud.RefreshAroundTheMap`, `Scripts/UI/MatchHud.Visibility.cs:71-84`, on the edge of `IsOpen`) hides three roots found by name (`MapHiddenRoots`, `ArenaView.MapOpen.cs:8`) and no actor.
- The bar: `Present` (`SpriteActorPresenter.cs:321`) switches `HpBack`, `Hp` and `Barrier` off (`:559`), then on for each `_barLayout` element that `OverheadStack.Build` lists (`:558`, `:561-578`; a player and an elite always, a hurt mob). It never reads the flag. The picture shows the Gaoler's bar at 41 percent of its normal brightness: the .86 scrim dims it like the floor, it does not remove it.
- The cluster: `PresentIndicators` (`SpriteActorPresenter.Overheads.cs:108`) calls `OverheadIndicators.Update` (`Presentation/OverheadIndicators.cs:242`), which hides only for `f.Dead` (`:244`). It draws the name, the bounty crown and number, the off-hand badge, the Wish icon, the mark pips and stat marks, the elite star, status glyphs, and (through `OverheadExtras.Update`, `:246`) the signature cue, the barrier bubble, the Fire roam glow, and the ground rings: hover, hunt, attack lock, rooted, zoned, elite, mark brand, teleport immunity.
- The patience outline: `CampPatienceOutlines` (dashed line, "PATIENCE RANGE" label) lives under the root "Camp patience outlines" made in `ArenaView.Awake` (`Scripts/View/ArenaView.cs:167`); `MapHiddenRoots` does not list it.

Rules (the outcome):
1. **The bar.** While `WorldLabelVisibility.Hidden` is true, `Present` leaves `HpBack`, `Hp` and `Barrier` `enabled == false` and the track's outline ("Health and barrier track outline", `SpriteActorPresenter.cs:759`) off, for every actor.
2. **The cluster.** While it is true, `OverheadIndicators.Update` draws nothing: every renderer it owns (`OverheadIndicators` and `OverheadExtras`, each a child of the actor) has `enabled == false`, and its three text objects ("Name", "Bounty number", "Signature cue") are inactive; `SpriteActorPresenter.NameShown(id)` (`:41`) is false.
3. **What stays.** The actor itself: the "Observed actor" root, "Authored character" (with its rim), the held weapon, "Contact shadow", "Cast shadow", "Faction footprint" (with its outline) and the local fighter's light. They stay under the existing 86 percent dim.
4. **Back on the first frame.** The first `Present` plus `PresentIndicators` pass after the flag is false draws every element as it would have been drawn had the map never opened: same enabled state, colours, scales and text. No rebuild, no event. The one exception is the name palette: hiding resets it to dark, and it is decided again at the end of that same frame (`FinishNames`, when `Surfaces` is set), so a name on ground between luminance .18 and .22 can come back in the other palette (the test rig has no `Surfaces`). An actor created, or taken from the pool, while the flag is true is born hidden and comes back the same way.
5. **The root.** `MapHiddenRoots` gains "Camp patience outlines": the open edge hides it with the other three and the close edge restores it. The three existing names behave as today.
6. **What a player loses while the map is open** (say it in the hand-off): hover, hunt and attack-lock rings, rooted and zoned rings, the elite's two rings and star, mark-brand and immunity rings, status glyphs, the barrier bubble round the body and the Fire roam glow, bounty, Wish icon, secondary-weapon badge, mark pips and stat marks, the TAXED cue, health and barrier bars, names, and the patience range line. The planning screen loses the same, and so does the map in toggle mode, where the loss lasts until the key is pressed again, not only while it is held. The actors and their footprints stay.
7. **Cost.** One static bool read per presented actor in `Present` and one in `OverheadIndicators.Update`; a steady hidden frame returns at the top of `Update` and allocates nothing. `HideAll` runs once on the open edge.
8. **One writer.** `WorldLabelVisibility.Hidden` keeps its one writer (`ShowPanel`); this packet only reads it (`Tools/tab-map-sheet.test.cjs` counts assignments in `TacticalMapController*.cs` and stays green).

## 2. Dependencies

The lane base `lane/ui-rework/base-3` (it holds P-UI-TABMAP-SHEET-1, P-UI-ALLOC-METER-1 and `AllocationMeter.cs`). Nothing else.

## 3. Relevant legacy paths
- `packages/client/src/ui/planning.css:57-63`: the legacy map is an overlay at `rgba(8, 10, 14, 0.5)`; nothing in the world is hidden under it (verified by reading).
- `packages/client/src/scenes/RaidScene.ts:1629-1638`: only the weapon-range guide hides while the map is held (`hidden: this.mapHold.held || ...`); DEV-016 (`docs/DEVIATIONS.md`).

## 4. New paths
- `client-unity/UnityProject/Assets/Tests/Editor/MapOpenWorldEditModeTests.cs` (+ `.meta`): the oracle.

No new product file; no change under `client-unity/RoguehallaNet/` (no arithmetic to move into the library).

## 5. Exclusive write scope
- `client-unity/UnityProject/Assets/Scripts/View/ArenaView.MapOpen.cs`: add "Camp patience outlines" to `MapHiddenRoots`; amend the two summaries.
- `client-unity/UnityProject/Assets/Scripts/Presentation/SpriteActorPresenter.cs`: `Present` only, the `for` over `_barLayout` (line 561), edited in place so the 772-line file gains no line: `for (int i = 0, n = WorldLabelVisibility.Hidden ? 0 : _barLayout.Count; i < n; i++)`. The track outline hides because it follows `HpBack` (`:701`, `a.OutHp.Sync`); if the test shows it does not, the implementer reports it (it may not add lines to this file).
- `client-unity/UnityProject/Assets/Scripts/Presentation/OverheadIndicators.cs`: a private `bool _hiddenByMap` and the top of `Update(in Facts f)` (Known traps, first item). 410 lines now, limit 800.
- Not in scope: `Presentation/OverheadExtras.cs` (its `HideAll` is the one used), `SpriteActorPresenter.Overheads.cs`, `WorldLabelVisibility.cs` (its stale "Stub" summary stays: a later packet), `MatchHud.Visibility.cs`, `TacticalMapController*.cs`, `View/ArenaView.cs` and `ArenaView.Fx.cs` (damage numbers), `CampPatienceOutlines.cs` (never split or rename it), `UI/TacticalMapMarks.cs`, `UI/TacticalMapController.cs` (other packets of this wave), every file wave 3 owns, `Tools/*.test.cjs`, `docs/DEVIATIONS.md`.
- No existing test changes expectation: no `oracle-amend:` commit.

## 6. Contracts and references
- Presentation only: Unity holds no game rule; nothing here decides an outcome (R-AUTH, D9).
- Lane rules: `HudUi`/theme colours untouched; no allocation in a steady frame; readability gate (800 lines, `ArenaView.cs` 798 and `SpriteActorPresenter.cs` 772 gain no line).
- `docs/backlog/packets/P-UI-TABMAP-SHEET-1.md` (the open edge and the three roots).

## 7. Behaviour to preserve
- `MergeMobNames`' hidden branch stays as it is (`NamePackFix4` pins it and it still empties `_nameRects`).
- Renderer and object names (pinned by many tests and `ContrastLabStage.ClassOf`): change none.
- The Tab map's scrim alpha (.86) and every other map behaviour.
- The three existing roots hide and return as today.

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-UI-MAP-OPEN-WORLD-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- `MapOpenWorldEditModeTests`: all 7 tests green; the existing classes below stay green.
- `Tools/tab-map-sheet.test.cjs` still counts one assignment of `WorldLabelVisibility.Hidden`.

## Oracle files (read-only for implementers)
- `client-unity/UnityProject/Assets/Tests/Editor/MapOpenWorldEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/MapOpenWorldEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-MAP-OPEN-WORLD-1.md`

## 9. Validation commands
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> MapOpenWorldEditModeTests` (the new class).
- `.agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> NamePackFix4EditModeTests,SpriteActorBarrierEditModeTests,ArenaPresentationEditModeTests,NamePackEditModeTests,ActorOutlineEditModeTests,OverheadIndicatorsEditModeTests,UiLabEditModeTests,UiLabFixesEditModeTests,SpriteActorSignatureEditModeTests,TabMapSheetEditModeTests,HudVisualFix4EditModeTests` (the existing pins, in groups of at most three per run if one run is too slow).
- `.agent-runs/lane-ui-rework/bin/in.sh <worktree> node --test client-unity/UnityProject/Tools/tab-map-sheet.test.cjs`.

## 10. Risk and suggested model tier
Low: three small edits and one bool. Sonnet implements, Sonnet reviews. The hard part is not hiding the body: never `SetActive(false)` an actor root.

## 11. Known traps
- **Where the cluster gate goes.** `Update` begins `if (f.Dead) { HideAll(); return; }` (`OverheadIndicators.cs:244`). Add directly under it: `if (WorldLabelVisibility.Hidden) { if (!_hiddenByMap) { _hiddenByMap = true; HideAll(); } return; }`, then `_hiddenByMap = false;`. `HideAll` (`:221`) clears `_bounty`, `_nameText` and the name palette, so the first `Update` after the map rewrites text and colours: that is test 2's "back". A pooled art reuses its `_hiddenByMap` as it was: harmless, `Release` calls `HideAll` itself.
- Never hide an actor by `SetActive` on its root (the body would go, and `Present` switches it back on each frame).
- Do not touch `MergeMobNames`' hidden branch.
- `Present` runs once per actor and `PresentIndicators` after it: both must see the flag in the same frame. The map's key is read in `Update` before `Present()`, so the open and close frames are exact; a map closed from `Tick` in `LateUpdate` returns one frame later. Accepted.
- An actor that is fading out (a ghost, at most .26 s, `VisionFade.ExitSeconds`) is not presented, so one that was already leaving when the map opened keeps its last overhead drawing until the fade ends. Accepted; not tested.
- Renderer and object names are pinned by many tests and `ContrastLabStage.ClassOf`: change none.
- `WorldLabelVisibility.Hidden`, `SpriteActorPresenter.FadeClock`, `TacticalMapController.SecondsClock`, `SpriteOutline.Enabled`, `UiTheme.Current` and `MatchClient.Arena` are shared state: tests restore them in `finally`. Time in EditMode does not advance.
- `in` arguments in a lambda: pass a field (`in _facts`), not a lambda parameter.
- `ArenaView.cs` and `SpriteActorPresenter.cs` gain no line. Run the node seams test only if a `MatchHud` member moves (none does here).

## Existing tests that pin this code (verified by reading; all must stay green, none expected red)
- `NamePackFix4EditModeTests.NoNameIsDrawnWhileWorldLabelsAreHiddenAndTheyReturnWhenShownAgain`: sets the flag true, expects zero active "Name" objects, then at least 3 again with the flag false. Other tests of the class never set the flag.
- `SpriteActorBarrierEditModeTests`: finds "Confirmed health" (line 40) and reads the bar with the flag at its default false.
- `ArenaPresentationEditModeTests` (`AssertRendererEnabled`, line 378) and `NamePackEditModeTests` (line 147, the track's bounds): bars with the flag false.
- `ActorOutlineEditModeTests`: the outlines of bodies, footprints and the track follow their sources, flag false.
- `OverheadIndicatorsEditModeTests`: the cluster's presence rules, flag false; it never sets the flag.
- `UiLabEditModeTests`, `UiLabFixesEditModeTests`, `SpriteActorSignatureEditModeTests`: the cluster and the signature cue, flag false.
- `TabMapSheetEditModeTests.WorldMarksAreHiddenWhileTheMapIsOpen` (line 449): iterates its own three root names; green, but it does not cover the fourth. `HudVisualFix4EditModeTests`: the flag follows the panel.
- `CampLabelsHiddenFix4`, `CampLabelStable`, `TabMapFlow` set the flag or open a map and read no bar; all restore it to false.

## Oracle author's notes ("Notes from the oracle author")
(filled in below)

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
