# P-RAID-Q3b: RaidBot quest decisions, part 2 (Two Banners, Trade Up) (BRIEF ONLY: no stub, no oracle yet)

- **Serves:** not in the plan: owner approval 2026-11-22 (`docs/backlog/BALANCE_LAB_PATCH.md` section 1, P-RAID-Q3's second half)
- **Implementer tier:** implementer-sonnet (Opus if the Trade Up risk model touches `farm::death_chance` shared state)
- **Reviewer:** reviewer
- **Integration base:** after `P-RAID-Q3` lands (it shares `quest/decide.rs`, the executor and the seam `Act::QuestPickup`'s file
  set); until then `packet/P-RAID-Q3`
- **D-rules touched:** D1 loose proof · D2 n/a · D5/D6/D9 none

## 1. ID and outcome

`RaidBot` chooses a branch of **Two Banners** at the giver and carries or delivers as the branch requires, and plays **Trade Up**:
barters with three wandering traders in turn and cashes out at any step, going on while the timer and the risk allow. Both are
`GoalKind::QuestDecide` in `quest/decide.rs` (extending the Q3 executor).

## 2. Why a separate packet

Q3's three decisions are pure rules plus one scene each. These two are routes: Two Banners is a carry of about 3 300-3 900 u to a
rival outpost that stands on a camp (death risk); Trade Up is three legs of 2 000-4 000 u between zones against a 95 s timer with
moving targets. Their oracle needs a new scene kind and its floors need measuring on an executor, so they follow Q3.

## 3. Facts (verified in code on `coord/int-84`; paths in `crates/rh-sim/src`)

**Two Banners** (`two_banners`, Gallows giver, reward `Warhorn` single-use spell, no route timer, clock Race, `catalog.rs:218-228`):
- Choose: at the giver within 64 u (`attempts/choices.rs:83-86`) the frame offers action 2 target 0 ("Carry the Gallows banner")
  and target 1 ("Carry the rival banner"); `interactions.rs:97-103` stores the branch; branch 0 sets `carrying` at once, branch 1
  does not. Before the choice the view shows no objective (`interactions.rs` `objective_for_owner`: `TwoBanners` with no branch is
  `None`).
- Branch 0: objective and end point = the rival outpost (`attempts.rs` `endpoint`: `TwoBanners if branch == Some(0)` =
  `tokens[0]`); `tick` (`interactions.rs`, `SmugglersRun | TwoBanners if carrying && at_end`) achieves it on arrival; turn-in
  at the outpost is not at the giver, so a scared-off giver does not block it (`ends_at_giver`).
- Branch 1: walk to the outpost, action 3 target 0 within 64 u ("Take the rival banner", `choices.rs:55`, `interactions.rs:105-111`
  sets `carrying`), then the objective is the giver (`objective_for_owner`: `branch == Some(1) && carrying -> o.at`) and
  standing there turns it in.
- The outpost is `placement.rs:558` `self.camp(self.distant(zone)?, false)`: a normal camp of the zone farthest from the Gallows
  pole. Measured: seeds 1, 2, 6 the objective is (592, 3568), seed 9 (1968, 4720); the Gallows giver is (4208, 2096) (seeds 1, 2, 6,
  9, 12, 20, 24) or (4176, 2000) (10, 16 and others). About 3 900 u and 3 450 u straight. Deals: seeds 1, 2, 6, 9, 10, 12, 16, 20,
  21, 24.
- To establish before the oracle: what the Race clock does to a second seat that also holds the quest (`quests/live.rs`,
  `done_by`), and whether the camp at the outpost makes branch 0 or 1 the safer walk (the carry is public? `public_markers`
  covers only the crown, smuggler, fort, meditation and bounty: not the banners).
- Priced at `reward_value("two_banners")` = 3.0; death term from the camp at the outpost (as the walker's `let_him_cook` term).

**Trade Up** (`trade_up`, Sanctum (Holy) giver, reward `TradeUp` permanent, timer `max(95 s, 1.5 x route / 170 + 15 s)`, measured
`expires_at` 95 150 ms after the accept, `catalog.rs:466-475`):
- Three traders: tokens at a Fire, a Plague and a Frost contact (`placement.rs:598-602`), each walking a 72 u patrol with a 12 s
  period, phases 3 s apart (`interactions.rs` `walk_traders`). The view's objective is the **next** trader's current position
  (`objective_for_owner`: `TradeUp if index == a.progress`). Measured first trader (973, 1744) on seeds 7, 8, 11, (973, 1712) on 12;
  giver (2832, 1232) or (2864, 1136): about 1 900 u. Deals: seeds 7, 8, 11, 12, 16, 18, 19, 23.
- Barter: action 3 with `target == progress` within 64 u of that trader (`choices.rs:65`, `interactions.rs:191-198`): progress + 1; at 3
  the quest is achieved with points 3. Cash out: action 4 (target 0) whenever `progress > 0` and the seat is within 64 u of the
  last trader (`choices.rs:91-93`, `interactions.rs:199-212`): points = progress, achieved.
- Turn-in: at the last trader (`endpoint` for `TradeUp` = `trader_at(progress - 1)`), standing there sets `pending`; the reward is
  `trade_up_stacks[points - 1]` = [12, 30, 54] stacks of the Penitent mob (max HP family) (`rewards/permanent.rs:176-185`,
  `tuning.rs:137`); the award `TradeUp` is permanent and paid once per run (`inventory::has`).
- Expiry at the deadline abandons the attempt and drops its progress (`attempts.rs:470-474`): cash out before the timer would be
  lost, and only the next leg's walk (to the trader's position now) is knowable.
- Priced at `reward_value("trade_up")` = 5.6 (the table's highest quest row); stages 12 / 30 / 54 stacks are 1 / 2.5 / 4.5
  camp-equivalents at `stacks_per_camp` 12: the executor goes on while the extra stacks, times the chance to reach the next
  trader before the timer, outweigh the death risk and the extra minutes.

## 4. Oracle (proposed; the author of Q3b writes it)

`tests/raid_decide_routes.rs` (or the Q3 file extended): a scripted control per quest on 3 seeds (accept, branch/barter, cash out or
deliver; the detector sees the row leave and `Penitent` stacks 12/30/54, or `Warhorn` held), then a `RaidBot` scene per quest with a
floor measured on a reference executor; Trade Up asserts it cashes out before the timer on every seed it starts and never loses a
barter progress to the timer; Two Banners asserts the branch chosen is the lower-death one by the executor's own death term.
Unit tests of the cash-out rule (stage, time left, walk to the next trader, stacks 12/30/54).

## 5. Exclusive write scope (proposed)

`crates/rh-policy/src/bots/raid/quest/decide.rs` (one more stage group) and `quest/mod.rs` for the oracle's test module line. No
registry line: `Decide` is already registered by Q3.

## Notes from previous attempts
<!-- Coordinator appends on escalation. -->
