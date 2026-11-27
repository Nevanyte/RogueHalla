#!/usr/bin/env python3
"""Aggregate raw `elite_field` shards into reproducible CSV and JSON summaries.

Example:
  python3 tools/balance-lab/elite_field_report.py .agent-runs/quest-elite-replicate/report \
    .agent-runs/quest-elite-replicate/shard-*.json

The input files must cover seeds 1..80 exactly once and use the elite-field/1 schema.
"""
import argparse
import collections
import csv
import json
import math
from pathlib import Path

# Elite variants in MobTypeId::ALL order. Keep possible zero-sample bosses in the report.
ELITE_KINDS = (
    "Wyrmling", "AshDrake", "Executioner", "Gaoler", "RatOgre", "PoxBehemoth",
    "GlacialWarden", "AvalancheTroll", "BloodSaint", "Archon",
)
SEED_SET = set(range(1, 81))


def quantile(values, p):
    """Nearest-rank quantile (ceil(p*n)-1), with null for no samples."""
    values = sorted(values)
    return values[max(0, math.ceil(p * len(values)) - 1)] if values else None


def distribution(values):
    return {
        "n": len(values),
        "p25_s": quantile(values, 0.25),
        "median_s": quantile(values, 0.50),
        "p75_s": quantile(values, 0.75),
    }


def write_csv(path, rows, columns):
    with path.open("w", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=columns, extrasaction="ignore")
        writer.writeheader()
        writer.writerows(rows)


def age_breakdown(camp_type, records):
    rows = []
    for start in (0, 5, 10, 15, 20):
        band = [r for r in records if r["start_minute"] >= start
                and (start == 20 or r["start_minute"] < start + 5)]
        for group in ("all", "solo", "multiple"):
            members = band if group == "all" else [
                r for r in band if (len(r["participants"]) == 1) == (group == "solo")
            ]
            clears = [r["start_to_clear_s"] for r in members if r["start_to_clear_s"] is not None]
            boss_times = [r["first_boss_to_kill_s"] for r in members if r["first_boss_to_kill_s"] is not None]
            summary = {
                "camp_type": camp_type, "minute_from": start, "minute_until": start + 5 if start < 20 else None,
                "participants": group, "pulls": len(members),
                "maps": len({r["seed"] for r in members}),
                "pulls_with_participant_death": sum(bool(r["participant_deaths"]) for r in members),
            }
            for measure, times in (("full_clear", clears), ("boss_contact_to_kill", boss_times)):
                summary.update({f"{measure}_{k}": v for k, v in distribution(times).items()})
            rows.append(summary)
    return rows


