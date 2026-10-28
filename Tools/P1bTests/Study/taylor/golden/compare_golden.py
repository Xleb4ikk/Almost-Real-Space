#!/usr/bin/env python3
"""Compare two golden files checkpoint by checkpoint (mpmath, same format)."""
import json
import sys
from mpmath import mp, mpf, nstr

if len(sys.argv) != 3:
    sys.exit("usage: compare_golden.py base.json candidate.json")

mp.dps = 60
base = json.load(open(sys.argv[1], encoding="utf-8-sig"))
cand = json.load(open(sys.argv[2], encoding="utf-8-sig"))
if len(base["checkpoints"]) != len(cand["checkpoints"]):
    sys.exit("different checkpoint counts")

for a, b in zip(base["checkpoints"], cand["checkpoints"]):
    if abs(float(a["days"]) - float(b["days"])) > 1e-12:
        sys.exit("checkpoint day mismatch: %s vs %s" % (a["days"], b["days"]))
    dp = max(abs(mpf(x) - mpf(y)) for x, y in zip(a["P"], b["P"]))
    dv = max(abs(mpf(x) - mpf(y)) for x, y in zip(a["V"], b["V"]))
    cert = b.get("golden_error_estimate")
    print("day %-6s dp=%-12s dv=%-12s cert=%s" % (
        a["days"], nstr(dp, 3), nstr(dv, 3), cert if cert else "-"))
