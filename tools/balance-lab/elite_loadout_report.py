#!/usr/bin/env python3
"""Summarize named elite chest card pickups at raid-age checkpoints.

Input is augment-field/1 JSONL emitted by rh-headless's augment_field binary.
This is cumulative at-pick concentration, not a survivor-only snapshot: that
trace does not record per-seat resolution times. See the README for that limit.
"""

import argparse
import csv
import json
import math
from pathlib import Path


CHECKPOINTS = (5, 10, 15, 20)
# balance_lab assigns the eight participant PlayerIds 1 through 8.
SEATS = range(1, 9)


def quantile(values, p):
    """Nearest-rank quantile, matching the balance-lab convention."""
    if not values:
        return ""
    ordered = sorted(values)
    return ordered[math.floor((len(ordered) - 1) * p + 0.5)]


def load(path):
    with path.open() as stream:
        header = json.loads(next(stream))
        if header.get("schema") != "augment-field/1":
            raise ValueError(f"expected augment-field/1, got {header.get('schema')!r}")
        if header.get("tick_ms") != 25:
            raise ValueError(f"expected 25 ms tick, got {header.get('tick_ms')!r}")
        rows = [json.loads(line) for line in stream if line.strip()]
    seeds = [row["seed"] for row in rows]
    if len(seeds) != len(set(seeds)):
        raise ValueError("input contains duplicate seeds")
    return header, rows


def report(rows):
    result = []
    for minute in CHECKPOINTS:
        cutoff = minute * 60
        eligible = []
        for row in rows:
            # Growth is sampled every active step for cards still in a build.
            # Its final ended_s is the last observed raid-age sample; unlike
            # progress.tick, it uses the raid-start-relative simulation clock.
            observed_until = max((g["ended_s"] for g in row["growth"]), default=0)
            if observed_until >= cutoff:
                eligible.append(row)

        seat_counts = []
        machine_gun = set()
        committed = set()
        both = set()
        named_elite_picks = 0
        unresolved_picks = 0
        for row in eligible:
            by_seat = {seat: [] for seat in SEATS}
            for pick in row["picks"]:
                if pick["age_s"] > cutoff:
                    continue
                if pick["source"] == "unresolved":
                    unresolved_picks += 1
                    continue
                if pick["source"] != "elite" or not pick["option"]:
                    continue
                named_elite_picks += 1
                by_seat[pick["seat"]].append(pick["option"])
            for seat, cards in by_seat.items():
                key = (row["seed"], seat)
                seat_counts.append(len(cards))
                has_mg = "machineGun" in cards
                has_committed = "committed" in cards
                if has_mg:
                    machine_gun.add(key)
                if has_committed:
                    committed.add(key)
                if has_mg and has_committed:
                    both.add(key)

        n_seats = len(seat_counts)
        seat_with_any = sum(count > 0 for count in seat_counts)
        result.append({
            "raid_age_minute": minute,
            "cutoff_s": cutoff,
            "seeds_observed_to_cutoff": len(eligible),
            "seat_observations": n_seats,
            "population_basis": "all roster participants; per-seat alive status unavailable",
            "named_elite_card_picks": named_elite_picks,
            "named_elite_picks_per_seat_median": quantile(seat_counts, 0.5),
            "named_elite_picks_per_seat_p25": quantile(seat_counts, 0.25),
            "named_elite_picks_per_seat_p75": quantile(seat_counts, 0.75),
            "seats_with_any_named_elite_pick": seat_with_any,
            "seats_with_any_named_elite_pick_pct": round(100 * seat_with_any / n_seats, 2) if n_seats else "",
            "machine_gun_seats": len(machine_gun),
            "machine_gun_seat_pct": round(100 * len(machine_gun) / n_seats, 2) if n_seats else "",
            "committed_seats": len(committed),
            "committed_seat_pct": round(100 * len(committed) / n_seats, 2) if n_seats else "",
            "both_machine_gun_and_committed_seats": len(both),
            "both_seat_pct": round(100 * len(both) / n_seats, 2) if n_seats else "",
            "unresolved_pick_events_excluded": unresolved_picks,
        })
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path, help="augment-field/1 JSONL cohort")
    parser.add_argument("output", type=Path, help="checkpoint CSV destination")
    args = parser.parse_args()
    _, rows = load(args.input)
    records = report(rows)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=records[0].keys())
        writer.writeheader()
        writer.writerows(records)


if __name__ == "__main__":
    main()
