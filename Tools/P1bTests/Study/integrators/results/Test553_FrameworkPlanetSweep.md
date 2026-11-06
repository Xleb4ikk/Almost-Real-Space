# Integrator comparison

Cost = RHS-equivalent calls of the cheapest actually executed run whose measured error is at or below the target (no interpolation, never extrapolated).
`EDGE` = even the cheapest tested point was already below the target (cost is an upper bound, not a result). `~` marks methods within 10% of the winner: that ranking is not resolved. `not reached` = the finest run (or the RHS budget) stayed above the target; the parenthesised value is the best error it did reach. `n/a` = method not valid for this problem.
Build: Release. Per-run RHS budget: per scenario (see section headers).

## Who wins where

| scenario | 100 m | 10 m | 1 m |
|---|---|---|---|
| Real solar 7-body, 10 y | DOP853-adaptive [cap=86400 s] (58,449) | DOP853-adaptive (77,392) | Y6C (338,346) |
| Real solar 7-body, 100 y | DOP853-adaptive [cap=86400 s] (1.03E+006) | Y6C (3.38E+006) | - |
| Custom 6-body, 10 y | DOP853-adaptive (96,066) | DOP853-adaptive (127,823) | DOP853-adaptive (170,173) |
| Tight binary, 10 y | DOP853-adaptive (141,745) | DOP853-adaptive (188,857) | Y6C (981,793) |
| Sun + eccentric comet + Jupiter, 10 y | DOP853-adaptive (15,806) | DOP853-adaptive (20,421) | DOP853-adaptive (23,430) |
| Heliocentric Sun-Jupiter probe, 10 y | WH-Y6 (5,083) | WH-Y6 (6,777) | WH-Y6 (10,165) ~ DOP853-adaptive (10,945) [tie within 10%] |

## Real solar 7-body, 10 y

Reference floor: 5.88E-002 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 1. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 532,780 (78 ms) | 884,392 (121 ms) | not reached (best 2.02E+000) |
| DOPRI5-fixed | 355,027 (74 ms) | 532,537 (112 ms) | not reached (best 2.68E+000) |
| DOPRI5-adaptive | 358,447 (98 ms) | 567,925 (150 ms) | not reached (best 1.37E+000) |
| DOPRI5-adaptive [cap=86400 s] | 358,447 (91 ms) | 567,925 (144 ms) | not reached (best 1.37E+000) |
| DOP853-adaptive | 58,672 (18 ms) | 77,392 (24 ms) | not reached (best 1.11E+000) |
| DOP853-adaptive [cap=86400 s] | 58,449 (18 ms) | 77,392 (24 ms) | not reached (best 1.11E+000) |
| PEFRL | 320,936 (42 ms) | 722,220 (95 ms) | not reached (best 3.09E+000) |
| Y6 | 167,042 (26 ms) | 507,942 (85 ms) | not reached (best 5.53E+000) |
| Y6C | 167,042 (43 ms) | 249,712 (64 ms) | 338,346 (88 ms) |
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
- PEFRL: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Y6: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- double-precision round-off estimate 6.34E+000 m (16 eps * scale * sqrt(max steps=1,678,324)); the tightest target 1.00E+000 m is at that scale, so that column measures summation quality, not method order.

## Real solar 7-body, 100 y

