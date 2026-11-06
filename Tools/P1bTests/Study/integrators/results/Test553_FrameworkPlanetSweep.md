# Integrator comparison

Cost = RHS-equivalent calls of the cheapest actually executed run whose measured error is at or below the target (no interpolation, never extrapolated).
`EDGE` = even the cheapest tested point was already below the target (cost is an upper bound, not a result). `~` marks methods within 10% of the winner: that ranking is not resolved. `not reached` = the finest run (or the RHS budget) stayed above the target; the parenthesised value is the best error it did reach. `below round-off floor` = the target is at or below the run's own double-precision accumulation noise, so it is not ranked. `n/a` = method not valid for this problem. Scenarios whose reference is not certified are listed in the detail but excluded from the summary.
Build: Release. Per-run RHS budget: per scenario (see section headers).

## Who wins where

| scenario | 100 m | 10 m | 1 m |
|---|---|---|---|
| Real solar 7-body, 10 y | DOP853-adaptive (62,845) | DOP853-adaptive (83,461) | DOP853-adaptive (197,137) |
| Real solar 7-body, 100 y | DOP853-adaptive (1.11E+006) | DOP853-adaptive (1.71E+006) | - |
| Custom 6-body, 10 y | DOP853-adaptive (81,841) | DOP853-adaptive (109,597) | DOP853-adaptive (145,945) |
| Tight binary, 10 y | DOP853-adaptive (132,421) | DOP853-adaptive (176,257) | Y6C (981,793) |
| Sun + eccentric comet + Jupiter, 10 y | DOP853-adaptive (15,426) | DOP853-adaptive (19,961) | DOP853-adaptive (22,909) |
| Heliocentric Sun-Jupiter probe, 10 y | WH-Y6 (5,083) | WH-Y6 (6,777) | WH-Y6 (10,165) ~ DOP853-adaptive (10,945) [tie within 10%] |

## Real solar 7-body, 10 y

