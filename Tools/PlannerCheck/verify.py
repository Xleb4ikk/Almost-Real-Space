"""Independent verifier for the Almost Real Space manoeuvre planner.

Oracle routes, all independent of the C# code under test:
  * two-body propagation: scipy DOP853 direct ODE integration (rtol 1e-13),
    cross-checked against a classical element/Kepler implementation (oracle.py);
  * conic geometry: closed-form asymptote angles (pi/2 +- asin(1/e));
  * costs: textbook Hohmann / escape formulas in mpmath.
"""
import sys
import numpy as np
from scipy.integrate import solve_ivp
from mpmath import mp, mpf, sqrt, pi, atan, atan2, asin, cos, sin
from oracle import propagate as propagate_el, dot, cross, norm

mp.dps = 40


def propagate(mu, r, v, dt):
    mu = float(mu)
    y0 = np.concatenate([np.array([float(x) for x in r]), np.array([float(x) for x in v])])

    def rhs(t, y):
        rr = y[:3]
        rn = np.linalg.norm(rr)
        return np.concatenate([y[3:], -mu*rr/rn**3])

    sol = solve_ivp(rhs, (0, float(dt)), y0, method='DOP853', rtol=1e-13, atol=1e-9)
    return sol.y[:3, -1], sol.y[3:, -1]


def relerr(a, b):
    a = np.asarray(a, float); b = np.asarray(b, float)
    return np.linalg.norm(a-b)/max(np.linalg.norm(a), np.linalg.norm(b), 1.0)


def f(x):
    return mpf(float(x))


fails = 0


def check(name, ok, detail=""):
    global fails
    if not ok:
        fails += 1
    print(("PASS " if ok else "FAIL ") + name + ("  " + detail if detail else ""))


rows = [l.strip().split(',') for l in open(sys.argv[1], encoding='utf-8') if l.strip()]

# ================================================================ Lambert
print("\n== Lambert: propagate the returned v1 for dt, must land on r2 ==")
w = 0.0; n = 0; bad = []
for r in rows:
    if r[0] not in ("L", "L180", "Lh"):
        continue
    mu = float(r[2]); r1 = [float(x) for x in r[3:6]]; r2 = [float(x) for x in r[6:9]]
    dt = float(r[9]); hpref = [float(x) for x in r[10:13]]
    if r[13] != "1":
        bad.append("refused " + r[0]+"/"+r[1]); continue
    v1 = [float(x) for x in r[14:17]]; v2 = [float(x) for x in r[17:20]]
    rf, _ = propagate(mu, r1, v1, dt)
    rb, _ = propagate(mu, r2, v2, -dt)
    e1 = relerr(rf, r2); e2 = relerr(rb, r1)
    sense = float(np.dot(np.cross(r1, v1), hpref))
    n += 1
    w = max(w, e1, e2)
    if not (e1 < 2e-10 and e2 < 2e-10 and sense > 0):
        bad.append("%s/%s end=%.2e back=%.2e h.hpref=%.3g" % (r[0], r[1], e1, e2, sense))
print("  cases=%d worst endpoint rel err=%.2e" % (n, w))
for b in bad[:6]:
    print("   ", b)
check("lambert endpoints (prograde branch), both directions", w < 2e-10 and not bad)

# Hohmann geometry at 180 degrees: v1 must be the analytic transfer speed
print("\n== Lambert at exactly 180 deg vs analytic Hohmann ==")
w180 = 0.0
for r in rows:
    if r[0] != "L180" or int(r[1]) > 5:
        continue
    mu = f(r[2]); r1 = f(r[3]); r2 = sqrt(f(r[6])**2 + f(r[7])**2)
    v1 = (f(r[14]), f(r[15]), f(r[16]))
    a = (r1+r2)/2
    want = sqrt(mu*(2/r1 - 1/a))
    err = abs(norm(v1) - want)/want
    eps = [1e-10, 1e-8, 1e-6, 1e-4, 1e-3, 1e-2, 0.1, 0.5, 1.0][int(r[1])]
    # a perturbation of the endpoint by eps rad moves the transfer speed by O(eps)
    allow = mpf(max(1e-9, 20*eps))
    if err > allow:
        print('   L180/%s eps=%.0e err=%.2e allow=%.1e' % (r[1], eps, float(err), float(allow)))
        w180 = 1.0
    else:
        w180 = max(w180, float(err)/float(allow))
print("  worst |v1| error / allowed O(eps) = %.2f" % w180)
check("180-degree Lambert = Hohmann transfer speed (within O(eps))", w180 <= 1.0, "worst err/allow=%.2f" % w180)

# ================================================================ Kepler
print("\n== UniversalKepler vs DOP853 ==")
wk = 0.0; nk = 0; bad = []
for r in rows:
    if r[0] != "K":
        continue
    mu = float(r[2]); r0 = [float(x) for x in r[3:6]]; v0 = [float(x) for x in r[6:9]]; dt = float(r[9])
    rt = [float(x) for x in r[11:14]]; vt = [float(x) for x in r[14:17]]
    nk += 1
    if r[10] != "1":
        bad.append("refused " + r[1]); continue
    ro, vo = propagate(mu, r0, v0, dt)
    ep = relerr(ro, rt); ev = relerr(vo, vt)
    wk = max(wk, ep, ev)
    if ep > 2e-10 or ev > 2e-10:
        bad.append("%s pos=%.2e vel=%.2e" % (r[1], ep, ev))
