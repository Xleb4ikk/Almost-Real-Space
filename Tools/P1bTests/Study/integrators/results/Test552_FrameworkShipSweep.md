# Integrator comparison

Cost = RHS-equivalent calls needed to reach the error target (log-log interpolation between the two bracketing runs; never extrapolated).
`EDGE` = the coarsest tested point was already below the target (cost is an upper bound, not a result). `not reached` = the finest run (or the RHS budget) stayed above the target; the parenthesised value is the best error it did reach. `n/a` = method not valid for this problem.
Build: Release. Per-run RHS budget: 2,000,000.

## Who wins where

| scenario | 100 m | 10 m | 1 m |
|---|---|---|---|
| Earth LEO 400 km circular | WH-Y6 (522) | WH-Y6 (709) | WH-Y6 (883) |
| Earth LEO 400 km polar | WH-Y6 (444) | WH-Y6 (533) | WH-Y6 (662) |
| Earth LEO 400 km sun-synchronous | WH-Y6 (2,014) | DOP853-adaptive (2,530) | DOP853-adaptive (3,241) |
| Earth GTO 400 x 40000 km | WH-Y6 (893) | WH-Y6 (1,690) | WH-Y6 (3,038) |
| Earth Molniya 500 x 40000 km | WH-Y6 (3,843) | WH-Y6 (8,621) | WH-Y6 (12,094) |
| Earth GEO 35786 km | WH-Y6 (2,088) | WH-Y6 (2,749) | WH-Y6 (3,913) |
| Sun 1 AU circular | DOP853-adaptive (324) | DOP853-adaptive (409) | DOP853-adaptive (600) |
| Sun 0.5 x 2 AU eccentric | DOP853-adaptive (417) | DOP853-adaptive (534) | DOP853-adaptive (677) |
| Earth 400 km + J2 | WH-Y6 (747) | WH-Y6 (1,105) | WH-Y6 (1,628) |
| Earth 250 km + atmosphere drag | DOP853-adaptive (2,097) | DOP853-adaptive (2,735) | DOP853-adaptive (3,354) |
| Earth LEO 400 km + 600 s prograde burn | DOP853-adaptive (2,278) | DOP853-adaptive [cap=600 s] (2,896) | DOP853-adaptive [cap=600 s] (3,510) |
| Earth-Moon flyby inside lunar SOI (ref. NOT certified) | DOPRI5-fixed (145) | DOP853-adaptive (<= 145) EDGE | DOP853-adaptive (228) |
| Earth LEO 400 km, 1000 orbits (~64 d) | WH-Y6 (40,167) | - | - |
| Earth HEO 300 x 120000 km | DOP853-adaptive (2,097) | DOP853-adaptive (2,488) | DOP853-adaptive (2,912) |
| Earth hyperbolic flyby e=1.5 | DOPRI5-adaptive (258) | DOPRI5-adaptive (335) | DOP853-adaptive (382) |
| Earth LEO 400 km, 30 d, world chunk 1 d | WH-Y6 (13,171) | WH-Y6 (16,492) | WH-Y6 (21,763) |
| Earth LEO 400 km + unaligned 600 s burn | DOPRI5-adaptive (3,553) | DOP853-adaptive [cap=600 s] (4,222) | DOP853-adaptive [cap=600 s] (4,793) |

## Earth LEO 400 km circular

