# P-LAND-FROST-1: the Frost lake is built first in front of the cave's mouth, on softly reserved ground, as targets

- **Serves:** owner direction for the land-generation lane, `docs/coordination/LAND_GENERATION_REQUIREMENTS.md` section
  3 (Frost, and "Every zone"), as decided by the owner on 2026-11-14 ("Yes to both"; `docs/OWNER_DECISIONS.md` row 10,
  MIGRATION "Integration 52"); ADR 0028 G10 (a thematic obstacle is placed by a rule that reads the landmark). Signature
  option only.
- **Implementer tier:** implementer-opus (rh-sim mapgen: the Frost styler's shape stage in signature mode and its draws,
  the Prefix's camp placement round, the landmark table; every option-on map moves).
- **Reviewer:** reviewer-opus.
- **Integration base:** `origin/feature/unity-rework` @ `b71590c8f` (the Fire crater rule, the Holy hall and the
  landmark-table refactor P-MAPGEN-SIG-HOLY-1-fix are in). Gate with `scripts/gate.sh --oracle-check --base
  origin/feature/unity-rework` and `scripts/gate.sh --quick --oracle packet/P-LAND-FROST-1 --base
  origin/feature/unity-rework`.
- **D-rules touched:** D1: option off byte-identical (G8; `pins::pinned_off` stays); no legacy counterpart (DEV-020:
  the signature landmarks and their arrangement are the owner's) · D2 n/a · no sim tick, wire, content hash or Unity
  change.
- **Status:** brief and oracle rewritten 2026-11-14 (oracle-author, Opus) after the owner's decision, on a fresh
  `packet/P-LAND-FROST-1` from `b71590c8f`. Supersedes the first brief and oracle (the hard-rule reading,
  `scratch/P-LAND-FROST-1-hard-packet` @ `d9483e2d5`, on the old base `2f33045d6`), whose definitions and helpers this
  oracle ports.

## 1. ID and outcome

### The owner's words

The direction (`LAND_GENERATION_REQUIREMENTS.md` section 3, kept on record as the direction):

> "**Frost** (`style_rime/`). The lake is built in front of the mouth first, not grown and checked afterwards: about
> 400 tiles (give or take 15%), on the line from the coliseum to the mouth, 3 dry tiles between mouth and shore, and
> always one dry way round it to the mouth at least 3 wide. One smaller second lake elsewhere."

> "**Every zone.** About today's share of the zone's free ground as fence, bog and ice, within about a fifth, measured
> after the site is taken out. Barrows, guard houses, spires, headlands and craters keep today's rules and move only
> where they would collide with the new arrangement."

The decision, 2026-11-14 (the land-generation session's chat; both questions worded so that "yes" accepts the
recommendation; answer **"Yes to both"**):

1. **Frost lake:** "build it first in front of the cave mouth, with the softer ground reservation, as targets that
   never cost a map? [...] The alternative is to keep today's lake."
2. **Holy:** "leave the ground outside the hall open, as today? If you want something added there, tell me what."

Standing rules, unchanged: guarantees are hard; numbers are targets (a number that cannot be met goes back to the owner
with its measurement, never changed quietly); a landmark or zone rule never loses a seed; option off stays
byte-identical; the owner sees a picture of real output on eight seeds before the merge request.

Measured as hard guarantees the rule cannot hold (at best 45 of 64 seeds kept a map before the hall; a hard room loses
seed 70 of 1-128), so by this decision its parts are **targets**: built for on every map, measured, reported, never a
reason to refuse a map.

### The measurement the owner decided on

Prototype `agent/opus/P-LAND-FROST-1-stack-measure` @ `3210782e4` (never merged; knobs `RH_PROTO_FROST_TARGETS`,
`RH_PROTO_FROST_ROOM`, `RH_PROTO_FROST_ROOM_SOFT`), on the stack with Fire, the hall and the table refactor; option on;
16 attempts; seeds 1-128. Raw rows: the lane's scratchpad `runs_frost/stack_soft_24_0.txt` (the decision) and
`stack_a_today.txt` (today).

| | today | built first, soft room 24 deep (the decision) | hard room 24 deep | no room |
|---|---|---|---|---|
| seeds with a map | 128 | 128 | 127 (seed 70 lost) | 128 |
| attempts, seeds 1-32 / 1-64 / 1-128 | 96 / 192 / 363 | 97 / 174 / 334 | 77 / 151 / 334 | 81 / 166 / 305 |
| worst kept attempt (index, seed) | 11, seed 11 | 10, seed 16 | no map | 9, seed 98 |
| lake of 340-460 tiles (maps) | 30 | 46 | 53 | 28 |
| lake size min / p10 / median / max | 23 / 55 / 254 / 539 | 0 / 137 / 305.5 / 400 | 0 / 178 / 323 / 400 | 0 / 0 / 229.5 / 400 |
| exactly 3 dry tiles from mouth to shore | 0 | 111 | 115 | 78 |
| centroid within 4 tiles of the line | 81 | 90 | 94 | 71 |
| a dry way 3 wide | 48 | 124 | 126 | 113 |
| one smaller second lake (60-200, 3 dry tiles away) | 37 | 104 | 103 | 85 |
| maps with stray ice | 56 | 0 | 0 | 0 |
| every part of the rule at once | 0 | 25 | 35 | 8 |
| maps with no lake at the mouth | 0 | 4 | 1 | 15 |
| ice share against option off, median, seeds 1-32 | 0.639 | 0.810 | 0.835 | 0.605 |
| Frost camp nearest-neighbour, mean / min | 19.57 / 9.0 | 18.56 / 9.0 | 18.29 / 9.0 | 19.59 / 9.0 |

With the soft room the ground was kept on 114 of the 128 kept maps and given up on 14; the rule is met almost only where
it was kept (24 of the 25 "all at once" maps). Soft rooms 20 and 16 deep: 128 maps, worst attempt 10, the whole rule on
19 and 18 maps, ice share 0.714 and 0.675.

### What "yes" means in the generator (the design; the implementer owns the details)

1. **The room is one table line.** The cave's entry of `spec::specs` gets `fan: Some(Fan { depth: d.cave_fan_depth,
   side: d.cave_fan_side })` (24 and 0, in `signature::Definition`; the stub added the numbers) and `fan_hold:
   FanHold::Soft` (the stub added the type and the field; every entry is `Hard` on the stub). The volcano's stays
   `Hard` (owner decision of the volcano packet).
2. **The soft retry** (`attempt/mod.rs`, the Prefix, the block under "P-MAPGEN-SIG-SCALE-4 (owner decision
   2026-11-14): the Fire fan"): a round is first tried with every fan reserved, as today (the site-camp check
   `fan_holds_a_site_camp`, then `place_camps_reserving`). When that fails and a soft fan was reserved, the round is
   tried once more with the soft fans left out: the site-camp check against the hard fans only, then
   `place_camps_reserving` continuing the same stream (no reset, no extra draw). When the retry succeeds the round goes
   on and the soft fans are **not recorded** (`AttemptState::fans` holds only what was reserved: the legacy landmark
   stage keeps its buildings out of the recorded fans only, and the Frost styler takes the ground it finds). When it
   fails the round fails as today. A hard fan is never given up. On the default table the first try is exactly today's
   with one more reserved rect, so a map whose first try succeeds draws what the room makes it draw; the retry draws
   only on a retry.
3. **The Frost styler, signature mode** (`style_rime/`; port the prototype's `mouth_lake.rs`, `shape.rs`, `lakes.rs`,
   `routes.rs` changes, without its knobs): the mouth lake first (near shore 3 dry tiles out from the mouth where that
   ground is free, else the nearest free start; best-first growth on the line toward the aim of 400 tiles; no wall
   within Chebyshev 5 of the mouth), the dry way 3 wide round one flank reserved, then one smaller second lake (at most
   three quarters of the mouth lake, 60-200), the spawn routes round the lakes, re-ice and refill after headlands and
   spires, no stray ice. **New, not in the prototype:** when the built-first lake comes out with no ice at the mouth,
   fall back to today's lake growth for that map (the prototype left 4 of 128 maps with no lake at the mouth; the build
   leaves none).
4. **Guarantees:** Frost's registered list stays `["lake-at-mouth"]`. The parts of the rule are not registered: their
   checks are pure functions, `style_rime::{mouth_lake_holds, dry_way_holds, second_lake_holds}` (stub: `false`), the
   oracle's definitions exactly, for the tests and the hand-off's report. `style_rime::lake_rule()` carries their
   numbers (in the stub; pinned).
5. No prototype knob, environment read or `proto` code ships. No new `const`/`static` tunable outside `lake_rule()`,
   the styler's own build figures (a function like the prototype's `build_rule()`) and `Definition`. Option off
   byte-identical. Emptied source files stay as one-line placeholders.

### Holy (decision 2, recorded here and in the requirements; nothing to build)

The ground outside the hall stays open, as today: the Holy styler's fill outside the hall's site is about 0.4 to 0.6
percent of the zone's free ground, before and after the hall packet (the 21 percent measured before was the styler's
own hall). Nothing is added. "Every zone: about today's share" is read for Holy as today's share outside its landmark.

### Also decided by the lane and reported to the owner the same day (no objection received)

"3 tiles clear of the shrine" (the hall's spawn rule) is built and measured as 3 tiles (Chebyshev) from the shrine's
healing light: the shrine's box plus one row in front and behind; a spawn point may stand one tile outside the shrine's
side wall. (Already the hall packet's behaviour; recorded, not changed.)

## 2. Dependencies

`origin/feature/unity-rework` @ `b71590c8f`: P-MAPGEN-SIG-HOLY-1-fix (the table's `fan` field read by
`SignatureContext::fan`, `fan_holds_a_site_camp` excepting a site's own camps, `from_table` and
`AttemptContext::with_signature_table`), P-LAND-FIRE-1 (the Fire gap pin), P-MAPGEN-SIG-HOLY-1 (the hall). No packet in
flight shares the write scope.

## 3. Relevant legacy paths

None: signature mode has no legacy counterpart (DEV-020; reached only through `pipeline::Options::signature`). Option
off is legacy's Frost styler (`packages/shared/src/zoneStyle/rime.ts`) and must not move. The code this packet changes,
at `b71590c8f`:

- `crates/rh-sim/src/mapgen/attempt/mod.rs:248-273` the Prefix's fans: every entry's fan reserved (`Reserve` with zone
  0), the site-camp check, `place_camps_reserving`; `:33-36` `AttemptState::fans` (what was reserved, read by
  `landmarks/stage/mod.rs:121-129`).
- `crates/rh-sim/src/mapgen/landmarks/signature/mod.rs:330-356` `fan_rect` (the geometry, ADR 0028 decision 10);
  `:639-673` `fan`, `fan_holds_a_site_camp` (own camps excepted); `:517` `from_table` (its pin-glyph check is a
  `debug_assert!`).
- `crates/rh-sim/src/mapgen/landmarks/signature/spec.rs:62-69` `Fan`; the cave's entry (`fan: None` today).
- `crates/rh-sim/src/mapgen/style_rime/shape.rs` (the signature path: the mere seeded at the mouth, `fill`,
  `keep_off_routes`), `lakes.rs`, `routes.rs`, `guarantees.rs` (`lake-at-mouth`).
- The prototype to port (read, never merge): `git diff b71590c8f agent/opus/P-LAND-FROST-1-stack-measure --
  crates/rh-sim/src/mapgen/style_rime` (its `signature/mod.rs` and `spec.rs` differences are mostly its older base, not
  the design: do not port them).

## 4. New paths

- `crates/rh-sim/src/mapgen/style_rime/targets.rs` (stub: `LakeRule`, `lake_rule`, the three checks).
- `crates/rh-sim/src/mapgen/style_rime/mouth_lake.rs` (the builder, ported from the prototype).
- `crates/rh-sim/tests/mapgen_land_frost.rs` (oracle).

## 5. Exclusive write scope

- `crates/rh-sim/src/mapgen/style_rime/**` (the styler, `targets.rs`'s bodies, `mouth_lake.rs`, its unit tests, its
  `README.md`).
- `crates/rh-sim/src/mapgen/landmarks/signature/spec.rs`: the cave entry's `fan` and `fan_hold`, `FanHold`'s doc.
- `crates/rh-sim/src/mapgen/landmarks/signature/mod.rs`: what the soft retry needs (a reader of an entry's `fan_hold`,
  the site-camp check over the hard fans), the `cave_fan_*` docs; nothing else.
- `crates/rh-sim/src/mapgen/attempt/mod.rs`: the soft retry only (the block named in section 1, design 2).
- `docs/adr/0028-signature-landmarks.md` (the corrections in section 12) and the hand-off
  `docs/coordination/HANDOFF_P-LAND-FROST-1_<date>.md`.
- Not in scope: `tests/**` (oracles), `tests/support/signature_pins.rs` and every digest pin (re-taken by the lane's
  session), `style_scorch/**`, `style_warren/**`, `zones/camps/**`, `landmarks/stage/**`, the pipeline, the certifier,
  `docs/landmarks/blueprints/**`, `Cargo.lock`.

## 6. Contracts and references

- ADR 0028 (decisions 10 and 14, G6 one stream per zone, G8 option off, G10), `docs/adr/0028-signature-landmarks.md`.
- C-PARITY (option-off maps untouched; `pinned_off`), the determinism rules of `AGENTS.md` (draw order is behaviour;
  ordered iteration), `crates/rh-sim/AGENTS.md`.
- `docs/OWNER_DECISIONS.md` row 10 (this decision); `docs/coordination/LAND_GENERATION_REQUIREMENTS.md` section 3.

## 7. Behaviour to preserve

- **Option off byte-identical** (G8): `mapgen_signature_frost.rs::g8_option_off_maps_are_todays`,
  `mapgen_signature_frost_lake.rs::option_off_maps_are_todays` and every `pins::pinned_off` user stay green. Option off
  never reads `lake_rule`, the room or the targets.
- **Every other zone's tests stay green:** `mapgen_land_sentenced.rs`, `mapgen_land_plague.rs`, `mapgen_land_fire.rs`,
  `mapgen_signature_*.rs` (as amended below), `mapgen_landmark_frame.rs`.
- **The guards with a stop-and-ask sentence (never edited, never re-pinned by this packet):** the Fire gap pin
  `mapgen_land_fire.rs::known_gap_about_one_fire_camp_in_twenty_has_no_crater` (at most 40 of the Fire camps of seeds
  1-64 without a crater, at most 5 on one map) and the Plague pin of DEV-034 (`mapgen_land_plague.rs`, 2 / 2). Every
  option-on map moves here: if the build's maps exceed one of them, the packet stops and the coordinator takes it to the
  owner.
- **The existing Frost rules** stay green unchanged: `mapgen_signature_frost.rs` (the cave closed but for one mouth,
  the mouth opens onto the largest lake within 10 floor steps, spires and headlands out of the core band, certified),
  `mapgen_signature_frost_lake.rs` (largest lake within 10 floor steps of the mouth, worst attempt 12),
  `mapgen_signature_scale.rs`, `mapgen_signature_reserve.rs` (`the_lake_and_apron_rules_hold`).
- **The room's neighbours:** `mapgen_signature_site_rules.rs` (as amended), `mapgen_signature_volcano.rs` (the
  volcano's fan geometry, 14 / 14), `mapgen_signature_church.rs` and `mapgen_signature_hall.rs` (their camps move with
  the room but their rules hold).

**Digest pins that go red on the build and are re-taken by the lane's session, not the implementer** (every option-on
map moves, and the Prefix's camps move with the room):
- `mapgen_signature_blueprint.rs::option_on_maps_are_unchanged` (`pins::pinned_on_maps`) and
  `the_landmarks_phase_is_unchanged` (`pins::pinned_landmarks`: the Prefix and the legacy stage move with the room);
- `mapgen_guarantees.rs::the_refusal_labels_of_seeds_1_to_32_are_todays_relabelled` (`pinned_labels`) and
  `a_lake_failure_is_refused_after_records_as_the_frost_guarantee` (its count, `assert_eq!(seen, 6)`, is the label
  table's: the scratch check measured 2, so the re-pin is a number; if the build left no `lake-at-mouth` refusal on
  seeds 1-32 its `first.expect("a lake refusal")` would have nothing to check, and the lane's session would amend it
  with the re-pin, for the owner);
- the `rh-mapdress` goldens that read option-on maps (`tests/anchor.rs`, `tests/marsh.rs` `SIG_GOLDEN`,
  `tests/sentenced_rings.rs`).
Report each red digest test in the hand-off with the table it prints; do not edit it.

## 8. Acceptance checks

All in `crates/rh-sim/tests/mapgen_land_frost.rs` (seeds 1-128, option on, no decor, 16 attempts, the maps generated
once per binary on eight threads; the definitions are its head comment). Every test is red on the stub for its own
assertion (verified: 13 of 13, section "Oracle files").

Hard (asserted on every map):

| Test | Asserts |
|---|---|
| `every_seed_keeps_a_map_with_a_lake_at_the_mouth_and_no_stray_ice` | no seed of 1-128 without a map in 16 attempts; the worst kept attempt index at most 12 (the stack's bound; prototype 10, today 11); every kept map has a mouth lake and passes the registered `lake-at-mouth`; no map has more than one lake besides the mouth lake |
| `breaking_a_target_refuses_no_map` | Frost's guarantee list is exactly `["lake-at-mouth"]`; `lake_rule()` equals the oracle's numbers; on kept maps where a target holds, a doctored copy (mouth lake dried from its far side to 200 tiles; one ice tile 2 tiles from the mouth; posts on every even-even dry tile beyond the gap; the second lake dried) is refused by no registered Frost rule, and the target's own check said yes before and says no after; at least 8 maps per doctoring |
| `the_target_checks_agree_with_this_oracle` | `mouth_lake_holds`, `dry_way_holds`, `second_lake_holds` give the oracle's verdict on every kept map |
| `the_caves_entry_carries_a_soft_room_and_the_volcanos_fan_stays_hard` | `DEFAULT_DEFINITION.(cave_fan_depth, cave_fan_side) == (24, 0)`; `specs(d)` gives (zone, fan, fan_hold): Fire (14, 14) Hard, Frost (`cave_fan_depth`, `cave_fan_side`) Soft, the others none, on the default definition and on one with the room moved to (17, 2) |
| `only_a_soft_fan_is_given_up_and_then_it_is_not_recorded` | through `AttemptContext::with_signature_table`: the cave's entry given a soft fan of (200, 200) (the whole map): seeds 1-8 keep at least 4 prefixes, each recording only the volcano's fan, with no camp halo or legacy building in it; the same fan marked Hard, and the volcano's fan made (200, 200), give no prefix on seeds 1-8 (never given up) |
| `a_kept_room_keeps_camp_halos_and_legacy_buildings_out` | on the kept attempts of seeds 1-128 with the default table, the recorded fans are exactly [the volcano's, the room] (kept) or [the volcano's] (given up); no other site's or zone's camp halo (`CampContext::halo_offsets`) and no legacy building in a recorded fan; the room kept on at least 100 maps and given up on at least 1 |

Counted floors (targets; each a measured-gap pin in the style of `mapgen_land_fire.rs`
`known_gap_about_one_fire_camp_in_twenty_has_no_crater`). Each floor is set on the lane's judgment (the oracle
author's, 2026-11-14) a little under the decided prototype's value and above today's game; it is the last value so set:
a later packet whose maps fall under one stops, and the measurement goes to the owner. Seeds 1-128, kept maps:

| Test | Counted | Prototype (decision) | Today | Floor | Scratch check (section 13) |
|---|---|---|---|---|---|
| `the_mouth_lake_holds_about_400_tiles_on_most_maps` | lakes of 340-460 tiles; the median size | 46; 305.5 | 30; 254 | 40; 280 | 46; 305.5 |
| `three_dry_tiles_lie_between_mouth_and_shore_on_most_maps` | the gap target (Chebyshev 4, 4 floor steps, no ice nearer) | 111 | 0 | 97 | 111 |
| `the_mouth_lake_stands_on_the_line_on_most_maps` | centroid within 4 of the line | 90 | 81 | 85 | 90 |
| `a_dry_way_three_wide_leads_to_the_mouth_on_most_maps` | the dry way | 124 | 48 | 110 | 124 |
| `one_smaller_second_lake_stands_apart_on_most_maps` | the second lake | 104 | 37 | 92 | 104 |
| `the_whole_lake_rule_holds_on_some_maps` | every part at once | 25 | 0 | 21 | 25 |

The owner's number, kept as he gave it: `the_ice_share_stays_within_a_fifth_of_option_off`, the median over seeds
1-32 of (ice share option on) / (ice share option off) within **0.8-1.2** (prototype 0.810, today 0.638 on the stub).
At the edge: if the build lands under 0.8 the packet stops and the measurement goes to the owner (the test's message
says so); the window is never moved. The same test keeps spires and headlands at 25 and 72 or more over seeds 1-32
(the first oracle's 80% of today's 32 and 91).

`line_floor` (85) can only lie between today's 81 and the prototype's 90: it is close to the measured value by
construction. If the real build lands between 81 and 85, that is a measurement for the owner, not a reason to lower it.

Plus: `scripts/gate.sh --quick --oracle packet/P-LAND-FROST-1 --base origin/feature/unity-rework` passes but for the
digest pins of section 7 (named, reported); `cargo clippy -p rh-sim --lib --tests --features trusted-status-probes --
-D warnings` and `cargo fmt --all -- --check` clean; `rg -n "RH_PROTO|proto_" crates/rh-sim/src` finds nothing.

## Oracle files (read-only for implementers)

- `crates/rh-sim/tests/mapgen_land_frost.rs` (new; `oracle:` commits)
- `crates/rh-sim/tests/mapgen_signature_site_rules.rs` (amended; `oracle-amend:` commit, section 10)
- `docs/coordination/LAND_GENERATION_REQUIREMENTS.md` (section 3: the Frost and Holy decisions of 2026-11-14)
- `docs/backlog/packets/P-LAND-FROST-1.md` (this brief)

The stub's files (`stub:` commit; the implementer replaces bodies, not signatures): `style_rime/targets.rs`
(`LakeRule`, `lake_rule()` with the rule's numbers, the three checks answering `false`), `style_rime/mod.rs` (`mod
targets;` and its `pub use`), `landmarks/signature/spec.rs` (`FanHold { Hard, Soft }`, `Spec::fan_hold`, every entry
`Hard`, the cave still without a fan), `landmarks/signature/mod.rs` (`Definition::cave_fan_depth` 24 and
`cave_fan_side` 0). How the stub kept every existing test compiling: `Fan` gained no field (`mapgen_signature_site_rules.rs`
builds `Fan { depth, side }` literals at three places), so "soft" is a separate `Spec` field (no test builds a `Spec`
literal); `Definition` gained two fields (every test literal uses `..DEFAULT_DEFINITION`); `ReservedSite` and
`AttemptState` gained none (literals in `mapgen_signature_church.rs:740`, `mapgen_signature_hall.rs:902`,
`signature/mod.rs` tests).

## 9. Validation commands

```sh
cd <worktree> && export PATH="$HOME/.cargo/bin:$PATH" CARGO_PROFILE_DEV_DEBUG=0 CARGO_PROFILE_TEST_DEBUG=0
cargo test -p rh-sim --features trusted-status-probes --test mapgen_land_frost
cargo test -p rh-sim --features trusted-status-probes --test mapgen_signature_site_rules --test mapgen_signature_frost \
  --test mapgen_signature_frost_lake --test mapgen_signature_fire --test mapgen_land_fire --test mapgen_land_plague \
  --test mapgen_land_sentenced --test mapgen_signature_volcano --test mapgen_signature_hall --test mapgen_signature_church
cargo test -p rh-sim --features trusted-status-probes --lib style_rime
scripts/gate.sh --quick --oracle packet/P-LAND-FROST-1 --base origin/feature/unity-rework
```

## 10. Existing oracles amended (old to new; for the owner)

`crates/rh-sim/tests/mapgen_signature_site_rules.rs` (P-MAPGEN-SIG-HOLY-1-fix's oracle; one `oracle-amend:` commit):
- `the_table_holds_todays_site_camp_spawn_and_fan_rules`: the Frost row's fan `None` to `Some(Fan { depth:
  d.cave_fan_depth, side: d.cave_fan_side })`. Reason: the owner's decision puts the room on the cave's entry. Red on
  the stub (the cave has no fan yet), green on the build.
- `an_entrys_fan_is_reserved_with_its_numbers`: the doctoring also sets the Frost entry's fan to `None`, so the
  recorded fans stay the (Fire, Plague) pair the test gives. Reason: with the room on the default table the recorded
  fans would hold a third. The test's assertions are unchanged; green on the stub and the build.

Nothing an owner direction pins was loosened. `mapgen_signature_fire.rs::note2_refusals_tell_no_site_from_a_discarded_map`
(at least one discard in seeds 6-10, attempts 0-3) is not amended: it stays green under the decided behaviour (section
13). `mapgen_guarantees.rs` is not amended here: its two red tests are digest pins re-taken by the lane (section 7).

## 11. Risk and suggested model tier

Opus: the styler's shape stage in signature mode, its streams and draw order, the Prefix's camp round (every option-on
map moves), and a fallback the prototype does not have. Main risks: the ice share at the edge of the owner's window
(0.810 against 0.8); the worst attempt (prototype 10 against the bound 12); a soft retry whose draws differ from the
design (the retry must continue the stream, not restart it).

## 12. Known traps

- **The soft retry's draws.** The retry draws on the camp stream only on a retry, continuing it. A first try that
  fails at the site-camp check draws nothing, so its retry starts where the round's placement would have. Do not
  re-run the partition or the reservation; do not touch the zones' signature streams.
- **What is recorded.** `AttemptState::fans` is the one record the legacy stage reads: a given-up room must not be in
  it, or the stage keeps its buildings out of ground nobody reserved (the oracle checks the fans exactly).
- **`fan_holds_a_site_camp` and the room.** The cave's own camp is not tested against its own fan; other sites' camps
  are. On a retry the check is against the hard fans only (the oracle's (200, 200) soft room holds other sites' camps,
  and must be given up, not fail the round).
- **A struct literal in an existing test is read-only:** do not add a field to `Fan`, `ReservedSite`,
  `AttemptState` or `FeatureSpec` (literals in `mapgen_signature_site_rules.rs`, `_church.rs:740`, `_hall.rs:902`, and
  the hall/volcano/blueprint tests); `Spec` and `Definition` (struct update only) are free.
- **The fallback.** "No ice at the mouth" means the mouth lake (the largest 4-connected body of unowned Frost ice)
  is empty; the fallback is today's lake growth (`lakes::grow_lakes`), not a retry of the attempt. Then the
  `lake-at-mouth` rule decides the map as today.
- **Stray ice.** Any piece of unowned Frost ice more than `spacing` from the second lake and not in the mouth lake is a
  third lake: the oracle allows none on any map. Check that nothing else in Frost lays ice after the lakes (today only
  the `Mere` features do), including the fallback.
- **Headlands and the gap; causeways; the ring road; the core band; the ice share** (the first brief's traps, still
  true): a headland on the near shore fills the gap or cuts the dry way; a route band across the mouth lake splits it;
  `surface` skips lake tiles within `ring_clear` of the coliseum ring; the new gap is Chebyshev 4, one tile farther than
  today's core band; count only unowned Frost ice outside the core band.
- **`tests/content.rs`** reads every upper-case `const` or `static` in `src/` as a tunable: keep numbers in
  `lake_rule()`, a build-figures function and `Definition`; no new `const`.
- **Draw order.** A new draw in the Frost styler moves every option-on Frost map (allowed) but must not move another
  zone's draws; option off draws nothing new.
- **The shared build folder.** Each worktree builds in its own `target`; never point `CARGO_TARGET_DIR` at another
  worktree's (binaries of two branches get mixed).
- **The doc corrections that ride with this packet** (the coordinator's request; the implementer's docs commit):
  ADR 0028 decision 14 should not say "no zone or feature kind decides them": Plague's line rule and its deepest-square
  fallback in `reserve` and the volcano's streams in `place` still do; it should say that `from_table`'s pin-glyph
  check is a `debug_assert!` (checked in debug builds), and that `reserve` and `streams` are public for the test seam.
  Add to decision 14 (or a new decision) the soft fan: `Spec::fan_hold`, the retry, what is recorded. The
  P-MAPGEN-SIG-HOLY-1-fix hand-off's wording on the same points is corrected in this packet's hand-off.

## 13. What the implementer measures and reports

In the hand-off, on the real build (option on, seeds 1-128): attempts per seed and refusals by reason; the worst
attempt; the mouth lake's and the second lake's sizes per seed; every counted target's count against its floor; the ice
share ratio per seed and its median (1-32); spires and headlands per seed; the room kept / given up; maps that needed
the fallback; Frost camp nearest-neighbour mean and min; time per map (optimised test build). A number under its floor
or outside the owner's window goes to the owner with its measurement; nothing is changed quietly.

### The oracle's passability check (oracle author, 2026-11-14)

On a throw-away branch `scratch/frost-targets-check` in the lane's worktree (never merged, never pushed; the packet tip
plus the prototype's `style_rime/` from `3210782e4` with `RH_PROTO_FROST_TARGETS=1`, the three checks wired to the
prototype's evaluators, Frost's list trimmed to `lake-at-mouth`, the cave's entry given the soft room, and the soft
retry of design 2 in `attempt/mod.rs`): `mapgen_land_frost.rs` 12 of 13 green. The one red is the expected one:
`every_seed_keeps_a_map_with_a_lake_at_the_mouth_and_no_stray_ice` finds no lake at the mouth on seeds 33 (attempt 0),
62 (5), 74 (0) and 119 (0): the fallback of design 3, which the prototype lacks; no seed lost, the worst attempt within
12, no stray ice on any map. Measured there: every counted value equals the decision's table (46, median 305.5, 111,
90, 124, 104, 25); the room kept on 114 kept maps and given up on 14; ice share median 0.809 (seeds 1-32); spires 38 and
headlands 123 over seeds 1-32. The retry of design 2 (the site-camp check against the hard fans only on a retry) gave
exactly the prototype's numbers on seeds 1-128: no round there fails the site-camp check because of the room.

The same branch, other targets: `mapgen_signature_site_rules.rs` (as amended), `mapgen_signature_fire.rs` (`note2`
included), `mapgen_signature_frost.rs`, `mapgen_signature_frost_lake.rs`, `mapgen_land_plague.rs` and
`mapgen_land_fire.rs` green (the Fire gap 18 of 640 camps on seeds 1-64, at most 2 on one map); red, as section 7
expects: `mapgen_signature_blueprint.rs` `option_on_maps_are_unchanged` and `the_landmarks_phase_is_unchanged`,
`mapgen_guarantees.rs` `the_refusal_labels_of_seeds_1_to_32_are_todays_relabelled` and
`a_lake_failure_is_refused_after_records_as_the_frost_guarantee` (2 `lake-at-mouth` refusals on seeds 1-32, was 6). Not
run there: the `rh-mapdress` goldens, the rest of the suite.

Close to its measured value: the ice share (0.809 against the owner's 0.8: one percent of margin, the decision says so);
`line_floor` 85 against 90 (it must lie above today's 81); `whole_floor` 21 against 25 (four maps). The other floors
sit 12 to 14 percent under the measured value.
