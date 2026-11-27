#!/usr/bin/env python3
"""Summarize stock augment field exposure and check final hashes against the quest cohort."""
import argparse
import collections
import csv
import json
import statistics
from pathlib import Path


# Units follow the native card tables (rh-sim/src/cards/native/{weapon,zone,faction}.rs).
READOUT_UNITS = {
    "marksmansTally": ("outgoing damage share",),
    "bastion": ("mitigated damage stored",),
    "calluses": ("armor",),
    "executionersLedger": ("Offense",),
    "eighthStrike": ("hits since last doubled hit",),
    "pathfinder": ("move-speed share", "distinct normal mob types"),
    "hazardWalker": ("stacks", "ember exposure ms", "bog exposure ms", "ice exposure ms"),
    "wishEngine": ("wish cooldown reduction share",),
    "corpseHoarder": ("max-HP share",),
    "stoked": ("Special Offense",),
    "glacier": ("armor",),
    "trophyHunter": ("outgoing damage share",),
}


def load_traces(paths):
    header = None
    matches = {}
    for path in paths:
        with path.open() as stream:
            info = json.loads(next(stream))
            assert header is None or header == info, path
            assert info["schema"] == "augment-field/1"
            header = info
            for line in stream:
                row = json.loads(line)
                assert row["seed"] not in matches
                matches[row["seed"]] = row
    return header, matches


def verify(matches, baseline):
    with baseline.open() as stream:
        next(stream)
        other = {row["seed"]: row for row in map(json.loads, stream)}
    fields = ("ticks", "state_hash", "terminated", "truncated")
    for seed, match in matches.items():
        assert all(match[k] == other[seed][k] for k in fields), seed


def median(values):
    return round(statistics.median(values), 3) if values else None


def growth_rows(card, holders):
    grouped = collections.defaultdict(list)
    for h in holders:
        for i, row in enumerate(h["rows"]):
            grouped[i, row["kind"]].append((h, row))
    return [{
        "kind": kind,
        "unit": READOUT_UNITS[card][i],
        "readout_holders": len(samples),
        "changed": sum(row["rises"] != 0 or row["falls"] != 0 for _, row in samples),
        "rose": sum(row["rises"] > 0 for _, row in samples),
        "fell": sum(row["falls"] > 0 for _, row in samples),
        "peaked_at_cap": sum(row["peak"] == row["cap"] for _, row in samples),
        "rise_steps": sum(row["rises"] for _, row in samples),
        "fall_steps": sum(row["falls"] for _, row in samples),
        "median_start": median([row["start"] for _, row in samples]),
        "median_peak": median([row["peak"] for _, row in samples]),
        "peak_max": round(max(row["peak"] for _, row in samples), 3),
        "median_last_before_end": median([row["last"] for _, row in samples]),
        "cap": samples[0][1]["cap"],
    } for (i, kind), samples in sorted(grouped.items())]


def summarize(meta, matches, catalog):
    names = {entry["id"]: entry["name"] for entry in catalog["augments"]}
    drop_rows = [drop for trace in matches.values() for drop in trace["drops"]]
    visible = [drop["seen"] for drop in drop_rows if drop["seen"] is not None]
    offered = collections.Counter(o for chest in visible for o in chest["options"])
    eligible = collections.Counter(
        o for chest in visible if chest["could_choose"] for o in chest["options"]
    )
    picks = collections.Counter(
        (p["source"], p["option"]) for trace in matches.values() for p in trace["picks"]
    )
    holders_by_card = collections.defaultdict(list)
    for trace in matches.values():
        for holder in trace["growth"]:
            holders_by_card[holder["card"]].append(holder)
    rows = []
    for card, name in names.items():
        all_holders = holders_by_card[card]
        elite_holders = [h for h in all_holders if h["source"] == "elite"]
        rows.append({
            "id": card, "name": name, "native": card in meta["native"],
            "elite_offered": offered[card],
            "elite_offered_with_sampled_chooser": eligible[card],
            "elite_picked": picks["elite", card],
            "other_picked": sum(picks[s, card] for s in ("other", "trophy")),
            "elite_holders": len(elite_holders),
            "other_holders": sum(h["source"] == "other" or h["source"] == "trophy"
                                 for h in all_holders),
            "unresolved_holders": sum(h["source"] == "unresolved" for h in all_holders),
            "elite_holder_standing_s": round(sum(h["standing_s"] for h in elite_holders), 3),
            "elite_median_standing_s": median([h["standing_s"] for h in elite_holders]),
            "elite_median_acquired_s": median([h["acquired_s"] for h in elite_holders]),
            "elite_no_longer_held": sum(not h["still_held"] for h in elite_holders),
            "native_rows_from_elite": growth_rows(card, elite_holders),
        })
    return {
        "content_hash": meta["content_hash"], "seeds": sorted(matches),
        "expected_elite_drops": len(drop_rows),
        "observed_elite_drops": len(visible),
        "observed_elite_drops_with_chooser": sum(bool(c["could_choose"]) for c in visible),
        "all_picks_by_source": dict(collections.Counter({
            source: sum(count for (kind, _), count in picks.items() if kind == source)
            for source in sorted({kind for (kind, _) in picks})
        })),
        "elite_card_offers": sum(offered.values()),
        "elite_named_card_picks": sum(picks["elite", card] for card in names),
        "elite_observed_options_outside_catalog": {
            card: n for card, n in sorted(offered.items()) if card not in names
        },
        "cards": rows,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("traces", nargs="+", type=Path)
    parser.add_argument("--catalog", required=True, type=Path)
    parser.add_argument("--baseline", required=True, type=Path)
    parser.add_argument("--csv", type=Path)
    parser.add_argument("--expect-seeds", help="required half-open seed range, e.g. 1..81")
    args = parser.parse_args()
    meta, matches = load_traces(args.traces)
    verify(matches, args.baseline)
    if args.expect_seeds:
        start, end = map(int, args.expect_seeds.split(".."))
        if sorted(matches) != list(range(start, end)):
            parser.error("trace files do not cover the requested seed range")
    matches = dict(sorted(matches.items()))
    report = summarize(meta, matches, json.loads(args.catalog.read_text()))
    if args.csv:
        with args.csv.open("w", newline="") as output:
            writer = csv.DictWriter(output, fieldnames=report["cards"][0])
            writer.writeheader()
            for row in report["cards"]:
                writer.writerow({**row, "native_rows_from_elite": json.dumps(
                    row["native_rows_from_elite"], separators=(",", ":")
                )})
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
