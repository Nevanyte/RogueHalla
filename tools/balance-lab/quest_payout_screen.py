"""Compare Trade Up stack payouts to two and four normal camps of preferred stats."""
import argparse
import json
from pathlib import Path
import statistics
import subprocess

from reward_matchups import execute, scenarios, trade_up_stacks
from route_comparison import write_csv


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("binary", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("before", type=int)
    parser.add_argument("after", type=int)
    args = parser.parse_args()
    binary = args.binary.resolve()
    catalog = json.loads(subprocess.check_output([str(binary), "--pvp", "--reward-catalog"]))
    reference = Path(__file__).resolve().parents[2] / "docs/evidence/raid-routing-ttk/stat_summary.csv"
    costs = ("two_preferred_camps", "four_preferred_camps")
    result = []
    for variant, payout in (("baseline", args.before), ("tuned", args.after)):
        cases = []
        for label, pair in scenarios("quests", catalog, reference, True):
            if label["reward"] != "TradeUp/stage3" or label["alternative"] not in costs:
                continue
            # Start from the comparison kit with equal farm. On this screen only the quest grant changes.
            stacks = dict(pair["target"]["stacks"])
            stacks["penitent"] += payout - trade_up_stacks(catalog)[-1]
            pair["target"]["stacks"] = list(stacks.items())
            pair["id"] = variant + "/" + pair["id"]
            pair["follow_displacement"] = True
            cases.append((label, pair))
        outcomes = execute(binary, "--farm-mirrors", cases)
        for alternative in costs:
            subset = [row for row in outcomes if row["alternative"] == alternative]
            if len(subset) != 30:
                raise ValueError("expected ten builds and three seeds")
            result.append(dict(
                variant=variant, stacks=payout, alternative=alternative, seat_exchanges=2 * len(subset),
                score=statistics.mean(row["point_score"] for row in subset),
                mean_hp_margin_pct=statistics.mean(row["hp_margin_pct"] for row in subset),
                timeouts=sum(row["timeouts"] for row in subset),
            ))
    write_csv(args.output, result)


if __name__ == "__main__":
    main()
