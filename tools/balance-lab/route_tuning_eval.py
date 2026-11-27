"""Paired authored-weight value and optional combat check for two route-lab JSONL arms.

Usage: python3 tools/balance-lab/route_tuning_eval.py TRIAL.jsonl NEAREST.jsonl OUT.json [--duel BIN]
The ratio uses all assigned maps that reached the minute, including eliminated players' earned
inventories. Combat uses only pairs alive at that minute and always swaps seating order.
"""
import argparse
import json
import random
import statistics
from pathlib import Path

from route_comparison import SCORES, WEAPON, run_mirrors


def rows(path):
    out = {}
    for line in path.read_text().splitlines():
        row = json.loads(line)
        key = row["seed"], row["minute"]
        if key in out:
            raise ValueError(f"duplicate snapshot {key} in {path}")
        out[key] = row
    return out


def interval(values, statistic=statistics.mean, seed=6417):
    if len(values) < 5:
        return None
    rng = random.Random(seed)
    draw = sorted(statistic(rng.choices(values, k=len(values))) for _ in range(3000))
    return [draw[74], draw[2924]]


def pooled_ratio_interval(rows, seed=6417):
    if len(rows) < 5:
        return None
    rng = random.Random(seed)
    samples = []
    for _ in range(3000):
        draw = rng.choices(rows, k=len(rows))
        baseline = sum(r["nearest_value"] for r in draw)
        if baseline:
            samples.append(sum(r["trial_value"] for r in draw) / baseline)
    samples.sort()
    return [samples[int(len(samples) * 0.025)], samples[int(len(samples) * 0.975)]] if samples else None


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("trial", type=Path)
    ap.add_argument("nearest", type=Path)
    ap.add_argument("output", type=Path)
    ap.add_argument("--duel", type=Path)
    a = ap.parse_args()
    root = Path(__file__).resolve().parents[2]
    source = json.loads((root / "testing/farm_route_data.json").read_text())
    reward = {r["id"]: r["stat"] for r in source["rewards"]}
    weight = {b["id"]: b["weights"] for pair in source["buildGoals"].values() for b in pair}
    focus = {"Fire": "damage", "Sentenced": "attackSpeed", "Plague": "ap",
             "Frost": "attackSpeed", "Holy": "attackSpeed"}
    def value(row):
        return sum(n * max(0.25, weight[row["build_id"]].get(reward[mob], 0))
                   for mob, n in row["stacks"])
    trial, nearest = rows(a.trial), rows(a.nearest)
    if trial.keys() != nearest.keys():
        raise ValueError("arms have different seed/minute keys")
    pairs = []
    duel_inputs = []
    for (seed, minute), (t, n) in ((key, (trial[key], nearest[key])) for key in sorted(trial)):
        for field in ("seed", "minute", "seat", "faction", "build_id", "map_hash"):
            if t[field] != n[field]:
                raise ValueError(f"unmatched {field} at {seed}/{minute}")
        if not (t["reached"] and n["reached"]):
            continue
        row = dict(seed=seed, minute=minute, faction=t["faction"], build=t["build_id"],
                   trial_arm=t["arm"], trial_alive=t["alive"], nearest_alive=n["alive"],
                   trial_value=value(t), nearest_value=value(n),
                   trial_stacks=sum(x[1] for x in t["stacks"]),
                   nearest_stacks=sum(x[1] for x in n["stacks"]),
                   trial_clears=t["camp_clears"], nearest_clears=n["camp_clears"],
                   trial_moving_s=t["moved_ticks"] * 0.025,
                   nearest_moving_s=n["moved_ticks"] * 0.025, duel_score=None)
        focus_weight = max(0.25, weight[t["build_id"]].get(focus[t["faction"]], 0))
        row["trial_focus_bonus_kills"] = t.get("focus_bonus_kills", 0)
        row["nearest_focus_bonus_kills"] = n.get("focus_bonus_kills", 0)
        row["trial_base_value"] = row["trial_value"] - 3 * row["trial_focus_bonus_kills"] * focus_weight
        row["nearest_base_value"] = row["nearest_value"] - 3 * row["nearest_focus_bonus_kills"] * focus_weight
        row["trial_bonus_weight"] = row["trial_focus_bonus_kills"] * focus_weight
        row["nearest_bonus_weight"] = row["nearest_focus_bonus_kills"] * focus_weight
        pairs.append(row)
        if t["alive"] and n["alive"] and a.duel:
            duel_inputs.append(dict(id=f"{seed}/{minute}", seed=seed,
                faction=t["faction"], weapon=WEAPON[t["build_id"]].capitalize(),
                companion=t["build_id"] == "plague:B",
                target={"stacks":t["stacks"],"clears":t["camp_clears"]},
                other={"stacks":n["stacks"],"clears":n["camp_clears"]}))
    if duel_inputs:
        outcomes = run_mirrors(duel_inputs, a.duel)
        for row in pairs:
            key = f"{row['seed']}/{row['minute']}"
            if key in outcomes:
                row["duel_score"] = statistics.mean(SCORES[x] for x in outcomes[key]["with_wishes"])
    summary = []
    for minute in (5, 10, 15, 20):
        groups = ("all", *sorted({r["faction"] for r in pairs}), *sorted({r["build"] for r in pairs}))
        for group in groups:
            selected = [r for r in pairs if r["minute"] == minute and group in ("all", r["faction"], r["build"])]
            if not selected:
                continue
            ratios = [r["trial_value"] / r["nearest_value"] for r in selected if r["nearest_value"] > 0]
            total_n = sum(r["nearest_value"] for r in selected)
            scores = [r["duel_score"] for r in selected if r["duel_score"] is not None]
            bt = sum(r["trial_base_value"] for r in selected)
            bn = sum(r["nearest_base_value"] for r in selected)
            et = sum(r["trial_bonus_weight"] for r in selected)
            en = sum(r["nearest_bonus_weight"] for r in selected)
            denom = et - 1.375 * en
            fitted_bonus = (1.375 * bn - bt) / denom if abs(denom) > 1e-9 else None
            summary.append(dict(minute=minute, group=group, assigned=len(selected),
                trial_alive=sum(r["trial_alive"] for r in selected),
                nearest_alive=sum(r["nearest_alive"] for r in selected),
                both_alive=sum(r["trial_alive"] and r["nearest_alive"] for r in selected),
                weighted_value_ratio=sum(r["trial_value"] for r in selected) / total_n if total_n else None,
                weighted_value_ratio_boot_interval=pooled_ratio_interval(selected),
                paired_ratio_median=statistics.median(ratios) if ratios else None,
                paired_ratio_boot_interval=interval(ratios, statistics.median),
                value_better=sum(r["trial_value"] > r["nearest_value"] for r in selected),
                value_tied=sum(r["trial_value"] == r["nearest_value"] for r in selected),
                value_worse=sum(r["trial_value"] < r["nearest_value"] for r in selected),
                mean_delta_stacks=statistics.mean(r["trial_stacks"] - r["nearest_stacks"] for r in selected),
                mean_delta_clears=statistics.mean(r["trial_clears"] - r["nearest_clears"] for r in selected),
                mean_delta_moving_s=statistics.mean(r["trial_moving_s"] - r["nearest_moving_s"] for r in selected),
                trial_focus_bonus_kills=sum(r["trial_focus_bonus_kills"] for r in selected),
                nearest_focus_bonus_kills=sum(r["nearest_focus_bonus_kills"] for r in selected),
                base_trial_value=bt, base_nearest_value=bn,
                bonus_weight_trial=et, bonus_weight_nearest=en,
                fitted_bonus_for_1_375=fitted_bonus,
                duel_pairs=len(scores), duel_score=statistics.mean(scores) if scores else None,
                duel_boot_interval=interval(scores)))
    a.output.parent.mkdir(parents=True, exist_ok=True)
    a.output.write_text(json.dumps({"summary": summary, "pairs": pairs}, indent=2) + "\n")


if __name__ == "__main__":
    main()
