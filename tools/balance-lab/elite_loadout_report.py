#!/usr/bin/env python3
"""Summarize named elite chest card pickups at raid-age checkpoints.

Input is augment-field/1 JSONL emitted by rh-headless's augment_field binary.
This uses strategy-field-ttk resolved records to exclude seats whose elimination
preceded each checkpoint. Resolution duration is rounded to whole seconds, so
seats equal to a checkpoint are classified as ambiguous and excluded from the
unresolved-seat counts. The trace does not distinguish standing from downed.
"""

import argparse
import csv
import json
import math
from pathlib import Path


CHECKPOINTS = (5, 10, 15, 20)


def quantile(values, p):
    """Nearest rank: ceil(p*n)-1, with the minimum observation at small n."""
    if not values:
        return ""
    ordered = sorted(values)
    return ordered[max(0, math.ceil(len(ordered) * p) - 1)]


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


def load_field(paths):
    matches = {}
    for path in paths:
        document = json.loads(path.read_text())
        if document.get("schema") != "strategy-field-ttk/1":
            raise ValueError(f"expected strategy-field-ttk/1 in {path}")
        for match in document["match_results"]:
            seed = match["seed"]
            if seed in matches:
                raise ValueError(f"duplicate strategy-field seed {seed}")
            records = match["resolved"]
            seats = [record["seat"] for record in records]
            if any(seat is None for seat in seats) or len(seats) != len(set(seats)):
                raise ValueError(f"seed {seed} has missing or duplicate resolved seat indices")
            if not seats or sorted(seats) != list(range(len(seats))):
                raise ValueError(f"seed {seed} resolved seats are not a contiguous roster")
            matches[seed] = {record["seat"]: record["duration_s"] for record in records}
    if not matches:
        raise ValueError("no strategy-field matches found")
    roster_sizes = {len(durations) for durations in matches.values()}
    if len(roster_sizes) != 1:
        raise ValueError(f"inconsistent roster sizes: {sorted(roster_sizes)}")
    return matches, roster_sizes.pop()


def report(rows, resolution, roster_size):
    missing = sorted(row["seed"] for row in rows if row["seed"] not in resolution)
    if missing:
        raise ValueError(f"strategy field data missing augment seeds: {missing}")
    result = []
    for minute in CHECKPOINTS:
        cutoff = minute * 60
        eligible = []
        for row in rows:
            # Growth is sampled every active step for cards still in a build.
            # Its final ended_s is the last observed raid-age sample; unlike
            # progress.tick, it uses the raid-start-relative simulation clock.
            observed_until = max((g["ended_s"] for g in row["growth"]), default=0)
            if observed_until >= cutoff and row["seed"] in resolution:
                eligible.append(row)

        all_seat_counts = []
        unresolved_seat_counts = []
        machine_gun = set()
        committed = set()
        both = set()
        resolved_before = 0
        unresolved = 0
        ambiguous = 0
        named_elite_picks = 0
        unresolved_named_picks = 0
        unresolved_picks = 0
        for row in eligible:
            seed = row["seed"]
            participant_ids = {
                item["seat"] for item in row["growth"]
            } | {item["seat"] for item in row["picks"]}
            if any(not 1 <= player_id <= roster_size for player_id in participant_ids):
                raise ValueError(f"seed {seed} has a PlayerId outside the resolved roster")
            by_seat = {seat: [] for seat in range(roster_size)}
            for pick in row["picks"]:
                if pick["age_s"] > cutoff:
                    continue
                if pick["source"] == "unresolved":
                    unresolved_picks += 1
                    continue
                if pick["source"] != "elite" or not pick["option"]:
                    continue
                named_elite_picks += 1
                # augment_field stores the PlayerId; the roster assigns ids
                # in join order starting at 1. ResolvedRecord.seat is the
                # zero-based position in that same sim.players() ordering.
                seat = pick["seat"] - 1
                if not 0 <= seat < roster_size:
                    raise ValueError(f"seed {seed} pickup PlayerId {pick['seat']} outside roster")
                by_seat[seat].append(pick["option"])
            for seat, cards in by_seat.items():
                all_seat_counts.append(len(cards))
                duration = resolution[seed][seat]
                if duration < cutoff:
                    resolved_before += 1
                    continue
                if duration == cutoff:
                    # duration_sec is JS-rounded at emission, so a value
                    # equal to the checkpoint cannot safely order the events.
                    ambiguous += 1
                    continue
                unresolved += 1
                unresolved_seat_counts.append(len(cards))
                key = (seed, seat)
                if cards:
                    unresolved_named_picks += len(cards)
                has_mg = "machineGun" in cards
                has_committed = "committed" in cards
                if has_mg:
                    machine_gun.add(key)
                if has_committed:
                    committed.add(key)
                if has_mg and has_committed:
                    both.add(key)

        all_n = len(all_seat_counts)
        seat_with_any = sum(count > 0 for count in all_seat_counts)
        result.append({
            "raid_age_minute": minute,
            "cutoff_s": cutoff,
            "seeds_observed_to_cutoff": len(eligible),
            "roster_size": roster_size,
            "all_roster_seat_observations": all_n,
            "resolved_before_cutoff_seats": resolved_before,
            "unresolved_seats_after_cutoff": unresolved,
            "duration_rounding_ambiguous_seats": ambiguous,
            "named_elite_card_picks": named_elite_picks,
            "all_roster_picks_per_seat_median": quantile(all_seat_counts, 0.5),
            "all_roster_picks_per_seat_p25": quantile(all_seat_counts, 0.25),
            "all_roster_picks_per_seat_p75": quantile(all_seat_counts, 0.75),
            "all_roster_seats_with_any_named_elite_pick": seat_with_any,
            "unresolved_named_elite_card_picks": unresolved_named_picks,
            "unresolved_picks_per_seat_median": quantile(unresolved_seat_counts, 0.5),
            "unresolved_picks_per_seat_p25": quantile(unresolved_seat_counts, 0.25),
            "unresolved_picks_per_seat_p75": quantile(unresolved_seat_counts, 0.75),
            "unresolved_seats_with_machine_gun": len(machine_gun),
            "unresolved_machine_gun_pct": round(100 * len(machine_gun) / unresolved, 2) if unresolved else "",
            "unresolved_seats_with_committed": len(committed),
            "unresolved_committed_pct": round(100 * len(committed) / unresolved, 2) if unresolved else "",
            "unresolved_seats_with_both": len(both),
            "unresolved_both_pct": round(100 * len(both) / unresolved, 2) if unresolved else "",
            "unnamed_or_source_unresolved_pick_events_excluded": unresolved_picks,
            "status_limit": "duration_s is rounded; unresolved is not a standing/downed distinction",
        })
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path, help="augment-field/1 JSONL cohort")
    parser.add_argument("field", type=Path, help="field-ttk.json or a directory of field-final-* shards")
    parser.add_argument("output", type=Path, help="checkpoint CSV destination")
    args = parser.parse_args()
    _, rows = load(args.input)
    paths = [args.field] if args.field.is_file() else sorted(args.field.glob("field-final-*/field-ttk.json"))
    resolution, roster_size = load_field(paths)
    records = report(rows, resolution, roster_size)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=records[0].keys(), lineterminator="\n")
        writer.writeheader()
        writer.writerows(records)


if __name__ == "__main__":
    main()
