# P-BAL-Q25-SEAM-1: every Top 25 quest number in one table, `TOP25`; content hash unchanged

- **Serves:** not in the plan: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` top row "Balance patch, test bot,
  per-elite numbers"; plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 3 "Tuning seams", wave 1 row
  P-BAL-Q25-SEAM-1)
- **Implementer tier:** implementer-opus
- **Reviewer:** reviewer-opus (content hash)
- **Integration base:** `feature/unity-rework` @ 83edb687f
- **D-rules touched:** D1 n/a (structure only, no number changes; the content-hash golden is the proof) · D2 n/a ·
  D5/D6/D9 none

## 1. ID and outcome

Every Top 25 quest number (dwell and range rules, reward cooldowns and charges, permanent sizes, spell effect durations,
ranges, damages, deployment numbers, Rebirth Egg and WallBuster rules) lives in one table,
`pub(crate) static TOP25: Top25Tuning` in `crates/rh-sim/src/quests/board/tuning.rs`, so a later retune is a one-line
change. No number changes, the content hash does not move, no state, checkpoint, wire or observation changes. Route
timers and objective counts stay in the `def!` rows of `board/catalog.rs` (each is one literal beside the
`reward_text`/`objective` that may restate it).

## 2. Dependencies

None. Shared file: `crates/rh-sim/tests/content.rs` (merges one at a time; see section 5).

## 3. Relevant legacy paths

- None. The Top 25 board is a Rust-native design (`docs/quests.md`, `docs/QUEST_VALUE_MODEL.md`); `rg "Hold the
  Fort|WardensCage" packages` finds nothing. The oracle is the current Rust code at the integration base, pinned by
  `GOLDEN_CONTENT_HASH` (`crates/rh-sim/tests/content.rs:646`, `0x8a4b_f0ce_37b4_06f2`).

## Established facts (committed before any stub or test)

### F1. The complete list (the implementer's checklist)

How completeness was made sure: (a) every `const`/`static` item in the non-test files of `src/quests/board/**`
(regex `^\s*(pub(..) )?(const|static) [A-Z_0-9]+`); (b) a token scan of every numeric literal other than 0/1 in the same
files, test modules cut (`#[cfg(test)] mod … {}` and files named `*tests.rs` / `attempts/scenarios*`), each hit read in
context and classified as tuning (below), not tuning (F1c) or out of scope (placement, catalog). 91 fields go into
`TOP25`; 6 consts stay (F1c); 15 route timers and 25 objective counts stay in `def!` rows.

Fold position: `catalog::visit_content` (`catalog.rs:478-501`) folds the 25 `def!` rows, then calls, in order,
**A** `attempts::visit_rules` (`attempts.rs:29-44`; it calls `interactions::visit_rules` `interactions.rs:10-14` and
`targets::visit_rules` `targets.rs:15-18` between its own words), **B** `rewards::visit_rules` (`rewards.rs:21-30`; it
calls `permanent::visit_rules` `permanent.rs:14-27`, then `effects::visit_rules` `effects.rs:2-16`, which ends with
`terrain::visit_rules` `terrain.rs:14-30`), **C** `egg::visit_rules` (`egg.rs:12-23`), **D** `wall::visit_rules`
(`wall.rs:5-10`). "A3" = third word group of A. Every f64 is folded as `to_bits()`; integers as `u64`. "not folded"
= not in the content hash today (F2).

