# Integrator comparison

Cost = RHS-equivalent calls needed to reach the error target (log-log interpolation between the two bracketing runs; never extrapolated).
`EDGE` = the coarsest tested point was already below the target (cost is an upper bound, not a result). `not reached` = the finest run (or the RHS budget) stayed above the target. `n/a` = method not valid for this problem.
Build: Release. Per-run RHS budget: 2,000,000.

## Who wins where

| scenario | 1.0E-6 rel (1E-06 m) | 1.0E-8 rel (1E-08 m) | 1.0E-10 rel (1E-10 m) |
|---|---|---|---|
| Earth LEO 400 km circular | - | - | - |
| Earth LEO 400 km polar | - | - | - |
| Earth LEO 400 km sun-synchronous | - | - | - |
| Earth GTO 400 x 40000 km | - | - | - |
| Earth Molniya 500 x 40000 km | - | - | - |
| Earth GEO 35786 km | - | - | - |
| Sun 1 AU circular | - | - | - |
| Sun 0.5 x 2 AU eccentric | - | - | - |
| Earth 400 km + J2 | - | - | - |
| Earth 250 km + atmosphere drag | - | - | - |
| Earth LEO 400 km + 600 s prograde burn | - | - | - |
| Earth-Moon flyby inside lunar SOI (ref. NOT certified) | - | - | - |
| Earth LEO 400 km, 1000 orbits (~64 d) | - | - | - |
| Earth HEO 300 x 120000 km | - | - | - |
| Earth hyperbolic flyby e=1.5 | DOPRI5-adaptive (258) | DOP853-adaptive (382) | DOP853-adaptive (521) |
| Earth LEO 400 km, 30 d, world chunk 1 d | - | - | - |
| Earth LEO 400 km + unaligned 600 s burn | DOP853-adaptive [cap=600 s] (4,319) | DOP853-adaptive [cap=600 s] (5,528) | - |

## Earth LEO 400 km circular

Reference floor: 5.02E-006 m. Kepler-solve weight: 1.23 RHS. RHS multiplier: 1.

| integrator | 1.0E-6 rel (1E-06 m) | 1.0E-8 rel (1E-08 m) | 1.0E-10 rel (1E-10 m) |
|---|---|---|---|
| RK4 | skip | skip | skip |
| DOPRI5-fixed | skip | skip | skip |
| DOPRI5-adaptive | skip | skip | skip |
| DOPRI5-adaptive [cap=600 s] | skip | skip | skip |
| DOP853-adaptive | skip | skip | skip |
| DOP853-adaptive [cap=600 s] | skip | skip | skip |
| PEFRL | skip | skip | skip |
| Y6 | skip | skip | skip |
| Y6C | skip | skip | skip |
| WH-Y6 | skip | skip | skip |
| Leapfrog | skip | skip | skip |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- WH-Y6: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').

## Earth LEO 400 km polar

Reference floor: 1.46E-006 m. Kepler-solve weight: 0.80 RHS. RHS multiplier: 1.

| integrator | 1.0E-6 rel (1E-06 m) | 1.0E-8 rel (1E-08 m) | 1.0E-10 rel (1E-10 m) |
|---|---|---|---|
| RK4 | skip | skip | skip |
| DOPRI5-fixed | skip | skip | skip |
| DOPRI5-adaptive | skip | skip | skip |
| DOPRI5-adaptive [cap=600 s] | skip | skip | skip |
| DOP853-adaptive | skip | skip | skip |
| DOP853-adaptive [cap=600 s] | skip | skip | skip |
| PEFRL | skip | skip | skip |
| Y6 | skip | skip | skip |
| Y6C | skip | skip | skip |
| WH-Y6 | skip | skip | skip |
| Leapfrog | skip | skip | skip |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- RK4: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- DOPRI5-fixed: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- PEFRL: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Y6: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').

## Earth LEO 400 km sun-synchronous

Reference floor: 2.62E-006 m. Kepler-solve weight: 0.56 RHS. RHS multiplier: 1.

