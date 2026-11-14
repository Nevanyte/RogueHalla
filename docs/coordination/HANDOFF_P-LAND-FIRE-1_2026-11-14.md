# Hand-off: P-LAND-FIRE-1 (implementer-opus, 2026-11-14, rewritten for the gentler rule)

```
PACKET:          P-LAND-FIRE-1 — serves the land-generation lane (LAND_GENERATION_REQUIREMENTS.md section 3, Fire), the
                 signature-map work (ADR 0028); the owner's decision of 2026-11-14 ("Yea to all 3", question 1)
BRANCH:          agent/opus/P-LAND-FIRE-1 @ the commit that adds this file (code at b6a921d21)
GATE:            not run by the implementer (scripts/gate.sh in no mode; whole rh-sim suite not run). Targeted tests
                 below: green except the pin tests the lane's session re-takes.
CHANGED:         crates/rh-sim/src/mapgen/style_scorch/guarantees.rs (crater-per-camp replaced by crater-on-camp, order
                 now clear-of-lava then on-camp, header, in-crate tests), style_scorch/craters.rs (doc comment of
                 place_craters only, this change), style_scorch/README.md, style_zones/guarantee.rs (Fire arm doc
                 comment only), this file
D-RULES:         D1: option off byte-identical (mapgen_signature_fire g8_option_off_maps_are_todays green; the
                 option-off pins and pinned_landmarks did not move; style_scorch parity fixtures green); no legacy
                 counterpart (DEV-020). D2 n/a. No tick, wire, protocol or Unity change; nothing serialized or hashed;
                 CraterRules unchanged (content hash unchanged). No new RNG draw, no prototype knob, no proto.rs.
DEVIATIONS:      none (the measured gap, about 1 Fire camp in 20 without a crater, is the owner's decision of
                 2026-11-14; the lane may want a row for it)
SHARED RECORDS:  none proposed here; the lane's session re-takes the pins listed under "Red pin tests"
LEGACY DOUBTS:   none
NOT ESTABLISHED: the whole rh-sim suite and the full gate; the picture of eight seeds (the lane's); time per map; the
                 seeds 65-128 camp count, the crater share median and the near-over-far mean were not re-measured
                 here (the tests pass on their bounds and print none of them: the packet's section 0 / 7 figures,
                 reproduced by the oracle author on scratch/fire-soft-check, are the values on record)
RISKS:           every option-on map moves (attempts kept change): the lane re-pins; the bare-ring retry changes the
                 look of the craters it rescues (a design choice carried from the hard-rule build).
```

## What changed from the hard-rule build

