# P-STORM-COMBAT-VFX-1: Keep the fighter readable in overlapping melee warnings

- **Outcome:** Retain accurate high-contrast warning perimeters while three or more overlapping arcs preserve the readability of fighters and ground. Confirmed swings transition to a brief edge stroke. The saturated hit brush remains driven by delivered Hit.
- **Dependencies:** Integrated Storm Ink champion equipment and the owner-reviewed live Bailiff encounter.
- **Exclusive scope:** `HitZonePresenter.cs`, the melee swing use of `ArenaView.Fx.cs`, the new focused `StormInkCombatVfxEditModeTests.cs` and its metadata, and this packet.
- **Contracts:** Authoritative event lifecycle, visibility, delivered weapon shapes, warning origin, reach and angle, pooling, cancellation, separate Hit/Block presentation, projectile visuals and reduced-motion support. Unity does not decide contacts or outcomes.
- **Red oracle:** Exercise three simultaneous production-presenter warnings against unchanged reach and outline checks, 25% cumulative fill limit, short rim-only swing and reduced-motion lifecycle. Existing protected oracles remain untouched.
- **Validation:** Focused EditMode before and after, fixed camera Unity comparison with three or more overlaps, bright and dark readable surfaces, and 1440p real Web contact check.
