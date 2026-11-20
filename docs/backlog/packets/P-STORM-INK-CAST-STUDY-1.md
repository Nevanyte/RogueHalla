# P-STORM-INK-CAST-STUDY-1

## Outcome and dependencies

D10 Unity visual review. The owner reopened the newer actor drawings after seeing how far they had drifted from the approved Fire–Frost study. Repaint one Inquisitor pose and judge it against that study and the church floor. Use the existing weapon turntable, actor scale, light, ground shadow and standard camera.

## Relevant legacy paths and contracts

The existing Rust observation selects the actor and item. The reference for inherited presentation behaviour is `packages/client/src/render/PlayerView.ts`. R-VIS, R-AUTH, C-OBS and ADR 0024 apply.

## Exclusive write scope and new paths

This packet, the character direction in `docs/art/STORM_INK.md`, and the affected rows of `docs/art/STORM_INK_VISUAL_CHECKS.csv`. The single-pose source, editor capture fixture and review images live in the session’s visual evidence directory under `fantasy-cast/inquisitor-study`.

## Failing oracle

The prior Inquisitor drawing repeats uniform feathers, outlines and an oversized gold glove. The recently drawn Warden and Haunted unarmed poses also require comparison against the approved brushwork. Their prior grip and movement evidence is retained.

## Behaviour and acceptance

Register the existing eastern SwordViews sprite to the closed lead hand. Use the same 128-pixel cell, pivot, physical height and texture filtering as the regular actor art. Capture the pose on the Rust-delivered church map at the normal camera setting. Verify the observer's fade has settled, the weapon anchor meets the hand, and the figure reads beside the current champion. Keep other directions and animation approval open until they have their own drawings and observations.

## Validation and risks

Inspect the native 2560 × 1440 frames and a 1:1 crop. Confirm that writing the inventory preserves 200 champion and 240 named-enemy facing rows. A still establishes the value grouping and stance. Animation and player approval require separate evidence.