Reference floor: 5.02E-006 m. Kepler-solve weight: 1.29 RHS. RHS multiplier: 1.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 11,051 (4 ms) | 18,024 (6 ms) | 29,730 (11 ms) |
| DOPRI5-fixed | 6,638 (3 ms) | 11,034 (6 ms) | 17,776 (9 ms) |
| DOPRI5-adaptive | 3,651 (2 ms) | 11,107 (6 ms) | 17,812 (10 ms) |
| DOPRI5-adaptive [cap=600 s] | 3,651 (1 ms) | 11,107 (4 ms) | 17,812 (7 ms) |
| DOP853-adaptive | 2,076 (1 ms) | 2,533 (1 ms) | 3,256 (1 ms) |
| DOP853-adaptive [cap=600 s] | 2,076 (1 ms) | 2,533 (1 ms) | 3,256 (1 ms) |
| PEFRL | 7,195 (3 ms) | 12,795 (6 ms) | 22,753 (10 ms) |
| Y6 | 6,107 (3 ms) | 8,968 (4 ms) | 13,165 (6 ms) |
| Y6C | 6,107 (3 ms) | 8,968 (4 ms) | 13,165 (6 ms) |
| WH-Y6 | 522 (0 ms) | 709 (0 ms) | 883 (0 ms) |
| Leapfrog | 146,093 (63 ms) | 461,985 (199 ms) | 1.46E+006 (649 ms) |

Findings:
- DOPRI5-adaptive: 1 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.

## Earth LEO 400 km polar

Reference floor: 1.46E-006 m. Kepler-solve weight: 0.84 RHS. RHS multiplier: 1.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 11,051 (3 ms) | 18,024 (5 ms) | 29,730 (9 ms) |
| DOPRI5-fixed | 6,638 (3 ms) | 11,034 (4 ms) | 17,776 (7 ms) |
| DOPRI5-adaptive | 3,651 (1 ms) | 11,107 (5 ms) | 17,812 (7 ms) |
| DOPRI5-adaptive [cap=600 s] | 3,651 (1 ms) | 11,107 (5 ms) | 17,812 (7 ms) |
| DOP853-adaptive | 2,076 (1 ms) | 2,533 (1 ms) | 3,256 (1 ms) |
| DOP853-adaptive [cap=600 s] | 2,076 (1 ms) | 2,533 (1 ms) | 3,256 (1 ms) |
| PEFRL | 7,195 (3 ms) | 12,795 (5 ms) | 22,753 (10 ms) |
| Y6 | 6,107 (3 ms) | 8,968 (4 ms) | 13,165 (6 ms) |
| Y6C | 6,107 (3 ms) | 8,968 (4 ms) | 13,165 (6 ms) |
| WH-Y6 | 444 (0 ms) | 533 (0 ms) | 662 (0 ms) |
| Leapfrog | 146,093 (64 ms) | 461,983 (202 ms) | 1.46E+006 (638 ms) |

Findings:
- DOPRI5-adaptive: 1 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.

## Earth LEO 400 km sun-synchronous

Reference floor: 2.62E-006 m. Kepler-solve weight: 0.55 RHS. RHS multiplier: 1.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 11,061 (4 ms) | 18,042 (6 ms) | 29,763 (10 ms) |
| DOPRI5-fixed | 6,640 (3 ms) | 11,038 (5 ms) | 17,783 (7 ms) |
| DOPRI5-adaptive | 3,636 (2 ms) | 11,104 (5 ms) | 17,811 (8 ms) |
| DOPRI5-adaptive [cap=600 s] | 3,636 (2 ms) | 11,104 (5 ms) | 17,811 (8 ms) |
| DOP853-adaptive | 2,073 (1 ms) | 2,530 (1 ms) | 3,241 (1 ms) |
| DOP853-adaptive [cap=600 s] | 2,073 (1 ms) | 2,530 (1 ms) | 3,241 (1 ms) |
| PEFRL | 7,201 (3 ms) | 12,805 (6 ms) | 22,772 (10 ms) |
| Y6 | 6,111 (3 ms) | 8,974 (4 ms) | 13,174 (6 ms) |
| Y6C | 6,111 (3 ms) | 8,974 (4 ms) | 13,174 (6 ms) |
| WH-Y6 | 2,014 (1 ms) | 2,984 (1 ms) | 4,398 (2 ms) |
| Leapfrog | 146,296 (65 ms) | 462,629 (207 ms) | 1.46E+006 (654 ms) |

