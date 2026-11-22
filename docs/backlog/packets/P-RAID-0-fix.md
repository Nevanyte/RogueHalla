# P-RAID-0-fix: RaidBot seams (a committed fight can be pre-empted; the death model and chest pick go through `Prices`)

- **Serves:** not in the plan: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot,
  per-elite numbers"; plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 1). Fixes the two notes of the
  `reviewer-opus` verdict LAND WITH NOTES on P-RAID-0, before P-RAID-C, -V, -E and -Q* inherit the seams.
- **Implementer tier:** implementer-opus (bot priority competition: `crates/rh-policy/AGENTS.md` "Routing")
- **Reviewer:** reviewer-opus
- **Integration base:** `coord/int-82` @ `81c42a70a`
- **D-rules touched:** D1 loose proof (owner direction 2026-11-10); the bot's play must not change · D2 n/a · D5/D6/D9
  none (no sim, wire, observation or client change; D9: the bot still acts only through `validate` + `Sim::submit`)

## 1. ID and outcome

`RaidBot` plays exactly as before. Underneath: (1) the arbiter keeps a committed, running incumbent only when the
top-ranked candidate is not a kind that may pre-empt (`GoalKind::preempts_commitment()`, true for `Sustain`), so the next
packet's potion-and-retreat candidate can win against a fight whose own score is still positive, with no edit to
`arbiter.rs`; (2) the farm death model and the chest pick numbers are fields of `Prices` (`Prices::LOCAL` holds today's
values), so `RaidBot::with_prices` and P-RAID-V's table move them; (3) `Prices` lives in `values.rs`, and gains
`reward_value(def)` and `augment_value(card)` for the quest and elite packets; (4) the farm follows the mob the owner has
locked; (5) two doc fixes.

## 2. Dependencies

P-RAID-0 (merged in the base). Later packets (P-RAID-C, -V, -E, -Q1..Q4, -P, -W, -S) depend on this one.

## 3. Relevant legacy paths

None: RaidBot has no legacy counterpart (P-RAID-0 section 3). Nothing in `packages/` is read or changed.

## Established facts (verified on `coord/int-82` @ `81c42a70a`)

**Note 1, the pre-emption hole.** `crates/rh-policy/src/bots/raid/arbiter.rs:142-148`: after `ranked.sort_by(better)`
(:141), `kept = (status == Status::Running && committed).then(|| ranked.iter().find(|s| s.candidate.goal.same(held) &&
s.score > 0.))`; :149 `winner = kept.or(ranked.first())`. So a committed running incumbent with a positive score wins
over every candidate, whatever its rank. `Farm` is committed while a member is within `FIGHT_U` (`exec/farm.rs:229-250`),
and `death_chance` floors at 0.02 (`farm.rs:49`), so a fight at 30% hp with one full-hp member left still scores
positive: no Sustain candidate could win. The doc comments say the same (`arbiter.rs:84-85`, `goal.rs` `Step::committed`).
`GoalKind::Sustain` exists (`goal.rs:15`, first in declaration order, so it also wins exact ties) but nothing proposes it
yet (`exec/sustain.rs` is a header).

**Note 2, the death model and the pick are constants.** `exec/farm.rs`: `TYPICAL_MEMBERS = 4.` :16 (read :163,
`value: camp * alive / TYPICAL_MEMBERS`), `FIGHT_WEIGHT = 3.` :31 (read in `hp_needed` :42), `DEATH_PER_RATIO = 0.4` :33 and
`SAFE_RATIO = 0.65` :34 (read in `death_chance` :47-50, called :160). `exec/chest.rs`: `PICK_SECONDS = 3.` :12 (read
:75, `minutes`) and `PICK_SUCCESS = 0.9` :14 (read :72, `success`). None goes through `Prices`
(`arbiter.rs:14-46`: `camp 1, stacks_per_camp 12, chest 1.5, roam_per_minute 0.1, death_loss 6, kill_seconds 10`), so
`RaidBot::with_prices` (`bot.rs:110-113`) cannot move them. `bot.rs:6` imports `Prices` from `arbiter` (`bot.rs` is
outside the write scope: `arbiter.rs` keeps `pub use super::values::{DeathModel, Prices}` so that line compiles).

