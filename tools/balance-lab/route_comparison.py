"""Compare paired active-raid snapshots using the release camp_bench farm mirrors.

Usage: python3 tools/balance-lab/route_comparison.py snapshots.jsonl target/release/camp_bench OUTPUT
"""

import argparse
import csv
import json
import random
import statistics
import subprocess
from collections import defaultdict
from pathlib import Path

# Signature weapon and commander setup from testing/farm_route_proof.py.
WEAPON = {
    "fire:A": "sword", "fire:B": "shield", "sentenced:A": "sword", "sentenced:B": "bow",
    "plague:A": "wand", "plague:B": "wand", "frost:A": "shield", "frost:B": "bow",
    "holy:A": "axe", "holy:B": "wand",
}
ARMS = ("target", "nearest", "random5")
SCORES = {"target": 1.0, "other": 0.0, "simultaneous": 0.5, "timeout": 0.5}


def write_csv(path, rows):
    with path.open("w", newline="") as output:
        writer = csv.DictWriter(output, fieldnames=rows[0], lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)


def fixed_budget(stacks, budget):
    """Largest-remainder allocation. Break exact ties by mob identifier."""
    total = sum(n for _, n in stacks)
    if not 0 < budget <= total:
        raise ValueError("equal-stack comparison requires two nonempty kits")
    parts = [(mob, *divmod(n * budget, total)) for mob, n in sorted(stacks)]
    extra = budget - sum(whole for _, whole, _ in parts)
    bonus = {mob for mob, _, _ in sorted(parts, key=lambda item: (-item[2], item[0]))[:extra]}
    return [[mob, whole + int(mob in bonus)] for mob, whole, _ in parts if whole or mob in bonus]


def paired_inputs(path, target_arm):
    snapshots = defaultdict(dict)
    for line in path.read_text().splitlines():
        row = json.loads(line)
        key = row["seed"], row["minute"]
        if row["arm"] in snapshots[key]:
            raise ValueError(f"duplicate snapshot {key}, {row['arm']}")
        snapshots[key][row["arm"]] = row

    cases, inputs = [], []
    for (seed, minute), arms in sorted(snapshots.items()):
        if not set(ARMS).issubset(arms) or not set(arms).issubset({*ARMS, "live", "local-target"}):
            raise ValueError(f"incomplete arms for {seed}, minute {minute}")
        if target_arm not in arms:
            continue
        target = arms[target_arm]
        comparisons = [arm for arm in (*ARMS, "live") if arm != target_arm and arm in arms]
        for arm in comparisons:
            other = arms[arm]
            for name in ("map_hash", "seat", "faction", "build_id"):
                if target[name] != other[name]:
                    raise ValueError(f"unpaired {name}: seed {seed}, minute {minute}")
            def status(row):
                return "ended" if not row["reached"] else ("alive" if row["alive"] else "eliminated")
            case_id = f"{seed}-{minute}-{arm}"
            case = dict(id=case_id, strategy=target_arm, seed=seed, minute=minute, faction=target["faction"],
                        build=target["build_id"], comparator=arm, target_status=status(target),
                        other_status=status(other), target_stacks=None, other_stacks=None,
                        target_clears=None, other_clears=None, target_movement_u=None,
                        other_movement_u=None, target_moving_s=None, other_moving_s=None,
                        weapon_score=None, wish_score=None,
                        weapon_verdicts=None, wish_verdicts=None, landed_wishes=None, mirror_ttk_s=None,
                        matched_stacks=None, mix_weapon_score=None, mix_wish_score=None)
            cases.append(case)
            if case["target_status"] != "alive" or case["other_status"] != "alive":
                continue
            for label, row in (("target", target), ("other", other)):
                case[label + "_stacks"] = sum(n for _, n in row["stacks"])
                case[label + "_clears"] = row["camp_clears"]
                case[label + "_movement_u"] = row["movement_distance_u"]
                case[label + "_moving_s"] = row["moved_ticks"] * 0.025
            def kit(row):
                return {"stacks": row["stacks"], "clears": row["camp_clears"]}
            duel = dict(id=case_id, seed=seed, faction=target["faction"],
                               weapon=WEAPON[target["build_id"]].capitalize(), companion=target["build_id"] == "plague:B",
                               target=kit(target), other=kit(other))
            inputs.append(duel)
            budget = min(case["target_stacks"], case["other_stacks"])
            if budget > 0:
                # Match the number of clears too, to isolate the stat mixture for Demon Blood.
                clears = min(case["target_clears"], case["other_clears"])
                case["matched_stacks"] = budget
                inputs.append(dict(duel, id=case_id + "/mix",
                                   target={"stacks": fixed_budget(target["stacks"], budget), "clears": clears},
                                   other={"stacks": fixed_budget(other["stacks"], budget), "clears": clears}))
    return cases, inputs


def run_mirrors(inputs, binary):
    data = "".join(json.dumps(pair) + "\n" for pair in inputs)
    result = subprocess.run([str(binary.resolve()), "--pvp", "--farm-mirrors"],
                            input=data, text=True, stdout=subprocess.PIPE, check=True)
    rows = [json.loads(line) for line in result.stdout.splitlines()]
    by_id = {r["id"]: r for r in rows}
    if len(rows) != len(inputs) or set(by_id) != {p["id"] for p in inputs}:
        raise ValueError("mirror did not emit one outcome per paired snapshot")
    return by_id


