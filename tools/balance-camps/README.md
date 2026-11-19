# balance-camps: camp-clear bench under both hitbox modes

`camp_bench` (`crates/rh-headless/src/bin/camp_bench.rs`, packet P-BALANCE-2) puts one fighter (one faction, one
weapon, no cards) against one camp of each of the 30 catalog camp kinds on the seed1 map, under `LegacyCircle` and
under `Archetypes` (mob spacing `Auto`, the live setting), and prints one JSON report. It changes no rule and no number.

## Run

```
cargo run --release -p rh-headless --bin camp_bench -- --quick --seed 1   # 16 cells: Frost,Holy x Sword,Wand x 2 kinds x 2 modes
cargo run --release -p rh-headless --bin camp_bench -- --seed 1           # 1800 cells (5 x 6 x 30 x 2)
cargo run --release -p rh-headless --bin camp_bench -- --list             # inventory only, runs nothing
```

Flags: `--seed N` (default 1), `--max-ticks N` (default 1800 = 90 s, the legacy campDuel cap), `--quick`, `--list`,
`--with-nospacing` (adds the measurement-only mode `archetypes-nospacing`; the server refuses spacing off), and the
test-only `--control-weapon-swap a=b` (cells labelled weapon `a` run with weapon `b`; a negative control).
Stdout is one JSON object; same seed and flags give identical bytes. It exits 0 whenever it ran: it is evidence, not a gate.

## Reading the report

* `cells`: one per faction x weapon x kind x mode. `outcome` is `cleared`, `died` (the fighter's hp reached 0; not a
  clear) or `truncated` (cap reached). `ticks_to_clear` is the tick of the last camp death, of the fighter's death, or
  the cap. `damage_dealt` / `damage_taken` sum hit, ability, signature and card-bleed events (taken is floored by the hp
  ledger, since burn is reported in batches). `healing_received` sums `Heal` events. `chill_seconds` is slow/root/stun
  applied by the fighter plus 0.05 s per tick per mob chilled by the fighter.
* `comparisons`: per scenario, the six metrics with `rel_delta = (archetypes - legacy) / legacy` (0 when both are zero,
  `null` when legacy is zero and archetypes is not).
* `out_of_band`: scenarios listed first by `outcome` (when the modes end differently), then each metric with
  `|rel_delta| > BAND` (`const BAND: f64 = 0.10`) or an undefined delta.

## Caveats

* The fighter is the scripted `Brawler`: it walks to the nearest actor and swings from 80 u whatever the weapon's reach
  (sword and axe swing at air until the mob closes; bow and wand fire at close range). The table is "Brawler with
  weapon W", not optimal play; both modes use the same policy, seed, camp and start point, which controls for that.
* One camp per kind (the first of its zone with that roster, no elite for normals), one seed, 10 tiles of approach.
* `LegacyCircle` is parity-proven on `parity/fixtures/mob_camp` and `crowded_8` only; it is the legacy stand-in here.
* `packages/server/bench/campDuel.ts` is the conceptual legacy cross-check, not a numeric oracle.
* Regen, consumables and quests are off, so lifesteal healing is not mixed with other healing.

## Elite mode (packet P-ELITE-BENCH-1)

`camp_bench --elite` puts one fighter against each of the 10 elite camps (2 per zone) for every faction x weapon x seed,
under `SimConfig::live_content()` (augments and elites `Rework`), once per build **column**, and prints a JSON report on
stdout and a death-rate table on stderr (per camp and overall: samples, then one death rate per column; the owner target
is 10-20% for an average minute-5 build). It needs the feature: a build without it prints that and exits 2.

```
cargo run --release -p rh-headless --features elite-bench --bin camp_bench -- --elite --seed 1 --seeds 1 > elite.json 2> elite-table.txt
cargo run --release -p rh-headless --features elite-bench --bin camp_bench -- --elite --quick     # Frost,Holy x Sword,Wand x 2 elites x 1 seed
cargo run --release -p rh-headless --features elite-bench --bin camp_bench -- --sample-builds --seeds 1..81 > tools/balance-camps/minute5_builds.json
```

Flags: `--seed S` (first seed, default 1; it is both the map-roll seed and the world seed), `--seeds N` (default 1),
`--max-ticks N` (1800), `--builds PATH` (default the checked-in bot sample). One seed of the full run is 1500 fights
(5 factions x 6 weapons x 10 elites x 5 columns) and takes about 7.5 CPU minutes in release (15 minutes wall on a busy
machine), so the default is one seed (30 samples per camp and column); more seeds scale linearly. A seed that rolls no
camp of an elite is listed in `skipped`.

### The banked-camp model (why the reference is not the bot sample)

The reference build follows the design workbook (`Augment_Value`, assumption `banked`): 12 normal camps already banked at
minute 5, one camp = 12 stacks of the stat its kills pay (`camp_buffs` under `Themed`, else the camp's own mob type).
`banked_camp_build(probe, seed, N)` draws N non-elite camps of the world with replacement and merges stacks by stat; no
cards, no boosts. Columns: `banked_0` (the bare fighter, the negative control), `banked_6`, `banked_12` (the
**reference**: `per_camp` and `overall` report its rate as their own), `banked_18`, and `bot_sample`. Every column fights
the same camp from the same start with the same seed. Each column carries a Wilson 95% interval (`ci95`).

Why not just the bots' minute-5 builds: measured over seeds 1..80, all 56 sampled builds hold 0 cards (cards come only
from elite chests, so a first elite fight has none) and 39 of 56 hold no stacks. Bots bank far fewer camps than the
workbook player, so `bot_sample` is the bots' reality and `banked_12` the design target, and they are not comparable. In
the quick run the first sample builds are empty, so `bot_sample` equals `banked_0` there.

The build is applied at tick 0 through `rh_sim::trusted_build::apply` and the fighter walks 4-10 tiles to the camp
(`start_dist`, `first_hit_tick` are reported), so Plant Your Feet's stillness clock is reset by the walk.

`minute5_builds.json` is written only by `--sample-builds` (never hand-edited): 8 `HumanBot` seats as
`examples/source_soak.rs`, `live_content()`, every bot still standing at tick 6000 contributes its cards, boosts and farm
stacks. `ended_early` lists the seeds whose match ended before minute 5; they contribute nothing.

Config overrides on top of `live_content()`: `lifecycle: ArenaMode::Off` (needed: `Sim::reset_active` refuses the Source
lifecycle), `min_players` 1 and a short lobby (a lone fighter), `regen` off and `quests` off (as the non-elite bench, so
no passive healing or quest reward mixes in). Everything else is the live world.

### Caveats

* The fighter is the scripted `Brawler` (swings from 80 u whatever the weapon): rates are "Brawler with weapon W".
* Bots misvalue the reworked cards until P-AUG-BOTS-1, so the `bot_sample` builds are "what bots pick today".
* Survivorship in the bot sample: only about 3 in 8 matches reach minute 5, and builds are those of the players standing.
* Before P-ELITE-TUNE-1 the elite numbers equal legacy: the table is a baseline, not a result.
