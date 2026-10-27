"""Golden for Test512: ship with a degree-2 Chebyshev child.

The child coefficients, mu, p0 and v0 are the exact IEEE doubles used by
TaylorShip. dps=60, K=24, h=0.005 (segment split at 0.25).
"""
import math
import time
from mpmath import mp, mpf, power

mp.dps = 60
L = 0.25
segments = 4
p0 = [1.0, 0.0, 0.0]
v0 = [0.0, 1.0, 0.0]
accel = [-1.0, 0.0, 0.0]
half = L / 2.0

coeffs = []
for s in range(segments):
    mid = s * L + half
    seg = []
    for axis in range(3):
        a = p0[axis] + (v0[axis] * mid) + (0.5 * accel[axis] * mid * mid)
        b = half * (v0[axis] + (accel[axis] * mid))
        c = 0.5 * accel[axis] * half * half
        seg.append([mpf(a + 0.5 * c), mpf(b), mpf(0.5 * c)])
    coeffs.append(seg)


def child_taylor(t):
    td = float(t)
    seg = int(math.floor(td / L))
    seg = max(0, min(segments - 1, seg))
    u = 2 * (t - mpf(seg * L)) / mpf(L) - 1
    out = []
    for axis in range(3):
        a, b, c = coeffs[seg][axis]
        val = a + b * u + c * (2 * u * u - 1)
        d1 = (b + 4 * c * u) * (2 / mpf(L))
        d2 = 4 * c * (2 / mpf(L)) ** 2
        out.append([val, d1, d2 / 2])
    return out


def pow_rec(u, alpha, K):
    w = [mpf(0)] * (K + 1)
    w[0] = power(u[0], alpha)
    for n in range(1, K + 1):
        s = mpf(0)
        for k in range(n):
            s += (alpha * (n - k) - k) * u[n - k] * w[k]
        w[n] = s / (n * u[0])
    return w


def force(P, C, mu_s, mu_e, n):
    a = [[mpf(0)] * (n + 1) for _ in range(3)]
    d = [[P[c][k] - C[c][k] for k in range(n + 1)] for c in range(3)]

    def sq(A):
        out = [mpf(0)] * (n + 1)
        for k in range(n + 1):
            out[k] = sum(sum(A[c][i] * A[c][k - i] for i in range(k + 1)) for c in range(3))
        return out

    w = pow_rec(sq(d), mpf(-3) / 2, n)
    wp = pow_rec(sq(P), mpf(-3) / 2, n)
    wc = pow_rec(sq(C), mpf(-3) / 2, n)
    for c in range(3):
        a[c][n] = (
            -mu_s * sum(P[c][k] * wp[n - k] for k in range(n + 1))
            - mu_e * sum(d[c][k] * w[n - k] for k in range(n + 1))
            - mu_e * sum(C[c][k] * wc[n - k] for k in range(n + 1))
        )
    return a


def horner(c, h):
    v = mpf(0)
    for k in range(len(c) - 1, -1, -1):
        v = v * h + c[k]
    return v


def run(h):
    mu_s = mpf(1)
    mu_e = mpf(float(0.01))
    p = [mpf(float(1.05)), mpf(0), mpf(0)]
    v = [mpf(0), mpf(float(1.0 + math.sqrt(0.01 / 0.05))), mpf(0)]
    K = 24
    t = mpf(0)
    T = mpf(1)
    while t < T:
        seg = int(math.floor(float(t) / L))
        seg = max(0, min(segments - 1, seg))
        seg_end = mpf((seg + 1) * L)
        step = min(h, seg_end - t, T - t)
        c = child_taylor(t)
        C = [[c[axis][0], c[axis][1], c[axis][2]] + [mpf(0)] * (K - 2) for axis in range(3)]
        P = [[p[axis]] + [mpf(0)] * K for axis in range(3)]
        V = [[v[axis]] + [mpf(0)] * K for axis in range(3)]
        for n in range(K):
            a = force(P, C, mu_s, mu_e, n)
            for axis in range(3):
                P[axis][n + 1] = V[axis][n] / (n + 1)
                V[axis][n + 1] = a[axis][n] / (n + 1)
        p = [horner(P[axis], step) for axis in range(3)]
        v = [horner(V[axis], step) for axis in range(3)]
        t += step
        if t >= T:
            break
    return p + v


if __name__ == "__main__":
    start = time.time()
    coarse = run(mpf("0.005"))
    fine = run(mpf("0.0025"))
    print("seconds", round(time.time() - start, 1))
    print("h=0.005 vs h=0.0025:", max(abs(x - y) for x, y in zip(coarse, fine)))
    for value in coarse:
        print(mp.nstr(value, 45))