Reference floor: 1.71E+000 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 1. RHS budget: 5,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | not reached (best 3.39E+004) | not reached (best 3.39E+004) | skip |
| DOPRI5-fixed | not reached (best 5.36E+003) | not reached (best 5.36E+003) | skip |
| DOPRI5-adaptive | not reached | not reached | skip |
| DOPRI5-adaptive [cap=86400 s] | not reached (best 2.83E+003) | not reached (best 2.83E+003) | skip |
| DOP853-adaptive | not reached (best 2.98E+011) | not reached (best 2.98E+011) | skip |
| DOP853-adaptive [cap=86400 s] | 1.03E+006 (344 ms) | not reached (best 3.73E+001) | skip |
| PEFRL | not reached (best 1.59E+002) | not reached (best 1.59E+002) | skip |
| Y6 | 4.59E+006 (745 ms) | not reached (best 9.41E+001) | skip |
| Y6C | 2.50E+006 (611 ms) | 3.38E+006 (838 ms) | skip |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | not reached (best 4.52E+006) | not reached (best 4.52E+006) | skip |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 7 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=86400 s]: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive [cap=86400 s]: CAP-BOUND in 11 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOP853-adaptive [cap=86400 s]: CAP-BOUND in 20 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: observed order 4.97 vs expected 6 (14 pairs): check the implementation or the regime.
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: the uncapped branch produced no finite samples while DOPRI5-adaptive [cap=86400 s] reached 2.83E+003 m; the step cap is load-bearing on this horizon.
- DOP853-adaptive: the uncapped branch reached only 2.98E+011 m while DOP853-adaptive [cap=86400 s] reached 3.73E+001 m; the step cap is load-bearing on this horizon.
- double-precision round-off estimate 1.09E+001 m (16 eps * scale * sqrt(max steps=4,973,488)); the tightest target 1.00E+001 m is at that scale, so that column measures summation quality, not method order.

## Custom 6-body, 10 y

Reference floor: 3.59E-002 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 1. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 795,272 (87 ms) | 1.32E+006 (154 ms) | not reached (best 4.22E+000) |
| DOPRI5-fixed | 530,347 (92 ms) | 880,255 (152 ms) | not reached (best 2.53E+000) |
| DOPRI5-adaptive | not reached | not reached | not reached |
| DOPRI5-adaptive [cap=86400 s] | 557,119 (120 ms) | 882,925 (190 ms) | 1.11E+006 (240 ms) |
| DOP853-adaptive | 96,066 (25 ms) | 127,823 (33 ms) | 170,173 (44 ms) |
| DOP853-adaptive [cap=86400 s] | 96,066 (25 ms) | 127,823 (33 ms) | 170,173 (44 ms) |
| PEFRL | 432,944 (46 ms) | 795,272 (85 ms) | not reached (best 3.26E+000) |
| Y6 | 224,995 (29 ms) | 336,645 (43 ms) | not reached (best 1.69E+000) |
| Y6C | 224,995 (46 ms) | 336,645 (69 ms) | 505,387 (103 ms) |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | not reached (best 1.19E+005) | not reached (best 1.19E+005) | not reached (best 1.19E+005) |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 6 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=86400 s]: CAP-BOUND in 10 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=86400 s]: CAP-BOUND in 17 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- PEFRL: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Y6: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: the uncapped branch produced no finite samples while DOPRI5-adaptive [cap=86400 s] reached 8.33E-001 m; the step cap is load-bearing on this horizon.
- double-precision round-off estimate 1.39E+000 m (16 eps * scale * sqrt(max steps=1,363,478)); the tightest target 1.00E+000 m is at that scale, so that column measures summation quality, not method order.

## Tight binary, 10 y

Reference floor: 9.13E-002 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 1. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | not reached (best 2.09E+002) | not reached (best 2.09E+002) | not reached (best 2.09E+002) |
| DOPRI5-fixed | 1.14E+006 (58 ms) | not reached (best 2.63E+001) | not reached (best 2.63E+001) |
| DOPRI5-adaptive | 894,631 (61 ms) | 1.42E+006 (97 ms) | not reached (best 2.55E+000) |
| DOPRI5-adaptive [cap=86400 s] | 894,631 (61 ms) | 1.42E+006 (97 ms) | not reached (best 2.55E+000) |
| DOP853-adaptive | 141,745 (12 ms) | 188,857 (16 ms) | not reached (best 1.07E+000) |
| DOP853-adaptive [cap=86400 s] | 141,745 (12 ms) | 188,857 (16 ms) | not reached (best 1.07E+000) |
| PEFRL | 1.26E+006 (38 ms) | not reached (best 4.98E+001) | not reached (best 4.98E+001) |
| Y6 | 483,225 (17 ms) | 654,529 (22 ms) | not reached (best 1.43E+000) |
| Y6C | 483,225 (27 ms) | 654,529 (37 ms) | 981,793 (55 ms) |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | not reached (best 3.83E+006) | not reached (best 3.83E+006) | not reached (best 3.83E+006) |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 2 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=86400 s]: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive [cap=86400 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=86400 s]: CAP-BOUND in 13 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Sun + eccentric comet + Jupiter, 10 y