Findings:
- DOPRI5-adaptive: 1 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.

## Earth GTO 400 x 40000 km

Reference floor: 3.39E-006 m. Kepler-solve weight: 1.07 RHS. RHS multiplier: 1.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 28,804 (9 ms) | 49,572 (15 ms) | 86,215 (26 ms) |
| DOPRI5-fixed | 15,079 (6 ms) | 24,439 (10 ms) | 38,849 (16 ms) |
| DOPRI5-adaptive | 2,897 (1 ms) | 4,721 (2 ms) | 7,476 (3 ms) |
| DOPRI5-adaptive [cap=600 s] | 3,282 (1 ms) | 4,721 (2 ms) | 7,476 (3 ms) |
| DOP853-adaptive | 2,352 (1 ms) | 2,841 (1 ms) | 3,518 (2 ms) |
| DOP853-adaptive [cap=600 s] | 4,200 (2 ms) | 4,489 (2 ms) | 4,860 (2 ms) |
| PEFRL | 23,894 (10 ms) | 42,487 (18 ms) | 75,550 (32 ms) |
| Y6 | 17,019 (7 ms) | 24,985 (11 ms) | 36,676 (16 ms) |
| Y6C | 17,019 (8 ms) | 24,985 (11 ms) | 36,675 (16 ms) |
| WH-Y6 | 893 (0 ms) | 1,690 (1 ms) | 3,038 (1 ms) |
| Leapfrog | 889,065 (387 ms) | not reached (best 4.04E+001) | not reached (best 4.04E+001) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 10 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 16 run(s); the step cap, not the tolerance, sets the cost there.
- WH-Y6: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Earth Molniya 500 x 40000 km

Reference floor: 9.52E-005 m. Kepler-solve weight: 1.13 RHS. RHS multiplier: 1.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 146,362 (42 ms) | 246,084 (74 ms) | 419,662 (127 ms) |
| DOPRI5-fixed | 84,771 (34 ms) | 136,142 (54 ms) | 216,618 (87 ms) |
| DOPRI5-adaptive | 14,844 (6 ms) | 25,587 (11 ms) | 40,733 (17 ms) |
| DOPRI5-adaptive [cap=600 s] | 15,649 (6 ms) | 25,587 (11 ms) | 40,733 (17 ms) |
| DOP853-adaptive | 10,285 (5 ms) | 12,933 (6 ms) | 14,741 (7 ms) |
| DOP853-adaptive [cap=600 s] | 15,615 (7 ms) | 17,115 (8 ms) | 18,314 (8 ms) |
| PEFRL | 111,444 (48 ms) | 198,179 (85 ms) | 352,442 (152 ms) |
| Y6 | 71,607 (31 ms) | 105,117 (46 ms) | 154,249 (67 ms) |
| Y6C | 71,607 (32 ms) | 105,116 (47 ms) | 154,299 (68 ms) |
| WH-Y6 | 3,843 (1 ms) | 8,621 (3 ms) | 12,094 (4 ms) |
| Leapfrog | not reached (best 1.59E+003) | not reached (best 1.59E+003) | not reached (best 1.59E+003) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 10 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 18 run(s); the step cap, not the tolerance, sets the cost there.
- WH-Y6: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Earth GEO 35786 km