print("  cases=%d worst=%.2e" % (nk, wk))
for b in bad[:6]:
    print("   ", b)
check("universal Kepler position & velocity", wk < 2e-10 and not bad)

wrt = 0.0
for r in rows:
    if r[0] != "Krt":
        continue
    if r[10] != "1":
        continue
    r0 = [float(x) for x in r[3:6]]; v0 = [float(x) for x in r[6:9]]
    rb = [float(x) for x in r[11:14]]; vb = [float(x) for x in r[14:17]]
    wrt = max(wrt, relerr(rb, r0), relerr(vb, v0))
print("  round-trip worst=%.2e" % wrt)
check("universal Kepler time reversibility", wrt < 1e-11)

# ================================================================ Escape
print("\n== EscapeSolver: asymptote, energy, periapsis ==")
we = 0.0; wdir = 0.0; wrp = 0.0; ne = 0; errs = []
for r in rows:
    if r[0] != "E":
        continue
    mu = f(r[2]); rn = f(r[3])
    rpos = (f(r[3])*f(r[4]), f(r[3])*f(r[5]), f(r[3])*f(r[6]))
    vinf = (f(r[10]), f(r[11]), f(r[12]))
    ne += 1
    if r[13] != "1":
        errs.append("refused " + r[1]); continue
    vpost = (f(r[17]), f(r[18]), f(r[19]))
    rp = f(r[20])
    vp2 = dot(vpost, vpost); vinf2 = dot(vinf, vinf)
    e_en = abs(vp2 - (vinf2 + 2*mu/rn))/(vinf2 + 2*mu/rn)
    h = cross(rpos, vpost); hn = norm(h)
    evec = tuple(((vp2 - mu/rn)*rpos[i] - dot(rpos, vpost)*vpost[i])/mu for i in range(3))
    e = norm(evec); eh = tuple(x/e for x in evec); hh = tuple(x/hn for x in h)
    qh = cross(hh, eh)
    th = pi/2 + atan(1/sqrt(e*e - 1))
    uout = tuple(cos(th)*eh[i] + sin(th)*qh[i] for i in range(3))
    vd = norm(tuple(uout[i] - vinf[i]/sqrt(vinf2) for i in range(3)))
    e_v = abs(sqrt(vp2 - 2*mu/rn) - sqrt(vinf2))/sqrt(vinf2)
    rp_el = hn*hn/mu/(1+e)
    e_rp = abs(rp_el - rp)/rp
    we = max(we, e_en); wdir = max(wdir, vd, e_v); wrp = max(wrp, e_rp)
    if e_en > 1e-12 or vd > 1e-12 or e_v > 1e-12 or e_rp > 1e-10:
        errs.append("%s dE=%.1e dir=%.1e vmag=%.1e rp=%.1e" % (r[1], float(e_en), float(vd), float(e_v), float(e_rp)))
print("  cases=%d energy=%.2e asymptote dir/speed=%.2e rp=%.2e" % (ne, we, wdir, wrp))
for b in errs[:6]:
    print("   ", b)
check("escape energy identity", we < 1e-12)
check("escape asymptote direction & speed", wdir < 1e-12)
check("escape periapsis radius", wrp < 1e-10)

# ================================================================ Flyby
print("\n== FlybyGeometry: conic reconstruction ==")
wft = 0.0; wfin = 0.0; wfout = 0.0; nf = 0; ferr = []; nzero = 0
for r in rows:
    if r[0] not in ("F", "Fdeg"):
        continue
    mu = f(r[2])
    vin = (f(r[3]), f(r[4]), f(r[5])); vout = (f(r[6]), f(r[7]), f(r[8]))
    need = f(r[10]); tmin = f(r[11]); tmax = f(r[12]); rp = f(r[13])
    xdir = (f(r[16]), f(r[17]), f(r[18])); ydir = (f(r[19]), f(r[20]), f(r[21]))
    vi, vo = norm(vin), norm(vout)
    nf += 1
    ang = atan2(norm(cross(vin, vout)), dot(vin, vout))
    wft = max(wft, abs(ang - need))
    if xdir == (0, 0, 0):
        nzero += 1
        continue
    if r[9] != "1":
        continue
    if need == 0:
        continue  # exactly parallel v_inf: the reported rp is an arbitrary SOI fallback
    e_in = 1 + rp*vi*vi/mu
    e_out = 1 + rp*vo*vo/mu
    th_in = pi/2 - atan(1/sqrt(e_in*e_in - 1))
    th_out = pi/2 + atan(1/sqrt(e_out*e_out - 1))
    uin_pred = tuple(cos(th_in)*xdir[i] + sin(th_in)*ydir[i] for i in range(3))
    uout_pred = tuple(cos(th_out)*xdir[i] + sin(th_out)*ydir[i] for i in range(3))
    e_in_dir = norm(tuple(uin_pred[i] - vin[i]/vi for i in range(3)))
    e_out_dir = norm(tuple(uout_pred[i] - vout[i]/vo for i in range(3)))
    wfin = max(wfin, e_in_dir); wfout = max(wfout, e_out_dir)
    if e_in_dir > 1e-12 or e_out_dir > 1e-12:
        ferr.append("%s in=%.1e out=%.1e" % (r[1], float(e_in_dir), float(e_out_dir)))
