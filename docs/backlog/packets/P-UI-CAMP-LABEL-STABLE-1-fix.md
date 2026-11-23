# P-UI-CAMP-LABEL-STABLE-1-fix: the ward text glides too when its camp's badge comes and goes

- **Serves:** the one note of the review of P-UI-CAMP-LABEL-STABLE-1 (LAND WITH NOTES, 2026-11-22); not in the plan: same owner direction 2026-11-22 as that packet ("the stat above a camp shimmers and glitches")
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `agent/sonnet/P-UI-CAMP-LABEL-STABLE-1` (this packet stacks on that unmerged branch; the gate compares against it, `--base agent/sonnet/P-UI-CAMP-LABEL-STABLE-1`)
- **D-rules touched:** D1 n/a (presentation only) · D2 n/a · D5/D6/D9 none

## 1. ID and outcome
On a warded camp that also has a reward badge, the ward text rides a 0.08 world unit gap above the badge's plaque. Today that
gap is applied AFTER the text's `LabelLift` and so has no rise, hold or fall: when the badge appears or disappears at the
vision edge the badge fades, but the ward text hops by about the plaque's height (0.7 to 0.8 world units) in one frame. That is
the shimmer the base packet removed from the badge, moved to the ward text.

The defect, in `client-unity/UnityProject/Assets/Scripts/Presentation/CampRespawnLabels.cs`, `DrawBeacons` (:404-426):
```csharp
float lift = TextLift(w.Index, 1, WantedClear(label, out float baseYMin), dt, reduced);      // :419
if (PlaqueTop(w.Index, out float top)) lift = Mathf.Max(lift, top + WardGap - baseYMin);     // :421  (the part with no memory)
```

Outcome: the plaque's requirement is part of what the ward text WANTS, before its `LabelLift` steps:
`wanted = Max(WantedClear(...), plaque showing for this camp ? top + WardGap - baseYMin : 0)`, then the one
`TextLift(w.Index, 1, wanted, dt, reduced)`; the second line above goes away. Consequences, all intended:

- A ward text drawn for the first time with the plaque already there starts at the right height (the first frame of a label is a
  snap, `LabelLift.Start`): the base packet's gap test keeps passing unchanged.
- When the badge appears later, the text rises at `LabelLift.RiseUnitsPerSecond` (8 per second, about 0.13 a frame at 60 fps): it
  reaches the gap in about 0.1 s, while the plaque is still fading in.
