# P-STORM-FIRE-AXE-CONTACT-2 — Register Fire axe anticipation and recovery hands

## ID and outcome

Audit and correct the Fire axe champion's painted right-hand registration for both anticipation drawings and both recovery drawings across all eight bearings. The shared axe remains attached to the anatomical fist in the delivered runtime poses.

## Dependencies

- `P-STORM-FIRE-AXE-PALM-1` (the prior release-contact pass; all 16 release registrations remain unchanged).
- Parent baseline `fc3457e6673db79f73f719d6092ebd1ca452b16e`.

## Relevant legacy paths

- `packages/client/src/render/eliteArt.ts` contains legacy elite weapon artwork, verified with `rg`; this packet changes only presentation registration and ports no game rule.

## New paths

- None.

## Exclusive write scope

- `client-unity/UnityProject/Assets/Resources/StormInk/Actors/FireAxeAuthoring.asset`
- `client-unity/UnityProject/Assets/Tests/Editor/StormInkFireAxePaintedContactEditModeTests.cs`
- This packet.

## Contracts and references

- `client-unity/AGENTS.md` — Unity presentation only; Rust remains authoritative.
- `StormInkFireAxeAuthoring.Frame` maps the named anticipation/recovery views to their authored atlas cells.
- `StormInkActors.FireAxeFrame` selects A/B poses from release parity; presentation timing and identities are preserved.

## Behaviour to preserve

- Keep all release A/B registrations and release atlas pixels byte-for-byte unchanged.
- Keep the shared axe sprite, detachable equipment grip, timings, body art, foot pivot, guard/walk registrations, and actor design unchanged.
- Change only the Fire right-hand pixel registration for anticipation and recovery cells when the existing point misses the painted anatomical fist.

## Acceptance checks

- For each of eight bearings, exercise Windup A, Recovery A, Windup B, and Recovery B through `SpriteActorPresenter` and assert the expected authored drawing.
- Independently pin reviewed source-image fist pixels for all 32 cases, verify the registered socket lands on that opaque fist, and verify the transformed painted axe haft intersects the same point at play scale.
- Existing 16-release contact cases and strike art hashes remain green.
- Native normal-camera captures compare the 32 frames with body-only and axe views where needed; guard and walk retain their existing source registrations.

## Validation commands

- `client-unity/UnityProject/Tools/editmode.sh` (targeted Unity Editor cases may be run with the project's documented EditMode filter when available).
- Native capture helper used for the reviewed source and each corrected normal-camera pose.

## Risk and suggested model tier

Low runtime risk; this is serialized presentation data with an independent art-pixel oracle. Review at normal gameplay scale because a socket can touch opaque pixels while still landing on a forearm or garment.

## Known traps

- Atlas coordinates use top-left pixel coordinates while Unity sprite UVs use bottom-left coordinates.
- Checking only that two authored UVs agree can certify a socket on transparent air, a sleeve, or a boot; the oracle must independently identify the visible fist and test painted shaft alpha.
- Row/column mapping is `FireAxe/<row>/<column>`; Windup B is column 6, not Release B at column 5.