| integrator | 1.0E-6 rel (1E-06 m) | 1.0E-8 rel (1E-08 m) | 1.0E-10 rel (1E-10 m) |
|---|---|---|---|
| RK4 | skip | skip | skip |
| DOPRI5-fixed | skip | skip | skip |
| DOPRI5-adaptive | skip | skip | skip |
| DOPRI5-adaptive [cap=600 s] | skip | skip | skip |
| DOP853-adaptive | skip | skip | skip |
| DOP853-adaptive [cap=600 s] | skip | skip | skip |
| PEFRL | skip | skip | skip |
| Y6 | skip | skip | skip |
| Y6C | skip | skip | skip |
| WH-Y6 | skip | skip | skip |
| Leapfrog | skip | skip | skip |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- RK4: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- PEFRL: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Y6: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- WH-Y6: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').

## Earth GTO 400 x 40000 km

Reference floor: 3.39E-006 m. Kepler-solve weight: 1.10 RHS. RHS multiplier: 1.

| integrator | 1.0E-6 rel (1E-06 m) | 1.0E-8 rel (1E-08 m) | 1.0E-10 rel (1E-10 m) |
|---|---|---|---|
| RK4 | skip | skip | skip |
| DOPRI5-fixed | skip | skip | skip |
| DOPRI5-adaptive | skip | skip | skip |
| DOPRI5-adaptive [cap=600 s] | skip | skip | skip |
| DOP853-adaptive | skip | skip | skip |
| DOP853-adaptive [cap=600 s] | skip | skip | skip |
| PEFRL | skip | skip | skip |
| Y6 | skip | skip | skip |
| Y6C | skip | skip | skip |
| WH-Y6 | skip | skip | skip |
| Leapfrog | skip | skip | skip |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 10 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- PEFRL: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Y6: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- WH-Y6: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Earth Molniya 500 x 40000 km

Reference floor: 9.52E-005 m. Kepler-solve weight: 1.10 RHS. RHS multiplier: 1.

| integrator | 1.0E-6 rel (1E-06 m) | 1.0E-8 rel (1E-08 m) | 1.0E-10 rel (1E-10 m) |
|---|---|---|---|
| RK4 | skip | skip | skip |
| DOPRI5-fixed | skip | skip | skip |
| DOPRI5-adaptive | skip | skip | skip |
| DOPRI5-adaptive [cap=600 s] | skip | skip | skip |
| DOP853-adaptive | skip | skip | skip |
| DOP853-adaptive [cap=600 s] | skip | skip | skip |
| PEFRL | skip | skip | skip |
| Y6 | skip | skip | skip |
| Y6C | skip | skip | skip |
| WH-Y6 | skip | skip | skip |
| Leapfrog | skip | skip | skip |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 10 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Earth GEO 35786 km

Reference floor: 1.80E-004 m. Kepler-solve weight: 0.70 RHS. RHS multiplier: 1.

| integrator | 1.0E-6 rel (1E-06 m) | 1.0E-8 rel (1E-08 m) | 1.0E-10 rel (1E-10 m) |
|---|---|---|---|
| RK4 | skip | skip | skip |
| DOPRI5-fixed | skip | skip | skip |
| DOPRI5-adaptive | skip | skip | skip |
| DOPRI5-adaptive [cap=600 s] | skip | skip | skip |
| DOP853-adaptive | skip | skip | skip |
| DOP853-adaptive [cap=600 s] | skip | skip | skip |
| PEFRL | skip | skip | skip |
| Y6 | skip | skip | skip |
| Y6C | skip | skip | skip |
| WH-Y6 | skip | skip | skip |
| Leapfrog | skip | skip | skip |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- PEFRL: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Sun 1 AU circular

Reference floor: 5.31E-004 m. Kepler-solve weight: 0.73 RHS. RHS multiplier: 1.

