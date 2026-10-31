"""Generate the Core-only two-body golden files.

The oracle is the closed-form Kepler propagation in mpmath (50+ digits), not
the production integrator.  The initial state is first rounded to IEEE double
and then promoted to mpmath, so the C# test can use exactly the same doubles.

Run from this directory:
    python gen_core_kepler.py
"""

import json
import math
import mpmath as mp

mp.mp.dps = 70
MU = 3.986004418e14


def rotate_z(v, angle):
    c = mp.cos(angle)
    s = mp.sin(angle)
    return (c * v[0] - s * v[1], s * v[0] + c * v[1], v[2])


def rotate_x(v, angle):
    c = mp.cos(angle)
    s = mp.sin(angle)
    return (v[0], c * v[1] - s * v[2], s * v[1] + c * v[2])


def perifocal_state(a, e, nu, mu):
    a = mp.mpf(a)
    e = mp.mpf(e)
    nu = mp.mpf(nu)
    p = a * (1 - e * e)
    r = p / (1 + e * mp.cos(nu))
    vscale = mp.sqrt(mu / p)
    return (
        (r * mp.cos(nu), r * mp.sin(nu), mp.mpf(0)),
        (-vscale * mp.sin(nu), vscale * (e + mp.cos(nu)), mp.mpf(0)),
    )


def initial_double(a, e, inc_deg, raan_deg, argp_deg, nu_deg):
    inc = math.radians(inc_deg)
    raan = math.radians(raan_deg)
    argp = math.radians(argp_deg)
    nu = math.radians(nu_deg)
    p = a * (1 - e * e)
    r = p / (1 + e * math.cos(nu))
    vscale = math.sqrt(MU / p)
    rp = (r * math.cos(nu), r * math.sin(nu), 0.0)
    vp = (-vscale * math.sin(nu), vscale * (e + math.cos(nu)), 0.0)

    def rz(v, angle):
        c, s = math.cos(angle), math.sin(angle)
        return (c * v[0] - s * v[1], s * v[0] + c * v[1], v[2])

    def rx(v, angle):
        c, s = math.cos(angle), math.sin(angle)
        return (v[0], c * v[1] - s * v[2], s * v[1] + c * v[2])

    r0 = rz(rx(rz(rp, argp), inc), raan)
    v0 = rz(rx(rz(vp, argp), inc), raan)
    return r0, v0


def propagate(r0, v0, mu, total_time):
    mu = mp.mpf(mu)
    r0 = tuple(mp.mpf(repr(x)) for x in r0)
    v0 = tuple(mp.mpf(repr(x)) for x in v0)
    r = mp.sqrt(sum(x * x for x in r0))
    v2 = sum(x * x for x in v0)
    h = (
        r0[1] * v0[2] - r0[2] * v0[1],
        r0[2] * v0[0] - r0[0] * v0[2],
        r0[0] * v0[1] - r0[1] * v0[0],
    )
    hmag = mp.sqrt(sum(x * x for x in h))
    energy = v2 / 2 - mu / r
    a = -mu / (2 * energy)
    evec = (
        (v2 - mu / r) * r0[0] - (r0[0] * v0[0] + r0[1] * v0[1] + r0[2] * v0[2]) * v0[0],
        (v2 - mu / r) * r0[1] - (r0[0] * v0[0] + r0[1] * v0[1] + r0[2] * v0[2]) * v0[1],
        (v2 - mu / r) * r0[2] - (r0[0] * v0[0] + r0[1] * v0[1] + r0[2] * v0[2]) * v0[2],
    )
    evec = tuple(x / mu for x in evec)
    e = mp.sqrt(sum(x * x for x in evec))
    p = hmag * hmag / mu
    hhat = tuple(x / hmag for x in h)
    phat = tuple(x / e for x in evec)
    qhat = (
        hhat[1] * phat[2] - hhat[2] * phat[1],
        hhat[2] * phat[0] - hhat[0] * phat[2],
        hhat[0] * phat[1] - hhat[1] * phat[0],
    )
    nu0 = mp.atan2(
        r0[0] * qhat[0] + r0[1] * qhat[1] + r0[2] * qhat[2],
        r0[0] * phat[0] + r0[1] * phat[1] + r0[2] * phat[2],
    )

    if a > 0:
        n = mp.sqrt(mu / (a * a * a))
        ecc0 = 2 * mp.atan2(mp.sqrt(1 - e) * mp.sin(nu0 / 2), mp.sqrt(1 + e) * mp.cos(nu0 / 2))
        mean = ecc0 - e * mp.sin(ecc0) + n * mp.mpf(total_time)
        f = lambda E: E - e * mp.sin(E) - mean
        ecc = mp.findroot(f, (mean - e, mean + e), solver="bisect", tol=mp.mpf("1e-55"))
        nu = 2 * mp.atan2(mp.sqrt(1 + e) * mp.sin(ecc / 2), mp.sqrt(1 - e) * mp.cos(ecc / 2))
        radius = p / (1 + e * mp.cos(nu))
        vscale = mp.sqrt(mu / p)
        return (
            tuple(phat[i] * radius * mp.cos(nu) + qhat[i] * radius * mp.sin(nu) for i in range(3)),
            tuple(phat[i] * -vscale * mp.sin(nu) + qhat[i] * vscale * (e + mp.cos(nu)) for i in range(3)),
        )

    alpha = -a
    half = mp.sqrt((e - 1) / (e + 1)) * mp.tan(nu0 / 2)
    h0 = 2 * mp.atanh(half)
    mean = e * mp.sinh(h0) - h0 + mp.sqrt(mu / (alpha ** 3)) * mp.mpf(total_time)
    f = lambda H: e * mp.sinh(H) - H - mean
    hyp = mp.findroot(f, mp.asinh(mean / e), tol=mp.mpf("1e-55"))
    radius = a * (1 - e * mp.cosh(hyp))
    x = a * (mp.cosh(hyp) - e)
    y = -a * mp.sqrt(e * e - 1) * mp.sinh(hyp)
    vx = -mp.sqrt(mu * alpha) * mp.sinh(hyp) / radius
    vy = mp.sqrt(mu * alpha) * mp.sqrt(e * e - 1) * mp.cosh(hyp) / radius
    return (
        tuple(phat[i] * x + qhat[i] * y for i in range(3)),
        tuple(phat[i] * vx + qhat[i] * vy for i in range(3)),
    )


