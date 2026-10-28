"""Add the next three asteroids with a tabulated Horizons GM to both epochs.

Ceres/Pallas/Vesta are already present. Hygiea, Interamnia and Psyche are the
only additional asteroids for which Horizons returns a GM; the rest of the
main belt still needs literature masses.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fetch_horizons as fh

HERE = os.path.dirname(os.path.abspath(__file__))
ASTEROIDS = [
    ("Hygiea", "10;"),
    ("Interamnia", "704;"),
    ("Psyche", "16;"),
]
MAIN = [
    ("2026-01-01", "2036-01-02", "horizons_real_2026_10y.json"),
    ("2016-01-01", "2026-01-02", "horizons_real_2016_10y.json"),
]


def main():
    for epoch, stop, name in MAIN:
        path = os.path.join(HERE, name)
        with open(path, encoding="utf-8") as handle:
            data = json.load(handle)
        existing = {body["name"] for body in data["bodies"]}
        fh.EPOCH = epoch
        fh.STOP = stop
        for asteroid, command in ASTEROIDS:
            if asteroid in existing:
                print(name, asteroid, "already present")
                continue
            states = fh.fetch_states(command)
            mu = fh.fetch_gm(command)
            data["bodies"].append({
                "name": asteroid,
                "mu": mu,
                "mu_body": mu,
                "states": states,
            })
            print(name, asteroid, "mu=", mu, "states=", len(states))
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(data, handle, indent=1)
        print("wrote", path)


if __name__ == "__main__":
    main()
