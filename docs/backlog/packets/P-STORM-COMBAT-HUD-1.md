# P-STORM-COMBAT-HUD-1: Keep helmets and simultaneous damage readable

- **Outcome:** Camp reward badges stay readable while their opaque plaques clear fighter helmets. Simultaneous damage numbers retain each delivered value and occupy distinct measured lanes.
- **Dependencies:** Integrated Storm Ink champion equipment and observed Holy/Sentenced fight.
- **Exclusive scope:** World-space reward plaque and damage placement in CampRespawnLabels, SpriteActorPresenter and ArenaView, and one small placement helper and metadata, the new focused EditMode test and metadata, and this packet.
- **Contracts:** No gameplay, content, art, event counter or protocol changes. Preserve numbers, colors, status meanings, lifetimes, fades, world anchoring, pool cleanup, reduced motion, and authored fighter shapes. No per-frame allocation in the placement path.
- **Red oracle:** A Holy fighter on a delivered health-reward camp centroid keeps the exact badge icon and clears the helmet. An eight-hit burst checks all exact amounts and pairwise nonoverlapping rendered damage plates.
- **Validation:** Frozen red and focused green Unity results, same-camera bright/dark HUD comparison and framed samples of simultaneous real game contacts if a matching player is available.
