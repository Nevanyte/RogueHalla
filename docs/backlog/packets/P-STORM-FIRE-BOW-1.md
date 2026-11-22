# P-STORM-FIRE-BOW-1 — Restored Fire and the shared bow

## ID and outcome

Phase 3, item 16a. Fire shoots first from a quick standing draw and then from a low crouch. Keep her left hand on the shared bow and pull its string to her anatomical right hand. Review the east-facing pair in Unity before painting the other seven views.

## Dependencies and existing evidence

Build on P-STORM-FIRE-SWORD-1 and the shared bowstring presentation. The old bow uses one forward-reaching body. Its Web baseline shows two eastward arrows, each ten server ticks after the owner's draw began. Review the newly painted Fire beside that baseline at the same camera scale.

## Source and timing

Rust selects the Draw action in `crates/rh-sim/src/systems/weapons.rs`. The owner's draw start comes from the delivered combat state. A newly observed arrow from this shooter starts the release. Unity's existing `CombatSparks`, `SpriteActorPresenter` and `StormInkBowstring` handle the presentation. Keep their projectile ownership, proximity, visibility and cancellation checks. The existing bow image also serves dropped items and every other faction.

## Exclusive write scope

Fire's bow body paintings, artist-owned action and grip definitions, related Fire body and shared-equipment presentation code, and the editor path for those definitions. Include ArenaView’s projectile observation order, the same-frame bow and wand regression fixtures, this packet, and the eight Fire bow tracker entries. Reuse Fire's existing guard, walk and foot anchor. The delivered arrow, weapon timing, collisions and damage stay authoritative.

## Failing oracle and acceptance

The current bow cannot show a standing draw followed by a low crouched draw. Check both actions through the normal presenter, including full tension before release, the return to guard, and a cancelled draw. Inspect the left palm against the riser, the right palm against the drawn string, and both nocks at gameplay size. Compare actual server draw and arrow receipts with captured browser frames. Check all eight headings on bright and dark ground, pickup, drop and re-equip, then run the integrated gate. Track the initial east-facing proof separately while the other seven bearings still use the existing artwork.

In the 1440p Web review, the first visible arrow briefly showed a guard drawing before the release. Sample the same frame through the joined membership, collections and actor presentation path. Verify a visible owner releases immediately, including a renewed NPC handle, and a retired owner cannot animate. Inspect the first projectile frame in the Web clips.
