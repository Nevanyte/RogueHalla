# P-STORM-ATMOSPHERE-BRUSH-1: Hand-inked snow and ember particles

- **Serves:** Not in the frozen plan; owner approved the atmosphere-art packet on 2026-11-19.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `codex/storm-ink` at `138a98e54`
- **D-rules touched:** D1 presentation-only; D2 n/a; D5/D6/D9 none

## 1. ID and outcome

Replace the round procedural flake and ember impostors with sparse, asymmetric dry-brush marks for snow and lofting embers. Keep Frost and Fire on the existing pooled particle layers. Embers hold an upright orientation as they rise; snow marks retain their varied rotation. A dedicated Universal2D transparent pass carries brush UV, particle vertex colour, and bank fade to the actual 2D renderer. Prewarm at the last known camera pose, then replace the pool only if the first camera tick reveals a teleport or an off-frustum particle bank. Existing budgets, density controls, reduced-motion behavior, foreground alpha/size limits, actor layering, and living-light sprite pool stay intact.

## 2. Dependencies

The existing Storm Ink atmosphere implementation in `codex/storm-ink` at `138a98e54`; no new asset pack or zone art dependency.

## 3. Relevant legacy paths

None. This is presentation-only ambient particle art; it changes no gameplay rules or actor data.

## 4. New paths

- `client-unity/UnityProject/Assets/Tests/Editor/AtmosphereBrushProfileEditModeTests.cs` and `.meta`
- `client-unity/UnityProject/Assets/Resources/StormInk/AtmosphereParticles2D.shader` and `.meta`
- `client-unity/UnityProject/Assets/Tests/Editor/AtmosphereParticleRenderEditModeTests.cs` and `.meta`
- `docs/backlog/packets/P-STORM-ATMOSPHERE-BRUSH-1.md`

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/View/Atmosphere/AtmosphereParticles.cs`
- `client-unity/UnityProject/Assets/Scripts/View/Atmosphere/ParticlePresets.cs`
- New `Universal2D` particle shader and focused rendering/prewarm oracle listed above.
- New paths listed above

`LifeLights.cs` was audited but does not require changes: its fireflies and ember glows are a separate fixed-cap sprite pool. No ground shader, ground material, or zone code is in scope.

## 6. Contracts and references

- Presentation only; particle layers expose no gameplay or hidden actor state.
- `client-unity/AGENTS.md`, existing `AtmosphereQuality` switchboard, `ParticlePresets.HardBudget`, and `LifeLights.Cap`.

## 7. Behaviour to preserve

- Keep existing per-preset layer counts, rate, maximum particle counts, density budgets, and the existing above-actor guards (tint alpha ≤ .8, size ≤ .5, count ≤ 30).
- Keep screen particles in the existing pooled banks; no per-particle or per-prop GameObject/ParticleSystem creation.
- Low quality and reduced motion continue to disable screen particles; the existing living-light pool remains at 14 sprite objects (9 fireflies, 6 embers maximum under its shared cap).
- Seeded prewarm remains immediate for pool readiness, then gets repeated only after a large camera move or an off-frustum prewarm. Update emitter shape to the current orthographic camera before that replacement prewarm.
- Ground and actors remain readable; no new high-opacity or large above-actor layer.

## 8. Acceptance checks

- Before implementation, the new alpha-profile oracle fails on both old round profiles at aspect 1.0; after implementation it verifies elongated, asymmetric alpha masks with dry-brush edge variation.
- The unchanged profile oracle passes both texture masks, the pooled-layer/budget constraints, and reduced-motion shutdown. The new renderer oracle fails against the prior sprite material and passes with the Universal2D pass; it also simulates a remote region change and proves emitters and bounds reset around the live camera rather than remaining near the prior pose.
- Matched normal-camera gameplay captures use `seed1.map.json`, Frost `(108,127)` and Fire `(20,70)`, 1920x1080 output, High quality, `?fx.brightness=1.4`, and a 51-frame 25fps sequence (2.04s). Successive frames show distinct snow brush strokes and ember flecks over terrain without covering the player. Durable GIFs and frame crops are outside the repository at `/private/tmp/rh-storm-proof/elemental-light-evidence/{frost,fire}-airborne-strokes-detail.gif`; source frames are in `final-verified-frames/`, and the capture log is `final-verified-capture.log`.
- Structural tests and screenshots do not establish Web frame cost or other hardware performance.

## Oracle files (read-only for implementers)

- `client-unity/UnityProject/Assets/Tests/Editor/AtmosphereBrushProfileEditModeTests.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/AtmosphereParticleRenderEditModeTests.cs`

## 9. Validation commands

- `unity test client-unity/UnityProject --mode EditMode --filter 'Roguehalla.Client.Tests.AtmosphereBrushProfileEditModeTests' --timeout 600 --format json`
- `Unity -batchmode -projectPath client-unity/UnityProject -runTests -testPlatform EditMode -testFilter Roguehalla.Client.Tests.AtmosphereParticleRenderEditModeTests`
- `unity run client-unity/UnityProject --timeout 600 -- -executeMethod Roguehalla.Client.AtmospherePreviewCapture.Run -atmMap <seed1.map.json> -atmOut <output> -atmSpots frost:108:127,fire:20:70 -atmW 1920 -atmH 1080 -atmQuality high -atmUrl '?fx.brightness=1.4'`
- Full project gate is coordinator-owned and was not run for this packet.

## 10. Risk and suggested model tier

Presentation-only, with a small procedural texture profile. Main risks are marks becoming too subtle on bright snow or too obscuring over actors. The fixed gameplay-camera capture and alpha-profile oracle cover these risks at the current review scale.

## 11. Known traps

- `AtmosphereTextures.Get` discards its CPU texture copy after upload; tests use GPU readback to inspect the actual generated alpha mask.
- `AtmosphereQuality` controls particle density independently from the fixed `LifeLights` sprite pool; do not add emitters to compensate for a small visual mark.
- Embers need their authored upright rotation while snow remains randomly rotated.
