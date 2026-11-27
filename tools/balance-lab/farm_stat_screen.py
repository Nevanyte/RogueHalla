"""Screen one camp's incremental farm-stat value in the ten authored faction/weapon mirrors."""
import argparse
from collections import defaultdict
from itertools import combinations
import json
from pathlib import Path
import statistics

from route_comparison import SCORES, WEAPON, run_mirrors, write_csv


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("camp_bench", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    data = json.loads((root / "testing/farm_route_data.json").read_text())
    mobs = {}
    for row in data["rewards"]:
        if row.get("k") in {"stat", "flat"}:
            mobs.setdefault(row["stat"], row["id"])
    if set(mobs) != {"ad", "ap", "armor", "shield", "maxHp", "attackSpeed", "moveSpeed", "lifesteal", "damage"}:
        raise ValueError("farm data does not contain the nine expected stat types")

    cases = []
    labels = {}
    for build, weapon in WEAPON.items():
        faction, _ = build.split(":")
        for baseline in (72, 180):
            # Twelve stacks model four credited kills. Both kits have equal total stacks and clears.
            for left, right in combinations(sorted(mobs), 2):
                def kit(extra):
                    return {"stacks": [[mob, baseline // 9 + 12 * (stat == extra)]
                                       for stat, mob in mobs.items()], "clears": baseline // 12 + 1}
                for seed in (71, 72, 73):
                    pair_id = f"{build}/{baseline}/{left}/{right}/{seed}"
                    labels[pair_id] = (build, baseline, left, right)
                    cases.append(dict(id=pair_id, seed=seed, faction=faction.capitalize(),
                                      weapon=weapon.capitalize(), companion=build == "plague:B",
                                      target=kit(left), other=kit(right)))
    results = run_mirrors(cases, args.camp_bench)
    comparisons = []
    groups = defaultdict(list)
    for key, verdict in results.items():
        build, baseline, left, right = labels[key]
        comparisons.append({"id": key, **{k: "|".join(verdict[k]) for k in ("weapon_only", "with_wishes")},
                            "landed_wishes": sum(verdict["landed_wishes"])})
        for column in ("weapon_only", "with_wishes"):
            score = statistics.mean(SCORES[x] for x in verdict[column])
            groups[build, baseline, left, column].append(score)
            groups[build, baseline, right, column].append(1 - score)
    summary = [dict(build=b, baseline_stacks=n, stat=stat, mode=mode, score=statistics.mean(scores),
                    alternatives=8, combat_seeds=3, exchanges=len(scores) * 2)
               for (b, n, stat, mode), scores in sorted(groups.items())]
    args.output.mkdir(parents=True, exist_ok=True)
    write_csv(args.output / "stat_comparisons.csv", comparisons)
    write_csv(args.output / "stat_summary.csv", summary)


if __name__ == "__main__":
    main()
