# Integrator comparison

Cost = RHS-equivalent calls, log-log interpolated between the nearest cheaper above-target run and the first bracketing below-target run; never extrapolated. `*` marks interpolated cells; no star means the first actually executed point that passed the target (used when the bracket is wider than 1.5x in cost or its local order misses the declared order by more than 30%). `ms` is the wall-clock time interpolated on the same bracket; sub-millisecond values are timing noise, use RHS for the deterministic ranking.
`EDGE` = even the cheapest tested point was already below the target (cost is an upper bound). A cap-limited EDGE is exact; a non-cap EDGE is marked **unresolved**. `~` marks methods within 10% of the winner (interpolated cells) or one grid step (unbracketed cells): that ranking is not resolved. `not reached` = the finest run (or the RHS budget) stayed above the target. `below round-off floor` = the target is at or below the run's own accumulation noise. `n/a` = method not valid for this problem. Scenarios whose reference is not certified are listed in the detail but excluded from the summary.
Reference-floor rule: a target below 10 x the scenario's reference floor is skipped.
Build: Release. Per-run RHS budget: per scenario (see section headers).

## Who wins where - RHS-equivalent calls

| scenario | 100 m | 10 m | 1 m |
|---|---|---|---|
| Real solar 7-body, 10 y | GJ8 (18,764*) | RKN12(10) (24,836*) ~ GJ8 (25,158) [tie within 10%] | RKN12(10) (29,096*) |
| Real solar 7-body, 100 y | GJ8 (285,002*) ~ RKN12(10) (288,995*) [tie within 10%] | - | - |
| Custom 6-body, 10 y | GJ8 (28,830*) | GJ8 (37,111*) ~ RKN12(10) (37,786*) [tie within 10%] | RKN12(10) (50,324*) ~ GJ8 (54,934*) [tie within 10%] |
| Tight binary, 10 y | RKN12(10) (44,012*) | RKN12(10) (51,403*) | RKN12(10) [cap=86400 s] (<= 62,153) EDGE |
| Sun + eccentric comet + Jupiter, 10 y | RKN12(10) (6,360*) | RKN12(10) (8,349*) | RKN12(10) (9,489*) |
| Heliocentric Sun-Jupiter probe, 10 y | RKN12(10) (2,990*) **unresolved** | RKN12(10) (3,445*) ~ WH-Y6 (6,777) ~ GJ8 (6,853*) [ties within 100%] | RKN12(10) (4,529*) ~ WH-BM14A (7,018) ~ GJ8 (8,602*) [ties within 100%] |

## Who wins where - wall-clock ms

| scenario | 100 m | 10 m | 1 m |
|---|---|---|---|
| Real solar 7-body, 10 y | RKN12(10) (5.2 ms*) | RKN12(10) (6.2 ms*) | RKN12(10) (7.0 ms*) |
| Real solar 7-body, 100 y | RKN12(10) (70.3 ms*) | - | - |
| Custom 6-body, 10 y | RKN12(10) (6.2 ms*) | RKN12(10) (7.5 ms*) | RKN12(10) (9.8 ms*) |
| Tight binary, 10 y | RKN12(10) (2.6 ms*) | RKN12(10) (3.2 ms*) | RKN12(10) [cap=86400 s] (<= 3.7 ms) EDGE |
| Sun + eccentric comet + Jupiter, 10 y | RKN12(10) (0.518 ms*) | RKN12(10) (0.679 ms*) | RKN12(10) (0.774 ms*) |
| Heliocentric Sun-Jupiter probe, 10 y | RKN12(10) (0.694 ms*) | RKN12(10) (0.801 ms*) | RKN12(10) (1.1 ms*) ~ WH-BM14A (2.0 ms) [tie within 100%] |

## Real solar 7-body, 10 y

