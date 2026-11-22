# P-STORM-CHURCH-ALIGN-1 — aisle and pew alignment

## Goal
Carry the approved overhead church floor and its paired rows of pews through every shipped orientation.

## Dependencies
P-STORM-CHURCH-ART-1 and the public church wall footprint.

## Relevant legacy paths
The existing church mechanism is a signature-map deviation under DEV-020.

## New paths
One original slate texture and its importer under the Storm Ink church resources.

## Exclusive write scope
ChurchLandmark.cs, ChurchLandmarkEditModeTests.cs (one additional oracle), the SlateWash PNG and its metadata, this packet and the art receipt.

## Contracts and references
R-VIS, C-OBS, R-AUTH, DEV-020 and the owner-selected overhead church study.

## Behaviour to preserve
Both entrances, whole roof coverage and fade, walk-through pews, furnishings sized to the authoritative room and the original movement and combat rules.

## Acceptance checks
Every pew spans its bank across the nave, the pulpit stands upright and the slate aisle follows both doors in all eight orientations.

## Validation commands
Focused church EditMode tests, full project gate, and the live reference church browser round.

## Risk and suggested model tier
Sol implementation, Luna review. Check the camera's 0.78 vertical projection on a sideways nave.

## Known traps
The map rotates the architectural plan. Its rotation does not turn the vertical axis of a painting of a standing lectern.
