"""Fetch heliocentric ecliptic-J2000 states and GMs from JPL Horizons.

Writes horizons_real_2026_10y.json next to this script. The tests are offline
and read that file; rerun this only to change the epoch or bodies.

GMs come from Horizons OBJ_DATA='YES'. Barycenter 5/6/7/8 is the system
barycenter, so its mu is the planet GM plus the moons' GMs; mu_body is the
planet-only GM (for the center-vs-barycenter variants).
"""

import json
import os
import re
import urllib.parse
import urllib.request

EPOCH = "2026-01-01"
STOP = "2036-01-02"
STEP = "365 d"
DAYS = [365 * i for i in range(11)]

STATE_BODIES = [
    ("Mercury", "199", None),
    ("Venus", "299", None),
    ("Earth", "399", None),
    ("Moon", "301", None),
    ("Mars", "499", None),
    ("Jupiter", "5", "599"),
    ("Saturn", "6", "699"),
    ("Uranus", "7", "799"),
    ("Neptune", "8", "899"),
    ("Ceres", "1;", None),
    ("Pallas", "2;", None),
    ("Vesta", "4;", None),
    ("Hygiea", "10;", None),
    ("Interamnia", "704;", None),
    ("Psyche", "16;", None),
    ("JupiterCenter", "599", None),
    ("Io", "501", None),
    ("Europa", "502", None),
    ("Ganymede", "503", None),
    ("Callisto", "504", None),
    ("SaturnCenter", "699", None),
    ("Mimas", "601", None),
    ("Enceladus", "602", None),
    ("Tethys", "603", None),
    ("Dione", "604", None),
    ("Rhea", "605", None),
    ("Titan", "606", None),
    ("Hyperion", "607", None),
    ("Iapetus", "608", None),
]

MOONS = {
    "5": ["501", "502", "503", "504"],
    "6": ["601", "602", "603", "604", "605", "606", "607", "608"],
    "7": ["701", "702", "703", "704", "705"],
    "8": ["801"],
}


def api(params):
    url = "https://ssd.jpl.nasa.gov/api/horizons.api?" + urllib.parse.urlencode(params)
    with urllib.request.urlopen(url, timeout=120) as response:
        return response.read().decode("utf-8")


def fetch_states(command):
    params = {
        "format": "text",
        "COMMAND": "'" + command + "'",
        "EPHEM_TYPE": "'VECTORS'",
        "CENTER": "'500@10'",
        "START_TIME": "'" + EPOCH + "'",
        "STOP_TIME": "'" + STOP + "'",
        "STEP_SIZE": "'" + STEP + "'",
        "VEC_TABLE": "'2'",
        "REF_PLANE": "'ECLIPTIC'",
        "REF_SYSTEM": "'J2000'",
        "OUT_UNITS": "'KM-S'",
        "CSV_FORMAT": "'YES'",
        "OBJ_DATA": "'NO'",
        "MAKE_EPHEM": "'YES'",
    }
    text = api(params)
    inside = False
    rows = []
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
        jd = float(parts[0])
        x, y, z = (float(parts[2]) * 1000.0, float(parts[3]) * 1000.0, float(parts[4]) * 1000.0)
        vx, vy, vz = (float(parts[5]) * 1000.0, float(parts[6]) * 1000.0, float(parts[7]) * 1000.0)
        rows.append((jd, x, y, z, vx, vy, vz))
    if len(rows) != len(DAYS):
        raise RuntimeError(command + ": expected " + str(len(DAYS)) + " rows, got " + str(len(rows)))
    jd0 = rows[0][0]
    states = []
    for index, row in enumerate(rows):
        day = row[0] - jd0
        if abs(day - DAYS[index]) > 1e-6:
            raise RuntimeError(command + ": day " + str(day) + " != " + str(DAYS[index]))
        states.append({
            "days": DAYS[index],
            "p": [row[1], row[2], row[3]],
            "v": [row[4], row[5], row[6]],
        })
    return states


def fetch_gm(command):
    params = {
        "format": "text",
        "COMMAND": "'" + command + "'",
        "OBJ_DATA": "'YES'",
        "MAKE_EPHEM": "'NO'",
    }
    text = api(params)
    match = re.search(
        r"GM\s*,?\s*(?:\(km\^3/s\^2\)|km\^3/s\^2)\s*=\s*([0-9]+(?:\.[0-9]*)?(?:[Ee][+\-]?[0-9]+)?)",
        text)
    if not match:
        match = re.search(r"GM\s*=\s*([0-9]+(?:\.[0-9]*)?(?:[Ee][+\-]?[0-9]+)?)", text)
    if not match:
        raise RuntimeError("no GM for " + command)
    return float(match.group(1)) * 1e9


def main():
    gm_cache = {}

    def gm(command):
        if command not in gm_cache:
            gm_cache[command] = fetch_gm(command)
        return gm_cache[command]

    system_gm = {}
    for barycenter, planet in (("5", "599"), ("6", "699"), ("7", "799"), ("8", "899")):
        total = gm(planet)
        for moon in MOONS[barycenter]:
            total += gm(moon)
        system_gm[barycenter] = total
        print("system", barycenter, "=", total)

    bodies = []
    for name, command, planet_center in STATE_BODIES:
        # Mars has moons: use the system barycentre state but the body GM, so a
        # point-mass model does not inherit the Phobos/Deimos centre wobble.
        state_command = "4" if name == "Mars" else command
        states = fetch_states(state_command)
        if name == "Mars":
            mu = gm("499")
            mu_body = mu
        elif planet_center is not None:
            mu = system_gm[command]
            mu_body = gm(planet_center)
        else:
            mu = gm(command)
            mu_body = mu
        bodies.append({
            "name": name,
            "mu": mu,
            "mu_body": mu_body,
            "states": states,
        })
        print(name, "mu=", mu, "mu_body=", mu_body, "states=", len(states))

    data = {
        "epoch": "2026-01-01 00:00:00 TDB",
        "frame": "heliocentric ecliptic J2000",
        "units": "m, m/s",
        "source": "NASA/JPL Horizons DE441",
        "days": DAYS,
        "bodies": bodies,
    }
    out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "horizons_real_2026_10y.json")
    with open(out, "w", encoding="utf-8") as handle:
        json.dump(data, handle, indent=1)
    print("wrote", out)


if __name__ == "__main__":
    main()
