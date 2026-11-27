"""Summarize complete field episodes, competing causes and match outcomes across disjoint seed shards."""
import argparse
import collections
import json
import math
from pathlib import Path
import random
import statistics

from route_comparison import write_csv

BANDS = ("0-5", "5-10", "10-15", "15-20", "20+")


def nearest_quantile(values, p):
    return values[int(math.floor((len(values) - 1) * p + 0.5))] if values else None


def seed_interval(rows):
    by_seed = collections.defaultdict(list)
    for row in rows:
        by_seed[row["seed"]].append(row["elapsed_s"])
    clusters = list(by_seed.values())
    if len(clusters) < 5:
        return None, None
    rng = random.Random(35291)
    samples = sorted(statistics.median(v for cluster in rng.choices(clusters, k=len(clusters)) for v in cluster)
                     for _ in range(1000))
    return samples[24], samples[974]


def summarize(band, group, rows):
    secs = sorted(row["elapsed_s"] for row in rows)
    p25, median, p75 = [nearest_quantile(secs, p) for p in (0.25, 0.50, 0.75)]
    low, high = seed_interval(rows)
    return dict(band=band, group=group, n=len(rows), map_n=len({r["seed"] for r in rows}),
                p25_s=p25, median_s=median, p75_s=p75, median_boot_low_s=low, median_boot_high_s=high,
                one_hit_n=sum(r["hits"] == 1 for r in rows), at_most_1s=sum(t <= 1 for t in secs),
                at_most_4s=sum(t <= 4 for t in secs))


def chart(summary, path):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    fig, axes = plt.subplots(1, 2, figsize=(10, 4.2), constrained_layout=True)
    for ax, group, title, color in zip(axes, ("camp", "pvp"), ("Normal camp clears", "Player kills"), ("#177765", "#814376")):
        selected = [r for r in summary if r["group"] == group]
        for i, row in enumerate(selected):
            if row["n"]:
                median, lo, hi = [row[k] for k in ("median_s", "p25_s", "p75_s")]
                ax.vlines(i, lo, hi, color=color, lw=2.5)
                ax.plot(i, median, "o", color=color)
            ax.text(i, -0.15, f'n={row["n"]}', ha="center", transform=ax.get_xaxis_transform(), fontsize=8)
        ax.set_xticks(range(5), ["0–5", "5–10", "10–15", "15–20", "20+"])
        ax.set_xlim(-0.5, 4.5)
        ax.set_ylim(bottom=0)
        ax.set_title(title, loc="left", fontweight="bold")
        ax.set_xlabel("Raid minutes at first contact", labelpad=25)
        ax.set_ylabel("Seconds, median and middle 50%")
        ax.grid(axis="y", alpha=0.20)
        ax.spines[["right", "top"]].set_visible(False)
    fig.savefig(path, dpi=165)
    plt.close(fig)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("shards", type=Path, nargs="+")
    parser.add_argument("--chart", action="store_true")
    args = parser.parse_args()
    episodes, matches, seen = [], [], set()
    for path in args.shards:
        data = json.loads(path.read_text())
        seeds = data["simulated_seeds"]
        if data["tick_ms"] != 25 or set(seeds) & seen or len(seeds) != len(set(seeds)):
            raise ValueError(f"unexpected tick rate or duplicate seed in {path}")
        seen.update(seeds)
        if set(seeds) != {m["seed"] for m in data["match_results"]}:
            raise ValueError(f"incomplete match list in {path}")
        episodes.extend(data["episodes"])
        matches.extend(data["match_results"])
    summary = []
    for band in BANDS:
        batch = [r for r in episodes if r["band"] == band]
        camps = [r for r in batch if r["outcome"] == "camp_cleared"]
        kills = [r for r in batch if r["outcome"] in ("death_clean", "death_mixed")]
        clean = [r for r in kills if r["outcome"] == "death_clean"]
        fresh = [r for r in camps if r["prior_pull"] is False]
        full = [r for r in clean if not r["gates_locked_start"] and (r["start_hp_fraction"] or 0) >= 0.90]
        for name, records in (("camp", camps), ("camp_first_pull", fresh), ("pvp", kills),
                              ("pvp_single_attacker", clean), ("pvp_single_healthy_prelock", full),
                              ("pvp_mixed", [r for r in kills if r["outcome"] == "death_mixed"])):
            summary.append(summarize(band, name, records))
    args.output.mkdir(parents=True, exist_ok=True)
    write_csv(args.output / "field_ttk.csv", summary)
    counts = collections.Counter((r["band"], r["kind"], r["outcome"]) for r in episodes)
    write_csv(args.output / "field_contacts.csv", [dict(band=b, kind=k, outcome=o, n=n)
              for (b, k, o), n in sorted(counts.items(), key=lambda kv: (BANDS.index(kv[0][0]), *kv[0][1:]))])
    results = [dict(seed=m["seed"], ticks=m["ticks"], terminated=m["terminated"], truncated=m["truncated"], **r)
               for m in matches for r in m["resolved"]]
    write_csv(args.output / "field_results.csv", results)
    if args.chart:
        chart(summary, args.output / "field-ttk.png")


if __name__ == "__main__":
    main()