| integrator | 1.0E-6 rel (1E-06 m) | 1.0E-8 rel (1E-08 m) | 1.0E-10 rel (1E-10 m) |
|---|---|---|---|
| RK4 | skip | skip | skip |
| DOPRI5-fixed | skip | skip | skip |
| DOPRI5-adaptive | skip | skip | skip |
| DOPRI5-adaptive [cap=600 s] | skip | skip | skip |
| DOP853-adaptive | skip | skip | skip |
| DOP853-adaptive [cap=600 s] | skip | skip | skip |
| PEFRL | skip | skip | skip |
| Y6 | skip | skip | skip |
| Y6C | skip | skip | skip |
| WH-Y6 | skip | skip | skip |
| Leapfrog | skip | skip | skip |

Findings:
- RK4: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- DOPRI5-fixed: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Y6: error GROWS as h shrinks in 3 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Y6: observed order 2.08 vs expected 6 (2 pairs): check the implementation or the regime.
- WH-Y6: error GROWS as h shrinks in 3 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').

## Sun 0.5 x 2 AU eccentric

Reference floor: 9.18E-004 m. Kepler-solve weight: 1.09 RHS. RHS multiplier: 1.

| integrator | 1.0E-6 rel (1E-06 m) | 1.0E-8 rel (1E-08 m) | 1.0E-10 rel (1E-10 m) |
|---|---|---|---|
| RK4 | skip | skip | skip |
| DOPRI5-fixed | skip | skip | skip |
| DOPRI5-adaptive | skip | skip | skip |
| DOPRI5-adaptive [cap=600 s] | skip | skip | skip |
| DOP853-adaptive | skip | skip | skip |
| DOP853-adaptive [cap=600 s] | skip | skip | skip |
| PEFRL | skip | skip | skip |
| Y6 | skip | skip | skip |
| Y6C | skip | skip | skip |
| WH-Y6 | skip | skip | skip |
| Leapfrog | skip | skip | skip |

Findings:
- RK4: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- DOPRI5-fixed: error GROWS as h shrinks in 4 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- DOPRI5-fixed: observed order 0.53 vs expected 5 (2 pairs): check the implementation or the regime.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Y6: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- WH-Y6: error GROWS as h shrinks in 6 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').

## Earth 400 km + J2

Reference floor: 2.57E-006 m. Kepler-solve weight: 0.64 RHS. RHS multiplier: 1.

| integrator | 1.0E-6 rel (1E-06 m) | 1.0E-8 rel (1E-08 m) | 1.0E-10 rel (1E-10 m) |
|---|---|---|---|
| RK4 | skip | skip | skip |
| DOPRI5-fixed | skip | skip | skip |
| DOPRI5-adaptive | skip | skip | skip |
| DOPRI5-adaptive [cap=600 s] | skip | skip | skip |
| DOP853-adaptive | skip | skip | skip |
| DOP853-adaptive [cap=600 s] | skip | skip | skip |
| PEFRL | skip | skip | skip |
| Y6 | skip | skip | skip |
| Y6C | skip | skip | skip |
| WH-Y6 | skip | skip | skip |
| Leapfrog | skip | skip | skip |

Findings:
- RK4: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- DOPRI5-fixed: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Y6: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').

## Earth 250 km + atmosphere drag

Reference floor: 1.92E-005 m. Kepler-solve weight: 0.13 RHS. RHS multiplier: 1.

| integrator | 1.0E-6 rel (1E-06 m) | 1.0E-8 rel (1E-08 m) | 1.0E-10 rel (1E-10 m) |
|---|---|---|---|
| RK4 | skip | skip | skip |
| DOPRI5-fixed | skip | skip | skip |
| DOPRI5-adaptive | skip | skip | skip |
| DOPRI5-adaptive [cap=600 s] | skip | skip | skip |
| DOP853-adaptive | skip | skip | skip |
| DOP853-adaptive [cap=600 s] | skip | skip | skip |
| PEFRL | n/a | n/a | n/a |
| Y6 | n/a | n/a | n/a |
| Y6C | n/a | n/a | n/a |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | n/a | n/a | n/a |

Findings:
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.

## Earth LEO 400 km + 600 s prograde burn