**Every constant in `bots/raid/**` that prices or times something, and whether it reads `Prices`:**
- `arbiter.rs`: `HYSTERESIS 0.25`, `HYSTERESIS_FLOOR 0.05`, `MIN_MINUTES 0.05`: how the arbiter ranks, not a price. Stay.
- `Prices` fields `camp`, `stacks_per_camp`, `chest`, `roam_per_minute`, `death_loss`, `kill_seconds`: already read
  through `cx.prices` (`farm.rs:163,166,167`, `chest.rs:70,73`, `travel.rs:123`). Unchanged.
- `farm.rs` `TYPICAL_MEMBERS 4`, `FIGHT_WEIGHT 3`, `DEATH_PER_RATIO 0.4`, `SAFE_RATIO 0.65`: the death model. MOVE to
  `Prices::death: DeathModel` (`typical_members`, `fight_weight`, `death_per_ratio`, `safe_ratio`).
- `chest.rs` `PICK_SECONDS 3`, `PICK_SUCCESS 0.9`: MOVE to `Prices::pick_seconds`, `Prices::pick_success`.
- `farm.rs` `MELEE_LOCK_U 40`, `DEFEND_U 96`, `FIGHT_U 240`, `TRACK_U 80`, `EMPTY_MS 3000`, `FAILED_BAN_MS 20000`: the
  act loop's geometry and timing (where to click, when to give a camp up). Not values. Stay local.
- `travel.rs` `ARRIVE_U 48`, `NEAR_U 150`, `STILL_MS 1200`, `ROAM_MIN_U 600` (outside the write scope): movement
  geometry. Stay.
- `goal.rs` `tier_mult` skill 0.85 / 1 / 1.15 and `persona_mult` `0.5 + 0.5 * trait`: score multipliers by tier and
  persona, not prices. Stay (P-RAID-V may revisit them in its own packet).
- `acts.rs` `MAX_QUEUED 8`, `ACT_TTL_MS 4000`, `ACT_GAP_MS (180, 420)`, `ACT_RESERVE 6` and `bot.rs` `ARBITER_TAG 16`,
  `ACTS_TAG 17`: pacing and stream tags. Outside the scope; stay.
- NEW in `Prices`: `reward_default`, `augment_default` (the flat defaults behind `reward_value` and `augment_value`;
  LOCAL value 1.0 each is a placeholder, not a measured price: P-RAID-V replaces it).

**How a test builds the arbiter.** `Arbiter::default()`, then `choose(now: f64, candidates: Vec<Candidate>, incumbent:
(&RaidGoal, Status, bool /* committed */), tier: &TierParams, persona: &PersonaParams, rng: &mut Rng) -> Option<RaidGoal>`
(`arbiter.rs:108-116`); `TierParams::by_id(TierId::Normal)`, `PersonaParams::by_id(PersonaId::Farmer)`, `Rng::new(16)`;
the in-file test at `arbiter.rs:155-` does exactly this. An executor test additionally builds a `Cx` (`goal.rs`
`Cx`): a hand-built `RaidPercept` (every field is `pub`; the oracle builds it by literal, so the implementer adds no
field to `RaidPercept` or `Me`), `PlanMap::from_arena(&arena)` and `RoomPathService::new(&arena.map, PathBudget::default())`
on `crates/rh-sim/src/mapdata/fixtures/seed1.map.json`.

**Where `owner_lock` is in the percept.** `RaidPercept::base: Percept` (`percept.rs`) is the shared hand percept;
`base.self_view.owner_lock: Option<OwnerLock>` (`bots/types.rs:1064-1067`, `Clear | Held { target: Option<FighterId> }`,
`FighterId = String`). `perception.rs:431-448` fills it from the owner's wire lock; `Held::target` is the rendered key
(`npc_key(id)` = `n<id>`, `perception.rs:89`) of the actor in THIS frame, or `None` when the locked actor is not in the
frame's actors. `percept.rs:349-352` already reads the `Held` variant for `loot_view`. The key is this frame's label for
`MobSeen::key`, so equality with `mob.key` identifies the mob within one frame only.

