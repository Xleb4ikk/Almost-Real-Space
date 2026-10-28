#!/usr/bin/env python3
"""Independent check of the DOP853 tableau used by JplDop853.

Parses StudyDop853A/B/C/E5 from Study/methods/Dop853Tableau.cs and runs:

  1. Consistency: row sums of A equal C; sum(B) = 1; sum(E5) = 0.
  2. Order conditions: sum(B * C^k) = 1/(k+1) for k = 0..7 (necessary
     conditions for order 8 on autonomous problems).
  3. Empirical order of the 8th-order solution: fixed-step RK on a Kepler
     orbit with an exact (Kepler-equation) reference; error should fall
     by ~2^8 per halving of h.
  4. Empirical order of the embedded 5th-order error estimate:
     |h * sum(E5 * k)| for one step should scale as h^6.

Pure Python (no numpy). Exit code 0 on pass, 1 on failure.

Usage:
    python check_dop853_order.py [path/to/Dop853Tableau.cs]
Default path (relative to this script in Study/jpl/):
    ../methods/Dop853Tableau.cs
"""

import math
import os
import re
import sys


HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_PATH = os.path.join(HERE, "..", "methods", "Dop853Tableau.cs")


def parse_floats(text):
    out = []
    for tok in text.replace("\n", " ").split(","):
        tok = tok.strip()
        if tok:
            out.append(float(tok.rstrip("dDfF")))
    return out


def load_tableau(path):
    with open(path, encoding="utf-8-sig") as handle:
        src = handle.read()

    def block(name, next_name=None):
        start = src.index(name + " =")
        end = src.index(next_name, start) if next_name else len(src)
        return src[start:end]

    a_blk = block("StudyDop853A", "StudyDop853B")
    a_rows = re.findall(r"new\[\] \{(.*?)\}", a_blk, flags=re.S)
    A = [[0.0] * 12 for _ in range(12)]
    for i, row in enumerate(a_rows, start=1):
        vals = parse_floats(row)
        A[i][: len(vals)] = vals

    def vec(name, next_name=None):
        blk = block(name, next_name)
        inner = blk[blk.index("{") + 1: blk.index("};")]
        return parse_floats(inner)

    B = vec("StudyDop853B", "StudyDop853C")
    C = vec("StudyDop853C", "StudyDop853E5")
    E5 = vec("StudyDop853E5")
    assert len(B) == len(C) == len(E5) == 12, (len(B), len(C), len(E5))
    assert len(a_rows) == 11, len(a_rows)
    return A, B, C, E5


# ---- Kepler problem, mu = 1, a = 1, exact solution from periapsis ----------
ECC = 0.1


def kepler_exact(t):
    m = t % (2.0 * math.pi)
    e_anom = m
    for _ in range(60):
        e_anom -= (e_anom - ECC * math.sin(e_anom) - m) / (1.0 - ECC * math.cos(e_anom))
    s = math.sqrt(1.0 - ECC * ECC)
    d = 1.0 - ECC * math.cos(e_anom)
    return [
        math.cos(e_anom) - ECC,
        s * math.sin(e_anom),
        -math.sin(e_anom) / d,
        s * math.cos(e_anom) / d,
    ]


def f(y):
    r = math.hypot(y[0], y[1])
    r3 = r * r * r
    return [y[2], y[3], -y[0] / r3, -y[1] / r3]


def stages(y, h, A):
    k = []
    for s in range(12):
        ys = list(y)
        for j in range(s):
            a = A[s][j]
            if a:
                for c in range(4):
                    ys[c] += h * a * k[j][c]
        k.append(f(ys))
    return k


def step(y, h, A, B):
    k = stages(y, h, A)
    return [y[c] + h * sum(B[j] * k[j][c] for j in range(12)) for c in range(4)]


def integrate(n, total, A, B):
    y = kepler_exact(0.0)
    h = total / n
    for _ in range(n):
        y = step(y, h, A, B)
    return y


def norm_diff(a, b):
    return math.sqrt(sum((x - z) ** 2 for x, z in zip(a, b)))


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_PATH
    A, B, C, E5 = load_tableau(path)
    failures = []

    def check(ok, label, detail=""):
        print(("PASS " if ok else "FAIL ") + label + (" | " + detail if detail else ""))
        if not ok:
            failures.append(label)

    # 1. consistency
    row_err = max(abs(sum(A[i]) - C[i]) for i in range(12))
    check(row_err < 1e-14, "row sums of A equal C", "%.2e" % row_err)
    check(abs(sum(B) - 1.0) < 1e-14, "sum(B) = 1", "%.2e" % (sum(B) - 1.0))
    check(abs(sum(E5)) < 1e-14, "sum(E5) = 0", "%.2e" % sum(E5))

    # 2. order conditions (necessary for order 8)
    worst = 0.0
    for k in range(8):
        lhs = sum(B[j] * C[j] ** k for j in range(12))
        worst = max(worst, abs(lhs - 1.0 / (k + 1)))
    check(worst < 1e-14, "sum(B*C^k) = 1/(k+1), k = 0..7", "%.2e" % worst)

    # 3. empirical order of the 8th-order solution
    total = 2.0 * math.pi
    exact = kepler_exact(total)
    ns = [16, 24, 32, 48, 64, 96]
    errs = [norm_diff(integrate(n, total, A, B), exact) for n in ns]
    print("\nfixed-step Kepler, e=%.1f, one orbit" % ECC)
    print("   n          error   observed order")
    observed = []
    for i, (n, e) in enumerate(zip(ns, errs)):
        if i == 0 or e < 3e-13 or errs[i - 1] < 3e-13:
            order = "      -"  # first row, or at the round-off floor
        else:
            p = math.log(errs[i - 1] / e) / math.log(n / ns[i - 1])
            observed.append(p)
            order = "%7.2f" % p
        print("%4d  %13.3e  %s" % (n, e, order))
    # Eccentric orbits are pre-asymptotic at few steps per orbit, so accept a
    # wide band overall but require the finest resolved pair to be ~8.
    ok = (len(observed) >= 3
          and all(6.5 <= p <= 9.5 for p in observed)
          and 7.5 <= observed[-1] <= 9.0)
    check(ok, "8th-order convergence of the solution",
          "orders " + ", ".join("%.2f" % p for p in observed))

    # 4. embedded 5th-order estimate: one-step |h*sum(E5*k)| ~ h^6
    y0 = kepler_exact(0.0)
    hs = [0.2, 0.1, 0.05, 0.025]
    est = []
    for h in hs:
        k = stages(y0, h, A)
        e = [h * sum(E5[j] * k[j][c] for j in range(12)) for c in range(4)]
        est.append(math.sqrt(sum(x * x for x in e)))
    print("\nembedded estimate |h*sum(E5*k)|, one step from periapsis")
    print("   h          estimate   observed exponent")
    slopes = []
    for i, (h, e) in enumerate(zip(hs, est)):
        if i == 0:
            slope = "      -"
        else:
            s_ = math.log2(est[i - 1] / e)
            slopes.append(s_)
            slope = "%7.2f" % s_
        print("%6.3f  %13.3e  %s" % (h, e, slope))
    ok5 = all(5.0 <= s_ <= 7.5 for s_ in slopes)
    check(ok5, "embedded 5th-order estimate scales ~ h^6 (local)",
          "exponents " + ", ".join("%.2f" % s_ for s_ in slopes))

    print()
    if failures:
        print("FAILED: " + "; ".join(failures))
        return 1
    print("ALL PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