Reference floor: 1.80E-004 m. Kepler-solve weight: 0.69 RHS. RHS multiplier: 1.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 39,920 (12 ms) | 65,039 (20 ms) | 107,259 (32 ms) |
| DOPRI5-fixed | 25,500 (10 ms) | 40,840 (16 ms) | 64,977 (26 ms) |
| DOPRI5-adaptive | 25,564 (11 ms) | 40,949 (17 ms) | 65,032 (27 ms) |
| DOPRI5-adaptive [cap=600 s] | <=25,933 EDGE | 40,949 (17 ms) | 65,032 (27 ms) |
| DOP853-adaptive | 5,695 (3 ms) | 7,109 (3 ms) | 9,004 (4 ms) |
| DOP853-adaptive [cap=600 s] | <=51,865 EDGE | <=51,865 EDGE | <=51,865 EDGE |
| PEFRL | 25,803 (11 ms) | 45,884 (20 ms) | 81,605 (35 ms) |
| Y6 | 17,822 (8 ms) | 26,164 (11 ms) | 38,406 (17 ms) |
| Y6C | 17,822 (8 ms) | 26,164 (12 ms) | 38,405 (17 ms) |
| WH-Y6 | 2,088 (1 ms) | 2,749 (1 ms) | 3,913 (1 ms) |
| Leapfrog | 972,328 (428 ms) | not reached (best 5.18E+001) | not reached (best 5.18E+001) |

Findings:
- DOPRI5-adaptive: 1 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 22 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Sun 1 AU circular

Reference floor: 5.31E-004 m. Kepler-solve weight: 0.71 RHS. RHS multiplier: 1.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 3,093 (1 ms) | 5,479 (2 ms) | 9,719 (3 ms) |
| DOPRI5-fixed | 1,029 (0 ms) | 1,714 (1 ms) | 2,781 (1 ms) |
| DOPRI5-adaptive | 1,074 (0 ms) | 1,751 (1 ms) | 2,807 (1 ms) |
| DOPRI5-adaptive [cap=600 s] | <=315,601 EDGE | <=315,601 EDGE | <=315,601 EDGE |
| DOP853-adaptive | 324 (0 ms) | 409 (0 ms) | 600 (0 ms) |
| DOP853-adaptive [cap=600 s] | <=631,201 EDGE | <=631,201 EDGE | <=631,201 EDGE |
| PEFRL | 2,842 (1 ms) | 5,052 (2 ms) | 8,968 (4 ms) |
| Y6 | 1,318 (1 ms) | 1,935 (1 ms) | 2,840 (1 ms) |
| Y6C | 1,318 (1 ms) | 1,935 (1 ms) | 2,839 (1 ms) |
| WH-Y6 | <=1,151 EDGE | <=1,151 EDGE | <=1,151 EDGE |
| Leapfrog | not reached (best 1.60E+003) | not reached (best 1.60E+003) | not reached (best 1.60E+003) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.

## Sun 0.5 x 2 AU eccentric

Reference floor: 9.18E-004 m. Kepler-solve weight: 1.08 RHS. RHS multiplier: 1.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 9,931 (3 ms) | 17,615 (5 ms) | 31,349 (10 ms) |
| DOPRI5-fixed | 3,383 (1 ms) | 5,275 (2 ms) | 8,259 (3 ms) |
| DOPRI5-adaptive | 1,012 (0 ms) | 1,514 (1 ms) | 2,315 (1 ms) |
| DOPRI5-adaptive [cap=600 s] | <=315,601 EDGE | <=315,601 EDGE | <=315,601 EDGE |
| DOP853-adaptive | 417 (0 ms) | 534 (0 ms) | 677 (0 ms) |
| DOP853-adaptive [cap=600 s] | <=631,201 EDGE | <=631,201 EDGE | <=631,201 EDGE |
| PEFRL | 8,731 (4 ms) | 15,522 (7 ms) | 27,439 (12 ms) |
| Y6 | 3,996 (2 ms) | 5,864 (3 ms) | 8,629 (4 ms) |
| Y6C | 3,996 (2 ms) | 5,865 (3 ms) | 8,610 (4 ms) |
| WH-Y6 | <=1,396 EDGE | <=1,396 EDGE | <=1,396 EDGE |
| Leapfrog | not reached (best 1.97E+003) | not reached (best 1.97E+003) | not reached (best 1.97E+003) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.

## Earth 400 km + J2

