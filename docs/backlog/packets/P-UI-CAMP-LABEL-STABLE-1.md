# P-UI-CAMP-LABEL-STABLE-1: the stat above a camp glides instead of hopping

- **Serves:** not in the plan: owner direction 2026-11-22 (chat of the UI rework lane): "there is a unity bug where the stat above a camp shimmers and glitches as a champ is moving around it"
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** feature/unity-rework
- **D-rules touched:** D1 n/a (presentation only) · D2 n/a · D5/D6 none · D9 untouched (no game rule, no wire)

## 1. ID and outcome
Camp world labels (the reward badge: plaque plus icons; the "RESPAWN Ns" text; the ward text) keep clear of fighters' heads,
bars and names, but they glide instead of hopping, and the badge no longer blinks at the vision edge.

1. **A lift with memory, per camp and per label kind** (badge, respawn text, ward text). Each frame the stateless answer "how
   far up must this label go from its base to be clear" is computed exactly as today, from the label's BASE position (never
   from its lifted position). That wanted value feeds `LabelLift` (below):
   - it rises toward a higher wanted value at `RiseUnitsPerSecond = 8` world units per second;
   - when the wanted value drops, the lift stays for `HoldSeconds = 0.45`, then falls at `FallUnitsPerSecond = 3`;
   - a wanted value within `Deadband = 0.03` of the current target changes nothing and keeps the hold fresh;
   - the first frame a label is drawn (no state yet, or not drawn for more than `ForgetSeconds = 1`) it takes its wanted value
     at once, so a label never appears on top of a head;
   - reduced motion (`ShowcaseSettings.Current.ReducedMotion`): no easing, the value jumps to the target, the hold stays.
   State is keyed by the camp's stable identity: the row's `Index` (`CampBadgeRow.Index`, `CampWardRow.Index`,
   `CampWorldLabel.Index` all carry the camp index: `CampBadges.cs:100,141`, `CampMarks.cs:142`), never by pool index.
2. **Badges fade at the vision edge instead of blinking.** A badge whose row disappears stays fully drawn for
   `LingerSeconds = 0.2`, then fades out over `FadeSeconds = 0.15` and is switched off. A badge whose row appears fades in
   over `FadeSeconds`, except on a frame where the labels start fresh (the first draw of this `CampRespawnLabels`, the first
   draw after the Tab map closes, or a draw more than `ForgetSeconds` after the last one): then it is fully drawn at once. A
   row that comes back during the linger shows no change at all. Reduced motion: no fade (alpha 0 or 1), the linger stays.
   The fade multiplies the alpha of the plaque and the icons; the reward colour is unchanged.
3. **Badges are not drawn while the Tab map is open** (`WorldLabelVisibility.Hidden`): `BadgesUsed` is 0 and no plaque or icon
   is active, like the respawn and ward labels today (`CampRespawnLabels.cs:207,283`).
4. **Ward text and badge of one camp do not overlap.** The two can co-occur: `CampBadges.Rewards` (`CampBadges.cs:80-112`)
   needs a living, unfinished, visible camp with a reward or elite; `CampBadges.Wards` (`:117-148`) needs only a live ward
   verdict. A warded camp with a reward has both, and today the ward text (48 sim px above the camp,
   `BeaconTextOffsetUnits`, `CampRespawnLabels.cs:27`) lands on the plaque (44 px, `BadgeOffsetUnits`). When a camp has both, the
   ward text's plate sits above the plaque's drawn rect with a 0.08 world unit gap, and follows it as it glides.
5. Everything else is unchanged: texts, colours, sort orders, pooling, offsets, the one-frame-old obstacle rects (the order of
   `PresentCollections` and `Present` in `Net/MatchClient.cs:657-659` stays).

## 2. Dependencies
none (base `feature/unity-rework` already has P-UI-INPUT-CUES-1).

