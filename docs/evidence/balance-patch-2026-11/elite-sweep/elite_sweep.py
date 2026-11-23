#!/usr/bin/env python3
"""Per-elite sweep driver (coordinator scratch tool, not part of the repository).

Usage: elite_sweep.py <worktree> <out.jsonl> <seeds> <jobs> <label>=<spec> [<label>=<spec> ...]
A spec is "hp,dmg" applied to every elite kind, or "kind:hp:dmg;kind:hp:dmg;..." for per-kind rows
(kinds not named get no row). For each spec it rewrites the Rework per-kind table in mob_runtime.rs,
rebuilds camp_bench, runs the Duelist elite bench and appends one JSON line of per-kind banked_12 results.
The source file is restored at the end.
"""
import json
import os
import subprocess
import sys
import time

KINDS = {
    "wyrmling": "Wyrmling", "ashDrake": "AshDrake", "executioner": "Executioner", "gaoler": "Gaoler",
    "ratOgre": "RatOgre", "poxBehemoth": "PoxBehemoth", "glacialWarden": "GlacialWarden",
    "avalancheTroll": "AvalancheTroll", "bloodSaint": "BloodSaint", "archon": "Archon",
}


def rows_of(spec):
    if ":" in spec:
        out = {}
        for part in spec.split(";"):
            kind, hp, dmg = part.split(":")
            out[kind] = (float(hp), float(dmg))
        return out
    hp, dmg = spec.split(",")
    return {k: (float(hp), float(dmg)) for k in KINDS}


def table(rows):
    if not rows:
        return "    per_kind: &[],"
    body = "".join(
        f"\n        EliteKindRow {{ ty: MobTypeId::{KINDS[k]}, hp: {hp!r}, dmg: {dmg!r} }},"
        for k, (hp, dmg) in rows.items()
    )
    return f"    per_kind: &[{body}\n    ],"


def main():
    wt, out, seeds, jobs = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]
    specs = [a.split("=", 1) for a in sys.argv[5:]]
    src = os.path.join(wt, "crates/rh-sim/src/mob_runtime.rs")
    original = open(src, encoding="utf-8").read()
    marker = "static REWORK_ELITES"
    at = original.index(marker)
    empty = "    per_kind: &[],"
    cut = original.index(empty, at)
    env = dict(os.environ, PATH=os.path.expanduser("~/.cargo/bin") + ":" + os.environ["PATH"])
    try:
        for label, spec in specs:
            rows = rows_of(spec)
            # an all-ones spec is the real (empty) table
            if all(v == (1.0, 1.0) for v in rows.values()):
                rows = {}
            open(src, "w", encoding="utf-8").write(original[:cut] + table(rows) + original[cut + len(empty):])
            t0 = time.time()
            subprocess.run(
                ["cargo", "build", "-q", "--release", "-p", "rh-headless", "--features", "elite-bench",
                 "--bin", "camp_bench"], cwd=wt, env=env, check=True)
            run = subprocess.run(
                [os.path.join(wt, "target/release/camp_bench"), "--elite", "--fighter", "duelist", "--seed", "1",
                 "--seeds", str(seeds), "--jobs", jobs], cwd=wt, env=env, check=True, capture_output=True, text=True)
            rep = json.loads(run.stdout)
            rec = {"label": label, "spec": spec, "seeds": seeds, "secs": round(time.time() - t0), "kinds": {}}
            for camp in rep["per_camp"]:
                col = next(c for c in camp["columns"] if c["column"] == "banked_12")
                rec["kinds"][camp["kind"]] = {
                    "n": col["engaged"], "died": col["deaths"], "cleared": col["cleared"],
                    "truncated": col["truncated"], "rate": round(col["death_rate"], 4),
                }
            rec["overall"] = round(rep["overall"]["death_rate"], 4) if "death_rate" in rep.get("overall", {}) else None
            rec["weapons"] = {
                w["weapon"]: {"n": w["engaged"], "died": w["deaths"], "truncated": w["truncated"]}
                for w in rep.get("by_weapon", [])
                if w.get("fighter") == "duelist" and w.get("column") == "banked_12"
            }
            # per kind: mean ticks of a cleared fight and mean damage taken (the time and hp an elite costs)
            for kind, k in rec["kinds"].items():
                cells = [c for c in rep["cells"] if c["kind"] == kind and c["column"] == "banked_12"
                         and c.get("fighter", "duelist") == "duelist"]
                won = [c for c in cells if c["outcome"] == "cleared"]
                k["clear_ticks"] = round(sum(c["ticks"] for c in won) / len(won)) if won else None
                k["taken_share"] = round(sum(c["damage_taken"] / c["hp_start"] for c in won) / len(won), 3) if won else None
            with open(out + "." + label + ".json", "w", encoding="utf-8") as f:
                f.write(run.stdout)
            with open(out, "a", encoding="utf-8") as f:
                f.write(json.dumps(rec) + "\n")
            print(label, spec, rec["secs"], "s", " ".join(f"{k}={v['rate']:.3f}" for k, v in rec["kinds"].items()),
                  flush=True)
    finally:
        open(src, "w", encoding="utf-8").write(original)


if __name__ == "__main__":
    main()
