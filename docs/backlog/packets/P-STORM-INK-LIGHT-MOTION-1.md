# P-STORM-INK-LIGHT-MOTION-1 — surface motion and painted light

## Outcome and order of operations

Phase 3, item 16a. Design a lighting and animation system for the Storm Ink art and connect its first material and emitter profiles to gameplay. Warm light originates from actual lava and lamp flames. Wet surfaces, ice and matte ink react differently. Preserve the shared weapon images and authoritative timing.

## Dependencies and references

The church and cypress pilots, delivered Holy hall, existing regional sun, pooled accent lights, surface contact FX, quality settings and public map. Follow R-AUTH, C-OBS and D9. The art and implementation guide is docs/art/STORM_INK_LIGHT_AND_MOTION.md.

## Exclusive write scope

This packet, the design guide, and the following paths inside client-unity/UnityProject/Assets: Resources/Regions/{RegionGround.shader,RegionSpriteLit.shader,RegionActorLit.shader,RhSun.hlsl,RhLocalLights.hlsl,StormInkMarsh.hlsl,StormInkLightMotion.hlsl}, Scripts/View/ArenaView.Build.cs, Scripts/View/Atmosphere/{AccentLights,RegionAtmosphere}.cs, Scripts/View/Regions/{RegionProps,TempleLayout.Signature}.cs and Scripts/View/Regions/Landmarks/{Landmark,StormInkLandmarkSprite,HallLandmark.StormInk,ChurchLandmark}.cs. Include the oracle below and metadata for new files.

## Behaviour to preserve

One world sun, the existing budgeted lights, gameplay surface identity, original local and remote attack timing, reduced motion and HUD legibility. A light source belongs to the asset or terrain that produces it. No additional network data.

## Oracle files

- client-unity/UnityProject/Assets/Tests/Editor/StormInkLightMotionTests.cs

## Verification

In graphics Unity compare the same receiver and camera on consecutive renders with only the selected lamp or lava emitter toggled. Evaluate static stone, a moving fighter, marsh and ice, daytime and reduced motion. Check the original sources of truth for real surface and combat contacts. Run the project gate and verify in a Unity Web player at 2560 by 1440.

## Risk and implementation tier

Presentation only. Clipping, a baked shadow doubled by the renderer, a flame lighting empty space, or a sharpening pass that erases ink are visible regressions. Reuse the existing quality budgets and measure presentation changes in gameplay.

## Regional emitter review — 21 November 2026

The five non-Coliseum regional emitters were checked against recorded public Rust states with the Unity renderer at 2560×1440. Matched captures retain the source, map, camera, pigment and shader inputs while changing only the selected emitter; a second frame bypasses only its baked wall-shadow slot. Repeat renders had zero RGB8 drift. The durable comparison and raw receipts are linked from `world/environment-light/index.html` under “Regional shadow and motion review.” The unchanged public map hash is `f38bffe743bfaea721905ba8898df5166279478bafa638c49342348e662d66fd`.

- Roadside lantern and Holy shrine lamps: clear nearby floor is lit, distant floor remains unchanged, and map-wall-blocked samples become darker than the matched shadow-bypass frame. The measured wall cells lie between the source and receiver.
- Holy aisle lamps: nearby floor receives spill and far ground stays unchanged. The actual aisle station has no blocking wall cell inside the lamps' 3.6-unit radius, so wall-shadow direction/cutoff remain unverified.
- Church candle: two actual public player positions were captured against the same registered candle. Near floor spill is present; a blocked receiver stays dark, but the matched shadow-bypass change is only 1 RGB level. Treat shadow direction as marginal, not passed.
- Lava: the stable registered bank light is re-identified by map position `(27.79412, 123.3824)` on every temporal sample. Its slot changes during normal updates, so slot-index-only motion checks are invalid. Across 48 samples, normal flicker varies; Reduced Motion holds the registered light and rendered frame static. The captured radius has no clear floor control, so its shadow direction/cutoff remain open.

Do not treat the open aisle, candle-contrast, or lava-control cases as complete shadow validation. These findings do not validate actor casts or alternate occluder layouts.
