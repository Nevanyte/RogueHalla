# P-RAID-Q3: RaidBot quest decisions (reward slot choice, dropped-spell pick-up, The Bargain)

- **Serves:** not in the plan: owner approval 2026-11-22 (`docs/OWNER_DECISIONS.md` row "Balance patch, test bot, per-elite
  numbers"; plan of record `docs/backlog/BALANCE_LAB_PATCH.md` section 1, packet P-RAID-Q3, "decisions (banners, Bargain,
  Trade Up, slot replacement)")
- **Implementer tier:** implementer-sonnet
- **Reviewer:** reviewer
- **Integration base:** `coord/int-84` @ `52ec36ccd` (the gate takes `--base coord/int-84` until it is merged)
- **D-rules touched:** D1 loose proof (owner direction 2026-11-10; RaidBot has no legacy counterpart, the oracle is a behaviour
  test on the live content world) · D2 n/a · D5/D6/D9 none (no sim, wire or observation change; the bot acts only through
  `validate` + `Sim::submit`)

## 1. ID and outcome

`RaidBot` makes the quest decisions the walker (P-RAID-Q1) leaves alone, as `GoalKind::QuestDecide` in
`quest/decide.rs`: it answers a **pending reward slot choice** (a third spell reward) keeping the two spells of higher table
value, **picks a dropped quest spell up** when a slot is free, and trades at **The Bargain** (20 stacks of one stat for 24 of
another, or the spare weapon for a stat) when a trade improves its build, never otherwise. **Split (see "Size").** Two Banners
and Trade Up are `P-RAID-Q3b` (`docs/backlog/packets/P-RAID-Q3b.md`, brief only). When this lands, a seat that has earned a third
spell answers within a few seconds instead of leaving the choice hanging, and the walker's freeze on a dropped spell is gone.

## 2. Dependencies

`P-RAID-Q1` (walker) and `P-RAID-V` (`Prices::reward_value`), both on the base. Nothing in flight is needed (no trusted quest
reward grant).

## 3. Relevant code (no legacy: the behaviour source is the Rust board; all paths verified on the base)

Everything is in `crates/rh-sim/src` unless noted.

- `quest_view/board.rs:45-73`: a pending attempt shows its handle in `pending`; when the seat is at its turn-in point
  (`attempts/choices.rs:244` `at_turn_in`) the frame adds action 8 per slot: `target` 0 and 1 (label "Drop <spell>", or
  "Equip in slot N" for an empty slot) and `target` 2 ("Leave the new reward on the ground"). `:157-186`: a drop is listed
  in `drops` (nearest first, 48 at most, only those the seat can see) and offered as action 9 (`target` = the drop's `index`,
  `quest` = a drop handle that is not a row) **only when fewer than two spells are held** (`:159` skips it at two) and the seat
  stands within pick-up reach.
- `quests/board/rewards/inventory.rs:198-250` (`pay_claim`): at turn-in a spell reward with a free slot is granted and the
  attempt finishes; `lost_brother` pays two spells (`awards.rs` `bundle`: Blind Rage and Still Mind): with one free slot the
  first is granted and the second is **dropped at the seat's feet** (`:230-236` `spill_draught`); with none free (or a
  reward with no free slot at all) the offer is pushed to `pending_choices` and waits (`:238-243`).
  `:263-308` (`resolve_choice`): needs `at_turn_in`, an offer in `pending_choices` and `target <= 2`; target 0/1 drops the old
  spell at the feet as loot and equips the new one in that slot (the rest of a bundle falls as loot too); target 2 leaves every
  award as loot. `:310-335` (`pickup`): refused without a free slot, a standing seat in reach, or for a bot seat.
