# Hand-off: P-LAND-FIRE-1 (implementer-opus, 2026-11-14, rewritten for the gentler rule)

```
PACKET:          P-LAND-FIRE-1 — serves the land-generation lane (LAND_GENERATION_REQUIREMENTS.md section 3, Fire), the
                 signature-map work (ADR 0028); the owner's decision of 2026-11-14 ("Yea to all 3", question 1)
BRANCH:          agent/opus/P-LAND-FIRE-1 @ the commit that adds the section "Gate, pins and review" to this file
                 (code at c42815cab; packet/P-LAND-FIRE-1 @ 8d7dac42a; base origin/feature/unity-rework @ 91af8942f)
GATE:            PASS (quick, --oracle packet/P-LAND-FIRE-1 --base origin/feature/unity-rework) on 2d24b9861, run by
                 the lane's session: rustfmt, clippy and tests of rh-mapdress, clippy rh-sim, the content registry,
                 the whole rh-sim suite as CI, the parity report. [not run: the full gate (the coordinator's, on the
                 merged tree). Run besides it: cargo test -p rh-server, 49 targets green, no Postgres.] One docs-only
                 commit (this file) follows the gated commit.
CHANGED:         against the base: crates/rh-sim/src/mapgen/style_scorch/{craters.rs (signature mode: each smaller
                 size, discs holding lava skipped, the bare-ring retry, a refused pen moves on), guarantees.rs (new:
                 crater-clear-of-lava, crater-on-camp, stream_lava, disc_holds, in-crate tests), mod.rs, README.md},
                 style_zones/guarantee.rs (Fire arm), this file. Packet branch (oracle and pins): tests
                 mapgen_land_fire.rs (new), mapgen_guarantees.rs, mapgen_signature_fire_bands.rs,
                 support/signature_pins.rs, rh-mapdress tests anchor.rs / marsh.rs / sentenced_rings.rs (golden
                 constants only), the brief, LAND_GENERATION_REQUIREMENTS.md section 3
D-RULES:         D1: option off byte-identical (mapgen_signature_fire g8_option_off_maps_are_todays green; the
                 option-off pins and pinned_landmarks did not move; style_scorch parity fixtures green); no legacy
                 counterpart (DEV-020). D2 n/a. No tick, wire, protocol or Unity change; nothing serialized or hashed;
                 CraterRules unchanged (content hash unchanged). No new RNG draw, no prototype knob, no proto.rs.
DEVIATIONS:      none (legacy also leaves camps without a crater; the measured gap is against the owner's rule, not
                 against legacy, and is recorded in LAND_GENERATION_REQUIREMENTS.md section 3 and pinned by the test
                 known_gap_about_one_fire_camp_in_twenty_has_no_crater)
SHARED RECORDS:  proposed edit: docs/OWNER_DECISIONS.md: a row "2026-11-14, Fire craters: the gentler rule (craters
                 clear of lava and on a camp are hard; a camp goes without when no size fits); about 1 Fire camp in 20
                 without a crater accepted as a measured gap; taken in the land-generation session's chat"
LEGACY DOUBTS:   none
NOT ESTABLISHED: the full gate; time per map; on this build the seeds 65-128 camp count, the crater share median and
                 the near-over-far mean are checked by their tests' bounds but not printed (the oracle author's
                 scratch reconstruction printed 37, 0.952 and 1.283); what made seed 30's attempt 5 a
                 SignatureDiscarded (the reviewer suspects a bare ring closing a camp in; not measured)
RISKS:           every option-on map may move again with the next packet that moves maps (the hall, the Frost lake):
                 the count ceiling (40) and the worst-map bound (4) have room, the pins are re-taken then; the
                 bare-ring retry changes the look of the craters it rescues.
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

## Gate, pins and review (the lane's session, 2026-11-14)

- **Gate:** as the block says. Output kept in the session's scratchpad (`verify/fire_gate.txt`).
- **Pins re-taken from this build** (`re-pin:` commit on the packet branch, constants only):

  | Expectation | Old | New |
  |---|---|---|
  | `signature_pins.rs` `pinned_on_maps` (seeds 1-32, option on) | 89 attempts, worst 9 (seed 22) | 15 of 32 rows moved (seeds 4, 7, 8, 9, 12, 13, 15, 18, 19, 24, 25, 27, 29, 30, 32); only seed 30 changes its attempt, 4 to 9; 94 attempts, worst 9 (seeds 22 and 30) |
  | `mapgen_guarantees.rs` `pinned_labels` | seed 30: RadiusPost, Prefix x 3 | seed 30: RadiusPost, Prefix x 3, FloorShare, SignatureDiscarded, SpawnPool, SpawnRatio, Signature; the other 31 seeds unchanged; lake-at-mouth refusals still 6 |
  | `rh-mapdress` `NO_ANCHOR_GOLDEN` / `SIG_GOLDEN` / `NO_ORIGIN_GOLDEN` | | 1 of 4 / 4 of 16 / 1 of 4 values (seed 4, option on) |
  | `pinned_landmarks`, `pinned_off`, the rh-mapdress option-off goldens | | unchanged |

  New helper: `print_pinned_labels`, an ignored test in `mapgen_guarantees.rs` that prints the label table ready to
  paste (as `print_signature_pins` and the rh-mapdress `print_goldens` do).
- **Tests amended by the oracle (old to new):** the brief's table (section 8). The one that stood for an owner
  direction, `every_fire_camp_has_its_crater`, is replaced by the owner's decision of 2026-11-14 with the
  measured-gap pin. No owner-directed test passes on a few seeds only: the gap pin's figures are 32 against 1 to 40,
  and the worst single map 2 and 3 against 4 (today's game's own worst).
- **Review:** reviewer-opus on 2d24b9861: **LAND**, no notes. It re-ran the gap, bounds and guarantee tests and read
  the styler, the guarantees, the oracle amendments and the re-pin commit.
- **Minor, not notes** (the reviewer's list; no packet, owner direction 2026-11-13):
  - `mapgen_land_fire.rs` lines 129-130 and 614-616: the ceiling 40 is above today's 36, so "may not leave more camps
    bare than today's game" overstates it (counts 37 to 40 would pass).
  - The brief's section 3 rule 3 and its section 8 row for `every_fire_camp_has_its_crater` still say worst map at
    most 3; the test and sections 7 and 10 say 4, and those govern. Section 6 does not list the three rh-mapdress
    goldens among the pins to re-take, though they were re-taken.
  - `guarantees.rs`, the disc test: `f.p.get(1).copied().unwrap_or(0)` passes a crater with no radius parameter; the
    certifier's parameter rule catches that first.
  - A bare ring with no frontier and no route crossing can close a camp in; the strict certifier's reachability check
    then discards the attempt (it costs an attempt rather than being avoided).
  - `craters.rs` lines 96-107: the reflowed doc comment has two broken lines.
- **Picture:** eight seeds of the real build (Fire zone, camps with and without a crater marked) sent to the owner:
  six maps complete, one camp without a crater on each of seeds 5 and 8, no crater within 3 tiles of lava.

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