def chart_clears(rows, path):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    fig, ax = plt.subplots(figsize=(8, 4.2), constrained_layout=True)
    for camp, color, offset in (("elite", "#804266", -0.09), ("normal", "#187467", 0.09)):
        sample = [r for r in rows if r["participants"] == "all" and r["camp_type"] == camp and r["full_clear_n"]]
        x = [r["minute_from"] / 5 + offset for r in sample]
        medians = [r["full_clear_median_s"] for r in sample]
        ax.vlines(x, [r["full_clear_p25_s"] for r in sample], [r["full_clear_p75_s"] for r in sample],
                  color=color, lw=2, alpha=0.65)
        ax.plot(x, medians, "o-", color=color, label=camp.title() + " camp")
    ax.set_title("Elite and normal camp clear times", loc="left", fontweight="bold")
    ax.set_xticks(range(5), ["0–5", "5–10", "10–15", "15–20", "20+"])
    ax.set_xlim(-0.35, 4.35)
    ax.set_ylim(bottom=0)
    ax.set_xlabel("Raid minutes at first damage")
    ax.set_ylabel("Seconds to full clear (median and middle 50%)")
    ax.legend(frameon=False)
    ax.spines[["right", "top"]].set_visible(False)
    ax.grid(axis="y", alpha=0.2)
    fig.savefig(path, dpi=160)
    plt.close(fig)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path, help="directory for summary.json and CSV tables")
    parser.add_argument("shards", type=Path, nargs="+", help="elite-field/1 shard JSON files")
    parser.add_argument("--chart", action="store_true", help="render a clear-time chart with matplotlib")
    args = parser.parse_args()

    matches = []
    seen = set()
    method = None
    for path in args.shards:
        report = json.loads(path.read_text())
        if report.get("schema") != "elite-field/1":
            raise ValueError(f"{path}: expected elite-field/1")
        if method is None:
            method = report["method"]
        elif report["method"] != method:
            raise ValueError(f"{path}: method metadata differs across shards")
        seeds = [row["seed"] for row in report["seeds"]]
        if len(seeds) != len(set(seeds)) or seen.intersection(seeds):
            raise ValueError(f"{path}: duplicate seed")
        seen.update(seeds)
        matches.extend(report["seeds"])
    if seen != SEED_SET:
        raise ValueError(f"expected seed set 1..80, found {sorted(seen)}")
    matches.sort(key=lambda row: row["seed"])

    elite_rows = [episode for match in matches for episode in match["elites"]]
    normal_rows = [episode for match in matches for episode in match["normals"]]
    unknown_kinds = {row["elite_kind"] for row in elite_rows} - set(ELITE_KINDS)
    if unknown_kinds:
        raise ValueError(f"unknown elite kinds: {unknown_kinds}")
    kind_rows = []
    for kind in ELITE_KINDS:
        records = [row for row in elite_rows if row["elite_kind"] == kind]
        boss_from_pull = [row["boss_kill_s"] - row["start_s"] for row in records if row["boss_kill_s"] is not None]
        boss_contact_to_kill = [row["first_boss_to_kill_s"] for row in records if row["first_boss_to_kill_s"] is not None]
        full_clear = [row["start_to_clear_s"] for row in records if row["start_to_clear_s"] is not None]
        outcomes = collections.Counter(row["outcome"] for row in records)
        death_episodes = [row for row in records if row["participant_deaths"]]
        all_died = sum(
            bool(row["participants"])
            and set(row["participants"]).issubset({death["seat"] for death in row["participant_deaths"]})
            for row in records
        )
        kind_rows.append({
            "elite_kind": kind,
            "attempts": len(records),
            "seeds": len({row["seed"] for row in records}),
            "boss_kills": sum(row["boss_kill_s"] is not None for row in records),
            "full_clears": len(full_clear),
            "abandoned_10s": outcomes.get("abandoned_no_damage_10s", 0),
            "all_participants_died": all_died,
            "participant_death_episodes": len(death_episodes),
            "match_end_before_clear": outcomes.get("match_end_before_clear", 0),
            "timeout_open": outcomes.get("timeout_open", 0),
            "boss_kill_from_pull": distribution(boss_from_pull),
            "boss_contact_to_kill": distribution(boss_contact_to_kill),
            "full_clear_from_pull": distribution(full_clear),
        })

    attempt_minutes = collections.Counter()
    for match in matches:
        attempt_minutes.update(match["active_minute_attempts"])
    minute_rows = [
        {"camp_type": key.split(":", 1)[0], "start_minute": int(key.split(":", 1)[1]), "attempts": count}
        for key, count in sorted(attempt_minutes.items(), key=lambda item: (item[0].split(":", 1)[0], int(item[0].split(":", 1)[1])))
    ]

    normal_groups = collections.defaultdict(list)
    for row in normal_rows:
        normal_groups[row["camp_kind"]].append(row)
    normal_type_rows = []
    for kind, records in sorted(normal_groups.items()):
        clears = [row["start_to_clear_s"] for row in records if row["start_to_clear_s"] is not None]
        normal_type_rows.append({
            "normal_camp_kind": kind,
            "attempts": len(records),
            "seeds": len({row["seed"] for row in records}),
            "full_clears": len(clears),
            "abandoned_10s": sum(row["outcome"] == "abandoned_no_damage_10s" for row in records),
            "full_clear_from_pull": distribution(clears),
        })

    payouts = [(match["seed"], payout) for match in matches for payout in match["payouts"]]
    payout_groups = collections.defaultdict(list)
    for _, payout in payouts:
        payout_groups["human_eligible_seat" if payout["seat"] == 0 else "fill_bot_seat"].append(payout)
    payout_rows = []
    for status in ("human_eligible_seat", "fill_bot_seat"):
        records = payout_groups[status]
        payout_rows.append({
            "recipient_class": status,
            "seats": "0" if status == "human_eligible_seat" else "1..7",
            "elite_gv_events": len(records),
            "amount_sum": sum(row["amount"] for row in records),
            "seeds": len({seed for seed, row in payouts if (row["seat"] == 0) == (status == "human_eligible_seat")}),
        })

    age_rows = age_breakdown("elite", elite_rows) + age_breakdown("normal", normal_rows)
    ticks = [row["ticks"] for row in matches]
    chest_picks = [pick for match in matches for pick in match["chest_picks"]]
    summary = {
        "schema": "elite-field-summary/1",
        "seed_count": len(matches),
        "seeds": [row["seed"] for row in matches],
        "method": method,
        "clock_and_baseline": {
            "tick_ms": 25,
            "tick_hz": 1000 / 25,
            "max_ticks": 72000,
            "tick_count": distribution(ticks),
            "tick_total": sum(ticks),
            "terminated_matches": sum(row["terminated"] for row in matches),
            "truncated_matches": sum(row["truncated"] for row in matches),
            "note": "Same generated map seed and 25 ms (40 Hz) step as the stock TTK field cohort; ticks stop at match termination, with 72,000 as the cap.",
        },
        "elite_attempts": sum(row["attempts"] for row in kind_rows),
        "normal_attempts": len(normal_rows),
        "elite_by_kind": kind_rows,
        "camp_by_age": age_rows,
        "attempts_by_start_minute": minute_rows,
        "normal_farm_by_type": normal_type_rows,
        "elite_gv_payouts": {
            "event_count": len(payouts),
            "amount_sum": sum(row["amount"] for _, row in payouts),
            "by_recipient_class": payout_rows,
            "by_seat": {
                str(seat): {"event_count": sum(row["seat"] == seat for _, row in payouts),
                            "amount_sum": sum(row["amount"] for _, row in payouts if row["seat"] == seat)}
                for seat in range(8)
            },
            "rule": "Event::Gv reason Elite is credited to any unresolved eligible player, including bot:true fill seats; award is base elite GV times kill multiplier plus Smite bonus, rounded and capped by remaining mob-GV cap. Rework stat-helper payouts to standing contributors are not Gv events and are not counted here.",
        },
        "chest_taken_events": {
            "event_count": len(chest_picks),
            "valid_choice_count": sum(row["pick"] >= 0 for row in chest_picks),
            "skip_count": sum(row["pick"] < 0 for row in chest_picks),
            "by_seat": {str(seat): sum(row["seat"] == seat for row in chest_picks) for seat in range(8)},
            "note": "Raw ChestTaken identifies seat, internal chest id and pick index, but these rows do not identify elite chest/card offers.",
        },
    }
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    write_csv(args.output / "camp_by_age.csv", age_rows, list(age_rows[0]))
    if args.chart:
        chart_clears(age_rows, args.output / "camp_clear_times.png")
    elite_csv_rows = []
    for row in kind_rows:
        flat = {key: value for key, value in row.items() if not isinstance(value, dict)}
        for name in ("boss_kill_from_pull", "boss_contact_to_kill", "full_clear_from_pull"):
            dist = row[name]
            flat[f"{name}_n"] = dist["n"]
            flat[f"{name}_p25_s"] = dist["p25_s"]
            flat[f"{name}_median_s"] = dist["median_s"]
            flat[f"{name}_p75_s"] = dist["p75_s"]
        elite_csv_rows.append(flat)
    write_csv(args.output / "elite_by_kind.csv", elite_csv_rows, [
        "elite_kind", "attempts", "seeds", "boss_kills", "full_clears", "abandoned_10s",
        "all_participants_died", "participant_death_episodes", "match_end_before_clear", "timeout_open",
        "boss_kill_from_pull_n", "boss_kill_from_pull_p25_s", "boss_kill_from_pull_median_s", "boss_kill_from_pull_p75_s",
        "boss_contact_to_kill_n", "boss_contact_to_kill_p25_s", "boss_contact_to_kill_median_s", "boss_contact_to_kill_p75_s",
        "full_clear_from_pull_n", "full_clear_from_pull_p25_s", "full_clear_from_pull_median_s", "full_clear_from_pull_p75_s",
    ])
    write_csv(args.output / "elite_start_minutes.csv", minute_rows, ["camp_type", "start_minute", "attempts"])
    normal_csv_rows = []
    for row in normal_type_rows:
        flat = {key: value for key, value in row.items() if not isinstance(value, dict)}
        dist = row["full_clear_from_pull"]
        flat.update(full_clear_n=dist["n"], full_clear_p25_s=dist["p25_s"],
                    full_clear_median_s=dist["median_s"], full_clear_p75_s=dist["p75_s"])
        normal_csv_rows.append(flat)
    write_csv(args.output / "normal_farm_by_type.csv", normal_csv_rows, [
        "normal_camp_kind", "attempts", "seeds", "full_clears", "abandoned_10s",
        "full_clear_n", "full_clear_p25_s", "full_clear_median_s", "full_clear_p75_s",
    ])
    write_csv(args.output / "elite_gv_payouts.csv", payout_rows, ["recipient_class", "seats", "elite_gv_events", "amount_sum", "seeds"])
    write_csv(args.output / "match_ticks.csv", [
        {"seed": row["seed"], "ticks": row["ticks"], "terminated": row["terminated"], "truncated": row["truncated"]}
        for row in matches
    ], ["seed", "ticks", "terminated", "truncated"])
    print(f"validated {len(matches)} seeds; {summary['elite_attempts']} elite attempts; {summary['normal_attempts']} normal attempts; {len(payouts)} elite-GV events")


if __name__ == "__main__":
    main()
