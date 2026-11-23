# P-RAID-HOLD-1: a seat healing up stops standing when it is hurt or cannot regenerate

- **Serves:** not in the plan: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot,
  per-elite numbers"); reviewer-opus measurement on P-RAID-C-fix (2026-11-23)
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer-opus
- **Integration base:** `coord/int-86` @ `29d412b12`
- **D-rules touched:** D1 loose proof (owner direction 2026-11-10) · D2 n/a · D5/D6/D9 none (no sim, wire, observation or
  client change)

## 1. ID and outcome

While `RaidBot` is "healing up" (the stand after a leave, or the plain heal-up candidate `Sustain/Target::None`) it does
not keep standing still when (a) its hp has fallen since the stand began or it was hit within the last `hurt_seconds`, by
any source, or (b) regeneration cannot happen (locked gates). Today the stand ends only at hp `ready_hp` or when a
regional mob comes within reach.

## 2. Dependencies

`coord/int-86` @ `29d412b12`. Sibling packet P-RAID-E (in review) appends `elite` last in `Prices` and owns
`exec/elite.rs`: this packet adds fields to `SustainPrices` only and does not touch `Prices`.

## 3. Relevant legacy paths

None: RaidBot has no legacy counterpart (P-RAID-C section 3). Sim facts below are the oracle.

## Established facts (code at `29d412b12`)

- **F1. The hold is a stand.** `Sustain::think` (`exec/sustain.rs:289-323`) returns `Step::running(Vec::new(), "healing up")`
  (line 322) unless `nearest()` finds a threat or hp is at `ready_hp` (line 318). `nearest()` (line 168-177) and `view()`
  keep `MobClass::Regional` rows only. The heal-up candidate (line 374-384) is offered while `hp < ready_hp` and
  `nearest()` is none; it never looks at `hurt_at`.
- **F2. The hurt clock exists and is unused by the hold.** `watch()` (line 114-119) sets `hurt_at` on a hp fall or
  `cx.p.hit_at`; only `in_fight()` (line 139) reads it, for fights, not for the hold. `hurt_seconds` is 3.0 (`values.rs:56`,
  98).
- **F3. Regeneration** (`crates/rh-sim/src/systems/regen.rs:36-58`): nothing while `w.rt.gates_locked`; otherwise
  `PLAYER_REGEN_SHARE_OUT` (0.8 % of max hp a second) only after `PLAYER_REGEN_QUIET_MS` (3000 ms,
  `constants.rs:385`) with no hit dealt or taken; in combat the share is 0 (`regen_in_combat` Off under
  `SimConfig::live_content()`). So a stand while hurt by an unseen source never heals, and in a locked coliseum a stand
  can end only in death.
- **F4. The percept shows the locked gates.** `cx.p.base.hud.gates_locked` (`percept.rs:349` reads the same field for
  `loot::View`; `HudView` is `pub`). So (b) is pinned, not a question.
- **F5. Measured by reviewer-opus** (lab, release, 8 raid seats, 4800 ticks, seeds 1 to 6; not re-measured here): 114
  "healing up" thinks with a hit in the last second (30 during a leave), longest run 6.2 s; seed 1 a heal-up stood while hp
  fell 0.216 to 0.009 over 2 s, in the minute of that seed's only death; seed 5 a leave stood while hp fell 0.57 to 0.34
  over 4 s.
- **F6. Measured here: a whole-sim scene cannot isolate it.** Scenes tried on seed1.map (one `RaidBot` at hp 0.5 with
  `rest_value` 50 so the heal-up outranks camps; a second seat of another faction, `scripted::Brawler`, 450 u away with
  fists or sword; seeds 1 to 16, 900 ticks): the seat dies at tick 235 to 343 in every seed. Its goal leaves "healing up"
  by itself at the first hit in all but a few ticks (zero ticks stood within 1 s of a hit on every seed), so the fall
  while standing the lab saw does not appear; the sim's locked gates need a full lifecycle checkpoint
  (`arena_validation.rs:15-23` refuses `gates_locked` with `ArenaMode::Off`). The oracle therefore drives the executor
  itself (section "The oracle").

## 4. New paths

`crates/rh-policy/tests/raid_hold.rs` (the oracle).

## 5. Exclusive write scope

- `crates/rh-policy/src/bots/raid/exec/sustain.rs`
- `crates/rh-policy/src/bots/raid/values.rs`: appended `SustainPrices` fields only (in both `OFF` and `LOCAL`); `Prices`
  itself and `Prices::farm_only` are not touched. The thresholds are fields, never constants in the executor.

## 6. The design decision (the oracle does not fix the "how")

The percept shows the three things the executor needs: `p.hit_at`, the hp share and its fall (`hurt_at`, `prev_hp`
kept by `watch()`), and `cx.p.base.hud.gates_locked`. It does not show who hurt the seat when the source is a damage-over-time
effect or an area, but it does show other players (`cx.p.players`) and every mob class (`cx.p.mobs`).

Default design (the coordinator's, adopted with this reason: moving breaks the hit chain that blocks regeneration, and it
reuses `kite::away_point`):

