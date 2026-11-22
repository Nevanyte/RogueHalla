# P-STORM-COLISEUM-TORCH-1 — Iron fire baskets at the arena walls

## Outcome and dependencies

Phase 3 item 16a, following the worn floor and arena standard. At revision `8a38edfc1` the three existing wall torches show a flat yellow triangle in a small blue dish. Inspect the rim, flame shape, feet and light beside a fighter in a joined Web match. Keep their positions on the public arena map.

## Scope and implementation

This packet owns the Coliseum torch pigment, separate flame sprite, artist-owned art and lighting definition, regional prop registration, shared socket placement for its flame and pooled light, focused tests, and environmental tracker entries. The upper face of the iron bowl must be visible from the gameplay camera. Its rim and coals take the same ambient light as the other painted props. Keep existing shading, flame and lighting available when the editable art is incomplete. Rust retains every wall, floor cell and movement decision.

## Oracle and review

Repeat the route past the western and northern fire baskets on seed 52666345. Compare both Web builds while approaching, standing beside the flame, crossing the foot and moving away. Check whether the flame stays seated, flickers in place and remains visible with Reduced Motion and Low quality. Confirm that a socket edit moves both the painting and the light, including mirrored placements. In a replay of the Web position, switch the torch light off and back on at a fixed time. Compare near limestone, the nearby fighter and distant ground. Inspect occlusion from the adjacent delivered wall.

## Authority and risks

D1 uses the existing map and gameplay renderer. D2 is n/a. C-OBS and D9 apply. The source art is original. All settings control presentation. A second flame painted into the bowl, a light floating above the fire or excessive light on the far side of a wall would require revision.
