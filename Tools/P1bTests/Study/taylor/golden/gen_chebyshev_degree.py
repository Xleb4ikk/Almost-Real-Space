"""Golden for Test515. dps=60; Chebyshev degree 12, u0=-0.26 (t=0.37, L=1)."""
from mpmath import mp, mpf, chebyt, taylor

mp.dps = 60
K = 12
base = [mpf(1) / (k + 1) for k in range(K + 1)]
t0 = mpf("0.37")


def p(tau):
    u = 2 * (t0 + tau) - 1
    return sum(base[k] * chebyt(k, u) for k in range(K + 1))


pos = taylor(p, 0, K)
vel = [(n + 1) * pos[n + 1] for n in range(K)] + [mpf(0)]
print("POS:")
for x in pos:
    print(mp.nstr(x, 40))
print("VEL:")
for x in vel:
    print(mp.nstr(x, 40))
