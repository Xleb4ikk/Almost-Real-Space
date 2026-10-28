#!/usr/bin/env python3
"""Arbitrary-precision golden for the real baked system (Test504/516 ship case).

Model (identical to TaylorShip.Force / ForceDd):
  * every non-root body has a piecewise Chebyshev LOCAL state relative to its parent;
    world position = parent world position + local position, root at the origin;
  * ship acceleration = sum_i Mu_i (W_i - p)/|W_i - p|^3  - rootAccel,
    rootAccel = sum over direct children c of the root of SubtreeMu_c (W_c - W_0)/|W_c - W_0|^3.
The Chebyshev coefficients are taken as EXACT doubles and the piecewise polynomial is the
exact model (no re-fitting, no double evaluation of the ephemeris).

Usage
  python mp_golden_real.py real_bake_export.json --checkpoints 1,30 --out golden_real.json
  python mp_golden_real.py real_bake_export.json --certify          # (K,tol) vs (K+6,tol/1e6)
  python mp_golden_real.py --selftest                                 # synthetic system, no file needed

Assumptions about the JSON (override with flags if wrong; every guess is checked and reported):
  top-level T0 = ship start time (s), Days = horizon, ShipP0/ShipV0 = world state (doubles),
  RelP/RelV = state relative to body index Central, Bodies = list of bodies; Parent is a
  name, an index or null; Coeffs is flat, per segment: axis-major, (Degree+1) per axis.
"""
import argparse, json, math, os, sys, time
from mpmath import mp, mpf, floor as mfloor


ALPHA = mpf(-3) / 2


# ----------------------------------------------------------------------------- loading
def _g(d, *names, default=None):
    low = {k.lower(): v for k, v in d.items()}
    for n in names:
        if n.lower() in low:
            return low[n.lower()]
    if default is not None:
        return default
    raise KeyError("missing key, tried %s; have %s" % (names, list(d)))


class Body:
    pass


def load_system(path_or_dict):
    data = path_or_dict if isinstance(path_or_dict, dict) else json.load(
        open(path_or_dict, encoding="utf-8-sig"))
    raw = _g(data, "Bodies")
    names = [_g(b, "Name") for b in raw]
    bodies = []
    for i, b in enumerate(raw):
        o = Body()
        o.name = names[i]
        par = _g(b, "Parent", default=-1) if any(k.lower() == "parent" for k in b) else None
        if par in (None, "", -1):
            o.parent = None
        elif isinstance(par, int):
            o.parent = par
        else:
            o.parent = names.index(par)
        o.mu = mpf(float(_g(b, "Mu")))
        o.smu = mpf(float(_g(b, "SubtreeMu")))
        if o.parent is None:
            o.deg = 0; o.nseg = 0; o.coeffs = []; o.T0 = o.L = None
        else:
            o.T0 = mpf(float(_g(b, "T0")))
            o.L = mpf(float(_g(b, "SegmentLength")))
            o.deg = int(_g(b, "Degree"))
            o.nseg = int(_g(b, "Segments"))
            c = _g(b, "Coeffs")
            if c and isinstance(c[0], list):
                c = [x for row in c for x in row]
            if len(c) != o.nseg * 3 * (o.deg + 1):
                raise ValueError("%s: Coeffs length %d != Segments*3*(Degree+1)=%d"
                                 % (o.name, len(c), o.nseg * 3 * (o.deg + 1)))
            o.coeffs = [mpf(float(x)) for x in c]
        bodies.append(o)
    roots = [i for i, b in enumerate(bodies) if b.parent is None]
    if len(roots) != 1:
        raise ValueError("expected exactly one root body, got %s" % roots)
    # topological order, root first
    order, seen = [], set()
    def visit(i):
        if i in seen: return
        if bodies[i].parent is not None: visit(bodies[i].parent)
        seen.add(i); order.append(i)
    for i in range(len(bodies)): visit(i)
    sysd = dict(bodies=bodies, order=order, root=roots[0],
                root_children=[i for i, b in enumerate(bodies) if b.parent == roots[0]],
                central=int(_g(data, "Central")),
                t0=mpf(float(_g(data, "T0"))), days=mpf(float(_g(data, "Days"))),
                p0=[mpf(float(x)) for x in _g(data, "ShipP0")],
                v0=[mpf(float(x)) for x in _g(data, "ShipV0")],
                relp=[mpf(float(x)) for x in _g(data, "RelP")],
                relv=[mpf(float(x)) for x in _g(data, "RelV")])
    return sysd