- **How long a choice may stay outstanding: for ever.** Turn-in clears the deadline (`attempts/interactions.rs:319-331`
  `next.deadline = 0.`), nothing expires a pending attempt, and it survives death (`attempts/signals.rs:300-306`: the death
  retain keeps `pending.is_some()` attempts) and the coliseum lock (`:185-196` keeps pending ones). While `gates_locked` or the
  seat is blocked or away from the turn-in point, action 8 is not offered and the choice just waits. **A reconnect** changes
  nothing: the pending offers are sim state; only the frame handles are new (key by def).
- **What the seat holds / the wire ids.** `QuestView.spells[]` `{slot, reward, charges, ready_at}`, `drops[]`
  `{index, reward, charges, ready_at, at}` (`quest_view/board.rs:122-134,168-185`), `reward = Award as u8`
  (`rewards/awards.rs:8-35`). `Award` and `RewardId` (`quests/board/catalog.rs:100-125`) agree on 0..=21; **22 is Still Mind,
  which has no `RewardId`** (lost_brother's second spell; `RewardId` 22 is `FeedOrHuntDamage`, a permanent reward). So a spell's
  reward id maps to its quest by `DEFINITIONS.iter().find(|d| d.reward as u8 == id && d.reward.kind() != Permanent)`, with 21
  and 22 both mapping to `lost_brother` (each standing for half its row). Reusable (`RewardKind::Reusable`): Hook, Shoulder
  Charge, Tornado, Turret, Smite, Spell Shield, Lantern Ward, Cleanse, Scout Falcon, Bolas; a single-use spell vanishes when
  cast (`rewards/casts.rs:105-113`: cooldown 0 empties the slot), a reusable one keeps `charges`/`ready_at` and is recharging at
  0 charges. **So a held single-use spell is never "spent"** (see "What the outline got wrong").
- **The Bargain** (`the_bargain`, Warren giver, reward `TheBargain` permanent, no timer, clock Decision;
  `catalog.rs:323-332`): accept at the giver; one token = the hag (`placement.rs:584` `contact(zone)`, 136 u from the giver on
  seed 2, measured); the view's objective is the hag. Within `contact_range` 64 u with a line (`attempts/choices.rs:94-123`)
  the frame offers, **only legal trades**: action 5 for every give stat with at least `bargain_cost` = 20 stacks
  (`tuning.rs:123`) to every other stat, `target = give * 9 + receive` (`signals.rs:358-369` the nine stats: 0 damage, 1 move
  speed, 2 max HP, 3 ability power, 4 armor, 5 shield, 6 attack damage, 7 attack speed, 8 lifesteal; a stat's stacks are the sum
  of the mobs that pay it, `signals.rs:388-402`), and action 7 per stat if the seat has a spare weapon (`interactions.rs:
  has_spare_weapon`: the other weapon slot is filled and the gear is not locked), `target = receive`. Sending one
  (`interactions.rs:213-247`) records the pending reward and achieves the quest; at the hag the tick sets `pending`, and
  `rewards/permanent.rs:130-158` (`bargain`) takes the 20 stacks (from the family in mob order) or the spare weapon and grants
  `bargain_stacks` = 24 (`tuning.rs:138`) of the received stat. **If the seat does nothing** the attempt stays accepted and
  open for ever (no deadline); if the payment fails (`stack_room`, locked gear) `reopen_bargain` (`attempts.rs:274-285`) reopens it.
  The seat can abandon (action 1) at any time; `Prices` has no row for the trade. Frame offers the one stage; the executor
  never guesses a target.
- `quests/live.rs:608-650` (`accept_eligibility`), `attempts.rs:287-312` (`accept`).
- rh-policy: `bots/raid/quest/walker.rs` (the walker: `drop_near` `:116-123` makes it propose and act on nothing while any drop
  lies within 160 u; `slots_taken` `:125-138`; `slot_choice` `:106-114`), `bots/raid/refs.rs:92-111` (`Refs::act` resolves a
  quest act by **def**, so it cannot name a drop: see "The seam"), `bots/raid/values.rs` (`reward_value`, `stacks_per_camp`),
  `bots/raid/percept.rs` (`Me`, `summary`: `WireBuildSummary.mob_rows` = `{mob_type, stat (HUD code), stacks, total}`),
  `bots/temporal_percept/modules/choice.rs:66-160,266-282` (the chest scoring's fit, private to that module).

## 4. Facts the brief establishes (measured on the base; the throwaway probe is not committed)

1. **A pending slot choice cannot be reached by ordinary RaidBot play.** The walker accepts a spell-reward quest only while
   `slots_taken` (held spells plus accepted unfinished spell quests) is under two (`walker.rs:250`), so it never earns a third
   (the Q1 implementer saw every seed end holding two spells). The cheapest other way is a **scripted prelude**: the control of
   `raid_quests.rs` (accept at the giver, the offered contact, a straight `Move` to the nearest objective: the server paths
   it) extended to a list of quests, run on the sim until the third quest stops pending with action 8 offered, then a `RaidBot`
   seat takes over the same sim. Measured (debug, no policy): seed 5 `barrow_run`, `a_cruel_joke`, `ember_walk` -> pending at
   tick 1260; seed 4 `pilgrims_path`, `letters`, `lost_brother` -> pending at tick 1775 (labels "Drop Cleanse", "Drop
   Forget-Me-Not Seed", "Leave the new reward on the ground"). Walker spell-reward quests dealt (seeds 1-6): 1: ember_walk,
   barrow_run, lost_brother, letters; 2: ember_walk, watchtowers, pilgrims_path; 4: smugglers_run, a_cruel_joke, lost_brother,
   pilgrims_path, letters; 5: smugglers_run, ember_walk, barrow_run, a_cruel_joke, letters; 6: smugglers_run, ember_walk,
   watchtowers, pilgrims_path.
2. **Deals** (`seed1.map.json`, seeds 1-24, a human seat at the board): `the_bargain` on seeds 2, 4, 6, 9, 10, 11, 12, 13, 17,
   20, 22, always at Warren, giver (1360, 3952) or (1360, 4048); the hag is a contact in the Warren zone (136 u from the giver on
   seed 2). `two_banners` on 1, 2, 6, 9, 10, 12, 16, 20, 21, 24; `trade_up` on 7, 8, 11, 12, 16, 18, 19, 23 (Q3b's).
3. **Getting 20 stacks and a spare weapon.** A spare weapon is just `Placement::weapon1: Some(..)` (`rh-sim` placement; the
   gear is not locked). Stacks: `rh_sim::trusted_build::apply(&mut sim, id, &TrustedBuild { stacks: vec![(MobTypeId, u64)],
   .. })` between steps, under the `trusted-build-setup` feature. rh-policy's tests did not enable it; **the stub commit adds it as a dev-dependency**
   (`crates/rh-policy/Cargo.toml`: `rh-sim = { path, features = ["trusted-build-setup"] }` under `[dev-dependencies]`; no
   `Cargo.lock` change; `rh-sim/tests/trusted_build_feature_guard.rs` allows dev-dependencies). 20 `EmberImp` stacks are 20 damage
   stacks (damage family: `EmberImp`, `Inquisitor`); the Bargain's result is read from `Player::farm.count(MobTypeId::Pardoner)`
   (attack damage) and `Player::weapon.weapon1`.
4. **Scripted controls (all green today, run in the oracle file).** `a_scripted_prelude_reaches_the_slot_choice_and_a_scripted_
   answer_resolves_it` (both slot seeds: two spells held, three answers offered, the table answer leaves the expected held and
   ground sets); `a_scripted_pickup_takes_the_dropped_spell_when_a_slot_is_free` (seed 4: lost_brother's two spells, pilgrims_path's
   Cleanse left on the ground, Blind Rage cast with `Cast { slot: Q }` so a slot is free, then action 9 picks Cleanse up);
   `a_scripted_bargain_trades_stacks_or_the_spare_weapon_on_every_seed` (seeds 2, 4, 6, 9: accept, walk, send `(5, 6)` or
   `(7, 6)`, the row leaves the view, 24 `Pardoner` stacks).
5. **Debug-build times:** cold build of `rh-policy` tests about 1 min; `cargo test -p rh-policy --test raid_decide` about 14 s
   on the stub (7 s with a working executor); the decision-rule unit tests under 1 s.
6. **A reference solution exists.** The brief's author wrote a throwaway executor and rules to prove the oracle passable (not
   committed; it is short): with it every test of both oracle files passes, and the Bargain scenes complete on 4 of 4 seeds
   (floor 3), the slot scenes answer within a few ticks and the bot accepts a quest after answering on 2 of 2 seeds.

## What the outline got wrong (code contradicts it)

- **"Pick up a drop worth more than a held spell" cannot happen.** The server offers action 9 only with a free slot
  (`quest_view/board.rs:159`) and `pickup` refuses without one (`inventory.rs:310-335`); there is no swap. The only way to take a
  better spell at full slots is the pending-choice answer. The pick-up rule is: **a slot is free and the drop is worth more
  than nothing**.
- **There is no "spent single-use spell".** A single-use spell vanishes when cast. The rule that does exist: a reusable spell
  at 0 charges is recharging and counts half; between equal worths the single-use spell is dropped before the reusable one.
- **A pending slot choice is not reachable by RaidBot alone** (fact 1): the oracle scripts the prelude.
- **The seam lacks a drop pick-up act.** `Act::QuestInteract { def, .. }` resolves by quest row, and a drop has no row, so
  action 9 could not be sent. The stub commit adds `Act::QuestPickup { index, reward }` in `acts.rs` and its resolution in
  `refs.rs` (a drop is named by its frame `index` and wire `reward`; both must match the same frame), with a test in the
  oracle. Both files are outside the outline's write scope: they are in the stub commit (edit-free for the implementer).
- **No stat-fit function is reachable from RaidBot.** `choice.rs`'s `base_fit`/`card_score` live in a private module of
  `temporal_percept`. The rule below copies its fit numbers locally (`stat_worth`), and its two prices (the neutral stat and the
  spare weapon) are local constants in `decide.rs` because `Prices` (`values.rs`) is outside the write scope.
- **The walker's freeze needs one line.** `drop_near` stops the walker while any drop lies within 160 u, even with two
  spells held when the drop cannot be picked up. After a slot answer the seat stands on a drop it cannot take, within 160 u of
  the givers nearby. `walker.rs` `drop_near` (`:116-123`) is added to the write scope: one condition,
  `q.spells.len() < SPELL_SLOTS &&`. The oracle pins it by a unit test. (Measured: with a reference executor the scene passes even
  without the line, because the bot walks off and the walker comes back; the unit test is the pin.)

## 5. New paths

- `crates/rh-policy/tests/raid_decide.rs`, `crates/rh-policy/src/bots/raid/quest/decide_tests.rs` (oracle).

## 6. Exclusive write scope

- `crates/rh-policy/src/bots/raid/quest/decide.rs` (the whole executor: `Decide`, `propose`, `act`, the pure rules).
- `crates/rh-policy/src/bots/raid/quest/walker.rs`: **only `drop_near`** (the one condition above).
- Nothing else. Not `exec/mod.rs` (the registry line, after the existing ones, is already in the stub), not `acts.rs`/`refs.rs`
  (the seam is in the stub), not `Cargo.toml`, `values.rs`, `bot.rs`, `arbiter.rs`, `goal.rs`, `percept.rs`. If a rule cannot be
  written without one of those, stop and say exactly what is missing.

## 7. Contracts and references

C-INPUT, C-OBS, C-POLICY; `crates/rh-policy/AGENTS.md`; `docs/agent/READABILITY.md` (`//!` header, 1 500 lines); `rh-policy`
`clippy.toml` (no hash collections, no clock, no platform libm: `rh_sim::math`); the extension guide in
`crates/rh-policy/src/bots/raid/mod.rs`.

## 8. The rules (what the oracle pins; the executor around them is the implementer's)

`decide.rs` exposes these pure functions (signatures are in the stub; the unit tests call them):

- `def_of_reward(reward) -> Option<(def, share)>`: the quest that pays a spell: first non-permanent `DEFINITIONS` entry whose
  `reward as u8 == id`; 21 and 22 both `("lost_brother", 0.5)`; permanent ids and 255 `None`.
- `spell_worth(prices, reward, charges)`: `prices.reward_value(def) * share`, halved for a reusable spell at 0 charges. Zero for
  an unknown id and under `Prices::farm_only()`.
- `slot_answer(prices, held, new_def) -> u8`: an empty slot (no held spell with that `slot`) is filled first (answer = that
  slot, "Equip in slot N"); else the new reward's worth is `reward_value(new_def)` (halved for `lost_brother`); it **replaces
  the held spell of lowest worth only if strictly greater** (answer = that spell's slot; of two equal lowest worths the
  single-use spell goes, then the lower slot), otherwise 2 (leave it). A tie therefore leaves the new reward.
- `pickup_wanted(prices, held, drop)`: `held.len() < 2` and the drop's worth above zero.
- `stat_worth(stat, me)`: 2.5 plus the legacy chest fit (`choice.rs` `base_fit`) for the weapon in hand (`me.weapons[me.slot]`,
  fists when empty): attack damage (6) +1 for sword, axe or bow, +0.5 fists, -0.5 wand, else -1; ability power (3) +1 wand, +0.5
  fists, 0 for a Fire, Sentenced or Plague fighter, else -1; armor (4) and shield (5) +1 for a shield weapon; the others 0.
- `bargain_trade(prices, me, offered) -> Option<(action, target)>`: for each offered trade the gain in camp-equivalents, with
  `unit = prices.stacks_per_camp * 2.5` (12 stacks of a neutral stat = 1): a stack trade `(24 * worth(receive) - 20 *
  worth(give)) / unit`; a weapon trade `24 * worth(receive) / unit - 1.5` (the spare weapon is worth 1.5 camp-equivalents:
  **a placeholder, no table row; flagged**). Take the largest gain that is above zero; ties: the weapon trade, then the lower
  give, then the lower receive; none above zero: `None`. (24 stacks for 20 means an equal-worth swap is a small gain; the
  numbers 24 and 20 are `TOP25.bargain_stacks` and `bargain_cost`, crate-private to `rh-sim`, so they are constants in
  `decide.rs`.)

The executor (kind `QuestDecide`, target `Target::Quest { def, stage }`, the def naming the quest or the drop's quest, stages
the implementer's own): (a) a row in `pending` is a candidate (value = `cx.prices.reward_value(def)`, minutes tiny so it ranks
first); at the point where the frame offers action 8 it sends `Act::QuestInteract { def, 8, slot_answer(..) }`; if action 8 is
not offered (the seat moved, died and respawned) it walks to the quest's turn-in point (the giver; for `tuber_eats`,
`even_skeletons_get_cold`, `letters` the token, which the view no longer shows once pending: remember the last objective seen
while `owner_ready`); (b) a visible drop with `pickup_wanted` is a candidate valued `spell_worth`, it walks to it and sends
`Act::QuestPickup`; (c) `the_bargain`: a candidate only while a trade can improve (stat stacks from `cx.p.summary` mob rows grouped
by their HUD stat code, 0..9 = Offense, Soul offense, Damage, Attack speed, Life steal, Health, Defense, Soul defense, Move
speed, which for the Bargain's stats are 2, 8, 5, 1, 6, 7, 0, 3, 4; or a spare weapon in `me.weapons`), accept at the giver, walk
to the hag, and when the frame offers trades send `bargain_trade(..)`'s pair (copied from the frame, never guessed); with no
improving trade it sends nothing and gives the quest up for a while. Throttle resends (about 2.5 s) as the walker does. Draw
nothing from `cx.rng`. It shares the slot notion with the walker by reading the same view (`q.spells.len()`), no helper needed.

## 9. Acceptance checks

- `scripts/gate.sh --quick --oracle packet/P-RAID-Q3 --base coord/int-84` passes.
- `cargo test -p rh-policy --test raid_decide` and `cargo test -p rh-policy --lib decide_tests` pass (not release-only).
- Stay green: `crates/rh-policy/tests/raid_bot.rs`, `raid_quests.rs`, `raid/fix_tests.rs`, `review_tests.rs`, `values_tests.rs`,
  `quest/walker_tests.rs`.

## Existing tests that pin what this packet touches (none is amended)

- `tests/raid_bot.rs` (`Prices::farm_only()`: the quest family is off, so a registered `Decide` proposes nothing there).
- `tests/raid_quests.rs`: the walker scenes; the walker's `drop_near` change does not touch a scene without a dropped spell.
- `quest/walker_tests.rs`: no test of `drop_near` or a drop (grepped); the slot accounting tests are untouched.
- `values_tests.rs`, `raid/fix_tests.rs`, `review_tests.rs`: no reference to `QuestDecide` or `decide`.
- Registry order: the stub puts `Decide` after `Roam`; adding an executor that proposes nothing draws nothing.

## Oracle files (read-only for implementers)

- `crates/rh-policy/tests/raid_decide.rs`
- `crates/rh-policy/src/bots/raid/quest/decide_tests.rs`
- `docs/backlog/packets/P-RAID-Q3.md`

Green on the stub **by design** (guards, not defects): the three scripted controls, `raid_bot_makes_no_bargain_that_is_not_an_
improvement`, and in `decide_tests` the "worth nothing/full slots/no improving trade" cases, `with_a_slot_free_a_dropped_spell_
at_the_feet_still_holds_the_walker_back` and the pick-up act test (the seam is in the stub). Red on the stub, each on an
assertion: the rest (14 of 20 unit tests, 6 of 10 scene tests).

## 10. Risk and suggested model tier

Sonnet: one executor in one file with pure rules and a reference of about 250 lines; the hard parts are the frame-local
handles (key by def and index), the two stats mappings (Bargain index to HUD code, reward id to def), and not trading when
nothing improves. Floors that are **not measured on an implementation** (the reference measured 4 of 4 and 2 of 2): the Bargain
scenes need 3 of 4 seeds; after answering the bot must accept some quest on 1 of 2 seeds. The coordinator may tighten them.

## 11. Known traps

- A frame with `quests == None`: propose nothing. Quest handles and drop handles change every frame: resolve by def, and by
  drop `index` plus `reward`.
- Action 8 shows only at the turn-in point; action 9 only with a free slot and reach; a drop list re-sorts by distance.
- `Pardoner` stacks can rise by 3 from a kill on the way (the oracle uses `>= 24`).
- Never abandon the Bargain; an accepted one with no improving trade stays open with no timer (it takes no spell slot).
- A permanent reward quest (`the_bargain`, `trade_up`) never uses a spell slot.

## Size

About 300 changed lines in one area. The outline allowed a split: **this packet is the slot choice, the pick-up and The Bargain**
(each has a scripted control and a reference); `P-RAID-Q3b` (Two Banners and Trade Up) is a separate packet because it needs a new
oracle scene kind (a four-thousand-unit carry across a rival camp, three moving traders on a 95 s timer) and its own risk (death
at the rival camp); it can start after Q3 lands and shares only the registry line.

## Notes from previous attempts
<!-- Coordinator appends on escalation: tier, branch, the gate's failure block. Nothing else. -->