## 3. Relevant legacy paths
None: the legacy game has no such presentation seam (legacy `CampBuffs.ts`/`CampWards.ts` draw labels at fixed points; the lift
is a Unity addition of P-UNITY-NAME-GROUP-2 / P-UNITY-NAME-LEGIBLE-1). Texts and offsets stay as they are.

The defect, in the current code (`client-unity/UnityProject/Assets/Scripts/Presentation/CampRespawnLabels.cs`):
- `DrawBadges` (:244-279) puts the plaque and icons back at the base point every frame (`Place`, :262-267) and then adds
  `CombatHudPlacement.Lift(...)` (:268-274; `CombatHudPlacement.cs:13-30`): a hard step to the top of any overlapping obstacle
  plus `Clearance = .06`. No easing, no hold, no memory: when a mob or champion crosses the plaque, or a sprite's bounds change
  between two animation frames, the badge hops by up to about 0.7 world units and drops back the frame the overlap ends.
  Obstacles: `_names` (`NameRectSource`) plus `HeaderRectSource` rects (:247-251), wired in `ArenaView.cs:164-165` to
  `SpriteActorPresenter.FighterNameRects` / `FighterHeaderRects`.
- `Clear(label)` (:190-203) does the same to the respawn text and the ward text (up to eight hard steps, `+ .04`), measuring
  `plate.bounds` AFTER the label was placed.
- A badge is in the row list only while the viewer can see the camp (`CampBadges.Rewards` with `_canSee`, `CampBadges.cs:96`):
  at the vision edge it blinks with the fog boolean.
- `DrawBadges` ignores `WorldLabelVisibility.Hidden`; `DrawRows` (:207) and `DrawBeacons` (:283) honour it.
- `Update(Session, visible)` (:110-127) is called from `ArenaView.cs:609` once per rendered frame.

## 4. New paths
- `client-unity/RoguehallaNet/Net/Ui/LabelLift.cs` and its synced copy `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/LabelLift.cs` (+ `.meta`)
- `client-unity/RoguehallaNet.Tests/LabelLiftTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/CampLabelStableEditModeTests.cs` (+ `.meta`)

### The library type (pure; tested with dotnet)
```csharp
namespace Roguehalla.Net.Ui {
  /// <summary>How far a world label is raised above its base to clear fighters: rises fast, holds, falls slowly.</summary>
  public struct LabelLift {
    public const float RiseUnitsPerSecond = 8f, FallUnitsPerSecond = 3f, HoldSeconds = .45f, Deadband = .03f;
    public float Value { get; }    // the lift to draw this frame, in world units, never negative
    public float Target { get; }   // what Value moves toward
    public static LabelLift Start(float wanted);                        // first frame: Value = Target = max(0, wanted)
    public LabelLift Step(float wanted, float dt, bool reducedMotion);  // one frame later; returns the new state
  }
}
```
`Step` rules, in order: clamp `wanted` at 0 and `dt` at 0; if `wanted > Target + Deadband`, `Target = wanted` and the hold
restarts; else if `wanted >= Target - Deadband`, the hold restarts; else the hold counts down by `dt` and, when it reaches 0,
`Target = wanted` and the hold restarts. Then `Value` moves toward `Target` by at most `RiseUnitsPerSecond * dt` upward or
`FallUnitsPerSecond * dt` downward; with `reducedMotion`, `Value = Target`.

### Constants in `CampRespawnLabels` (not in the library)
`LingerSeconds = 0.2`, `FadeSeconds = 0.15`, `ForgetSeconds = 1`, ward-over-plaque gap `0.08`.

### Time and the test seam (public API the oracle uses)
- `Update(Session, visible)` uses `Time.unscaledDeltaTime`.
- `public void Draw(List<CampWorldLabel> rows, List<CampWardRow> wards, List<CampBadgeRow> badges, float dt)`: the tests drive
  frames through it. The existing two- and three-argument `Draw` keep their meaning for single-frame tests: each draws one
  FRESH frame (labels at their wanted lift at once, badges fully drawn, no state carried).