| # | Field (suggested) | Today: file:line, name | Type | Value | Fold |
|---|---|---|---|---|---|
| 1 | `fort_ms` | `attempts.rs:46` `FORT_MS` | f64 | 45_000 | A1 |
| 2 | `meditate_ms` | `attempts.rs:47` `MEDITATE_MS` | f64 | 30_000 | A2 |
| 3 | `crown_burn_rate` | `attempts.rs:48` `CROWN_BURN_RATE` | f64 | 0.01 | A3 |
| 4 | `crown_power_cap` | `attempts.rs:49` `CROWN_POWER_CAP` | u8 | 48 | A4 (`u64::from`) |
| 5 | `contact_range` | `interactions.rs:5` `CONTACT_RANGE` = registry `QUEST_TURN_IN_RANGE` (64) | f64 | 64 | A5 |
| 6 | `fort_range` | `interactions.rs:6` `FORT_RANGE` | f64 | 72 | A6 |
| 7 | `brush_reveal_range` | `interactions.rs:7` `BRUSH_REVEAL_RANGE` | f64 | 112 | A7 |
| 8 | `giver_flee_ms` | `interactions.rs:8` `GIVER_FLEE_MS` | f64 | 60_000 | A8 |
| 9 | `quest_respawn_ms` | `targets.rs:12` `QUEST_RESPAWN_MS` | f64 | 25_000 | A9 |
| 10 | `warden_hp_multiplier` | `targets.rs:13` `WARDEN_HP_MULTIPLIER` | f64 | 3 | A10 |
| 11 | `bargain_cost` | `signals.rs:359` `BARGAIN_COST` | u64 | 20 | A11 |
| 12 | `route_pace` | `attempts.rs:304` literal `1500.` in `length * 1500. / PLAYER_SPEED + 15_000.` | f64 | 1500 | not folded |
| 13 | `route_slack_ms` | `attempts.rs:304` literal `15_000.` (same expression) | f64 | 15_000 | not folded |
| 14 | `hook_cooldown_ms` | `awards.rs:80` | f64 | 45_000 | B, per quest (`cooldown_ms().to_bits()`, quest order) |
| 15 | `shoulder_charge_cooldown_ms` | `awards.rs:81` | f64 | 12_000 | B, per quest |
| 16 | `tornado_cooldown_ms` | `awards.rs:82` | f64 | 14_000 | B, per quest |
| 17 | `turret_cooldown_ms` | `awards.rs:83` | f64 | 30_000 | B, per quest |
| 18 | `smite_cooldown_ms` | `awards.rs:84` | f64 | 20_000 | B, per quest |
| 19 | `spell_shield_cooldown_ms` | `awards.rs:85` | f64 | 25_000 | B, per quest |
| 20 | `lantern_ward_cooldown_ms` | `awards.rs:86` | f64 | 60_000 | B, per quest |
| 21 | `cleanse_cooldown_ms` | `awards.rs:87` | f64 | 30_000 | B, per quest |
| 22 | `scout_falcon_cooldown_ms` | `awards.rs:88` | f64 | 35_000 | B, per quest |
| 23 | `bolas_cooldown_ms` | `awards.rs:89` | f64 | 18_000 | B, per quest |
| 24 | `lantern_ward_charges` | `inventory.rs:19` literal `2` in `Owned::new` | u8 | 2 | B, per quest (`max_charges`) |
| 25 | `trade_up_stacks` | `permanent.rs:30` `TRADE_UP_STACKS` | [u64; 3] | [12, 30, 54] | B-permanent 1-3 |
| 26 | `bargain_stacks` | `permanent.rs:31` `BARGAIN_STACKS` | u64 | 24 | B-permanent after the stat codes |
| 27 | `feed_damage_stacks` | `permanent.rs:32` `FEED_DAMAGE_STACKS` | u64 | 6 | B-permanent next |
| 28 | `chef_bonus_share` | `permanent.rs:33` `CHEF_BONUS_SHARE` | f64 | 0.20 | B-permanent next |
| 29 | `helm_bypass` | `permanent.rs:34` `HELM_BYPASS` | f64 | 0.20 | B-permanent last |
| 30 | `stasis_ms` | `effects.rs:43` `STASIS_MS` | f64 | 2500 | B-effects 1 |
| 31 | `shield_ms` | `effects.rs:44` `SHIELD_MS` | f64 | 1500 | B-effects 2 |
| 32 | `warhorn_ms` | `effects.rs:45` `WARHORN_MS` | f64 | 2500 | B-effects 3 |
| 33 | `tornado_windup_ms` | `effects.rs:46` `TORNADO_WINDUP` (also the Cleansed buff length, `effects.rs:483`) | f64 | 600 | B-effects 4 |
| 34 | `tornado_radius` | `effects.rs:47` `TORNADO_RADIUS` | f64 | 240 | B-effects 5 |
| 35 | `pull_tick_distance` | `effects.rs:48` `PULL_TICK_DISTANCE` | f64 | 80 | B-effects 6 |
| 36 | `smite_range` | `effects.rs:55` `SMITE_RANGE` | f64 | 450 | B-effects 7 |
| 37 | `swap_range` | `effects.rs:56` `SWAP_RANGE` | f64 | 400 | B-effects 8 |
| 38 | `hook_range` | `effects.rs:99` `(700., 14., MASK_MOVE)` | f64 | 700 | not folded |
| 39 | `hook_width` | `effects.rs:99` | f64 | 14 | not folded |
| 40 | `bolas_range` | `effects.rs:100` `(550., 12., MASK_SIGHT)` | f64 | 550 | not folded |
| 41 | `bolas_width` | `effects.rs:100` | f64 | 12 | not folded |
| 42 | `blind_rage_attack_rate` | `effects.rs:208` `1.20` | f64 | 1.20 | not folded |
| 43 | `warhorn_damage_per_taunt` | `effects.rs:222` `0.2` | f64 | 0.2 | not folded |
| 44 | `warhorn_guard_per_taunt` | `effects.rs:234` `0.15` | f64 | 0.15 | not folded |
| 45 | `warhorn_guard_floor` | `effects.rs:234` `.max(0.4)` | f64 | 0.4 | not folded |
| 46 | `tornado_cast_range` | `effects.rs:343` `placement(cx, from, aim, 500., 0.)` | f64 | 500 | not folded |
| 47 | `warhorn_radius` | `effects.rs:361` `<= 280.` | f64 | 280 | not folded |
| 48 | `smite_damage` | `effects.rs:387` `hit(cx, id, victim, 220., …)` | f64 | 220 | not folded |
| 49 | `swap_ms` | `effects.rs:427` `+ 10_000.`; restated `effects/checkpoint.rs:64` | f64 | 10_000 | not folded |
| 50 | `blind_rage_ms` | `effects.rs:450` `6000.`; restated `effects/checkpoint.rs:26` | f64 | 6000 | not folded |
| 51 | `still_mind_ms` | `effects.rs:463` `3000.`; restated `effects/checkpoint.rs:27` | f64 | 3000 | not folded |
| 52 | `charge_range` | `effects.rs:600` `aim_end(cx, ix, aim, 220.)` | f64 | 220 | not folded |
| 53 | `charge_width` | `effects.rs:605` `21.` | f64 | 21 | not folded |
| 54 | `charge_knockback` | `effects.rs:611-612` `120.` (twice) | f64 | 120 | not folded |
| 55 | `charge_stun_ms` | `effects.rs:615` `500.` | f64 | 500 | not folded |
| 56 | `shot_speed` | `effects.rs:763` `per_step(cx, 175.)` (per legacy 50 ms tick) | f64 | 175 | not folded |
| 57 | `hook_stop_distance` | `effects.rs:774` `- 42.` | f64 | 42 | not folded |
| 58 | `hook_pull_ms` | `effects.rs:775` `control(…, 600., Pull)` | f64 | 600 | not folded |
| 59 | `bolas_root_ms` | `effects.rs:778` `Root, 1500.` | f64 | 1500 | not folded |
| 60 | `tornado_pull` | `effects.rs:971` `180. *`; and `terrain.rs:432` `180. *` | f64 | 180 | not folded |
| 61 | `tornado_pull_ms` | `effects.rs:982` `300.` | f64 | 300 | not folded |
| 62 | `taunt_stop_distance` | `effects.rs:1023` `- 42.` | f64 | 42 | not folded |
| 63 | `cage_radius` | `terrain.rs:11` `CAGE_RADIUS` | f64 | 160 | B-terrain 1 |
| 64 | `cage_ms` | `terrain.rs:12` `CAGE_MS` | f64 | 6000 | B-terrain 2 |
| 65 | `falcon_speed_per_ms` | `terrain.rs:38` `FALCON_SPEED_PER_MS` | f64 | 6 | B-terrain 3 |
| — | (stays) | `terrain.rs:39` `RADIUS` deployment hitbox | f64 | 16 | B-terrain 4 |
| 66 | `cage_gap_half_angle` | `terrain.rs:13` `GAP_HALF_ANGLE` (radians) | f64 | 0.48 | B-terrain 5 |
| 67 | `turret_ms` | `terrain.rs:32` `TURRET_MS` | f64 | 25_000 | B-terrain 6 |
| 68 | `turret_interval_ms` | `terrain.rs:33` `TURRET_INTERVAL` | f64 | 850 | B-terrain 7 |
| 69 | `turret_range` | `terrain.rs:34` `TURRET_RANGE` | f64 | 340 | B-terrain 8 |
| 70 | `turret_reward_range` | `terrain.rs:35` `TURRET_REWARD_RANGE` | f64 | 400 | B-terrain 9 |
| 71 | `ward_ms` | `terrain.rs:36` `WARD_MS` | f64 | 75_000 | B-terrain 10 |
| 72 | `falcon_ms` | `terrain.rs:37` `FALCON_MS` | f64 | 4000 | B-terrain 11 |
| 73 | `cage_cast_range` | `terrain.rs:91` `placement(cx, from, at, 400., 0.)` | f64 | 400 | not folded |
| 74 | `deploy_range` | `terrain.rs:227` `placement(cx, from, at, 400., RADIUS)` | f64 | 400 | not folded |
| 75 | `turret_hp` | `terrain.rs:231` `100.`; `terrain.rs:448` `100.`; `terrain.rs:432` `/ 100.` (caster max hp over turret max hp, the shape of `effects.rs:971`) | f64 | 100 | not folded |
| 76 | `ward_hp` | `terrain.rs:233`, `terrain.rs:449` `35.` | f64 | 35 | not folded |
| 77 | `turret_cap` | `terrain.rs:238` `1`; `terrain.rs:448` `1` | usize | 1 | not folded |
| 78 | `ward_cap` | `terrain.rs:238` `2`; `terrain.rs:449` `2` | usize | 2 | not folded |
| 79 | `falcon_cap` | `terrain.rs:450` `1` | usize | 1 | not folded |
| 80 | `falcon_range` | `terrain.rs:263` `aim_end(…, 1200.)`; `terrain.rs:483` `1200.` | f64 | 1200 | not folded |
| 81 | `turret_reveal` | `terrain.rs:314` `220.` | f64 | 220 | not folded |
| 82 | `ward_reveal` | `terrain.rs:315` `365.` | f64 | 365 | not folded |
| 83 | `falcon_reveal` | `terrain.rs:318` `165.` | f64 | 165 | not folded |
| 84 | `turret_damage_share` | `terrain.rs:405` `0.3 *` | f64 | 0.3 | not folded |
| 85 | `egg_safe_distance` | `egg.rs:25` `SAFE_DISTANCE` | f64 | 600 | C1 |
| 86 | `egg_sight_radius` | `egg.rs:26` `SIGHT_RADIUS` | f64 | 200 | C2 |
| 87 | `egg_hatch_ms` | `egg.rs:27` `HATCH_MS` | f64 | 3000 | C3 |
| 88 | `egg_hatch_hp_share` | `egg.rs:28` `HATCH_HP_SHARE` | f64 | 0.4 | C4 |
| — | (stays) | `egg.rs:30` `RADIUS` egg hitbox | f64 | 14 | C5 |
| 89 | `egg_min_standing` | `egg.rs:29` `MIN_STANDING` | usize | 4 | C6 (`as u64`) |
| 90 | `wall_radius` | `wall.rs:12` `RADIUS` (and `RADIUS_SQ = RADIUS * RADIUS`, `wall.rs:13`) | f64 | 300 | D1 |
| 91 | `wall_anchor_margin` | `wall.rs:15` `ANCHOR_MARGIN` = registry `TILE` (32) | f64 | 32 | D2 |

