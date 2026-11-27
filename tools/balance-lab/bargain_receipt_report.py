"""Join settled Bargain receipts to net route costs and score payment both ways."""
import argparse
import csv
import json
from collections import Counter
from pathlib import Path
import random
from statistics import median, quantiles

from quest_economy import measure, read


STAT_ORDER = ("damage", "movement", "max_hp", "ability_power", "armor",
              "shield", "attack_damage", "attack_speed", "lifesteal")


def prices(receipt, payout, fee):
    changes = receipt["changes"]
    credits = [item for item in changes if item["stack_change"] > 0]
    debits = [item for item in changes if item["stack_change"] < 0]
    held, after = receipt["held_before"], receipt["held_after"]
    inactive = 1 - receipt["active_before"]
    if len(credits) != 1 or credits[0]["stack_change"] != payout:
        return {"payment": "ambiguous_state_change"}
    gain = credits[0]
    weight = receipt["neutral_policy_weight"]
    gross_fit = payout / 12 * gain["per_stack_policy_weight"] / weight
    if len(debits) == 1 and debits[0]["stack_change"] == -fee and held == after:
        paid = debits[0]
        fit_cost = fee / 12 * paid["per_stack_policy_weight"] / weight
        equal_net = (payout - fee) / 12
        payment = paid["stat"]
    elif not debits and held[inactive] and after[inactive] is None and held[1 - inactive] == after[1 - inactive]:
        fit_cost = receipt["spare_weapon_policy_cost_ce"]
        equal_net = payout / 12 - fit_cost
        payment = "spare_" + held[inactive]
    else:
        return {"payment": "ambiguous_state_change", "received_stat": gain["stat"]}
    return {
        "payment": payment, "received_stat": gain["stat"],
        "equal_net_ce": equal_net, "policy_fit_net_ce": gross_fit - fit_cost,
        "fit_input_ce": fit_cost, "fit_output_ce": gross_fit,
    }


def opportunity(meta, match, baseline, receipt):
    alternatives = {row["start_s"]: row for row in match.get("bargain_nearby", [])}
    weights = receipt.get("per_stack_policy_weights")
    if not weights:
        return {}
    normalizer = receipt["neutral_policy_weight"]
    valued = 0.
    full_camp = 0.
    first = None
    for leg in match["quest_goal_legs"]:
        if leg["quest"] != "the_bargain" or leg["start_s"] > receipt["age_s"]:
            continue
        alternative = alternatives.get(leg["start_s"])
        if not alternative:
            return {}
        first = first or alternative
        cost = next(row for row in measure(meta, [{**match, "quest_goal_legs": [leg]}], baseline)
                    if row["quest"] == "the_bargain")["expected_cost_per_paid_camp_mean"]
        weight = weights[STAT_ORDER.index(alternative["paid_stat"])] / normalizer
        # The baseline route cost is denominated in a 12-stack normal camp.
        # The closest live normal roster has its own remaining stack count.
        full_camp += cost * weight
        valued += cost * (alternative["alive"] * 3 / 12) * weight
    if first is None:
        return {}
    return {"nearby_first_stat": first["paid_stat"], "nearby_first_alive": first["alive"],
            "nearby_first_distance": first["distance"], "nearby_route_fit_ce": valued,
            "full_nearby_route_fit_ce": full_camp}


def report(meta, matches, baseline, payout, fee):
    for match in matches:
        receipts = match["bargain_receipts"]
        attempts = [a for a in match["attempts"] if a["quest"] == "TheBargain" and a["reward"] == "received"]
        if len(receipts) != len(attempts) or len(receipts) > 1:
            raise ValueError(f"Unmatched Bargain receipts at seed {match['seed']}")
        if not receipts:
            continue
        receipt = receipts[0]
        assert receipt["offer"] == attempts[0]["offer"] and receipt["age_s"] == attempts[0]["settled_s"]
        route = next(r for r in measure(meta, [match], baseline) if r["quest"] == "the_bargain")
        cost = route["expected_cost_per_paid_camp_mean"]
        assert cost is not None
        result = prices(receipt, payout, fee)
        alternative = opportunity(meta, match, baseline, receipt)
        fit_cost = alternative.get("nearby_route_fit_ce")
        full_cost = alternative.get("full_nearby_route_fit_ce")
        net = result.get("policy_fit_net_ce")
        fit_output = result.get("fit_output_ce")
        fit_input = result.get("fit_input_ce")
        fit_multiple = net / fit_cost if net is not None and fit_cost else None
        # Sensitivity assumes the same choice would still be taken at a different price.
        payout_target = ((1.375 * full_cost + fit_input) / fit_output * payout
                         if full_cost and fit_input is not None and fit_output else None)
        yield {
            "seed": match["seed"], "faction": match["faction"],
            "active_weapon": receipt["held_before"][receipt["active_before"]] or "Fists",
            "paid_age_s": receipt["age_s"], "route_cost_ce": cost, **result, **alternative,
            "equal_multiple": result["equal_net_ce"] / cost if cost and "equal_net_ce" in result else None,
            "policy_fit_multiple": net / cost if cost and net is not None else None,
            "nearby_fit_multiple": fit_multiple,
            "full_nearby_fit_multiple": net / full_cost if full_cost and net is not None else None,
            "same_trade_37_5pct_payout": payout_target,
        }