- `style_scorch/guarantees.rs`: `guarantees()` = `"crater-clear-of-lava"` (as built) then `"crater-on-camp"` (every Fire
  crater, a `Crater` feature of zone Fire, is anchored on a Fire camp's tile; a camp without a crater passes). The
  per-camp half ("every camp has a crater") is neither checked nor refused. Header rewritten.
- `style_scorch/craters.rs`: no logic change; `place_craters` already tried the band's option, then each smaller allowed
  size, skipped a size whose disc holds stream or pool lava, retried a short rim as a bare ring and moved a refused pen
  on to the next size. A camp no size fits goes without. Its doc comment now says so.
- In-crate tests (`guarantees.rs`, five on hand-built 40 x 40 maps): registry order and clearance 2; `crater-on-camp`
  (a camp without a crater is NOT refused; a Frost crater and a Fire barrow are ignored; a Fire crater one tile off
  its camp, or on a Frost camp, IS refused); the Chebyshev 2 vs 3 boundary; the disc boundary r4 40 vs 41; only LOW
  lava outside the cone's box counts.
- `style_scorch/README.md` and the Fire arm's doc comment in `style_zones/guarantee.rs` name the two rules and the gap.

## Test results (opt-level 2, debug assertions on, `--features trusted-status-probes`)

| Command | Result |
|---|---|
| `--test mapgen_land_fire` | 16 passed, 0 failed |
| `--test mapgen_guarantees` | 6 passed, 1 failed (`the_refusal_labels_of_seeds_1_to_32_are_todays_relabelled`) |
| `--test mapgen_signature_fire` | 11 passed |
| `--test mapgen_signature_fire_bands` | 5 passed |
| `--test mapgen_signature_volcano` | 20 passed, 1 ignored |
| `--test mapgen_landmark_frame` | 8 passed (pinned_landmarks did not move) |
| `--test mapgen_signature_blueprint` | 9 passed, 1 failed (`option_on_maps_are_unchanged`), 1 ignored |
| `--lib style_scorch` | 13 passed |
| `cargo fmt --check -p rh-sim`; `cargo clippy -p rh-sim --lib --tests --features trusted-status-probes -- -D warnings` | clean |

## Measurements (the tests' own printed lines)

| Figure | Measured | Oracle bound |
|---|---|---|
| Fire camps without a crater, seeds 1-64 | 32 of 640 | 1 to 40 |
| worst map, seeds 1-64 | 2 camps (seed 55; also seeds 10, 29, 54 at 2) | <= 4 |
| worst map, seeds 65-128 | 3 camps (seed 114) | <= 4 |
| kept maps 1-128 with a bare Fire camp | 59 | (no bound) |
| attempts, seeds 1-32: sum / worst | 94 / 9 (seed 30) | <= 137 / <= 12 |
| Fire guarantee refusals, seeds 1-32 | 0 (`[]`) | <= 2 |
| seeds with a map, 1-128 | all 128 | 0 lost |
| craters off a camp, crater tiles in a disc or within 2 of stream lava, seeds 1-128 | 0 | 0 |
| at the band's option, share over 1-32 | passes (>= 0.9; prototype 302 of 302) | >= 0.9 |
| crater share on/off median, 1-32 | passes (within [0.8, 1.2]; prototype 0.952) | [0.8, 1.2] |
| near over far mean tiles, 1-32 | passes (>= 1.15; prototype 1.283) | >= 1.15 |

The last three, and the 65-128 count (prototype 37), are pass/fail in the tests and not printed; the values in
parentheses are the prototype's V1 figures, which the oracle author reproduced exactly on this rule. Camps without a
crater on seeds 1-64 are the same 32 camps the test prints (seeds 5, 8, 9, 10 x2, 11, 15, 19, 20, 22, 23, 24, 25, 27,
29 x2, 30, 32, 35, 38, 45, 46, 48, 50, 53, 54 x2, 55 x2, 56, 61, 63).

## Red pin tests (left red, the lane's session re-takes them)

- `mapgen_guarantees::the_refusal_labels_of_seeds_1_to_32_are_todays_relabelled`: first difference seed 30, labels now
  `["Repair(RadiusPost)", "Prefix", "Prefix", "Prefix", "Repair(FloorShare)", "SignatureDiscarded", "SpawnPool",
  "SpawnRatio", "Signature"]` against pinned `["Repair(RadiusPost)", "Prefix", "Prefix", "Prefix"]`. The test stops at
  its first difference, so other seeds may differ too: re-pin from the labels it builds (`pinned_labels` at
  `tests/mapgen_guarantees.rs:196`).
- `mapgen_signature_blueprint::option_on_maps_are_unchanged` (first difference seed 4: now `(0, "95105424",
  0x0c91b512fb056170)`, pinned `(0, "27ec1783", ...)`). New values, all seeds:
  `cargo test -p rh-sim --features trusted-status-probes --test mapgen_signature_blueprint print_signature_pins --
  --ignored --nocapture`, which prints `pinned_on_maps` and `pinned_landmarks` in the pin file's format.
- Not red: `pins::pinned_landmarks` (`mapgen_landmark_frame` green) and every option-off pin.

## History: the hard-rule build (same day, superseded)

The first build made "every Fire camp keeps its crater" a guarantee (`crater-per-camp`). With 16 attempts it lost seeds
27, 30 and 70 (62 of 64 kept on 1-64, 6 lost on 1-128): 36 Fire refusals on seeds 1-32 against 8 allowed, attempts 139
on the 30 kept seeds against 137, and every option-on test that needs every seed's map panicked. Camps fail for three
reasons (first build, seeds 1-32, 117 camps): 71 rim too short on every allowed size (never the route stretch), 40 stream
or pool lava in every allowed disc (camps 4 to 5 tiles from lava), 6 the runtime's pen share; by band far 67, near 33,
mid 16, core 1. Levers measured: grow-to-larger option 53 to 44 refusals (breaks "never grows"), one-breach retry 41,
bare-ring retry 36 (kept), upstream stream steering removes at most the 17 lava refusals. The owner then chose the
gentler rule this file now describes (decision of 2026-11-14, question 1, "Yea to all 3").

Not taken, for a later packet if the owner asks: a lower rim minimum (6; rescues 5 of the 14 rim camps; needs a
`CraterRules` field, content hash moves), the 15 lava camps need a smaller smallest crater or stream steering, and
giving every camp a crater means placing Fire camps farther from the streams and map corners (the Prefix).
