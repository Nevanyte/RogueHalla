# P-STORM-SURFACE-LIGHT-REVIEW-1 — Ice movement and local light response

## Outcome and dependencies

Phase 3 item 16a. The existing ice lake and regional light emitters had static samples. Verify their behaviour in a joined encounter and record a controlled native light comparison.

## Scope and ownership

Review packet for the environmental rows in the visual tracker, this specification and the backlog index. Use the production Web player, real Rust map and reconstructed Unity views from public actor and map data. No runtime code or fixtures change. D1 checks the delivered region and terrain. D2 is n/a. R-AUTH and C-OBS apply.

## Oracles

For the Frost lake, compare the projected displacement of fixed plate detail and nearby world objects when the fighter walks from snow onto the ice. Inspect glints and ground contrast at normal game speed. For each light, compare the lit scene with the same local emitter disabled, holding the camera, fighter pose, other lights and shader time fixed. Use a body-only difference mask and independently sampled near and distant floor. Repeat the lit render to check drift. Keep temporal flicker and cast-shadow direction open wherever a static toggle does not settle them.

## Evidence

The joined Frost run is in world/frost-lake. The six matched lamp, candle and lava comparisons, their exact public source positions, and sampled luminance are in world/environment-light. Each tracker entry names the evidence that applies to it.
