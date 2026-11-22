# P-STORM-PILOT-REGRESSIONS-1 — preserve church roof and contact regressions

- **Serves:** ORDER_OF_OPERATIONS phase 2, item 16. Owner-approved Storm Ink revised church pilot.
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** revised church pilot 6a997d682
- **D-rules touched:** D1 presentation, D2 n/a, D9 server-confirmed contact.

## 1. ID and outcome
The carved rear doorway keeps its three former wall tiles in the church's logical roof footprint. Both door openings remain visually notched and keep the roof opaque when an actor stands on the threshold. Confirmed contact emits one brief mark under the applied atmosphere quality policy even after a different graphics preset was tested.

## 2. Dependencies
P-STORM-CHURCH-MAP-1, P-UNITY-LANDMARK-SEAMS-1 and P-STORM-INK-CONTACT-1.

## 3. Relevant legacy paths
`packages/client/src/render/FxView.ts:392` dispatches authority-delivered combat cues. The original game has no signature church (DEV-020).

## 4. New paths
This packet and a focused EditMode quality regression test.

## 5. Exclusive write scope
ChurchLandmark.cs, AtmosphereQuality.cs, CombatSparks.cs. Extend only the LandmarkSeamsEditModeTests church fixture decoder for the newly approved blueprint glyph; its assertions and pins remain intact. The original contact oracle remains intact.

## 6. Contracts and references
ADR 0028, P-UNITY-LANDMARK-SEAMS-1 (exact cover mask), C-OBS and R-AUTH.

## 7. Behaviour to preserve
Cover is the historical 319-tile church footprint across all eight delivered orientations, excluding 24 headstone tiles and the front threshold. Roof fade stays on interior/wall tiles. Contact pigment stays tied to confirmed hits and excludes block transitions, passive hits and reduced-motion settings.

## 8. Acceptance checks
Existing LandmarkSeams, ChurchLandmark and StormInkContact fixtures pass. Editing the builder back to the 316-tile mask fails the same cover oracle, and the new test catches the independently mutable graphics gates.

## Oracle files
- Existing LandmarkSeamsEditModeTests and StormInkContactTests.
- New StormInkPilotRegressionTests.

## 9. Validation commands
`client-unity/UnityProject/Tools/editmode.sh` and the full integrated gate.

## 10. Risk and suggested model tier
Sonnet. Roof footprint and actor-triggered fade are separate masks, and the eight rotations and reflections must classify the new rear run identically.

## 11. Known traps
The rear `R` glyph is plain traversable floor in the current blueprint. Treating it as a collision wall would accidentally make the fixture prove the old map. The gate's unrelated Rust mapgen failures are scoped to the separate generator packet.
