"""Add the 30 DE441 Kuiper-belt perturbers to both epochs.

GMs are from JPL IOM 392R-21-005, Table 1 (au^3/day^2). Numbered objects use
'number;', unnumbered ones use their provisional designation. Bodies are named
Kbo<number-or-designation> so they cannot collide with moons or asteroids.
"""

import json
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fetch_horizons as fh

HERE = os.path.dirname(os.path.abspath(__file__))
AU = 1.495978707e11
DAY = 86400.0
AU3_DAY2 = AU * AU * AU / (DAY * DAY)

# Name, number (or None), GM in au^3/day^2.
KBOS = [
    ("Orcus", 90482, 9.4096246431561093e-14),
    ("2002 MS4", 307261, 7.6535017490671978e-14),
    ("2006 QH181", None, 6.9916025897378817e-14),
    ("Salacia", 120347, 6.5117020793631265e-14),
    ("2003 UZ413", 455502, 6.4838580755817233e-14),
    ("2003 AZ84", 208996, 6.0821235283947692e-14),
    ("2002 AW197", 55565, 6.0247324245997600e-14),
    ("Haumea", 136108, 5.9616300545042843e-13),
    ("Varuna", 20000, 5.4950752234118650e-14),
    ("Ixion", 28978, 4.5176656225097590e-14),
    ("Varda", 174567, 3.9657768869641912e-14),
    ("2004 GV9", 90568, 3.7193502373824432e-14),
    ("2004 XA192", 230965, 3.6450226020091094e-15),
    ("2005 RN43", 145452, 3.6285479917789626e-14),
    ("Makemake", 136472, 3.4318555006292089e-13),
    ("2010 KZ39", None, 2.9579875156960054e-14),
    ("2004 XR190", None, 2.9040995181884837e-14),
    ("2001 UR163", 42301, 2.7251938920337723e-14),
    ("2007 JJ43", 278361, 2.6769158975337699e-14),
    ("Gonggong", 225088, 2.5856788277090390e-13),
    ("2010 FX86", None, 2.5710342570810209e-14),
    ("Chaos", 19521, 2.5383796456297310e-14),
    ("Eris", 136199, 2.4854480302873160e-12),
    ("2002 TC302", 84522, 2.2819963431204538e-13),
    ("2008 ST291", 528381, 2.1338903510045792e-14),
    ("2010 RE64", 523639, 1.9128866968694360e-14),
    ("2002 UX25", 55637, 1.8603512445448687e-14),
    ("2010 RF43", None, 1.7561425934907343e-14),
    ("Quaoar", 50000, 1.5294877459751145e-13),
    ("Sedna", 90377, 1.4934867884244863e-13),
]
MAIN = [
    ("2026-01-01", "2036-01-02", "horizons_real_2026_10y.json"),
    ("2016-01-01", "2026-01-02", "horizons_real_2016_10y.json"),
]


def command_of(name, number):
    return (str(number) + ";") if number is not None else name


def kbo_name(name, number):
    if number is not None:
        return "Kbo" + str(number)
    return "Kbo" + "".join(ch for ch in name if ch.isalnum())


def fetch_retry(command):
    last = None
    for attempt in range(5):
        try:
            return fh.fetch_states(command)
        except Exception as exc:
            last = exc
            print("retry", command, attempt + 1, exc)
            time.sleep(1.5 * (attempt + 1))
    raise last


def main():
    total = sum(gm for _, _, gm in KBOS) * AU3_DAY2
    print("KBOs", len(KBOS), "total GM", total)
    for epoch, stop, name in MAIN:
        path = os.path.join(HERE, name)
        with open(path, encoding="utf-8") as handle:
            data = json.load(handle)
        by_name = {body["name"]: body for body in data["bodies"]}
        fh.EPOCH = epoch
        fh.STOP = stop
        added = 0
        for label, number, gm_au in KBOS:
            body_name = kbo_name(label, number)
            mu = gm_au * AU3_DAY2
            if body_name in by_name:
                by_name[body_name]["mu"] = mu
                by_name[body_name]["mu_body"] = mu
                continue
            states = fetch_retry(command_of(label, number))
            time.sleep(0.2)
            data["bodies"].append({
                "name": body_name,
                "mu": mu,
                "mu_body": mu,
                "states": states,
            })
            added += 1
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(data, handle, indent=1)
        print(name, "added", added, "total", len(data["bodies"]))


if __name__ == "__main__":
    main()