Reference floor: 2.57E-006 m. Kepler-solve weight: 0.64 RHS. RHS multiplier: 1.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 11,099 (3 ms) | 18,098 (5 ms) | 29,845 (10 ms) |
| DOPRI5-fixed | 6,678 (3 ms) | 11,095 (5 ms) | 17,873 (7 ms) |
| DOPRI5-adaptive | 4,216 (2 ms) | 11,135 (5 ms) | 17,941 (8 ms) |
| DOPRI5-adaptive [cap=600 s] | 4,216 (2 ms) | 11,135 (5 ms) | 17,941 (8 ms) |
| DOP853-adaptive | 2,083 (1 ms) | 2,543 (1 ms) | 3,267 (1 ms) |
| DOP853-adaptive [cap=600 s] | 2,083 (1 ms) | 2,543 (1 ms) | 3,267 (1 ms) |
| PEFRL | 7,221 (3 ms) | 12,841 (6 ms) | 22,836 (10 ms) |
| Y6 | 6,122 (3 ms) | 8,990 (4 ms) | 13,197 (6 ms) |
| Y6C | 6,122 (3 ms) | 8,990 (4 ms) | 13,197 (6 ms) |
| WH-Y6 | 747 (0 ms) | 1,105 (0 ms) | 1,628 (1 ms) |
| Leapfrog | 146,788 (65 ms) | 464,183 (208 ms) | 1.47E+006 (657 ms) |

Findings:
- DOPRI5-adaptive: 1 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.

## Earth 250 km + atmosphere drag

Reference floor: 1.92E-005 m. Kepler-solve weight: 0.13 RHS. RHS multiplier: 1.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 11,582 (6 ms) | 18,922 (10 ms) | 31,278 (17 ms) |
| DOPRI5-fixed | 6,942 (4 ms) | 11,528 (7 ms) | 18,563 (11 ms) |
| DOPRI5-adaptive | 4,842 (3 ms) | 11,593 (7 ms) | 18,659 (12 ms) |
| DOPRI5-adaptive [cap=600 s] | 4,842 (3 ms) | 11,593 (7 ms) | 18,659 (12 ms) |
| DOP853-adaptive | 2,097 (1 ms) | 2,735 (2 ms) | 3,354 (2 ms) |
| DOP853-adaptive [cap=600 s] | 2,097 (1 ms) | 2,735 (2 ms) | 3,354 (2 ms) |
| PEFRL | n/a | n/a | n/a |
| Y6 | n/a | n/a | n/a |
| Y6C | n/a | n/a | n/a |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | n/a | n/a | n/a |

Findings:
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.

## Earth LEO 400 km + 600 s prograde burn

Reference floor: 1.78E-005 m. Kepler-solve weight: 0.76 RHS. RHS multiplier: 1.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 7,781 (2 ms) | 12,201 (3 ms) | 44,949 (14 ms) |
| DOPRI5-fixed | 4,337 (2 ms) | 8,581 (3 ms) | 14,950 (6 ms) |
| DOPRI5-adaptive | 2,595 (1 ms) | 4,610 (2 ms) | 7,746 (3 ms) |
| DOPRI5-adaptive [cap=600 s] | 2,595 (1 ms) | 4,610 (2 ms) | 7,746 (3 ms) |
| DOP853-adaptive | 2,278 (1 ms) | 2,896 (1 ms) | 3,582 (2 ms) |
| DOP853-adaptive [cap=600 s] | 2,456 (1 ms) | 2,896 (1 ms) | 3,510 (2 ms) |
| PEFRL | n/a | n/a | n/a |
| Y6 | n/a | n/a | n/a |
| Y6C | n/a | n/a | n/a |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | n/a | n/a | n/a |

Findings:
- RK4: error GROWS as h shrinks in 2 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 10 run(s); the step cap, not the tolerance, sets the cost there.

## Earth-Moon flyby inside lunar SOI