def summarize(rows):
    groups = defaultdict(list)
    for row in rows:
        for name in ("all", row["faction"], row["build"]):
            groups[name, row["minute"], row["comparator"]].append(row)
    for (group, minute, arm), cases in sorted(groups.items()):
        paired = [r for r in cases if r["wish_score"] is not None]
        scores = [r["wish_score"] for r in paired]
        one_side = lambda who: sum(r[who + "_status"] == "alive" and
                                   r[("other" if who == "target" else "target") + "_status"] == "eliminated"
                                   for r in cases)
        out = dict(group=group, minute=minute, comparator=arm, assigned=len(cases),
                   paired_alive=len(paired), target_only_alive_both_reached=one_side("target"),
                   other_only_alive_both_reached=one_side("other"),
                   either_run_ended=sum("ended" in (r["target_status"], r["other_status"]) for r in cases))
        for col in ("weapon_score", "wish_score"):
            out[col] = statistics.mean(r[col] for r in paired) if paired else None
        timed = sorted(r["mirror_ttk_s"] for r in paired if r["mirror_ttk_s"] is not None)
        out["mirror_ttk_maps"] = len(timed)
        out["median_mirror_ttk_s"] = statistics.median(timed) if timed else None
        mix = [r for r in paired if r["mix_wish_score"] is not None]
        out["mix_pairs"] = len(mix)
        for col in ("mix_weapon_score", "mix_wish_score"):
            out[col] = statistics.mean(r[col] for r in mix) if mix else None
        # Both seating orientations belong to the same seed. Small groups are reported without intervals.
        ci = [None, None]
        if len(scores) >= 5:
            rng = random.Random(9433)
            samples = sorted(statistics.mean(rng.choices(scores, k=len(scores))) for _ in range(2000))
            ci = samples[49], samples[1949]
        out["wish_seed_boot_low"], out["wish_seed_boot_high"] = ci
        out["wish_timeout_exchanges"] = sum(r["wish_verdicts"].split("|").count("timeout") for r in paired)
        out["wish_simultaneous_exchanges"] = sum(r["wish_verdicts"].split("|").count("simultaneous") for r in paired)
        for key in ("stacks", "clears", "movement_u", "moving_s"):
            out["median_paired_delta_" + key] = statistics.median(
                r["target_" + key] - r["other_" + key] for r in paired) if paired else None
        yield out


def raid_outcomes(path, target_arm):
    """Compare focal placement on every paired map with two resolved seats."""
    seeds = defaultdict(dict)
    for line in path.read_text().splitlines():
        row = json.loads(line)
        arms = seeds[row["seed"]]
        if row["arm"] in arms:
            raise ValueError(f"duplicate match result: {row['arm']}, seed {row['seed']}")
        arms[row["arm"]] = row["result"]
    for comparator in (*ARMS, "live"):
        if comparator == target_arm:
            continue
        cohort = [m for m in seeds.values() if target_arm in m and comparator in m]
        if not cohort:
            continue
        pairs = [(m[target_arm], m[comparator]) for m in cohort
                 if m[target_arm] is not None and m[comparator] is not None]
        delta = [b["place"] - a["place"] for a, b in pairs]
        yield dict(strategy=target_arm, comparator=comparator, assigned=len(cohort),
                   both_resolved=len(pairs), target_better=sum(d > 0 for d in delta),
                   same_place=sum(d == 0 for d in delta), other_better=sum(d < 0 for d in delta),
                   target_first=sum(a["place"] == 1 for a, _ in pairs),
                   other_first=sum(b["place"] == 1 for _, b in pairs),
                   mean_places_gained=statistics.mean(delta) if delta else None)


def main():
    args = argparse.ArgumentParser(description=__doc__)
    args.add_argument("snapshots", type=Path)
    args.add_argument("camp_bench", type=Path)
    args.add_argument("output", type=Path)
    args.add_argument("--target-arm", choices=("target", "local-target"), default="target")
    args.add_argument("--resolutions", type=Path, help="one final focal result per arm and seed, as emitted to stderr")
    a = args.parse_args()
    cases, inputs = paired_inputs(a.snapshots, a.target_arm)
    if not cases:
        args.error("no complete paired snapshots for the requested target arm")
    results = run_mirrors(inputs, a.camp_bench)
    a.output.mkdir(parents=True, exist_ok=True)
    for case in cases:
        if case["id"] in results:
            row = results[case["id"]]
            case["landed_wishes"] = sum(row["landed_wishes"])
            times = [t / 1000 for t in row["victim_contact_to_death_ms"] if t is not None]
            case["mirror_ttk_s"] = statistics.mean(times) if times else None
            for label, key in (("weapon", "weapon_only"), ("wish", "with_wishes")):
                case[label + "_score"] = statistics.mean(SCORES[v] for v in row[key])
                case[label + "_verdicts"] = "|".join(row[key])
                mix = results.get(case["id"] + "/mix")
                if mix:
                    case["mix_" + label + "_score"] = statistics.mean(SCORES[v] for v in mix[key])
    write_csv(a.output / "route_pairs.csv", cases)
    write_csv(a.output / "route_summary.csv", list(summarize(cases)))
    if a.resolutions:
        write_csv(a.output / "placements.csv", list(raid_outcomes(a.resolutions, a.target_arm)))


if __name__ == "__main__":
    main()