Reference floor: 7.36E-002 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 1. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 1.30E+006 (51 ms) | not reached (best 2.25E+001) | not reached (best 2.25E+001) |
| DOPRI5-fixed | 522,313 (40 ms) | 783,829 (61 ms) | not reached (best 1.95E+000) |
| DOPRI5-adaptive | 71,149 (7 ms) | 112,567 (11 ms) | 224,281 (21 ms) |
| DOPRI5-adaptive [cap=86400 s] | 71,149 (7 ms) | 112,567 (11 ms) | 224,281 (21 ms) |
| DOP853-adaptive | 15,806 (2 ms) | 20,421 (2 ms) | 23,430 (3 ms) |
| DOP853-adaptive [cap=86400 s] | 46,405 (5 ms) | 48,445 (6 ms) | 49,849 (6 ms) |
| PEFRL | 1.06E+006 (41 ms) | not reached (best 1.72E+001) | not reached (best 1.72E+001) |
| Y6 | 332,375 (16 ms) | 497,715 (24 ms) | not reached (best 4.29E+000) |
| Y6C | 332,375 (28 ms) | 497,715 (42 ms) | 746,572 (63 ms) |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | not reached (best 1.02E+007) | not reached (best 1.02E+007) | not reached (best 1.02E+007) |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive [cap=86400 s]: CAP-BOUND in 17 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=86400 s]: CAP-BOUND in 27 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- double-precision round-off estimate 3.22E+000 m (16 eps * scale * sqrt(max steps=1,487,176)); the tightest target 1.00E+000 m is at that scale, so that column measures summation quality, not method order.

## Heliocentric Sun-Jupiter probe, 10 y

Reference floor: 6.88E-004 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 1. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 104,544 (23 ms) | 173,756 (39 ms) | 318,956 (71 ms) |
| DOPRI5-fixed | 51,547 (12 ms) | 77,683 (18 ms) | 128,503 (29 ms) |
| DOPRI5-adaptive | 32,905 (8 ms) | 51,907 (12 ms) | 82,087 (20 ms) |
| DOPRI5-adaptive [cap=86400 s] | 32,905 (8 ms) | 51,907 (13 ms) | 82,087 (20 ms) |
| DOP853-adaptive | 7,567 (2 ms) | 9,540 (3 ms) | 10,945 (3 ms) |
| DOP853-adaptive [cap=86400 s] | <=43,573 EDGE | <=43,573 EDGE | <=43,573 EDGE |
| PEFRL | 85,668 (19 ms) | 156,816 (37 ms) | 431,728 (98 ms) |
| Y6 | 33,034 (8 ms) | 49,127 (11 ms) | 73,690 (17 ms) |
| Y6C | 33,034 (8 ms) | 49,127 (12 ms) | 73,690 (18 ms) |
| WH-Y6 | 5,083 (2 ms) | 6,777 (2 ms) | 10,165 (3 ms) |
| Leapfrog | not reached (best 4.63E+004) | not reached (best 4.63E+004) | not reached (best 4.63E+004) |

Findings:
- DOPRI5-adaptive [cap=86400 s]: CAP-BOUND in 18 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=86400 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- PEFRL: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- TIE at 1 m: DOP853-adaptive (10,945), WH-Y6 (10,165) are within 10%; do not read the ranking as resolved.
- double-precision round-off estimate 6.89E-001 m (16 eps * scale * sqrt(max steps=1,003,816)); the tightest target 1.00E+000 m is at that scale, so that column measures summation quality, not method order.
