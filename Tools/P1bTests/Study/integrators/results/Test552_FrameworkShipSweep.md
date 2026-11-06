# Integrator comparison

Cost = RHS-equivalent calls of the cheapest actually executed run whose measured error is at or below the target (no interpolation, never extrapolated).
`EDGE` = even the cheapest tested point was already below the target (cost is an upper bound, not a result). `~` marks methods within 10% of the winner: that ranking is not resolved. `not reached` = the finest run (or the RHS budget) stayed above the target; the parenthesised value is the best error it did reach. `n/a` = method not valid for this problem.
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
| Earth LEO 400 km + 600 s prograde burn | DOP853-adaptive (7,221) ~ DOPRI5-adaptive (7,905) [tie within 10%] | DOP853-adaptive [cap=600 s] (9,186) | DOP853-adaptive [cap=600 s] (11,304) |
| Earth-Moon flyby inside lunar SOI (ref. NOT certified) | DOPRI5-fixed (435) | DOP853-adaptive (<= 435) EDGE | DOP853-adaptive (720) |
| Earth LEO 400 km, 1000 orbits (~64 d) | WH-Y6 (93,187) | - | - |
| Earth HEO 300 x 120000 km | DOP853-adaptive (6,729) | DOP853-adaptive (7,575) | DOP853-adaptive (9,060) |
| Earth hyperbolic flyby e=1.5 | WH-Y6 (<= 451) EDGE | WH-Y6 (899) | DOP853-adaptive (1,191) |
| Earth LEO 400 km, 30 d | WH-Y6 (43,011) | WH-Y6 (50,179) | WH-Y6 (57,347) |
| Earth LEO 400 km + unaligned 600 s burn | DOP853-adaptive (7,422) ~ DOPRI5-adaptive (7,797) [tie within 10%] | DOP853-adaptive [cap=600 s] (9,219) | DOP853-adaptive [cap=600 s] (11,409) |

## Earth LEO 400 km circular

Reference floor: 5.02E-006 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 35,328 (4 ms) | 58,560 (7 ms) | 97,152 (11 ms) |
| DOPRI5-fixed | 21,315 (3 ms) | 35,427 (5 ms) | 58,467 (8 ms) |
| DOPRI5-adaptive | 10,983 (2 ms) | 34,293 (5 ms) | 54,165 (8 ms) |
| DOPRI5-adaptive [cap=600 s] | 10,983 (2 ms) | 34,293 (5 ms) | 54,165 (8 ms) |
| DOP853-adaptive | 6,987 (1 ms) | 8,751 (1 ms) | 10,443 (2 ms) |
| DOP853-adaptive [cap=600 s] | 6,987 (1 ms) | 8,751 (1 ms) | 10,443 (2 ms) |
| PEFRL | 23,616 (4 ms) | 38,976 (6 ms) | 71,616 (11 ms) |
| Y6 | 18,483 (3 ms) | 27,555 (5 ms) | 41,331 (7 ms) |
| Y6C | 18,483 (3 ms) | 27,555 (4 ms) | 41,331 (6 ms) |
| WH-Y6 | 1,347 (0 ms) | 1,347 (0 ms) | 1,795 (0 ms) |
| Leapfrog | 458,739 (65 ms) | 1.40E+006 (196 ms) | 4.72E+006 (664 ms) |

Findings:
- DOPRI5-adaptive: 1 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.

## Earth LEO 400 km polar

Reference floor: 1.46E-006 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 35,328 (4 ms) | 58,560 (6 ms) | 97,152 (11 ms) |
| DOPRI5-fixed | 21,315 (3 ms) | 35,427 (5 ms) | 58,467 (8 ms) |
| DOPRI5-adaptive | 10,983 (2 ms) | 34,293 (5 ms) | 54,165 (8 ms) |
| DOPRI5-adaptive [cap=600 s] | 10,983 (2 ms) | 34,293 (5 ms) | 54,165 (8 ms) |
| DOP853-adaptive | 6,987 (1 ms) | 8,751 (1 ms) | 10,443 (2 ms) |
| DOP853-adaptive [cap=600 s] | 6,987 (1 ms) | 8,751 (1 ms) | 10,443 (2 ms) |
| PEFRL | 23,616 (4 ms) | 38,976 (6 ms) | 71,616 (11 ms) |
| Y6 | 18,483 (3 ms) | 27,555 (4 ms) | 41,331 (6 ms) |
| Y6C | 18,483 (3 ms) | 27,555 (4 ms) | 41,331 (6 ms) |
| WH-Y6 | 1,347 (0 ms) | 1,347 (0 ms) | 1,795 (0 ms) |
| Leapfrog | 458,739 (66 ms) | 1.40E+006 (197 ms) | 4.72E+006 (669 ms) |

