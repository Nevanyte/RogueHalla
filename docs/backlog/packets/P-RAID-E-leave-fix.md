# P-RAID-E-leave-fix: four defects reviewer-opus found in P-RAID-E-leave

- **Serves:** not in the plan's phases: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot,
  per-elite numbers"); the three notes of reviewer-opus on P-RAID-E-leave (2026-11-23). Headless human seats only.
  Lands before the pilot measurement (notes 1 and 2 bias what it reads).
- **Implementer tier:** implementer-opus
- **Reviewer:** reviewer-opus
- **Integration base:** `coord/int-86` @ `834cdd175`. Straight line; do not merge the base in.
- **D-rules touched:** D1 loose proof (RaidBot has no legacy counterpart) · D2 n/a · D5/D6/D9 none.

## 1. ID and outcome

1. **A kill's chest is lost to Sustain's farm-model leave.** After the kill, `elite_lost` returns `None` (no live elite),
   Sustain prices the adds still alive by the farm model, a seat that won at low hp is judged lost and leaves at once, out
   of the chest's reach, so the priced pick never fires. Lab (release, 8 raid seats, seeds 1 to 12, 6000 ticks): 19 kills
   recorded by the killing seat, 3 with no pick (seed 3 p3 camp 48; seed 7 p1 camp 46; seed 1 p2 camp 27, cause there not
   established); on the base before P-RAID-E-leave 16 kills and 0 without a pick.
   After: a seat that has killed its elite is not pulled off the chest by the leave while the chest phase is within its
   bounds; adds that will kill it still pull it away (decision D1).
2. **A Plague seat's own zombie counts as a threat.** `nearest()` is never `None`, so no heal-up is proposed, and a
   `Sustain/Camp` leave never reaches its stand: it walks from its pet until the arbiter drops the goal at `ready_hp`.
   Lab: seed 8 p5, about 100 s, 50 propose ticks, hp 0.18 to 0.92. After: a zombie of the seat's own faction is no threat.
3. **One time-out counts as two give-ups.** `fight_step` bans and returns `Failed` with `fight_since` still set; the next
   frame's goal switch makes `watch_goal` ban again. Bans run 40, 160, 300 s instead of 20, 40, 80. After: one give-up
   counts once however it ends.
4. **A fight under way has no time bound** (reviewer-opus, integration 86 merge review). After: once `fight_since` is set,
   `now - since > give_up_seconds` ends the fight whether or not the elite is within `FIGHT_U`: ban once, fail.

## 2. Dependencies

Base `coord/int-86` @ `834cdd175` (P-RAID-E-leave merged). It owns the bookkeeping (`giveups`, `ban_seconds_after`) and the
elite term in Sustain (`elite_lost`) that this packet adjusts.

## 3. Relevant legacy paths

None: RaidBot has no legacy counterpart. Searched `docs/OWNER_DECISIONS.md` for the subject: only the row above.

## Established facts (verified on `834cdd175`; file:line)

### F1. Note 1

- `Sustain::in_fight` (`exec/sustain.rs:162`) counts an Elite goal on a camp as working it (a regional or elite in
  `FIGHT_U`). `fight` (`:325`) asks `elite_lost` (`:343`) first; that returns `None` unless the camp's elite mob is in
  sight (`elite_mob`), so once the elite is dead `fight` falls through to `death` (farm model) and `lost` is
  `death >= disengage_death - disengage_lead && hp < ready_hp` (LOCAL: 0.5 - 0.15 = 0.35, `ready_hp` 0.85).
- `propose` (`:482`): `lost` (when `now >= cornered_until`) pushes `Sustain/Camp(camp)` (`leave_candidate`, value
  `leave_value`), which outbids the Elite chest candidate and walks the seat away (`think`, `:381`).
- The chest phase is Elite's: `act` runs `chest_step` once `killed` has the camp (`elite.rs:824`), bounded by `chest_over`
  (`:393`: `chest_find_seconds` 3 s without a chest, `chest_wait_seconds` 16 s, picked, expired, asks). The pick rides
  along (`Elite::ride_along`, `:832`), from `pick_chest`, after the chest's birth hold (`CHEST_HUNT.hold_ms`).
  The chest is `Elite::camp_chest` (`:380`): of `facts.chests`, within `CAMP_JOIN_RADIUS` of the camp.
