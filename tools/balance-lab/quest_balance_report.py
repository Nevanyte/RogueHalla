#!/usr/bin/env python3
"""Summarize quest_balance JSONL and verify the matching strategy_ttk outcomes."""
import argparse
import collections
import csv
import glob
import json
import statistics
from itertools import chain
from pathlib import Path


# Entries absent from raid/exec/mod.rs (walk/dwell/decide are registered).
NO_QUEST_EXECUTOR = {
    "TheWarden", "BountyHunter", "TheBurningCrown", "EliteSlayer", "ABoneToPick",
    "FeedOrHunt", "RimeHunt", "WhispersInTheBrush", "Gatecrasher",
}

# Cast names from board/rewards/awards.rs. The cast policy dispatch table is raid/spells.rs.
SPELLS = {
    "WardensCage": ("Warden's Cage",),
    "Hook": ("Hook",),
    "ShoulderCharge": ("Shoulder Charge",),
    "Tornado": ("Tornado",),
    "Turret": ("Turret",),
    "RebirthEgg": ("Rebirth Egg",),
    "Warhorn": ("Warhorn",),
    "Smite": ("Smite",),
    "SpellShield": ("Spell Shield",),
    "ForgetMeNotSeed": ("Forget-Me-Not Seed",),
    "SwapStick": ("Swap-Stick",),
    "LanternWard": ("Lantern Ward",),
    "Cleanse": ("Cleanse",),
    "ScoutFalcon": ("Scout Falcon",),
    "Stasis": ("Stasis",),
    "Bolas": ("Bolas",),
    "WallBuster": ("WallBuster",),
    "BlindRageAndStillMind": ("BlindRage", "StillMind"),
}
CAST_RULES = {"Cleanse", "Smite", "Turret", "Shoulder Charge", "Hook", "Bolas"}


def load(paths):
    catalog = None
    matches = {}
    for path in paths:
        with open(path) as stream:
            header = json.loads(next(stream))
            assert header["schema"] == "quest-balance/1"
            assert catalog is None or header == catalog
            catalog = header
            for line in stream:
                match = json.loads(line)
                seed = match["seed"]
                assert seed not in matches, seed
                assert match["tick_ms"] == 25
                matches[seed] = match
    return catalog, matches


def verify_field(matches, paths):
    baseline = {}
    for path in sorted(set(chain.from_iterable(glob.glob(pattern) for pattern in paths))):
        report = json.loads(Path(path).read_text())
        for result in report["match_results"]:
            seed = result["seed"]
            assert seed not in baseline, seed
            baseline[seed] = result
    fields = ("ticks", "terminated", "truncated", "raid_ended_event")
    resolution_fields = ("bot", "outcome", "duration_s", "kills", "place")
    for seed, match in matches.items():
        result = baseline[seed]
        assert all(match[k] == result[k] for k in fields), seed
        expected = [
            {k: r[k] for k in resolution_fields}
            for r in result["resolved"] if r["seat"] == 0
        ]
        assert match["resolutions"] == expected, seed


def round_median(values):
    return round(statistics.median(values), 3) if values else None