Findings:
- DOPRI5-adaptive: 1 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.

## Earth LEO 400 km sun-synchronous

Reference floor: 2.62E-006 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 35,328 (4 ms) | 58,560 (6 ms) | 97,152 (12 ms) |
| DOPRI5-fixed | 21,315 (3 ms) | 35,427 (5 ms) | 58,467 (8 ms) |
| DOPRI5-adaptive | 10,983 (2 ms) | 34,293 (5 ms) | 54,165 (7 ms) |
| DOPRI5-adaptive [cap=600 s] | 10,983 (2 ms) | 34,293 (5 ms) | 54,165 (7 ms) |
| DOP853-adaptive | 6,987 (1 ms) | 7,599 (1 ms) | 10,443 (2 ms) |
| DOP853-adaptive [cap=600 s] | 6,987 (1 ms) | 7,599 (1 ms) | 10,443 (2 ms) |
| PEFRL | 23,616 (3 ms) | 38,976 (6 ms) | 71,616 (10 ms) |
| Y6 | 18,483 (3 ms) | 27,555 (4 ms) | 41,331 (6 ms) |
| Y6C | 18,483 (3 ms) | 27,555 (4 ms) | 41,331 (6 ms) |
| WH-Y6 | 5,379 (1 ms) | 8,067 (1 ms) | 11,651 (2 ms) |
| Leapfrog | 458,739 (72 ms) | 1.40E+006 (204 ms) | 4.72E+006 (692 ms) |

Findings:
- DOPRI5-adaptive: 1 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.
- TIE at 10 m: DOP853-adaptive (7,599), WH-Y6 (8,067) are within 10%; do not read the ranking as resolved.

## Earth GTO 400 x 40000 km

Reference floor: 3.39E-006 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 95,604 (10 ms) | 158,472 (17 ms) | 263,376 (29 ms) |
| DOPRI5-fixed | 47,433 (6 ms) | 78,123 (11 ms) | 117,183 (15 ms) |
| DOPRI5-adaptive | 9,057 (1 ms) | 17,553 (2 ms) | 27,651 (4 ms) |
| DOPRI5-adaptive [cap=600 s] | 9,939 (1 ms) | 17,553 (2 ms) | 27,651 (4 ms) |
| DOP853-adaptive | 7,311 (1 ms) | 9,417 (1 ms) | 12,096 (2 ms) |
| DOP853-adaptive [cap=600 s] | 12,768 (2 ms) | 13,815 (2 ms) | 15,360 (2 ms) |
| PEFRL | 78,120 (11 ms) | 129,456 (18 ms) | 237,708 (33 ms) |
| Y6 | 55,338 (8 ms) | 82,680 (12 ms) | 111,975 (16 ms) |
| Y6C | 55,338 (8 ms) | 82,680 (12 ms) | 111,975 (16 ms) |
| WH-Y6 | 2,607 (0 ms) | 4,343 (1 ms) | 5,211 (1 ms) |
| Leapfrog | 2.80E+006 (395 ms) | not reached (best 4.04E+001) | not reached (best 4.04E+001) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 10 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 16 run(s); the step cap, not the tolerance, sets the cost there.
- WH-Y6: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Earth Molniya 500 x 40000 km

Reference floor: 9.52E-005 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 444,264 (42 ms) | 814,056 (81 ms) | 1.35E+006 (144 ms) |
| DOPRI5-fixed | 267,717 (37 ms) | 444,909 (62 ms) | 666,399 (92 ms) |
| DOPRI5-adaptive | 47,667 (7 ms) | 94,575 (14 ms) | 149,223 (20 ms) |
| DOPRI5-adaptive [cap=600 s] | 47,667 (6 ms) | 94,575 (13 ms) | 149,223 (20 ms) |
| DOP853-adaptive | 32,013 (5 ms) | 41,214 (6 ms) | 45,123 (7 ms) |
| DOP853-adaptive [cap=600 s] | 47,412 (7 ms) | 52,716 (8 ms) | 55,566 (8 ms) |
| PEFRL | 362,088 (51 ms) | 600,912 (84 ms) | 1.10E+006 (154 ms) |
| Y6 | 231,444 (33 ms) | 346,041 (49 ms) | 467,379 (66 ms) |
| Y6C | 231,444 (33 ms) | 346,041 (50 ms) | 467,379 (67 ms) |
| WH-Y6 | 11,987 (2 ms) | 17,979 (3 ms) | 26,967 (4 ms) |
| Leapfrog | not reached (best 1.59E+003) | not reached (best 1.59E+003) | not reached (best 1.59E+003) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 10 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 18 run(s); the step cap, not the tolerance, sets the cost there.
- WH-Y6: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Earth GEO 35786 km

