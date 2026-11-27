"""Summarize fresh, same-build normal/elite solo camp pairs and retain failed attempts."""
import argparse
import collections
import csv
import json
from pathlib import Path
import math
import random
from statistics import median


def map_interval(pairs):
    by_seed = collections.defaultdict(list)
    for normal, elite in pairs:
        if normal["clear_s"] is not None and normal["clear_s"] > 0 and elite["clear_s"] is not None:
            by_seed[normal["seed"]].append(elite["clear_s"] / normal["clear_s"])
    maps = sorted(by_seed)
    if len(maps) < 2:
        return None, None
    rng = random.Random(763)
    samples = []
    for _ in range(1000):
        selected = rng.choices(maps, k=len(maps))
        samples.append(median([v for seed in selected for v in by_seed[seed]]))
    samples.sort()
    return samples[math.ceil(.025 * len(samples)) - 1], samples[math.ceil(.975 * len(samples)) - 1]


def summarize(pairs):
    normals = [n["clear_s"] for n, _ in pairs if n["clear_s"] is not None]
    elites = [e["clear_s"] for _, e in pairs if e["clear_s"] is not None]
    matched = [(n["clear_s"], e["clear_s"]) for n, e in pairs if n["clear_s"] is not None and n["clear_s"] > 0 and e["clear_s"] is not None]
    ratios = sorted(e / n for n, e in matched)
    low, high = map_interval(pairs)
    return dict(
        contributing_maps=len({n["seed"] for n, e in pairs if n["clear_s"] is not None and n["clear_s"] > 0 and e["clear_s"] is not None}),
        ratio_ci_low=low, ratio_ci_high=high, pairs=len(pairs), normal_clears=len(normals), elite_clears=len(elites),
        normal_zero_tick_clears=normals.count(0), elite_zero_tick_clears=elites.count(0),
        normal_deaths=sum(n["outcome"] == "died" for n, _ in pairs),
        elite_deaths=sum(e["outcome"] == "died" for _, e in pairs),
        matched_clears=len(matched), median_normal_s=median(normals) if normals else None,
        median_elite_s=median(elites) if elites else None,
        median_within_pair_ratio=median(ratios) if ratios else None,
        ratio_of_matched_medians=median([e for _, e in matched]) / median([n for n, _ in matched]) if matched else None,
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("inputs", type=Path, nargs="+")
    args = parser.parse_args()
    by_pair = collections.defaultdict(dict)
    for path in args.inputs:
        for r in json.loads(path.read_text()):
            key = (r["seed"], r["minute"], r["boss"], r["faction"], r["weapon"])
            pair = by_pair[key]
            if r["camp_type"] in pair:
                raise ValueError(f"duplicate pair member: {key}")
            pair[r["camp_type"]] = r
    groups = collections.defaultdict(list)
    for (_, minute, boss, _, _), members in by_pair.items():
        pair = members["normal"], members["elite"]
        for group in ("all", boss):
            groups[minute, group].append(pair)
    rows = [dict(minute=k[0], boss=k[1], **summarize(v)) for k, v in sorted(groups.items())]
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=rows[0], lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)
    for r in rows:
        if r["boss"] == "all":
            print({k: round(v, 3) if isinstance(v, float) else v for k, v in r.items()})


if __name__ == "__main__":
    main()
