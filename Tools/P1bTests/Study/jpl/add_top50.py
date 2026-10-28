"""Add the 50 most massive DE441 main-belt asteroids to both epochs.

GMs are the exact DE441 values from JPL IOM 392R-21-005, Table 1 (au^3/day^2).
The first 16 (SB441-N16) are already present; the next 34 are fetched here.
'Europa' (52) and 'Io' (85) are renamed to avoid collisions with Jovian moons.
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

TOP50 = [
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
    ("Dido", "209;", 1.9511008134506610e-15),
    ("Aurora", "94;", 1.9476499609659456e-15),
    ("Doris", "48;", 1.9085161956485640e-15),
    ("Bertha", "154;", 1.9062882233624486e-15),
    ("Amphitrite", "29;", 1.7788759570378772e-15),
    ("Ursula", "375;", 1.7716875294908651e-15),
    ("Herculina", "532;", 1.7651295651126369e-15),
    ("Stereoskopia", "566;", 1.7007818925283807e-15),
    ("Siegena", "386;", 1.6744520343182799e-15),
    ("Alauda", "702;", 1.6730262398441171e-15),
    ("Laetitia", "39;", 1.6238080168284406e-15),
    ("Metis", "9;", 1.4490138025867273e-15),
    ("Hebe", "6;", 1.4423591761343029e-15),
    ("Bamberga", "324;", 1.3808681586222395e-15),
    ("Elektra", "130;", 1.3272835717079433e-15),
    ("Themis", "24;", 1.3109170444249925e-15),
    ("Patientia", "451;", 1.2973797046097596e-15),
    ("Fortuna", "19;", 1.2396175182794244e-15),
    ("Palma", "372;", 1.2340564359220072e-15),
    ("Daphne", "41;", 1.2055616051144469e-15),
    ("Irene", "14;", 1.1863795139948057e-15),
    ("Diotima", "423;", 1.1511452195400677e-15),
    ("Egeria", "13;", 1.1389436761809467e-15),
    ("Hesperia", "69;", 1.1215489958719225e-15),
    ("Parthenope", "11;", 1.0258530487431674e-15),
    ("Papagena", "471;", 1.0154946317000345e-15),
    ("Lachesis", "120;", 1.0049283867977807e-15),
    ("Aglaja", "47;", 9.5484521825652372e-16),
    ("Aegle", "96;", 9.4294212130582306e-16),
    ("Hedwig", "476;", 9.3290849487079058e-16),
    ("Aspasia", "409;", 9.3071301802989339e-16),
    ("Juewa", "139;", 9.2595552169711510e-16),
    ("Winchester", "747;", 8.9162160614799751e-16),
    ("Kalliope", "22;", 8.8054945035882155e-16),
    ("Io85", "85;", 8.4105681134479927e-16),
]
MAIN = [
    ("2026-01-01", "2036-01-02", "horizons_real_2026_10y.json"),
    ("2016-01-01", "2026-01-02", "horizons_real_2016_10y.json"),
]


def main():
    total = sum(gm for _, _, gm in TOP50) * AU3_DAY2
    print("top50 total GM =", total, "m^3/s^2")
    for epoch, stop, name in MAIN:
        path = os.path.join(HERE, name)
        with open(path, encoding="utf-8") as handle:
            data = json.load(handle)
        by_name = {body["name"]: body for body in data["bodies"]}
        fh.EPOCH = epoch
        fh.STOP = stop
        for asteroid, command, gm_au in TOP50:
            mu = gm_au * AU3_DAY2
            if asteroid in by_name:
                by_name[asteroid]["mu"] = mu
                by_name[asteroid]["mu_body"] = mu
            else:
                states = fh.fetch_states(command)
                data["bodies"].append({
                    "name": asteroid,
                    "mu": mu,
                    "mu_body": mu,
                    "states": states,
                })
                print(name, asteroid, "added mu=", mu)
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(data, handle, indent=1)
        print("wrote", path)


if __name__ == "__main__":
    main()