Reference floor: 1.80E-004 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 124,920 (13 ms) | 207,360 (21 ms) | 344,520 (36 ms) |
| DOPRI5-fixed | 83,703 (11 ms) | 125,283 (17 ms) | 207,363 (29 ms) |
| DOPRI5-adaptive | 83,181 (12 ms) | 131,781 (19 ms) | 208,461 (30 ms) |
| DOPRI5-adaptive [cap=600 s] | <=77,799 EDGE | 131,781 (18 ms) | 208,461 (28 ms) |
| DOP853-adaptive | 17,355 (2 ms) | 22,755 (3 ms) | 30,315 (4 ms) |
| DOP853-adaptive [cap=600 s] | <=155,595 EDGE | <=155,595 EDGE | <=155,595 EDGE |
| PEFRL | 83,520 (12 ms) | 138,240 (19 ms) | 254,160 (36 ms) |
| Y6 | 58,593 (8 ms) | 79,383 (11 ms) | 119,073 (17 ms) |
| Y6C | 58,593 (8 ms) | 79,383 (11 ms) | 119,073 (17 ms) |
| WH-Y6 | 5,043 (1 ms) | 6,723 (1 ms) | 10,083 (1 ms) |
| Leapfrog | 2.99E+006 (420 ms) | not reached (best 5.18E+001) | not reached (best 5.18E+001) |

Findings:
- DOPRI5-adaptive: 1 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 22 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOP853-adaptive: the uncapped branch reached only 8.44E-002 m while DOP853-adaptive [cap=600 s] reached 2.56E-004 m; the step cap is load-bearing on this horizon.

## Sun 1 AU circular

Reference floor: 5.31E-004 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 9,696 (1 ms) | 16,992 (2 ms) | 31,104 (3 ms) |
| DOPRI5-fixed | 3,315 (0 ms) | 5,331 (1 ms) | 8,355 (1 ms) |
| DOPRI5-adaptive | 3,603 (0 ms) | 5,619 (1 ms) | 8,805 (1 ms) |
| DOPRI5-adaptive [cap=600 s] | <=946,803 EDGE | <=946,803 EDGE | <=946,803 EDGE |
| DOP853-adaptive | 975 (0 ms) | 1,227 (0 ms) | 1,803 (0 ms) |
| DOP853-adaptive [cap=600 s] | <=1.89E+006 EDGE | <=1.89E+006 EDGE | <=1.89E+006 EDGE |
| PEFRL | 9,216 (1 ms) | 15,360 (2 ms) | 28,128 (4 ms) |
| Y6 | 4,035 (1 ms) | 5,883 (1 ms) | 8,907 (1 ms) |
| Y6C | 4,035 (1 ms) | 5,883 (1 ms) | 8,907 (1 ms) |
| WH-Y6 | <=2,691 EDGE | <=2,691 EDGE | <=2,691 EDGE |
| Leapfrog | not reached (best 1.60E+003) | not reached (best 1.60E+003) | not reached (best 1.60E+003) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- double-precision round-off estimate 1.59E-001 m (16 eps * scale * sqrt(max steps=89,968)); the tightest target 1.00E+000 m is at that scale, so that column measures summation quality, not method order.

## Sun 0.5 x 2 AU eccentric

