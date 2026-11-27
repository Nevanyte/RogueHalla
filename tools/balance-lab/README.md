# balance-lab

Plays seeded 8-seat matches under `SimConfig::live_content()` and reports the rates the quest and augment prices depend
on: camps, deaths, elite fights, the quest funnel, chest offers and picks, augment holders. Code:
`crates/rh-headless/src/balance_lab.rs` (options, run, report), `balance_lab/observer.rs` (the read-only observer),
`balance_lab/acc.rs` (counters), `balance_lab/table.rs` (stderr table).

```sh
cargo run --release -p rh-headless --bin balance_lab -- --seeds 1..9 --jobs 4 --human-seats > report.json 2> table.txt
cargo test -p rh-headless --test balance_lab
```

Every seat playing the RaidBot policy (what the quest numbers below were measured with):

```sh
cargo run --release -p rh-headless --bin balance_lab -- --human-seats --seeds 1..3 --max-ticks 2400 \
  --seat 0=raid --seat 1=raid --seat 2=raid --seat 3=raid --seat 4=raid --seat 5=raid --seat 6=raid --seat 7=raid
```

## Flags

`--seeds A..B` (half-open, default `1..2`), `--jobs N` (speed only: the bytes do not depend on it), `--max-ticks N`
(default 24000), `--map generate|PATH` (default: one generated map per seed), `--seat I=human|brawler|raid` (repeatable;
unlisted seats play `human`), `--human-seats` (seat everyone `bot:false`). A usage error exits 2.

## Reading the report

One compact JSON object on stdout, schema `balance-lab/1`, keys in declaration order: `schema`, `content_hash`, `seeds`,
`seating`, `maps` (the map the sim admitted), `world`, `elites`, `quests`, `rewards`, `augments`, `seats`, `notes`. Counters are
sums over seeds, folded in seed order. Sums of seconds carry `n`, the sample count; minute samples (`world.minutes`, at
minutes 5, 8, 12 of the sim clock) carry `samples`. Matches of eight human-bot seats end after about 900 to 3,400 ticks,
so later minutes have few samples or none.

## Where each seat's time goes (`world.time`, `seats`)

