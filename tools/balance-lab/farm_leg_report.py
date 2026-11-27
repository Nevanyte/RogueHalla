"""Report matched, same-position normal-camp choices. Resample maps, keeping every build on a drawn map."""
import argparse
import collections
import csv
import gzip
import json
import random
import statistics
from pathlib import Path


def totals(pairs):
    def arm(rows):
        seconds = sum(r["elapsed_s"] for r in rows)
        weighted = sum(r["earned_value"] for r in rows)
        raw = sum(n for r in rows for _, n in r["earned"])
        return dict(value_per_s=weighted / seconds, stacks_per_s=raw / seconds,
                    clear=sum(r["outcome"] == "cleared" for r in rows),
                    death=sum(r["outcome"] == "died" for r in rows),
                    median_leg_s=statistics.median(r["elapsed_s"] for r in rows))
    a, b = (arm([p[key] for p in pairs]) for key in ("target", "nearest"))
    return {"ratio": a["value_per_s"] / b["value_per_s"] if b["value_per_s"] else None,
            **{"target_" + k: v for k, v in a.items()}, **{"nearest_" + k: v for k, v in b.items()}}


def bootstrap(pairs):
    maps = collections.defaultdict(lambda: [0., 0., 0., 0.])
    for p in pairs:
        a, b = p["target"], p["nearest"]
        bucket = maps[a["seed"]]
        for i, x in enumerate([a["earned_value"], a["elapsed_s"], b["earned_value"], b["elapsed_s"]]):
            bucket[i] += x
    rng = random.Random(1183)
    draws = []
    for _ in range(2000):
        va, ta, vb, tb = map(sum, zip(*rng.choices(list(maps.values()), k=len(maps))))
        if vb:
            draws.append((va / ta) / (vb / tb))
    draws.sort()
    return (draws[int(.025 * len(draws))], draws[int(.975 * len(draws))]) if draws else (None, None)


def duel_rows(pairs, binary):
    from route_comparison import SCORES, WEAPON, run_mirrors

    inputs = []
    result = []
    for (seed, build), p in sorted(pairs.items()):
        a, b = p["target"], p["nearest"]
        if a["outcome"] != "cleared" or b["outcome"] != "cleared":
            continue

        def kit(row):
            counts = collections.Counter(dict(row["initial_stacks"]))
            counts.update(dict(row["earned"]))
            return dict(stacks=sorted(counts.items()), clears=row["credited_clears"])

        key = f"{seed}/{build}"
        inputs.append(dict(id=key, seed=seed, faction=a["faction"].capitalize(), weapon=WEAPON[build].capitalize(),
                           companion=False, target=kit(a), other=kit(b)))
        result.append(dict(id=key, seed=seed, build=build, faction=a["faction"], distinct=a["chosen_distinct"]))
    outcomes = run_mirrors(inputs, binary)
    for r in result:
        sides = outcomes[r["id"]]["with_wishes"]
        r.update(first_seat=sides[0], second_seat=sides[1], score=statistics.mean(SCORES[x] for x in sides))
    return result


def main():
    args = argparse.ArgumentParser(description=__doc__)
    args.add_argument("output", type=Path)
    args.add_argument("inputs", nargs="+", type=Path)
    args.add_argument("--duel", type=Path, help="camp_bench executable for the full-health matched-kit comparison")
    opt = args.parse_args()
    pairs = collections.defaultdict(dict)
    raw = []
    for path in sorted(opt.inputs):
        opener = gzip.open if path.suffix == ".gz" else open
        with opener(path, "rt") as stream:
            for row in json.load(stream):
                group = pairs[row["seed"], row["build"]]
                if row["arm"] in group:
                    raise ValueError("duplicate map / build / arm")
                group[row["arm"]] = row
                raw.append(row)
    for p in pairs.values():
        a, b = p["target"], p["nearest"]
        if a["start"] != b["start"] or a["chosen_distinct"] != (a["camp"] != b["camp"]):
            raise ValueError("world or destination mismatch")
        if not a["chosen_distinct"] and {**a, "arm": "nearest"} != b:
            raise ValueError("identical decision produced different results")
    report = []
    for who in ("all", "fire", "sentenced", "plague", "frost", "holy"):
        cohort = [p for p in pairs.values() if who in ("all", p["target"]["faction"])]
        for subset in ("all_choices", "different_destinations"):
            selected = [p for p in cohort if subset == "all_choices" or p["target"]["chosen_distinct"]]
            if not selected:
                continue
            lo, hi = bootstrap(selected)
            report.append(dict(faction=who, subset=subset, pairs=len(selected), **totals(selected),
                               ratio_ci_low=lo, ratio_ci_high=hi))
    opt.output.mkdir(exist_ok=True, parents=True)
    with (opt.output / "returns.csv").open("w", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=report[0], lineterminator="\n")
        writer.writeheader()
        writer.writerows(report)
    if opt.duel:
        duels = duel_rows(pairs, opt.duel)
        with (opt.output / "kit_duels.csv").open("w", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=duels[0], lineterminator="\n")
            writer.writeheader()
            writer.writerows(duels)
    if all(path.suffix != ".gz" for path in opt.inputs):
        with gzip.GzipFile(str(opt.output / "attempts.json.gz"), "wb", mtime=0) as stream:
            stream.write(json.dumps(raw, separators=(",", ":")).encode())


if __name__ == "__main__":
    main()
