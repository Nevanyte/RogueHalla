# P-UI-MOTION-1: one motion helper and one HUD clock, as new files

- **Serves:** not in the plan: owner direction 2026-11-22 (UI rework lane chat): "make the front end as fluid as possible".
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** feature/unity-rework
- **D-rules touched:** D1 n/a (presentation) · D2 n/a · D5/D6 none · D9 untouched: the clock only animates displays that
  already show server facts; it predicts no outcome and nothing is sent from it.

## 1. ID and outcome
Nothing in the in-match HUD has a show or hide transition (every panel is `SetActive`), there is no shared motion helper
(reduced motion, `ShowcaseSettings.Current.ReducedMotion`, is read ad hoc, e.g. `UI/MatchHud.cs:210` and
`UI/LaunchBackdrop.cs:118`), a pressed tile pops from its corner (`HudUi.Place`, `UI/HudUi.cs:46-49`, sets the pivot to
the anchor, and `MatchHud.TickPulses`, `UI/MatchHud.cs:205-224`, scales about the pivot), and countdowns move only when a
snapshot arrives because `Session.ServerTimeMs` (`RoguehallaNet/Net/Session.cs:131`) is the newest snapshot's time.
This packet adds the tools only, as three new files: `UiEase` and `HudClock` (pure library) and `UiMotion` (`UiMotion`,
`HudFade`, `HudPulses`; Unity). No existing source file changes; later packets use them. `MatchHud.cs` keeps its own
pulse code (and the P-UI-INPUT-CUES-1 `Session.CueSent` subscription) until P-UI-ABILITY-MOTION-1.

## 2. Dependencies
none (base `feature/unity-rework` already has P-UI-INPUT-CUES-1).

## 3. Relevant legacy paths
None: presentation-only, no legacy rule is ported (Unity-side UI helpers).

## 4. New paths
- `client-unity/RoguehallaNet/Net/Ui/UiEase.cs` and `HudClock.cs` (pure, namespace `Roguehalla.Net.Ui`), plus their synced
  copies under `client-unity/UnityProject/Assets/RoguehallaNet/Net/Ui/` (run `bash client-unity/sync-net.sh` through
  `in.sh`) and the `.meta` files Unity writes for them.
- `client-unity/UnityProject/Assets/Scripts/UI/UiMotion.cs` (+ `.meta`), namespace `Roguehalla.Client` like the other
  `UI/` files. Needs a `/// <summary>` before its first type and at most 800 lines (readability gate).
- Tests (oracle): see "Oracle files".

### API (exact)
```csharp
/// <summary>Easing and stepping for HUD motion; pure functions of time.</summary>
public static class UiEase
{
    public static float Clamp01(float t);
    public static float OutCubic(float t);        // 1 - (1-t)^3 on [0,1], clamped
    public static float InOutSine(float t);       // (1 - cos(pi t)) / 2 on [0,1], clamped
    public static float Pop(float t);             // sin(pi t) on [0,1]; 0 outside: 0 at both ends, 1 at t = .5
    public static float Toward(float current, float target, float maxDelta);   // never overshoots; maxDelta < 0 counts as 0
    /// next alpha of a fade: toward 1 over inSeconds when show, toward 0 over outSeconds otherwise; with reduced,
    /// exactly 0 or 1. A duration of 0 or less is instant.
    public static float FadeStep(float alpha, bool show, float dt, float inSeconds, float outSeconds, bool reduced);
    /// next value of a bar easing toward its target: closes the gap in about `seconds`, never overshoots,
    /// jumps when the gap is under epsilon or reduced is on.
    public static float Approach(float current, float target, float dt, float seconds, bool reduced, float epsilon = .001f);
}

/// <summary>Server time for HUD countdowns, advanced between snapshots so sweeps and bars move every frame.</summary>
public sealed class HudClock
{
    public const double MaxAheadMs = 150;
    public bool Ready { get; }                                   // false until the first OnServerTime
    public void Reset();
    public void OnServerTime(double serverMs, double localMs);   // call when the newest snapshot's server time is seen to change
    public double Now(double localMs);                           // server ms to draw countdowns at; 0 when not Ready
}

public static class UiMotion
{
    public static bool? ReducedOverride;      // tests set it; null in play
    public static bool Reduced { get; }       // ReducedOverride ?? (ShowcaseSettings.Current != null && ShowcaseSettings.Current.ReducedMotion)
}

public sealed class HudFade
{
    public const float InSeconds = .12f, OutSeconds = .25f;
    public HudFade(RectTransform root, bool interactive, float inSeconds = InSeconds, float outSeconds = OutSeconds);
    public bool Shown { get; }                // the wanted state
    public float Alpha { get; }               // what is drawn
    public bool Active { get; }               // root.gameObject.activeSelf
    public void Set(bool shown);              // starts the fade; activates the object at once when shown
    public void Snap(bool shown);             // no fade
    public void Tick(float dt);               // once per frame while Active or Shown; does nothing and allocates nothing otherwise
}

public sealed class HudPulses
{
    public const float Seconds = .15f, Pop = .08f;
    public void Play(RectTransform rect, float now);   // a rect already pulsing restarts; null is ignored
    public void Tick(float now);                        // unscaled time
    public void Clear();                                // every rect back to scale 1 and its own position
    public int Count { get; }
}
```