print("  cases=%d turn=%.2e in-asymptote=%.2e out-asymptote=%.2e (degenerate zero-axis: %d)"
      % (nf, wft, wfin, wfout, nzero))
for b in ferr[:6]:
    print("   ", b)
check("flyby: needed turn = angle between v_inf vectors", wft < 1e-12)
check("flyby: incoming/outgoing asymptotes match the common-rp hyperbola", wfin < 1e-12 and wfout < 1e-12)

# feasibility semantics after the fix: only a turn deficit ABOVE the rpMin corridor costs dv.
badb = 0
for r in rows:
    if r[0] not in ("F", "Fdeg"):
        continue
    need = f(r[10]); tmin = f(r[11]); tmax = f(r[12]); rp = f(r[13])
    deficit = f(r[15]); vo = norm((f(r[6]), f(r[7]), f(r[8])))
    okflag = r[9] == "1"
    if need <= tmax + mpf('1e-12'):
        if not okflag:
            badb += 1; print("   should be feasible:", r[0], r[1], float(need), float(tmax))
        if need < tmin:
            # weak turn -> periapsis beyond the SOI, free
            if deficit != 0:
                badb += 1; print("   weak turn charged:", r[0], r[1], float(deficit))
            if rp < mpf('7.2e7'):
                badb += 1; print("   weak turn rp inside rpMin:", r[0], r[1], float(rp))
    else:
        want = 2*vo*sin((need - tmax)/2)
        if okflag or abs(deficit - want) > mpf('1e-9')*max(want, mpf(1)):
            badb += 1
            print("   upper deficit wrong:", r[0], r[1], float(deficit), float(want))
check("flyby: no dv for a turn weaker than the SOI corridor, exact deficit above rpMin", badb == 0,
      "mismatches=%d" % badb)

# ================================================================ ArcMinRadius
print("\n== ArcMinRadius vs DOP853 dense sampling ==")
wa = 0.0; na = 0
for r in rows:
    if r[0] != "A":
        continue
    mu = float(r[2]); r1 = [float(x) for x in r[3:6]]; v1 = [float(x) for x in r[6:9]]
    dt = float(r[12]); got = float(r[13])
    mu_ = mu
    y0 = np.concatenate([np.array(r1), np.array(v1)])

    def rhs(t, y):
        rr = y[:3]; rn = np.linalg.norm(rr)
        return np.concatenate([y[3:], -mu_*rr/rn**3])

    sol = solve_ivp(rhs, (0, dt), y0, method='DOP853', rtol=1e-13, atol=1e-3, dense_output=True)
    ts = np.linspace(0, dt, 2001)
    oracle = float(np.min(np.linalg.norm(sol.sol(ts)[:3], axis=0)))
    err = abs(oracle-got)/oracle
    wa = max(wa, err); na += 1
    if err > 1e-6:
        print("   A/%s got=%.6e oracle=%.6e rel=%.2e" % (r[1], got, oracle, err))
print("  cases=%d worst rel err=%.2e" % (na, wa))
check("arc minimum radius", wa < 1e-6)

# ================================================================ ConicMath
print("\n== ConicMath anchors ==")
mu_e = mpf('3.986004418e14')
bad = 0
for r in rows:
    if r[0] != "Cdv":
        continue
    vinf = [0, 300, 1500, 4000, 9000][int(r[1])]
    want = sqrt(mpf(vinf)**2 + 2*mu_e/mpf('6.7e6')) - sqrt(mu_e/mpf('6.7e6'))
    if abs(f(r[2]) - want) > mpf('1e-9') or abs(f(r[3]) - want) > mpf('1e-9'):
        bad += 1
check("escape/capture dv = sqrt(vinf^2+2mu/r) - sqrt(mu/r)", bad == 0)

mu_j = mpf('1.26686534e17')
bad = 0
for r in rows:
    if r[0] != "Cfly":
        continue
    rp = f(r[2]); vinf = f(r[3]); e = f(r[4]); turn = f(r[5]); dv = f(r[6])
    e_want = 1 + rp*vinf*vinf/mu_j
    turn_want = 2*atan(1/sqrt(e_want*e_want - 1))
    dv_want = 2*vinf/e_want
    if abs(e - e_want) > mpf('1e-9')*e_want or abs(turn - turn_want) > mpf('1e-13') or abs(dv - dv_want) > mpf('1e-9')*dv_want:
        bad += 1
check("flyby e, turn=2 asin(1/e), dv=2 vinf/e", bad == 0)

print("\n============================")
print("FAILURES:", fails)

