"""Fetch a held-out epoch (2016-2026) with the same bodies and GMs as the 2026 file.

The GMs are epoch-independent and are copied from horizons_real_2026_10y.json.
Only states are queried, so the model choices made on the 2026 window are
tested on data that was not used to choose them.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fetch_horizons as fh

EPOCH = "2016-01-01"
STOP = "2026-01-02"
OUT = "horizons_real_2016_10y.json"

BODIES = [
    ("Mercury", "199"),
    ("Venus", "299"),
    ("Earth", "399"),
    ("Moon", "301"),
    ("Mars", "4"),
    ("Jupiter", "5"),
    ("Saturn", "6"),
    ("Uranus", "7"),
    ("Neptune", "8"),
    ("Ceres", "1;"),
    ("Pallas", "2;"),
    ("Vesta", "4;"),
    ("Hygiea", "10;"),
    ("Interamnia", "704;"),
    ("Psyche", "16;"),
]


def main():
    fh.EPOCH = EPOCH
    fh.STOP = STOP
    here = os.path.dirname(os.path.abspath(__file__))
    with open(os.path.join(here, "horizons_real_2026_10y.json"), encoding="utf-8") as handle:
        source = json.load(handle)
    gm = {body["name"]: body for body in source["bodies"]}

    bodies = []
    for name, command in BODIES:
        states = fh.fetch_states(command)
        bodies.append({
            "name": name,
            "mu": gm[name]["mu"],
            "mu_body": gm[name]["mu_body"],
            "states": states,
        })
        print(name, "states=", len(states))

    data = {
        "epoch": EPOCH + " 00:00:00 TDB",
        "frame": "heliocentric ecliptic J2000",
        "units": "m, m/s",
        "source": "NASA/JPL Horizons DE441",
        "days": fh.DAYS,
        "bodies": bodies,
    }
    out = os.path.join(here, OUT)
    with open(out, "w", encoding="utf-8") as handle:
        json.dump(data, handle, indent=1)
    print("wrote", out)


if __name__ == "__main__":
    main()
