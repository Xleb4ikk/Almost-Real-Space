# Integrator comparison

Cost = RHS-equivalent calls of the cheapest actually executed run whose measured error is at or below the target (no interpolation, never extrapolated).
`EDGE` = even the cheapest tested point was already below the target (cost is an upper bound, not a result). `~` marks methods within 10% of the winner: that ranking is not resolved. `not reached` = the finest run (or the RHS budget) stayed above the target; the parenthesised value is the best error it did reach. `below round-off floor` = the target is at or below the run's own double-precision accumulation noise, so it is not ranked. `n/a` = method not valid for this problem. Scenarios whose reference is not certified are listed in the detail but excluded from the summary.
Build: Release. Per-run RHS budget: per scenario (see section headers).

## Who wins where

| scenario | 100 m | 10 m | 1 m |
|---|---|---|---|
| Earth LEO 400 km circular | WH-Y6 (1,347) | WH-Y6 (1,347) | WH-Y6 (1,795) |
| Earth LEO 400 km polar | WH-Y6 (1,347) | WH-Y6 (1,347) | WH-Y6 (1,795) |
| Earth LEO 400 km sun-synchronous | WH-Y6 (5,379) | DOP853-adaptive (7,599) ~ WH-Y6 (8,067) [tie within 10%] | DOP853-adaptive (10,443) |
| Earth GTO 400 x 40000 km | WH-Y6 (2,607) | WH-Y6 (4,343) | WH-Y6 (5,211) |
| Earth Molniya 500 x 40000 km | WH-Y6 (11,987) | WH-Y6 (17,979) | WH-Y6 (26,967) |
| Earth GEO 35786 km | WH-Y6 (5,043) | WH-Y6 (6,723) | WH-Y6 (10,083) |
| Sun 1 AU circular | DOP853-adaptive (975) | DOP853-adaptive (1,227) | DOP853-adaptive (1,803) |
| Sun 0.5 x 2 AU eccentric | DOP853-adaptive (1,329) | DOP853-adaptive (1,650) | DOP853-adaptive (2,082) |
| Earth 400 km + J2 | WH-Y6 (2,243) | WH-Y6 (3,139) | WH-Y6 (4,035) |
| Earth 250 km + atmosphere drag | DOP853-adaptive (7,023) | DOP853-adaptive (9,291) | DOP853-adaptive (10,479) |
| Earth LEO 400 km + 600 s prograde burn | DOP853-adaptive (7,221) ~ DOPRI5-adaptive (7,905) [tie within 10%] | DOP853-adaptive (9,186) | DOP853-adaptive (11,304) |
| Earth LEO 400 km, 1000 orbits (~64 d) | WH-Y6 (93,187) | - | - |
| Earth HEO 300 x 120000 km | DOP853-adaptive (6,729) | DOP853-adaptive (7,575) | DOP853-adaptive (9,060) |
| Earth hyperbolic flyby e=1.5 | WH-Y6 (<= 451) EDGE | WH-Y6 (899) | DOP853-adaptive (1,191) |
| Earth LEO 400 km, 30 d | WH-Y6 (43,011) | WH-Y6 (50,179) | WH-Y6 (57,347) |
| Earth LEO 400 km + unaligned 600 s burn | DOP853-adaptive (7,422) ~ DOPRI5-adaptive (7,797) [tie within 10%] | DOP853-adaptive (9,219) | DOP853-adaptive (11,409) |

Not ranked (reference not certified; detail only): Earth-Moon flyby inside lunar SOI.

## Earth LEO 400 km circular