**Doc contradictions found.** `percept.rs:85` (`MobSeen::key`): "stable across frames" contradicts `percept.rs:4`
("An npc's key is new in every frame") and `goal.rs:95-96` (`Target::Actor`: "an npc's wire id, and so its `n<id>` key,
is new in every frame"). The extension guide in `mod.rs:11-18` says a later packet edits "its own file plus one line in
`exec::registry`" and "A kind the plan did not name adds one `GoalKind` line too" and never says that `GoalKind` lives in
`goal.rs` or that a quest module also needs its `quest/` file declared in `quest/mod.rs`; it must list exactly the
shared files a later packet touches.

## Existing tests that pin what this packet changes

- `crates/rh-policy/tests/raid_bot.rs` (4 tests): precondition and `HumanBot` control, `RaidBot` clears the scene camp
  within 1,800 ticks on at least 7 of 8 seeds, `SeatStats.rejected == 0`, replay equal (same clear tick, stats, state
  hash). Stays green and unedited. Measured on the base, seeds 1-8, clear ticks **258, 112, 1257, 362, 139, 321, 319, 399**
  (from the coordinator; the brief does not re-measure them): moving constants into `Prices::LOCAL` and adding a
  pre-emption rule that nothing triggers must leave them identical; the lock-target preference may move them, and if it
  does the implementer reports before and after.
- `arbiter.rs` `death_term_and_ties_are_deterministic` (in-file): hysteresis, ties, "a committed incumbent stays while
  its score is positive and yields once the fight is losing", with Farm and Roam candidates only. No Farm or Roam
  candidate pre-empts, so it stays green unedited.
- `exec/farm.rs` `death_chance_grows_with_the_fight_and_falls_with_hp` (in-file): calls the free function
  `death_chance(hp, share, members)` and asserts monotonicity, the 0.02 floor, `< 0.1` for 3 members and `> 0.3` for 5 at
  `share 0.965`. If the implementer gives `death_chance`/`hp_needed` a `&DeathModel` parameter, the only permitted edit
  to this test is passing `&Prices::LOCAL.death` at each call: no assertion changes. Not an `oracle-amend:`; it is in the
  write scope and compile-only.
- Nothing else names `Prices`, the death-model constants or the pick constants outside `bots/raid/` (grep over
  `crates/`: only `bot.rs:6,45,83,111-112,246,262,435`, which use `Prices` as a type and `Prices::LOCAL`).

No conflict with any existing test; none needs an amendment.

## 4. New paths

- `crates/rh-policy/src/bots/raid/fix_tests.rs` (the oracle; declared by a `#[cfg(test)] mod fix_tests;` line that the
  `stub:` commit adds to `mod.rs`).

## 5. Exclusive write scope

`crates/rh-policy/src/bots/raid/{goal,arbiter,values,percept,mod}.rs` and `crates/rh-policy/src/bots/raid/exec/{farm,chest}.rs`.
Nothing else (`bot.rs`, `exec/mod.rs`, `exec/travel.rs`, `tests/raid_bot.rs`, `fix_tests.rs` are untouched; `Cargo.*`
too). `mod.rs` keeps the `#[cfg(test)] mod fix_tests;` line.

## 6. Contracts and references

C-INPUT, C-OBS, C-POLICY; `crates/rh-policy/AGENTS.md` ("priorities compete every decision tick"); `clippy.toml` (no
hash-ordered collections, no clock, no platform libm); `docs/agent/READABILITY.md`; `BALANCE_LAB_PATCH.md` section 1.

## 7. Behaviour to preserve

- RaidBot's play: `tests/raid_bot.rs` green and unedited; clear ticks on seeds 1-8 identical to the list above (report
  before/after if the lock-target preference moves any).
