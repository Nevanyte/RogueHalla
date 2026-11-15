# Frost stage-six callbacks

This leaf ports Rime's shape, surface and props callbacks. `Callbacks::new` borrows the
accepted stage-four Rime definition/template and fixed layout/definition, resolves prop
and terrain IDs from the sole catalogs, and admits the stage-six policy. The coordinator
registers the module and its ordered content visitor; shared definitions are folded once.

The native callback grows one or two connected lakes with quantized noise, threshold
selection, plateau correction and two smoothing passes, regrows the first lake when its
ICE-able share is short, places whole headlands and spires, records causeways and meres,
then lays ICE and ordered decorations. The plan retains every source field. Source draw
order is preserved, including random facet inclusion draws for the four mandatory axes
and random shelf draws that later fail placement.

Thresholds/floods/smoothing/noise/directions reuse `style_features`. Clearance/allow and
second-seed selection use the proposed crate-private stage-four geometry extraction;
the stage-four wrapper still supplies no spawn/corridor sources. Real walk lanes call the
existing exact `Pathfinder` with `TileMap` and the accepted player radius. They use a
private view where only other zones' painted ground is made open, and retain source gate
ordering, sampled path bands, and attempted-member bookkeeping. No second pathfinder,
fixed-layout builder, shrine table or prop catalog is introduced.

All writes use the accepted phase-specific runtime contexts. Context dimensions above
2048 in either direction refuse before shape work or continuation writes, keeping squared
integer distances and coordinate sums within the admitted carriers. Definitions are
bounded before use; plans refuse invalid dimensions, indices, ordering and counters before
surface/props writes. Plans are internal callback continuations, not authenticated external
inputs. Public rule fields have one declaration and one ordered content fold.

## Evidence boundary

The source tool executes six genuine default `tryAttempt` calls, with decor both enabled
and disabled and high-word seed/attempt aliases. Instrumented copies only observe caller
boundaries and RNG draws. Every actual case requires equality of the complete final map
(or null) and map hash against the unchanged source caller. All six pinned attempts return
maps, so terminal-null outcome coverage is not claimed. All 75 original source hashes are
recorded.

Fourteen labelled direct contexts cover empty/single/two-lake geometry, narrow basins,
protection and existing surfaces, blocker refusal, prop protection/caps, real source-stamped
watchtower decoration, fixed-grid ring clearance and route-corridor clearance. One labelled
surface continuation replaces only the first source-produced lake's input tile list with
floor cells at footprint distances 4, 5, 6 and 255; source surface/props callbacks alone
produce its expectations. Replay independently derives and verifies that explicit input. Actual
caller cases exercise two through seven real routed walks, causeways and regrowth. Twenty
actual exported `facetShelfCells` cases compare full ordered cells, draw counts and RNG
continuations at every used radius and boundary seeds.

Native replay invokes callbacks, not recorded operations. The test-only runtime prefix
helper restores only **source-captured initial input scratch/wrote data** belonging to
prerequisite work. Every post-shape, post-surface and final plan/layer/owner/feature/prop,
reserved mask, scratch buffer, counter and stream continuation is produced natively and
compared exactly. Actual cases also execute runtime `finish_shape` before surface. This
proves the Frost leaf on genuine inputs; it does not prove native execution of earlier
faction callbacks or a continuous full styled generation.

The parser refuses recursive duplicate keys, unknown/missing fields, missing/duplicate
cases and noncanonical RLE. Fixture scalar narrowing and dimensions are checked. Invalid
continuation tests assert no writes, and the input-only prefix seam has 14 atomic refusal
controls. Numeric content coverage destructures every rule field exhaustively.

Canonical capture/check uses cached Docker image `c86ecead9e55`, `--pull=never`, Linux
amd64 Node 22.23.4, read-only source and private output/dependencies. Set
`RH_STYLE_RIME_OUTPUT` for a private output file; `--check` is byte exact. Use the pinned
`tsx` loader for the source's extensionless imports. No source, dependency pin or map pin
is rewritten. Native validation is scoped to `style_rime`, existing `direct_family`
regressions, formatting and strict Clippy. Semantic mutation evidence is recorded in the
handoff; compile failures never count. No full-workspace, full-generator, styled-pin,
allocation or performance claim is made.