# ------------------------------------------------------------------ ephemeris jets
def _segment(b, t):
    seg = int(mfloor((t - b.T0) / b.L))
    end = b.T0 + b.nseg * b.L
    if t < b.T0 or t > end:
        raise ValueError("t=%s outside bake range of %s [%s, %s]" % (t, b.name, b.T0, end))
    seg = max(0, min(b.nseg - 1, seg))
    return seg


def next_boundary(b, t):
    seg = int(mfloor((t - b.T0) / b.L))
    return b.T0 + min(b.nseg, seg + 1) * b.L


def local_jets(b, t, K):
    """power series in tau (time offset from t) of the 3 local coordinates, length K+1."""
    seg = _segment(b, t)
    start = b.T0 + seg * b.L
    u0 = 2 * (t - start) / b.L - 1
    du = 2 / b.L
    tk_prev = [mpf(0)] * (K + 1); tk_prev[0] = mpf(1)            # T0
    tk = [mpf(0)] * (K + 1); tk[0] = u0
    if K >= 1: tk[1] = du                                         # T1
    n1 = b.deg + 1
    base = seg * 3 * n1
    out = [[mpf(0)] * (K + 1) for _ in range(3)]
    def acc(Tj, k):
        for a in range(3):
            c = b.coeffs[base + a * n1 + k]
            o = out[a]
            for j in range(K + 1):
                o[j] += c * Tj[j]
    acc(tk_prev, 0)
    if b.deg >= 1: acc(tk, 1)
    for k in range(2, b.deg + 1):
        new = [mpf(0)] * (K + 1)
        for j in range(K + 1):
            v = 2 * u0 * tk[j] - tk_prev[j]
            if j: v += 2 * du * tk[j - 1]
            new[j] = v
        tk_prev, tk = tk, new
        acc(tk, k)
    return out


def world_jets(S, t, K):
    W = [None] * len(S["bodies"])
    for i in S["order"]:
        b = S["bodies"][i]
        if b.parent is None:
            W[i] = [[mpf(0)] * (K + 1) for _ in range(3)]
        else:
            L = local_jets(b, t, K)
            Pw = W[b.parent]
            W[i] = [[L[a][j] + Pw[a][j] for j in range(K + 1)] for a in range(3)]
    return W


def inv_cube_jet(d, K):
    """d: 3 jets (len K+1). returns f = d * |d|^-3 as 3 jets."""
    u = [sum(d[c][k] * d[c][m - k] for c in range(3) for k in range(m + 1)) for m in range(K + 1)]
    w = [None] * (K + 1)
    w[0] = 1 / (u[0] * mp.sqrt(u[0]))
    for m in range(1, K + 1):
        s = mpf(0)
        for k in range(m):
            s += (ALPHA * (m - k) - k) * u[m - k] * w[k]
        w[m] = s / (m * u[0])
    return [[sum(d[c][k] * w[m - k] for k in range(m + 1)) for m in range(K + 1)] for c in range(3)]