Reference floor: not certified. Kepler-solve weight: 1.09 RHS. RHS multiplier: 1.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 173 (0 ms) | 292 (0 ms) | 507 (0 ms) |
| DOPRI5-fixed | 145 (0 ms) | 210 (0 ms) | 306 (0 ms) |
| DOPRI5-adaptive | 203 (0 ms) | 267 (0 ms) | 367 (0 ms) |
| DOPRI5-adaptive [cap=600 s] | <=1,741 EDGE | <=1,741 EDGE | <=1,741 EDGE |
| DOP853-adaptive | <=145 EDGE | <=145 EDGE | 228 (0 ms) |
| DOP853-adaptive [cap=600 s] | <=3,481 EDGE | <=3,481 EDGE | <=3,481 EDGE |
| PEFRL | 195 (0 ms) | 346 (0 ms) | 616 (0 ms) |
| Y6 | 196 (0 ms) | 288 (0 ms) | 422 (0 ms) |
| Y6C | 196 (0 ms) | 288 (0 ms) | 422 (0 ms) |
| WH-Y6 | 408 (0 ms) | 600 (0 ms) | 880 (0 ms) |
| Leapfrog | 2,645 (1 ms) | 8,361 (4 ms) | 26,438 (11 ms) |

Findings:
- REFERENCE NOT CERTIFIED: every number of this scenario is provisional (no reference floor).
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.

## Earth LEO 400 km, 1000 orbits (~64 d)

Reference floor: 8.52E+000 m. Kepler-solve weight: 0.72 RHS. RHS multiplier: 1.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | not reached (best 1.11E+004) | skip | skip |
| DOPRI5-fixed | not reached (best 1.62E+003) | skip | skip |
| DOPRI5-adaptive | not reached (best 4.03E+002) | skip | skip |
| DOPRI5-adaptive [cap=600 s] | not reached (best 4.03E+002) | skip | skip |
| DOP853-adaptive | 334,011 (149 ms) | skip | skip |
| DOP853-adaptive [cap=600 s] | 334,011 (149 ms) | skip | skip |
| PEFRL | 1.30E+006 (569 ms) | skip | skip |
| Y6 | 783,796 (345 ms) | skip | skip |
| Y6C | 783,793 (351 ms) | skip | skip |
| WH-Y6 | 40,167 (13 ms) | skip | skip |
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

Reference floor: 2.32E-005 m. Kepler-solve weight: 1.07 RHS. RHS multiplier: 1.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 101,022 (31 ms) | 177,142 (51 ms) | 312,438 (96 ms) |
| DOPRI5-fixed | 46,445 (19 ms) | 73,158 (30 ms) | 114,734 (47 ms) |
| DOPRI5-adaptive | 2,995 (1 ms) | 4,613 (2 ms) | 6,913 (3 ms) |
| DOPRI5-adaptive [cap=600 s] | 5,517 (2 ms) | 6,503 (3 ms) | 8,232 (3 ms) |
| DOP853-adaptive | 2,097 (1 ms) | 2,488 (1 ms) | 2,912 (1 ms) |
| DOP853-adaptive [cap=600 s] | 8,967 (4 ms) | 9,128 (4 ms) | 9,326 (4 ms) |
| PEFRL | 88,663 (39 ms) | 157,661 (69 ms) | 280,356 (122 ms) |
| Y6 | 56,030 (24 ms) | 82,248 (36 ms) | 120,728 (53 ms) |
| Y6C | 56,030 (25 ms) | 82,248 (36 ms) | 120,729 (53 ms) |
| WH-Y6 | 5,221 (2 ms) | 7,239 (2 ms) | 9,028 (3 ms) |
| Leapfrog | not reached (best 8.29E+002) | not reached (best 8.29E+002) | not reached (best 8.29E+002) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 17 run(s); the step cap, not the tolerance, sets the cost there.
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Earth hyperbolic flyby e=1.5