def seed_interval(matches, paid, failed_costs, key):
    by_seed = {row["seed"]: row for row in paid}
    costs_and_returns = []
    for match in matches:
        seed = match["seed"]
        row = by_seed.get(seed)
        cost = (row[key] if row else 0.) + failed_costs[seed]
        gain = row["policy_fit_net_ce"] if row else 0.
        costs_and_returns.append((cost, gain))
    if len(costs_and_returns) < 5:
        return None
    rng = random.Random(4020)
    trials = []
    for _ in range(2000):
        picked = rng.choices(costs_and_returns, k=len(costs_and_returns))
        cost = sum(c for c, _ in picked)
        if cost:
            trials.append(sum(v for _, v in picked) / cost)
    if len(trials) < 2:
        return None
    points = quantiles(trials, n=40, method="inclusive")
    return [points[0], points[-1]]


def summarize(meta, matches, rates, rows):
    route = next(r for r in measure(meta, matches, rates) if r["quest"] == "the_bargain")
    priced = [row for row in rows if row.get("nearby_route_fit_ce") is not None and "policy_fit_net_ce" in row]
    failed = route["unsuccessful_only_route_cost_total"]
    failed_costs = {
        m["seed"]: next(
            r["unsuccessful_only_route_cost_total"]
            for r in measure(meta, [m], rates) if r["quest"] == "the_bargain"
        ) for m in matches
    }
    summaries = {}
    for basis, key in (("neutral", "route_cost_ce"), ("remaining_nearby", "nearby_route_fit_ce"),
                       ("full_nearby", "full_nearby_route_fit_ce")):
        denom = sum(row[key] for row in priced)
        numerator = sum(row["policy_fit_net_ce"] for row in priced)
        # Failed routes have no settlement loadout. Sensitivity covers the full
        # native stat-fit range (0.6–1.4 times neutral), holding clear rate fixed.
        ratios = [row["policy_fit_net_ce"] / row[key] for row in priced if row[key]]
        summaries[basis] = {
            "spread_below_in_above": [sum(x < 1.25 for x in ratios), sum(1.25 <= x <= 1.5 for x in ratios),
                                      sum(x > 1.5 for x in ratios)],
            "median_paid_multiple": median(ratios) if ratios else None,
            "map_bootstrap_95pct": seed_interval(matches, priced, failed_costs, key),
            "paid_route_cost_ce": denom, "fit_gain_ce": numerator,
            "multiple_before_failed_spend": numerator / denom if denom else None,
            "multiple_including_failed_neutral": numerator / (denom + failed) if denom else None,
            "including_failed_low_multiple": numerator / (denom + 1.4 * failed) if denom else None,
            "including_failed_high_multiple": numerator / (denom + .6 * failed) if denom else None,
        }
    death_durations = [result["duration_s"] for m in matches for result in m["resolutions"]
                       if result["outcome"] == "Died"]
    return dict(
        maps=len(matches), offers=sum("the_bargain" in m["raid"] for m in matches),
        accepted=sum(any(a["quest"] == "TheBargain" for a in m["attempts"]) for m in matches),
        paid=len(rows), priced=len(priced), failed_route_cost_neutral_ce=failed,
        focal_deaths=len(death_durations),
        focal_deaths_before_5m=sum(t < 300 for t in death_durations),
        bargain_attempt_deaths=sum(a["quest"] == "TheBargain" and a["outcome"] == "death"
                                     for m in matches for a in m["attempts"]),
        payments=Counter(row["payment"] for row in rows),
        received=Counter(row.get("received_stat", "unidentified") for row in rows),
        timings=route, opportunity=summaries,
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("routes", type=Path, nargs="+")
    parser.add_argument("--farm-reference", type=Path, required=True)
    parser.add_argument("--payout", type=int, default=29)
    parser.add_argument("--fee", type=int, default=20)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--summary", type=Path)
    args = parser.parse_args()
    meta, matches = read(args.routes, {})
    _, rates = read([args.farm_reference], {})
    columns = ("seed", "faction", "active_weapon", "paid_age_s", "route_cost_ce", "payment",
               "received_stat", "equal_net_ce", "policy_fit_net_ce", "fit_input_ce",
               "fit_output_ce", "equal_multiple", "policy_fit_multiple",
               "nearby_first_stat", "nearby_first_alive", "nearby_first_distance", "nearby_route_fit_ce",
               "nearby_fit_multiple", "full_nearby_route_fit_ce", "full_nearby_fit_multiple",
               "same_trade_37_5pct_payout")
    rows = list(report(meta, matches, rates, args.payout, args.fee))
    with args.output.open("w", newline="") as out:
        writer = csv.DictWriter(out, fieldnames=columns, lineterminator="\n")
        writer.writeheader()
        writer.writerows(rows)
    if args.summary:
        args.summary.write_text(json.dumps(summarize(meta, matches, rates, rows), indent=2) + "\n")


if __name__ == "__main__":
    main()