Reference floor: 5.88E-002 m (independent dd Taylor references (43200 s and 21600 s); floor = worst pair difference). Kepler-solve weight: 1.00 RHS. RHS multiplier: 1. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 532,780 (164 ms) | 884,392 (275 ms) | not reached (best 2.02E+000) |
| DOPRI5-fixed | 355,027 (173 ms) | 532,537 (261 ms) | not reached (best 2.68E+000) |
| DOPRI5-adaptive | 334,453 (197 ms) | 667,015 (393 ms) | below round-off floor (2.05E+000 m) |
| DOPRI5-adaptive [cap=86400 s] | 334,453 (196 ms) | 667,015 (393 ms) | below round-off floor (2.05E+000 m) |
| DOP853-adaptive | 62,845 (44 ms) | 83,461 (57 ms) | 197,137 (135 ms) |
| DOP853-adaptive [cap=86400 s] | 62,845 (43 ms) | 83,461 (57 ms) | 197,137 (137 ms) |
| PEFRL | 320,936 (97 ms) | 722,220 (216 ms) | not reached (best 3.09E+000) |
| Y6 | 167,042 (61 ms) | 507,942 (187 ms) | not reached (best 5.53E+000) |
| Y6C | 167,042 (90 ms) | 249,712 (132 ms) | below round-off floor (1.53E+000 m) |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | not reached (best 3.36E+004) | not reached (best 3.36E+004) | not reached (best 3.36E+004) |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- DOPRI5-adaptive: 3 run(s) did not complete the horizon: aborted at t=1.805E+007 s: a pair distance collapsed to 3.74E+004 m (initial minimum 4.00E+008 m): the trajectory left the physical domain; worst error at completed checkpoints 6.18E+008 m; excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=86400 s]: CAP-BOUND in 8 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=86400 s]: CAP-BOUND in 23 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- PEFRL: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- PEFRL: error is non-monotonic at coarse h in 1 place(s) but recovers at finer h (h=2.14E+03 -> 1.94E+03 s, recovers by h=1.75E+03 s); that coarse branch is pre-asymptotic, not a round-off floor.
- Y6: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- double-precision round-off estimate 1.53E+000 m (16 eps * scale * sqrt(steps=98,253 of the most accurate run); each cell uses its own run's step count, and a run whose error is within that noise is reported as 'below round-off floor', not as a ranking result (3 cell(s) excluded).

## Real solar 7-body, 100 y

Reference floor: 1.71E+000 m (independent dd Taylor references (43200 s and 21600 s); floor = worst pair difference). Kepler-solve weight: 1.00 RHS. RHS multiplier: 1. RHS budget: 5,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | not reached (best 3.39E+004) | not reached (best 3.39E+004) | skip |
| DOPRI5-fixed | not reached (best 5.36E+003) | not reached (best 5.36E+003) | skip |
| DOPRI5-adaptive | not reached | not reached | skip |
| DOPRI5-adaptive [cap=86400 s] | not reached (best 3.07E+003) | not reached (best 3.07E+003) | skip |
| DOP853-adaptive | 1.11E+006 (779 ms) | 1.71E+006 (1251 ms) | skip |
| DOP853-adaptive [cap=86400 s] | 1.11E+006 (821 ms) | 1.71E+006 (1190 ms) | skip |
| PEFRL | not reached (best 1.59E+002) | not reached (best 1.59E+002) | skip |
| Y6 | 4.59E+006 (1728 ms) | not reached (best 9.41E+001) | skip |
| Y6C | 2.50E+006 (1323 ms) | 3.38E+006 (1807 ms) | skip |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | not reached (best 4.52E+006) | not reached (best 4.52E+006) | skip |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 4 run(s) did not complete the horizon: aborted at t=1.805E+007 s: a pair distance collapsed to 3.74E+004 m (initial minimum 4.00E+008 m): the trajectory left the physical domain; worst error at completed checkpoints 6.18E+008 m; excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=86400 s]: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive [cap=86400 s]: CAP-BOUND in 8 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive: 1 run(s) did not complete the horizon: aborted at t=1.301E+009 s: a pair distance collapsed to 4.00E+004 m (initial minimum 4.00E+008 m): the trajectory left the physical domain; worst error at completed checkpoints 7.75E+008 m; excluded from ranking, finer controls were still tried.
- DOP853-adaptive [cap=86400 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: observed order 4.97 vs expected 6 (14 pairs): check the implementation or the regime.
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: the uncapped branch produced no finite samples while DOPRI5-adaptive [cap=86400 s] reached 3.07E+003 m; the step cap is load-bearing on this horizon.

## Custom 6-body, 10 y

Reference floor: 3.59E-002 m (independent dd Taylor references (43200 s and 21600 s); floor = worst pair difference). Kepler-solve weight: 1.00 RHS. RHS multiplier: 1. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 795,272 (191 ms) | 1.32E+006 (318 ms) | not reached (best 4.22E+000) |
| DOPRI5-fixed | 530,347 (210 ms) | 880,255 (350 ms) | not reached (best 2.53E+000) |
| DOPRI5-adaptive | 590,827 (274 ms) | 936,115 (431 ms) | not reached (best 1.42E+000) |
| DOPRI5-adaptive [cap=86400 s] | 590,827 (363 ms) | 936,115 (603 ms) | not reached (best 1.42E+000) |
| DOP853-adaptive | 81,841 (45 ms) | 109,597 (60 ms) | 145,945 (80 ms) |
| DOP853-adaptive [cap=86400 s] | 81,841 (57 ms) | 109,597 (66 ms) | 145,945 (80 ms) |
| PEFRL | 432,944 (99 ms) | 795,272 (178 ms) | not reached (best 3.26E+000) |
| Y6 | 224,995 (61 ms) | 336,645 (91 ms) | not reached (best 1.69E+000) |
| Y6C | 224,995 (89 ms) | 336,645 (132 ms) | 505,387 (198 ms) |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | not reached (best 1.19E+005) | not reached (best 1.19E+005) | not reached (best 1.19E+005) |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 3 run(s) did not complete the horizon: aborted at t=1.016E+007 s: a pair distance collapsed to 8.49E+003 m (initial minimum 8.50E+007 m): the trajectory left the physical domain; worst error at completed checkpoints 1.39E+008 m; excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=86400 s]: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive [cap=86400 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=86400 s]: CAP-BOUND in 11 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- PEFRL: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Y6: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Tight binary, 10 y

