"""Compare 40 Hz field observers over a common half-open seed range."""
import argparse
from collections import Counter
import csv
from glob import glob
import gzip
import json
from pathlib import Path
from statistics import median, quantiles


AGES = ("0-5", "5-10", "10-15", "15-20", "20+")


def load(pattern, key, schema, expected):
    matches = {}
    for path in sorted(glob(pattern)):
        data = json.loads(Path(path).read_text())
        if data["schema"] != schema or data.get("tick_ms", data.get("method", {}).get("tick_ms")) != 25:
            raise ValueError(f"Wrong schema or tick for {path}")
        for match in data[key]:
            seed = match["seed"]
            if seed not in expected:
                continue
            if seed in matches:
                raise ValueError(f"Duplicate seed {seed} in {path}")
            matches[seed] = match
        if key == "match_results":
            for episode in data["episodes"]:
                seed = episode["seed"]
                if seed in expected:
                    matches[seed].setdefault("episodes", []).append(episode)
    if set(matches) != expected:
        raise ValueError(f"Missing seeds {sorted(expected - matches.keys())} in {pattern}")
    return [matches[seed] for seed in sorted(matches)]


def summary_row(cohort, band, kind, samples):
    times = [time for _, time in samples]
    lo, _, hi = quantiles(times, n=4, method="inclusive") if len(times) > 1 else (None,) * 3
    return dict(cohort=cohort, band=band, group=kind, n=len(times),
                map_n=len({row[0] for row in samples}), p25_s=lo,
                median_s=median(times) if times else None, p75_s=hi)


