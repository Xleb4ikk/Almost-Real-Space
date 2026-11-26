"""Two-body oracle: classical conic + Kepler's equation in mpmath (50 digits).

Deliberately a different algorithm from the project's universal-variable code:
elements -> mean anomaly -> anomaly -> perifocal state -> inertial.
Self-test at the bottom; run  python oracle.py  to check it.
"""
from mpmath import mp, mpf, sqrt, sin, cos, sinh, cosh, tan, tanh, atan, asinh, atan2, pi
import math
mp.dps = 50


def cross(a, b):
    return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


def dot(a, b):
    return a[0]*b[0]+a[1]*b[1]+a[2]*b[2]


def norm(a):
    return sqrt(dot(a, a))


def _newton(fun, fp, x0, tol=mpf(10)**(-45), iters=200):
    x = mpf(x0)
    for _ in range(iters):
        d = fun(x)/fp(x)
        x -= d
        if abs(d) < tol*max(mpf(1), abs(x)):
            break
    return x


def propagate(mu, r, v, dt):
    mu = mpf(mu)
    r = tuple(mpf(x) for x in r)
    v = tuple(mpf(x) for x in v)
    dt = mpf(dt)
    rn = norm(r)
    h = cross(r, v)
    hn = norm(h)
    if hn == 0:
        raise ValueError('rectilinear')
    p = hn*hn/mu                      # semi-latus rectum
    evec = tuple(((dot(v, v) - mu/rn)*r[i] - dot(r, v)*v[i])/mu for i in range(3))
    e = norm(evec)
    energy = dot(v, v)/2 - mu/rn
    if e < mpf('1e-20'):
        # circular: rotate the state about h by the mean motion
        n = sqrt(mu/rn**3)
        ang = n*dt
        hh = tuple(x/hn for x in h)
        # Rodrigues rotation of r and v about hh
        def rot(w, hh, ang):
            c, s = cos(ang), sin(ang)
            cr = cross(hh, w)
            d = dot(hh, w)
            return tuple(w[i]*c + cr[i]*s + hh[i]*d*(1-c) for i in range(3))
        return rot(r, hh, ang), rot(v, hh, ang)
    eh = tuple(x/e for x in evec)
    hh = tuple(x/hn for x in h)
    qh = cross(hh, eh)

    if e < 1:
        a = p/(1 - e*e)
        n = sqrt(mu/a**3)
        cosE = (1 - rn/a)/e
        cosE = max(mpf(-1), min(mpf(1), cosE))
        sinE = dot(r, v)/(e*sqrt(mu*a))
        E0 = atan2(sinE, cosE)
        M = (E0 - e*sin(E0)) + n*dt
        E = _newton(lambda x: x - e*sin(x) - M, lambda x: 1 - e*cos(x), M)
        nu = 2*atan(sqrt((1+e)/(1-e))*tan(E/2))
    elif e > 1:
        a = p/(e*e - 1)                # |a|
        n = sqrt(mu/a**3)
        coshH = (rn/a + 1)/e
        coshH = max(mpf(1), coshH)
        sinhH = dot(r, v)/(e*sqrt(mu*a))
        H0 = asinh(sinhH)
        M = (e*sinh(H0) - H0) + n*dt
        H = _newton(lambda x: e*sinh(x) - x - M, lambda x: e*cosh(x) - 1,
                    math.asinh(float(M/e)) if abs(float(M)) < 1e300 else float(M))
        nu = 2*atan(sqrt((e+1)/(e-1))*tanh(H/2))
    else:
        # parabolic: Barker
        D = dot(r, v)/sqrt(mu*p)       # tan(nu/2) for p = 2 rp
        t0 = sqrt(p**3/mu)*(D + D**3/3)/2
        Dt = _newton(lambda x: sqrt(p**3/mu)*(x + x**3/3)/2 - (t0 + dt),
                     lambda x: sqrt(p**3/mu)*(1 + x*x)/2, D + 1e-9)
        nu = 2*atan(Dt)

    rn2 = p/(1 + e*cos(nu))
    k = sqrt(mu/p)
    rc = tuple(rn2*(cos(nu)*eh[i] + sin(nu)*qh[i]) for i in range(3))
    vc = tuple(k*(-sin(nu)*eh[i] + (e + cos(nu))*qh[i]) for i in range(3))
    return rc, vc


def energy(mu, r, v):
    return dot(v, v)/2 - mu/norm(r)


def angmom(r, v):
    return norm(cross(r, v))


if __name__ == '__main__':
    mu = mpf(1)
    # circular quarter turn
    r0, v0 = (mpf(1), mpf(0), mpf(0)), (mpf(0), mpf(1), mpf(0))
    r1, v1 = propagate(mu, r0, v0, pi/2)
    print('circular:', [float(x) for x in r1], [float(x) for x in v1])
    # elliptic period closure
    e = mpf('0.6'); a = mpf(2); rp = a*(1-e)
    vp = sqrt(mu*(1+e)/rp)
    r0, v0 = (rp, mpf(0), mpf(0)), (mpf(0), vp, mpf(0))
    T = 2*pi*sqrt(a**3/mu)
    r1, v1 = propagate(mu, r0, v0, T)
    print('ellipse period closure:', float(norm(tuple(r1[i]-r0[i] for i in range(3)))),
          float(norm(tuple(v1[i]-v0[i] for i in range(3)))))
    print('ellipse E,h:', float(energy(mu, r0, v0)), float(angmom(r0, v0)), float(angmom(r1, v1)))
    # hyperbolic: turn angle at infinity must be 2 asin(1/e)
    e = mpf(3); rp = mpf(1)
    vp = sqrt(mu*(1+e)/rp)
    r0, v0 = (rp, mpf(0), mpf(0)), (mpf(0), vp, mpf(0))
    print('hyp E,h:', float(energy(mu, r0, v0)), float(angmom(r0, v0)))
    big = 4000
    r1, v1 = propagate(mu, r0, v0, big)
    print('hyp far radius:', float(norm(r1)), 'speed:', float(norm(v1)))
    # asymptote direction relative to periapsis
    u = tuple(v1[i]/norm(v1) for i in range(3))
    print('hyp asymptote angle from +x (deg):', float(atan2(u[1], u[0])*180/pi), '  expected',
          180 - float(atan(1/sqrt(e*e-1))*180/pi)/1 if False else float((pi/2 + atan(1/sqrt(e*e-1)))*180/pi))
    print('  => deflection for e=3 is 2 asin(1/3) =', float(2*atan(1/sqrt(e*e-1))*180/pi), 'deg')
    # energy/h conserved along the hyperbola
    r2, v2 = propagate(mu, r0, v0, mpf(5))
    print('hyp E,h at t=5:', float(energy(mu, r2, v2)), float(angmom(r2, v2)))
