"""Measure contact-to-death for equal farm kits with the Machine Gun and Committed pair."""
import argparse
from collections import Counter, defaultdict
from concurrent.futures import ThreadPoolExecutor
import json
from pathlib import Path
import statistics
import subprocess

from reward_matchups import scenarios
from route_comparison import WEAPON, write_csv

CARD_PAIR = ("machineGun", "committed")
ARMS = {
    "neither": ((), ()),
    "pair_both": (CARD_PAIR, CARD_PAIR),
    "pair_target_only": (CARD_PAIR, ()),
    "machine_gun_both": (("machineGun",), ("machineGun",)),
    "committed_both": (("committed",), ("committed",)),
}
SEEDS = (71, 72, 73)


def cases(catalog, reference):
    expected = {(build, stacks, seed) for build in WEAPON for stacks in (72, 180) for seed in SEEDS}
    for label, base in scenarios("augments", catalog, reference, False):
        if label["reward"] != "machineGun" or label["alternative"] != "no_extra_farm":
            continue
        build, stacks, seed = (label[key] for key in ("build", "baseline", "seed"))
        expected.remove((build, stacks, seed))
        for arm, (target, other) in ARMS.items():
            pair = dict(base, id=f"{arm}/{build}/{stacks}/{seed}", follow_displacement=True)
            pair["target"] = dict(base["target"], cards=target)
            pair["other"] = dict(base["other"], cards=other)
            yield (arm, build, stacks, seed), pair
    if expected:
        raise ValueError(f"missing base kits: {sorted(expected)}")


def execute(binary, chunk):
    payload = "".join(json.dumps(pair) + "\n" for _, pair in chunk)
    result = subprocess.run([str(binary), "--pvp", "--farm-mirrors"], input=payload,
                            text=True, stdout=subprocess.PIPE, check=True)
    outputs = [json.loads(line) for line in result.stdout.splitlines()]
    if len(outputs) != len(chunk):
        raise ValueError("a matchup is missing")
    for ((arm, build, stacks, seed), pair), out in zip(chunk, outputs):
        if out["id"] != pair["id"]:
            raise ValueError(f"mismatched matchup: {pair['id']}")
        for seat, outcome in enumerate(out["with_wishes"]):
            yield dict(
                arm=arm, build=build, stacks=stacks, clears=pair["target"]["clears"], seed=seed,
                target_seat=seat, outcome=outcome,
                victim_contact_to_death_ms=out["victim_contact_to_death_ms"][seat],
                landed_wishes=out["landed_wishes"][seat],
            )


def sample_key(row):
    return tuple(row[key] for key in ("build", "stacks", "seed", "target_seat"))


def median(values):
    return statistics.median(values) if values else None


def summarize(samples):
    baseline = {sample_key(row): row["victim_contact_to_death_ms"]
                for row in samples if row["arm"] == "neither"}
    groups = defaultdict(list)
    for row in samples:
        groups[row["stacks"], row["arm"]].append(row)
    for (stacks, arm), batch in sorted(groups.items()):
        counts = Counter(row["outcome"] for row in batch)
        times = [row["victim_contact_to_death_ms"] for row in batch
                 if row["victim_contact_to_death_ms"] is not None]
        paired = [
            row["victim_contact_to_death_ms"] - baseline[sample_key(row)]
            for row in batch if row["victim_contact_to_death_ms"] is not None
            and baseline[sample_key(row)] is not None
        ]
        yield dict(
            stacks=stacks, arm=arm, tick_ms=25, builds=len({row["build"] for row in batch}),
            combat_seeds=len({row["seed"] for row in batch}), seat_exchanges=len(batch),
            target_wins=counts["target"], other_wins=counts["other"],
            simultaneous=counts["simultaneous"], timeouts=counts["timeout"],
            measured_ttk_n=len(times), median_victim_contact_to_death_ms=median(times),
            paired_ttk_n=len(paired), median_paired_delta_from_neither_ms=median(paired),
            median_landed_wishes=median([row["landed_wishes"] for row in batch]),
        )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("binary", type=Path)
    parser.add_argument("samples_csv", type=Path)
    parser.add_argument("summary_csv", type=Path)
    parser.add_argument("--jobs", type=int, default=6)
    args = parser.parse_args()
    if args.jobs < 1:
        parser.error("--jobs must be positive")
    binary = args.binary.resolve()
    catalog = json.loads(subprocess.check_output([str(binary), "--pvp", "--reward-catalog"]))
    reference = Path(__file__).resolve().parents[2] / "docs/evidence/raid-routing-ttk/stat_summary.csv"
    pairs = list(cases(catalog, reference))
    with ThreadPoolExecutor(max_workers=args.jobs) as pool:
        batches = pool.map(lambda chunk: list(execute(binary, chunk)),
                           [pairs[i::args.jobs] for i in range(args.jobs)])
        samples = sorted((row for batch in batches for row in batch),
                         key=lambda row: (row["arm"], sample_key(row)))
    write_csv(args.samples_csv, samples)
    write_csv(args.summary_csv, list(summarize(samples)))


if __name__ == "__main__":
    main()