def summarize(catalog, matches):
    wins = collections.Counter()
    seconds = collections.Counter()
    for match in matches.values():
        faction = match["faction"]
        wins[faction] += match["without_open_camps"]
        seconds[faction] += match["without_open_seconds"]
    # Stock credits include normal/elite clears, with credit to each nearby standing contributor.
    # Use the observed faction rate outside an open attempt as an elapsed-time yardstick.
    rates = {key: 60 * wins[key] / seconds[key] for key in seconds if seconds[key] > 0}
    detailed_attempts = [
        {**a, "seed": m["seed"], "faction": m["faction"]}
        for m in matches.values() for a in m["attempts"]
    ]
    casts = collections.Counter()
    casting_seeds = collections.defaultdict(set)
    for m in matches.values():
        for r in m["notices"]:
            if r["text"].startswith("Used "):
                spell = r["text"][len("Used "):]
            elif r["text"].startswith("Seed planted."):
                spell = "Forget-Me-Not Seed"
            else:
                continue
            casts[spell, r["locked"]] += 1
            casting_seeds[spell].add(m["seed"])
    rows = []
    for q in catalog["catalog"]:
        attempts = [a for a in detailed_attempts if a["quest"] == q["quest"]]
        accepted = len({a["seed"] for a in attempts})
        objective = [a for a in attempts if a["objective_s"] is not None]
        turn_in = [a for a in attempts if a["turn_in_s"] is not None]
        settled = [a for a in attempts if a["settled_s"] is not None]
        claimed = [a for a in settled if a["reward"] in {"received", "equipped_after_choice"}]
        paid_spans = [a["settled_s"] - a["accept_s"] for a in claimed]
        lost = [a for a in attempts if a["outcome"] in {"timer", "death", "lost"}]
        open_attempts = [a for a in attempts if a["outcome"] in {"open", "pending"}]
        priced = [a for a in claimed if a["faction"] in rates]
        ce = [(a["settled_s"] - a["accept_s"]) * rates[a["faction"]] / 60 for a in priced]
        spell_names = SPELLS.get(q["reward"], ())
        rows.append({
            **q,
            "raidbot_quest_executor": q["quest"] not in NO_QUEST_EXECUTOR,
            "raidbot_cast_rule": any(s in CAST_RULES for s in spell_names) if spell_names else None,
            "prelock_casts": sum(casts[s, False] for s in spell_names),
            "locked_casts": sum(casts[s, True] for s in spell_names),
            "seeds_cast": len(set().union(*(casting_seeds[s] for s in spell_names))),
            "planning": sum(q["id"] in m["planning"] for m in matches.values()),
            "raid": sum(q["id"] in m["raid"] for m in matches.values()),
            "accepted_offers": accepted,
            "attempts": len(attempts),
            "objective": len(objective),
            "turn_in": len(turn_in),
            "settled": len(settled),
            "claimed": len(claimed),
            "left_on_ground": sum(a["reward"] == "left_on_ground" for a in settled),
            "unknown_reward": sum(a["reward"] == "no_reward_notice" for a in settled),
            "timer": sum(a["outcome"] == "timer" for a in lost),
            "death": sum(a["outcome"] == "death" for a in lost),
            "lost": sum(a["outcome"] == "lost" for a in lost),
            "open": len(open_attempts),
            "median_accept_s": round_median([a["accept_s"] for a in attempts]),
            "median_to_objective_s": round_median([a["objective_s"] - a["accept_s"] for a in objective]),
            "median_to_turn_in_s": round_median([a["turn_in_s"] - a["accept_s"] for a in turn_in]),
            "median_to_claim_s": round_median(paid_spans),
            "median_claim_age_s": round_median([a["settled_s"] for a in claimed]),
            "median_failed_lifetime_s": round_median([a["end_s"] - a["accept_s"] for a in lost]),
            "median_claim_elapsed_clear_equivalents": round_median(ce),
            "min_claim_s": round(min(paid_spans), 3) if paid_spans else None,
            "max_claim_s": round(max(paid_spans), 3) if paid_spans else None,
            "progress_at_claim_max_counts": " ".join(
                f"{value}:{count}" for value, count in sorted(collections.Counter(
                    a["max_progress"] for a in claimed
                ).items())
            ),
            "camps_during_claimed_attempts": sum(a["camps_during"] for a in claimed),
        })
    notices = collections.Counter(
        (r["text"], r["locked"]) for m in matches.values() for r in m["notices"]
    )
    totals = {k: sum(row[k] for row in rows) for k in (
        "planning", "raid", "accepted_offers", "attempts", "objective", "turn_in", "settled", "claimed",
        "left_on_ground", "unknown_reward", "timer", "death", "lost", "open",
    )}
    assert sum(casts.values()) == sum(row["prelock_casts"] + row["locked_casts"] for row in rows)
    assert totals["planning"] == 5 * len(matches)
    assert totals["raid"] == 10 * len(matches)
    assert totals["attempts"] == sum(totals[k] for k in ("settled", "timer", "death", "lost", "open"))
    assert totals["settled"] == sum(totals[k] for k in ("claimed", "left_on_ground", "unknown_reward"))
    claimed_attempts = [
        a for a in detailed_attempts if a["reward"] in {"received", "equipped_after_choice"}
    ]
    claims_by_seed = collections.Counter(a["seed"] for a in claimed_attempts)
    return {
        "content_hash": catalog["content_hash"],
        "seeds": sorted(matches),
        "cohort": {
            "terminated": sum(m["terminated"] for m in matches.values()),
            "truncated": sum(m["truncated"] for m in matches.values()),
            "focal_resolved": sum(bool(m["resolutions"]) for m in matches.values()),
            "any_accepted": len({a["seed"] for a in detailed_attempts}),
            "any_claimed": len(claims_by_seed),
            "median_claims_per_match": round_median([claims_by_seed[seed] for seed in matches]),
            "median_claim_raid_age_s": round_median([a["settled_s"] for a in claimed_attempts]),
            "spell_quests_claimed": sum(q["claimed"] for q in rows
                                        if q["raidbot_cast_rule"] is not None),
            "spell_quests_claimed_with_no_bot_cast_rule": sum(
                q["claimed"] for q in rows if q["raidbot_cast_rule"] is False
            ),
        },
        "totals": totals,
        "focal_without_open_quest": {
            faction: {
                "clear_credits": wins[faction], "standing_minutes": seconds[faction] / 60,
                "credited_clears_per_minute": rate,
            }
            for faction, rate in sorted(rates.items())
        },
        "notices": [{"text": text, "locked": locked, "count": n}
                    for (text, locked), n in sorted(notices.items())],
        "quests": rows,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("traces", nargs="+", type=Path)
    parser.add_argument("--expect-seeds", help="required half-open seed range, e.g. 1..81")
    parser.add_argument("--field", action="append", default=[])
    parser.add_argument("--csv", type=Path)
    args = parser.parse_args()
    catalog, matches = load(args.traces)
    if args.expect_seeds:
        start, end = map(int, args.expect_seeds.split(".."))
        if sorted(matches) != list(range(start, end)):
            parser.error("trace files do not cover the requested seed range")
    if args.field:
        verify_field(matches, args.field)
    report = summarize(catalog, matches)
    if args.csv:
        with args.csv.open("w", newline="") as output:
            writer = csv.DictWriter(output, fieldnames=report["quests"][0])
            writer.writeheader()
            writer.writerows(report["quests"])
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