def make_case(case_id, a, e, inc, raan, argp, nu, duration, checkpoint_fractions=(0.25, 0.5, 0.75)):
    r0, v0 = initial_double(a, e, inc, raan, argp, nu)
    p, v = propagate(r0, v0, MU, duration)
    checkpoints = []
    for fraction in checkpoint_fractions:
        cp, cv = propagate(r0, v0, MU, duration * fraction)
        checkpoints.append({
            "fraction": fraction,
            "P": [mp.nstr(x, 60) for x in cp],
            "V": [mp.nstr(x, 60) for x in cv],
        })
    return {
        "id": case_id,
        "mu": MU,
        "duration": duration,
        "r0": [format(x, ".17g") for x in r0],
        "v0": [format(x, ".17g") for x in v0],
        "P": [mp.nstr(x, 60) for x in p],
        "V": [mp.nstr(x, 60) for x in v],
        "checkpoints": checkpoints,
        "digits": 60,
    }


def period(a):
    return 2 * math.pi * math.sqrt(a ** 3 / MU)


FAST = [
    make_case("golden-kepler-circular-1p", 7.0e6, 0.0, 0.0, 0.0, 0.0, 0.0, period(7.0e6)),
    make_case("golden-kepler-e05-1p", 1.0e7, 0.5, 25.0, 40.0, 60.0, 20.0, period(1.0e7)),
    make_case("golden-kepler-e09-1p", 1.0e7, 0.9, 0.0, 0.0, 0.0, 0.0, period(1.0e7)),
    make_case("golden-kepler-e099-1p", 1.0e7, 0.99, 0.0, 0.0, 0.0, 0.0, period(1.0e7)),
    make_case("golden-kepler-near-parabolic-100s", 1.0e7, 0.999, 0.0, 0.0, 0.0, 0.0, 100.0),
    make_case("golden-kepler-hyperbolic-1h", -1.4e7, 1.5, 0.0, 0.0, 0.0, 0.0, 3600.0),
    make_case("golden-kepler-inclined-1p", 8.0e6, 0.2, 63.4, 30.0, 45.0, 10.0, period(8.0e6)),
]

SLOW = [
    make_case("golden-kepler-e05-1y", 1.0e7, 0.5, 25.0, 40.0, 60.0, 20.0, 365.25 * 86400.0),
]

for name, cases in (("golden_core_kepler.json", FAST), ("golden_core_kepler_1y.json", SLOW)):
    with open(name, "w", encoding="utf-8") as handle:
        json.dump({"generator": "gen_core_kepler.py", "model": "two-body, central at origin", "cases": cases}, handle, indent=1)
        handle.write("\n")
    print("wrote", name, "with", len(cases), "cases")
