# Stock raid pacing on matched maps

Both cohorts use generated maps with seeds 1–12, authoritative 25 ms steps, and one human-eligible seat driven by RaidBot with seven true RaidBot fills. The current frozen observers use the by-kind elite tuning, tuned quests, and the ordinary Themed farming rules with optional focus off. The comparison combines all changes in the frozen builds. Matches may diverge as the bots react to different outcomes.

## Results

Medians are in seconds. Sample counts are completed encounters or raids unless indicated. The same 12 maps contribute in each cohort; the episodes within them can differ.

| Measure | Stock median (n) | Tuned median (n) |
|---|---:|---:|
| Normal full clear, 10-second gap | 14.500 (683) | 14.713 (640) |
| Normal full clear, 30-second gap | 15.300 (683) | 15.375 (640) |
| Normal first pull, 30-second gap | 15.375 (651) | 15.725 (602) |
| Elite full clear | 13.825 (311) | 22.513 (216) |
| Elite boss contact to boss kill | 12.075 (347) | 20.063 (252) |
| PvP player final blow | 8.600 (29) | 9.850 (27) |
| PvP mixed | 8.600 (29) | 10.200 (25) |
| PvP clean | — (0) | 3.675 (2) |
| PvP healthy, clean and before gates lock | — (0) | — (0) |
| Raid duration | 1209.5 (12) | 1208.5 (12) |
| Time to first death | 425.5 (12) | 380.0 (12) |

- The 10-second observer recorded 683/991 → 640/940 normal clears and 311/409 → 216/350 elite clears. In minutes 0–5 normal clears took 13.075 → 13.500 seconds (419 → 422 clears), while elite clears took 22.238 → 20.700 seconds (44 → 51 clears). In minutes 10–15 solo normal clears took 15.988 → 14.875 seconds (42 → 31) and solo elite clears 9.825 → 17.925 seconds (51 → 27). These are separate medians for naturally selected fights.
- Environmental damage was present in 28 of 29 stock PvP final-blow samples and 22 of 27 tuned samples. Multiple hostile owners appeared in 16 and 17 respectively, with overlap. The minutes 0–5 PvP sample is two → one finishes, all within one map in each cohort. The overall median shift describes mixed field fights. These samples are too sparse to estimate healthy duel TTK.
- All 12 raids ended in both cohorts with no truncation. Earliest/latest raid ends were 1088–1331 seconds stock and 800–1449 tuned. Zero → one raids ended before minute 15, and four → three before minute 20. The total stayed at 84 deaths. Result credit recorded 29 → 25 player kills, 48 → 49 sudden-death deaths and 7 → 10 other or uncredited deaths. There were six → four deaths before minute five and 18 → 20 before minute ten.

[All timing groups and age bands](timings.csv) · [Per-map results](matches.csv) · [Encounter outcomes](outcomes.csv) · [Raw archive](raw.json.gz)

## Timing definitions

Both observers use raid age from the server's recorded raid start. The historical and current runs use the same definitions below. The script checks both observers agree on terminal ticks, termination and truncation for each seed.

- A normal or elite camp clear starts at the first settled positive player or owned-companion damage to any camp member. Both report the time to the full camp's clear. The shared normal/elite observer closes an attempt after a 10-second contact gap. The TTK observer uses a 30-second gap for normal camps and also identifies first pulls. Full-clear medians only include completed attempts. The outcome table keeps unfinished pulls visible.
- Boss-only time starts at first damage to the boss and ends at its death. Full elite clear can also include escorts. Solo field attempts have one recorded player owner, including their companions. They vary in gear, prior pulls and opportunity, so the controlled paired solo report is the pace-target test.
- PvP is victim first attributed hostile player contact to final blow. A ten-second contact gap closes the episode. Player-final-blow samples include mixed fights with multiple attackers or environmental damage. The clean subset requires one attacker, no environmental damage and the same finisher. The healthy pre-lock subset further requires at least 90% HP at first contact, before gates lock.
- Match length uses the last authoritative result duration, in whole raid seconds. Death cause tracks the credited killer on the authoritative result. That can differ from the raw final blow used for the PvP timing episodes.

## Reproduce

The checked-in archive preserves all per-seed encounter records, timing episodes and match results selected for this comparison. The three CSVs can be regenerated from it.

```sh
python3 tools/balance-lab/field_pacing_compare.py /tmp/field-check --seeds 1..13 \
  --read-archive docs/evidence/alpha-balance-targets/field/raw.json.gz
```

To recapture the current data, run each frozen executable with `--seeds FIRST..END` using half-open disjoint ranges. The actual starts were 1, 3, 5, 7, 9 and 11, each with `END=FIRST+2`. Set `--out` to `elite-FIRST.json` for `elite_field` and `timing-FIRST` for `strategy_ttk`. The timing observer writes `field-ttk.json` under its directory. Compare against the historical raw shards in the same repository worktree.

```sh
python3 tools/balance-lab/field_pacing_compare.py /tmp/field-compare --seeds 1..13 \
  --cohort baseline '.agent-runs/quest-elite-replicate/shard-*.json' '.agent-runs/strategy-ttk/field-final-*/field-ttk.json' \
  --cohort tuned '.agent-runs/alpha-balance-targets/field/elite-*.json' '.agent-runs/alpha-balance-targets/field/timing-*/field-ttk.json'
```

| Frozen executable | SHA-256 |
|---|---|
| Current `elite_field` | `0665b260c544fc71638a8da8b6cad50f583c66b9184ad6d43fe48925a6ac9ce3` |
| Current `strategy_ttk` | `99d2541835ef603c635041bd5f695b3e5ce9bfb3e91d58d5a2523becdb5fe344` |
| Baseline `elite_field`, source `c8b46d760` | `a7a83a92888fa58dba0ac95b1ab049ad0c18664200fa3ec5a393307c15e9b6db` |

The baseline TTK comparison uses the archived 1–10 and 11–20 output shards. Their SHA-256 values are `597a1cd5f7b089c7d642dd5a14aa9535105a590614ccf05c4c6b38b600a25f36` and `a2f260b72f7f3e9eaa28aa80a34e383e2af1deed7c78f3b67a059a52e0d6fd70`.
