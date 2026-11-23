# Per-elite sweep (coordinator, 2026-11-22): measurements only, no number committed

Serves `docs/backlog/BALANCE_LAB_PATCH.md` section 4 (owner, 2026-11-22: "Yes, per-elite numbers"). The sweep was
paused by the owner the same day ("please pause and save all development") before any number was chosen. Nothing here
changes the game: `REWORK_ELITES.per_kind` is still empty on every branch.

## How it was measured

- Tool: `camp_bench --elite --fighter duelist` (`tools/balance-camps/README.md`), the owner's reference: the Duelist at
  12 banked camps (`banked_12`), one fight per faction x weapon x elite x seed.
- Tree: `agent/opus/P-BAL-ELITE-SEAM-1` @ `aeab047a5` (the per-kind seam, merged in integration 83). The baseline on
  that tree reproduces the recorded table of P-ELITE-TUNE-1 exactly at 3 seeds (17.4% overall, 157 of 900).
- Driver: `elite_sweep.py` in this folder. For each candidate it writes literal rows into `REWORK_ELITES.per_kind` in a
  throwaway worktree, rebuilds the bench, runs it, appends one JSON line of per-elite results, and restores the file.
  A row is `kind:hp:dmg`, both multipliers on top of the global Rework pair (hp x1.5, damage x1.0).
  `python3 -I elite_sweep.py <worktree> <out.jsonl> <seeds> <jobs> <label>=<spec> ...`
- Results: `grid1.jsonl` (6 seeds: one multiplier for all ten elites), `grid2.jsonl` (9 seeds: three candidate tables),
  `grid3.jsonl` (9 seeds: the baseline and table B again, with the full reports kept). The full bench reports
  (`base`, `B`; 6.7 MB each) are not committed: they are in the git-ignored `.agent-runs/elite-sweep/` of the main
  checkout and are reproduced byte for byte by re-running the driver with the same seeds.
- Sample sizes: 6 seeds give 180 fights an elite (about +/-6 points at 20%); 9 seeds give 270 an elite, of which 180
  melee (sword, axe, shield, fists), 45 bow and 45 wand. The bow and wand columns below rest on 45 fights each.

## Grid 1: death rate by one multiplier for all elites (6 seeds)

| Elite | now | dmg 0.7 | dmg 0.85 | dmg 1.2 | dmg 1.5 | dmg 1.9 | hp 0.75 | hp 1.3 | dmg for 15% (fit) |
|---|---|---|---|---|---|---|---|---|---|
| Avalanche Troll | 37.8% | 27.2% | 31.7% | 47.2% | 56.1% | 70.0% | 31.7% | 49.4% | 0.49 |
| Glacial Warden | 26.7% | 18.9% | 23.9% | 32.8% | 45.0% | 63.3% | 11.7% | 37.8% | 0.66 |
| Ash Drake | 26.7% | 13.9% | 18.3% | 33.9% | 44.4% | 61.7% | 21.7% | 31.7% | 0.75 |
| Gaoler | 24.4% | 16.1% | 20.0% | 33.9% | 55.0% | 73.9% | 7.2% | 45.6% | 0.76 |
| Wyrmling | 22.8% | 13.3% | 18.3% | 33.9% | 49.4% | 65.6% | 11.1% | 36.1% | 0.78 |
| Pox Behemoth | 11.7% | 11.1% | 11.1% | 12.8% | 17.2% | 20.6% | 11.1% | 15.6% | 1.27 |
| Rat Ogre | 10.6% | 8.3% | 9.4% | 13.3% | 21.1% | 33.3% | 8.3% | 19.4% | 1.15 |
| Archon | 6.7% | 6.7% | 6.7% | 7.2% | 9.4% | 20.0% | 6.7% | 7.8% | 1.73 |
| Blood Saint | 6.2% | 6.2% | 6.2% | 6.2% | 8.4% | 12.4% | 6.2% | 6.7% | about 3 (poor fit) |
| Executioner | 1.1% | 0.6% | 0.6% | 1.7% | 5.0% | 16.7% | 0.6% | 3.3% | 1.89 |

## Grid 2: three candidate tables (9 seeds)

Rows are `hp:dmg` per elite. All three put every elite's OVERALL rate between 8% and 22%.

| Elite | A (damage only) | rate | B (mixed) | rate | C (bracket) | rate |
|---|---|---|---|---|---|---|
| Wyrmling | 1:0.78 | 16.3% | 0.9:0.85 | 14.1% | 1:0.72 | 14.4% |
| Ash Drake | 1:0.75 | 14.4% | 1:0.8 | 15.6% | 1:0.7 | 14.4% |
| Gaoler | 1:0.76 | 16.3% | 0.88:0.9 | 13.0% | 1:0.82 | 18.1% |
| Glacial Warden | 1:0.66 | 13.0% | 0.85:0.8 | 11.1% | 1:0.72 | 14.8% |
| Avalanche Troll | 1:0.49 | 13.7% | 0.8:0.55 | 9.3% | 0.7:0.6 | 7.8% |
| Rat Ogre | 1:1.15 | 13.0% | 1.15:1.05 | 15.2% | 1:1.25 | 14.8% |
| Pox Behemoth | 1:1.27 | 13.0% | 1.2:1.1 | 14.4% | 1:1.5 | 17.0% |
| Executioner | 1:1.89 | 16.7% | 1.3:1.6 | 18.5% | 1.5:1.5 | 22.2% |
| Archon | 1:1.73 | 11.5% | 1.3:1.5 | 14.1% | 1.5:1.4 | 18.1% |
| Blood Saint | 1:2.5 | 15.3% | 1.3:2.0 | 14.9% | 1.5:1.8 | 14.2% |
| overall | | 14.3% | | 14.0% | | 15.6% |

