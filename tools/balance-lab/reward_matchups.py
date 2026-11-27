"""Marginal value of legal elite augments and combat quest payouts in paired, seat-swapped duels."""
import argparse
from collections import defaultdict
from concurrent.futures import ThreadPoolExecutor
import csv
import json
from pathlib import Path
import statistics
import subprocess

from route_comparison import SCORES, WEAPON, write_csv

# Utility/terrain rewards and a multi-player resurrection require different tests. The three
# explicit stack payouts here come from the existing real turn-in oracle, in the units it verifies.
DIRECT_QUEST = {"Turret", "Hook", "ShoulderCharge", "Warhorn", "SpellShield", "ChefsKiss", "BreakersHelm",
                "Bolas", "FeedOrHuntDamage", "Tornado", "BlindRageAndStillMind", "Stasis", "WardensCage"}
def trade_up_stacks(catalog):
    # The frozen pre-tune reward catalog did not export this field. Its archived payout was 12/30/54.
    return catalog.get("trade_up_stacks", (12, 30, 54))


def scenarios(group, catalog, reference, cost_ladder):
    stack_cases = {
        "TradeUp": tuple((f"stage{i}", "penitent", n)
                         for i, n in enumerate(trade_up_stacks(catalog), 1)),
        "CrownPower": (("full", "emberImp", 48),),
    }
    root = Path(__file__).resolve().parents[2]
    mob_by_stat = {}
    for row in json.loads((root / "testing/farm_route_data.json").read_text())["rewards"]:
        if row.get("k") in ("stat", "flat"):
            mob_by_stat.setdefault(row["stat"], row["id"])
    prior = defaultdict(list)
    for row in csv.DictReader(reference.open()):
        if row["mode"] == "with_wishes":
            prior[row["build"], int(row["baseline_stacks"])].append((float(row["score"]), row["stat"]))
    for build, weapon in WEAPON.items():
        faction = build.split(":")[0].capitalize()
        choices = []
        if group == "augments":
            choices = [(r["id"], r["name"], {"cards": [r["id"]]}, None)
                       for r in catalog["augments"] if r["faction"] == faction]
        else:
            for row in catalog["quests"]:
                reward = row["reward"]
                if reward in DIRECT_QUEST:
                    choices.append((reward, row["name"], {"rewards": [reward]}, None))
                for label, mob, n in stack_cases.get(reward, ()):
                    choices.append((reward + "/" + label, row["name"], {}, (mob, n)))
        for baseline in ((72, 180) if group == "augments" else (180,)):
            best_score = max(s for s, _ in prior[build, baseline])
            # Stable tie break when multiple stat increments shared the highest screen score.
            best = min(stat for score, stat in prior[build, baseline] if score == best_score)
            inventory = {mob: baseline // 9 for mob in mob_by_stat.values()}
            for key, title, additions, stack_payoff in choices:
                if cost_ladder and (baseline != 180 or key not in {
                    "committed", "machineGun", "secondWind", "TradeUp/stage2", "TradeUp/stage3", "CrownPower/full",
                }):
                    continue
                costs = ((2, "two_preferred_camps"), (4, "four_preferred_camps"),
                         (8, "eight_preferred_camps")) if cost_ladder else ((0, "no_extra_farm"), (1, "one_preferred_camp"))
                for extra, alternative in costs:
                    target, other = inventory.copy(), inventory.copy()
                    if stack_payoff:
                        mob, n = stack_payoff
                        target[mob] = target.get(mob, 0) + n
                    other[mob_by_stat[best]] += 12 * extra
                    for seed in (71, 72, 73):
                        label = dict(reward=key, title=title, build=build, seed=seed, baseline=baseline,
                                     alternative=alternative, camp_stat=best)
                        pair = dict(id=f"{key}/{build}/{baseline}/{alternative}/{seed}", seed=seed,
                                    faction=faction, weapon=weapon.capitalize(), companion=build == "plague:B",
                                    target={"stacks": list(target.items()), "clears": baseline // 12, **additions},
                                    other={"stacks": list(other.items()), "clears": baseline // 12 + extra})
                        yield label, pair


def execute(binary, flag, chunk):
    data = "".join(json.dumps(pair) + "\n" for _, pair in chunk)
    run = subprocess.run([str(binary), "--pvp", flag], input=data, text=True, stdout=subprocess.PIPE, check=True)
    outcomes = [json.loads(line) for line in run.stdout.splitlines()]
    if len(outcomes) != len(chunk):
        raise ValueError("a matchup is missing")
    rows = []
    for (label, pair), out in zip(chunk, outcomes):
        if out["id"] != pair["id"]:
            raise ValueError(f"mismatched matchup: {pair['id']}")
        result = out["with_wishes"]
        rows.append(dict(**label, point_score=statistics.mean(SCORES[v] for v in result),
                         hp_margin_pct=100 * statistics.mean(out["target_hp_margin"]),
                         timeouts=result.count("timeout"), simultaneous=result.count("simultaneous"),
                         reward_uses=sum(out["target_quest_uses"]), outcomes="|".join(result)))
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("group", choices=("augments", "quests"))
    parser.add_argument("binary", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--jobs", type=int, default=4)
    parser.add_argument("--follow", action="store_true", help="both fighters approach again past 96 units of separation")
    parser.add_argument("--reward", action="append", help="measure only the named reward ID; repeat for multiple")
    parser.add_argument("--cost-ladder", action="store_true", help="stress-test selected immediate rewards against 2, 4 and 8 preferred camps")
    args = parser.parse_args()
    binary = args.binary.resolve()
    if args.jobs < 1:
        parser.error("--jobs must be positive")
    catalog = json.loads(subprocess.check_output([str(binary), "--pvp", "--reward-catalog"]))
    ref = Path(__file__).resolve().parents[2] / "docs/evidence/raid-routing-ttk/stat_summary.csv"
    cases = list(scenarios(args.group, catalog, ref, args.cost_ladder))
    if args.reward:
        selected = set(args.reward)
        cases = [(label, pair) for label, pair in cases if label["reward"] in selected]
        missing = selected - {label["reward"] for label, _ in cases}
        if missing:
            parser.error(f"reward IDs are not in this screen: {', '.join(sorted(missing))}")
    if args.follow:
        for _, pair in cases:
            pair["follow_displacement"] = True
    flag = "--farm-mirrors" if args.group == "augments" else "--quest-mirrors"
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        parts = list(pool.map(lambda cases: execute(binary, flag, cases),
                              (cases[i::args.jobs] for i in range(args.jobs))))
    rows = sorted((r for part in parts for r in part), key=lambda r: (r["reward"], r["build"], r["baseline"], r["alternative"], r["seed"]))
    groups = defaultdict(list)
    for row in rows:
        groups[row["reward"], row["title"], row["baseline"], row["alternative"]].append(row)
    summary = [dict(reward=reward, title=title, baseline=base, alternative=alt,
                    build_n=len({r["build"] for r in batch}), combat_seeds=len({r["seed"] for r in batch}),
                    seat_exchanges=2*len(batch), point_score=statistics.mean(r["point_score"] for r in batch),
                    mean_hp_margin_pct=statistics.mean(r["hp_margin_pct"] for r in batch),
                    reward_uses=sum(r["reward_uses"] for r in batch), timeouts=sum(r["timeouts"] for r in batch),
                    simultaneous=sum(r["simultaneous"] for r in batch))
               for (reward, title, base, alt), batch in sorted(groups.items())]
    args.output.mkdir(parents=True, exist_ok=True)
    prefix = args.group + ("_pursuit" if args.follow else "") + ("_cost" if args.cost_ladder else "")
    write_csv(args.output / (prefix + "_duel_samples.csv"), rows)
    write_csv(args.output / (prefix + "_duel_summary.csv"), summary)


if __name__ == "__main__":
    main()
