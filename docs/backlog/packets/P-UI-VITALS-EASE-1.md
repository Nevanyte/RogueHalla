# P-UI-VITALS-EASE-1: health and barrier bars ease to their new value, and lost health lingers

- **Serves:** not in the plan: owner direction 2026-11-22 (UI rework lane chat): "make the front end as fluid as possible"
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `lane/ui-rework/base-3` (lane branch over `feature/unity-rework`)
- **D-rules touched:** D1: legacy eases both bars (`packages/client/src/style.css:312`: `.hud-health-fill, .hud-health-barrier { ... transition: width 100ms linear; }`) and has no ghost; Unity has no ease today. This differs (constant rate, ghost): propose a `DEVIATIONS.md` row in the hand-off. D2 n/a. D5/D6 none. D9 untouched.

Evidence: `.agent-runs/lane-ui-rework/understand/fluidity.txt` (defect 6) and `hud-shell.txt` (the plate), in the main checkout. Lane rulings already made: a big FALL never snaps because of its size (only a rise over .6 of the bar snaps); the ghost stays behind the barrier fill; a rise does not cancel a running ghost; only a fall that is damage uses the fall rate; a DEVIATIONS row is proposed; `OwnerVitalBars.Percent` differing from legacy's formula goes into the hand-off as a legacy doubt, unchanged here.

## 1. ID and outcome

**What is wrong today.** `MatchHud.RefreshOwnerPresentation` (`UI/MatchHud.cs:313`) writes `_healthBarFill.anchorMax` and both anchors of `_barrierBarFill` (lines 323-328) straight from `OwnerVitalBars.Percent` of the newest snapshot. It runs only on a frame with `_dirty` set (`HudLateUpdate`, `MatchHud.cs:238`, returns at line 248 before it otherwise), so both bars jump at most 20 times a second and lost health vanishes in one frame. The bars are `OwnerVitalBar/Meter/HealthFill` and `BarrierFill` (`CreatePlate`, `UI/MatchHud.Legacy.cs:95`, bars at lines 113-118): fractional anchors in the 74-unit `Meter`. **The plate is the only writer in this packet.** The owner's overhead bar (`View/ArenaView.cs:260`, drawn through `_actors.Present`) shares only the static `OwnerVitalBars.Percent` (`Presentation/OwnerVitalBars.cs`); the overhead bars of other actors are drawn by `SpriteActorPresenter` and are separate. Both stay as they are.

**Outcome.** The plate's health and barrier bars ease toward the delivered fraction; lost health lingers behind the health fill.

1. **Shown fill.** It moves toward the delivered fraction at a constant rate and never overshoots. It falls at 6 bar-widths per second only when the target is below the shown fill AND the step's ghost flag is true (damage); every other move, the barrier's included, moves at 3 per second in both directions. A move larger than the gap lands exactly on the target.
2. **Ghost, health only.** A second fill, a `HealthGhost` image behind `HealthFill` (draw order: ghost, health, barrier), `UiTheme.With(theme.Danger, .55f)` (in `CreatePlate` this is spelled `Tone(theme.Danger, .55f)`, the helper the plate already uses). When the delivered health falls as damage, the ghost keeps its value .25 s, then falls at 2 bar-widths per second to the shown fill. A further fall during the hold or the fall leaves the ghost where it is and restarts the hold. A rise does not cancel a running ghost: the ghost becomes `max(Ghost, Shown)`, the hold keeps counting, and the ghost then falls to the shown fill as after a fall; a rise that lifts the shown fill above the ghost brings the ghost up with it. Only a step with the ghost flag false sets the ghost to the shown fill and zeroes the hold. The barrier has no ghost.
3. **Snap** (shown and ghost jump to the delivered value): the first value after a bind or `ClearOwnerPresentation`; a timeline reset (`ResetTransientPresentation`); a new life (a delivery with `Status` Alive after one that was not, or a different `SelfView.Id`); a single delivered RISE over .6 (`target - previous delivered target > .6`, strictly, not measured from the shown fill: a respawn or a full heal); and always under `UiMotion.Reduced`, which shows no ghost. A FALL never snaps because of its size: the heaviest hit is the one whose ghost matters most.
4. **Numbers, texts and colours are never eased:** `_vitals`, `_hpValue`, `_combatStats` and the health fill colour (Success, Warning or Danger by delivered share, `RefreshLegacyHud`, `MatchHud.Legacy.cs:304`) show the delivered value when it arrives.
5. **Geometry.** The barrier eases its width. The health fill spans 0 to `HealthShown`; the barrier fill spans `HealthShown` to `min(1, HealthShown + BarrierShown)`; the ghost spans 0 to `HealthGhost`.
6. **A fall that is not damage shows no ghost and does not dip the bar.** `OwnerVitalBars.Percent` divides by `max(MaxHp, hp + barrier)`, so gaining barrier at full health lowers the health fraction (100/100, barrier 0 becomes health .667, barrier .333). When the health target falls and `me.Hp` is not below the previous delivered `Hp`, the ghost flag is false: the ghost equals the shown fill on every frame of that fall, and the health falls at 3 per second like the barrier rises, so `HealthShown + BarrierShown` stays constant and the right edge of the bar does not retreat.
7. **Time.** A per-frame tick with unscaled `dt`, before the `_dirty` gate, moves the fills on every frame. A delivery registers its target with `dt = 0` and moves nothing but a snap. A steady frame allocates nothing, writes no rect.

