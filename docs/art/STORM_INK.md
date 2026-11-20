# Storm Ink production art

The owner selected Storm Ink on 2026-11-19. The church study sets the camera and value range. Broad dry-brush strokes carry the world. Figures use ink contours and flatter fills. Elemental pigment peaks at contact and falls away through recovery.

## Editing the church in Unity

Open **Tools → RogueHalla → Storm Ink → Church Art Studio**. Select a painted pew in the Scene or Hierarchy. In **Asset defaults**, change **Width (world units)** to shorten every pew that inherits its width. In **This slot**, choose **Override** beside Width to shorten just the selected row and bank. **Reset** restores inheritance for that field. The slot name follows the architecture through rotations and reflections. Unity Undo, Redo and Save Project apply to the selected save target.

Select the garnet window to adjust mounting height, jamb thickness or its transmitted light. In **Asset defaults → Permitted orientations**, choose which wall bays accept the glass, permitted local turns, and whether the painting follows a reflected layout. An edge-on face shows stone and an outward-hidden face is concealed. A window can reverse on its mounting wall, the pulpit supports four approaches, and a floor painting can take a continuous local turn. The source handle moves its position on the artwork. A preferred local offset or light source that is masked by an override can be moved in **This slot**. Select the candle to move its flame socket with the amber Scene handle. The cyan handle adjusts the part’s offset in church coordinates. Use **Map and fighter footprints** to compare visual placement with wall tiles, door openings and the footprint dimensions from the Rust archetype fixture. **Opaque roof**, **Ambient light** and **Frame at gameplay zoom** use the arena presentation. The reflection control chooses a symmetric public church whose cosmetic layout resolves to the requested hand.

Use **Fire weapon** to select Axe, Sword or Bow. Choose one of eight facings. For Sword and Bow, the phase, progress and alternate performance controls hold the current drawing at gameplay scale. Select Fire's body in the Scene to drag either palm or enter its pixel coordinates in the Inspector. The same Inspector exposes her height and foot anchor. Bow draw progress follows the artist’s mid-draw and sustained-draw thresholds. With a drawn bow pose selected, **Draw string behind body for this facing** lets a shoulder cover the string. Select the equipped bow in the Scene to drag its grip and both string nocks. **Shared bow** selects the registration asset for all eight views. These settings apply to the one bow painting set used by every faction and by ground pickups.

## Gameplay camera

Paint floors, paths, liquid channels and the pew seat in plan view. Long edges and grain follow the delivered floor plane. A backrest reads as a narrow cap from above. On a rotated church, both pew banks must still face the liturgical rear. Judge the furnished room against the approved overhead church study.

Trees and bushes rise into an elevated 2.5D view. Give the viewer both a canopy top and enough trunk or stem to see where the plant meets the earth. Moving characters use the steeper, mostly top-down three-quarter framing seen in League of Legends. The crown, top of the shoulder armour and upper forearms should be visible at normal zoom. Keep the chest readable through foreshortening and the boots planted. An east-facing fighter still faces east while the viewer sees the upper planes of the shoulder and head. Keep the direction of the individual weapon, anatomical grip and action legible through each turn.

The owner approved the restored Fire axe fighter in the east, southeast and north comparison at matched normal zoom. Carry the original clasp, tied hair, red wrap and ochre tails forward through the eight-way action. Use the same shared axe, ground contact, world scale and attack phase for each comparison. Existing equip, hand, timing and traversal evidence remains independently traceable in the checklist.

## Fantasy character design

Watercolor supplies soft edges and granulation on terrain. Broken dark brush contours and flat local colour separate the cast from the painted world. Use each character’s job, anatomy, climate and equipment to design the shape of the head, shoulders, hands and feet. At gameplay size, a fighter should be identifiable with the colour turned off and the weapon put away.

Fire follows her original ink portrait. Keep the high, loosely tied dark hair and escaping strands, vermilion wrap, wide ochre sash with trailing ends, and flowing panels that follow her burst. Carry that silhouette through the new elevated view and across equipped weapons. The Haunted champion keeps his approved loose, unbelted reaper cloak and black gloves. The Plague doctor hides beneath a sealed mask and carries his supplies on one side. Frost braces in fur and heavy ice boots. Holy has a broad plate cuirass and sun crown. The Inquisitor’s first single-pose repaint was too small and its modelling too uniform. For the next reviewed prompt, use the approved Fire–Frost figure drawing as the primary reference. Put the black brush accents at the jaw, weighted forearm and planted boot. Carry the coat with two broad washes. Compare head-to-boot height at equal camera depth alongside Haunted. The Chain Warden carries actual manacles and reels in the weighted links after each lash.

