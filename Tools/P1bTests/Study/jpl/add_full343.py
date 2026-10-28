"""Add all 343 DE441 main-belt perturbers to both epochs.

The 343 numbers and exact DE441 GMs (au^3/day^2) are in de441_asteroids.csv,
extracted from JPL IOM 392R-21-005, Table 1. States are Horizons SB441
ephemerides. The 50 already present under names are copied; the rest are
fetched. Full-model bodies are named Ast<number> to avoid moon collisions.
The JSON is written incrementally so a dropped Horizons connection resumes.
"""

import csv
import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fetch_horizons as fh
from add_top50 import TOP50

HERE = os.path.dirname(os.path.abspath(__file__))
AU = 1.495978707e11
DAY = 86400.0
AU3_DAY2 = AU * AU * AU / (DAY * DAY)
MAIN = [
    ("2026-01-01", "2036-01-02", "horizons_real_2026_10y.json"),
    ("2016-01-01", "2026-01-02", "horizons_real_2016_10y.json"),
]


def load_rows():
    path = os.path.join(HERE, "de441_asteroids.csv")
    with open(path, encoding="utf-8") as handle:
        return [(int(r["number"]), float(r["gm_au3d2"])) for r in csv.DictReader(handle)]


def fetch_retry(command):
    last = None
    for attempt in range(5):
        try:
            return fh.fetch_states(command)
        except Exception as exc:
            last = exc
            print("retry", command, "attempt", attempt + 1, exc)
            time.sleep(1.5 * (attempt + 1))
    raise last


def write(path, data):
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(data, handle, indent=1)


def main():
    rows = load_rows()
    print("asteroids", len(rows), "total GM", sum(g for _, g in rows) * AU3_DAY2)
    named = {}
    for name, command, _ in TOP50:
        named[int(command[:-1])] = name

    for epoch, stop, name in MAIN:
        path = os.path.join(HERE, name)
        with open(path, encoding="utf-8") as handle:
            data = json.load(handle)
        by_name = {body["name"]: body for body in data["bodies"]}
        fh.EPOCH = epoch
        fh.STOP = stop
        added = 0
        skipped = 0
        for number, gm_au in rows:
            body_name = "Ast" + str(number)
            mu = gm_au * AU3_DAY2
            if body_name in by_name:
                by_name[body_name]["mu"] = mu
                by_name[body_name]["mu_body"] = mu
                continue
            source = named.get(number)
            if source is not None and source in by_name:
                states = by_name[source]["states"]
            else:
                try:
                    states = fetch_retry(str(number) + ";")
                except Exception as exc:
                    print("FAILED", number, exc)
                    skipped += 1
                    continue
                time.sleep(0.2)
            body = {
                "name": body_name,
                "mu": mu,
                "mu_body": mu,
                "states": states,
            }
            data["bodies"].append(body)
            by_name[body_name] = body
            added += 1
            if added % 20 == 0:
                write(path, data)
                print(name, "progress", added, "added")
        write(path, data)
        print(name, "added", added, "skipped", skipped, "total", len(data["bodies"]))


if __name__ == "__main__":
    main()
