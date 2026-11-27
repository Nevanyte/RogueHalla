#!/usr/bin/env python3
"""Write the controlled camp_bench solo duelist results by elite kind and build stage."""
import argparse
from collections import Counter, defaultdict
import csv
import json
from pathlib import Path
import statistics

# elite_bench::config inherits the 50 ms SimConfig::live_content default.
TICK_MS = 50
CAMP_STACKS = 12
OUTCOMES = ("cleared", "died", "truncated", "no_engage")


def median_clear_seconds(clears, origin):
    if not clears:
        return ""
    ticks = [cell["ticks"] - origin(cell) for cell in clears]
    return round(statistics.median(ticks) * TICK_MS / 1000, 3)


def rows(report):
    if (report["mode"], report["fighter"], report["solo"]) != ("elite", "duelist", True):
        raise ValueError("expected camp_bench --elite --fighter duelist")
    groups = defaultdict(list)
    for cell in report["cells"]:
        groups[cell["kind"], cell["column"]].append(cell)
    for camp in report["per_camp"]:
        for totals in camp["columns"]:
            key = camp["kind"], totals["column"]
            trials = groups.pop(key)
            counts = Counter(cell["outcome"] for cell in trials)
            expected = dict(zip(OUTCOMES, (
                totals["cleared"], totals["deaths"], totals["truncated"], totals["no_engage"],
            )))
            if len(trials) != totals["samples"] or dict(counts) != {
                state: count for state, count in expected.items() if count
            }:
                raise ValueError(f"cell counts disagree with bench totals for {key}")
            if any(cell["weapon"] not in {"sword", "bow"} for cell in trials):
                raise ValueError(f"expected only sword and bow trials for {key}")
            cleared = [cell for cell in trials if cell["outcome"] == "cleared"]
            if any(not 0 < (cell["first_hit_tick"] or 0) <= cell["ticks"] for cell in cleared):
                raise ValueError(f"successful clear lacks a valid first dealt hit for {key}")
            banked = totals["camps"]
            row = {
                "elite_kind": camp["kind"],
                "zone": camp["zone"],
                "build_stage": totals["column"],
                "banked_normal_camps": banked,
                "banked_stacks": banked * CAMP_STACKS if banked is not None else "",
                "tick_ms": TICK_MS,
                "cap_s": report["max_ticks"] * TICK_MS / 1000,
                "samples": totals["samples"],
                "engaged": totals["engaged"],
                **expected,
                "clear_pct_of_engaged": percent(totals["clear_rate"]),
                "death_pct_of_engaged": percent(totals["death_rate"]),
                "death_pct_of_decided": percent(totals["decided_rate"]),
                "median_full_clear_s_from_start": median_clear_seconds(cleared, lambda _: 0),
                "median_full_clear_s_from_first_dealt_hit": median_clear_seconds(
                    cleared, lambda cell: cell["first_hit_tick"]
                ),
            }
            for weapon in ("sword", "bow"):
                counts = Counter(cell["outcome"] for cell in trials if cell["weapon"] == weapon)
                for outcome in OUTCOMES:
                    row[f"{weapon}_{outcome}"] = counts[outcome]
            yield row
    if groups:
        raise ValueError(f"unreported trial groups: {sorted(groups)}")


def percent(rate):
    return round(100 * rate, 2) if rate is not None else ""


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input_json", type=Path)
    parser.add_argument("output_csv", type=Path)
    args = parser.parse_args()
    result = list(rows(json.loads(args.input_json.read_text())))
    if not result:
        parser.error("input contains no trial rows")
    with args.output_csv.open("w", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=result[0], lineterminator="\n")
        writer.writeheader()
        writer.writerows(result)


if __name__ == "__main__":
    main()