Reference floor: 1.78E-005 m. Kepler-solve weight: 0.73 RHS. RHS multiplier: 1.

| integrator | 1.0E-6 rel (1E-06 m) | 1.0E-8 rel (1E-08 m) | 1.0E-10 rel (1E-10 m) |
|---|---|---|---|
| RK4 | skip | skip | skip |
| DOPRI5-fixed | skip | skip | skip |
| DOPRI5-adaptive | skip | skip | skip |
| DOPRI5-adaptive [cap=600 s] | skip | skip | skip |
| DOP853-adaptive | skip | skip | skip |
| DOP853-adaptive [cap=600 s] | skip | skip | skip |
| PEFRL | n/a | n/a | n/a |
| Y6 | n/a | n/a | n/a |
| Y6C | n/a | n/a | n/a |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | n/a | n/a | n/a |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.

## Earth-Moon flyby inside lunar SOI

Reference floor: not certified. Kepler-solve weight: 1.08 RHS. RHS multiplier: 1.

| integrator | 1.0E-6 rel (1E-06 m) | 1.0E-8 rel (1E-08 m) | 1.0E-10 rel (1E-10 m) |
|---|---|---|---|
| RK4 | not reached | not reached | not reached |
| DOPRI5-fixed | not reached | not reached | not reached |
| DOPRI5-adaptive | not reached | not reached | not reached |
| DOPRI5-adaptive [cap=600 s] | not reached | not reached | not reached |
| DOP853-adaptive | not reached | not reached | not reached |
| DOP853-adaptive [cap=600 s] | not reached | not reached | not reached |
| PEFRL | not reached | not reached | not reached |
| Y6 | not reached | not reached | not reached |
| Y6C | not reached | not reached | not reached |
| WH-Y6 | not reached | not reached | not reached |
| Leapfrog | not reached | not reached | not reached |

Findings:
- REFERENCE NOT CERTIFIED: every number of this scenario is provisional (no reference floor).
- RK4: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- DOPRI5-fixed: observed order 1.83 vs expected 5 (7 pairs): check the implementation or the regime.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: error GROWS as h shrinks in 3 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Y6: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Y6: observed order 2.82 vs expected 6 (7 pairs): check the implementation or the regime.
- Y6C: observed order 0.39 vs expected 6 (10 pairs): check the implementation or the regime.
- WH-Y6: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').

## Earth LEO 400 km, 1000 orbits (~64 d)

Reference floor: 8.52E+000 m. Kepler-solve weight: 0.73 RHS. RHS multiplier: 1.

| integrator | 1.0E-6 rel (1E-06 m) | 1.0E-8 rel (1E-08 m) | 1.0E-10 rel (1E-10 m) |
|---|---|---|---|
| RK4 | skip | skip | skip |
| DOPRI5-fixed | skip | skip | skip |
| DOPRI5-adaptive | skip | skip | skip |
| DOPRI5-adaptive [cap=600 s] | skip | skip | skip |
| DOP853-adaptive | skip | skip | skip |
| DOP853-adaptive [cap=600 s] | skip | skip | skip |
| PEFRL | skip | skip | skip |
| Y6 | skip | skip | skip |
| Y6C | skip | skip | skip |
| WH-Y6 | skip | skip | skip |
| Leapfrog | skip | skip | skip |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive [cap=600 s]: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Earth HEO 300 x 120000 km

Reference floor: 2.32E-005 m. Kepler-solve weight: 1.02 RHS. RHS multiplier: 1.

| integrator | 1.0E-6 rel (1E-06 m) | 1.0E-8 rel (1E-08 m) | 1.0E-10 rel (1E-10 m) |
|---|---|---|---|
| RK4 | skip | skip | skip |
| DOPRI5-fixed | skip | skip | skip |
| DOPRI5-adaptive | skip | skip | skip |
| DOPRI5-adaptive [cap=600 s] | skip | skip | skip |
| DOP853-adaptive | skip | skip | skip |
| DOP853-adaptive [cap=600 s] | skip | skip | skip |
| PEFRL | skip | skip | skip |
| Y6 | skip | skip | skip |
| Y6C | skip | skip | skip |
| WH-Y6 | skip | skip | skip |
| Leapfrog | skip | skip | skip |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- WH-Y6: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Earth hyperbolic flyby e=1.5

