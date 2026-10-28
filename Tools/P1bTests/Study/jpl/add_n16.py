"""Add the 16 most massive DE441 asteroids (SB441-N16) to both epochs.

GMs are the exact DE441 values from JPL IOM 392R-21-005, Table 1 (au^3/day^2).
States come from Horizons, whose SB441-N16 ephemeris is the one DE441 uses.
The six asteroids already present keep their states and get the DE441 GM.
"""

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fetch_horizons as fh

HERE = os.path.dirname(os.path.abspath(__file__))
AU = 1.495978707e11
DAY = 86400.0
AU3_DAY2 = AU * AU * AU / (DAY * DAY)

# JPL IOM 392R-21-005, Table 1 (SB441-N16, first 16 entries).
N16 = [
    ("Ceres", "1;", 1.3964518123081070e-13),
    ("Vesta", "4;", 3.8548000225257904e-14),
    ("Pallas", "2;", 3.0471146330043200e-14),
    ("Hygiea", "10;", 1.2542530761640810e-14),
    ("Davida", "511;", 8.6836253492286545e-15),
    ("Interamnia", "704;", 6.3110343420878887e-15),
    ("Europa52", "52;", 5.9824315264869841e-15),
    ("Sylvia", "87;", 4.8345606546105521e-15),
    ("Eunomia", "15;", 4.5107799051436795e-15),
    ("Juno", "3;", 4.2823439677995011e-15),
    ("Psyche", "16;", 3.5445002842488978e-15),
    ("Camilla", "107;", 3.2191392075878588e-15),
    ("Thisbe", "88;", 2.6529436610356353e-15),
    ("Iris", "7;", 2.5416014973471498e-15),
    ("Euphrosyne", "31;", 2.4067012218937576e-15),
    ("Cybele", "65;", 2.0917175955133682e-15),
]
MAIN = [
    ("2026-01-01", "2036-01-02", "horizons_real_2026_10y.json"),
    ("2016-01-01", "2026-01-02", "horizons_real_2016_10y.json"),
]


def main():
    total = sum(gm for _, _, gm in N16) * AU3_DAY2
    print("N16 total GM =", total, "m^3/s^2")
    for epoch, stop, name in MAIN:
        path = os.path.join(HERE, name)
        with open(path, encoding="utf-8") as handle:
            data = json.load(handle)
        by_name = {body["name"]: body for body in data["bodies"]}
        fh.EPOCH = epoch
        fh.STOP = stop
        for asteroid, command, gm_au in N16:
            mu = gm_au * AU3_DAY2
            if asteroid in by_name:
                by_name[asteroid]["mu"] = mu
                by_name[asteroid]["mu_body"] = mu
                print(name, asteroid, "GM updated")
            else:
                states = fh.fetch_states(command)
                data["bodies"].append({
                    "name": asteroid,
                    "mu": mu,
                    "mu_body": mu,
                    "states": states,
                })
                print(name, asteroid, "added mu=", mu, "states=", len(states))
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(data, handle, indent=1)
        print("wrote", path)


if __name__ == "__main__":
    main()