## Grid 3: the same numbers by weapon class (9 seeds)

Today (the empty table):

| Elite | overall | melee | bow | wand | cleared in (s) | hp taken in a clear |
|---|---|---|---|---|---|---|
| Wyrmling | 22.2% | 10.6% | 44.4% | 46.7% | 15.0 | 90% |
| Ash Drake | 23.3% | 8.3% | 75.6% | 31.1% | 14.8 | 80% |
| Executioner | 0.7% | 0.6% | 0.0% | 2.2% | 24.2 | 63% |
| Gaoler | 22.6% | 15.0% | 51.1% | 24.4% | 37.4 | 106% |
| Rat Ogre | 11.1% | 2.8% | 33.3% | 22.2% | 31.5 | 71% |
| Pox Behemoth | 11.9% | 0.0% | 31.1% | 40.0% | 27.7 | 50% |
| Glacial Warden | 21.1% | 12.8% | 68.9% | 6.7% | 38.9 | 88% |
| Avalanche Troll | 31.5% | 25.0% | 57.8% | 31.1% | 54.5 | 96% |
| Blood Saint | 6.3% | 0.0% | 39.5% | 0.0% | 25.8 | 31% |
| Archon | 5.9% | 0.0% | 35.6% | 0.0% | 28.6 | 57% |
| all | 15.7% | 7.5% | 43.8% | 20.4% | | |

By weapon today: sword 7.3%, axe 4.4%, shield 6.4%, fists 11.8%, bow 43.8%, wand 20.4%.

Table B:

| Elite | overall | melee | bow | wand | cleared in (s) | hp taken in a clear |
|---|---|---|---|---|---|---|
| Wyrmling | 14.1% | 6.1% | 40.0% | 20.0% | 14.3 | 85% |
| Ash Drake | 15.6% | 2.8% | 68.9% | 13.3% | 14.9 | 73% |
| Executioner | 18.5% | 12.8% | 11.1% | 48.9% | 29.2 | 100% |
| Gaoler | 13.0% | 8.9% | 22.2% | 20.0% | 33.7 | 95% |
| Rat Ogre | 15.2% | 7.2% | 40.0% | 22.2% | 35.3 | 79% |
| Pox Behemoth | 14.4% | 0.6% | 33.3% | 51.1% | 32.0 | 62% |
| Glacial Warden | 11.1% | 6.1% | 40.0% | 2.2% | 34.7 | 78% |
| Avalanche Troll | 9.3% | 8.3% | 8.9% | 13.3% | 47.8 | 78% |
| Blood Saint | 14.9% | 1.7% | 79.1% | 6.7% | 31.2 | 67% |
| Archon | 14.1% | 1.7% | 51.1% | 26.7% | 33.6 | 85% |
| all | 14.0% | 5.6% | 39.3% | 22.4% | | |

## What it shows

1. **The overall rate hides a split.** Melee dies 7.5% of the time, bow 43.8%, wand 20.4%. Blood Saint, Archon and Pox
   Behemoth kill no melee fighter at all today; their whole death rate is ranged.
2. **A table tuned to the overall band is the wrong patch.** Table B puts every elite's overall rate in the band, and
   does it for Blood Saint by killing bow 79% of the time (melee 1.7%) and for Archon by killing bow 51% (melee 1.7%).
   The elites are "balanced" on the average and worse for the weapon that was already dying.
3. **Bow against elites is partly the bench.** The ranged Duelist stands and shoots and never steps back
   (`tools/balance-camps/README.md`, "The give-up rule": retreating resets the camp). The bench's own caveat says ranged
   rows are biased. How much of the 43.8% a real player would avoid is not measured.
4. **What the multipliers do well:** the five hard elites respond smoothly to damage, and Gaoler, Wyrmling and Glacial
   Warden to hp as well. Avalanche Troll needs damage: its deaths barely move with hp.

## Coordinator's recommendation when the work resumes (not applied)

- Tune each elite on the **melee reference** (four weapons, 180 fights at 9 seeds) toward the band, and report overall,
  bow and wand beside it. On that measure today: Avalanche Troll 25.0% is above the band; Gaoler 15.0%, Glacial Warden
  12.8% and Wyrmling 10.6% are inside it; Ash Drake 8.3% is just under; Rat Ogre 2.8%, Executioner 0.6%, Pox Behemoth,
  Blood Saint and Archon 0% are far under. That is a different patch from tables A to C: it would ease the Troll and
  raise the five easy elites until melee is at risk, without making bow worse.
- Settle bow separately and first measure how much is the script: give the bench's ranged Duelist a kite inside the
  leash (a bench packet), re-run the baseline, and only then decide whether a weapon factor (a mechanic change, which
  the owner's "numbers and offer rules" excludes) is worth proposing.
- The owner set the target on the overall rate (2026-11-19, "10-20% for an average minute-5 build"). Tuning on the
  melee reference is a change of measure: it is his to confirm.
- A row for the candidate table "M" (smaller multipliers on the easy elites) was queued and not run:
  `wyrmling:0.9:0.85;ashDrake:1:0.8;gaoler:0.88:0.9;glacialWarden:0.85:0.85;avalancheTroll:0.8:0.6;ratOgre:1:1.1;executioner:1.3:1.5;archon:1.2:1.3;bloodSaint:1.2:1.5`.

## Not established

- Any committed number. The bots' elite estimates (`rh-policy`) still read the global pair (P-BAL-ELITE-BOTS-1).
- Human play. The Duelist never retreats and does not read dodges; its rates are biased upward for every weapon.
- Time cost. "Cleared in" moves with hp (the Troll 54.5 s to 47.8 s under B); the augment price depends on it and no
  table was chosen with it in mind.
