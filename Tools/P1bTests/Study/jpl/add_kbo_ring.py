"""Add the DE441 Kuiper-belt ring: 36 equal masses at 44 au in the ecliptic.

The per-point GM is from the DE441 header.441 (the 36 equal final values):
0.552276997169882142e-12 au^3/day^2. The ring is circular, with equally
spaced phases and circular velocities; its total mass is exact, the phase
realization is the smooth-ring approximation used by DE441.
"""

import json
import math
import os

HERE = os.path.dirname(os.path.abspath(__file__))
AU = 1.495978707e11
DAY = 86400.0
GM_PER = 0.552276997169882142e-12 * AU * AU * AU / (DAY * DAY)
SUN_MU = 1.32712440041279419e20
R = 44.0 * AU
V = math.sqrt(SUN_MU / R)
COUNT = 36
MAIN = [
    ("horizons_real_2026_10y.json"),
    ("horizons_real_2016_10y.json"),
]


def main():
    print("ring per point", GM_PER, "total", GM_PER * COUNT)
    for name in MAIN:
        path = os.path.join(HERE, name)
        with open(path, encoding="utf-8") as handle:
            data = json.load(handle)
        by_name = {body["name"]: body for body in data["bodies"]}
        added = 0
        for k in range(COUNT):
            body_name = "Kbr" + str(k)
            if body_name in by_name:
                continue
            theta = 2.0 * math.pi * k / COUNT
            c = math.cos(theta)
            s = math.sin(theta)
            data["bodies"].append({
                "name": body_name,
                "mu": GM_PER,
                "mu_body": GM_PER,
                "states": [{
                    "days": 0,
                    "p": [R * c, R * s, 0.0],
                    "v": [-V * s, V * c, 0.0],
                }],
            })
            added += 1
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(data, handle, indent=1)
        print(name, "added", added, "total", len(data["bodies"]))


if __name__ == "__main__":
    main()
