# P-STORM-COLISEUM-TORCH-1 — Iron fire baskets at the arena walls

## Outcome and dependencies

Phase 3 item 16a, following the worn floor and arena standard. At revision `8a38edfc1` the three existing wall torches show a flat yellow triangle in a small blue dish. Inspect the rim, flame shape, feet and light beside a fighter in a joined Web match. Keep their positions on the public arena map.

## Scope and implementation

This packet owns the Coliseum torch pigment, separate flame sprite, artist-owned art and lighting definition, regional prop registration, shared socket placement for its flame and pooled light, focused tests, and environmental tracker entries. The upper face of the iron bowl must be visible from the gameplay camera. Its rim and coals take the same ambient light as the other painted props. Keep existing shading, flame and lighting available when the editable art is incomplete. Rust retains every wall, floor cell and movement decision.

## Oracle and review

Repeat the route past the western and northern fire baskets on seed 52666345. Compare both Web builds while approaching, standing beside the flame, crossing the foot and moving away. Check whether the flame stays seated, flickers in place and remains visible with Reduced Motion and Low quality. Confirm that a socket edit moves both the painting and the light, including mirrored placements. In a replay of the Web position, switch the torch light off and back on at a fixed time. Compare near limestone, the nearby fighter and distant ground. Inspect occlusion from the adjacent delivered wall.

## Authority and risks

D1 uses the existing map and gameplay renderer. D2 is n/a. C-OBS and D9 apply. The source art is original. All settings control presentation. A second flame painted into the bowl, a light floating above the fire or excessive light on the far side of a wall would require revision.

## Evidence

- Reviewed the painted basket in two joined Web builds on seed 52666345, from the original at `8a38edfc1` to the painted version at `ce7f87418`. Five matched 2560 × 1440 views show two placements, including one where all three feet are visible. Frost crosses the nearer basket's base and obscures it in the foreground. The idle films show the flame moving from its base on the coals. The public maps are byte-identical.
- In native Unity at `72033d5ca`, Ultra and Low with Reduced Motion preserve the bowl silhouette. A fixed-time light toggle increases median linear luminance by 0.0221 on Frost and 0.0641 on near limestone. The same samples on the previous torch measured 0.0215 and 0.0637. Distant ground shows zero change. Disabling wall shadows adds 23 red, 13 green and 6 blue levels beyond tile (82,77), where the active shadow stops the spill.
- Editing the mirrored socket by (+0.26,+0.15) in normalized art coordinates moves the painted flame and projected light 15.6 screen pixels left and 14.0 pixels up. Their origins coincide in both images. Across 48 light samples in 1.96 seconds, normal red gain ranges from 0.850 to 0.975. With Reduced Motion it holds at 0.900 and the two captured extremes are pixel-identical.
- The first Web compile caught a texture import hash available only to the Editor. `ce7f87418` confines the reimport check to the Editor and caches the built texture by reference in the player. The corrected Web build and 17 focused Unity tests passed.
- Gallery and receipts are in `world/coliseum-torch/index.html` in the session's visual evidence directory. Regional solid wall dressing and the combined crowded arena review remain separate checks.