- When the badge leaves, the text does not move while the plaque lingers and fades (0.2 s + 0.15 s; the text's hold is 0.45 s),
  then comes down at `LabelLift.FallUnitsPerSecond` (3 per second).
- "Plaque showing" is true from the frame the badge is drawn until its state is removed after the fade: use the same condition
  `PlaqueTop` uses today (`CampRespawnLabels.cs:397-402`: a badge state with `Drawn` set this frame, matched by camp `Index`).
- Reduced motion: the text jumps, the hold stays (the base packet's rule for every lift: `LabelLift.Step(..., reducedMotion)`).

Nothing else changes: no constant, no badge behaviour, no public signature.

## 2. Dependencies
`agent/sonnet/P-UI-CAMP-LABEL-STABLE-1` (head `f32177fe3`: `LabelLift`, `TextLift`, `WantedClear`, `PlaqueTop`, the four-argument
`Draw`, the `ReducedMotionSource` seam). It is not merged into `feature/unity-rework`; this branch starts from it.

## 3. Relevant legacy paths
None: the legacy game has no such presentation seam (legacy `CampBuffs.ts` / `CampWards.ts` draw labels at fixed points; the lift
is a Unity addition). Current code the fix touches (all in `CampRespawnLabels.cs`):
- `DrawBadges` (:325-394): per-camp `BadgeState` (`Lift`, `Alpha`, `Gone`, `Top`, `Seen`, `Drawn`); sets `s.Top = centre.y + PlaqueHeight * .5f; s.Drawn = true` (:388) for every state it draws, including a lingering or fading one (a state is dropped when `Alpha <= 0`, :362-369). It runs before `DrawBeacons` (:193), so `Drawn` and `Top` are this frame's.
- `PlaqueTop` (:397-402): the "plaque showing" condition and the plaque's top edge.
- `DrawBeacons` (:404-426): the ward label is first placed at `home` (`WorldPoint(x, y + BeaconTextOffsetUnits)`, :415-416), the plate fitted (:418), then lifted (:419-422).
- `WantedClear(label, out baseYMin)` (:253-270): measured with the label at its home position; `baseYMin` is the plate's lower edge there. The plaque term must use this home `baseYMin`, never a lifted rect, or the lift feeds itself.
- `TextLift(index, kind, wanted, dt, reduced)` (:204-216): per camp and kind (ward = kind 1) `LabelLift` state; first frame is `LabelLift.Start(wanted)`; a state not drawn for `ForgetSeconds` (1 s) is dropped (:194-200).
- Constants (:167): `LingerSeconds = .2f, FadeSeconds = .15f, ForgetSeconds = 1f, WardGap = .08f`. `LabelLift`: `client-unity/RoguehallaNet/Net/Ui/LabelLift.cs:6` (`RiseUnitsPerSecond = 8`, `FallUnitsPerSecond = 3`, `HoldSeconds = .45`, `Deadband = .03`).

## 4. New paths
- `client-unity/UnityProject/Assets/Tests/Editor/CampWardTextGlideEditModeTests.cs` (+ `.meta`), the oracle (this packet's commit).

## 5. Exclusive write scope
- `client-unity/UnityProject/Assets/Scripts/Presentation/CampRespawnLabels.cs` (`DrawBeacons` and, if needed, the small helper it calls)
- Not in scope: `CampLabelStableEditModeTests.cs`, `LabelLiftTests.cs` (the oracle of the base packet; read-only), `LabelLift.cs`, `ArenaView.cs`, `CombatHudPlacement.cs`.
- The file is 459 lines; keep it well under 800.

## 6. Contracts and references
- R-AUTH / ADR 0008: Unity holds no game rule; presentation of server observations only.
- Lane motion rules: unscaled time, instant under reduced motion, nothing allocated in a steady frame (no new per-frame allocation in `DrawBeacons`).

## 7. Behaviour to preserve
- The base packet's 13 `CampLabelStableEditModeTests` (including `TheWardTextStaysAboveThePlaqueOfItsCamp` and `ASteadyFrameAllocatesNothing`) and the existing classes below stay green, unchanged.
- Texts, colours, sort orders, pooling, offsets, the one-frame-old obstacle rects; badge behaviour; the 0.08 gap; the 2- and 3-argument `Draw` (one fresh frame).

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-UI-CAMP-LABEL-STABLE-1-fix --base agent/sonnet/P-UI-CAMP-LABEL-STABLE-1` passes.
- Unity class `CampWardTextGlideEditModeTests` green (5 tests).
- The existing classes stay green:

### Existing tests that pin this code (must stay green, unchanged; none needs an `oracle-amend`)
All build `CampRespawnLabels` and call the 2/3-argument `Draw` (one fresh frame), the four-argument `Draw`, or `Update`:
- `CampLabelStableEditModeTests` (base packet's oracle, 13 tests): the badge glides, holds, lingers, fades, is hidden with the Tab map; the respawn and ward text glide as a fighter walks under them (a ward alone, no badge); `TheWardTextStaysAboveThePlaqueOfItsCamp` (:444-461): ward and badge (both camp index 3) drawn from frame 0, with a fighter walking by from frame 1, every frame the ward plate is at least 0.08 above the drawn plaque and does not overlap it; `ASteadyFrameAllocatesNothing`.
- `NamePackGroup2EditModeTests` (`:244-338`): respawn/ward labels have a dark plate (alpha >= .6, text contrast >= 4.5); with a far name they sit at `WorldPoint(x, y+32)` / `WorldPoint(x, y-48)` within 1e-3; a name at the label's position makes the label not overlap it and stay within 3 units of home.
- `NameLegibleEditModeTests.AnEliteNameDoesNotIntersectItsCampBadgeOrThePackMembersBars` (`:342-378`): one fresh frame of an elite's badge (two icons), no wards: the badge does not intersect the elite's name or bars and stays within 3 units of home.
- `CampLabelsHiddenFix4EditModeTests.RespawnLabelsAreNotDrawnWhileWorldLabelsAreHidden` (`:16-29`): `Used` 1 then 0 when `WorldLabelVisibility.Hidden`.
- `StormInkCombatHudEditModeTests.CampRewardPlaqueKeepsItsExactArtAndClearsTheNearbyHolyHelmet` (`:40-56`): through `ArenaView._campLabels`, one badge row, `BadgesUsed == 1`, icon sprite, plaque clear of the Holy helmet (no wards).
- Library `LabelLiftTests`, `CampBadgesTests`, `CampMarksTests`: unaffected (no library change).

## Oracle files (read-only for implementers)
- `client-unity/UnityProject/Assets/Tests/Editor/CampWardTextGlideEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/CampWardTextGlideEditModeTests.cs.meta`
- `docs/backlog/packets/P-UI-CAMP-LABEL-STABLE-1-fix.md` (this packet)

No stub commit: the tests use only members that exist on the base.

## 9. Validation commands
- `bash .agent-runs/lane-ui-rework/bin/unity-class.sh <worktree> CampWardTextGlideEditModeTests,CampLabelStableEditModeTests,NamePackGroup2EditModeTests,NameLegibleEditModeTests,CampLabelsHiddenFix4EditModeTests,StormInkCombatHudEditModeTests`
- `bash .agent-runs/lane-ui-rework/bin/in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-CAMP-LABEL-STABLE-1-fix --base agent/sonnet/P-UI-CAMP-LABEL-STABLE-1`

## 10. Risk and suggested model tier
Sonnet: a three-line change in one function. The care is in measuring the plaque term from the home rect and in not changing the
first-frame snap.

## 11. Known traps
- The drawn plaque rect is position plus `SpriteRenderer.size`, never `bounds` (a pooled plaque's `bounds` lags a changed size in EditMode; the first frame of a pooled plaque is drawn at its sprite's native size, so the tests warm two frames before reading a size).
- `WantedClear` measures the text at its HOME position; the plaque term is `top + WardGap - baseYMin` with `baseYMin` from that same home measurement, never from the lifted label.
- `TextLift` must be called exactly once per ward per frame, with the combined wanted value (it marks the state `Seen`).
- `Index` matches ward to badge (`CampWardRow.Index`, `CampBadgeRow.Index`): use `w.Index` as today.

## Notes from the oracle author
- Test 5 (`AFreshWardTextOverAPlaqueIsAlreadyClear`) is GREEN on the base: it guards the first-frame snap of the fix.
- Test 3 (`ABadgeThatFlickersLeavesTheWardTextStill`) is red on the base only if the flicker toggles the plaque's presence; the badge row on odd frames is absent but the badge lingers 0.2 s, so the drawn plaque (and the ward text) should not move: see the red list below for what was observed.
- The drawn gap is read as plate `bounds` of the ward label's child "Plate" (a transform-driven rect, as in the base packet's test) against the plaque rect from position and `size`.
- Frames go through the four-argument `Draw` at 1/60 s; "the text rests" is waited out with a fixed frame count (60 frames > hold + travel).

## Red tests on the base
(filled after the run)
