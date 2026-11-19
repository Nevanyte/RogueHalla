# Storm Ink lighting and motion

A fighter crossing from frozen stone into firelight should change colour on the side facing the flame. The ink on the glove should still read black, the ground shadow should agree with the nearby pillar, and the shared sword should keep its painted silhouette. Use that crossing as the reference for new assets.

## How to paint a receiver

Paint the local colour first. Keep broad value changes that explain the object, such as the hollow under a hood, the underside of a root or the bevel on an ice shelf. Leave broad illuminated faces a little below paper white so the live light has room to reach them. Reserve the darkest strokes for joints and silhouette breaks. Keep foliage mattes and the soles or base of a prop free of detached directional shadows. A small contact wash can touch a foot or a root.

Use the region sun, ambient grade and world shadow mask for the shared direction. Local lights supply a short wash of reflected colour. Painted stone, oak, cloth and bark receive diffuse light across their fill. A change from dark ink to pale paper within the painting should not become a raised ridge under a torch. For painted actors, the silhouette gives the light its direction. Apply the local rim inside that silhouette so the hand, weapon grip and head remain easy to pick out.

## Surfaces at the gameplay camera

| Surface | Resting image | Motion and light |
|---|---|---|
| Frost ice | Fixed mineral-blue fractures run under an opaque, snow-scoured rim. | A narrow highlight travels along an existing facet. The fracture stays in place. Local lights can catch the facet. Snow may shed a few wind-aligned dry strokes. |
| Shallow water | Grey-teal reflected sky breaks around visible silt and reeds. | Broken reflections drift downstream in a narrow run and broaden into short arcs in a quiet pool. The silt and reeds stay anchored. A presented footfall opens a broad ring and a short V-shaped wake using the existing footstep system. |
| Plague bog | Opaque olive-grey channels with a dark bank beside the roots. The narrow dry crossing remains readable. | Occasional small, incomplete bubble rims rise and close. A foot draws a short, slow trough. Keep ambient spores above the water sparse enough to see an attack windup. |
| Mud and marsh grass | Warm wet earth and desaturated sedge share a ground wash. | Imprints and moved reeds identify a passing actor. Keep moving water reflections within the wet areas. |
| Fire | Matte angular basalt, ember ground broken into dry flecks, and continuous red-orange lava channels. | Long vermilion currents carry small gold strokes past dark rafts of crust. Cooling cinder shows short, disconnected flecks. The lava core pulses below the crust. Near rock receives a softer warm wash. Rising embers start close to a heat source and fade to brown. Concentrate the brightest gold at combat contact. |
| Lamps and candles | The flame and glass mark the emitter. The iron post and stone base receive ambient light. | Mark the wick or cage relative to the sprite’s painted foot. Use this point to position the warm light and to mask flame colour inside an atlas cell after packing. A short warm pool reaches the floor, a nearby actor and the near face of a prop. Keep the flame readable when particle density falls. |

MineralIce and MoltenInk are original colour paintings. Unity imports each into a 256-pixel slice at the end of the existing ground detail array. A narrow edge blend lets a vein cross tile boundaries in the same direction. On ice, preserve cobalt in the deeper plates and place the pale moving glint on the painted fracture. Lava and shallow water follow the connected wet cells in the delivered map. Downstream distance follows a rounded centreline. The signed distance across it keeps a long painted stroke aligned through diagonal bends. Deep pigment travels more quickly than folds near the cooling lip. In a broad pool the drift gives way to a quiet eddy. The four neighbouring lava samples still set the reach of its reflected warmth. Vermilion and small gold strokes travel within the fixed bank. Cinder stays darker and broken.

## Layers and their order

1. **Ground and contact.** Terrain supplies the base wash and the true ice, lava or bog boundary. Painted mud and shallow variants supply decoration. The ground shadow and footprint sit beneath the actor.
2. **World light.** The existing region sun determines the overall grade. Flames register a map position and height with the culled local-light pool. Bent channels use an actual molten tile for each pooled source. The local shader colours both the floor and the figure. The lamp pool and real wall occlusion give the light its position in the room.
3. **Moving pigment.** Ground reflections and bubbles use a clock shared by the atmosphere. Give airborne snow and embers their own silhouette, drift and fade in the existing particle pools. A still frame must identify the surface and emitter.
4. **Figures and combat.** Keep the body and weapon drawings crisp. Express the windup through pose and negative space, the confirmed contact through one short ink accent, and recovery through settling cloth and fading pigment. Drive these phases from the current clip and public combat events. HUD, targeting, warning shapes and health bars stay in the existing foreground passes.

## Artist and gameplay checks

- Pose one matte object under cool ambient light and beside a warm lamp. Keep the camera, exposure and art fixed. Switch only that emitter off, then on again. The nearby floor and inward-facing part of the actor should respond. Check an unlit area beyond the light radius for comparison.
- Repeat on basalt beside a real lava channel. The dark plate, hot seam and projected spill should each remain recognizable with bloom disabled.
- Review ice, water and bog at rest, during a step and after the step. Check that the ice cracks hold still, water carries a spreading wake and the opaque bog closes more slowly.
- New lamp paintings need a marked foot and flame centre. Check reflected, rotated and fallback versions in Unity at normal zoom. Piers and matte furnishings should receive spill without glowing by themselves.
- On low quality keep material boundaries, combat silhouettes, static heat and flame cores. At higher tiers add glints, contact detail and sparse particles within the existing budgets. Reduced Motion freezes ornamental surface travel, the projected lantern pattern and flame flicker. Keep the live surface state and essential confirmed hit cue.
- Use the standard 2560×1440 Web route and fixed-camera lamp and lava comparisons. Record quality settings and actor counts alongside frame intervals. Check crowded fights on both bright and dark ground.