Every seat-step (a seat's state after one match step; the lobby counts; alive or not) lands in exactly one bucket, the
first that matches:

| bucket | meaning |
|---|---|
| `dead` | the seat is not standing (`Player::standing()`); its fight windows are cleared |
| `elite`, `camp`, `player` | the seat dealt or took a hit in the last 60 steps (3 s, counting the hit's own step) against a mob of an elite camp, a mob of a normal camp (or with no camp), or another player; each kind has its own window, and the highest open one wins |
| `quest_moving`, `quest_still` | an open quest attempt in the sim's probe (accepted, not turned in or completed), split by moved |
| `moving` | the seat's position changed by at least 1.0 unit since the last step |
| `resting` | standing still below full hp |
| `idle` | standing still at full hp |

The 3 s window and the 1 u distance are choices, not measurements (`FIGHT_WINDOW_STEPS`, `MOVED_DIST`). `world.time.steps`
is all seat-steps; `world.time.buckets.<bucket>` has `all` and the minute bands, which split on `tick / 1200` (the lab's
minute, which includes the lobby): `m0_5` is ticks below 6,000, `m5_11` ticks 6,000 to 13,199, `m11_plus` every tick from
13,200. `seats` has one row per seed and seat, in seed then seat order: faction, last weapon, steps, buckets, camps
credited (`camps_normal`, `camps_elite`), `deaths`, `quests_completed`, `elite_kills`, `farm_stacks` at the minute-5, 8,
12 samples (null where the seat was not standing) and named chest `picks`. The seat credits are the world's: the camp's
best-damage seat, the elite's killing-blow seat, the lab's own death and quest-completion counts, so the seats' sums
equal the world totals.

`world.normal_clear_secs_by_minute` retains every normal-camp duration in seconds, grouped by the raid minute at the
start of each five-minute interval (0, 5, 10 and so on). It measures the credited seat's first landed hit through the
clear. A fight interrupted by walking or another engagement can have a long elapsed time.

A camp is elite when the observer has seen its elite alive: a camp whose elite nobody saw counts as normal
(`camps_credited_normal`, `kill_secs_normal`; the elite share is the difference, `kill_secs_elite`). `kill_secs.n` can be
below `camps_credited`. `world.elite_kills_credited` is every elite death the sim credits to a seat (the seats'
`elite_kills` summed). It minus the elites block's `killed` is the kills made by a seat with no open attempt (it only hit
adds, it had gone idle for 10 s, or its attempt was cleared when it died).

What the buckets do not show:

- `elite` is time against a mob of an elite camp, the elite or its adds, including the adds after the elite is dead (the
  observer's set of elite camps is never pruned, which keeps the bucket consistent with `camps_elite`).
- `dead` includes a seat eliminated for good, until the match ends, so it dominates the late band of a full-length run.
  A living share is `bucket / (steps - dead)`; the stderr table prints both.
- `resting` is exactly "did not move and hp below the maximum", with no tolerance: a seat standing at 99.9% health is
  `resting`. A seat waiting at a quest giver with no open attempt, standing at a chest, or frozen for any reason lands in
  `resting` or `idle` by its health.
- Walking to a quest not yet accepted is `moving`; fleeing is `moving` once the window passes; area or status damage with
  no attacker is no fight; being knocked about is `moving`.
- A teleport counts as moved; a respawn does not (the first step after one has no previous position).
- A Plague seat's zombie is a mob: a hit between it and another seat is that seat's `camp`-kind fight, and a zombie's hit
  on a mob is nobody's.

## Caveats

- Without `--human-seats` every seat is `bot:true`: it has no quest view and cannot take quests, so the quests block is
  zero by construction. The `HumanBot` policy itself did not accept a quest in the measured runs (`accepted` stays 0).
- Quests are read from `Sim::quest_attempts` for every `bot:false` seat after every step (the last one too). Per quest:
  `dealt` (rows first seen in the seat's observation), `accepted` (offers taken at least once), `attempts` (a retry of an
  offer is a new attempt), and `attempts = completed + ended_timer + ended_death + abandoned + open + turned_in_unpaid`.
  `completed` is a paid reward (it was always 0 before: the seat's own journal drops a paid offer). `abandoned` keeps its
  name and means "ended otherwise": manual abandon, departure or anything the state cannot tell apart. `open` is still
  accepted at the last read; `turned_in_unpaid` met its objective and was never paid (full spell slots, a granted reward).
  `complete_secs` is `{sum, n, samples}`: the accept to the first read turned in or paid, one sample per completion, in
  seat then seed order. A vanished row is a timer end when its last deadline passed within
  the last two ticks (100 ms) and the gates were not locked; otherwise it is a death end if the seat died that step or the
  one before, else `abandoned` (a row gone later than the two ticks is never a timer). Not measured: abandon versus departure, a
  retry within one step, a death two or more steps before the row vanished, and what a seat chose not to do. The schema
  stays `balance-lab/1`; only the fields above are new.
- Elite fights (`elites.by_kind_weapon`, per kind and opening weapon). A seat is *engaged* with an elite while it deals or
  takes a hit against the elite or any mob of its camp. An attempt opens on the first hit the seat deals to the elite
  itself and ends exactly once: `attempts = killed + taken_by_other + died + died_elsewhere + left + open`. `killed`: the
  seat was credited with the kill (`Death.by`: the seat with the most damage on the elite, or a killing Smite). `taken_by_other`: the elite died to anyone else while the seat was engaged.
  `died`: the seat died while engaged and the resolved attacker (`Death.by`, else the latest hit inside the 5 s window) was
  the elite or a mob of its camp. `died_elsewhere`: it died while engaged to anything else. `left`: idle more than 10 s
  (`LEAVE_MS`); a later death or kill of the elite is not that attempt's. `open`: still undecided at the end.
  **`died` and `taken_by_other` are narrower than before** (they used to count every later seat death and every elite
  death after a give-up); the schema stays `balance-lab/1`. A seat that is engaged again later opens a new attempt, so
  tries per kill is `attempts / killed`. `kill_secs` is `{sum, n, samples}` with `n == killed`: the first contact of the
  engaged streak (a hit taken counts) to the kill: the seat's own engagement, not the fight's length. A zero-second
  sample is a seat credited with the kill whose first contact with the elite is the step the elite dies.
- Chests. Each kill has one chest outcome, `killed = chest_killer + chest_other + chest_none + chest_open`: picked by the
  killer, picked only by another seat, picked by nobody before it expired, or still standing at the end;
  `chest_bystander_picks` counts other seats' picks. The killer is the sim's credited killer (`Death.by`), and the same seat
  holds the first pick. The lab reads only seats' observations, so it recognises an
  elite's chest as the non-trophy chest whose drop time (`expires_at` minus the chest lifetime) is the elite's death tick
  and which lies within 12 tiles of the elite's body or home; a different chest dropped in that same tick nearby would
  fool it. A chest nobody saw counts as none 30 s after the kill; a pick on the match's last step is not seen.
  A pick is one `ChestTaken` event: a chest is named by seat, `expires_at` and position (never by handle, which the sim
  reissues), so `sum(picks_by_kind) + picks_unnamed == chest_taken_events`. `rewards.picks_unnamed` counts events no
  observation could name: a skip (`pick == -1`, no option taken), a pick on the last step, a chest that expired in the
  step of the pick. An elite whose last position is unknown never matches its chest by place and ends as `none`.
  `rewards.picks_by_chest_kind` splits `picks_by_kind` into `elite`, `trophy` and `ground`; `augments.by_card[]` has
  `picked_by_killer` and `picked_by_bystander` (elite chests only). `world.elite_camps_cleared` is the part of
  `camps_cleared` whose camp held an elite.
- Camp kill/heal/walk seconds and quest-spell casts are heuristics, as documented in `notes`. A death falls back to the
  victim's latest hit only within the sim's kill-credit window (5 s).
- `Event::Hit.by` carries mob ids for mob attackers (measured: `hits_on_players_by_mob`), so deaths split into
  `elite`/`mob`/`player`/`hazard`.
- Observations are not sampled on the step that ends the match. The observer reads `&Sim`, events and observations only.
- Share reaching the coliseum and encounters while questing are not measured.

## Evaluating a report

`balance_eval` turns a lab report into verdicts with the value model:

```
cargo run --release -p rh-headless --bin balance_eval -- --report <report.json> --inputs tools/balance-lab/inputs.json
```

The JSON result goes to stdout and a table to
stderr. Every model number comes from the report or from `inputs.json`; the evaluator has no defaults for them. Required in
the inputs: `band`, `premium`, `stacks_per_camp`, `coliseum_start_minute`, `difficulty_targets`, `failure`,
`elite_assumptions`; a missing one is an error naming it. A reward descriptor needs `kind` and, for `permanent_buff`,
`direct_stacks`, `damage_fraction` and `move_fraction`. A quest with no descriptor is "not measured".

- **Reference farm rate.** The optional `reference_farm_rate` (`camps_per_minute`, above 0, and its `source`) makes every quest
  and augment row print a second time with the measured rate replaced by the reference everywhere it enters (the price, the
  retry term, a permanent reward's dividend, an augment's income term, the risk in "needed"). The `*_at_reference` fields and
  a table header line name both rates. Without it the output is unchanged and the word "reference" appears nowhere.
- **Reward kinds.** `permanent_buff` (`direct_stacks`, `damage_fraction`, `move_fraction`, optional `upfront_stacks`),
  `spell` with `spell_type` `single_use` (the default; `duel_equivalent_ce` alone) or `reusable` (`duel_equivalent_ce`,
  `farm_rate_gain`, `cap_camps_per_minute`; all three give the 4.3 value, a missing one is "not measured" and named). A
  number another kind does not read is an error naming the quest and the field. Any other kind is unpriced.
- **Upfront cost.** `upfront_stacks` is added to the price as `upfront_stacks / stacks_per_camp` camp-equivalents, certain
  and not scaled by the farm rate; the value stays gross. The Bargain is priced as a trade.
- **Native augments.** `growth: "measured"` needs `readout_per_stack` (above 0): the measured readout growth per minute is
  divided by it to give stacks per minute.

- **not measured**: the report lacks what the quantity needs, or the price is not above 0 or the value is not finite. The
  field is absent (never a 0, never a ratio beside a blank) and `not_measured` gives the reason.
- **assumed**: a number taken from `inputs.json` as design intent (the elite's cost, reward descriptors, difficulty
  classes), not measured by the lab.

Rime Hunt's class is `medium` in the workbook's `Time_Limits` sheet and `hard` in `Top_25`; the shipped inputs use medium.

## inputs.json: where the numbers come from

`tools/balance-lab/inputs.json` holds the design numbers the evaluator prices from. The `quests` and `augments` objects
were transcribed by hand from `docs/quests/FUTURE_ONLY_Quest_Design_Discussion.xlsx` (sheets `Top_25` and
`Augment_Tuning`); every `source` string names the row. Spell rewards stay "not measured" until paired duels give a
duel equivalence (P-BAL-LAB-2); their entries carry no number.

To change one number, edit its line in `inputs.json`, keep the JSON valid, and run
`cargo test -p rh-headless --test balance_inputs --test balance_eval`. The `balance_inputs` test tables pin each number
exactly, so a design change edits both files in the same reviewed change.

## Faction farming and fight timing

The [raid routing and TTK report](../../docs/evidence/raid-routing-ttk/README.md) compares the ten authored builds against nearest-camp and random-nearby farming. Run from the project root. Each route seed reuses the map, focal seat, build and seven opposing RaidBots. The fourth arm runs the current unmodified RaidBot. The lab feature is enabled only for the route experiment.

```sh
mkdir -p .agent-runs/route-repro
cargo build --locked --release -p rh-headless --features strategy-route-lab,elite-bench --bins
target/release/strategy_route_lab 100 1 > .agent-runs/route-repro/primary.jsonl 2> .agent-runs/route-repro/results.jsonl
target/release/strategy_route_lab 10 1 live > .agent-runs/route-repro/live.jsonl 2> .agent-runs/route-repro/live-results.jsonl
target/release/strategy_route_lab 60 41 local-target > .agent-runs/route-repro/bounded.jsonl 2> .agent-runs/route-repro/bounded-results.jsonl
cat .agent-runs/route-repro/primary.jsonl .agent-runs/route-repro/live.jsonl .agent-runs/route-repro/bounded.jsonl > .agent-runs/route-repro/snapshots.jsonl
cat .agent-runs/route-repro/results.jsonl .agent-runs/route-repro/live-results.jsonl .agent-runs/route-repro/bounded-results.jsonl > .agent-runs/route-repro/resolutions.jsonl
python3 tools/balance-lab/route_comparison.py .agent-runs/route-repro/snapshots.jsonl target/release/camp_bench .agent-runs/route-repro --resolutions .agent-runs/route-repro/resolutions.jsonl
python3 tools/balance-lab/route_comparison.py .agent-runs/route-repro/snapshots.jsonl target/release/camp_bench .agent-runs/route-repro/bounded --target-arm local-target --resolutions .agent-runs/route-repro/resolutions.jsonl
python3 tools/balance-lab/farm_stat_screen.py target/release/camp_bench .agent-runs/route-repro
target/release/strategy_ttk --seeds 1..81 --out .agent-runs/route-repro/field
python3 tools/balance-lab/field_ttk_report.py .agent-runs/route-repro .agent-runs/route-repro/field/field-ttk.json --chart
```

The farm comparison uses the same weapon and faction on both sides, runs each kit on both sides and records simultaneous kills and timeouts. The first comparison carries the mob stacks and credited camp clears actually earned by that raid minute. The second gives both kits the lower stack budget and clear count, retaining each route's stat proportions with largest-remainder rounding. Each exchange uses either attacks alone or attacks with the faction Wish. Both commander seats start with one zombie. The Wish-enabled exchange also times first damage on the first fighter to die through their death. Timeouts stay unmeasured. The separate nine-stat screen exchanges the next twelve earned stacks for each alternative at two fixed baseline investments.

## Quest and elite balance

The [quest and elite report](../../docs/evidence/quest-elite-balance/README.md) uses the same 80 seeded 40 Hz raids as the farming study. Run these commands from the repository root. The optional six-seed replay measures time spent using the weapon required for Bastion or Calluses. The normal and elite field series use the same 10-second break in contact to end an encounter.

```sh
d=.agent-runs/quest-elite-repro
mkdir -p "$d"
cargo build --locked --release -p rh-headless --features elite-bench --bins
target/release/quest_balance 1 81 > "$d/quests.jsonl"
target/release/strategy_ttk --seeds 1..81 --out "$d/timing"
python3 tools/balance-lab/quest_balance_report.py "$d/quests.jsonl" --expect-seeds 1..81 --field "$d/timing/field-ttk.json" --csv "$d/quest_funnel.csv" > "$d/quest-summary.json"
target/release/elite_field --seeds 1..81 --out "$d/elites.json"
python3 tools/balance-lab/elite_field_report.py "$d/elite-report" "$d/elites.json" --chart
target/release/augment_field 1 81 > "$d/augments.jsonl"
target/release/camp_bench --pvp --reward-catalog > "$d/catalog.json"
for seed in 2 11 21 31 41 71; do
    target/release/augment_field "$seed" "$((seed + 1))" > "$d/exposure-$seed.jsonl"
done
python3 tools/balance-lab/augment_field_report.py "$d/augments.jsonl" --catalog "$d/catalog.json" --baseline "$d/quests.jsonl" --elite-events "$d/elites.json" --exposure "$d"/exposure-*.jsonl --expect-seeds 1..81 --csv "$d/augment_growth.csv" > "$d/utility.json"
python3 tools/balance-lab/elite_loadout_report.py "$d/augments.jsonl" "$d/timing/field-ttk.json" "$d/elite_loadouts.csv"
python3 tools/balance-lab/elite_pair_timing.py target/release/camp_bench "$d/pair_ttk_samples.csv" "$d/paired_cards_ttk.csv"
python3 tools/balance-lab/reward_matchups.py augments target/release/camp_bench "$d"
python3 tools/balance-lab/reward_matchups.py augments target/release/camp_bench "$d" --cost-ladder
python3 tools/balance-lab/reward_matchups.py quests target/release/camp_bench "$d"
python3 tools/balance-lab/reward_matchups.py quests target/release/camp_bench "$d" --follow
python3 tools/balance-lab/reward_matchups.py quests target/release/camp_bench "$d" --follow --cost-ladder
target/release/camp_bench --elite --fighter duelist --seed 1 --seeds 3 --jobs 4 --weapon sword,bow > "$d/controlled-elites.json"
python3 tools/balance-lab/elite_solo_report.py "$d/controlled-elites.json" "$d/elite_solo.csv"
cargo test --locked --release -p rh-sim --lib all_twenty_five_quests_complete_at_the_live_40_hz_step
cargo test --locked --release -p rh-headless --features elite-bench --bin camp_bench farm_mirrors::tests
```

The solo elite bench uses its existing 20 Hz fixture and a 90-second cutoff. The 40 Hz field measurements are the primary timings for the playable alpha. The reward comparisons read the previously measured preferred stat for each build from the [farming screen](../../docs/evidence/raid-routing-ttk/stat_summary.csv). A single-camp alternative credits twelve preferred-stat stacks and a clear. The cost ladder repeats the comparison for two, four and eight such clears.