Headbands, topknots, tied waist cloths and ribbons recur in the first sheets. Review each character in the lineup, including bare hands and head shape, and replace repeated features where their role gives a stronger silhouette. Identify each elite by its signature weapon or action at normal camera distance. The acceptance list records which designs have passed that comparison.

The approved Fire–Frost study carries the body weight through the hip, bent knee and planted boot. The contour grows heavier where forms overlap and tapers away on lit edges. Group coat folds into a few washes and keep the hands and head in human proportion. Reserve the brightest pigment for the action. Compare one pose with the approved drawing and then with the figure on the church floor through the normal play camera. Judge the stance, silhouette, cloth and sword at that size before painting the other angles.

## Solid walls across the map

Each solid cell in the delivered Rust map keeps its position, footprint and blocked approaches. The Unity wall renderer can paint broad trunks, buttresses, ice or stone over those cells. Use the same footprint for an isolated block, a run, a corner and a junction. The darkest contact stroke belongs at the blocked base. Tall art can overlap the ground behind it with the same foreground ordering as other upright scenery. Keep narrow lanes, entrances and actors readable at gameplay zoom.

The church uses decorated limestone on its enclosure and the eight solid groups in the nave. Select **Nave wall finish** in the studio to change its wash, stone size, carving contrast and upright height. The wash and blind-arcade painting are original art for this chapel. **Face Bay UV** sets the start and end of one carved panel in the source image. Enable **Read/Write** when importing a replacement wall texture. The floor footprint comes from the delivered walls. Weathered graveyard masonry can extend the palette to Haunted’s scattered obstacles. Candidate treatments for the other families are joined cypress trunks in Plague, sandstone screens in Holy, fractured ice in Frost, basalt in Fire and worn limestone in the Coliseum. Each family review compares the visible base with the delivered wall overlay, approaches from each accessible side, and walks a neighboring open passage in the Web player. Preserve the square footprint even when a trunk or buttress has an irregular crown. Use the prop catalog separately for walk-through dressing.

## Champion cast and equipment

All five champions and the Executioner use original 512 × 1024 transparent sheets under `Assets/Resources/StormInk/Actors`. The 128 × 128 cells share a foot baseline and carry no painted ground shadow. Rows from the top are S, SE, E, NE, N, NW, W and SW. Columns are ready, stride, anticipation and strike. The delivered actor archetype still sets world scale and clip timing.

Fire’s restored axe loadout now uses a separate eighteen-column action sheet per eight-facing set. Each facing has four guard poses, eight walk poses and a windup, release and recovery for each of two attacks. The shared axe remains a separate painting attached at the authored grip.

The Haunted fighter has a loose reaper cloak and black gloves. The Executioner keeps its broad masked silhouette, garnet mantle and black lamellar apron. Fire, Plague, Frost and Holy have their own painted posture and costume. The champions' palms are registered on each view and pose.

Sword, Bow, Wand, Axe and Shield each use a single shared 1024 × 512 turntable with eight 256 × 256 views. Equipment keeps its shape through equip, attack and drop. Confirmed cues select a stance and direction without replacing the authoritative action. The bow draws from the painted right hand while the left holds its grip. The named-enemy registrations and the status of each of the 30 regional creatures are tracked in STORM_INK_VISUAL_CHECKS.csv.

## Church furnishings and contact

The oak pew and raised pulpit are original cutouts generated for this project from the selected church study. Their warm edge highlights pick out hand-cut oak, worn stair treads and shallow diamond and lancet carvings. They are transparent single sprites. Unity places them on the floor within the two banks of pews and the side of the nave using the delivered wall layout.

The contact mark is an original monochrome dry-brush drawing generated for this project. Black bristles frame an opaque white centre that takes its pigment from the delivered attacker's faction. A confirmed weapon hit displays the mark for 65 ms. The mark uses a bounded particle pool and the same quality switch as impact sparks.

The aisle uses an original dry-brush slate study with muted ochre and grey masonry. The garnet lancet and bronze three-branch candelabrum are original transparent paintings generated for this church. They share the overhead camera angle and visible worn edges of the oak furnishings.

The candelabrum has a taller transparent canvas for the central smoke. The softest pixels and a clear margin survive export. The sprite pivot keeps the base planted at the original height.