Reference floor: 9.18E-004 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 31,104 (3 ms) | 57,120 (5 ms) | 94,848 (9 ms) |
| DOPRI5-fixed | 10,227 (1 ms) | 16,995 (2 ms) | 25,491 (3 ms) |
| DOPRI5-adaptive | 3,675 (0 ms) | 4,575 (1 ms) | 7,203 (1 ms) |
| DOPRI5-adaptive [cap=600 s] | <=946,803 EDGE | <=946,803 EDGE | <=946,803 EDGE |
| DOP853-adaptive | 1,329 (0 ms) | 1,650 (0 ms) | 2,082 (0 ms) |
| DOP853-adaptive [cap=600 s] | <=1.89E+006 EDGE | <=1.89E+006 EDGE | <=1.89E+006 EDGE |
| PEFRL | 28,128 (4 ms) | 46,656 (7 ms) | 85,728 (12 ms) |
| Y6 | 12,603 (2 ms) | 17,979 (2 ms) | 26,883 (4 ms) |
| Y6C | 12,603 (2 ms) | 17,979 (3 ms) | 26,883 (4 ms) |
| WH-Y6 | <=2,691 EDGE | <=2,691 EDGE | <=2,691 EDGE |
| Leapfrog | not reached (best 1.97E+003) | not reached (best 1.97E+003) | not reached (best 1.97E+003) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- double-precision round-off estimate 5.86E-001 m (16 eps * scale * sqrt(max steps=303,640)); the tightest target 1.00E+000 m is at that scale, so that column measures summation quality, not method order.

## Earth 400 km + J2

Reference floor: 2.57E-006 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 35,328 (4 ms) | 58,560 (6 ms) | 97,152 (10 ms) |
| DOPRI5-fixed | 21,315 (3 ms) | 35,427 (5 ms) | 58,467 (8 ms) |
| DOPRI5-adaptive | 21,639 (3 ms) | 34,311 (5 ms) | 54,453 (7 ms) |
| DOPRI5-adaptive [cap=600 s] | 21,639 (3 ms) | 34,311 (5 ms) | 54,453 (7 ms) |
| DOP853-adaptive | 6,987 (1 ms) | 8,751 (1 ms) | 10,443 (2 ms) |
| DOP853-adaptive [cap=600 s] | 6,987 (1 ms) | 8,751 (1 ms) | 10,443 (2 ms) |
| PEFRL | 23,616 (3 ms) | 38,976 (6 ms) | 71,616 (10 ms) |
| Y6 | 18,483 (3 ms) | 27,555 (4 ms) | 41,331 (6 ms) |
| Y6C | 18,483 (3 ms) | 27,555 (4 ms) | 41,331 (6 ms) |
| WH-Y6 | 2,243 (0 ms) | 3,139 (0 ms) | 4,035 (1 ms) |
| Leapfrog | 458,739 (66 ms) | 1.40E+006 (201 ms) | 4.72E+006 (682 ms) |

Findings:
- DOPRI5-adaptive: 1 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.

## Earth 250 km + atmosphere drag

Reference floor: 1.92E-005 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 36,480 (6 ms) | 60,480 (11 ms) | 100,416 (19 ms) |
| DOPRI5-fixed | 21,027 (5 ms) | 36,579 (8 ms) | 60,483 (13 ms) |
| DOPRI5-adaptive | 22,485 (5 ms) | 35,445 (7 ms) | 56,181 (11 ms) |
| DOPRI5-adaptive [cap=600 s] | 22,485 (5 ms) | 35,445 (7 ms) | 56,181 (11 ms) |
| DOP853-adaptive | 7,023 (1 ms) | 9,291 (2 ms) | 10,479 (2 ms) |
| DOP853-adaptive [cap=600 s] | 7,023 (2 ms) | 9,291 (2 ms) | 10,479 (2 ms) |
| PEFRL | n/a | n/a | n/a |
| Y6 | n/a | n/a | n/a |
| Y6C | n/a | n/a | n/a |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | n/a | n/a | n/a |

Findings:
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.

## Earth LEO 400 km + 600 s prograde burn

Reference floor: 1.78E-005 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 26,148 (3 ms) | 53,028 (5 ms) | 107,568 (11 ms) |
| DOPRI5-fixed | 10,695 (1 ms) | 26,247 (3 ms) | 47,847 (6 ms) |
| DOPRI5-adaptive | 7,905 (1 ms) | 15,429 (2 ms) | 24,375 (3 ms) |
| DOPRI5-adaptive [cap=600 s] | 7,905 (1 ms) | 15,429 (2 ms) | 24,375 (3 ms) |
| DOP853-adaptive | 7,221 (1 ms) | 9,240 (1 ms) | 11,622 (2 ms) |
| DOP853-adaptive [cap=600 s] | 7,596 (1 ms) | 9,186 (1 ms) | 11,304 (2 ms) |
| PEFRL | n/a | n/a | n/a |
| Y6 | n/a | n/a | n/a |
| Y6C | n/a | n/a | n/a |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | n/a | n/a | n/a |

