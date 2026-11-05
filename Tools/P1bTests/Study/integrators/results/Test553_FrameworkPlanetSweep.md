# Integrator comparison

Cost = RHS-equivalent calls needed to reach the error target (log-log interpolation between the two bracketing runs; never extrapolated).
`EDGE` = the coarsest tested point was already below the target (cost is an upper bound, not a result). `not reached` = the finest run (or the RHS budget) stayed above the target. `n/a` = method not valid for this problem.
Build: Release. Per-run RHS budget: 2,000,000.

## Who wins where

| scenario | 100 m | 10 m | 1 m |
|---|---|---|---|
| Real solar 7-body, 10 y | Y6C (157,042) | Y6C (231,950) | Y6C (364,568) |

## Real solar 7-body, 10 y

Reference floor: 5.88E-002 m. Kepler-solve weight: 0.00 RHS. RHS multiplier: 1.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 517,628 (73 ms) | 761,682 (107 ms) | not reached |
| DOPRI5-fixed | 345,199 (74 ms) | 551,385 (120 ms) | not reached |
| DOPRI5-adaptive | not reached | not reached | not reached |
| DOPRI5-adaptive [cap=86400 s] | not reached | not reached | not reached |
| DOP853-adaptive | not reached | not reached | not reached |
| DOP853-adaptive [cap=86400 s] | not reached | not reached | not reached |
| PEFRL | 308,844 (42 ms) | 619,253 (84 ms) | not reached |
| Y6 | 157,628 (26 ms) | 383,268 (62 ms) | not reached |
| Y6C | 157,042 (37 ms) | 231,950 (54 ms) | 364,568 (85 ms) |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | not reached | not reached | not reached |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- DOPRI5-adaptive [cap=86400 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=86400 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- PEFRL: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Y6: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