- **Measured on this base (oracle frame)**: Elite goal on camp 0, no elite mob, hp 0.4, no potion, chest at the camp:
  2 adds in reach give farm death chance 0.05 (not lost: the reviewer's "two adds" frame proposes no leave here); 3 adds
  0.36 (lost, leave proposed); 4 adds 0.78; 6 adds at hp 0.05 0.98. The oracle uses 3 adds.
- Kills now land at much lower hp (0.09 to 0.51 in 8 of 19) because Sustain no longer leaves fights the elite term rates
  as winning (P-RAID-E-leave).

### F2. Note 2

- `threat` (`exec/sustain.rs:79`): alive and not a `Dummy`; it includes `MobClass::Zombie`. `nearest` (`:233`), `in_fight`,
  `away` (`:192`) and `propose`'s heal-up condition read it. `think` runs the threat branch before the `healed` check.
- A Plague owner's zombie walks beside it (`crates/rh-sim/src/systems/plague_zombies.rs:59` `beside_owner`).
- **The owner is not in the frame.** `rh-sim` `ActorView.owner` exists (`observe.rs:155`, set `:1192`) but `WireActor`
  (`crates/rh-protocol/src/actor.rs:186`) has no owner field, and `MobSeen` (`percept.rs:84`) carries `faction`
  (`Option<FactionId>`), `class`, `camp`, `engaged`, no owner. So the exact fix (own zombie only) needs a wire change:
  not proposed. The faction rule is what the frame supports: a zombie whose `faction` equals `cx.p.me.faction` is no
  threat (this also ignores another Plague player's zombie: rare, accepted).

### F3. Note 3

- `Elite::ban` (`exec/elite.rs:157`) bumps the camp's `giveups` count and pushes the ban. Calls: `watch_goal` (`:254`,
  on a goal switch while `fight_since.is_some() && alive && not killed`, then clears `fight_since`, `:256`) and
  `fight_step` (`:608` time-out, `:613` never reached, `:625` walk failed, `:632` empty camp). `fight_step` does not
  clear `fight_since`; a time-out, a walk failure or an empty camp after a fight began (`fight_since` set) returns `Failed`
  and the arbiter's next frame switches the goal, so `watch_goal` bans again. The existing oracle calls `ban` directly.
- Measured: oracle test `a_fight_that_ran_out_of_time_is_one_give_up`: count 2 on the base.

### F3b. Note 4

- `fight_step` (`exec/elite.rs`, after `let started`, about `:605-616`): the `give_up_seconds` check sits inside `if fighting`;
  the else-arm (`never reached`) applies only while `fight_since.is_none()`. `fighting` is the elite within `FIGHT_U` or a
  member near. With the first blow landed (`fight_since` set) and the elite alive in sight but beyond `FIGHT_U`, neither
  bound runs: the step is `Running` (no path: no move intent; or walking), uncommitted, and the Elite candidate stays
  positive at full hp, so hysteresis keeps it. This breaks the file's header rule that every stand is bounded.
- Measured: oracle test `a_fight_under_way_with_the_elite_out_of_reach_times_out_once`: no `Failed` in 130 s on the base.
  The fix meets note 3: the time-out path clears `fight_since` (or `watch_goal` skips a camp banned), so one give-up.

### F4. Double-recorded kills (investigated, no fix)

- Bot side: `Elite::watch_elites` (`exec/elite.rs:224`) pushes `(camp, now)` into `killed` when `was.alive && !alive && mine
  && fight_since.is_some()`: `mine` is the seat's own held goal. Two seats that both hold the Elite goal on one camp both
  record a kill when its elite dies (seed 7 camp 46, seed 10 camp 48). That is each seat's **private memory** (`killed`
  is a field of one `Elite`); no shared counter exists. Its effect: the non-killer's chest phase (it walks to the chest and
  may ask for a card; the server decides). Not a bookkeeping double count.
- Lab side: `crates/rh-headless/src/balance_lab/observer.rs:273` opens `elite_open` per `(seat, elite mob)` on a hit;
  `mob_death` (`:375-402`) removes each open row and adds `killed` only `if by == Some(k.0)`, otherwise `taken_by_other`.
  One death event has one `by`, so one seat is credited: the lab cannot double count kills.

### F5. Minor list

