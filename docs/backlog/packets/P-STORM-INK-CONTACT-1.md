# P-STORM-INK-CONTACT-1 — confirmed ink contact

## Goal
Give a confirmed weapon contact a single painted impact drawing with the attacking faction's pigment.

## Dependencies
ArenaView's delivered hit cues, the actor presenter, the existing effects quality settings, and the Storm Ink church pilot.

## Relevant legacy paths
`packages/client/src/render/` and the current Unity combat sparks presenter.

## New paths
One brush texture under `Assets/Resources/StormInk/Combat`, its metadata and the contact EditMode test.

## Exclusive write scope
`CombatSparks.cs`, `ArenaView.Fx.cs`, the new combat resource folder, `StormInkContactTests.cs` and its metadata, and this packet.

## Contracts and references
R-AUTH, C-OBS, ADR 0008. The client can only show a confirmed hit at a presented actor's position.

## Behaviour to preserve
Hit and block feedback, quality switches, reduced motion, target text, original timing, the existing bounded particle pool and reset paths.

## Acceptance checks
One non-passive confirmed hit produces a short painted mark. A block and passive or reduced-motion damage retain their existing feedback. Reset clears the mark.

## Validation commands
Unity EditMode suite, full gate, and real Web church combat round.

## Risk and suggested model tier
Sol implementation, Luna review. A mark must stay close to the actual contact and clear before the following attack.

## Known traps
Server event subjects are victims for Hit. Pigment follows the delivered attacker when its identity is available.
