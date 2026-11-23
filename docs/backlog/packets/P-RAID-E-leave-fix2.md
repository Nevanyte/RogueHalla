# P-RAID-E-leave-fix2: two defects of the test bot's elite executor (chest reach, ranged fights)

- **Serves:** not in the plan's phases: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot,
  per-elite numbers"; `docs/backlog/BALANCE_LAB_PATCH.md`, wave 5, P-RAID-E); the two notes of reviewer-opus on
  P-RAID-E-leave-fix. Headless human seats only. Lands before the pilot measurement (it reads elite kills and chest picks
  by weapon; both defects lose a credited killer's pick).
- **Implementer tier:** implementer-opus (rh-policy bot state across ticks)
- **Reviewer:** reviewer-opus
- **Integration base:** `coord/int-87` @ `e554f117c`. Straight line; do not merge the base in.
- **D-rules touched:** D1 loose proof (RaidBot has no legacy counterpart) · D2 n/a · D5/D6/D9 none.

## 1. ID and outcome

1. **A seat out of reach counts as standing at its chest.** `Elite::chest_step` (`exec/elite.rs:~533`) calls
   `travel::walk` and, when the step is not `Running`, returns "at the elite's chest". Travel's arrival rule
   (`exec/travel.rs:67`) is `d <= ARRIVE_U (48)` or `d <= NEAR_U (150)` after `STILL_MS (1200)` of standing still
   (`cx.still_since`). A melee killer has just been standing still, so at 89 to 150 u it has "arrived" without moving; the
   pick needs the panel range (`CHEST_PANEL_RANGE` = 88, `perception.rs:171`; the server's reach is 96). Traced in the lab:
   seed 1, a Shield seat killed 103 u from the chest, stood 15 s, never got `can_choose`, the chest expired; seed 7, a seat
   stood 149 u away and bled from 0.48 to 0.37 hp before leaving.
2. **A ranged seat's fight is never recorded.** `fighting` in `Elite::fight_step` (`:~600`) is true only when the elite or a
   camp member is within `FIGHT_U` (240, `farm.rs`). `lock_range` (`farm.rs:135`) is `range * 0.8`: 512 for a Wand (range
   640), 416 for a Bow (520). A seat fighting from beyond 240 u never sets `fight_since`, so its kill is not recorded
   (`watch_elites` needs `fight_since.is_some()`) and it gets no chest phase (traced: seed 7, a Wand seat at 421 u was the
   credited killer and never picked), the 120 s bound from the first blow never runs (only "never reached" does), and a
   hand-away is not banned (`watch_goal` needs `fight_since`).

Verified against the code, and where the review was imprecise: both defects are as described. The review's bound for (2)
says "only the never-reached bound limits it": true, and that bound is measured from `started` (the first think), so it
ends a long ranged fight at 120 s from the goal's start, banning once; after the fix `fight_since` bounds it from the first
blow instead. For (1) the arrival rule needs `cx.still_since`, which the frame helper of the existing tests sets to 0
(so any `now >= 1200` is "standing still"); in a live run the seat has to have stopped, which a killer does.

## Decisions (coordinator, 2026-11-23)

1. **Standing at the chest means within the pick's reach.** While the chest is farther than `CHEST_PANEL_RANGE` less a named
   margin, `chest_step` returns a move intent to the chest's position itself (a `Move` intent, `move_intent`/
   `step_toward`), whatever travel's stillness rule says; inside that distance it stands as today ("at the elite's
   chest", `Running`, no intents). The distance derives from `CHEST_PANEL_RANGE`; the margin is a named constant beside
   the use: no free-standing literal. The oracle fixes the margin only between 0 and 28 u (a seat 60 u from the chest
   stands; a seat at 100 u or more moves). `chest_over` (the wait, the find time, the chest's life) is unchanged.
2. **A seat is fighting the elite when the frame shows it attacking that camp from any range:** the existing radius rule, OR
   the seat's own attack lock (`cx.p.base.self_view.owner_lock`, read in `fight_step` at `:~574`) names the elite or a
   member of the camp (same `member`/elite tests the step already uses). The lock serves: the sim's observation fills
   `attack_target` for a held lock (`perception.rs:443-465`: `Held { target: attack_target }`), and a ranged lock at 400 u
   is within vision (`VISION_RADIUS` 900). A `Held { target: None }` (redacted) names nothing: the fallback the
   implementer may add for it, the elite itself within `lock_range` of the weapon in hand, is not pinned by the oracle.
   Being merely within weapon reach without a lock is not fighting (guard). Nothing else in the fight changes: the 120 s
   bound from the first blow, the bans, the give-up count.
3. No price, no new field in `Prices`, nothing outside `exec/elite.rs` except the new test module and its `mod` line. Not
   `exec/travel.rs` (other executors rely on its arrival rule), not `exec/sustain.rs`.

## 2. Dependencies

`coord/int-87` (P-RAID-E-leave-fix merged: `give_up`, `ban_seconds_after`, `chest_step`, the chest exemption in Sustain).

## 3. Relevant legacy paths

None: RaidBot has no legacy counterpart. Searched `docs/OWNER_DECISIONS.md` for the subject: only the row above.

## 4. New paths

`crates/rh-policy/src/bots/raid/exec/elite_leave_fix2_tests.rs` (oracle; registered by `mod leave_fix2_tests;` at the end of
`exec/elite.rs`, from the `stub:` commit).

## 5. Exclusive write scope

`crates/rh-policy/src/bots/raid/exec/elite.rs` (and the `mod` line in it).

## 6. Contracts and references

C-INPUT, C-OBS (the lock and the mobs are the seat's own observation), C-TICK, determinism (no draw added; ordered
iteration), `crates/rh-policy/AGENTS.md`. Nothing reaches a live room.

## 7. Behaviour to preserve

No draw added or moved; registry order unchanged; `Prices::farm_only()` proposes nothing from Elite; `chest_over` bounds;
the 120 s bound, bans and give-up counts of `fight_step`; `raid_seats` replays exactly except where a seat now picks.

## Existing tests that pin what changes

| Test | Asserts | Conflict |
|---|---|---|
| `exec/elite_leave_fix_tests.rs` (11) | chest exemption in Sustain, own-zombie rule, one give-up per time-out, a fight under way with the elite at 400 u (seat at 400 u of a Sword: fight began within 240 u first) times out once, hand-away is one give-up | none: its fights begin within `FIGHT_U`; its chest frames call Sustain, not `chest_step` |
| `exec/elite_tests.rs`, `exec/elite_leave_tests.rs` | pricing, the elite verdict, the ban table | none: no frame calls `chest_step` or `fight_step` with a far seat (searched `chest_step`, `fight_since`, `elite's chest`: only `elite.rs` and `elite_leave_fix_tests.rs`) |
| `tests/raid_elite.rs`, `tests/raid_seats` (rh-headless) | executioner kill and card within 400 ticks on 4 of 6 seeds; seats replay | none expected; the implementer runs both |

No existing expectation conflicts with the fix.

## 8. Acceptance checks (the implementer's; not part of the oracle)

- `scripts/gate.sh --quick --base coord/int-87 --oracle packet/P-RAID-E-leave-fix2` passes.
- `cargo test --locked -p rh-headless --test raid_seats` stays green (the quick gate of an rh-policy packet does not run it).
- Lab, release, seeds 1 to 12, `--human-seats --max-ticks 6000`, eight `raid` seats, base against head: elite kills
  (the lab's elite table) and how many of the credited killers picked from the chest. The review counted 16 of 19 on the
  base with the three misses in seeds 1, 3 and 7; the fix should recover seeds 1 and 7 (seed 3, camp 48, is another cause).
  Deaths not up beyond noise.

## 9. Validation commands

`export PATH="$HOME/.cargo/bin:$PATH"`; `cargo test --locked -p rh-policy --lib leave_fix2`; `cargo test --locked -p
rh-policy --lib leave_fix`; lab: `cargo build --release --locked -p rh-headless --bin balance_lab`; `SEATS=(); for i in 0 1 2 3 4 5 6 7;
do SEATS+=(--seat "$i=raid"); done; ./target/release/balance_lab --human-seats --seeds 1..13 --max-ticks 6000 "${SEATS[@]}"`.

## 10. Risk and suggested model tier

implementer-opus, reviewer-opus. Risks: a seat that now walks to the chest leaves a fight with adds still alive (the
Sustain exemption of the previous packet bounds that); a lock on an add recorded as the elite fight makes a bot that only
trades with an add count as fighting (intended: the add is of the camp).

## 11. Known traps

- `fight_since` feeds `watch_elites` (kill record), `watch_goal` (ban), `track` (tally): setting it earlier for ranged
  seats changes all three, as intended; do not set it for a seat with no lock.
- `chest_step` runs also while the chest is not yet delivered ("looking for the elite's chest"): unchanged.
- `step_toward` returns the destination itself within `2 * ARRIVE_U` or when the line is clear.
- `Elite::act` runs `chest_step` only once `killed` has the camp.

## Oracle files (read-only for implementers)

(filled in the `oracle:` commit)