### Rules
- `HudClock`: `Now(local) = lastServer + clamp(local - arrivalLocal, 0, MaxAheadMs)`, and never lower than the previous
  answer until `Reset`. `OnServerTime` with a server time lower than the last one is a new timeline: it resets first.
  A repeated `OnServerTime` with the same server time changes nothing (the arrival stamp stays). A snapshot that arrives
  with a server time behind the extrapolated answer does not make `Now` go back: `Now` waits until the new base passes
  it. Local time going backwards counts as no time passing.
- `HudFade` adds a `CanvasGroup` to the root if it has none. `Set(true)`: the object is activated, alpha rises from its
  current value. `Set(false)`: `blocksRaycasts` and `interactable` are false at once, alpha falls from its current value,
  and the object is deactivated when alpha reaches 0. While shown, `blocksRaycasts` and `interactable` are true only if
  `interactive` is true: a non-interactive fade never takes the pointer. With `UiMotion.Reduced`, `Set` behaves as
  `Snap`. A `Set` to the state already wanted does nothing.
- `HudPulses`: during a pulse the rect's `localScale` is `1 + Pop * UiEase.Pop(t)` and its position is shifted so the
  centre of the rect stays where it was, whatever its pivot. When the pulse ends, on `Clear`, and under
  `UiMotion.Reduced`, the scale is exactly 1 and `anchoredPosition` is exactly what it was before `Play`. A destroyed rect
  is dropped without an error. `Tick` with no pulse allocates nothing. Pulses need a point-anchored rect (anchorMin ==
  anchorMax, as `HudUi.Place` makes); the tests assert that case. Centre in parent space = `anchoredPosition +
  (0.5 - pivot) * size * scale`.

## 5. Exclusive write scope
- The new files of section 4 only (the three sources, the synced copies of the two library files, all `.meta`).
- No existing source file. In particular not `UI/MatchHud.cs`, `UI/HudUi.cs`, `Session.cs`.

## 6. Contracts and references
- Presentation only (AGENTS.md: Unity holds no game rule). Motion uses unscaled time and is instant under reduced motion.
- No `Update` method and no MonoBehaviour: the owner ticks these objects, so a hidden HUD costs nothing; steady-frame
  ticks allocate nothing; `CanvasGroup.alpha` is written only while it changes.
- A destroyed `RectTransform` compares equal to null in Unity: test `rect == null` before use.

## 7. Behaviour to preserve
- Nothing existing changes: no HUD behaviour, no wire, no `crates/`. `Session.ServerTimeMs` unchanged.

## 8. Acceptance checks
- `scripts/gate.sh --quick --oracle packet/P-UI-MOTION-1` passes (prints `GATE PASS`).
- dotnet `UiEaseTests` and `HudClockTests` green; Unity `UiMotionEditModeTests` green; `sync-net.sh --check` clean.