- `public Func<bool> ReducedMotionSource` (default: reads `ShowcaseSettings.Current`, false when none), so a test can set it.
- Do not add lines to `View/ArenaView.cs` (798 of 800).

## 5. Exclusive write scope
- `client-unity/UnityProject/Assets/Scripts/Presentation/CampRespawnLabels.cs`
- `client-unity/RoguehallaNet/Net/Ui/LabelLift.cs` and its synced copy with `.meta`
- Not `CombatHudPlacement.cs` (other callers: damage numbers, signature cue), not `ArenaView.cs`, not `SpriteActorPresenter*.cs`.
- New code may go in a `CampRespawnLabels.<Concern>.cs` partial if the file would pass 800 lines (then the class becomes `partial`;
  a new non-test C# file needs a `/// <summary>` before its first type).

## 6. Contracts and references
- R-AUTH / ADR 0008: Unity holds no game rule; this is presentation of server observations only.
- Motion rules of the lane: unscaled time, instant under `ShowcaseSettings.Current.ReducedMotion`, nothing allocated in a steady frame.

## 7. Behaviour to preserve
- Texts, colours, sort orders (`Order`, `Order - 1`), pooling, offsets, the one-frame-old obstacle rects.
- A fresh single frame (the 2- and 3-argument `Draw`) is exactly today's output: positions, alpha 1, reward colours.
- Object names the tests read: "Camp reward plaque", "Camp reward badge", "Camp respawn label", "Camp ward label", child "Plate".

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-UI-CAMP-LABEL-STABLE-1` passes.
- `dotnet test client-unity/RoguehallaNet.Tests --filter LabelLiftTests` green; Unity class `CampLabelStableEditModeTests` green.
- Existing classes stay green and unchanged (below).

## Existing tests that pin this code (must stay green, unchanged; none needs an `oracle-amend`)
All of them build `CampRespawnLabels` and call the 2/3-argument `Draw` (one fresh frame) or `Update`:
- `NamePackGroup2EditModeTests` (`:244-338`): the respawn/ward label has a dark plate (alpha >= .6, text contrast >= 4.5);
  with a far name the labels sit exactly at `WorldPoint(x, y+32)` / `WorldPoint(x, y-48)` within 1e-3 (the OFFSETS); a name at
  the label's position makes the label's plate+text rect not overlap it and stay within 3 units of home.
- `NameLegibleEditModeTests.AnEliteNameDoesNotIntersectItsCampBadgeOrThePackMembersBars` (`:340-376`): one fresh frame of an elite's
  badge (two icons) through `NameRectSource = _presenter.FighterNameRects`: the badge does not intersect the elite's name or bars;
  it stays within 3 units of home `WorldPoint(x, y-44)`.
- `CampLabelsHiddenFix4EditModeTests.RespawnLabelsAreNotDrawnWhileWorldLabelsAreHidden`: `Used` 1 then 0 when
  `WorldLabelVisibility.Hidden`.
- `StormInkCombatHudEditModeTests.CampRewardPlaqueKeepsItsExactArtAndClearsTheNearbyHolyHelmet` (`:38-56`): through `ArenaView`'s
  `_campLabels` (reflection): one badge row `BadgesUsed == 1`, icon sprite is `CampRespawnLabels.Art("health")`, plaque does
  not overlap the Holy helmet rect (it relies on the HeaderRectSource wiring).
- Library `CampBadgesTests`, `CampMarksTests` (pure rows; unaffected).
- Callers that only read the type: `TacticalMapMarks.cs`, `TacticalMapController.cs` use `CampRespawnLabels.Art`/`StatGlyph` statics (unchanged).
Rows in those tests that leave `Index` at 0 for several camps: the fresh-frame rule makes that harmless; keying uses `Index`.

## Oracle files (read-only for implementers)
- `client-unity/RoguehallaNet.Tests/LabelLiftTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/CampLabelStableEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/CampLabelStableEditModeTests.cs.meta`
(The stub commit holds `LabelLift.cs` (library and synced copy, with its `.meta`) and the `ReducedMotionSource` / four-argument `Draw` stubs in
`CampRespawnLabels.cs`: those are the implementer's to replace.)

## 9. Validation commands
- `bash .agent-runs/lane-ui-rework/bin/in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter LabelLiftTests`
- `bash .agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> CampLabelStableEditModeTests,NamePackGroup2EditModeTests,NameLegibleEditModeTests,CampLabelsHiddenFix4EditModeTests,StormInkCombatHudEditModeTests`
- `bash client-unity/sync-net.sh --check`

## 10. Risk and suggested model tier
Sonnet. Small state machine plus presentation wiring. The hard parts: computing the wanted lift from the BASE rect (the plate's
size is known before placing; do not read `renderer.bounds` after last frame's lift), keeping per-camp state when the row order
changes, and zero allocation in a steady frame.

## 11. Known traps
- Wanted lift from a rect at the base position, not from `renderer.bounds` after last frame's lift (the badge: plaque size is
  `(n * BadgeStep + .3, PlaqueHeight)` at the base centre; the texts: the plate's size at the base). Mirror the existing
  arithmetic: badge via `CombatHudPlacement.Lift` (clearance .06), texts via `Clear`'s loop (`+ .04`, up to 8 passes).
- The pools are indexed by draw order; the state is per camp and per kind. A camp list that changes order between frames must not
  swap two camps' lifts. Rows with the same `Index` and different kinds (badge / respawn / ward) are separate states.
- No per-frame allocation: no LINQ, no per-frame lambdas, no `foreach` over a dictionary; a small list of state structs searched
  linearly is fine.
- `Time.unscaledDeltaTime` after a pause can be large: a `dt` above `ForgetSeconds` counts as a fresh frame.
- Alpha: `Place(...)` sets `r.color` every frame (:238); apply the fade there. Existing tests compare icon colours to the reward
  colour on a fresh frame: alpha must be 1 there. A fade-out ends with the plaque and icons inactive.
- The plaque and icons are positioned by transform (not children): lift all of them by the same amount (icons keep their offset).
- The ward text must follow the plaque's DRAWN rect (it glides), plus the 0.08 gap; its own obstacle lift still applies (take the larger).
- `Draw(.., dt)` must clear stale state the way `Update` does (a draw after `Hidden` was true counts as "start fresh").

## Notes from the oracle author
- Test 4 of the spec (`ABadgeDrawnForTheFirstTimeOverAHeadIsAlreadyClear`) is true of today's stateless code too, so on the stub it
  would pass. It is written to also assert the new fade-in on a badge that appears after the labels were running (a "set-up"
  assertion `alpha < 1` that is red on the stub), together with the position being clear in that very frame.
- Test 12 (`ASteadyFrameAllocatesNothing`) is likewise green on today's code for the allocation part; it also asserts that the plaque
  stays still across a flickering obstacle (the new behaviour), which is what makes it red on the stub.
- Alpha is read from `SpriteRenderer.color.a`; an inactive plaque counts as alpha 0.
- The "ward above plaque" gap is asserted on every frame including the glide (test 11); the plate's rect is the child "Plate" sprite bounds.
- A trap the oracle exposed (existing behaviour, not in the spec): in `DrawBadges` (`CampRespawnLabels.cs:266-268` before the stub's added lines) the
  plaque is given `drawMode = Sliced; size = ...` and only THEN `Place` assigns the sprite; Unity resets `size` to the sprite's native size when a
  sprite is assigned, so a pooled plaque is drawn at 1.5 x 1.0 in its very first frame and at 0.92 x 0.78 from the second. The tests read the drawn
  rect as position plus `SpriteRenderer.size` (never `bounds`, which lags). `BaseBadge()` warms two frames. The implementer must assign the sprite
  before the size (or otherwise draw the right size at once) so the first frame is drawn at the size the lift was computed for: test 4 asserts the
  DRAWN plaque clears the obstacle on a fresh frame.
- In the fade-in tests the plaque must be ACTIVE on the frame its row appears, with alpha below 1 (alpha 0 is allowed), and reach 1 within
  `ceil(FadeSeconds / dt) + 1` = 10 frames. During the linger (frames 1..11 at 1/60 s after the row leaves) the plaque and its icon stay active at alpha 1
  at the same position, so the implementer remembers the last row of a badge. At 23 frames (0.383 s) they are inactive.
- The tests read the plaque by name ("Camp reward plaque", sliced `size`), icons by "Camp reward badge" and the texts' plates as the child sprite of
  "Camp respawn label" / "Camp ward label". Only active (`activeSelf`) objects count.
- Camp rows use distinct `Index` per camp in the tests; the respawn and ward text tests use separate camps for each kind so that the
  two never interact.

## Red tests on the stub
dotnet `LabelLiftTests`: all 11 cases red on their value assertions (the stub returns 0): `StartTakesTheWantedValueAtOnceAndNeverNegative`
(Value 0, expected 0.7), `ARiseMoves...`, `ADropHolds...`, `AWantedValueThatAlternates...`, `JitterInsideTheDeadband...`,
`AWantedValueBackInsideTheDeadbandRestartsTheHold`, `ARiseAboveTheDeadband...`, `ReducedMotionJumps...` (0, expected 0.5), `ANegativeDtOrWantedIsClamped`
(Target), `ValueIsNeverNegative...(false/true)` (`sawLift`). The tests were also run against a throwaway reference implementation of the Step rules
above: 11 of 11 green (not committed).

Unity `CampLabelStableEditModeTests` (13 cases, all red on assertions, none on an exception):
- `ABadgeGlidesWhenAFighterWalksUnderIt`: the plaque hopped 0.46 at frame 38 (limit 0.134).
- `AnObstacleThatFlickersLeavesTheBadgeStill`: y follows the flicker at frame 1.
- `TheBadgeComesDownOnlyAfterTheHold`: y drops at once, 1 frame after the obstacle leaves.
- `ABadgeDrawnForTheFirstTimeOverAHeadIsAlreadyClear`: the set-up `alpha < 1` after the badge enters a running label set (alpha is 1). The single-fresh-frame half
  passes on the stub (it is today's behaviour) and stays as a regression pin.
- `SmallChangesInTheObstacleDoNotMoveTheBadge`: y moves by 0.02 at frame 0.
- `RespawnAndWardTextGlideTheSameWay(false/true)`: the text hopped 0.34 (limit 0.134).
- `BadgesAreNotDrawnWhileTheTabMapIsOpen`: `BadgesUsed` is 1 while Hidden (expected 0).
- `ABadgeThatLeavesTheRowsLingersThenFades`: the plaque is gone 1 frame after the row left (expected drawn through the linger).
- `ABadgeThatEntersLaterFadesIn`: alpha is 1 on the frame the row appears (expected below 1).
- `ReducedMotionJumpsButStillHolds`: y drops at once after the obstacle leaves (the hold is missing).
- `TheWardTextStaysAboveThePlaqueOfItsCamp`: at frame 0 the ward text rect intersects the plaque.
- `ASteadyFrameAllocatesNothing`: the allocation half is green on the stub (today's code allocates nothing); the flicker half is red (y follows the flicker at frame 1).

Existing classes run on the stub, 83 of 83 green: `NamePackGroup2EditModeTests`, `NameLegibleEditModeTests`, `CampLabelsHiddenFix4EditModeTests`,
`StormInkCombatHudEditModeTests`, `TacticalMapEditModeTests`. `CampRespawnLabels` is constructed only by `ArenaView` and by those four test classes
(`git grep CampRespawnLabels`); `TacticalMapController.cs` / `TacticalMapMarks.cs` use only its statics.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
