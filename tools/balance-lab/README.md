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
`seating`, `maps` (the map the sim admitted), `world`, `elites`, `quests`, `rewards`, `augments`, `notes`. Counters are
sums over seeds, folded in seed order. Sums of seconds carry `n`, the sample count; minute samples (`world.minutes`, at
minutes 5, 8, 12 of the sim clock) carry `samples`. Matches of eight human-bot seats end after about 900 to 3,400 ticks,
so later minutes have few samples or none.

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
  seat then seed order. A vanished row is a timer end if its last deadline had passed and the gates were not locked, else
  a death end if the seat died that step or the one before, else `abandoned`. Not measured: abandon versus departure, a
  retry within one step, a death two or more steps before the row vanished, and what a seat chose not to do. The schema
  stays `balance-lab/1`; only the fields above are new.
- Camp kill/heal/walk seconds and quest-spell casts are heuristics, as documented in `notes`. A death falls back to the
  victim's latest hit only within the sim's kill-credit window (5 s).
- `Event::Hit.by` carries mob ids for mob attackers (measured: `hits_on_players_by_mob`), so deaths split into
  `elite`/`mob`/`player`/`hazard`.
- Observations are not sampled on the step that ends the match. The observer reads `&Sim`, events and observations only.
- Share reaching the coliseum and encounters while questing are not measured.