Reference floor: 9.13E-002 m (independent dd Taylor references (43200 s and 21600 s); floor = worst pair difference). Kepler-solve weight: 1.00 RHS. RHS multiplier: 1. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | not reached (best 2.09E+002) | not reached (best 2.09E+002) | not reached (best 2.09E+002) |
| DOPRI5-fixed | 1.14E+006 (127 ms) | not reached (best 2.63E+001) | not reached (best 2.63E+001) |
| DOPRI5-adaptive | 989,779 (140 ms) | 1.57E+006 (222 ms) | 1.97E+006 (281 ms) |
| DOPRI5-adaptive [cap=86400 s] | 989,779 (139 ms) | 1.57E+006 (221 ms) | 1.97E+006 (282 ms) |
| DOP853-adaptive | 132,421 (22 ms) | 176,257 (30 ms) | not reached (best 1.75E+000) |
| DOP853-adaptive [cap=86400 s] | 132,421 (22 ms) | 176,257 (30 ms) | not reached (best 1.75E+000) |
| PEFRL | 1.26E+006 (75 ms) | not reached (best 4.98E+001) | not reached (best 4.98E+001) |
| Y6 | 483,225 (36 ms) | 654,529 (47 ms) | not reached (best 1.43E+000) |
| Y6C | 483,225 (57 ms) | 654,529 (74 ms) | 981,793 (111 ms) |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | not reached (best 3.83E+006) | not reached (best 3.83E+006) | not reached (best 3.83E+006) |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 3 run(s) did not complete the horizon: aborted at t=2.837E+007 s: a pair distance collapsed to 1.98E+006 m (initial minimum 2.00E+010 m): the trajectory left the physical domain; worst error at completed checkpoints 4.51E+010 m; excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=86400 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=86400 s]: CAP-BOUND in 14 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Sun + eccentric comet + Jupiter, 10 y

Reference floor: 7.36E-002 m (independent dd Taylor references (43200 s and 21600 s); floor = worst pair difference). Kepler-solve weight: 1.00 RHS. RHS multiplier: 1. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 1.30E+006 (108 ms) | not reached (best 2.25E+001) | not reached (best 2.25E+001) |
| DOPRI5-fixed | 522,313 (78 ms) | 783,829 (116 ms) | not reached (best 1.95E+000) |
| DOPRI5-adaptive | 82,117 (16 ms) | 129,967 (25 ms) | not reached (best 1.08E+000) |
| DOPRI5-adaptive [cap=86400 s] | 82,243 (16 ms) | 129,967 (25 ms) | not reached (best 1.08E+000) |
| DOP853-adaptive | 15,426 (4 ms) | 19,961 (5 ms) | 22,909 (6 ms) |
| DOP853-adaptive [cap=86400 s] | 45,780 (11 ms) | 47,377 (11 ms) | 49,933 (12 ms) |
| PEFRL | 1.06E+006 (87 ms) | not reached (best 1.72E+001) | not reached (best 1.72E+001) |
| Y6 | 332,375 (34 ms) | 497,715 (51 ms) | not reached (best 4.29E+000) |
| Y6C | 332,375 (55 ms) | 497,715 (83 ms) | below round-off floor (1.17E+000 m) |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | not reached (best 1.02E+007) | not reached (best 1.02E+007) | not reached (best 1.02E+007) |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive [cap=86400 s]: CAP-BOUND in 16 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=86400 s]: CAP-BOUND in 26 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- double-precision round-off estimate 1.17E+000 m (16 eps * scale * sqrt(steps=195,896 of the most accurate run); each cell uses its own run's step count, and a run whose error is within that noise is reported as 'below round-off floor', not as a ranking result (1 cell(s) excluded).

## Heliocentric Sun-Jupiter probe, 10 y

Reference floor: 6.88E-004 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 1. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 104,544 (50 ms) | 173,756 (84 ms) | 318,956 (156 ms) |
| DOPRI5-fixed | 51,547 (26 ms) | 77,683 (38 ms) | 128,503 (63 ms) |
| DOPRI5-adaptive | 32,905 (17 ms) | 51,907 (27 ms) | 82,087 (48 ms) |
| DOPRI5-adaptive [cap=86400 s] | 32,905 (17 ms) | 51,907 (27 ms) | 82,087 (43 ms) |
| DOP853-adaptive | 7,567 (4 ms) | 9,540 (5 ms) | 10,945 (6 ms) |
| DOP853-adaptive [cap=86400 s] | <=43,573 EDGE | <=43,573 EDGE | <=43,573 EDGE |
| PEFRL | 85,668 (42 ms) | 156,816 (78 ms) | 529,012 (262 ms) |
| Y6 | 33,034 (17 ms) | 49,127 (25 ms) | 73,690 (37 ms) |
| Y6C | 33,034 (17 ms) | 49,127 (25 ms) | 73,690 (38 ms) |
| WH-Y6 | 5,083 (3 ms) | 6,777 (5 ms) | 10,165 (7 ms) |
| Leapfrog | not reached (best 4.63E+004) | not reached (best 4.63E+004) | not reached (best 4.63E+004) |

Findings:
- DOPRI5-adaptive [cap=86400 s]: CAP-BOUND in 18 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=86400 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- PEFRL: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- TIE at 1 m: DOP853-adaptive (10,945), WH-Y6 (10,165) are within 10%; do not read the ranking as resolved.