Findings:
- RK4: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 10 run(s); the step cap, not the tolerance, sets the cost there.
- TIE at 100 m: DOPRI5-adaptive (7,905), DOP853-adaptive (7,221) are within 10%; do not read the ranking as resolved.

## Earth-Moon flyby inside lunar SOI

Reference floor: not certified. Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 576 (0 ms) | 960 (0 ms) | 1,536 (0 ms) |
| DOPRI5-fixed | 435 (0 ms) | 723 (0 ms) | 1,011 (0 ms) |
| DOPRI5-adaptive | 705 (0 ms) | 813 (0 ms) | 1,227 (0 ms) |
| DOPRI5-adaptive [cap=600 s] | <=5,223 EDGE | <=5,223 EDGE | <=5,223 EDGE |
| DOP853-adaptive | <=435 EDGE | <=435 EDGE | 720 (0 ms) |
| DOP853-adaptive [cap=600 s] | <=10,443 EDGE | <=10,443 EDGE | <=10,443 EDGE |
| PEFRL | 672 (0 ms) | 1,056 (0 ms) | 2,016 (0 ms) |
| Y6 | 675 (0 ms) | 1,011 (0 ms) | 1,347 (0 ms) |
| Y6C | 675 (0 ms) | 1,011 (0 ms) | 1,347 (0 ms) |
| WH-Y6 | 899 (0 ms) | 1,347 (0 ms) | 1,795 (0 ms) |
| Leapfrog | 8,379 (1 ms) | 25,563 (4 ms) | 86,211 (12 ms) |

Findings:
- REFERENCE NOT CERTIFIED: every number of this scenario is provisional (no reference floor).
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOPRI5-adaptive: the uncapped branch reached only 1.35E-001 m while DOPRI5-adaptive [cap=600 s] reached 5.89E-005 m; the step cap is load-bearing on this horizon.
- DOP853-adaptive: the uncapped branch reached only 1.95E-001 m while DOP853-adaptive [cap=600 s] reached 3.01E-005 m; the step cap is load-bearing on this horizon.

## Earth LEO 400 km, 1000 orbits (~64 d)

Reference floor: 8.52E+000 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | not reached (best 1.11E+004) | skip | skip |
| DOPRI5-fixed | not reached (best 1.62E+003) | skip | skip |
| DOPRI5-adaptive | not reached (best 4.03E+002) | skip | skip |
| DOPRI5-adaptive [cap=600 s] | not reached (best 4.03E+002) | skip | skip |
| DOP853-adaptive | 1.14E+006 (175 ms) | skip | skip |
| DOP853-adaptive [cap=600 s] | 1.14E+006 (167 ms) | skip | skip |
| PEFRL | 4.15E+006 (592 ms) | skip | skip |
| Y6 | 2.39E+006 (361 ms) | skip | skip |
| Y6C | 2.39E+006 (341 ms) | skip | skip |
| WH-Y6 | 93,187 (13 ms) | skip | skip |
| Leapfrog | not reached (best 1.82E+005) | skip | skip |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 4 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive [cap=600 s]: 4 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Earth HEO 300 x 120000 km

Reference floor: 2.32E-005 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 331,200 (33 ms) | 549,120 (55 ms) | 1.01E+006 (102 ms) |
| DOPRI5-fixed | 148,323 (19 ms) | 221,763 (29 ms) | 367,203 (47 ms) |
| DOPRI5-adaptive | 9,237 (1 ms) | 14,529 (2 ms) | 22,197 (3 ms) |
| DOPRI5-adaptive [cap=600 s] | 16,671 (2 ms) | 19,839 (3 ms) | 25,635 (3 ms) |
| DOP853-adaptive | 6,729 (1 ms) | 7,575 (1 ms) | 9,060 (1 ms) |
| DOP853-adaptive [cap=600 s] | 27,114 (4 ms) | 27,432 (4 ms) | 28,137 (4 ms) |
| PEFRL | 270,720 (38 ms) | 496,320 (70 ms) | 912,000 (127 ms) |
| Y6 | 173,043 (24 ms) | 258,723 (36 ms) | 386,403 (54 ms) |
| Y6C | 173,043 (25 ms) | 258,723 (37 ms) | 386,403 (55 ms) |
| WH-Y6 | 8,963 (1 ms) | 13,443 (2 ms) | 20,163 (3 ms) |
| Leapfrog | not reached (best 8.29E+002) | not reached (best 8.29E+002) | not reached (best 8.29E+002) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 17 run(s); the step cap, not the tolerance, sets the cost there.
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Earth hyperbolic flyby e=1.5