1. **Hurt (a):** the hold counts as hurt when `watch()` stamped `hurt_at` within `hurt_seconds` or hp fell by more than a
   new `SustainPrices` threshold since the stand began (kept in the executor, set at `start`). Then `think` ends the stand:
   it moves along `away_point` from the nearest visible threat of any class (mob of any `MobClass`, or a player of another
   faction), or along the last heading when none is in view, drinking as `ride_along` already does. The heal-up candidate
   is not proposed while hurt, and not again until `hurt_seconds` pass with hp no lower.
2. **No regeneration (b):** `gates_locked` ends the stand (`Step::failed`, so the arbiter decides again) and the heal-up is
   not proposed while it holds. Potion drinking is unchanged.
3. **State:** any new field is cleared in `observe` when `cx.goal.kind != Sustain` and in `reset` (P-RAID-C-fix's stale
   leave came from state that outlived its goal). Nothing draws from `cx.rng`.
4. The leave's own retreat is unchanged; this applies to the stand part only. It must not become a second leave rule:
   `leaving` and `cornered_until` keep their meaning.

## 7. Behaviour to preserve

- Sustain OFF replays byte for byte (`raid_bot.rs`, `fix_tests.rs`, `review_tests.rs`, `raid_seats.rs` unchanged).
- No draw added, removed or moved.
- `raid_sustain`, `raid_sustain_fix`, `raid_bot`, `raid_dwell`, `raid_decide`, `raid_quests` stay green.
- A quiet stand still stands and reaches `ready_hp` (guards in the oracle).

## 8. Acceptance checks

- `scripts/gate.sh --quick --base coord/int-86 --oracle packet/P-RAID-HOLD-1` passes.
- `cargo test --locked -p rh-headless --test raid_seats` passes (the quick gate does not run it).
- Lab comparison, seeds 1 to 6 at 4800 ticks, release, before and after (the command of P-RAID-C-fix section 8): camps
  cleared and deaths not worse than the base beyond noise (reviewer-opus's figures for the tree before the quest-decision
  fixes were 98 camps and 8 deaths; measure the base row on `coord/int-86` too). Report the count of "healing up" thinks
  with a hit in the last second (base 114).

## The oracle (loose proof) and its choices

`crates/rh-policy/tests/raid_hold.rs`: the executor driven frame by frame as the seat drives it (`start`, `observe`,
`act` / `propose`) on hand-built percepts with no mob in view, so the hold's own view sees nothing, like another player's
shot, a damage-over-time effect or an elite. "Standing" is a step that is `Running` with no intents and no acts. Bound:
within 20 frames (1 s) of the first fall or hit. Red on the base (assertions), checked:

1. `a_stand_ends_when_hp_falls_from_a_source_the_hold_does_not_see`: hp falls 0.01 a frame, no hit event; the base stands
   for all 60 frames.
2. `a_stand_ends_when_the_seat_is_hit`: the same with a hit every 5th frame.
3. `a_heal_up_is_not_proposed_while_something_hurts_the_seat`: the base offers it in all 20 of the last 20 frames.
4. `a_stand_ends_when_the_gates_are_locked`: locked gates, hp 0.5: the base stands.
5. `guard_the_hold_state_ends_with_its_goal_and_the_rng_is_untouched`: red on the base only through its setup (the stand never
   ends); after the fix it pins that a fresh stand after another goal held the seat is quiet and the rng is untouched.

Green on the base and to stay green: `guard_a_quiet_stand_still_stands_heals_and_ends_at_ready_hp` (stands 400 frames,
done at 0.86), `guard_a_hit_longer_ago_than_hurt_seconds_does_not_end_the_stand`, `guard_sustain_off_proposes_nothing_and_never_acts`.

## Oracle files (read-only for implementers)

- `crates/rh-policy/tests/raid_hold.rs`
- `docs/backlog/packets/P-RAID-HOLD-1.md` (this brief)

No existing test or capture is amended.

## 9. Validation commands

`cargo test --locked -p rh-policy --test raid_hold`, then `--test raid_sustain`, `--test raid_sustain_fix`,
`--test raid_bot`, `--test raid_dwell`, `--test raid_decide`, `--test raid_quests`; `cargo test --locked -p rh-headless
--test raid_seats`.

## 10. Risk and suggested model tier

implementer-sonnet; reviewer-opus. The care: moving away must not fight the leave or the farm (a stand that ends under
fire hands the seat to the arbiter, which may re-offer the heal-up: hence the "not again until hurt_seconds" rule), and a
seat cornered by walls must fail rather than spin.

## NOT ESTABLISHED

- That the real harm happens in the whole-sim as the executor-level frames model it: no whole-sim scene reproduced the
  lab's stand-while-falling (F6), so the live effect (deaths, camps) rests on the lab comparison, not on the oracle.
- Which direction the seat should run when no threat is in view (only "not standing" is pinned).
- The hp-fall threshold's number (the oracle uses 0.01 a frame, far above any sensible value).

## Questions for the coordinator

- Locked gates: the default ends the stand and stops proposing the heal-up (no movement). If the owner wants the seat to
  head for something instead (the coliseum fight), that is a separate packet.