**Worked numbers** (`BarEase`, `dt` .05, so a fall step is 6 x .05 = .30, a rise or not-damage step 3 x .05 = .15, a ghost step 2 x .05 = .10, a hold step .05).
- Fall: `Snap(1)`, `Step(.7, 0, false)` gives Shown 1, Ghost 1, hold .25. Then `Step(.7, .05, false)` eight times: Shown 1 - .30 = .70 from the first step; Ghost 1.00 for steps 1 to 5 (hold .20, .15, .10, .05, 0); then .90, .80, .70 at steps 6 to 8 (.25 s hold + .15 s fall = .40 s).
- Rise: `Snap(.7)`, `Step(1, 0, false)`, `Step(1, .05, false)` twice: Shown .70 + .15 = .85 and Ghost max(.70, .85) = .85; then 1.00 and 1.00.
- Rise during the hold: after fall steps 1 and 2 (Shown .70, Ghost 1.00, hold .15), `Step(.8, .05, false)` gives Shown .70 + .15 -> lands .80, Ghost max(1.00, .80) = 1.00, hold .15 - .05 = .10. Two more `Step(.8, .05, false)` give Ghost 1.00 (hold .05, then 0); two more give .90, then .80.
- Not damage: `Snap(1)`, `Step(.5, .05, false, false)` gives Shown 1 - .15 = .85 (rate 3, not 6), Ghost .85, hold 0.
- Constant sum: health `Snap(1)` and barrier `Snap(0)`; each step `health.Step(2f/3f, .05f, false, false)` and `barrier.Step(1f/3f, .05f, false, false)` gives (.85, .15), (.70, .30), (.667, .333): the sum is 1.00 each time.
- Jump: `Snap(.3f)`, `Step(.95f, ...)` snaps (a rise of .65). Separately `Snap(.3f)`, `Step(.85f, .05f, false)` eases (a rise of .55): Shown .30 + .15 = .45, Target .85; never test a rise of exactly .6 (float).
- A big fall eases: `Snap(1)`, `Step(.2f, .05f, false)` gives Shown 1 - .30 = .70, Ghost 1.00.
- Restart: after fall step 3 (hold .10), `Step(.5, 0, false)` keeps Ghost 1.00 and sets the hold to .25.
- Plate, `OwnerVitalFills` (MaxHp 100): Hp 100 then Hp 70 (lingers) then six `Tick(.05f)`: health .70, ghost 1.0 for ticks 1 to 5 and .90 at tick 6; a heal to Hp 71 after tick 2 gives Shown .71 and Ghost 1.00 (the hold of .15 keeps running and reaches 0 at tick 5), then .90 at tick 6.
- Barrier at full health: Hp 100/100 to Barrier 50 gives h = 100/150 = .667, b = 50/150 = .333; ticks give health .85, .70, .667 and barrier .15, .30, .333.

## 2. Dependencies

