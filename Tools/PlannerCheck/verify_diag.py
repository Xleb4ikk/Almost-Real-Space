import sys
import numpy as np
from mpmath import mp, mpf, sqrt, pi, atan, asin, cos, sin, acos
mp.dps = 40

rows = [l.strip().split(',') for l in open(sys.argv[1] if len(sys.argv) > 1 else 'diag.txt', encoding='utf-8') if l.strip()]

fails = 0
def check(name, ok, detail=''):
    global fails
    if not ok:
        fails += 1
    print(('PASS ' if ok else 'FAIL ') + name + ('  ' + detail if detail else ''))

print('== CaptureGeometry.TryIncoming: incoming asymptote of the periapsis state ==')
mu = mpf('4.282837e13'); rp = mpf('3.3895e6') + mpf('400e3')
worst = 0.0; worst_vp = 0.0; worst_orth = 0.0
for r in rows:
    if r[0] != 'CAP':
        continue
    if r[2] != '1':
        check('capture solved ' + r[1], False); continue
    vinf = np.array([float(x) for x in r[3:6]])
    x = np.array([float(x) for x in r[6:9]])
    y = np.array([float(x) for x in r[9:12]])
    n = np.array([float(x) for x in r[12:15]])
    rpv = float(r[15]); vp = float(r[16]); e = float(r[17])
    vi = np.linalg.norm(vinf)
    # conic: incoming asymptote at pi/2 - asin(1/e) from x toward y
    e_geo = 1 + rp*vi*vi/mu
    th = pi/2 - atan(1/sqrt(e_geo*e_geo-1))
    uin = cos(th)*x + sin(th)*y
    err = np.linalg.norm(uin - vinf/vi)
    worst = max(worst, err)
    vp_want = float(sqrt(vi*vi + 2*mu/rp))
    worst_vp = max(worst_vp, abs(vp-vp_want)/vp_want)
    worst_orth = max(worst_orth, abs(np.dot(x, y)), abs(np.dot(x, n)), abs(np.dot(y, n)),
                     np.linalg.norm(np.cross(x, y)-n), abs(rpv-rp)/rp, abs(e-e_geo)/e_geo)
print('  worst asymptote err %.2e |vp| %.2e orthonormal/e %.2e' % (worst, worst_vp, worst_orth))
check('capture periapsis state reproduces v_inf', worst < 1e-12)
check('capture |vp| = sqrt(vinf^2+2mu/rp)', worst_vp < 1e-14)
check('capture frame orthonormal, n = x cross y, e consistent', worst_orth < 1e-12)

print()
print('== OrbitPrep.TryHohmann ==')
muE = mpf('3.986004418e14')
bad = 0
for r in rows:
    if r[0] != 'PREP':
        continue
    if r[2] != '1':
        print('  prep refused', r[1]); continue
    r1 = mpf(r[3]); r2 = mpf(r[4])
    dv1 = mpf(r[5]); dv2 = mpf(r[6]); half = mpf(r[7]); rAfter = mpf(r[8]); vAfter = mpf(r[9])
    at = (r1+r2)/2
    half_want = pi*sqrt(at**3/muE)
    vc = sqrt(muE/r2)
    # the transfer half period and the final circular speed
    ok = abs(half-half_want)/half_want < 1e-9 and abs(rAfter-r2)/r2 < 1e-9 and abs(vAfter-vc)/vc < 1e-9
    if not ok:
        bad += 1
        print('  PREP/%s half=%.6e (want %.6e) rAfter=%.6e vAfter=%.6e (want %.6e)'
              % (r[1], float(half), float(half_want), float(rAfter), float(vAfter), float(vc)))
check('prep: half period, final radius and circular speed', bad == 0, 'bad=%d' % bad)

# For a circular start the two impulses must equal the textbook Hohmann dv.
bad = 0
for r in rows:
    if r[0] != 'PREP':
        continue
    if r[2] != '1':
        continue
    r1 = mpf(r[3]); r2 = mpf(r[4]); dv1 = mpf(r[5]); dv2 = mpf(r[6])
    at = (r1+r2)/2
    w1 = sqrt(muE/r1)*(sqrt(2*r2/(r1+r2))-1)
    w2 = sqrt(muE/r2)*(1-sqrt(2*r1/(r1+r2)))
    if abs(dv1-abs(w1)) > mpf('1e-9') or abs(dv2-abs(w2)) > mpf('1e-9'):
        bad += 1
        print('  PREP/%s dv=(%.6f, %.6f) want (%.6f, %.6f)' % (r[1], float(dv1), float(dv2), float(w1), float(w2)))
check('prep: dv1/dv2 equal the textbook Hohmann formulas (circular start)', bad == 0, 'bad=%d' % bad)

print()
print('FAILURES:', fails)