def camp_rows(matches, key, outcome, elapsed, band, *, solo=None):
    return [(m["seed"], row[elapsed]) for m in matches for row in m[key]
            if row[elapsed] is not None and (band == "all" or
            AGES[min(row["start_minute"] // 5, 4)] == band)
            and (outcome is None or row["outcome"] == outcome)
            and (solo is None or (len(row["participants"]) == 1) == solo)]


def death_group(result):
    if result["killed_by_bot"]:
        return "credited_player"
    if result["killed_by"] == "sudden death":
        return "sudden_death"
    return "other_or_uncredited"


def analyze(name, data):
    encounters, field = data["encounters"], data["timing"]
    camp_by_seed = {row["seed"]: row for row in encounters}
    for row in field:
        camp = camp_by_seed[row["seed"]]
        if (row["ticks"], row["terminated"], row["truncated"], row["raid_ended_event"]) != (
                camp["ticks"], camp["terminated"], camp["truncated"], camp["raid_ended"]):
            raise ValueError(f"Observers diverged at {name} seed {row['seed']}")
    episodes = [episode for match in field for episode in match.get("episodes", [])]
    timing = []
    for band in ("all", *AGES):
        batch = [row for row in episodes if band == "all" or row["band"] == band]
        normal = [r for r in batch if r["outcome"] == "camp_cleared"]
        pvp = [r for r in batch if r["outcome"] in ("death_clean", "death_mixed")]
        for label, rows in (
            ("normal_clear_30s", normal),
            ("normal_first_pull_30s", [r for r in normal if not r["prior_pull"]]),
            ("pvp_final_blow", pvp),
            ("pvp_mixed", [r for r in pvp if r["outcome"] == "death_mixed"]),
            ("pvp_clean", [r for r in pvp if r["outcome"] == "death_clean"]),
            ("pvp_mixed_environment", [r for r in pvp if r["outcome"] == "death_mixed" and r["environment"]]),
            ("pvp_mixed_multiple_owners", [r for r in pvp if r["outcome"] == "death_mixed" and r["third_party"]]),
            ("pvp_healthy_clean_prelock", [r for r in pvp if r["outcome"] == "death_clean"
                and not r["gates_locked_start"] and (r["start_hp_fraction"] or 0) >= .9]),
        ):
            timing.append(summary_row(name, band, label, [(r["seed"], r["elapsed_s"]) for r in rows]))
        for label, key, outcome, elapsed in (
            ("normal_clear_10s", "normals", "full_clear", "start_to_clear_s"),
            ("elite_full_clear_10s", "elites", "full_clear", "start_to_clear_s"),
            ("elite_boss_kill_10s", "elites", None, "first_boss_to_kill_s"),
        ):
            timing.append(summary_row(name, band, label, camp_rows(encounters, key, outcome, elapsed, band)))
        for key in ("normals", "elites"):
            for label, solo in (("solo", True), ("multiple", False)):
                timing.append(summary_row(name, band, f"{key}_full_clear_{label}", camp_rows(
                    encounters, key, "full_clear", "start_to_clear_s", band, solo=solo,
                )))
    elites = [r for m in encounters for r in m["elites"]]
    for kind in sorted({row["elite_kind"] for row in elites}):
        timing.append(summary_row(name, "all", f"elite_full_clear_{kind}", [
            (r["seed"], r["start_to_clear_s"]) for r in elites
            if r["elite_kind"] == kind and r["outcome"] == "full_clear"
        ]))
    timing.extend(summary_row(name, "all", kind, samples) for kind, samples in (
        ("raid_duration", [(m["seed"], max(r["duration_s"] for r in m["resolved"])) for m in field]),
        ("first_death_time", [(m["seed"], min(r["duration_s"] for r in m["resolved"] if r["outcome"] == "Died"))
                              for m in field if any(r["outcome"] == "Died" for r in m["resolved"])]),
    ))
    matches = []
    for m in field:
        deaths = [r for r in m["resolved"] if r["outcome"] == "Died"]
        causes = Counter(death_group(r) for r in deaths)
        # Resolved credit and the raw Death final blow answer different questions.
        matches.append(dict(cohort=name, seed=m["seed"], ticks=m["ticks"], terminated=m["terminated"],
                            truncated=m["truncated"], raid_ended=m["raid_ended_event"],
                            duration_s=max(r["duration_s"] for r in m["resolved"]),
                            first_death_s=min((r["duration_s"] for r in deaths), default=None),
                            deaths=len(deaths), credited_player_deaths=causes["credited_player"],
                            sudden_deaths=causes["sudden_death"], other_deaths=causes["other_or_uncredited"],
                            deaths_lt_5m=sum(r["duration_s"] < 300 for r in deaths),
                            deaths_lt_10m=sum(r["duration_s"] < 600 for r in deaths)))
    counts = Counter((r["kind"], r["band"], r["outcome"]) for r in episodes)
    counts.update((key, AGES[min(r["start_minute"] // 5, 4)], r["outcome"])
                  for m in encounters for key in ("normals", "elites") for r in m[key])
    outcome_rows = [dict(cohort=name, group=group, band=band, outcome=outcome, n=n)
                    for (group, band, outcome), n in sorted(counts.items())]
    return timing, matches, outcome_rows


def write(path, rows):
    with path.open("w", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=rows[0], lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    parser.add_argument("--seeds", default="1..13")
    parser.add_argument("--cohort", nargs=3, action="append", metavar=("NAME", "ELITE_GLOB", "TIMING_GLOB"))
    parser.add_argument("--read-archive", type=Path)
    args = parser.parse_args()
    lo, hi = map(int, args.seeds.split(".."))
    expected = set(range(lo, hi))
    if not expected:
        parser.error("--seeds must select at least one seed")
    if bool(args.cohort) == bool(args.read_archive):
        parser.error("provide either --cohort or --read-archive")
    if args.read_archive:
        raw = json.loads(gzip.decompress(args.read_archive.read_bytes()))
        for data in raw.values():
            if any({m["seed"] for m in rows} != expected for rows in data.values()):
                parser.error("--seeds does not match archive")
    else:
        names = [item[0] for item in args.cohort]
        if len(names) != len(set(names)):
            parser.error("cohort names must be unique")
        raw = {name: dict(encounters=load(elite, "seeds", "elite-field/1", expected),
                          timing=load(timing, "match_results", "strategy-field-ttk/1", expected))
               for name, elite, timing in args.cohort}
    all_timing, all_matches, all_outcomes = [], [], []
    for name, data in raw.items():
        timing, matches, outcomes = analyze(name, data)
        all_timing.extend(timing)
        all_matches.extend(matches)
        all_outcomes.extend(outcomes)
    args.output.mkdir(parents=True, exist_ok=True)
    write(args.output / "timings.csv", all_timing)
    write(args.output / "matches.csv", all_matches)
    write(args.output / "outcomes.csv", all_outcomes)
    if not args.read_archive:
        archive = json.dumps(raw, separators=(",", ":")).encode()
        (args.output / "raw.json.gz").write_bytes(gzip.compress(archive, mtime=0))


if __name__ == "__main__":
    main()
