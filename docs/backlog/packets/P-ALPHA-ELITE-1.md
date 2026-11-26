# P-ALPHA-ELITE-1: derived splash must not count as a fresh native weapon hit

- **Serves:** ORDER_OF_OPERATIONS Phase 2 full card pool; P-AUG-NATIVE-2 follow-up.
- **Implementer tier:** implementer-opus
- **Reviewer:** reviewer-opus
- **Integration base:** `88ff931c1`
- **D-rules touched:** D1 loose proof against legacy predicate; D2 harness n/a; D5 full-loop card behavior; D6/D9 none.

## 1. ID and outcome
The primary wand hit advances Eighth Strike once. Its derived splash still deals damage to a nearby victim but does not advance Eighth Strike, fire the native HitCount hook or consume Bastion's first-hit burst.

## 2. Dependencies
P-AUG-NATIVE-1b and P-AUG-NATIVE-2.

## 3. Relevant legacy paths
`packages/server/src/sim/world.ts` describes `derived` as a follow-on, not a fresh card hit; `packages/server/src/sim/projectiles.ts` marks wand splash `derived`; `packages/server/src/sim/cardHooks.ts` excludes derived hits in `isWeaponHit`; `packages/server/src/sim/onHit.ts` says derived entries apply no on-hit rows.

## 4. New paths
This packet only.

## 5. Exclusive write scope
`crates/rh-sim/src/systems/ledger.rs`, `crates/rh-sim/src/cards/native/hooks_tests.rs`, this packet.

## 6. Contracts and references
C-PARITY, C-TICK, C-INPUT and P-AUG-NATIVE-2's landed weapon-hit definition. `crates/rh-sim/src/cards/after_damage.rs::is_weapon_hit` already rejects `derived`.

## 7. Behaviour to preserve
Derived splash remains an ordinary positive `Event::Hit` for damage and credit. No protocol, content, checkpoint, replay, observation or RNG change.

## 8. Acceptance checks
A real wand shot produces a primary and a splash hit, but only one native HitCount and one Eighth Strike counter step. Existing weapon batch, Bastion and parity tests remain green.

## 9. Validation commands
`cargo test -p rh-sim wand_splash_is_damage_but_not_a_second_native_weapon_hit --locked`; `cargo test -p rh-sim cards::native --locked`; `scripts/gate.sh --quick` on the integrated branch.

## 10. Risk and suggested model tier
The hit predicate also gates Bastion bursts and other native on-hit hooks; one predicate change affects all those cards. Implementer-opus and reviewer-opus.

## 11. Known traps
`Event::Hit` is intentionally still emitted for a splash. Testing event count alone would incorrectly make the splash disappear. The source `derived` flag determines card semantics even when the entry carries a weapon ID.
