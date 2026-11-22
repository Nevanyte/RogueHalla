# balance-lab

Plays seeded 8-seat matches under `SimConfig::live_content()` and reports the rates the quest and augment prices depend
on: camps, deaths, elite fights, the quest funnel, chest offers and picks, augment holders. Code:
`crates/rh-headless/src/balance_lab.rs` (options, run, report), `balance_lab/observer.rs` (the read-only observer),
`balance_lab/acc.rs` (counters), `balance_lab/table.rs` (stderr table).

```sh
cargo run --release -p rh-headless --bin balance_lab -- --seeds 1..9 --jobs 4 --human-seats > report.json 2> table.txt
cargo test -p rh-headless --test balance_lab
```

## Flags

`--seeds A..B` (half-open, default `1..2`), `--jobs N` (speed only: the bytes do not depend on it), `--max-ticks N`
(default 24000), `--map generate|PATH` (default: one generated map per seed), `--seat I=human|brawler` (repeatable;
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
- Camp kill/heal/walk seconds, the quest end cause (timer / death / abandoned) and quest-spell casts are heuristics, as
  documented in `notes`.
- `Event::Hit.by` carries mob ids for mob attackers (measured: `hits_on_players_by_mob`), so deaths split into
  `elite`/`mob`/`player`/`hazard`.
- Observations are not sampled on the step that ends the match. The observer reads `&Sim`, events and observations only.
- Share reaching the coliseum and encounters while questing are not measured.