# --------------------------------------------------------------------- Taylor step
def step_coeffs(S, t, p, v, K, check=None):
    W = world_jets(S, t, K)
    bodies = S["bodies"]
    root_acc = [[mpf(0)] * (K + 1) for _ in range(3)]
    for c in S["root_children"]:
        f = inv_cube_jet([W[c][a] for a in range(3)], K)     # root world position is 0
        for a in range(3):
            for m in range(K + 1):
                root_acc[a][m] += bodies[c].smu * f[a][m]
    src = [i for i, b in enumerate(bodies) if b.mu != 0]
    P = [[mpf(0)] * (K + 2) for _ in range(3)]
    V = [[mpf(0)] * (K + 2) for _ in range(3)]
    A = [[mpf(0)] * (K + 1) for _ in range(3)]
    for a in range(3):
        P[a][0] = p[a]; V[a][0] = v[a]
    D = {i: [[mpf(0)] * (K + 1) for _ in range(3)] for i in src}
    U = {i: [mpf(0)] * (K + 1) for i in src}
    Wp = {i: [mpf(0)] * (K + 1) for i in src}
    for m in range(K):
        acc = [mpf(0)] * 3
        for i in src:
            d, u, w = D[i], U[i], Wp[i]
            for a in range(3):
                d[a][m] = W[i][a][m] - P[a][m]
            u[m] = sum(d[a][k] * d[a][m - k] for a in range(3) for k in range(m + 1))
            if m == 0:
                w[0] = 1 / (u[0] * mp.sqrt(u[0]))
            else:
                s = mpf(0)
                for k in range(m):
                    s += (ALPHA * (m - k) - k) * u[m - k] * w[k]
                w[m] = s / (m * u[0])
            for a in range(3):
                acc[a] += bodies[i].mu * sum(d[a][k] * w[m - k] for k in range(m + 1))
        for a in range(3):
            A[a][m] = acc[a] - root_acc[a][m]
            P[a][m + 1] = V[a][m] / (m + 1)
            V[a][m + 1] = A[a][m] / (m + 1)
    return P, V, A, W


def horner(c, h, n):
    r = mpf(0)
    for k in range(n, -1, -1):
        r = r * h + c[k]
    return r


def direct_accel(S, t, p):
    """independent force: plain Clenshaw evaluation of the ephemeris + textbook formula."""
    bodies = S["bodies"]
    W = {}
    def cheb(b, t):
        seg = _segment(b, t); start = b.T0 + seg * b.L
        u = 2 * (t - start) / b.L - 1; n1 = b.deg + 1; base = seg * 3 * n1
        res = []
        for a in range(3):
            c = b.coeffs[base + a * n1: base + (a + 1) * n1]
            b1 = b2 = mpf(0)
            for k in range(b.deg, 0, -1):
                b1, b2 = 2 * u * b1 - b2 + c[k], b1
            res.append(u * b1 - b2 + c[0])
        return res
    for i in S["order"]:
        b = bodies[i]
        if b.parent is None: W[i] = [mpf(0)] * 3
        else:
            l = cheb(b, t); W[i] = [l[a] + W[b.parent][a] for a in range(3)]
    acc = [mpf(0)] * 3
    for i, b in enumerate(bodies):
        if b.mu == 0: continue
        d = [W[i][a] - p[a] for a in range(3)]
        r3 = mp.sqrt(sum(x * x for x in d)) ** 3
        for a in range(3): acc[a] += b.mu * d[a] / r3
    for c in S["root_children"]:
        d = W[c]; r3 = mp.sqrt(sum(x * x for x in d)) ** 3
        for a in range(3): acc[a] -= bodies[c].smu * d[a] / r3
    return acc, W


def check_jets(S, t, p, v, K):
    P, V, A, W = step_coeffs(S, t, p, v, K)
    h = mpf(20)
    ph = [horner(P[a], h, K) for a in range(3)]
    ad, _ = direct_accel(S, t + h, ph)
    aj = [horner(A[a], h, K - 1) for a in range(3)]
    return max(abs(ad[a] - aj[a]) / max(abs(ad[a]), mpf(10) ** -30) for a in range(3))


