# P-STORM-BACKLASH-READABILITY-1 — Lift the Backlash stamp into view

- **Serves:** Phase 3, item 16a (confirmed signature marks), focused visual follow-up authorized for the Fire Burn/backlash Web review
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `e9d3e7fa2d6e774923ac7234227321dfdedc2c37`
- **D-rules touched:** D1 presentation only · D2 n/a · D5/D6/D9 none

## 1. ID and outcome

Keep Backlash on the existing confirmed `SigHit` path and retain its two-part reverse brush. Move and enlarge only the Backlash mark so it reads clear of the victim's boot and targeting ring on warm volcanic ground, without stepping on nearby damage numbers. The other signature marks and Heal keep their current placement and size. The change does not alter source, victim, amount, hit timing, expiry, pooling, or reduced-motion behavior.

## 2. Dependencies

- `P-STORM-HIT-PIGMENTS-1` is integrated at the base commit.
- The owner-reviewed Fire Web encounter at `/private/tmp/rh-storm-proof/fire-sword-web-burn-backlash/run-10` confirms delivered Burn and Backlash events but identifies the warm-ground Backlash mark as hard to distinguish.

## 3. Relevant legacy paths

- None. This packet changes how a confirmed presentation event is drawn; no gameplay rule or wire event changes.

## 4. New paths

- `docs/backlog/packets/P-STORM-BACKLASH-READABILITY-1.md`

## 5. Exclusive write scope

- `client-unity/UnityProject/Assets/Scripts/Presentation/StormInkHitMarks.cs`
- `client-unity/UnityProject/Assets/Scripts/View/ArenaView.Fx.cs`
- `client-unity/UnityProject/Assets/Tests/Editor/StormInkHitMarkEditModeTests.cs`
- `client-unity/UnityProject/Assets/Resources/StormInk/Combat/SignatureBrushes.png` (Backlash cell only; other atlas cells must remain pixel-identical)
- this packet brief

## 6. Contracts and references

- `C-OBS`: the client uses only already-delivered observations. The presentation still attaches to the received victim.
- `docs/art/STORM_INK_VISUAL_CHECKS.csv`, Fire row `Burn ticks and backlash` (line 601 at the base): warm-ground readability was open.
- `docs/backlog/packets/P-STORM-HIT-PIGMENTS-1.md`: existing confirmed marks, duration, pool, bright/dark and reduced-motion oracle.

## 7. Behaviour to preserve

- The mark is selected only for an already confirmed `SigHit`; no predicted or local attack cue is added.
- Burn, Disease, Mark, Heal and ordinary hit rendering retain their positions, size, colors, timing and pooling.
- Backlash retains its reverse-facing identity and damage number, with the reviewed connected reverse-hook brush in place of the split flecks.
- All marks expire through the existing 0.55-second pooled flash path. Reduced motion freezes mark scale at its initial size. The mark remains attached to the delivered victim position.
- Protocol bytes, game rules, hitboxes, source attribution and damage are unchanged.

## 8. Acceptance checks

- The new red oracle fails on the base assertion because Backlash is still anchored at the boot/inner ring and uses the common small mark size.
- The oracle passes after the focused Backlash-only placement/size change and proves all other signature anchors and sizes stay unchanged.
- Re-run the existing 18-case bright/dark native signature-mark comparison; other marks, contact expiry, number duration, reduced motion and reuse remain intact.
- Capture matched before/after frames at the same gameplay-scale Fire warm-ground stage and camera. The hook must sit outside the victim foot/target ring and leave the label and nearest damage number legible.
- Keep the existing displacement and scale. Replace only the Backlash atlas cell with a connected tapered dark-ink silhouette and narrow warm-ash highlight; confirm all other cells are unchanged.
- No tracker row is marked passed by this packet; the Web gameplay readability check remains open for owner review.

## Oracle files

- `client-unity/UnityProject/Assets/Tests/Editor/StormInkHitMarkEditModeTests.cs`

## 9. Validation commands

- `client-unity/UnityProject/Tools/editmode.sh` (focused invocation documented in the evidence receipt; full suite only if focused and graphics oracle pass)
- External `SignatureContactCapture` native stage runner for the existing bright/dark cases and a Fire warm-ground before/after capture.

## 10. Risk and suggested model tier

A larger or shifted stamp can collide with floating numbers or look like another faction effect. Keep the effect restrained and use the same placement function for event and native preview; compare at 1:1 render scale.

## 11. Known traps

- Screen-space Y and the actor's foot anchor are not intuitive from the signature anchor's local coordinates; judge the rendered stage, not just the numeric vector.
- Do not use the aggregate gameplay hit counters as a receipt for signature FX; they omit `SigHit`. The existing Web receipt decodes the received protocol-v18 signature event directly.
