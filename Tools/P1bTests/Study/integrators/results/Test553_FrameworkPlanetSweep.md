# Integrator comparison

Cost = RHS-equivalent calls needed to reach the error target (log-log interpolation between the two bracketing runs; never extrapolated).
`EDGE` = the coarsest tested point was already below the target (cost is an upper bound, not a result). `not reached` = the finest run (or the RHS budget) stayed above the target; the parenthesised value is the best error it did reach. `n/a` = method not valid for this problem.
Build: Release. Per-run RHS budget: 2,000,000.

## Who wins where

| scenario | 100 m | 10 m | 1 m |
|---|---|---|---|
| Real solar 7-body, 10 y | DOP853-adaptive [cap=86400 s] (57,926) | DOP853-adaptive (75,658) | Y6C (364,568) |

## Real solar 7-body, 10 y

Reference floor: 5.88E-002 m. Kepler-solve weight: 0.00 RHS. RHS multiplier: 1.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 517,628 (72 ms) | 761,682 (106 ms) | not reached (best 2.02E+000) |
| DOPRI5-fixed | 345,199 (74 ms) | 551,385 (119 ms) | not reached (best 2.68E+000) |
| DOPRI5-adaptive | 349,417 (91 ms) | 554,881 (145 ms) | not reached (best 1.37E+000) |
| DOPRI5-adaptive [cap=86400 s] | 349,417 (91 ms) | 554,881 (144 ms) | not reached (best 1.37E+000) |
| DOP853-adaptive | 58,273 (18 ms) | 75,658 (24 ms) | not reached (best 1.11E+000) |
| DOP853-adaptive [cap=86400 s] | 57,926 (18 ms) | 75,658 (24 ms) | not reached (best 1.11E+000) |
| PEFRL | 308,844 (42 ms) | 619,253 (84 ms) | not reached (best 3.09E+000) |
| Y6 | 157,628 (25 ms) | 383,268 (62 ms) | not reached (best 5.53E+000) |
| Y6C | 157,042 (39 ms) | 231,950 (57 ms) | 364,568 (90 ms) |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | not reached (best 3.36E+004) | not reached (best 3.36E+004) | not reached (best 3.36E+004) |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- DOPRI5-adaptive: 6 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=86400 s]: CAP-BOUND in 11 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=86400 s]: CAP-BOUND in 28 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- PEFRL: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Y6: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
