"""Fetch daily states for the first year at two epochs for short-horizon diagnostics.

Only Mercury, Venus, Earth, Mars and Jupiter are needed; the full model state is
taken from the main files. GMs are copied from horizons_real_2026_10y.json.
"""

import datetime
import json
import os
import sys
import urllib.parse
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

EPOCHS = [
    ("2026-01-01", "horizons_short_2026.json"),
    ("2016-01-01", "horizons_short_2016.json"),
]
BODIES = [
    ("Mercury", "199"),
    ("Venus", "299"),
    ("Earth", "399"),
    ("Mars", "4"),
    ("Jupiter", "5"),
    ("Saturn", "6"),
    ("Uranus", "7"),
    ("Neptune", "8"),
]
DAYS = [0, 1, 10, 30, 90, 180, 365]


def api(params):
    url = "https://ssd.jpl.nasa.gov/api/horizons.api?" + urllib.parse.urlencode(params)
    with urllib.request.urlopen(url, timeout=120) as response:
        return response.read().decode("utf-8")


def fetch_daily(command, epoch):
    start = datetime.date.fromisoformat(epoch)
    stop = start + datetime.timedelta(days=365)
    params = {
        "format": "text",
        "COMMAND": "'" + command + "'",
        "EPHEM_TYPE": "'VECTORS'",
        "CENTER": "'500@10'",
        "START_TIME": "'" + start.isoformat() + " 00:00'",
        "STOP_TIME": "'" + stop.isoformat() + " 00:00'",
        "STEP_SIZE": "'1 d'",
        "VEC_TABLE": "'2'",
        "REF_PLANE": "'ECLIPTIC'",
        "REF_SYSTEM": "'J2000'",
        "OUT_UNITS": "'KM-S'",
        "CSV_FORMAT": "'YES'",
        "OBJ_DATA": "'NO'",
        "MAKE_EPHEM": "'YES'",
    }
    text = api(params)
    rows = []
    inside = False
    for line in text.splitlines():
        if line.strip() == "$$SOE":
            inside = True
            continue
        if line.strip() == "$$EOE":
            break
        if not inside or not line.strip():
            continue
        parts = [p.strip() for p in line.split(",")]
        if len(parts) < 8:
            continue
        rows.append((float(parts[0]),) + tuple(float(parts[i]) * 1000.0 for i in range(2, 8)))
    if len(rows) < 366:
        raise RuntimeError(command + ": expected 366 daily rows, got " + str(len(rows)))
    jd0 = rows[0][0]
    by_day = {int(round(row[0] - jd0)): row for row in rows}
    states = []
    for day in DAYS:
        row = by_day[day]
        states.append({
            "days": day,
            "p": [row[1], row[2], row[3]],
            "v": [row[4], row[5], row[6]],
        })
    return states


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    with open(os.path.join(here, "horizons_real_2026_10y.json"), encoding="utf-8") as handle:
        source = json.load(handle)
    gm = {body["name"]: body for body in source["bodies"]}

    for epoch, out_name in EPOCHS:
        bodies = []
        for name, command in BODIES:
            states = fetch_daily(command, epoch)
            bodies.append({
                "name": name,
                "mu": gm[name]["mu"],
                "mu_body": gm[name]["mu_body"],
                "states": states,
            })
            print(epoch, name, "days=", [state["days"] for state in states])

        data = {
            "epoch": epoch + " 00:00:00 TDB",
            "frame": "heliocentric ecliptic J2000",
            "units": "m, m/s",
            "source": "NASA/JPL Horizons DE441",
            "days": DAYS,
            "bodies": bodies,
        }
        out = os.path.join(here, out_name)
        with open(out, "w", encoding="utf-8") as handle:
            json.dump(data, handle, indent=1)
        print("wrote", out)


if __name__ == "__main__":
    main()
