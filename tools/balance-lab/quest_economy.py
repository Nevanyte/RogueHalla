"""Price stock quest route time against measured normal-camp clear credits."""
import argparse
from collections import defaultdict
import csv
import json
from pathlib import Path
from statistics import median, quantiles


# Explicit scenario inputs for routes the stock policy does not play. Each range is
# marginal action time (seconds), not an objective deadline. See the evidence ledger.
REFERENCE_NORMAL_CLEARS_PER_MINUTE = 1.5
DESIGNED_ROUTES = {
    "the_warden": (65, 130, 0),
    "bounty_hunter": (30, 90, 0),
    "the_burning_crown": (55, 90, 0),
    "elite_slayer": (25, 65, 0),
    "a_bone_to_pick": (35, 65, 0),
    "feed_or_hunt": (30, 65, 0),
    "rime_hunt": (35, 80, 1),
    "whispers_in_the_brush": (20, 45, 0),
    "gatecrasher": (20, 60, 0),
}


def read(paths, baseline):
    matches = []
    meta = None
    seen = set()
    for path in paths:
        with path.open() as stream:
            header = json.loads(next(stream))
            if meta is None:
                meta = header
            if header != meta:
                raise ValueError(f"mixed quest configurations: {path}")
            for line in stream:
                row = json.loads(line)
                seed = row["seed"]
                if seed in seen:
                    raise ValueError(f"duplicate seed {seed}")
                if baseline and row["state_hash"] != baseline[seed]:
                    raise ValueError(f"baseline observer changed the terminal state at seed {seed}")
                seen.add(seed)
                matches.append(row)
    return meta, sorted(matches, key=lambda row: row["seed"])


def overlap(leg, start, end, tick_s):
    return max(0., min(leg["end_s"] + tick_s, end) - max(start, leg["start_s"]))


def measure(meta, matches, rate_matches):
    rates = defaultdict(lambda: [0, 0])
    for m in rate_matches:
        for band, (ticks, clears) in enumerate(m["normal_outside_bands"]):
            r = rates[m["faction"], band]
            r[0] += ticks
            r[1] += clears

    def rate(faction, band, tick):
        ticks, clears = rates[faction, band]
        if ticks == 0:
            return None
        return clears / (ticks * tick)

    observations = defaultdict(list)
    failed_normal_equivalents = defaultdict(float)
    for m in matches:
        paid_by_quest = {
            a["quest"]: a for a in m["attempts"]
            if a["reward"] in ("received", "equipped_after_choice")
        }
        tick = m["tick_ms"] / 1000
        legs = defaultdict(list)
        for leg in m["quest_goal_legs"]:
            legs[leg["quest"]].append(leg)
        for quest in meta["catalog"]:
            enabled = legs[quest["id"]]
            paid = paid_by_quest.get(quest["quest"])
            if not enabled:
                continue
            stop = paid["settled_s"] + tick if paid else float("inf")
            approach = objective = gross = 0.
            concurrent = 0
            for leg in enabled:
                end = min(leg["end_s"] + tick, stop)
                if end <= leg["start_s"]:
                    continue
                if paid:
                    approach += overlap(leg, 0., min(end, paid["accept_s"]), tick)
                    objective += overlap(leg, paid["accept_s"], end, tick)
                if end >= leg["end_s"] + tick:
                    concurrent += leg["normal_clears"]
                boundaries = (0., 300., 600., end)
                for band in range(3):
                    start_t, end_t = boundaries[band:band + 2]
                    duration = overlap(leg, start_t, min(end_t, end), tick)
                    if duration > 0:
                        gross += duration * (rate(m["faction"], band, tick) or 0.)
            net = max(0., gross - concurrent)
            if paid:
                observations[quest["id"]].append(
                    (approach, objective, concurrent, net, paid["max_progress"])
                )
            else:
                failed_normal_equivalents[quest["id"]] += net

    for q in meta["catalog"]:
        trials = observations[q["id"]]
        failed = failed_normal_equivalents[q["id"]]
        measured_costs = [row[3] for row in trials]
        if len(measured_costs) > 1:
            lo, _, hi = quantiles(measured_costs, n=4, method="inclusive")
            cost_basis = "field_iqr"
        elif measured_costs:
            lo = hi = measured_costs[0]
            cost_basis = "single_field_sample"
        elif q["id"] in DESIGNED_ROUTES:
            start, end, concurrent = DESIGNED_ROUTES[q["id"]]
            rate_per_s = REFERENCE_NORMAL_CLEARS_PER_MINUTE / 60
            lo, hi = max(0., start * rate_per_s - concurrent), max(0., end * rate_per_s - concurrent)
            cost_basis = "designed_scenario"
        else:
            lo = hi = None
            cost_basis = "unavailable"
        stages = [sum(row[4] == stage for row in trials) for stage in (1, 2, 3)]
        stage_three = [row[3] for row in trials if row[4] == 3]

        def mid(i):
            return round(median(row[i] for row in trials), 3) if trials else None
        yield dict(
            quest=q["id"], reward=q["reward"], observations=len(trials),
            approach_s_p50=mid(0), objective_s_p50=mid(1), route_s_p50=round(
                median(row[0] + row[1] for row in trials), 3) if trials else None,
            concurrent_normal_clears=sum(row[2] for row in trials),
            cost_basis=cost_basis,
            net_cost_range_low_ce=round(lo, 3) if lo is not None else None,
            net_cost_range_high_ce=round(hi, 3) if hi is not None else None,
            route_cost_normal_camps_p50=mid(3),
            unsuccessful_only_route_cost_total=round(failed, 3),
            expected_cost_per_paid_camp_mean=round(
                (sum(row[3] for row in trials) + failed) / len(trials), 3
            ) if trials else None,
            trade_up_cashouts="/".join(map(str, stages)) if q["id"] == "trade_up" else None,
            trade_up_top_route_cost_mean=round(
                sum(stage_three) / len(stage_three), 3
            ) if q["id"] == "trade_up" and stage_three else None,
        )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("routes", nargs="+", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--baseline", type=Path, help="verify exact state hashes against a stock trace")
    parser.add_argument("--rate-traces", nargs="+", type=Path, help="use a separate normal-farming reference cohort")
    args = parser.parse_args()
    baseline = {}
    if args.baseline:
        with args.baseline.open() as stream:
            next(stream)
            baseline = {row["seed"]: row["state_hash"] for row in map(json.loads, stream)}
    meta, matches = read(args.routes, baseline)
    _, reference = read(args.rate_traces, {}) if args.rate_traces else (meta, matches)
    result = list(measure(meta, matches, reference))
    with args.output.open("w", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=result[0], lineterminator="\n")
        writer.writeheader()
        writer.writerows(result)


if __name__ == "__main__":
    main()
