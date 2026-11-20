# P-STORM-COMBAT-HUD-1: Keep helmets and simultaneous damage readable

- **Outcome:** Existing signature marks stay readable without a neighbor's opaque mark plate covering the fighter's helmet. Simultaneous damage numbers retain each delivered value and occupy distinct measured lanes.
- **Dependencies:** Integrated Storm Ink champion equipment and observed Holy/Sentenced fight.
- **Exclusive scope:** World-space mark and damage placement in OverheadExtras, OverheadIndicators, SpriteActorPresenter and ArenaView Fx partials, one small placement helper and metadata, the new focused EditMode test and metadata, and this packet.
- **Contracts:** No gameplay, content, art, event counter or protocol changes. Preserve numbers, colors, status meanings, lifetimes, fades, world anchoring, pool cleanup, reduced motion, and authored fighter shapes. No per-frame allocation in the placement path.
- **Red oracle:** A close Holy/Sentenced arrangement checks the exact delivered mark text and separates the cue plate from the neighboring helmet. An eight-hit burst checks all exact amounts and pairwise nonoverlapping rendered damage plates.
- **Validation:** Frozen red and focused green Unity results, same-camera bright/dark HUD comparison and framed samples of simultaneous real game contacts if a matching player is available.
