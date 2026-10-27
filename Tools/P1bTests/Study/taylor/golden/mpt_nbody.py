"""Arbitrary-precision N-body Taylor integrator (recurrences, no full jets).

Used by the golden generators. Units are arbitrary; G is absorbed in mu.
"""
from mpmath import mp, mpf, power


def pow_rec(u, alpha, K):
    w = [mpf(0)] * (K + 1)
    w[0] = power(u[0], alpha)
    for n in range(1, K + 1):
        s = mpf(0)
        for k in range(n):
            s += (alpha * (n - k) - k) * u[n - k] * w[k]
        w[n] = s / (n * u[0])
    return w


def nbody_step_coeffs(mu, p, v, K):
    n = len(mu)
    dim = 3 * n
    P = [[mpf(0)] * (K + 2) for _ in range(dim)]
    V = [[mpf(0)] * (K + 2) for _ in range(dim)]
    for i in range(dim):
        P[i][0] = p[i]
        V[i][0] = v[i]
    pairs = [(i, j) for i in range(n) for j in range(i + 1, n)]
    D = {pr: [[mpf(0)] * (K + 1) for _ in range(3)] for pr in pairs}
    U = {pr: [mpf(0)] * (K + 1) for pr in pairs}
    W = {pr: [mpf(0)] * (K + 1) for pr in pairs}
    for m in range(K):
        a = [mpf(0)] * dim
        for (i, j) in pairs:
            d = D[(i, j)]
            u = U[(i, j)]
            w = W[(i, j)]
            for c in range(3):
                d[c][m] = P[3 * j + c][m] - P[3 * i + c][m]
            u[m] = sum(d[c][k] * d[c][m - k] for c in range(3) for k in range(m + 1))
            if m == 0:
                w[0] = power(u[0], mpf(-3) / 2)
            else:
                al = mpf(-3) / 2
                s = mpf(0)
                for k in range(m):
                    s += (al * (m - k) - k) * u[m - k] * w[k]
                w[m] = s / (m * u[0])
            for c in range(3):
                f = sum(d[c][k] * w[m - k] for k in range(m + 1))
                a[3 * i + c] += mu[j] * f
                a[3 * j + c] -= mu[i] * f
        for i in range(dim):
            P[i][m + 1] = V[i][m] / (m + 1)
            V[i][m + 1] = a[i] / (m + 1)
    return P, V


def horner(c, h):
    v = mpf(0)
    for k in range(len(c) - 1, -1, -1):
        v = v * h + c[k]
    return v


def run_nbody(mu, P0, V0, T, K=40, tol=mpf(10) ** -40, hmax=mpf("0.02")):
    n = len(mu)
    dim = 3 * n
    p = [mpf(x) for x in P0]
    v = [mpf(x) for x in V0]
    mu = [mpf(x) for x in mu]
    t = mpf(0)
    h = hmax
    steps = 0
    T = mpf(T)
    while t < T:
        P, V = nbody_step_coeffs(mu, p, v, K)
        hh = min(h, T - t)
        while True:
            e = mpf(0)
            for i in range(dim):
                e = max(e,
                        abs(P[i][K]) * hh ** K,
                        abs(P[i][K - 1]) * hh ** (K - 1),
                        abs(V[i][K]) * hh ** K,
                        abs(V[i][K - 1]) * hh ** (K - 1))
            if e <= tol:
                break
            hh = hh * mpf("0.7")
        p = [horner(P[i], hh) for i in range(dim)]
        v = [horner(V[i], hh) for i in range(dim)]
        t += hh
        steps += 1
        h = min(hmax, hh * mpf("1.3"))
    return p, v, steps