Reference floor: 5.02E-006 m (fixed DOP853 h=14.4397 s, ratio=328.63 order=8.36 (floor from finest disagreement), est 5.02E-006 m / 5.7E-009 m/s, finest d=5.02E-006 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 35,328 (9 ms) | 58,560 (14 ms) | 97,152 (26 ms) |
| DOPRI5-fixed | 21,315 (6 ms) | 35,427 (11 ms) | 58,467 (18 ms) |
| DOPRI5-adaptive | 10,983 (4 ms) | 34,293 (12 ms) | 54,165 (17 ms) |
| DOPRI5-adaptive [cap=600 s] | 10,983 (3 ms) | 34,293 (11 ms) | 54,165 (17 ms) |
| DOP853-adaptive | 6,987 (2 ms) | 8,751 (3 ms) | 10,443 (3 ms) |
| DOP853-adaptive [cap=600 s] | 6,987 (2 ms) | 8,751 (3 ms) | 10,443 (3 ms) |
| PEFRL | 23,616 (7 ms) | 38,976 (12 ms) | 71,616 (23 ms) |
| Y6 | 18,483 (7 ms) | 27,555 (9 ms) | 41,331 (13 ms) |
| Y6C | 18,483 (6 ms) | 27,555 (9 ms) | 41,331 (14 ms) |
| WH-Y6 | 1,347 (1 ms) | 1,347 (1 ms) | 1,795 (1 ms) |
| Leapfrog | 458,739 (152 ms) | 1.40E+006 (475 ms) | 4.72E+006 (1623 ms) |

Findings:
- DOPRI5-adaptive: 1 run(s) did not complete the horizon: aborted at t=2.413E+005 s: scaled error stayed above 1 at the smallest step; step floor 8.640E-010 s reached at t=2.413E+005 s (last scaled error 2.664E+001); worst error at completed checkpoints 1.20E+007 m; excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.

## Earth LEO 400 km polar

Reference floor: 1.46E-006 m (fixed DOP853 h=14.4397 s, ratio=336.92 order=8.40 (floor from finest disagreement), est 1.46E-006 m / 1.6E-009 m/s, finest d=1.46E-006 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 35,328 (8 ms) | 58,560 (13 ms) | 97,152 (23 ms) |
| DOPRI5-fixed | 21,315 (6 ms) | 35,427 (11 ms) | 58,467 (18 ms) |
| DOPRI5-adaptive | 10,983 (3 ms) | 34,293 (10 ms) | 54,165 (17 ms) |
| DOPRI5-adaptive [cap=600 s] | 10,983 (3 ms) | 34,293 (10 ms) | 54,165 (17 ms) |
| DOP853-adaptive | 6,987 (2 ms) | 8,751 (3 ms) | 10,443 (3 ms) |
| DOP853-adaptive [cap=600 s] | 6,987 (2 ms) | 8,751 (3 ms) | 10,443 (3 ms) |
| PEFRL | 23,616 (9 ms) | 38,976 (13 ms) | 71,616 (29 ms) |
| Y6 | 18,483 (6 ms) | 27,555 (12 ms) | 41,331 (17 ms) |
| Y6C | 18,483 (6 ms) | 27,555 (10 ms) | 41,331 (17 ms) |
| WH-Y6 | 1,347 (0 ms) | 1,347 (0 ms) | 1,795 (1 ms) |
| Leapfrog | 458,739 (159 ms) | 1.40E+006 (480 ms) | 4.72E+006 (1631 ms) |

Findings:
- DOPRI5-adaptive: 1 run(s) did not complete the horizon: aborted at t=2.413E+005 s: scaled error stayed above 1 at the smallest step; step floor 8.640E-010 s reached at t=2.413E+005 s (last scaled error 2.664E+001); worst error at completed checkpoints 1.20E+007 m; excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.

## Earth LEO 400 km sun-synchronous

Reference floor: 2.62E-006 m (fixed DOP853 h=14.4397 s, ratio=307.53 order=8.26 (floor from finest disagreement), est 2.62E-006 m / 2.9E-009 m/s, finest d=2.62E-006 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 35,328 (9 ms) | 58,560 (15 ms) | 97,152 (25 ms) |
| DOPRI5-fixed | 21,315 (8 ms) | 35,427 (12 ms) | 58,467 (18 ms) |
| DOPRI5-adaptive | 10,983 (3 ms) | 34,293 (13 ms) | 54,165 (18 ms) |
| DOPRI5-adaptive [cap=600 s] | 10,983 (3 ms) | 34,293 (12 ms) | 54,165 (19 ms) |
| DOP853-adaptive | 6,987 (2 ms) | 7,599 (3 ms) | 10,443 (3 ms) |
| DOP853-adaptive [cap=600 s] | 6,987 (2 ms) | 7,599 (3 ms) | 10,443 (3 ms) |
| PEFRL | 23,616 (8 ms) | 38,976 (13 ms) | 71,616 (25 ms) |
| Y6 | 18,483 (6 ms) | 27,555 (9 ms) | 41,331 (14 ms) |
| Y6C | 18,483 (6 ms) | 27,555 (9 ms) | 41,331 (15 ms) |
| WH-Y6 | 5,379 (2 ms) | 8,067 (3 ms) | 11,651 (4 ms) |
| Leapfrog | 458,739 (160 ms) | 1.40E+006 (578 ms) | 4.72E+006 (1661 ms) |

Findings:
- DOPRI5-adaptive: 1 run(s) did not complete the horizon: aborted at t=2.468E+005 s: scaled error stayed above 1 at the smallest step; step floor 8.640E-010 s reached at t=2.468E+005 s (last scaled error 1.226E+002); worst error at completed checkpoints 1.20E+007 m; excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.
- TIE at 10 m: DOP853-adaptive (7,599), WH-Y6 (8,067) are within 10%; do not read the ranking as resolved.

## Earth GTO 400 x 40000 km

Reference floor: 3.39E-006 m (fixed DOP853 h=14.4397 s, ratio=923.72 order=9.85 (floor from finest disagreement), est 3.39E-006 m / 1.7E-009 m/s, finest d=3.39E-006 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 95,604 (22 ms) | 158,472 (40 ms) | 263,376 (63 ms) |
| DOPRI5-fixed | 47,433 (14 ms) | 78,123 (23 ms) | 117,183 (36 ms) |
| DOPRI5-adaptive | 9,057 (3 ms) | 17,553 (5 ms) | 27,651 (8 ms) |
| DOPRI5-adaptive [cap=600 s] | 9,939 (3 ms) | 17,553 (5 ms) | 27,651 (9 ms) |
| DOP853-adaptive | 7,311 (2 ms) | 9,417 (3 ms) | 12,096 (4 ms) |
| DOP853-adaptive [cap=600 s] | 12,768 (4 ms) | 13,815 (5 ms) | 15,360 (5 ms) |
| PEFRL | 78,120 (25 ms) | 129,456 (44 ms) | 237,708 (81 ms) |
| Y6 | 55,338 (19 ms) | 82,680 (28 ms) | 111,975 (37 ms) |
| Y6C | 55,338 (19 ms) | 82,680 (28 ms) | 111,975 (38 ms) |
| WH-Y6 | 2,607 (1 ms) | 4,343 (2 ms) | 5,211 (2 ms) |
| Leapfrog | 2.80E+006 (948 ms) | not reached (best 4.04E+001) | not reached (best 4.04E+001) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 10 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 16 run(s); the step cap, not the tolerance, sets the cost there.
- WH-Y6: error is non-monotonic at coarse h in 1 place(s) but recovers at finer h (h=2.77E+03 -> 1.85E+03 s, recovers by h=1.23E+03 s); that coarse branch is pre-asymptotic, not a round-off floor.
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Earth Molniya 500 x 40000 km

Reference floor: 9.52E-005 m (fixed DOP853 h=14.7608 s, ratio=553.83 order=9.11 (floor from finest disagreement), est 9.52E-005 m / 7.4E-008 m/s, finest d=9.52E-005 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 444,264 (99 ms) | 814,056 (193 ms) | 1.35E+006 (324 ms) |
| DOPRI5-fixed | 267,717 (81 ms) | 444,909 (137 ms) | 666,399 (204 ms) |
| DOPRI5-adaptive | 47,667 (15 ms) | 94,575 (30 ms) | 149,223 (47 ms) |
| DOPRI5-adaptive [cap=600 s] | 47,667 (15 ms) | 94,575 (29 ms) | 149,223 (47 ms) |
| DOP853-adaptive | 32,013 (10 ms) | 41,214 (14 ms) | 45,123 (16 ms) |
| DOP853-adaptive [cap=600 s] | 47,412 (16 ms) | 52,716 (18 ms) | 55,566 (19 ms) |
| PEFRL | 362,088 (124 ms) | 600,912 (204 ms) | 1.10E+006 (402 ms) |
| Y6 | 231,444 (79 ms) | 346,041 (119 ms) | 467,379 (155 ms) |
| Y6C | 231,444 (81 ms) | 346,041 (120 ms) | 467,379 (159 ms) |
| WH-Y6 | 11,987 (4 ms) | 17,979 (6 ms) | 26,967 (9 ms) |
| Leapfrog | not reached (best 1.59E+003) | not reached (best 1.59E+003) | not reached (best 1.59E+003) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 10 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 18 run(s); the step cap, not the tolerance, sets the cost there.
- WH-Y6: error is non-monotonic at coarse h in 1 place(s) but recovers at finer h (h=2.83E+03 -> 1.89E+03 s, recovers by h=1.71E+03 s); that coarse branch is pre-asymptotic, not a round-off floor.
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Earth GEO 35786 km

Reference floor: 1.80E-004 m (fixed DOP853 h=448.769 s, ratio=1688.84 order=10.72 (floor from finest disagreement), est 1.80E-004 m / 1.3E-008 m/s, finest d=1.80E-004 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 124,920 (29 ms) | 207,360 (45 ms) | 344,520 (78 ms) |
| DOPRI5-fixed | 83,703 (26 ms) | 125,283 (38 ms) | 207,363 (62 ms) |
| DOPRI5-adaptive | 83,181 (25 ms) | 131,781 (41 ms) | 208,461 (69 ms) |
| DOPRI5-adaptive [cap=600 s] | <=77,799 EDGE | 131,781 (40 ms) | 208,461 (64 ms) |
| DOP853-adaptive | 17,355 (6 ms) | 22,755 (7 ms) | 30,315 (10 ms) |
| DOP853-adaptive [cap=600 s] | <=155,595 EDGE | <=155,595 EDGE | <=155,595 EDGE |
| PEFRL | 83,520 (28 ms) | 138,240 (45 ms) | 254,160 (85 ms) |
| Y6 | 58,593 (19 ms) | 79,383 (26 ms) | 119,073 (39 ms) |
| Y6C | 58,593 (20 ms) | 79,383 (28 ms) | 119,073 (42 ms) |
| WH-Y6 | 5,043 (2 ms) | 6,723 (2 ms) | 10,083 (3 ms) |
| Leapfrog | 2.99E+006 (994 ms) | not reached (best 5.18E+001) | not reached (best 5.18E+001) |

Findings:
- DOPRI5-adaptive: 1 run(s) did not complete the horizon: aborted at t=1.227E+006 s: scaled error stayed above 1 at the smallest step; step floor 2.592E-008 s reached at t=1.227E+006 s (last scaled error 2.664E+001); worst error at completed checkpoints 7.64E+007 m; excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 22 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOP853-adaptive: the uncapped branch reached only 8.44E-002 m while DOP853-adaptive [cap=600 s] reached 2.56E-004 m; the step cap is load-bearing on this horizon.

## Sun 1 AU circular

Reference floor: 5.31E-004 m (fixed DOP853 h=82182.8 s, ratio=280.08 order=8.13 (floor from finest disagreement), est 5.31E-004 m / 1.1E-010 m/s, finest d=5.31E-004 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 9,696 (2 ms) | 16,992 (4 ms) | 31,104 (7 ms) |
| DOPRI5-fixed | 3,315 (1 ms) | 5,331 (2 ms) | 8,355 (2 ms) |
| DOPRI5-adaptive | 3,603 (1 ms) | 5,619 (2 ms) | 8,805 (3 ms) |
| DOPRI5-adaptive [cap=600 s] | below round-off floor (1.22E-001 m) | below round-off floor (1.22E-001 m) | below round-off floor (1.22E-001 m) |
| DOP853-adaptive | 975 (0 ms) | 1,227 (0 ms) | 1,803 (1 ms) |
| DOP853-adaptive [cap=600 s] | below round-off floor (1.22E-001 m) | below round-off floor (1.22E-001 m) | below round-off floor (1.22E-001 m) |
| PEFRL | 9,216 (3 ms) | 15,360 (5 ms) | 28,128 (9 ms) |
| Y6 | 4,035 (1 ms) | 5,883 (2 ms) | 8,907 (3 ms) |
| Y6C | 4,035 (1 ms) | 5,883 (2 ms) | 8,907 (3 ms) |
| WH-Y6 | <=2,691 EDGE | <=2,691 EDGE | <=2,691 EDGE |
| Leapfrog | not reached (best 1.60E+003) | not reached (best 1.60E+003) | not reached (best 1.60E+003) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- double-precision round-off estimate 5.21E-003 m (16 eps * scale * sqrt(steps=96 of the most accurate run); each cell uses its own run's step count, and a run whose error is within that noise is reported as 'below round-off floor', not as a ranking result (6 cell(s) excluded).

## Sun 0.5 x 2 AU eccentric

Reference floor: 9.18E-004 m (fixed DOP853 h=29056 s, ratio=216.70 order=7.76 (floor from finest disagreement), est 9.18E-004 m / 7.8E-011 m/s, finest d=9.18E-004 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 31,104 (7 ms) | 57,120 (13 ms) | 94,848 (23 ms) |
| DOPRI5-fixed | 10,227 (3 ms) | 16,995 (5 ms) | 25,491 (8 ms) |
| DOPRI5-adaptive | 3,675 (1 ms) | 4,575 (1 ms) | 7,203 (2 ms) |
| DOPRI5-adaptive [cap=600 s] | below round-off floor (2.44E-001 m) | below round-off floor (2.44E-001 m) | below round-off floor (2.44E-001 m) |
| DOP853-adaptive | 1,329 (0 ms) | 1,650 (1 ms) | 2,082 (1 ms) |
| DOP853-adaptive [cap=600 s] | below round-off floor (2.44E-001 m) | below round-off floor (2.44E-001 m) | below round-off floor (2.44E-001 m) |
| PEFRL | 28,128 (9 ms) | 46,656 (16 ms) | 85,728 (29 ms) |
| Y6 | 12,603 (4 ms) | 17,979 (6 ms) | 26,883 (9 ms) |
| Y6C | 12,603 (4 ms) | 17,979 (6 ms) | 26,883 (9 ms) |
| WH-Y6 | <=2,691 EDGE | <=2,691 EDGE | <=2,691 EDGE |
| Leapfrog | not reached (best 1.97E+003) | not reached (best 1.97E+003) | not reached (best 1.97E+003) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- double-precision round-off estimate 2.44E-001 m (16 eps * scale * sqrt(steps=52,600 of the most accurate run); each cell uses its own run's step count, and a run whose error is within that noise is reported as 'below round-off floor', not as a ranking result (6 cell(s) excluded).

## Earth 400 km + J2

Reference floor: 2.57E-006 m (fixed DOP853 h=14.4397 s, ratio=350.49 order=8.45 (floor from finest disagreement), est 2.57E-006 m / 2.9E-009 m/s, finest d=2.57E-006 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 35,328 (8 ms) | 58,560 (16 ms) | 97,152 (25 ms) |
| DOPRI5-fixed | 21,315 (6 ms) | 35,427 (11 ms) | 58,467 (20 ms) |
| DOPRI5-adaptive | 21,639 (7 ms) | 34,311 (11 ms) | 54,453 (19 ms) |
| DOPRI5-adaptive [cap=600 s] | 21,639 (7 ms) | 34,311 (14 ms) | 54,453 (19 ms) |
| DOP853-adaptive | 6,987 (3 ms) | 8,751 (3 ms) | 10,443 (4 ms) |
| DOP853-adaptive [cap=600 s] | 6,987 (2 ms) | 8,751 (3 ms) | 10,443 (4 ms) |
| PEFRL | 23,616 (8 ms) | 38,976 (13 ms) | 71,616 (24 ms) |
| Y6 | 18,483 (7 ms) | 27,555 (9 ms) | 41,331 (14 ms) |
| Y6C | 18,483 (6 ms) | 27,555 (9 ms) | 41,331 (15 ms) |
| WH-Y6 | 2,243 (1 ms) | 3,139 (1 ms) | 4,035 (1 ms) |
| Leapfrog | 458,739 (155 ms) | 1.40E+006 (539 ms) | 4.72E+006 (1784 ms) |

Findings:
- DOPRI5-adaptive: 1 run(s) did not complete the horizon: aborted at t=2.373E+005 s: scaled error stayed above 1 at the smallest step; step floor 8.640E-010 s reached at t=2.373E+005 s (last scaled error 1.278E+002); worst error at completed checkpoints 1.20E+007 m; excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.

## Earth 250 km + atmosphere drag

Reference floor: 1.92E-005 m (fixed DOP853 h=27.9251 s, ratio=240.21 order=7.91 (floor from finest disagreement), est 1.92E-005 m / 2.2E-008 m/s, finest d=1.92E-005 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 36,480 (14 ms) | 60,480 (24 ms) | 100,416 (40 ms) |
| DOPRI5-fixed | 21,027 (9 ms) | 36,579 (17 ms) | 60,483 (28 ms) |
| DOPRI5-adaptive | 22,485 (10 ms) | 35,445 (17 ms) | 56,181 (26 ms) |
| DOPRI5-adaptive [cap=600 s] | 22,485 (10 ms) | 35,445 (17 ms) | 56,181 (26 ms) |
| DOP853-adaptive | 7,023 (3 ms) | 9,291 (4 ms) | 10,479 (5 ms) |
| DOP853-adaptive [cap=600 s] | 7,023 (3 ms) | 9,291 (4 ms) | 10,479 (5 ms) |
| PEFRL | n/a | n/a | n/a |
| Y6 | n/a | n/a | n/a |
| Y6C | n/a | n/a | n/a |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | n/a | n/a | n/a |

Findings:
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.

## Earth LEO 400 km + 600 s prograde burn

Reference floor: 1.78E-005 m (fixed DOP853 h=28.8795 s, ratio=1277.39 order=10.32 (floor from finest disagreement), est 1.78E-005 m / 8.5E-009 m/s, finest d=1.78E-005 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 26,148 (6 ms) | 53,028 (12 ms) | 107,568 (26 ms) |
| DOPRI5-fixed | 10,695 (3 ms) | 26,247 (8 ms) | 47,847 (15 ms) |
| DOPRI5-adaptive | 7,905 (2 ms) | 15,429 (5 ms) | 24,375 (7 ms) |
| DOPRI5-adaptive [cap=600 s] | 7,905 (2 ms) | 15,429 (5 ms) | 24,375 (7 ms) |
| DOP853-adaptive | 7,221 (2 ms) | 9,240 (3 ms) | 11,622 (4 ms) |
| DOP853-adaptive [cap=600 s] | 7,596 (2 ms) | 9,186 (3 ms) | 11,304 (4 ms) |
| PEFRL | n/a | n/a | n/a |
| Y6 | n/a | n/a | n/a |
| Y6C | n/a | n/a | n/a |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | n/a | n/a | n/a |

Findings:
- RK4: error is non-monotonic at coarse h in 2 place(s) but recovers at finer h (h=36.1 -> 24 s, recovers by h=19.6 s); that coarse branch is pre-asymptotic, not a round-off floor.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 10 run(s); the step cap, not the tolerance, sets the cost there.
- TIE at 100 m: DOPRI5-adaptive (7,905), DOP853-adaptive (7,221) are within 10%; do not read the ranking as resolved.

## Earth-Moon flyby inside lunar SOI

Reference floor: not certified (fixed DOP853 h=52.0833 s, ratio=4.81 order=2.27 UNRESOLVED (no converging pair), est NaN m / NaN m/s, finest d=9.09E-006 m retry 2). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 576 (0 ms) | 960 (0 ms) | 1,536 (0 ms) |
| DOPRI5-fixed | 435 (0 ms) | 723 (0 ms) | 1,011 (0 ms) |
| DOPRI5-adaptive | 705 (0 ms) | 813 (0 ms) | 1,227 (0 ms) |
| DOPRI5-adaptive [cap=600 s] | <=5,223 EDGE | <=5,223 EDGE | <=5,223 EDGE |
| DOP853-adaptive | <=435 EDGE | <=435 EDGE | 720 (0 ms) |
| DOP853-adaptive [cap=600 s] | <=10,443 EDGE | <=10,443 EDGE | <=10,443 EDGE |
| PEFRL | 672 (0 ms) | 1,056 (0 ms) | 2,016 (1 ms) |
| Y6 | 675 (0 ms) | 1,011 (0 ms) | 1,347 (0 ms) |
| Y6C | 675 (0 ms) | 1,011 (0 ms) | 1,347 (0 ms) |
| WH-Y6 | 899 (0 ms) | 1,347 (0 ms) | 1,795 (1 ms) |
| Leapfrog | 8,379 (3 ms) | 25,563 (8 ms) | 86,211 (28 ms) |

Findings:
- REFERENCE NOT CERTIFIED: every number of this scenario is provisional (no reference floor). Reference: fixed DOP853 h=52.0833 s, ratio=4.81 order=2.27 UNRESOLVED (no converging pair), est NaN m / NaN m/s, finest d=9.09E-006 m retry 2
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOPRI5-adaptive: the uncapped branch reached only 1.35E-001 m while DOPRI5-adaptive [cap=600 s] reached 5.89E-005 m; the step cap is load-bearing on this horizon.
- DOP853-adaptive: the uncapped branch reached only 1.95E-001 m while DOP853-adaptive [cap=600 s] reached 3.01E-005 m; the step cap is load-bearing on this horizon.

## Earth LEO 400 km, 1000 orbits (~64 d)

Reference floor: 8.52E+000 m (fixed DOP853 h=43.3192 s, ratio=392.84 order=8.62, est 1.82E-004 m / 2.1E-007 m/s, finest d=7.15E-002 m + independent dd endpoint floor 8.522E+000 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | not reached (best 1.11E+004) | skip | skip |
| DOPRI5-fixed | not reached (best 1.62E+003) | skip | skip |
| DOPRI5-adaptive | not reached (best 4.03E+002) | skip | skip |
| DOPRI5-adaptive [cap=600 s] | not reached (best 4.03E+002) | skip | skip |
| DOP853-adaptive | 1.14E+006 (420 ms) | skip | skip |
| DOP853-adaptive [cap=600 s] | 1.14E+006 (381 ms) | skip | skip |
| PEFRL | 4.15E+006 (1347 ms) | skip | skip |
| Y6 | 2.39E+006 (806 ms) | skip | skip |
| Y6C | 2.39E+006 (799 ms) | skip | skip |
| WH-Y6 | 93,187 (30 ms) | skip | skip |
| Leapfrog | not reached (best 1.82E+005) | skip | skip |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 4 run(s) did not complete the horizon: aborted at t=2.395E+005 s: scaled error stayed above 1 at the smallest step; step floor 5.545E-008 s reached at t=2.395E+005 s (last scaled error 2.664E+001); worst error at completed checkpoints 7.29E+006 m; excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive [cap=600 s]: 4 run(s) did not complete the horizon: aborted at t=2.967E+005 s: scaled error stayed above 1 at the smallest step; step floor 5.545E-008 s reached at t=2.967E+005 s (last scaled error 2.664E+001); worst error at completed checkpoints 1.18E+007 m; excluded from ranking, finer controls were still tried.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Earth HEO 300 x 120000 km

Reference floor: 2.32E-005 m (fixed DOP853 h=14.121 s, ratio=133.44 order=7.06 (floor from finest disagreement), est 2.32E-005 m / 8.9E-009 m/s, finest d=2.32E-005 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 331,200 (80 ms) | 549,120 (131 ms) | 1.01E+006 (241 ms) |
| DOPRI5-fixed | 148,323 (45 ms) | 221,763 (66 ms) | 367,203 (110 ms) |
| DOPRI5-adaptive | 9,237 (3 ms) | 14,529 (4 ms) | 22,197 (7 ms) |
| DOPRI5-adaptive [cap=600 s] | 16,671 (5 ms) | 19,839 (6 ms) | 25,635 (8 ms) |
| DOP853-adaptive | 6,729 (2 ms) | 7,575 (2 ms) | 9,060 (3 ms) |
| DOP853-adaptive [cap=600 s] | 27,114 (9 ms) | 27,432 (9 ms) | 28,137 (9 ms) |
| PEFRL | 270,720 (90 ms) | 496,320 (167 ms) | 912,000 (304 ms) |
| Y6 | 173,043 (58 ms) | 258,723 (88 ms) | 386,403 (132 ms) |
| Y6C | 173,043 (58 ms) | 258,723 (88 ms) | 386,403 (130 ms) |
| WH-Y6 | 8,963 (3 ms) | 13,443 (5 ms) | 20,163 (7 ms) |
| Leapfrog | not reached (best 8.29E+002) | not reached (best 8.29E+002) | not reached (best 8.29E+002) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 17 run(s); the step cap, not the tolerance, sets the cost there.
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Earth hyperbolic flyby e=1.5

Reference floor: 1.82E-006 m (fixed DOP853 h=14.121 s, ratio=315.52 order=8.30 (floor from finest disagreement), est 1.82E-006 m / 2.0E-011 m/s, finest d=1.82E-006 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 26,688 (6 ms) | 48,960 (11 ms) | 89,856 (21 ms) |
| DOPRI5-fixed | 12,675 (4 ms) | 18,723 (5 ms) | 29,667 (9 ms) |
| DOPRI5-adaptive | 795 (0 ms) | 1,065 (0 ms) | 1,569 (0 ms) |
| DOPRI5-adaptive [cap=600 s] | 2,883 (1 ms) | 2,973 (1 ms) | 3,297 (1 ms) |
| DOP853-adaptive | 939 (0 ms) | 1,047 (0 ms) | 1,191 (0 ms) |
| DOP853-adaptive [cap=600 s] | 5,331 (2 ms) | 5,367 (2 ms) | 5,403 (2 ms) |
| PEFRL | 22,848 (7 ms) | 39,936 (13 ms) | 73,344 (23 ms) |
| Y6 | 21,843 (7 ms) | 31,251 (10 ms) | 46,707 (15 ms) |
| Y6C | 21,843 (7 ms) | 31,251 (10 ms) | 46,707 (16 ms) |
| WH-Y6 | <=451 EDGE | 899 (0 ms) | 2,243 (1 ms) |
| Leapfrog | 423,891 (137 ms) | 1.29E+006 (421 ms) | 4.36E+006 (1420 ms) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 14 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 13 run(s); the step cap, not the tolerance, sets the cost there.

## Earth LEO 400 km, 30 d

Reference floor: 2.54E-003 m (fixed DOP853 h=14.4397 s, ratio=560.12 order=9.13 (floor from finest disagreement), est 2.54E-003 m / 2.9E-006 m/s, finest d=2.54E-003 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 3.95E+006 (938 ms) | not reached (best 4.35E+001) | not reached (best 4.35E+001) |
| DOPRI5-fixed | 2.63E+006 (785 ms) | 4.37E+006 (1303 ms) | not reached (best 6.29E+000) |
| DOPRI5-adaptive | 2.57E+006 (790 ms) | 4.08E+006 (1250 ms) | not reached (best 2.82E+000) |
| DOPRI5-adaptive [cap=600 s] | 2.57E+006 (796 ms) | 4.08E+006 (1383 ms) | not reached (best 2.82E+000) |
| DOP853-adaptive | 405,579 (134 ms) | 534,603 (176 ms) | 709,707 (234 ms) |
| DOP853-adaptive [cap=600 s] | 405,579 (134 ms) | 534,603 (177 ms) | 709,707 (234 ms) |
| PEFRL | 1.59E+006 (512 ms) | 2.91E+006 (945 ms) | not reached (best 1.44E+000) |
| Y6 | 1.01E+006 (352 ms) | 1.51E+006 (517 ms) | 2.27E+006 (776 ms) |
| Y6C | 1.01E+006 (344 ms) | 1.51E+006 (516 ms) | 2.27E+006 (771 ms) |
| WH-Y6 | 43,011 (14 ms) | 50,179 (17 ms) | 57,347 (19 ms) |
| Leapfrog | not reached (best 1.68E+004) | not reached (best 1.68E+004) | not reached (best 1.68E+004) |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 3 run(s) did not complete the horizon: aborted at t=2.404E+005 s: scaled error stayed above 1 at the smallest step; step floor 2.592E-008 s reached at t=2.404E+005 s (last scaled error 2.664E+001); worst error at completed checkpoints 9.37E+006 m; excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive [cap=600 s]: 3 run(s) did not complete the horizon: aborted at t=2.987E+005 s: scaled error stayed above 1 at the smallest step; step floor 2.592E-008 s reached at t=2.987E+005 s (last scaled error 2.664E+001); worst error at completed checkpoints 1.30E+007 m; excluded from ranking, finer controls were still tried.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Earth LEO 400 km + unaligned 600 s burn

Reference floor: 2.51E-005 m (fixed DOP853 h=28.8795 s, ratio=174.67 order=7.45 (floor from finest disagreement), est 2.51E-005 m / 2.4E-008 m/s, finest d=2.51E-005 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 31,932 (8 ms) | 53,040 (12 ms) | 97,176 (23 ms) |
| DOPRI5-fixed | 17,643 (5 ms) | 28,857 (8 ms) | 47,901 (15 ms) |
| DOPRI5-adaptive | 7,797 (2 ms) | 19,119 (6 ms) | 30,171 (9 ms) |
| DOPRI5-adaptive [cap=600 s] | 7,797 (2 ms) | 19,119 (6 ms) | 30,171 (9 ms) |
| DOP853-adaptive | 7,422 (2 ms) | 9,240 (3 ms) | 11,520 (4 ms) |
| DOP853-adaptive [cap=600 s] | 7,596 (2 ms) | 9,219 (3 ms) | 11,409 (4 ms) |
| PEFRL | n/a | n/a | n/a |
| Y6 | n/a | n/a | n/a |
| Y6C | n/a | n/a | n/a |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | n/a | n/a | n/a |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 10 run(s); the step cap, not the tolerance, sets the cost there.
- TIE at 100 m: DOPRI5-adaptive (7,797), DOP853-adaptive (7,422) are within 10%; do not read the ranking as resolved.
