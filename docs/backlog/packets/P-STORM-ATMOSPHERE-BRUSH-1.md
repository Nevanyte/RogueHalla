# P-STORM-ATMOSPHERE-BRUSH-1: Hand-inked snow and ember particles

- **Serves:** Not in the frozen plan; owner approved the atmosphere-art packet on 2026-11-19.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `codex/storm-ink` at `138a98e54`
- **D-rules touched:** D1 presentation-only; D2 n/a; D5/D6/D9 none

## 1. ID and outcome

Replace the round procedural flake and ember impostors with sparse, asymmetric dry-brush marks for snow and lofting embers. Keep Frost and Fire on the existing pooled particle layers. Embers hold an upright orientation as they rise; snow marks retain their varied rotation. Existing budgets, density controls, reduced-motion behavior, actor layering, and living-light sprite pool stay intact. The test oracle reads the generated texture alpha masks and exercises the quality switch on the live pool.

## 2. Dependencies

The existing Storm Ink atmosphere implementation in `codex/storm-ink` at `138a98e54`; no new asset pack or zone art dependency.

## 3. Relevant legacy paths

None. This is presentation-only ambient particle art; it changes no gameplay rules or actor data.

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/AtmosphereBrushProfileEditModeTests.cs` and `.meta`
- `docs/backlog/packets/P-STORM-ATMOSPHERE-BRUSH-1.md`

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/View/Atmosphere/AtmosphereParticles.cs`
- `client-unity/UnityProject/Assets/Scripts/View/Atmosphere/ParticlePresets.cs`
- New paths listed above

`LifeLights.cs` was audited but does not require changes: its fireflies and ember glows are a separate fixed-cap sprite pool. No ground shader, ground material, or zone code is in scope.

## 6. Contracts and references

- Presentation only; particle layers expose no gameplay or hidden actor state.
- `client-unity/AGENTS.md`, existing `AtmosphereQuality` switchboard, `ParticlePresets.HardBudget`, and `LifeLights.Cap`.

## 7. Behaviour to preserve

- Keep existing per-preset layer counts, rate, maximum particle counts, tint limits, size limits for above-actor layers, and density budgets.
- Keep screen particles in the existing pooled banks; no per-particle or per-prop GameObject/ParticleSystem creation.
- Low quality and reduced motion continue to disable screen particles; the existing living-light pool remains at 14 sprite objects (9 fireflies, 6 embers maximum under its shared cap).
- Ground and actors remain readable; no new high-opacity or large above-actor layer.

## 8. Acceptance checks

- Before implementation, the new alpha-profile oracle fails on both old round profiles at aspect 1.0; after implementation it verifies elongated, asymmetric alpha masks with dry-brush edge variation.
- The focused EditMode suite passes the profile, unchanged pooled-layer/budget constraints, and reduced-motion shutdown checks.
- Matched gameplay-camera captures use the same `AtmospherePreviewCapture`, `seed1.map.json`, camera positions, 1920x1080 output, High quality, and `?fx.brightness=1.4` before and after. Evidence is outside the repository at `/private/tmp/rh-storm-proof/atmosphere-brush-proof/{before,after}/{frost,fire}.png`; captured logs are `before-unity.log` and `after-unity.log` in that directory. The capture scenes include the player and three remote fighters over Frost snow and Fire lava.
- Structural tests and screenshots do not establish Web frame cost or other hardware performance.

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/AtmosphereBrushProfileEditModeTests.cs`

## 9. Validation commands

- `unity test client-unity/UnityProject --mode EditMode --filter 'Roguehalla.Client.Tests.AtmosphereBrushProfileEditModeTests' --timeout 600 --format json`
- `unity run client-unity/UnityProject --timeout 600 -- -executeMethod Roguehalla.Client.AtmospherePreviewCapture.Run -atmMap <seed1.map.json> -atmOut <output> -atmSpots frost:108:127,fire:20:70 -atmW 1920 -atmH 1080 -atmQuality high -atmUrl '?fx.brightness=1.4'`
- Full project gate is coordinator-owned and was not run for this packet.

## 10. Risk and suggested model tier

Presentation-only, with a small procedural texture profile. Main risks are marks becoming too subtle on bright snow or too obscuring over actors. The fixed gameplay-camera capture and alpha-profile oracle cover these risks at the current review scale.

## 11. Known traps

- `AtmosphereTextures.Get` discards its CPU texture copy after upload; tests use GPU readback to inspect the actual generated alpha mask.
- `AtmosphereQuality` controls particle density independently from the fixed `LifeLights` sprite pool; do not add emitters to compensate for a small visual mark.
- Embers need their authored upright rotation while snow remains randomly rotated.
