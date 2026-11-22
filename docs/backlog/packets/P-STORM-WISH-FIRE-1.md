# P-STORM-WISH-FIRE-1: Paint the Fire Blink and trail

- **Serves:** not in the current backend operation plan: owner-approved visual work, 2026-11-19
- **Implementer tier:** implementer
- **Reviewer:** reviewer
- **Integration base:** `codex/storm-ink` combined client snapshot `6f5ecd6202766d0a689162fdac700eb1a1339226`
- **D-rules touched:** D1 n/a (Unity presentation only) · D2 focused Editor oracle · D5/D6/D9 none

## 1. ID and outcome

Draw the confirmed Fire Blink and its delivered trail with original dry-brush pigment textures, localized warm light, and a short contact release. Keep the map endpoints, trail width, lifetime, and cue lifetime tied to server-delivered values. Do not infer fire damage from the generic public DoT bit.

## 2. Dependencies

The combined player presentation snapshot above, including delivered Wish fx/area rows and Fire palette support.

## 3. Relevant legacy paths

- None. This is a presentation change and does not alter Rust outcomes or the wire contract.

## 4. New paths

- `docs/backlog/packets/P-STORM-WISH-FIRE-1.md`

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/Presentation/WishFxArt.cs`
- `client-unity/UnityProject/Assets/Scripts/Presentation/WishFxPresenter.cs`
- `client-unity/UnityProject/Assets/Scripts/Presentation/WishFxPresenter.Areas.cs`
- `client-unity/UnityProject/Assets/Scripts/Presentation/WishFxPresenter.Cues.cs`
- `client-unity/UnityProject/Assets/Scripts/Presentation/WishFxPresenter.Statuses.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/WishFxPresenterEditModeTests.cs`
- `docs/backlog/packets/P-STORM-WISH-FIRE-1.md`

## 6. Contracts and references

- Unity FX are presentation only; server outcomes and visibility remain authoritative.
- Blaze has `cast_ms: 0` in `crates/rh-sim/src/abilities/mod.rs`, so there is no `CastStarted` phase to animate.
- `WireFx.HitKindBurn` identifies burn `SigHit`s, but `WishFxPresenter` receives no such records. Public actor `Dot` does not identify the source.
- Trail field endpoints, half-width, and lifetime come from delivered `AreaView` data; the existing 700 ms landing cue duration is retained.

## 7. Behaviour to preserve

- Preserve event acceptance and actor visibility gates, area geometry/lifetime, landing timing, particle quality caps, reduced-motion handling, and the existing pools.
- Preserve source-specific damage presentation elsewhere. Do not paint generic DoT as Fire.
- No gameplay, protocol, map, sprite actor, or other Wish changes.

## 8. Acceptance checks

- Focused EditMode checks prove actual brush texture pixels are used, the trail renderer follows the delivered endpoints and the cue ends at 700 ms, and generic DoT does not emit Fire actor art.
- Existing area extent, bounds, reduced-motion, quality, and pool checks in `WishFxPresenterEditModeTests` remain green.
- No oracle/capture files are changed.

## Oracle files (read-only for implementers)

- None. The new assertions are source-driven and join the existing focused EditMode suite.

## 9. Validation commands

- `/Applications/Unity/Hub/Editor/6000.0.85f2/Unity.app/Contents/MacOS/Unity -batchmode -projectPath client-unity/UnityProject -runTests -testPlatform EditMode -testFilter Roguehalla.Client.Tests.WishFxPresenterEditModeTests -testResults <path> -logFile <path>`
- `git diff --check`

## 10. Risk and suggested model tier

Low-to-medium: Unity sorting, ground squash, and artist-authored alpha texture proportions affect how the effect reads at gameplay scale. The client has no authoritative fire-source identity on generic DoT.

## 11. Known traps

- A Wish landing event is a point pair only when both ends are visible. Do not draw a fallback beam from a hidden endpoint.
- Do not make a `WishStarted` telegraph for Blaze; its actual cast duration is zero.
- A generic DoT bit may come from Plague or another effect; only the delivered Blaze field itself is safe to style as Fire in this packet.
