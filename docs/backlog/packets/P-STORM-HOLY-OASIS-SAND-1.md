# P-STORM-HOLY-OASIS-SAND-1 — Holy oasis approach

## ID and outcome

Phase 3, item 16a. The owner's environment priority begins with the Holy gate approach. In the joined player, closely spaced highlights make the sand look like raised corduroy. Paint a single overhead sand and sandstone wash, with broad quiet areas that leave the lamps, vegetation and combat silhouettes readable.

## Dependencies and references

The regional ground renderer, P-STORM-HOLY-HALL-1 and the delivered Holy Hall map. Use the approved Plague marsh as the reference for how a few material changes convey place and depth. The previous Holy gate, outdoor approach, shrine and fight form the matched gameplay baseline.

## Exclusive write scope

The Holy sand texture and importer, its editable HolyDrySandArt definition, script and metadata, regional ground texture array, material binding and shader, the Holy sand EditMode oracle and metadata, this brief, the backlog row, and one dedicated Holy oasis sand row in the visual tracker.

## Contracts and acceptance

R-AUTH and D9. The Rust map supplies region weights, terrain, walkability and the landmark. Preserve the existing Hall floor and other regions. Paint dry Holy sand in map space, soften the old ripple lighting there, retain the oasis transition, and apply arena light, shadows and exposure to the result. Expose wash strength and world repeat span on the artist definition. A missing source or zero wash uses the original sand. Pack the paint into the existing ground texture array to respect the Web sampler budget.

## Oracle and verification

On the previous renderer the normal ground material has no authored Holy sand input. The regression test requires the runtime ground quad to use the new source, a positive world repeat span and the exposure supplied by its caller. Compare matched native and 2560×1440 joined gameplay at the south gate, outdoors, in the hall and during a bright-ground fight. Check texture repetition, ground boundaries, readable silhouettes and the transition into the temple. Run the full project gate on the playable revision.