- "Hurt and cornered" (`sustain.rs:443`) sets no `cornered_until` (the retreat's cornered branch does: `:~420`), so the leave
  can be adopted again and fail every tick (from P-RAID-HOLD-1). **Left out**: the fix is one line but pinning it needs a
  frame of hold, goal switch and re-adoption through the arbiter; not cheap. Listed for the coordinator.

## Existing tests that pin what changes

| Test | Asserts | Conflict |
|---|---|---|
| `exec/elite_leave_tests.rs` (13) | no heal-up beside a live elite; Elite-goal verdict by `elite_fight_lost`; ban table and `Elite::ban` direct growth; drink changes no price; no fall-latch during a leave | none: all keep a live elite or call `ban` directly |
| `tests/raid_elite_leave.rs`, `raid_elite.rs` | pure functions; executioner kill on 4 of 6 seeds, card within 400 ticks | none expected: the chest phase is more protected, not less |
| `tests/raid_hold.rs`, `raid_sustain*.rs`, `raid_bot.rs` | leave and stand on normal camps; `farm_only()` controls | none: no zombie, no Elite goal in them (implementer confirms by running them) |

No existing expectation conflicts with a fix (checked by reading; the implementer runs them).

## 4. New paths

`crates/rh-policy/src/bots/raid/exec/elite_leave_fix_tests.rs` (oracle; registered by a `mod leave_fix_tests;` line at the
end of `exec/elite.rs`, from the `stub:` commit).

## 5. Exclusive write scope

`exec/sustain.rs`, `exec/elite.rs`. `percept.rs` only if the coordinator answers yes to Q1 (not proposed). No `values.rs`
change unless D1 needs a field (it does not: it uses `disengage_death`).

## 6. Contracts and references

C-INPUT, C-OBS (faction is public on every actor), C-TICK, determinism (no draw in `observe`/`propose`; ordered iteration),
`crates/rh-policy/AGENTS.md`. Nothing reaches a live room.

## 7. Behaviour to preserve

No draw added or moved; registry order unchanged; `Prices::farm_only()` proposes nothing from Elite or Sustain; no state
outlives its goal (a new field is cleared when its goal ends); `stand_hp`'s life and the hold order in `think`
(P-RAID-HOLD-1) stay; `raid_seats` replays exactly.

## Design decisions

- **D1 (note 1): the exemption and its bound.** In `elite_lost`, when the goal is `Elite/Camp(c)`, no live elite of the
  camp's kind is in sight and a chest stands within `CAMP_JOIN_RADIUS` of the camp (`Elite::camp_chest`'s test, a pure
  read of `facts.chests`), the verdict is lost only when the farm model's death chance is at least `disengage_death` (the
  full line, 0.5, not `disengage_death - disengage_lead`, 0.35) and `hp < ready_hp`. The reviewer's smallest fix returns
  `Some(false)` always; that leaves a seat at hp 0.05 under six adds to die at the chest, so the bound is the existing full
  line: Sustain's own rule ends the chest phase when death is likely, with no new price. Drinking still rides along. The
  oracle pins both sides: 3 adds at hp 0.4 (0.36) is not led off; 4 adds at hp 0.4 (0.78) and 6 adds at hp 0.05 are. The
  implementer may use a simpler "always `Some(false)`" only if the coordinator says so (Q2); then
  `four_adds...` and `adds_that_will_kill...` go red and are amended by the owner.
- **D2 (note 2): faction rule.** `threat` takes the seat's faction: a `MobClass::Zombie` whose `faction` is
  `cx.p.me.faction` is not a threat (in `nearest`, `away`, and every reader). Another faction's zombie stays one.
- **D4 (note 4): bound by `fight_since`.** Move the `give_up_seconds` check out of `if fighting`: when `fight_since` is
  `Some(since)` and `now - since > give_up_seconds`, ban and fail; `fight_since` is cleared with it (D3).
- **D3 (note 3): clear `fight_since` beside each `ban` in `fight_step`** (or skip a camp already banned in `watch_goal`);
  the count after one time-out and a goal switch is 1 and the first ban is `ban_seconds`.

## 8. Acceptance checks

- `scripts/gate.sh --quick --base coord/int-86 --oracle packet/P-RAID-E-leave-fix` passes;
  `cargo test --locked -p rh-headless --test raid_seats` passes.
