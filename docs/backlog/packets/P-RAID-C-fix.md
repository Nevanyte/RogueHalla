# P-RAID-C-fix: a leave must not outlive the Sustain goal that held it

- **Serves:** not in the plan: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot,
  per-elite numbers"); the one reviewer-opus note on P-RAID-C (`docs/backlog/packets/P-RAID-C.md`)
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer-opus (a goal that pre-empts a commitment: `GoalKind::preempts_commitment`)
- **Integration base:** `coord/int-85` @ `c348a5b98`
- **D-rules touched:** D1 loose proof (owner direction 2026-11-10) · D2 n/a · D5/D6/D9 none (no sim, wire, observation or
  client change)

## 1. ID and outcome

`RaidBot` forgets a leave once the seat no longer holds a Sustain goal. After another goal has taken the seat, an earlier
leave comes back only if the death model calls the current fight lost again. Today it comes back at any strategy tick
with hp below `ready_hp`, over any goal, near the old camp or not, and pre-empts a committed Farm.

## 2. Dependencies

`coord/int-85` @ `c348a5b98` (P-RAID-C and P-RAID-Q3 merged, not yet gated). A sibling packet (P-RAID-Q3-fix) edits
`values.rs` and its tests at the same time: this packet does not touch them.

## 3. Relevant legacy paths

None: RaidBot has no legacy counterpart (P-RAID-C section 3).

## Established facts (code at `c348a5b98`, `crates/rh-policy/src/bots/raid/exec/sustain.rs`)

- **F1. `leaving` is set at `start` (line 381-386) and cleared in two places only:** `think`, at "cornered" (line 312) and
  at "healed" (line 318-320, `hp >= ready_hp` once no member is in reach), and in `reset`. `think` runs only while the
  Sustain goal is the seat's goal (`act`, line 387).
- **F2. The hold ends where entry is gated.** `propose` (line 350-367) offers the leave while
  `self.leaving.filter(|_| hp < s.ready_hp)` or a lost fight; `fight()` (line 262-273) calls a fight lost only with
  `hp < ready_hp` too. When hp passes `ready_hp` (regen, a potion) with a member in reach, `think` keeps retreating but
  `propose` offers nothing, the arbiter hands the seat to Farm (or another goal), and `leaving` stays `Some(old camp)`.
- **F3. The stale leave.** From then on, at any strategy tick with hp below `ready_hp`, `propose` pushes
  `leave_candidate(old camp)` (line 365): worth `leave_value` 3 over `leave_seconds / 60` = 30 camp-equivalents a minute.
  It pre-empts a commitment (`preempts_commitment`, `arbiter.rs:115`) without asking `fight()` or the death model, even
  far from that camp or with no fight. With no regional member near, `think` answers "healing up" (line 322): the bot
  stands still at 30 ce/min until hp reaches 0.85 (25 s or more); the intended heal-up (`Target::None`, line 369-379) is
  worth 0.288 ce/min, which a camp beats.
- **F4. A leave the death model called lost names the camp the bot works.** `view()` (line 180-213) prefers the camp of
  the seat's own Farm/Sustain goal when it has members alive, so a legitimate adoption from `Farm/Camp(d)` is
  `Sustain/Camp(d)`, and it needs a member within `threat_u` (240 units melee, a bow's range plus 60 more) of the bot.
- **F5. Measured by reviewer-opus** (lab, release, 8 raid seats, 4800 ticks, seeds 1 to 6): 5 of 50 Sustain adoptions
  stale, all pre-empting a Farm goal at hp 0.77 to 0.84 with `lost = false`; seed 6 at t = 144450 a stale leave adopted from
  Farm with no mob in the percept, the bot standing and healing at hp 0.65 to 0.70.
- **F6. Measured by this oracle's scene on the base** (debug build, `raid_sustain_fix.rs`: a four-member camp 100 u away,
  start hp 0.5, two large potions, sustain on, `death_loss 0` so the farm takes the fight, 2400 ticks): the seat leaves,
  drinks and hands over to Farm at hp 0.85 or more on seeds 5, 6, 7, 10, 12, 13, 14 (of 1 to 14). Stale leaves adopted:
  **seed 5, tick 2168, hp 0.848, nearest member 26 u, `Farm/Camp(34)` -> `Sustain/Camp(51)`; seed 7, tick 1727, hp 0.815,
  nearest 37 u, `Farm/Camp(36)` -> `Sustain/Camp(51)`**. Camp 51 was left earlier (seed 5 t922, hand-over t1473; seed 7
  t826, hand-over t1583). Seeds 1 to 8 are the sweep: two stale, six clean.
- **F7. Checked: the reviewer's one-line fix turns the scene green.** With
  `.filter(|_| hp < s.ready_hp && cx.goal.kind == GoalKind::Sustain)` in `propose` (applied in the worktree, then reverted)
  the new scene test passes with its hand-over floor intact and `raid_sustain` stays green (4 of 4).