Reference floor: 5.88E-002 m (independent dd Taylor references (43200 s and 21600 s); floor = worst pair difference). Kepler-solve weight: 1.00 RHS. RHS multiplier: 1. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 509,431* (71.7 ms) | 884,392 (127.0 ms) | not reached (best 2.02E+000) |
| DOPRI5-fixed | 344,157* (75.7 ms) | 532,537 (117.0 ms) | not reached (best 2.68E+000) |
| DOPRI5-adaptive | 334,163* (88.1 ms) | 557,491* (147.3 ms) | below round-off floor (2.05E+000 m) |
| DOPRI5-adaptive [cap=86400 s] | 334,163* (88.5 ms) | 557,491* (147.1 ms) | below round-off floor (2.05E+000 m) |
| DOP853-adaptive | 57,162* (18.6 ms) | 73,812* (24.0 ms) | 190,134* (62.1 ms) |
| DOP853-adaptive [cap=86400 s] | 57,162* (18.0 ms) | 73,812* (23.2 ms) | 190,134* (60.1 ms) |
| PEFRL | 298,001* (40.3 ms) | 722,220 (97.9 ms) | not reached (best 3.09E+000) |
| Y6 | 157,385* (25.2 ms) | 507,942 (81.5 ms) | not reached (best 5.53E+000) |
| Y6C | 157,042* (37.7 ms) | 230,560* (57.2 ms) | below round-off floor (1.08E+000 m) |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | not reached (best 3.36E+004) | not reached (best 3.36E+004) | not reached (best 3.36E+004) |
| RKN12(10) | 20,895* (5.0 ms) | 24,836* (6.0 ms) | 29,096* (7.0 ms) |
| RKN12(10) [cap=86400 s] | <=62,153 EDGE | <=62,153 EDGE | <=62,153 EDGE |
| IAS15 | 52,732* (37.5 ms) | 54,164* (38.5 ms) | 55,636* (39.6 ms) |
| IAS15 [cap=86400 s] | below round-off floor (2.96E-001 m) | below round-off floor (2.96E-001 m) | below round-off floor (2.96E-001 m) |
| SRKN14A | 61,520* (8.3 ms) | 90,342 (12.1 ms) | not reached (best 2.15E+000) |
| WH-BM14A | n/a | n/a | n/a |
| GJ8 | 18,764* (22.2 ms) | 25,158 (20.4 ms) | 43,066 (37.0 ms) |
| Encke-IAS15 | n/a | n/a | n/a |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- DOPRI5-adaptive: 3 run(s) did not complete the horizon: aborted at t=1.805E+007 s: a pair distance collapsed to 3.74E+004 m (initial minimum 4.00E+008 m): the trajectory left the physical domain; worst error at completed checkpoints 6.18E+008 m; excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=86400 s]: CAP-BOUND in 8 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=86400 s]: CAP-BOUND in 27 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- PEFRL: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- PEFRL: error is non-monotonic at coarse h in 1 place(s) but recovers at finer h (h=2.14E+03 -> 1.94E+03 s, recovers by h=1.75E+03 s); that coarse branch is pre-asymptotic, not a round-off floor.
- Y6: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- RKN12(10) [cap=86400 s]: CAP-BOUND in 27 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=86400 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- SRKN14A: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- SRKN14A: error GROWS as h shrinks in 3 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- GJ8: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- GJ8: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- GJ8: error is non-monotonic at coarse h in 1 place(s) but recovers at finer h (h=4.39E+03 -> 2.93E+03 s, recovers by h=1.95E+03 s); that coarse branch is pre-asymptotic, not a round-off floor.
- GJ8: observed order 8.87 vs expected 10 (6 pairs): check the implementation or the regime.
- TIE at 10 m: RKN12(10) (24,836), GJ8 (25,158) are within 10%; do not read the ranking as resolved.
- double-precision round-off estimate 2.96E-001 m (16 eps * scale * sqrt(steps=3,656 of the most accurate run); each cell uses its own run's step count, and a run whose error is within that noise is reported as 'below round-off floor', not as a ranking result (6 cell(s) excluded).

## Real solar 7-body, 100 y

Reference floor: 1.71E+000 m (independent dd Taylor references (43200 s and 21600 s); floor = worst pair difference). Kepler-solve weight: 1.00 RHS. RHS multiplier: 1. RHS budget: 5,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | not reached (best 3.39E+004) | skip | skip |
| DOPRI5-fixed | not reached (best 5.36E+003) | skip | skip |
| DOPRI5-adaptive | not reached | skip | skip |
| DOPRI5-adaptive [cap=86400 s] | not reached (best 3.07E+003) | skip | skip |
| DOP853-adaptive | 1.03E+006* (325.1 ms) | skip | skip |
| DOP853-adaptive [cap=86400 s] | 1.03E+006* (366.2 ms) | skip | skip |
| PEFRL | not reached (best 1.59E+002) | skip | skip |
| Y6 | 4.59E+006 (754.8 ms) | skip | skip |
| Y6C | 2.35E+006* (557.8 ms) | skip | skip |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | not reached (best 4.52E+006) | skip | skip |
| RKN12(10) | 288,995* (70.3 ms) | skip | skip |
| RKN12(10) [cap=86400 s] | <=620,977 EDGE | skip | skip |
| IAS15 | 554,899* (418.3 ms) | skip | skip |
| IAS15 [cap=86400 s] | <=1.62E+006 EDGE | skip | skip |
| SRKN14A | 1.21E+006 (161.7 ms) | skip | skip |
| WH-BM14A | n/a | n/a | n/a |
| GJ8 | 285,002* (238.4 ms) | skip | skip |
| Encke-IAS15 | n/a | n/a | n/a |

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
- RKN12(10) [cap=86400 s]: CAP-BOUND in 23 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=86400 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- SRKN14A: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- SRKN14A: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- SRKN14A: observed order 4.94 vs expected 6 (10 pairs): check the implementation or the regime.
- GJ8: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- GJ8: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- DOPRI5-adaptive: the uncapped branch produced no finite samples while DOPRI5-adaptive [cap=86400 s] reached 3.07E+003 m; the step cap is load-bearing on this horizon.
- TIE at 100 m: RKN12(10) (288,995), GJ8 (285,002) are within 10%; do not read the ranking as resolved.

## Custom 6-body, 10 y

Reference floor: 3.59E-002 m (independent dd Taylor references (43200 s and 21600 s); floor = worst pair difference). Kepler-solve weight: 1.00 RHS. RHS multiplier: 1. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 794,647* (81.5 ms) | 1.32E+006 (135.4 ms) | not reached (best 4.22E+000) |
| DOPRI5-fixed | 527,289* (91.1 ms) | 880,255 (152.6 ms) | not reached (best 2.53E+000) |
| DOPRI5-adaptive | 526,196* (111.5 ms) | 804,187* (170.8 ms) | not reached (best 1.42E+000) |
| DOPRI5-adaptive [cap=86400 s] | 526,196* (111.5 ms) | 804,187* (170.8 ms) | not reached (best 1.42E+000) |
| DOP853-adaptive | 81,575* (21.4 ms) | 105,560* (27.5 ms) | 138,369* (36.3 ms) |
| DOP853-adaptive [cap=86400 s] | 81,575* (21.4 ms) | 105,560* (27.8 ms) | 138,369* (36.3 ms) |
| PEFRL | 432,944 (46.1 ms) | 795,272 (84.8 ms) | not reached (best 3.26E+000) |
| Y6 | 223,697* (27.9 ms) | 336,645 (41.0 ms) | not reached (best 1.69E+000) |
| Y6C | 221,849* (45.0 ms) | 325,383* (66.2 ms) | 505,387 (103.2 ms) |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | not reached (best 1.19E+005) | not reached (best 1.19E+005) | not reached (best 1.19E+005) |
| RKN12(10) | 32,321* (6.2 ms) | 37,786* (7.5 ms) | 50,324* (9.8 ms) |
| RKN12(10) [cap=86400 s] | <=62,153 EDGE | <=62,153 EDGE | 62,153* (12.1 ms) |
| IAS15 | 82,239* (49.1 ms) | 84,439* (50.4 ms) | 86,697* (51.8 ms) |
| IAS15 [cap=86400 s] | below round-off floor (7.21E-002 m) | below round-off floor (7.21E-002 m) | below round-off floor (7.21E-002 m) |
| SRKN14A | 87,098* (9.1 ms) | 132,958 (13.7 ms) | not reached (best 1.95E+000) |
| WH-BM14A | n/a | n/a | n/a |
| GJ8 | 28,830* (21.3 ms) | 37,111* (27.4 ms) | 54,934* (40.8 ms) |
| Encke-IAS15 | n/a | n/a | n/a |

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
- RKN12(10) [cap=86400 s]: CAP-BOUND in 26 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=86400 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- SRKN14A: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- SRKN14A: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- SRKN14A: error is non-monotonic at coarse h in 1 place(s) but recovers at finer h (h=3E+04 -> 2E+04 s, recovers by h=1.33E+04 s); that coarse branch is pre-asymptotic, not a round-off floor.
- GJ8: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- GJ8: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- GJ8: observed order 7.59 vs expected 10 (9 pairs): check the implementation or the regime.
- TIE at 10 m: RKN12(10) (37,786), GJ8 (37,111) are within 10%; do not read the ranking as resolved.
- TIE at 1 m: RKN12(10) (50,324), GJ8 (54,934) are within 10%; do not read the ranking as resolved.
- double-precision round-off estimate 7.21E-002 m (16 eps * scale * sqrt(steps=3,656 of the most accurate run); each cell uses its own run's step count, and a run whose error is within that noise is reported as 'below round-off floor', not as a ranking result (3 cell(s) excluded).

## Tight binary, 10 y

Reference floor: 9.13E-002 m (independent dd Taylor references (43200 s and 21600 s); floor = worst pair difference). Kepler-solve weight: 1.00 RHS. RHS multiplier: 1. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | not reached (best 2.09E+002) | not reached (best 2.09E+002) | not reached (best 2.09E+002) |
| DOPRI5-fixed | 1.07E+006* (50.8 ms) | not reached (best 2.63E+001) | not reached (best 2.63E+001) |
| DOPRI5-adaptive | 853,159* (57.7 ms) | 1.40E+006* (94.5 ms) | 1.90E+006* (128.6 ms) |
| DOPRI5-adaptive [cap=86400 s] | 853,159* (57.6 ms) | 1.40E+006* (94.5 ms) | 1.90E+006* (128.6 ms) |
| DOP853-adaptive | 126,514* (10.3 ms) | 165,043* (13.4 ms) | not reached (best 1.75E+000) |
| DOP853-adaptive [cap=86400 s] | 126,514* (10.3 ms) | 165,043* (13.4 ms) | not reached (best 1.75E+000) |
| PEFRL | 1.18E+006* (33.6 ms) | not reached (best 4.98E+001) | not reached (best 4.98E+001) |
| Y6 | 442,038* (15.0 ms) | 631,596* (21.5 ms) | not reached (best 1.43E+000) |
| Y6C | 440,701* (24.9 ms) | 646,691* (36.7 ms) | 947,301* (53.7 ms) |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | not reached (best 3.83E+006) | not reached (best 3.83E+006) | not reached (best 3.83E+006) |
| RKN12(10) | 44,012* (2.6 ms) | 51,403* (3.2 ms) | not reached (best 1.55E+000) |
| RKN12(10) [cap=86400 s] | <=62,153 EDGE | <=62,153 EDGE | <=62,153 EDGE |
| IAS15 | 74,789* (14.4 ms) | 78,832* (15.3 ms) | 83,365* (16.4 ms) |
| IAS15 [cap=86400 s] | <=168,898 EDGE | <=168,898 EDGE | <=168,898 EDGE |
| SRKN14A | 157,936* (4.3 ms) | 230,209* (6.3 ms) | 388,626 (10.5 ms) |
| WH-BM14A | n/a | n/a | n/a |
| GJ8 | 58,006* (14.8 ms) | 74,046* (19.0 ms) | 91,822 (23.5 ms) |
| Encke-IAS15 | n/a | n/a | n/a |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 3 run(s) did not complete the horizon: aborted at t=2.837E+007 s: a pair distance collapsed to 1.98E+006 m (initial minimum 2.00E+010 m): the trajectory left the physical domain; worst error at completed checkpoints 4.51E+010 m; excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=86400 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=86400 s]: CAP-BOUND in 14 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- RKN12(10) [cap=86400 s]: CAP-BOUND in 33 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=86400 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- SRKN14A: error is non-monotonic at coarse h in 1 place(s) but recovers at finer h (h=6.86E+03 -> 4.58E+03 s, recovers by h=3.05E+03 s); that coarse branch is pre-asymptotic, not a round-off floor.
- GJ8: observed order 8.89 vs expected 10 (7 pairs): check the implementation or the regime.

## Sun + eccentric comet + Jupiter, 10 y

Reference floor: 7.36E-002 m (independent dd Taylor references (43200 s and 21600 s); floor = worst pair difference). Kepler-solve weight: 1.00 RHS. RHS multiplier: 1. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 1.19E+006* (44.0 ms) | not reached (best 2.25E+001) | not reached (best 2.25E+001) |
| DOPRI5-fixed | 487,059* (32.8 ms) | 783,829 (53.2 ms) | not reached (best 1.95E+000) |
| DOPRI5-adaptive | 74,942* (7.0 ms) | 119,079* (11.1 ms) | not reached (best 1.08E+000) |
| DOPRI5-adaptive [cap=86400 s] | 75,528* (7.0 ms) | 119,079* (11.1 ms) | not reached (best 1.08E+000) |
| DOP853-adaptive | 14,628* (1.6 ms) | 18,179* (2.0 ms) | 22,671* (2.5 ms) |
| DOP853-adaptive [cap=86400 s] | 45,677* (5.3 ms) | 47,239* (5.5 ms) | 49,602* (5.7 ms) |
| PEFRL | 1.05E+006* (41.2 ms) | not reached (best 1.72E+001) | not reached (best 1.72E+001) |
| Y6 | 327,944* (16.1 ms) | 485,111* (23.8 ms) | not reached (best 4.29E+000) |
| Y6C | 326,299* (27.2 ms) | 479,610* (39.9 ms) | below round-off floor (8.62E-001 m) |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | not reached (best 1.02E+007) | not reached (best 1.02E+007) | not reached (best 1.02E+007) |
| RKN12(10) | 6,360* (0.518 ms) | 8,349* (0.679 ms) | 9,489* (0.774 ms) |
| RKN12(10) [cap=86400 s] | <=62,136 EDGE | <=62,136 EDGE | below round-off floor (1.60E-001 m) |
| IAS15 | 17,210* (4.9 ms) | 17,711* (5.1 ms) | 21,890* (6.4 ms) |
| IAS15 [cap=86400 s] | below round-off floor (1.60E-001 m) | below round-off floor (1.60E-001 m) | below round-off floor (1.60E-001 m) |
| SRKN14A | 133,042* (4.8 ms) | 197,722 (7.1 ms) | below round-off floor (4.24E-001 m) |
| WH-BM14A | n/a | n/a | n/a |
| GJ8 | 59,980* (21.1 ms) | 77,333* (27.3 ms) | 127,748 (45.0 ms) |
| Encke-IAS15 | n/a | n/a | n/a |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive [cap=86400 s]: CAP-BOUND in 16 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=86400 s]: CAP-BOUND in 26 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Y6: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- RKN12(10) [cap=86400 s]: CAP-BOUND in 28 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=86400 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- GJ8: error is non-monotonic at coarse h in 1 place(s) but recovers at finer h (h=5.48E+03 -> 4.96E+03 s, recovers by h=3.3E+03 s); that coarse branch is pre-asymptotic, not a round-off floor.
- GJ8: observed order 8.92 vs expected 10 (8 pairs): check the implementation or the regime.
- double-precision round-off estimate 1.60E-001 m (16 eps * scale * sqrt(steps=3,656 of the most accurate run); each cell uses its own run's step count, and a run whose error is within that noise is reported as 'below round-off floor', not as a ranking result (6 cell(s) excluded).

## Heliocentric Sun-Jupiter probe, 10 y

Reference floor: 6.88E-004 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 1. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 96,977* (21.8 ms) | 170,784* (38.4 ms) | 297,590* (63.8 ms) |
| DOPRI5-fixed | 47,559* (10.4 ms) | 75,493* (16.6 ms) | 128,503 (28.2 ms) |
| DOPRI5-adaptive | 31,459* (7.3 ms) | 49,881* (11.5 ms) | 76,840* (17.7 ms) |
| DOPRI5-adaptive [cap=86400 s] | 31,751* (7.3 ms) | 49,881* (11.6 ms) | 76,840* (17.7 ms) |
| DOP853-adaptive | 7,200* (1.7 ms) | 8,766* (2.1 ms) | 10,678* (2.5 ms) |
| DOP853-adaptive [cap=86400 s] | <=43,573 EDGE | <=43,573 EDGE | <=43,573 EDGE |
| PEFRL | 81,058* (17.6 ms) | 143,820* (31.2 ms) | 529,012 (114.9 ms) |
| Y6 | 32,854* (7.3 ms) | 48,132* (10.6 ms) | 73,690 (16.3 ms) |
| Y6C | 32,840* (7.5 ms) | 48,197* (11.0 ms) | 70,802* (16.1 ms) |
| WH-Y6 | 5,083 (1.5 ms) | 6,777 (2.0 ms) | 10,165 (3.0 ms) |
| Leapfrog | not reached (best 4.63E+004) | not reached (best 4.63E+004) | not reached (best 4.63E+004) |
| RKN12(10) | 2,990* (0.694 ms) | 3,445* (0.801 ms) | 4,529* (1.1 ms) |
| RKN12(10) [cap=86400 s] | <=61,728 EDGE | <=61,728 EDGE | <=61,728 EDGE |
| IAS15 | <=6,974 EDGE | 12,463 (3.9 ms) | 12,463 (3.9 ms) |
| IAS15 [cap=86400 s] | below round-off floor (4.14E-002 m) | below round-off floor (4.14E-002 m) | below round-off floor (4.14E-002 m) |
| SRKN14A | 10,910* (2.4 ms) | 16,029* (3.5 ms) | 24,509* (5.3 ms) |
| WH-BM14A | <=3,509 EDGE | 7,018 (2.0 ms) | 7,018 (2.0 ms) |
| GJ8 | 5,383* (1.8 ms) | 6,853* (2.3 ms) | 8,602* (2.9 ms) |
| Encke-IAS15 | <=57,248 EDGE | <=57,248 EDGE | <=57,248 EDGE |

Findings:
- DOPRI5-adaptive [cap=86400 s]: CAP-BOUND in 18 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=86400 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- PEFRL: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- RKN12(10) [cap=86400 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=86400 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- double-precision round-off estimate 4.14E-002 m (16 eps * scale * sqrt(steps=3,631 of the most accurate run); each cell uses its own run's step count, and a run whose error is within that noise is reported as 'below round-off floor', not as a ranking result (3 cell(s) excluded).
