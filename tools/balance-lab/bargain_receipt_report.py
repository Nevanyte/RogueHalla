"""Join settled Bargain receipts to net route costs and score payment both ways."""
import argparse
import csv
from pathlib import Path

from quest_economy import measure, read


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
        yield {
            "seed": match["seed"], "faction": match["faction"],
            "active_weapon": receipt["held_before"][receipt["active_before"]] or "Fists",
            "paid_age_s": receipt["age_s"], "route_cost_ce": cost, **result,
            "equal_multiple": result["equal_net_ce"] / cost if cost and "equal_net_ce" in result else None,
            "policy_fit_multiple": result["policy_fit_net_ce"] / cost if cost and "policy_fit_net_ce" in result else None,
        }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("routes", type=Path, nargs="+")
    parser.add_argument("--farm-reference", type=Path, required=True)
    parser.add_argument("--payout", type=int, default=29)
    parser.add_argument("--fee", type=int, default=20)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    meta, matches = read(args.routes, {})
    _, rates = read([args.farm_reference], {})
    columns = ("seed", "faction", "active_weapon", "paid_age_s", "route_cost_ce", "payment",
               "received_stat", "equal_net_ce", "policy_fit_net_ce", "fit_input_ce",
               "fit_output_ce", "equal_multiple", "policy_fit_multiple")
    with args.output.open("w", newline="") as out:
        writer = csv.DictWriter(out, fieldnames=columns, lineterminator="\n")
        writer.writeheader()
        writer.writerows(report(meta, matches, rates, args.payout, args.fee))


if __name__ == "__main__":
    main()