Base `lane/ui-rework/base-3`. `Assets/Tests/Editor/AllocationMeter.cs` (P-UI-ALLOC-METER-1) is on the base (checked). `UiEase.Toward` (`RoguehallaNet/Net/Ui/UiEase.cs`) exists. P-UI-ABILITY-MOTION-1 edits the same two `MatchHud.cs` methods (`HudLateUpdate`, `ResetTransientPresentation`): the coordinator merges one at a time and keeps both insertions.

## 3. Relevant legacy paths
- `packages/client/src/style.css:311-316`: the meter is 8 px, `overflow: hidden`; `.hud-health-fill, .hud-health-barrier` carry `transition: width 100ms linear`; fill colours `#66bb6a`, `#4fc3f7`, low `#ffca28`, critical `#ef5350`. No ghost exists in legacy. (verified with `rg`)

Unity code this changes (all under `client-unity/`):
- `UnityProject/Assets/Scripts/UI/MatchHud.cs:313-329` (`RefreshOwnerPresentation`, the six bar lines 323-328 inside the `if (me.Hp != _lastHp || ...)` block), `:350-363` (`ClearOwnerPresentation`; bar lines 361-362; it sets `_lastHp = NaN`, `_lastBarrier = 0` at line 352), `:205-215` (`ResetTransientPresentation`; `ResetPlayerFlowTimeline();` at 210, the `if (_session == null) ClearOwnerPresentation(...)` at 211), `:238-248` (`HudLateUpdate`; the `ReconnectAttempt` line is 247).
- `UnityProject/Assets/Scripts/UI/MatchHud.Legacy.cs:95-118` (`CreatePlate`), `:304` (`RefreshLegacyHud` health colour).
- `UnityProject/Assets/Scripts/Presentation/OwnerVitalBars.cs` (static `Percent`, unchanged; it differs from legacy's formula: LEGACY DOUBT for the hand-off).
- `UnityProject/Assets/Scripts/UI/UiMotion.cs:8-12` (`UiMotion.Reduced`, test seam `UiMotion.ReducedOverride`).

## 4. New paths
- `client-unity/RoguehallaNet/Net/Ui/BarEase.cs`, its synced copy `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/BarEase.cs` (run `bash client-unity/sync-net.sh` through `in.sh`) and the `.meta` Unity writes for the copy.
- `client-unity/UnityProject/Assets/Scripts/Presentation/OwnerVitalFills.cs` and `client-unity/UnityProject/Assets/Scripts/UI/MatchHud.Vitals.cs`, each with `.meta`.
- Oracle: `client-unity/RoguehallaNet.Tests/BarEaseTests.cs`, `client-unity/UnityProject/Assets/Tests/Editor/OwnerVitalEaseEditModeTests.cs` (with `.meta`).

New non-test C# files need a `/// <summary>` before the first type and at most 800 lines.

### API: `BarEase` (namespace `Roguehalla.Net.Ui`)

```csharp
/// <summary>The shown fraction of a bar and the ghost of lost value behind it, eased toward a target.</summary>
public struct BarEase
{
    public const float FallPerSecond = 6f, RisePerSecond = 3f, GhostHoldSeconds = .25f, GhostFallPerSecond = 2f,
        SnapJump = .6f;
    public float Shown, Ghost, GhostHold;       // GhostHold: seconds of hold left
    public bool Has { get; private set; }       // a value was given since default or Clear
    public float Target { get; private set; }   // the last delivered target, clamped to 0..1
    public void Snap(float target);             // Shown = Ghost = Target = clamp(target); GhostHold = 0; Has = true
    public void Step(float target, float dt, bool reduced);                  // = Step(target, dt, reduced, true)
    /// <summary>ghost false: not damage (a barrier move, a health fall that Hp did not cause): Ghost equals Shown
    /// after the step and the fall rate is not used.</summary>
    public void Step(float target, float dt, bool reduced, bool ghost);
    public void Clear();                        // back to default: the next Step snaps
}
```
`clamp` maps NaN and negatives to 0, above 1 to 1; a `dt` that is NaN or not above 0 counts as 0. `Step`, in order:
1. `t = clamp(target)`. If `!Has`, `reduced` or `t - Target > SnapJump` (a rise only): `Snap(t)`, return.
2. `fell = t < Target`; `Target = t`; `from = Shown`.
3. `Shown = UiEase.Toward(from, t, rate * dt)`; `rate` is `FallPerSecond` when `t < from && ghost`, else `RisePerSecond` (so a fall with `ghost` false, and every barrier step, moves at 3).
4. If `!ghost`: `Ghost = Shown`, `GhostHold = 0`, return. (A rise with `ghost` true does not come here.)
5. If `fell`: `Ghost = max(Ghost, from)`, `GhostHold = GhostHoldSeconds`. Then `Ghost = max(Ghost, Shown)` (on a rise this keeps the ghost and lifts it only when Shown passes it).
6. `rest = dt`; spend `min(GhostHold, rest)` off both; then if `rest > 0` and `Ghost > Shown`: `Ghost = max(Shown, Ghost - GhostFallPerSecond * rest)`.

### API: `OwnerVitalFills` (namespace `Roguehalla.Client`, as `OwnerVitalBars`)

```csharp
/// <summary>Eases the plate's health, ghost and barrier fills toward the delivered owner vitals.</summary>
public sealed class OwnerVitalFills
{
    public OwnerVitalFills(RectTransform health, RectTransform barrier, RectTransform ghost);   // ghost may be null
    public bool Ready { get; }                       // a value was delivered since the last Clear
    public float HealthShown { get; }  public float HealthGhost { get; }  public float BarrierShown { get; }
    public void Deliver(SelfView me, bool reduced);  // null me: Clear()
    public void Tick(float dt, bool reduced);        // nothing while !Ready
    public void Clear();                             // forgets everything and writes all three rects to zero width
}
```
It keeps its two `BarEase` values in non-readonly fields (a struct held in a readonly field is copied on write). `Deliver`: `h`, `b` are `healthPercent / 100`, `barrierPercent / 100` from `OwnerVitalBars.Percent`; on a new life (the health ease `Has` and `me.Id` differs from the remembered `Id`, or `me.Status` is Alive and the remembered one was not) clear both eases first; when `h` differs from the health `Target`, set `lingers = !(h < Target && me.Hp >= previous Hp)` (true while not `Has`; it stays as it was when `h` does not differ). `lingers` is the ghost flag: it is false only for a health fall that Hp did not cause (barrier gained), so a heal or a life-steal tick never wipes a running ghost. Remember `Hp`, `Id`, `Status`; `Step(h, 0, reduced, lingers)` on health, `Step(b, 0, reduced, false)` on barrier; write. `Tick` steps health at its `Target` with the remembered `lingers` and the barrier at its `Target` with ghost false, then writes. A rect is written only when its value changed. Anchors: health `anchorMax = (HealthShown, 1)`; barrier `anchorMin = (HealthShown, 0)`, `anchorMax = (min(1, HealthShown + BarrierShown), 1)`; ghost `anchorMax = (HealthGhost, 1)`.

### Wiring
- New `UI/MatchHud.Vitals.cs` (`public sealed partial class MatchHud`, `/// <summary>` above the type): fields `RectTransform _healthGhostFill`, `OwnerVitalFills _ownerVitals`; `OwnerVitalFills OwnerVitals()` (built on first use from `_healthBarFill`, `_barrierBarFill`, `_healthGhostFill`; null while either of the first two is null); `void DeliverOwnerVitals(SelfView me)`; `internal void TickOwnerVitals(float dt)`; `void ClearOwnerVitals()`. Each passes `UiMotion.Reduced` and does nothing without `OwnerVitals()`. No other partial declares these names.
- `UI/MatchHud.cs` (four edits, nothing else): in `RefreshOwnerPresentation` delete the six lines from `OwnerVitalBars.Percent(...)` to the barrier `anchorMax` write (lines 323-328) and call `DeliverOwnerVitals(me);` after that `if` block (every refresh delivers); in `ClearOwnerPresentation` replace the two `if (_healthBarFill != null)` and `if (_barrierBarFill != null)` lines (361-362) with `ClearOwnerVitals();`; in `ResetTransientPresentation` add `ClearOwnerVitals();` directly before `if (_session == null) ClearOwnerPresentation(...)` (after the `ResetPlayerFlowTimeline();` line); in `HudLateUpdate` add `TickOwnerVitals(Time.unscaledDeltaTime);` directly before `if (_session != null && _session.ReconnectAttempt != ...` (line 247). Net about minus 4 lines (575 now; `Tools/hud-seams.test.cjs` caps the file at 599).
- `UI/MatchHud.Legacy.cs`, `CreatePlate` only: `_healthGhostFill = Rect(inner, "HealthGhost", 0, 0, 0, 1)` with an `Image` colour `Tone(theme.Danger, .55f)` and `raycastTarget = false`, created before `HealthFill` (line 113).

## 5. Exclusive write scope
- New `client-unity/RoguehallaNet/Net/Ui/BarEase.cs`, its synced copy under `UnityProject/Assets/RoguehallaNet/Net/Ui/` and `.meta`; new `UnityProject/Assets/Scripts/Presentation/OwnerVitalFills.cs` and `UI/MatchHud.Vitals.cs` with `.meta`.
- `UI/MatchHud.cs` (the four edits), `UI/MatchHud.Legacy.cs` (`CreatePlate`), this packet.
- Not in scope: `Presentation/OwnerVitalBars.cs`, `View/ArenaView.cs`, `Presentation/SpriteActorPresenter.cs`, other `MatchHud.*.cs` partials, `HudTextScaleEditModeTests.cs`, `Tools/hud-seams.test.cjs`, any oracle file.

## 6. Contracts and references
- R-AUTH / ADR 0008: presentation only; every value is the server's, only its drawing is eased. Nothing is predicted: a delivered target always wins.
- Lane rules `.agent-runs/lane-ui-rework/specs/COMMON.md`: theme colours only, motion on unscaled time, instant under `ReducedMotion`, nothing allocates in a steady frame, `MatchHud` binds and unbinds many times.

## 7. Behaviour to preserve
- The texts (`_vitals`, `_hpValue`, `_combatStats`) and the health fill colour show the delivered value at once.
- The owner's overhead bar (`ArenaView.cs:260`) and other actors' bars are untouched.
- `RefreshOwnerPresentation` keeps every other line, `ClearOwnerPresentation` keeps `_lastHp = NaN`, `_lastBarrier = 0`.
- The `Session.CueSent` subscription and the press pulses stay.
- No wire, protocol or Rust change.

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-UI-VITALS-EASE-1 --base lane/ui-rework/base-3` prints `GATE PASS`.
- `BarEaseTests` and `OwnerVitalEaseEditModeTests` green; the amended `MatchHudWishBarrierEditModeTests` test green.
- If a `MatchHud` member is renamed or moved: `node --test client-unity/UnityProject/Tools/hud-seams.test.cjs` (not expected to be needed).

## Oracle files (read-only for implementers)
- `client-unity/RoguehallaNet.Tests/BarEaseTests.cs` (8 tests, dotnet)
- `client-unity/UnityProject/Assets/Tests/Editor/OwnerVitalEaseEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/OwnerVitalEaseEditModeTests.cs.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/MatchHudWishBarrierEditModeTests.cs` (the `oracle-amend:` commit, see below)
- `docs/backlog/packets/P-UI-VITALS-EASE-1.md`

Stubbed by the `stub:` commit (the implementer replaces them): `client-unity/RoguehallaNet/Net/Ui/BarEase.cs` and its synced copy, `Presentation/OwnerVitalFills.cs`, `UI/MatchHud.Vitals.cs` (with their `.meta`), and the compile-only `_healthGhostFill` wiring noted in "Notes from the oracle author".

## Existing tests that pin this code (verified by reading each)
- `MatchHudWishBarrierEditModeTests.OwnerFactsRenderAndClearAcrossWishAndSessionResetTransitions` (`Assets/Tests/Editor/MatchHudWishBarrierEditModeTests.cs`). Sets `_healthBarFill` and `_barrierBarFill` by reflection on a bare `MatchHud` (no plate, no ghost). Asserts after the first refresh at Hp 60, Barrier 25: `_health.anchorMax.x` .60, `_barrier.anchorMin.x` .60, `_barrier.anchorMax.x` .85 (the first value snaps: stays green); after `me.Barrier = 0` and one refresh: `_barrier.anchorMax.x == _barrier.anchorMin.x`. **This one turns red**: the barrier now eases from .25 and reads .85 against .60. **Amendment (`oracle-amend:` commit, for the owner to approve):** insert `Invoke("TickOwnerVitals", 1f);` before that assertion (a 1 s tick at 3 per second lands the barrier on 0). Its final asserts (`ClearOwnerPresentation` leaves both `anchorMax.x` zero) stay green (`ClearOwnerVitals` zeroes).
- `MatchHudWishBarrierEditModeTests.OwnerVitalPercentagesPreserveDeliveredSharesAndHandleInvalidInput`: pins static `OwnerVitalBars.Percent` (null, 80/100+25, NaN). Unchanged, stays green.
- `MatchHudIconPresentationEditModeTests.ChampionHudSitsBottomLeftAndOwnerLossClearsWeaponAndUnavailableValues` (`:57`): a full plate; `RefreshOwnerPresentation` at Hp 70 then `ClearOwnerPresentation`; asserts champion-panel texts only, not the bars. Stays green (the plate gains one child, the ghost).
- `MatchHudStatusNotesEditModeTests` (`:68`) and `MatchHudStatusTextsEditModeTests` (`Frame()` at `:75`): call `RefreshOwnerPresentation` through a real `Session`; assert texts, no bar. Stay green. `MatchHudIconPresentationEditModeTests:123` likewise (summary and hint texts).
- `SpriteActorBarrierEditModeTests`, `OverheadLayoutTests`: pin the overhead bars of actors, a different writer. Unaffected.
- `HudLegacyLayoutEditModeTests`, `HudTextScaleEditModeTests` build the plate through `CreateLegacyChrome`; the ghost is one more `Image` child of `Meter` (no label, no text): they assert layout rects and text fit, so they stay green. A test that counts the plate's Images, or checks "every HUD graphic uses a theme colour", must see `theme.Danger` through `Tone`.
- `UiMotion.Reduced` / `ReducedOverride` is the seam; `UiEaseTests` pins `UiEase.Toward` (used, unchanged).

## 9. Validation commands
- `in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter BarEaseTests`
- `unity-class.sh <worktree> OwnerVitalEaseEditModeTests,MatchHudWishBarrierEditModeTests,MatchHudStatusTextsEditModeTests,MatchHudStatusNotesEditModeTests,MatchHudIconPresentationEditModeTests`
- `in.sh <worktree> bash client-unity/sync-net.sh` after editing the library.
- `in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-VITALS-EASE-1 --base lane/ui-rework/base-3` (about ten minutes, once, at the end).

## 10. Risk and suggested model tier
implementer-sonnet. The arithmetic is a small state machine with worked numbers. The risks are the ghost rules (rise keeps the ghost; barrier gain at full health is not damage) and the wiring order (tick before the `_dirty` gate, `ClearOwnerVitals` on every clear path).

## 11. Known traps
- **AllocationMeter.** `Assets/Tests/Editor/AllocationMeter.cs` is the only allocation meter: `GC.GetAllocatedBytesForCurrentThread()` reads 0 in this editor.
- **Build `OwnerVitalFills` on first use.** `MatchHudWishBarrierEditModeTests` sets `_healthBarFill` and `_barrierBarFill` by reflection and never builds the plate (`_healthGhostFill` is null there), so the class must work with a null ghost.
- **`TickOwnerVitals` runs before the `_dirty` early return**, or the bars move only on snapshot frames. On the frame a snapshot arrives the tick runs before the delivery, so a bar starts moving one frame late; accepted.
- **The delivery runs on every dirty frame:** its `Step(..., 0, ...)` must be a no-op for an unchanged target. Do not compare with `_lastHp` or `_lastBarrier`: `ClearOwnerPresentation` sets `_lastHp` to NaN and `_lastBarrier` to 0, and `Bind` sets both to NaN. `Session.Me` is one reused `SelfView`: copy values out (remember Hp, Id, Status by value). `ClearOwnerPresentation` runs in `Bind`, `ShowMenu` and for a spectator: each must forget the eases.
- A struct in a readonly field is copied on write: keep both `BarEase` values in plain fields.
- The delivered "rise" that snaps is measured from the previous delivered target, not from the shown fill.
- Other packets edit `HudLateUpdate` and `ResetTransientPresentation`: add only the lines above, at the anchors named. The packet branch stays linear: never merge another branch into `packet/P-UI-VITALS-EASE-1`.
- Hand-off must carry: a proposed DEVIATIONS row (constant-rate ease plus ghost; legacy has a 100 ms linear width transition and no ghost) and the LEGACY DOUBT that `OwnerVitalBars.Percent` (divides by `max(MaxHp, hp + barrier)`) differs from legacy's `hud.ts` formula, left unchanged.

## Notes from the oracle author
Red tests on the stub, each on an assertion about behaviour (stub: `BarEase.Step` and `Snap` do nothing, `OwnerVitalFills` writes nothing, the `MatchHud.Vitals.cs` methods are empty and `MatchHud.cs` still writes the bars directly):
- `BarEaseTests` (8, dotnet): all red, first assertion `Expected: .5 +/- .001, But was: 0` (a value of the ease stays 0).
- `OwnerVitalEaseEditModeTests` (11, Unity): all red. Group A tests (`TheFirstDeliverySnapsAndWrites`, `DamageEasesAndTheGhostLingers`, `AHealKeepsARunningGhost`, `TheBarrierEasesItsWidth`, `GainingBarrierAtFullHealthShowsNoGhostAndNoDip`, `ANewLifeSnaps`, `ABigHitEasesABigHealSnapsAndReducedMotionSnaps`, `ClearZeroesAndForgets`, `ASettledFrameAllocatesNothingAndWritesNoRect`) fail with an anchor reading .5 (the default of an unwritten `RectTransform`) or 0 against the expected eased value, or `Ready` false. `ARefreshDeliversAndATickMoves` fails on "the delivery moves nothing" (old code writes .70 at once: `Expected 1.0, But was .70`). `AMidEaseTimelineResetSnaps` fails on "mid-ease, so the premise holds" (`Expected .85, But was .70`).
- `MatchHudWishBarrierEditModeTests.OwnerFactsRenderAndClearAcrossWishAndSessionResetTransitions` is GREEN on the stub with its amendment (the old code writes the barrier at once, and `TickOwnerVitals` exists as an empty method); it turns red without the amendment once the barrier eases. The amendment is in its own `oracle-amend:` commit, for the owner to approve.

Existing classes run on the stub and green: `MatchHudWishBarrierEditModeTests` (all, with the amendment), `MatchHudStatusTextsEditModeTests`, `MatchHudStatusNotesEditModeTests`, `MatchHudIconPresentationEditModeTests` (25 of 36 tests green; the 11 red are the new class).

Checks done before this hand-off:
- `BarEaseTests`: a scratch reference implementation of the spec's `Step` (not committed, reverted) passed all 8, so every worked number in the tests is consistent with the rules.
- `OwnerVitalEaseEditModeTests`, `MatchHudWishBarrierEditModeTests`: the same scratch reference (`OwnerVitalFills`, `MatchHud.Vitals.cs`, the four `MatchHud.cs` edits, not committed) passed all 16 tests in Unity, so each line of every test can run and the set-up (rig, reflection, `Fix3Transport`, `ResetTransientPresentation` on a half-built HUD) is sound.
- Scratch run with a catch-and-collect helper (not committed; every assertion statement wrapped, failures listed in TearDown) on the stub: all 11 tests ran to their last line; every collected entry was an assertion about the missing behaviour (eased anchor values, ghost values, `Ready`, `HealthShown`, zeroed rects, "premise holds"). One exception: in that scratch run `AllocationMeter.AssertNone` reported its own self-check failure ("it reported allocation for a known clean control") after earlier failures had been caught in the same test; that is not an assertion about the feature, and I have not established its cause. In the plain run, and on the scratch reference, the test reaches the meter only with the correct behaviour and passed. If an implementer sees the meter complain, it is the meter's own check: report it, do not write another meter.
- Notes on the tests as written: `AMidEaseTimelineResetSnaps` builds its own rig (the `MatchHudStatusTextsEditModeTests` one, with `Fix3Transport` and a `Snapshot(hp)` helper of its own, since that class's helper takes no Hp) and replaces `_healthBarFill`, `_barrierBarFill`, `_healthGhostFill` by bare rects. `TickOwnerVitals` is called through reflection (`internal`). Group A reads `anchorMax.x`, so a freshly created rect reads .5, not 0: an implementation must write the rect, a missing write cannot pass.
- Nothing in the spec was changed; the tests follow its list one for one (test 9's last paragraph is in `ASettledFrameAllocatesNothingAndWritesNoRect`).

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
