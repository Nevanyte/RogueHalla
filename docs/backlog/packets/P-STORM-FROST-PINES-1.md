# P-STORM-FROST-PINES-1 — Pines at the Frost lakeshore

## ID and outcome

Phase 3, item 16a. Beside the painted lake, ordinary conifers read as stacks of dark triangles. Paint two elevated silhouettes that read at gameplay zoom. Give the tall spruce broken snow shelves and a visible foot. Let the shorter tree show the direction of the wind through its trunk and boughs.

## Dependencies and references

Use the approved regional prop atlas workflow from P-STORM-HOLY-OASIS-SHRUB-1. Compare the delivered Frost bank, grove and cave-fringe route at seed 52666345 with the accepted environment player. The map has fourteen kind 33 trees and eleven kind 34 trees.

## Exclusive write scope

The two Frost pine paintings and importers, their Unity art definitions, shared definition fields and atlas binding, an ice-spire painting, its importer and editable definition, and isolation of the existing spire fallback in the Unity renderer, focused editor tests and metadata, the pine and ice-spire rows in the visual tracker, the backlog row and this brief.

## Contracts and acceptance

R-AUTH and D9. Preserve Rust placement, tall flags, the existing actor-dependent fade, wind and arena lighting. The upright and swept tree each have editable dimensions and a foot anchor. Use the previous procedural tree when a painting is missing. The first gameplay comparison identified tall ice spires (kind 81) that still borrowed the old tree shape. Give those cells a fixed, matte ice slab with its own foot and dimensions. Keep the former procedural silhouette as the missing-art fallback. Check the painterly edges against both bright snow and the icy channel.

## Oracle and verification

The previous regional atlas returns procedural sprites for both pine keys. Require actual paintings on the shared atlas and verify their intended prop assignments. Run the margin and tall-prop checks. Compare matched native and 2560×1440 Web frames at three waypoints, including trunk overlap. Run the full project gate on the integrated environment revision.

The subsequent spire check compares the tree and the ice slab at the cave fringe. Only the trees should sway.

## Review evidence

At playable revision `5e126272c`, the 2560 × 1440 Web player reached the same three positions as the original player. The cave review exposed two neighboring ice spires that had inherited the pine silhouette. A second pass distinguished their height and facing. The 14 snow pines, 11 wind pines and 8 spires keep their delivered positions. In three runs per build at the grove, the median 95th-percentile frame interval was 8.6 ms before and 8.2 ms after. The six new art definitions passed Inspector edits, Undo/Redo, save/reload and texture reimport. The regional dressing rows in the visual tracker reference the matched gallery at `world/frost-and-fire/index.html`.