## Existing tests that pin this code
Searched the dotnet and EditMode tests for `UiEase`, `HudClock`, `UiMotion`, `HudFade`, `HudPulses` (no hits: new names)
and for the MatchHud pulse members (`_pulses`, `TickPulses`, `ClearPulses`, `PulseSeconds`, `PulsePop`): no test
references them. `ChampionPanelNoGarbageEditModeTests` is only the pattern for the allocation measurement
(`GC.GetAllocatedBytesForCurrentThread`, warm up 5 calls, 200 calls, `Is.LessThanOrEqualTo(0)`). Because this packet
changes no existing file, no existing expectation changes; no `oracle-amend:` commit. Readability/theme gate tests apply
to the new `UiMotion.cs` (summary before first type, <= 800 lines).

## Oracle files (read-only for implementers)
- `client-unity/RoguehallaNet.Tests/UiEaseTests.cs`
- `client-unity/RoguehallaNet.Tests/HudClockTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/UiMotionEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/UiMotionEditModeTests.cs.meta`

## 9. Validation commands
- `in.sh <worktree> dotnet test client-unity/RoguehallaNet.Tests --filter "UiEaseTests|HudClockTests"`
- `unity-class.sh <worktree> UiMotionEditModeTests`
- `in.sh <worktree> bash client-unity/sync-net.sh` then `--check`.
- `in.sh <worktree> scripts/gate.sh --quick --oracle packet/P-UI-MOTION-1`

## 10. Risk and suggested model tier
Low: new self-contained files, pure maths plus one Unity helper. implementer-sonnet; the risk is pivot-shift algebra in
`HudPulses` and exact restoration of `anchoredPosition`.

## 11. Known traps
- Unity null check on destroyed `RectTransform`.
- Restore `anchoredPosition` exactly: store the original at `Play`, never accumulate shifts (a restarted pulse keeps the
  first original).
- A pulse on a rect the owner moved during the pulse is not handled: out of scope.
- `HudFade.Tick` does nothing and allocates nothing when not Active and not Shown.

## Notes from the oracle author
Run on the stub (all red on assertions, none on a compile error or missing member):
- dotnet `UiEaseTests` (13 tests) and `HudClockTests` (8): 21 of 21 failed on value assertions (e.g. `Clamp01(.25)` gave 0,
  `Ready` stayed false, `Now` gave 0).
- Unity `UiMotionEditModeTests`: 13 of 13 failed on assertions (e.g. "a hidden panel is deactivated", "HudFade adds a
  CanvasGroup to its root", pulse count 0 instead of 1, scale not above 1 mid pulse).
- Existing classes: none pin this code (new files only), so there is no existing class to re-run.

Where the tests go beyond or differ from the spec text:
- Pulse tests use a point-anchored rect (anchorMin == anchorMax, as `HudUi.Place` makes), with the three pivots of the
  spec; a stretched-anchor rect is not tested (the packet states pulses need a point anchor).
- `APulseScalesAboutTheCentreWhateverThePivot` also pins the formula `scale = 1 + Pop * sin(pi t)` at 14 points (1e-3),
  the end state (scale exactly 1, `anchoredPosition` exactly the original) and that a finished pulse leaves `Count` 0.
- `ReducedMotionPulseDoesNotScale` ends with a normal pulse after reduced motion is turned off: without it the test
  would pass on the stub (a pulse that does nothing is also "not scaled"). It does not pin whether a reduced pulse is
  counted in `Count`.
- `AnIdleTickAllocatesNothing` first asserts a settled shown fade (Alpha 1) and a settled hidden fade (inactive), for the
  same reason (no pass on the stub); the measurement is the `ChampionPanelNoGarbageEditModeTests` pattern.
- Extra tests: `SettingTheWantedStateAgainDoesNothing`, `AFadeUsesAnExistingCanvasGroupAndAddsOnlyOne`,
  `ARestartedPulseRestartsTheClockAndKeepsTheFirstOriginal` (rules of the packet that had no listed test).
- `HudClockTests.BetweenSnapshotsNowRisesWithLocalTime` is ordered so that the "never lower than the previous answer"
  rule does not contradict its expectation (a snapshot is applied before `Now` is asked past it).
- `UiEase.Approach` is pinned only by behaviour (a frame moves part of the gap, no overshoot, gap at most 15 percent
  after `seconds`, exact landing, jump under epsilon or reduced, no move at dt 0), not by a formula.

## Notes from previous attempts
<!-- Coordinator appends on escalation. -->