- **F8. Drift guard.** `SustainPrices::LOCAL.large_share/small_share/heal_seconds/regen_per_second` are hand copies of
  `rh_sim::potions::DEFINITION.large_share/small_share/heal_ms` and `rh_sim::constants::PLAYER_REGEN_SHARE_OUT`. All are
  `pub` (`potions/mod.rs:25`, `constants.rs:382`): nothing left out, no rh-sim change. Green on the base, by design.

## 4. New paths

`crates/rh-policy/tests/raid_sustain_fix.rs` (the oracle: new file, so it does not overlap the sibling packet's edits).

## 5. Exclusive write scope

- `crates/rh-policy/src/bots/raid/exec/sustain.rs` only. No `values.rs` change is needed: the fix needs no new
  `SustainPrices` field. (An optional end-of-hold margin above `ready_hp` would be one; if the implementer wants it,
  stop and tell the coordinator, because `values.rs` merges one packet at a time.)

## 6. Contracts and references

C-TICK and determinism (`propose` and `observe` draw nothing), `crates/rh-policy/AGENTS.md` ("a commitment is the
exception: `GoalKind::preempts_commitment`").

## 7. Behaviour to preserve

- Sustain OFF (`Prices::farm_only()`) replays byte for byte: `raid_bot.rs`, `fix_tests.rs`, `review_tests.rs` unchanged.
- No draw added, removed or moved (`Sustain` draws nothing from `cx.rng`).
- `raid_sustain.rs`, `raid_quests.rs`, `raid_dwell.rs`, `raid_decide.rs` and `crates/rh-headless/tests/raid_seats.rs` stay
  green (the last needs its own command: the quick gate of an rh-policy packet does not run rh-headless tests).
- A fight the death model calls lost is still left within a second; a leave still holds until `ready_hp` while the goal is
  Sustain.

## 8. Acceptance checks

- `scripts/gate.sh --quick --base coord/int-85 --oracle packet/P-RAID-C-fix` passes.
- `cargo test --locked -p rh-headless --test raid_seats` passes (implementer and reviewer).
- Lab comparison, seeds 1 to 6 at 4800 ticks, release, before and after:
  `cargo run --release -p rh-headless --bin balance_lab -- --human-seats --seeds 1..7 --max-ticks 4800 --seat 0=raid ...
  --seat 7=raid` (all eight seats spelled out). Camps cleared is not lower than the reviewer's 83 by more than noise;
  report stale adoptions (none expected), deaths and clears.

## The oracle (loose proof) and its choices

`crates/rh-policy/tests/raid_sustain_fix.rs`, two tests:

1. `a_leave_does_not_outlive_the_sustain_goal_that_held_it` (**red on the base**, assertion). The scene is F6, run on seeds 1
   to 8. The seat's `RaidBot` is shared through a mutex (`Probe`) so the test reads `RaidBot::goal()` each tick. It asserts
   (a) a floor: at least 3 hand-overs of a leave at hp 0.85 or more across the seeds (so the scene exercises the note, and
   the fix cannot pass by never handing over), and (b) no Sustain goal on a camp is adopted from another goal when the goal
   before was a Farm on a different camp (F4) or no regional member is within 600 u. The hp edit is the potion, not a
   checkpoint mid-run (a mid-run `Sim::restore` of hp made the bot stand still and bleed in the lab; not investigated).
2. `the_sustain_prices_match_the_sim_numbers_they_copy` (green on the base): the F8 drift guard.

**Not pinned:** the heal-up case with hp dipping at full distance (the scene never produced a stale leave with no member
within 600 u in 8 seeds; the criterion is in the test but unproven to fire); the exact tick of the hand-over.

## Oracle files (read-only for implementers)

- `crates/rh-policy/tests/raid_sustain_fix.rs`
- `docs/backlog/packets/P-RAID-C-fix.md` (this brief)

No existing test or capture is amended.

## 9. Validation commands

- `cargo test --locked -p rh-policy --test raid_sustain_fix` (about 15 s, debug), `--test raid_sustain` (about a minute),
  `--test raid_bot`, `--test raid_dwell`, `--test raid_decide`, `--test raid_quests`.
- `cargo test --locked -p rh-headless --test raid_seats`.

## 10. Risk and suggested model tier

implementer-sonnet: one executor, a few lines. The care: the leave must still survive the goal's own thinks (a retreat in
progress is Sustain, so `cx.goal.kind == Sustain` holds), and a fresh `lost` fight must still start one. reviewer-opus
reads the pre-emption.

## 11. Known traps

- `start` sets `leaving` from the goal's target, so a Farm-to-Sustain re-adoption through `lost` re-arms it correctly; do
  not clear in `start`.
- Clearing in `observe` and filtering in `propose` differ at the strategy tick that adopts the goal (is `cx.goal` already
  Sustain?): the scene test catches a wrong choice only as a missed leave in `raid_sustain.rs`, so run it.
- The oracle's criterion F4 is a proxy: a Sustain re-adoption naming the camp the Farm works is allowed.