# ------------------------------------------------------------------------ driver
def integrate(S, K, tol, hmax, checkpoints, log=True, verify_every=0, on_checkpoint=None):
    t = S["t0"]; T = t + S["days"] * 86400
    p = list(S["p0"]); v = list(S["v0"])
    cps = sorted(set(mpf(c) * 86400 + S["t0"] for c in checkpoints if mpf(c) * 86400 + S["t0"] < T)) + [T]
    out, steps, t_wall = [], 0, time.time()
    ci = 0
    while ci < len(cps):
        tgt = cps[ci]
        P, V, A, W = step_coeffs(S, t, p, v, K)
        est = mpf("inf")
        for C in (P, V):
            for a in range(3):
                for n in (K, K - 1):
                    c = abs(C[a][n])
                    if c > 0: est = min(est, (tol / c) ** (mpf(1) / n))
        h = min(0.9 * est, mpf(hmax), tgt - t)
        for b in S["bodies"]:
            if b.parent is not None:
                nb = next_boundary(b, t)
                if nb > t: h = min(h, nb - t)
        if verify_every and steps % verify_every == 0:
            e = check_jets(S, t, p, v, K)
            if e > mpf(10) ** -(mp.dps // 2): print("  WARNING jets vs direct force rel.diff", mp.nstr(e, 3))
        hit = (h >= tgt - t)
        p = [horner(P[a], h, K) for a in range(3)]
        v = [horner(V[a], h, K) for a in range(3)]
        t = tgt if hit else t + h
        steps += 1
        if log and steps % 500 == 0:
            print("  step %d  t-t0=%.3f d  h=%.1f s  (%.0f s wall)" % (
                steps, float((t - S["t0"]) / 86400), float(h), time.time() - t_wall), flush=True)
        if hit:
            Wt = world_jets(S, t, 1)
            cen = [Wt[S["central"]][a][0] for a in range(3)]
            cenv = [Wt[S["central"]][a][1] for a in range(3)]
            out.append(dict(days=float((t - S["t0"]) / 86400), steps=steps, t=t, p=list(p), v=list(v),
                            central=cen, rel_p=[p[a] - cen[a] for a in range(3)],
                            rel_v=[v[a] - cenv[a] for a in range(3)]))
            ci += 1
            if on_checkpoint is not None:
                on_checkpoint(out)
    return out


def sanity_against_export(S):
    """schema/layout check: central body state at t0 must match ShipP0-RelP / ShipV0-RelV."""
    W = world_jets(S, S["t0"], 1)
    c = S["central"]
    dp = max(abs(W[c][a][0] - (S["p0"][a] - S["relp"][a])) for a in range(3))
    dv = max(abs(W[c][a][1] - (S["v0"][a] - S["relv"][a])) for a in range(3))
    return dp, dv


def fmt(x, n=45):
    return mp.nstr(x, n)


def checkpoint_entry(r):
    return dict(days=r["days"], steps=r["steps"], t=fmt(r["t"]),
                P=[fmt(x) for x in r["p"]], V=[fmt(x) for x in r["v"]],
                central_P=[fmt(x) for x in r["central"]],
                rel_P=[fmt(x) for x in r["rel_p"]], rel_V=[fmt(x) for x in r["rel_v"]])


def write_doc(path, doc):
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(doc, f, indent=1)
    os.replace(tmp, path)


def run(S, args):
    mp.dps = args.dps
    dp, dv = sanity_against_export(S)
    print("layout check  |W_central(t0)-(ShipP0-RelP)| = %s m, velocity %s m/s" % (mp.nstr(dp, 3), mp.nstr(dv, 3)))
    if dp > 1e-2 or dv > 1e-5:
        print("  !! central-body state does not match the export: wrong T0 / Coeffs layout / parent chain; stop.")
        if not args.force: sys.exit(2)
    jerr = check_jets(S, S["t0"], S["p0"], S["v0"], args.K)
    print("jets vs direct force at t0: rel.diff %s (should be << 1e-%d)" % (mp.nstr(jerr, 3), args.dps // 2))
    params = dict(K=args.K, tol=args.tol, dps=args.dps, hmax=args.hmax,
                  t0=fmt(S["t0"]), bake_offset=fmt(S["t0"] - S["bodies"][S["central"]].T0))

    def make_doc(checkpoints, cert=None, partial=False):
        doc = dict(params=params, checkpoints=[])
        for i, r in enumerate(checkpoints):
            e = checkpoint_entry(r)
            if cert:
                e["golden_error_estimate"] = dict(dp=mp.nstr(cert[i][0], 3), dv=mp.nstr(cert[i][1], 3))
            doc["checkpoints"].append(e)
        if partial:
            doc["partial"] = True
        return doc

    def save_partial(out):
        if args.partial_out:
            write_doc(args.partial_out, make_doc(out, partial=True))
            print("  partial -> %s (%d checkpoint(s))" % (args.partial_out, len(out)), flush=True)

    t0 = time.time()
    res = integrate(S, args.K, mpf(args.tol), args.hmax, args.checkpoints,
                    verify_every=args.verify_every, on_checkpoint=save_partial)
    print("main run: K=%d tol=%s dps=%d  %d steps  %.0f s" % (args.K, args.tol, args.dps, res[-1]["steps"], time.time() - t0))
    cert = None
    if args.certify:
        mp.dps = args.dps + 10
        K2, tol2 = args.K + 6, mpf(args.tol) / mpf(10) ** 6
        t0 = time.time()
        res2 = integrate(S, K2, tol2, args.hmax, args.checkpoints, log=False)
        mp.dps = args.dps
        cert = []
        for a, b in zip(res, res2):
            dpp = max(abs(a["p"][i] - b["p"][i]) for i in range(3)); dvv = max(abs(a["v"][i] - b["v"][i]) for i in range(3))
            cert.append((dpp, dvv)); print("  certificate day %.2f: |dp|=%s m |dv|=%s m/s" % (a["days"], mp.nstr(dpp, 3), mp.nstr(dvv, 3)))
        print("certify run: K=%d tol=%s %.0f s" % (K2, mp.nstr(tol2, 2), time.time() - t0))
    if args.out:
        write_doc(args.out, make_doc(res, cert))
        print("wrote", args.out)
    return res


# ---------------------------------------------------------------- synthetic selftest
def synthetic_export(days=3):
    import numpy as np
    from numpy.polynomial import chebyshev as C
    mu_s, mu_e, mu_m = 1.32712440018e20, 3.986004418e14, 4.9048695e12
    r_e, r_m = 1.495978707e11, 3.844e8
    n_e = math.sqrt(mu_s / r_e ** 3); n_m = math.sqrt(mu_e / r_m ** 3)
    inc = math.radians(5.1)
    def earth(t): return np.array([r_e * math.cos(n_e * t), r_e * math.sin(n_e * t), 0.0])
    def moon(t): return np.array([r_m * math.cos(n_m * t), r_m * math.sin(n_m * t) * math.cos(inc), r_m * math.sin(n_m * t) * math.sin(inc)])
    def bake(fn, T0, L, nseg, deg):
        co = []
        for s in range(nseg):
            for a in range(3):
                co += list(C.chebinterpolate(lambda u: np.array([fn(T0 + s * L + (x + 1) * L / 2)[a] for x in np.atleast_1d(u)]), deg))
        return [float(x) for x in co]
    bodies = [dict(Name="Sun", Parent=None, Mu=mu_s, SubtreeMu=mu_s + mu_e + mu_m),
              dict(Name="Earth", Parent="Sun", Mu=mu_e, SubtreeMu=mu_e + mu_m, T0=0.0, SegmentLength=5 * 86400.0, Degree=14, Segments=3),
              dict(Name="Moon", Parent="Earth", Mu=mu_m, SubtreeMu=mu_m, T0=0.0, SegmentLength=0.5 * 86400.0, Degree=14, Segments=30)]
    bodies[1]["Coeffs"] = bake(earth, 0.0, 5 * 86400.0, 3, 14)
    bodies[2]["Coeffs"] = bake(moon, 0.0, 0.5 * 86400.0, 30, 14)
    rp, ra = 6.771e6, 4.6371e7
    a_ = (rp + ra) / 2; vp = math.sqrt(mu_e * (2 / rp - 1 / a_))
    t0 = 172800.0
    ev = (earth(t0 + 1.0) - earth(t0 - 1.0)) / 2.0
    relp = np.array([rp, 0.0, 0.0]); relv = np.array([0.0, vp * math.cos(0.3), vp * math.sin(0.3)])
    ep = earth(t0)
    return dict(T0=t0, Days=days, Central=1, Bodies=bodies,
                ShipP0=[float(x) for x in ep + relp], ShipV0=[float(x) for x in ev + relv],
                RelP=[float(x) for x in relp], RelV=[float(x) for x in relv])


def selftest(args):
    import numpy as np
    from scipy.integrate import solve_ivp
    mp.dps = 40
    data = synthetic_export(days=2)
    S = load_system(data)
    W = world_jets(S, S["t0"], 1)
    S["v0"] = [S["relv"][a] + W[1][a][1] for a in range(3)]
    S["p0"] = [S["relp"][a] + W[1][a][0] for a in range(3)]
    print("== layout check:", [mp.nstr(x, 3) for x in sanity_against_export(S)])
    print("== jets vs direct force (K=28):", mp.nstr(check_jets(S, S["t0"], S["p0"], S["v0"], 28), 3))
    t0 = time.time()
    r1 = integrate(S, 28, mpf("1e-22"), 3600, [1], log=False)
    w1 = time.time() - t0
    t0 = time.time()
    mp.dps = 50
    r2 = integrate(S, 34, mpf("1e-28"), 3600, [1], log=False)
    w2 = time.time() - t0
    mp.dps = 40
    for a, b in zip(r1, r2):
        print("== day %.1f certificate: dp=%s m dv=%s m/s  (steps %d vs %d)" % (a["days"],
              mp.nstr(max(abs(a["p"][i] - b["p"][i]) for i in range(3)), 3),
              mp.nstr(max(abs(a["v"][i] - b["v"][i]) for i in range(3)), 3), a["steps"], b["steps"]))
    print("== wall: %.0f s for %d steps (K=28) -> %.2f s/step; K=34/dps50: %.0f s" % (w1, r1[-1]["steps"], w1 / r1[-1]["steps"], w2))
    def rhs(t, y):
        acc, _ = direct_accel(S, mpf(t), [mpf(y[0]), mpf(y[1]), mpf(y[2])])
        return [y[3], y[4], y[5]] + [float(x) for x in acc]
    y0 = [float(x) for x in S["p0"]] + [float(x) for x in S["v0"]]
    sol = solve_ivp(rhs, [float(S["t0"]), float(S["t0"]) + 86400.0], y0, method="DOP853", rtol=1e-13, atol=1e-6)
    d = max(abs(float(r1[0]["p"][i]) - sol.y[i, -1]) for i in range(3))
    print("== |mp golden - scipy DOP853 (double, world frame)| after 1 d = %.3e m  (sanity only)" % d)


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("export", nargs="?")
    ap.add_argument("--K", type=int, default=28)
    ap.add_argument("--tol", default="1e-20")
    ap.add_argument("--dps", type=int, default=40)
    ap.add_argument("--hmax", type=float, default=3600.0)
    ap.add_argument("--checkpoints", type=lambda s: [float(x) for x in s.split(",")], default=[1.0])
    ap.add_argument("--certify", action="store_true")
    ap.add_argument("--verify-every", type=int, default=0, help="re-check jets against the direct force every N steps")
    ap.add_argument("--out")
    ap.add_argument("--partial-out", help="write checkpoints incrementally (atomic replace)")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--selftest", action="store_true")
    a = ap.parse_args()
    if a.selftest: selftest(a)
    elif a.export:
        mp.dps = a.dps
        run(load_system(a.export), a)
    else: ap.error("give an export file or --selftest")

