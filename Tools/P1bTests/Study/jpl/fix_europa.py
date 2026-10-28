"""Resolve the 52 Europa / Jovian moon Europa name collision.

The 2026 file contains the Jovian moon 'Europa' (502). The N16 asteroid 52
Europa must therefore be stored as 'Europa52'. The 2016 holdout file has no
Jovian moons, so its 'Europa' entry is renamed.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fetch_horizons as fh

HERE = os.path.dirname(os.path.abspath(__file__))
AU = 1.495978707e11
DAY = 86400.0
GM_EUROPA52 = 5.9824315264869841e-15 * AU * AU * AU / (DAY * DAY)
GM_JOVIAN_EUROPA = 3.2027121e12
MAIN = [
    ("2026-01-01", "2036-01-02", "horizons_real_2026_10y.json"),
    ("2016-01-01", "2026-01-02", "horizons_real_2016_10y.json"),
]


def set_body(body, states):
    body["mu"] = GM_EUROPA52
    body["mu_body"] = GM_EUROPA52
    body["states"] = states


def main():
    for epoch, stop, name in MAIN:
        path = os.path.join(HERE, name)
        with open(path, encoding="utf-8") as handle:
            data = json.load(handle)
        fh.EPOCH = epoch
        fh.STOP = stop
        states = fh.fetch_states("52;")
        by_name = {body["name"]: body for body in data["bodies"]}
        if name.startswith("horizons_real_2026"):
            if "Europa52" not in by_name:
                body = {"name": "Europa52", "mu": 0.0, "mu_body": 0.0, "states": []}
                data["bodies"].append(body)
                by_name["Europa52"] = body
            set_body(by_name["Europa52"], states)
            # Restore the Jovian moon overwritten by add_n16.py.
            moon = by_name["Europa"]
            moon["mu"] = GM_JOVIAN_EUROPA
            moon["mu_body"] = GM_JOVIAN_EUROPA
        else:
            if "Europa52" in by_name:
                set_body(by_name["Europa52"], states)
            elif "Europa" in by_name:
                set_body(by_name["Europa"], states)
                by_name["Europa"]["name"] = "Europa52"
            else:
                data["bodies"].append({
                    "name": "Europa52",
                    "mu": GM_EUROPA52,
                    "mu_body": GM_EUROPA52,
                    "states": states,
                })
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(data, handle, indent=1)
        print("updated", path)


if __name__ == "__main__":
    main()