Reference floor: 1.82E-006 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 26,688 (3 ms) | 48,960 (5 ms) | 89,856 (9 ms) |
| DOPRI5-fixed | 12,675 (2 ms) | 18,723 (2 ms) | 29,667 (4 ms) |
| DOPRI5-adaptive | 795 (0 ms) | 1,065 (0 ms) | 1,569 (0 ms) |
| DOPRI5-adaptive [cap=600 s] | 2,883 (0 ms) | 2,973 (0 ms) | 3,297 (0 ms) |
| DOP853-adaptive | 939 (0 ms) | 1,047 (0 ms) | 1,191 (0 ms) |
| DOP853-adaptive [cap=600 s] | 5,331 (1 ms) | 5,367 (1 ms) | 5,403 (1 ms) |
| PEFRL | 22,848 (3 ms) | 39,936 (6 ms) | 73,344 (10 ms) |
| Y6 | 21,843 (3 ms) | 31,251 (4 ms) | 46,707 (7 ms) |
| Y6C | 21,843 (3 ms) | 31,251 (4 ms) | 46,707 (7 ms) |
| WH-Y6 | <=451 EDGE | 899 (0 ms) | 2,243 (0 ms) |
| Leapfrog | 423,891 (60 ms) | 1.29E+006 (181 ms) | 4.36E+006 (649 ms) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 14 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 13 run(s); the step cap, not the tolerance, sets the cost there.
- WH-Y6: 1 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.

## Earth LEO 400 km, 30 d

Reference floor: 2.54E-003 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 3.95E+006 (391 ms) | not reached (best 4.35E+001) | not reached (best 4.35E+001) |
| DOPRI5-fixed | 2.63E+006 (358 ms) | 4.37E+006 (573 ms) | not reached (best 6.29E+000) |
| DOPRI5-adaptive | 2.57E+006 (339 ms) | 4.08E+006 (537 ms) | not reached (best 2.82E+000) |
| DOPRI5-adaptive [cap=600 s] | 2.57E+006 (340 ms) | 4.08E+006 (540 ms) | not reached (best 2.82E+000) |
| DOP853-adaptive | 405,579 (62 ms) | 534,603 (82 ms) | 709,707 (101 ms) |
| DOP853-adaptive [cap=600 s] | 405,579 (58 ms) | 534,603 (76 ms) | 709,707 (101 ms) |
| PEFRL | 1.59E+006 (220 ms) | 2.91E+006 (416 ms) | not reached (best 1.44E+000) |
| Y6 | 1.01E+006 (142 ms) | 1.51E+006 (212 ms) | 2.27E+006 (319 ms) |
| Y6C | 1.01E+006 (156 ms) | 1.51E+006 (216 ms) | 2.27E+006 (324 ms) |
| WH-Y6 | 43,011 (6 ms) | 50,179 (7 ms) | 57,347 (8 ms) |
| Leapfrog | not reached (best 1.68E+004) | not reached (best 1.68E+004) | not reached (best 1.68E+004) |

Findings:
- RK4: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-fixed: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive: 3 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- DOPRI5-adaptive [cap=600 s]: 3 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- PEFRL: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Earth LEO 400 km + unaligned 600 s burn

Reference floor: 2.51E-005 m. Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 31,932 (3 ms) | 53,040 (5 ms) | 97,176 (10 ms) |
| DOPRI5-fixed | 17,643 (2 ms) | 28,857 (4 ms) | 47,901 (6 ms) |
| DOPRI5-adaptive | 7,797 (1 ms) | 19,119 (3 ms) | 30,171 (4 ms) |
| DOPRI5-adaptive [cap=600 s] | 7,797 (1 ms) | 19,119 (3 ms) | 30,171 (4 ms) |
| DOP853-adaptive | 7,422 (1 ms) | 9,240 (1 ms) | 11,520 (2 ms) |
| DOP853-adaptive [cap=600 s] | 7,596 (1 ms) | 9,219 (1 ms) | 11,409 (2 ms) |
| PEFRL | n/a | n/a | n/a |
| Y6 | n/a | n/a | n/a |
| Y6C | n/a | n/a | n/a |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | n/a | n/a | n/a |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 10 run(s); the step cap, not the tolerance, sets the cost there.
- TIE at 100 m: DOPRI5-adaptive (7,797), DOP853-adaptive (7,422) are within 10%; do not read the ranking as resolved.