The field names are suggestions the stub fixes; the implementer may not rename them (the oracle reads them).

**F1c. Numeric consts that stay (the oracle's allow-list, with reasons):**
`mod.rs:14` `FIRST_BOARD_DEF` (provenance id range of board definitions, an identifier); `signals.rs:358`
`BARGAIN_STAT_COUNT` (length of `BARGAIN_STAT_NAMES` and the action-5/7 target encoding, also read by `src/input.rs:654`
outside the scope; folded A12); `terrain.rs:39` `RADIUS` and `egg.rs:30` `RADIUS` (hitboxes read by
`src/systems/melee.rs:112,346` and `src/systems/projectiles.rs:400,583`, outside the scope; geometry, not balance);
`wall.rs:14` `FENCE` (the low-tile kind code of a fence, a map code; folded D4); `wall.rs:17` `MAX_REMOVALS` (wire
capacity, 300 reliable cells per wire seat; folded D3).

**F1d. Literals that stay (not tuning):** `1000.` ms-per-second conversions (`attempts.rs:233,246`, `signals.rs:328`,
`terrain.rs:406`); `0.5`, `2.` halves and midpoints; `1e-6` tolerances (`effects/checkpoint.rs:93,95`, `terrain.rs:483`);
`1e6` threat sentinel (`effects.rs:494`); `16.` fallback body box (`effects.rs:657`); `0.65` cage ring thickness in tiles
and `4.` x `24` shove-out search steps (`terrain.rs:108,151-152`); `3000.`/`12_000.`/`6000.`/`72.` the Trade Up trader
patrol shape (`interactions.rs:419-425`); `2. * TILE`/`3. * TILE` encounter camp radius (`targets.rs:104-105`);
integer action codes, slot counts, bit positions and objective-progress encodings (`interactions.rs` match arms,
`rewards.rs:42-43`, `inventory.rs` slot `2`s, `attempts.rs:579`). `placement.rs` and `map_admission.rs` are board
placement geometry (where givers and objectives are dealt), out of this packet; `catalog.rs` keeps its `def!` rows.

### F2. Numbers not folded into the content hash today (finding for the coordinator; not fixed here)

39 of the 91: rows 12-13 (route deadline floor `max(def timer, length * 1500 / PLAYER_SPEED + 15 000)`,
`attempts.rs:303-305`), 38-62 (25 spell-effect numbers in `effects.rs`), 73-84 (12 deployment numbers in `terrain.rs`).
They decide outcomes (Smite's 220 damage, Bolas' 1.5 s root, Swap-Stick's 10 s, turret hp and damage share), so a client
and server that disagree on them would pass content negotiation. This packet keeps them unfolded (folding them moves the
hash); a follow-up packet can fold them with one regolden. Also: the effective route deadline is
`max(def timer, 1.5 x walk + 15 s)`, so the `def!` route timer is only a floor; the balance patch's timer rule (plan
section 4) must account for it.

### F3. Quest texts that state a number

| Quest (`catalog.rs` line) | Text | Restates |
|---|---|---|
| The Warden (206) | "a six-second fence ring" | `cage_ms` 6000 |
| Hold the Fort (216) | "Hold a Gallows guard house for 45 seconds" | `fort_ms` 45 000 |
| Let Him Cook (283) | "Chef's Kiss: +20% to your lowest stat" | `chef_bonus_share` 0.20 |
| Tuber Eats (305) | "20% of axe and bow damage ignores armor" | `helm_bypass` 0.20 |
| Barrow Run (316) | "destroys walls within 300 units" | `wall_radius` 300 |
| The Bargain (327) | "Convert 20 stacks into 24 of a stat" | `bargain_cost` 20, `bargain_stacks` 24 |
| A Cruel Joke (337) | "they flee for 60 seconds" | `giver_flee_ms` 60 000 |
| A Cruel Joke (338) | "swaps weapons with a target for 10 seconds" | `swap_ms` 10 000 |
| A Bone to Pick (349) | "roots for 1.5 seconds" | `bolas_root_ms` 1500 |
| Feed or Hunt (360) | "+6% damage as a zone stat" | `feed_damage_stacks` 6 (one Ember Imp stack = 1% damage: the per-stack size is the farm table's, not `TOP25`'s) |
| Gatecrasher (437) | "hatch there with 40% HP" | `egg_hatch_hp_share` 0.4 |
| Stay A While (458) | "a 30-second uninterrupted meditation" | `meditate_ms` 30 000 |
| Stay A While (459) | "Stasis: 2.5 seconds invulnerable" | `stasis_ms` 2500 |

Objective counts restated in words ("three tokens", "three zones", "three quest givers", "three zone items", "three
wandering traders", "two distinct opponents") restate `def.objective_count`, already one literal in the same row; not
part of this oracle.

### F4. Existing tests that pin a quest number

- Exactly, by value: `rewards/tests.rs:101` (Helm 0.20), `rewards/permanent/tests.rs:237` (crown cap 48), `:297` (1.20),
  `:459-460` (Bargain 24 stacks, x1.24), `effects/departure_and_rate_tests.rs:146` and `attempts/scenarios/spells.rs:214`
  (BlindRage 1.2), `effects/tests.rs:317` (Warhorn 1.2 with one taunt), `special/egg_tests.rs:121` (hatch 0.4 HP),
  `attempts/scenarios.rs:725,836` (24 stacks), `catalog.rs:508-543` (25 quests, 5 per zone; 8/10/7 reward kinds).
- By name (compile only; the stub rewrites them to read `TOP25`, same value): `attempts/tests.rs:542,548` (`FORT_MS`,
  `MEDITATE_MS`), `attempts/scenarios.rs:470,474,822` (`FORT_MS`, `MEDITATE_MS`, `BARGAIN_COST`),
  `rewards/effects/tests.rs:110,512` (`STASIS_MS`, `TORNADO_WINDUP`).
- Implicitly, through durations, ranges and timers: about 120 of the board's tests (the scenario harness in
  `attempts/scenarios*.rs` and `attempts/tests.rs`, `rewards/**/tests.rs`, `special/egg_tests.rs`, `wall.rs` inline
  tests), plus `crates/rh-sim/tests/top25_map_admission.rs` and `crates/rh-server/tests/top25_playthrough.rs`. None
  changes, since no number changes.
- The hash: `crates/rh-sim/tests/content.rs:646` `GOLDEN_CONTENT_HASH` and its five copies
  (`scripts/regolden-content.sh --check`).

### F5. The `tests/content.rs` const scanner

- The quest consts are not in `NON_GAMEPLAY`: they are rows of `TABLES_FOLDED_BY_HASH` (`content.rs:214-268`, reason
  "Top 25 authoritative tuning folded by quests/board/catalog::visit_content").
- The scanner (`declared_consts`, `content.rs:507`) keys on `const ` and `static ` alike, upper-case names only; a
  struct-typed `static TOP25: Top25Tuning` is number-bearing and needs a row.
- A listed name with no declaration fails it: `every_const_in_the_source_tree_is_registered_or_explained`
  (`content.rs:1093-1098`, "exemption {f}: {n} matches no number-bearing declaration; remove it").
- So: the stub adds the `TOP25` row (`TABLES_FOLDED_BY_HASH`), and **the implementer removes the row of each const it
  deletes** (rows for `FORT_MS` … `ANCHOR_MARGIN`; the six F1c consts and `BARGAIN_STAT_NAMES`, `SCORCH_STAT`,
  `CHEF_STATS`, `ALL`, `DEFINITIONS` keep theirs). `tests/content.rs` is in the write scope for those rows only.

## 4. New paths

- `crates/rh-sim/src/quests/board/tuning.rs` (stub: struct, static, today's values; the implementer removes its
  `#[allow(dead_code)]`)
- `crates/rh-sim/src/quests/board/tuning_tests.rs` (oracle: ranges, cross-field rules, text numbers)
- `crates/rh-sim/tests/top25_tuning.rs` (oracle: source scan)

## 5. Exclusive write scope

- The non-test files under `crates/rh-sim/src/quests/board/**` (`tuning.rs` included).
- The stub-edited test lines: `attempts/tests.rs`, `attempts/scenarios.rs`, `rewards/effects/tests.rs` (only if a
  name they read moves again; they already read `TOP25`).
- `crates/rh-sim/tests/content.rs`: only the quest rows of `TABLES_FOLDED_BY_HASH` (delete the rows of deleted consts).
  Shared file: this packet merges alone with respect to other packets touching it.
- Nothing else: `src/input.rs`, `src/systems/**`, `constants.rs`, `content.rs` stay untouched.

## 6. Contracts and references

- C-PARITY / content negotiation: `content_hash` unchanged (`GOLDEN_CONTENT_HASH`).
- `crates/rh-sim/AGENTS.md` (purity, determinism, no-alloc); C-NUM (f64 operation order kept).
- `docs/agent/AUGMENT_REWORK.md` "Moving the content hash" (not used here: the hash must not move).
- Why not `content!` registry rows: the registry folds each entry's name with its value (`src/constants.rs:8-16`), so
  moving these numbers there would move the hash with no number changed.

## 7. Behaviour to preserve

- `content_hash` (`GOLDEN_CONTENT_HASH` = `0x8a4b_f0ce_37b4_06f2`); `scripts/regolden-content.sh --check` clean.
- Each `visit_rules` keeps its place and word order and reads `TOP25.x` in place of the const (a const that stays keeps
  its slot). f64 bit patterns and integer widths as in F1 (`u8` cap folded through `u64::from`, `usize` as `u64`).
- Expression shapes and operation order: e.g. `length * TOP25.route_pace / PLAYER_SPEED + TOP25.route_slack_ms`,
  `TOP25.wall_radius * TOP25.wall_radius` for `RADIUS_SQ`.
- State hash, `CHECKPOINT_FORMAT`, replay and observation formats; every test under `src/quests/board/**`;
  `tests/no_alloc.rs` and the other `*_no_alloc.rs`; `tests/content.rs`.

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-BAL-Q25-SEAM-1` passes.
- `cargo test -p rh-sim --test top25_tuning` and `cargo test -p rh-sim --lib quests::board::tuning` green.
- `cargo test -p rh-sim --test content` green with `GOLDEN_CONTENT_HASH` unchanged; `scripts/regolden-content.sh --check`
  prints no `STALE`.

## Oracle files (read-only for implementers)

- `crates/rh-sim/tests/top25_tuning.rs`: red today. `no_numeric_const_outside_the_tuning_table` (42 consts listed
  today; allow-list = F1c, each must stay declared), `no_tuning_float_literal_outside_the_tuning_table` (86 literals
  today; allow-list = F1d; skips `catalog.rs`, `placement.rs`, `map_admission.rs`), `the_table_carries_no_dead_code_allowance`
  (the stub's `#[allow(dead_code)]` must go, so rustc proves every field, the integer ones included, is read by the
  game), and `the_scanner_sees_what_it_should` (green; the scanner's self-check).
- `crates/rh-sim/src/quests/board/tuning_tests.rs`: green on the stub and must stay green. `every_field_sits_in_its_range`
  (one range per field, the reason in the message), `couplings_the_restore_validators_assume` (meditate <= fort; hook
  and tornado pulls <= warhorn; Trade Up escalates; Bargain pays >= cost), `reward_text_numbers_match_tuning` (the 13
  pairs of F3).
- `docs/backlog/packets/P-BAL-Q25-SEAM-1.md`

The stub (`stub:` commit, the implementer's to edit): `tuning.rs` (struct, `static TOP25` with today's values, the
`#[allow(dead_code)]` to remove, the `#[path = "tuning_tests.rs"] mod tests;` line), `board/mod.rs` (`pub(crate) mod
tuning;`), the `TOP25` row in `tests/content.rs`, and six test lines that now read `TOP25` instead of the consts
(`attempts/tests.rs:542,548`, `attempts/scenarios.rs:470,474,822`, `rewards/effects/tests.rs:110,512`). Field names are
fixed by the oracle; adding a field is allowed, renaming or removing one is not.

A later number packet that moves a value outside its range or changes a quest text amends `tuning_tests.rs` in an
`oracle-amend:` commit (the ranges are reasoned bounds, not owner-signed numbers).

## 9. Validation commands

- `export PATH="$HOME/.cargo/bin:$PATH"; cargo test -p rh-sim --test top25_tuning`
- `cargo test -p rh-sim --lib quests::board`
- `cargo test -p rh-sim --test content`
- `scripts/regolden-content.sh --check`

## 10. Risk and suggested model tier

Opus: about 90 numbers across 12 files feed the content hash through a word-ordered chain and the checkpoint validator;
a reordered word, a changed width or a changed operation order moves the hash or a state hash silently.

## 11. Known traps

- `static` cannot be read in a `const fn` or a const context: `Award::cooldown_ms` (`awards.rs:78`) loses `const`;
  `RADIUS_SQ` becomes a computed product; `BARGAIN_STAT_NAMES`' length stays the const `BARGAIN_STAT_COUNT`.
- Restore validators restate numbers: `effects/checkpoint.rs:26-27,64` (BlindRage, StillMind, swap), `terrain.rs:447-450`
  (caps, life, hp), `terrain.rs:483` (falcon range): they must read the same fields as the effects.
- Cross-field couplings the validators assume (pinned by the oracle's cross-field test): `attempts.rs:575` caps every
  dwell at `FORT_MS`, so `meditate_ms <= fort_ms`; `effects/checkpoint.rs:45` bounds every control row by `WARHORN_MS`,
  so `hook_pull_ms` and `tornado_pull_ms <= warhorn_ms`; the Cleansed buff lasts `tornado_windup_ms`.
- `CONTACT_RANGE` is the registry's `QUEST_TURN_IN_RANGE` and `ANCHOR_MARGIN` the registry's `TILE`: the table holds
  them by reference (`contact_range: QUEST_TURN_IN_RANGE`), keeping them registry-tied.
- `terrain.rs:432` `/ 100.` is the turret's max hp (read `turret_hp`); `effects.rs:971` and `terrain.rs:432` share
  `tornado_pull`.
- `per_step(cx, 175.)` scales with the step: keep `per_step(cx, TOP25.shot_speed)`.

## Notes from previous attempts