- The arbiter stream's draw order and count (one `uniform` per `choose`), the registry order, `Prices::LOCAL`'s existing
  six values.
- `Prices` stays `Copy + PartialEq + Debug` and `Default`; `RaidBot::with_prices(Prices)` keeps its signature;
  `arbiter::Prices` stays reachable (re-export) because `bot.rs` imports it from there.
- No new `Cx` or `RaidPercept` field (the oracle builds both by literal).

## 8. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-RAID-0-fix` passes.
- `cargo test -p rh-policy --lib raid::` and `cargo test -p rh-policy --test raid_bot` pass.
- Design points the oracle does not see, for `reviewer-opus`: `preempts_commitment` has a doc stating when a kind may
  claim it (the kind must be one whose job is to leave or end the fight: Sustain, and later a flee; never a goal that
  starts new work); `Prices` is defined in `values.rs` only; `arbiter.rs` holds no price; the fix to `percept.rs:85`;
  the guide in `mod.rs` names exactly the shared files a later packet touches (the `exec/mod.rs` registry line, and
  nothing else: `GoalKind` already declares every kind the plan names, `Executor` needs no edit, `Prices` accessors are
  filled by P-RAID-V in `values.rs`); `Prices::reward_value` and `augment_value` are the only way later packets read a
  reward or augment price.

## Oracle files (read-only for implementers)

- `crates/rh-policy/src/bots/raid/fix_tests.rs`
- `docs/backlog/packets/P-RAID-0-fix.md`

What it asserts (one scene: camp 0 of `seed1.map.json`, owner Holy 200 units east of the camp, active raid, no
weapons; the farm's `propose` and `act` are called on a hand-built `Cx`):
1. `only_sustain_may_claim_a_committed_fight`: `GoalKind::Sustain.preempts_commitment()`; Farm, Roam, Chest do not.
2. `a_top_ranked_sustain_candidate_takes_over_...`: committed running Farm incumbent, own rate positive (death 0.02),
   plus a Sustain candidate that ranks first: `choose` returns Sustain. Red on the base.
3. `a_committed_fight_is_kept_against_a_top_ranked_candidate_that_does_not_pre_empt`: a better Farm, a better Roam, and a
   Sustain that does not rank first: the incumbent stays (green on the base, pins today's behaviour).
4. `prices_answer_for_quest_rewards_and_augments_from_their_table`: `reward_value` and `augment_value` are finite and
   non-negative on `LOCAL`, and a doctored `reward_default`/`augment_default` is returned for any key. Red on the stub.
5. Death-model, pick and lock-target tests: see "Added with the oracle" below.

## 9. Validation commands

`export PATH="$HOME/.cargo/bin:$PATH"; cargo test -p rh-policy --lib raid::`; `cargo test -p rh-policy --test raid_bot`;
`scripts/gate.sh --quick --oracle packet/P-RAID-0-fix`.

## 10. Risk and suggested model tier

implementer-opus, reviewer-opus: it changes how bot priorities compete (`AGENTS.md` routing for rh-policy). Small
(about 150 changed lines). Hard part: leaving the play bit-identical while the constants move and the kept-target rule
changes.

## 11. Known traps

- `Cx::prices` is `&Prices`; `Prices::LOCAL` is a `const`, so `death_chance` needs the model passed, not a global.
- `death_chance` clamps to `[0.02, 0.98]`: a doctored model in a test must be read where the clamp does not hide it.
- The kept-target change restricts the position fallback to the goal's camp too; `Farm::pick` still takes an engaged
  member of another camp within `DEFEND_U` when nothing is kept (a mob on top of us is fought first, unchanged).
- A held lock whose `target` is `None` (the locked actor is not in the frame) is no lock: fall back to position.
- Do not add fields to `RaidPercept`, `Me` or `Cx`; read `cx.p.base.self_view.owner_lock`.

## Notes from previous attempts
<!-- Coordinator appends on escalation. -->