Revision-two admission checks preflight matching watchtower boxes before either continuation
writes. Fixed gate coordinates must be finite, ordered north/east/south/west, and within the
supported coordinate envelope. When the fixed field matches the callback grid, gate points
and shifted mouths must also lie inside that grid before shape initializes its guard or any
phase writes. These checks bound route sampling and refuse malformed metadata atomically;
the source defaults, capture tool, fixture and callback algorithms are unchanged.

Signature mode (P-MAPGEN-SIG-FROST-1, ADR 0028 decision 11) runs only when the styler is handed
the zone's landmark frame (`BaseContext::landmark`, read by `signature_anchor`; P-MAPGEN-LANDMARK-
FRAME-1): no lake tile lies in the cave's core band. Without the frame every draw and write
above is unchanged (G8).

Since P-LAND-FROST-1 (owner decision 2026-11-14, "Yes to both": build the lake first in front of
the mouth, on softly reserved ground, as targets that never cost a map) the lakes are built, not
grown and repaired (`mouth_lake::build`, all on the styler's noise, no RNG draw):

1. the near shore `mouth_gap + 1` from the mouth (Chebyshev and floor steps), nearest the line from
   the mouth to the coliseum's centre tile; when that ground is taken, the free tile nearest the mouth;
2. the mouth lake grown best-first from it to `build_rule().lake_target` (400) tiles under an ellipse
   on the line plus today's lake noise, off the route bands, the ring road's clearing and the gap;
3. the dry way: a walk of `way_width` squares of dry floor round one flank to the mouth; when the lake
   leaves none, the cheapest walk is planned, taken out of the lake and the lake regrown elsewhere;
   the way and the ground within `mouth_gap + 2` of the mouth take no wall (`Sig::keep`);
4. one second lake from today's second seed, `spacing` dry tiles from the mouth lake, to
   `second_target` (100) tiles or 75% of the mouth lake, whichever is fewer;
5. the spawn routes drawn round both lakes (`RouteBook::ensure_wet`); a band that still clips a lake
   is taken out of it and the lake regrown; after the headlands and spires each lake is brought back
   to its size on its own ground and kept to one body (no stray ice).

The fallback: when the built mouth lake is under `lake_min` (the plan would drop it and the second
lake with it, leaving no ice at the mouth) or the frame gives no line, the styler takes that map back
to the growth it had before the packet, on a fresh route book: the mere seeded within reach of the
mouth (`lakes::grow_lakes_at`, else `lakes::grow_lakes`), `fill`, `keep_off_routes` after a seeded
mere; `lake-at-mouth` then decides the map as before. On seeds 1-128 it fires on 7 attempts, all
refused (4 by `lake-at-mouth`), so no kept map is a fallback map today.

The rule's parts (`targets.rs`: `lake_rule`, `mouth_lake_holds`, `dry_way_holds`,
`second_lake_holds`) are pure reads of the finished map with the oracle's definitions
(`tests/mapgen_land_frost.rs`); they are targets, not registered guarantees. The room in front of
the cave is the cave entry's soft fan (`landmarks/signature/spec.rs`, `FanHold::Soft`; the Prefix
gives it up for a round whose camps cannot be placed with it). `report.rs` is an ignored per-seed
report for hand-offs.

End-of-attempt guarantee (P-MAPGEN-GUARANTEES-1, `guarantees.rs`, signature mode only):
`"lake-at-mouth"`, the finished map's largest unowned Frost ice body lies within
`lake_reach + lake_slack` floor steps of the cave's mouth (the frame's anchor entrance). A failure
discards the attempt as `pipeline::Refusal::Guarantee { zone: Frost, rule }`; nothing is repaired.
