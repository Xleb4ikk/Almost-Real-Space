"""Replace the Mars body-centre state ("499") with the Mars system barycentre ("4").

The point-mass model has no Phobos/Deimos, so the body-centre wobble velocity
(~3e-5 m/s) becomes a spurious linear drift (~3 m/day). The GM stays the Mars
body GM; the moons' total mass is ~2e-8 of Mars (effect ~1 m over 10 years).
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fetch_horizons as fh
import fetch_short as fs

HERE = os.path.dirname(os.path.abspath(__file__))
MAIN = [
    ("2026-01-01", "2036-01-02", "horizons_real_2026_10y.json"),
    ("2016-01-01", "2026-01-02", "horizons_real_2016_10y.json"),
]
SHORT = [
    ("2026-01-01", "horizons_short_2026.json"),
    ("2016-01-01", "horizons_short_2016.json"),
]


def replace(path, states):
    with open(path, encoding="utf-8") as handle:
        data = json.load(handle)
    for body in data["bodies"]:
        if body["name"] == "Mars":
            body["states"] = states
            break
    else:
        raise RuntimeError("Mars not found in " + path)
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(data, handle, indent=1)
    print("updated", path)


def main():
    for epoch, stop, name in MAIN:
        fh.EPOCH = epoch
        fh.STOP = stop
        states = fh.fetch_states("4")
        replace(os.path.join(HERE, name), states)
        print(" ", epoch, "yearly states", len(states))

    for epoch, name in SHORT:
        states = fs.fetch_daily("4", epoch)
        replace(os.path.join(HERE, name), states)
        print(" ", epoch, "short states", len(states))


if __name__ == "__main__":
    main()