Reference floor: 1.82E-006 m. Kepler-solve weight: 3.21 RHS. RHS multiplier: 1.

| integrator | 1.0E-6 rel (100 m) | 1.0E-8 rel (1 m) | 1.0E-10 rel (0.01 m) |
|---|---|---|---|
| RK4 | 8,859 (3 ms) | 27,666 (8 ms) | 87,118 (28 ms) |
| DOPRI5-fixed | 3,958 (2 ms) | 9,497 (4 ms) | 22,334 (9 ms) |
| DOPRI5-adaptive | 258 (0 ms) | 459 (0 ms) | 884 (0 ms) |
| DOPRI5-adaptive [cap=600 s] | 938 (0 ms) | 1,079 (0 ms) | 1,428 (1 ms) |
| DOP853-adaptive | 307 (0 ms) | 382 (0 ms) | 521 (0 ms) |
| DOP853-adaptive [cap=600 s] | not reached | not reached | not reached |
| PEFRL | 7,396 (3 ms) | 23,375 (10 ms) | 73,901 (32 ms) |
| Y6 | 6,979 (3 ms) | 15,050 (7 ms) | 32,428 (14 ms) |
| Y6C | 6,979 (3 ms) | 15,050 (7 ms) | 32,430 (14 ms) |
| WH-Y6 | <=473 EDGE | not reached | not reached |
| Leapfrog | 135,173 (59 ms) | 1.35E+006 (598 ms) | not reached |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 16 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.

## Earth LEO 400 km, 30 d, world chunk 1 d

Reference floor: 2.54E-003 m. Kepler-solve weight: 0.51 RHS. RHS multiplier: 1.

| integrator | 1.0E-6 rel (1E-06 m) | 1.0E-8 rel (1E-08 m) | 1.0E-10 rel (1E-10 m) |
|---|---|---|---|
| RK4 | skip | skip | skip |
| DOPRI5-fixed | skip | skip | skip |
| DOPRI5-adaptive | skip | skip | skip |
| DOPRI5-adaptive [cap=600 s] | skip | skip | skip |
| DOP853-adaptive | skip | skip | skip |
| DOP853-adaptive [cap=600 s] | skip | skip | skip |
| PEFRL | skip | skip | skip |
| Y6 | skip | skip | skip |
| Y6C | skip | skip | skip |
| WH-Y6 | skip | skip | skip |
| Leapfrog | skip | skip | skip |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 3 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6C: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Earth LEO 400 km + unaligned 600 s burn

Reference floor: 2.34E-004 m. Kepler-solve weight: 0.77 RHS. RHS multiplier: 1.

| integrator | 1.0E-6 rel (6.77 m) | 1.0E-8 rel (0.0677 m) | 1.0E-10 rel (0.000677 m) |
|---|---|---|---|
| RK4 | not reached | not reached | skip |
| DOPRI5-fixed | not reached | not reached | skip |
| DOPRI5-adaptive | 5,597 (2 ms) | not reached | skip |
| DOPRI5-adaptive [cap=600 s] | 5,597 (2 ms) | not reached | skip |
| DOP853-adaptive | 4,329 (2 ms) | 5,674 (3 ms) | skip |
| DOP853-adaptive [cap=600 s] | 4,319 (2 ms) | 5,528 (2 ms) | skip |
| PEFRL | n/a | n/a | n/a |
| Y6 | n/a | n/a | n/a |
| Y6C | n/a | n/a | n/a |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | n/a | n/a | n/a |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- RK4: observed order 1.02 vs expected 4 (8 pairs): check the implementation or the regime.
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: observed order 1.02 vs expected 5 (8 pairs): check the implementation or the regime.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 9 run(s); the step cap, not the tolerance, sets the cost there.
