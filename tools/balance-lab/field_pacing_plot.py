"""Plot field camp encounters and full-health player duels on their respective clocks."""
import argparse
import csv
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt


def read(path):
    with path.open() as stream:
        return list(csv.DictReader(stream))


def plot(field, duels, output):
    figure, (camps, players) = plt.subplots(1, 2, figsize=(10.5, 4.8), layout="constrained")
    bands = ["0-5", "5-10", "10-15", "15-20", "20+"]
    for group, label, color, offset in (
        ("normal_clear_10s", "Ordinary camp", "#106c77", -0.11),
        ("elite_full_clear_10s", "Regional elite", "#a34820", 0.11),
    ):
        entries = {r["band"]: r for r in read(field) if r["cohort"] == "final" and r["group"] == group}
        marks = [(i + offset, entries[band]) for i, band in enumerate(bands) if int(entries[band]["n"])]
        x, median, low, high = [], [], [], []
        for position, row in marks:
            center = float(row["median_s"])
            x.append(position)
            median.append(center)
            low.append(center - float(row["p25_s"]))
            high.append(float(row["p75_s"]) - center)
        camps.errorbar(x, median, yerr=(low, high), fmt="o", color=color,
                       label=label, capsize=4, markersize=6, linewidth=1.6)
    camps.set(title="Camp clears in the final 12 raids", ylabel="Clear time (seconds)",
              xlabel="Raid age at engagement (minutes)", xticks=range(len(bands)),
              xticklabels=["0–5", "5–10", "10–15", "15–20", "20+"], ylim=(0, None))
    camps.legend(frameon=False, loc="upper left")
    camps.text(0.04, 0.04, "Points show medians. Bars span the middle 50%.",
               transform=camps.transAxes, fontsize=8)
    rows = sorted((r for r in read(duels) if r["group"] == "all" and r["comparator"] == "nearest"),
                  key=lambda r: int(r["minute"]))
    x = [int(r["minute"]) for r in rows]
    y = [float(r["median_mirror_ttk_s"]) for r in rows]
    players.plot(x, y, marker="o", color="#333f83", label="Earned farm kits", lw=2)
    for minute, seconds, row in zip(x, y, rows):
        players.annotate(f"{seconds:.1f} s\n{row['mirror_ttk_maps']} pairs",
                         (minute, seconds), xytext=(0, 8), textcoords="offset points",
                         ha="center", fontsize=8)
    players.scatter([0, 20], [25, 10], marker="D", s=38, color="#9a5a00",
                    label="Requested early and late times", zorder=3)
    players.set(title="Full-health player duels", ylabel="Time to first death (seconds)",
                xlabel="Raid minute when inventory was sampled", xticks=[0, 5, 10, 15, 20],
                xlim=(-1, 21.5), ylim=(0, 34))
    players.legend(frameon=False, loc="lower left")
    for axis in (camps, players):
        axis.grid(axis="y", alpha=0.2)
        axis.set_axisbelow(True)
        axis.spines[["top", "right"]].set_visible(False)
    figure.savefig(output, dpi=160, facecolor="white")
    plt.close(figure)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("field", type=Path)
    parser.add_argument("duels", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    plot(args.field, args.duels, args.output)
