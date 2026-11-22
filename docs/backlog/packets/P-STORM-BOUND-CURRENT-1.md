# P-STORM-BOUND-CURRENT-1: Flow along delivered lava and shallow-water banks

- **Outcome:** Owner-approved Storm Ink material refinement. Paint travels downstream through the actual bends in connected lava and shallow water. The existing coverage clips the bank, and wide pools and dead ends quiet down.
- **Dependencies:** Accepted elemental pigment and marsh contact packets. Reuse the certified public map and game presentation renderer.
- **Exclusive scope:** `RegionGround.cs`; new `StormBoundCurrent.cs` and Unity metadata; `RegionGround.shader`; `StormInkLightMotion.hlsl`; the Storm Ink elemental art guide; new `StormBoundCurrentEditModeTests.cs` plus metadata; this packet.
- **Contracts:** Presentation-only bake from effective public lava and classified shallow-water floor. No terrain, contact, collision, movement, wire, physics or simulation changes. Four-neighbour connectivity excludes diagonal corner joins and any incompatible surface. Local data is built once for the bound material. Existing ground art remains available when a field cannot be used.
- **Oracle:** L-bend retains downstream progression through two axes; a wider attached pool has deeper but quieter water; dry guard cells carry zero activity/depth and diagonals cannot connect two channels. Repeated builds agree.
- **Validation:** Focused Unity EditMode test, existing dressed/region oracles and normal-camera moving captures on certified lava and water. Verify release Web shader compilation.
- **Risk:** A single downstream coordinate cannot encode a branching flow network exactly. Disconnected or slow pool regions damp movement so the presentation does not imply transport between channels.