Reference floor: 1.82E-006 m. Kepler-solve weight: 3.42 RHS. RHS multiplier: 1.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 8,859 (3 ms) | 15,629 (5 ms) | 27,666 (8 ms) |
| DOPRI5-fixed | 3,958 (2 ms) | 6,172 (2 ms) | 9,497 (4 ms) |
| DOPRI5-adaptive | 258 (0 ms) | 335 (0 ms) | 459 (0 ms) |
| DOPRI5-adaptive [cap=600 s] | 938 (0 ms) | 988 (0 ms) | 1,079 (0 ms) |
| DOP853-adaptive | 307 (0 ms) | 349 (0 ms) | 382 (0 ms) |
| DOP853-adaptive [cap=600 s] | 1,777 (1 ms) | 1,783 (1 ms) | 1,801 (1 ms) |
| PEFRL | 7,396 (3 ms) | 13,147 (6 ms) | 23,375 (10 ms) |
| Y6 | 6,979 (3 ms) | 10,250 (4 ms) | 15,050 (7 ms) |
| Y6C | 6,979 (3 ms) | 10,250 (5 ms) | 15,050 (7 ms) |
| WH-Y6 | <=496 EDGE | 959 (0 ms) | 2,329 (0 ms) |
| Leapfrog | 135,173 (59 ms) | 427,452 (184 ms) | 1.35E+006 (591 ms) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 14 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 13 run(s); the step cap, not the tolerance, sets the cost there.
- WH-Y6: 1 run(s) had no finite samples (controller collapse or diverged state); excluded from ranking, finer controls were still tried.

## Earth LEO 400 km, 30 d, world chunk 1 d

Reference floor: 2.54E-003 m. Kepler-solve weight: 0.49 RHS. RHS multiplier: 1.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 1.23E+006 (369 ms) | not reached (best 4.35E+001) | not reached (best 4.35E+001) |
| DOPRI5-fixed | 835,444 (332 ms) | 1.33E+006 (528 ms) | not reached (best 6.29E+000) |
| DOPRI5-adaptive | 835,780 (345 ms) | 1.33E+006 (549 ms) | not reached (best 2.82E+000) |
| DOPRI5-adaptive [cap=600 s] | 835,780 (346 ms) | 1.33E+006 (550 ms) | not reached (best 2.82E+000) |
| DOP853-adaptive | 133,753 (59 ms) | 170,282 (75 ms) | 219,847 (97 ms) |
| DOP853-adaptive [cap=600 s] | 133,753 (59 ms) | 170,282 (75 ms) | 219,847 (100 ms) |
| PEFRL | 504,352 (220 ms) | 896,951 (396 ms) | not reached (best 1.44E+000) |
| Y6 | 322,735 (142 ms) | 473,821 (211 ms) | 695,054 (311 ms) |
| Y6C | 322,736 (144 ms) | 473,800 (212 ms) | 695,783 (313 ms) |
| WH-Y6 | 13,171 (5 ms) | 16,492 (6 ms) | 21,763 (8 ms) |
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

Reference floor: 2.34E-004 m. Kepler-solve weight: 0.73 RHS. RHS multiplier: 1.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | not reached (best 1.11E+004) | not reached (best 1.11E+004) | not reached (best 1.11E+004) |
| DOPRI5-fixed | not reached (best 1.30E+004) | not reached (best 1.30E+004) | not reached (best 1.30E+004) |
| DOPRI5-adaptive | 3,553 (1 ms) | 4,605 (2 ms) | 10,112 (4 ms) |
| DOPRI5-adaptive [cap=600 s] | 3,553 (1 ms) | 4,605 (2 ms) | 10,112 (4 ms) |
| DOP853-adaptive | 3,605 (2 ms) | 4,237 (2 ms) | 4,818 (2 ms) |
| DOP853-adaptive [cap=600 s] | 3,710 (2 ms) | 4,222 (2 ms) | 4,793 (2 ms) |
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