- `cargo test --locked -p rh-policy --lib leave_fix` green; `--lib elite_leave` and `--test raid_elite_leave --test
  raid_elite --test raid_hold --test raid_sustain --test raid_sustain_fix --test raid_bot` stay green.
- Lab, seeds 1 to 12, 6000 ticks, 8 raid seats, base against head: kills recorded by a seat and how many have a pick by
  that seat (base: 19 and 16), deaths, hand-aways, and for any Plague seat the heal-ups proposed (base: none while its
  zombie lives). Kills with a pick not down; deaths not up beyond noise.

## 9. Validation commands

`export PATH="$HOME/.cargo/bin:$PATH"`; `cargo test --locked -p rh-policy --lib leave_fix`; lab: `cargo build --release
--locked -p rh-headless --bin balance_lab`; `SEATS=(); for i in 0 1 2 3 4 5 6 7; do SEATS+=(--seat "$i=raid"); done;
./target/release/balance_lab --human-seats --seeds 1..13 --max-ticks 6000 "${SEATS[@]}"`; `python3
scripts/readability_check.py`.

## 10. Risk and suggested model tier

implementer-opus, reviewer-opus. Risks: D1 bound changes which seats leave after a kill (deaths at the chest); D2 also
ignores another Plague player's zombie; seed 1 p2 camp 27 (kill, no chest within 15 s) is a different cause, not fixed here.

## 11. Known traps

- The chest exemption must test the chest by the same radius Elite uses; `facts.chests` holds every delivered chest.
- `elite_lost` is called by `fight`, which `urgent` also reads: the exemption changes the urgent edge too.
- Clearing `fight_since` in `fight_step` must not change `started`/`empty_since` logic or `track` (it reads `fight_since`).

## Oracle files (read-only for implementers)

- `crates/rh-policy/src/bots/raid/exec/elite_leave_fix_tests.rs` (11 tests: 5 red on the base by assertion, 6 green guards)
- `docs/backlog/packets/P-RAID-E-leave-fix.md` (this brief)

The `stub:` commit holds the `mod leave_fix_tests;` line in `exec/elite.rs` (not an oracle file). No `oracle-amend:`.

Routing: serves: the P-RAID-E-leave follow-up · tier: implementer-opus · reviewer: reviewer-opus · D-rules: D1 loose proof,
no D5/D6/D9 · write scope: section 5.

Red on the base: `a_seat_that_killed_its_elite_is_not_led_off_its_chest_by_the_leave` (proposes `Sustain/Camp(0)`),
`a_plague_seat_beside_its_own_zombie_is_offered_the_heal_up` (no candidate), `a_leave_beside_the_seats_own_zombie_stands_to_heal`
(moves "leave a lost fight"), `a_fight_that_ran_out_of_time_is_one_give_up` (count 2), `a_fight_under_way_with_the_elite_out_of_reach_times_out_once` (no
time-out in 130 s). Green guards:
`without_a_chest_at_the_camp_the_leave_of_a_lost_fight_stands`, `the_chest_exemption_is_for_the_elite_goal_only`,
`adds_that_will_kill_the_seat_still_pull_it_off_the_chest`, `four_adds_at_hp_0_4_still_pull_the_seat_off_the_chest`,
`another_factions_zombie_in_reach_is_still_a_threat`, `a_fight_handed_away_is_one_give_up`.

## NOT ESTABLISHED

- That the chest pick then fires: the oracle pins only that no leave is proposed; the lab count carries the proof.
- The cause of seed 1 p2 camp 27 (no chest found within 15 s).
- That the D1 bound (0.5) is the right line; untuned.
- A zombie of another Plague player is ignored too (faction rule).
- "Hurt and cornered" re-adoption (F5) is left out.
- Left out, facts from the integration 86 review, no change here: `Sustain::elite_lost` looks the elite up with no distance
  filter while `elite.rs` uses its `watch.at` filter (they agree except when two camps hold the same elite kind); an
  Elite-to-Elite goal switch clears `fight_since` in `start`, so the camp left is not banned; Elite and Farm do not check
  `gates_locked` (under sudden death, camps outside are proposed, fail their walk at once and are banned).

## Decisions for the coordinator (default in force)

- **Q1: owner field.** The owner is not on the wire, so the exact fix is a protocol change. Default: faction rule only, no
  percept or wire change.
- **Q2: D1 bound.** Default: full `disengage_death` line during the chest phase. Alternative: always `Some(false)`.
