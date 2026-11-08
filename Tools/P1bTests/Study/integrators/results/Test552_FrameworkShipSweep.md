# Integrator comparison

Cost = RHS-equivalent calls, log-log interpolated between the nearest cheaper above-target run and the first bracketing below-target run; never extrapolated. `*` marks interpolated cells; no star means the first actually executed point that passed the target (used when the bracket is wider than 1.5x in cost or its local order misses the declared order by more than 30%). `ms` is the wall-clock time interpolated on the same bracket; sub-millisecond values are timing noise, use RHS for the deterministic ranking.
`EDGE` = even the cheapest tested point was already below the target (cost is an upper bound). A cap-limited EDGE is exact; a non-cap EDGE is marked **unresolved**. `~` marks methods within 10% of the winner (interpolated cells) or one grid step (unbracketed cells): that ranking is not resolved. `not reached` = the finest run (or the RHS budget) stayed above the target. `below round-off floor` = the target is at or below the run's own accumulation noise. `n/a` = method not valid for this problem. Scenarios whose reference is not certified are listed in the detail but excluded from the summary.
Reference-floor rule: a target below 10 x the scenario's reference floor is skipped.
Build: Release. Per-run RHS budget: per scenario (see section headers).

## Who wins where - RHS-equivalent calls

| scenario | 100 m | 10 m | 1 m |
|---|---|---|---|
| Earth LEO 400 km circular | WH-BM14A (<= 912) EDGE **unresolved** | WH-Y6 (1,347) ~ WH-BM14A (1,824) [tie within 56%] | WH-Y6 (1,795) ~ WH-BM14A (1,824) [tie within 56%] |
| Earth LEO 400 km polar | WH-BM14A (<= 912) EDGE **unresolved** | WH-Y6 (1,347) ~ WH-BM14A (1,824) [tie within 56%] | WH-Y6 (1,795) ~ WH-BM14A (1,824) [tie within 56%] |
| Earth LEO 400 km sun-synchronous | RKN12(10) (2,655*) ~ WH-BM14A (2,736) [tie within 33%] | RKN12(10) (3,430*) ~ WH-BM14A (3,648) [tie within 33%] | RKN12(10) (4,236*) ~ WH-BM14A (4,560) [tie within 10%] |
| Earth GTO 400 x 40000 km | WH-BM14A (<= 1,767) EDGE **unresolved** | WH-BM14A (3,534) ~ WH-Y6 (4,343) ~ RKN12(10) (4,557*) [ties within 56%] | WH-Y6 (5,211) ~ RKN12(10) (5,227*) ~ WH-BM14A (5,301) [ties within 46%] |
| Earth Molniya 500 x 40000 km | WH-Y6 (11,987) ~ RKN12(10) (14,750*) ~ WH-BM14A (12,198) [ties within 33%] | WH-BM14A (12,198) | WH-BM14A (18,297) ~ WH-Y6 (26,967) ~ RKN12(10) (19,259*) [ties within 56%] |
| Earth GEO 35786 km | WH-Y6 (5,043) ~ WH-BM14A (5,130) [tie within 18%] | WH-BM14A (5,130) | WH-BM14A (6,840) |
| Sun 1 AU circular | RKN12(10) (<= 564) EDGE **unresolved** | RKN12(10) (<= 564) EDGE **unresolved** | RKN12(10) (678*) |
| Sun 0.5 x 2 AU eccentric | RKN12(10) (717*) | RKN12(10) (928*) | RKN12(10) (1,060*) |
| Earth 400 km + J2 | WH-BM14A (<= 912) EDGE **unresolved** | WH-BM14A (1,824) | WH-BM14A (1,824) |
| Earth 250 km + atmosphere drag | DOP853-adaptive (6,292*) | DOP853-adaptive (8,204*) | DOP853-adaptive (10,063*) |
| Earth LEO 400 km + 600 s prograde burn | DOP853-adaptive (6,834*) | DOP853-adaptive (8,688*) | DOP853-adaptive (10,529*) |
| Earth LEO 400 km, 1000 orbits (~64 d) | WH-BM14A (72,960) | - | - |
| Earth HEO 300 x 120000 km | WH-BM14A (<= 4,560) EDGE **unresolved** | RKN12(10) (6,010*) ~ DOP853-adaptive (7,463*) [tie within 33%] | RKN12(10) (6,626*) ~ DOP853-adaptive (8,736*) [tie within 56%] |
| Earth hyperbolic flyby e=1.5 | WH-Y6 (<= 451) EDGE **unresolved** | WH-Y6 (899) ~ DOPRI5-adaptive (1,004*) ~ DOP853-adaptive (1,047*) ~ RKN12(10) (1,074*) [ties within 56%] **unresolved** | WH-BM14A (<= 912) EDGE **unresolved** |
| Earth LEO 400 km, 30 d | WH-BM14A (29,184) ~ WH-Y6 (43,011) [tie within 56%] | WH-BM14A (43,776) ~ WH-Y6 (50,179) [tie within 18%] | WH-Y6 (57,347) ~ WH-BM14A (58,368) [tie within 18%] |
| Earth LEO 400 km + unaligned 600 s burn | DOP853-adaptive (7,214*) ~ DOPRI5-adaptive (7,718*) [tie within 10%] | DOP853-adaptive (9,041*) | DOP853-adaptive (11,067*) |

Not ranked (reference not certified; detail only): Earth-Moon flyby inside lunar SOI.

## Who wins where - wall-clock ms

| scenario | 100 m | 10 m | 1 m |
|---|---|---|---|
| Earth LEO 400 km circular | WH-BM14A (<= 0.157 ms) EDGE **unresolved** | WH-Y6 (0.225 ms) ~ WH-BM14A (0.324 ms) [tie within 56%] | WH-Y6 (0.298 ms) ~ WH-BM14A (0.324 ms) [tie within 33%] |
| Earth LEO 400 km polar | WH-BM14A (<= 0.129 ms) EDGE **unresolved** | WH-Y6 (0.185 ms) ~ WH-BM14A (0.259 ms) [tie within 56%] | WH-Y6 (0.245 ms) ~ WH-BM14A (0.259 ms) [tie within 56%] |
| Earth LEO 400 km sun-synchronous | RKN12(10) (0.365 ms*) ~ WH-BM14A (0.389 ms) [tie within 18%] | RKN12(10) (0.476 ms*) ~ WH-BM14A (0.518 ms) [tie within 10%] | RKN12(10) (0.504 ms*) |
| Earth GTO 400 x 40000 km | WH-BM14A (<= 0.263 ms) EDGE **unresolved** | WH-BM14A (0.525 ms) ~ WH-Y6 (0.658 ms) ~ RKN12(10) (0.613 ms*) [ties within 76%] | RKN12(10) (0.705 ms*) ~ WH-Y6 (0.784 ms) ~ WH-BM14A (0.778 ms) [ties within 33%] |
| Earth Molniya 500 x 40000 km | WH-BM14A (1.8 ms) ~ WH-Y6 (1.8 ms) ~ RKN12(10) (2.0 ms*) [ties within 97%] | WH-BM14A (1.8 ms) ~ WH-Y6 (2.7 ms) ~ RKN12(10) (2.4 ms*) [ties within 97%] | RKN12(10) (2.6 ms*) ~ WH-BM14A (2.7 ms) [tie within 46%] |
| Earth GEO 35786 km | WH-Y6 (0.688 ms) ~ RKN12(10) (0.894 ms*) ~ WH-BM14A (0.704 ms) [ties within 33%] | WH-BM14A (0.704 ms) ~ WH-Y6 (0.926 ms) [tie within 33%] | WH-BM14A (0.951 ms) |
| Sun 1 AU circular | RKN12(10) (<= 0.075 ms) EDGE **unresolved** | RKN12(10) (<= 0.075 ms) EDGE **unresolved** | RKN12(10) (0.091 ms*) |
| Sun 0.5 x 2 AU eccentric | RKN12(10) (0.095 ms*) | RKN12(10) (0.124 ms*) | RKN12(10) (0.141 ms*) |
| Earth 400 km + J2 | WH-BM14A (<= 0.133 ms) EDGE **unresolved** | WH-BM14A (0.262 ms) ~ WH-Y6 (0.453 ms) ~ RKN12(10) (0.451 ms*) [ties within 94%] | WH-BM14A (0.262 ms) |
| Earth 250 km + atmosphere drag | DOP853-adaptive (1.3 ms*) | DOP853-adaptive (1.7 ms*) | DOP853-adaptive (2.1 ms*) |
| Earth LEO 400 km + 600 s prograde burn | DOP853-adaptive (0.971 ms*) ~ DOPRI5-adaptive (1.0 ms*) [tie within 10%] | DOP853-adaptive (1.2 ms*) | DOP853-adaptive (1.5 ms*) |
| Earth LEO 400 km, 1000 orbits (~64 d) | WH-BM14A (10.1 ms) | - | - |
| Earth HEO 300 x 120000 km | WH-BM14A (<= 0.673 ms) EDGE **unresolved** | RKN12(10) (0.793 ms*) | RKN12(10) (0.890 ms*) ~ DOP853-adaptive (1.2 ms*) [tie within 46%] |
| Earth hyperbolic flyby e=1.5 | WH-Y6 (<= 0.085 ms) EDGE **unresolved** | DOPRI5-adaptive (0.131 ms*) ~ DOP853-adaptive (0.147 ms*) ~ WH-Y6 (0.175 ms) ~ RKN12(10) (0.142 ms*) [ties within 94%] | RKN12(10) (0.159 ms*) ~ DOPRI5-adaptive (0.180 ms*) ~ DOP853-adaptive (0.161 ms*) [ties within 18%] **unresolved** |
| Earth LEO 400 km, 30 d | WH-BM14A (4.0 ms) ~ WH-Y6 (5.9 ms) [tie within 64%] | WH-BM14A (6.1 ms) ~ WH-Y6 (6.8 ms) [tie within 33%] | WH-Y6 (7.8 ms) ~ WH-BM14A (8.1 ms) [tie within 18%] |
| Earth LEO 400 km + unaligned 600 s burn | DOPRI5-adaptive (1.0 ms*) ~ DOP853-adaptive (1.0 ms*) [tie within 10%] | DOP853-adaptive (1.3 ms*) | DOP853-adaptive (1.6 ms*) |

Not ranked (reference not certified; detail only): Earth-Moon flyby inside lunar SOI.

## Earth LEO 400 km circular

Reference floor: 5.02E-006 m (fixed DOP853 h=14.4397 s, ratio=328.63 order=8.36 (floor from finest disagreement), est 5.02E-006 m / 5.7E-009 m/s, finest d=5.02E-006 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 33,131* (3.8 ms) | 54,021* (6.0 ms) | 89,126* (10.3 ms) |
| DOPRI5-fixed | 19,948* (3.0 ms) | 33,160* (4.2 ms) | 53,370* (6.8 ms) |
| DOPRI5-adaptive | 10,952* (1.6 ms) | 33,320* (4.7 ms) | 53,436* (7.6 ms) |
| DOPRI5-adaptive [cap=600 s] | 10,952* (1.6 ms) | 33,320* (4.7 ms) | 53,436* (7.6 ms) |
| DOP853-adaptive | 6,229* (0.880 ms) | 7,600* (1.1 ms) | 9,769* (1.4 ms) |
| DOP853-adaptive [cap=600 s] | 6,229* (0.878 ms) | 7,600* (1.1 ms) | 9,769* (1.4 ms) |
| PEFRL | 21,584* (3.0 ms) | 38,385* (5.3 ms) | 68,260* (9.6 ms) |
| Y6 | 18,321* (2.9 ms) | 26,903* (4.3 ms) | 39,496* (6.2 ms) |
| Y6C | 18,321* (2.6 ms) | 26,903* (3.8 ms) | 39,496* (5.6 ms) |
| WH-Y6 | 1,347 (0.225 ms) | 1,347 (0.225 ms) | 1,795 (0.298 ms) |
| Leapfrog | 438,279* (60.4 ms) | 1.39E+006* (191.7 ms) | 4.38E+006* (605.9 ms) |
| RKN12(10) | 2,655* (0.463 ms) | 3,466* (0.595 ms) | 4,035* (0.687 ms) |
| RKN12(10) [cap=600 s] | <=7,500 EDGE | <=7,500 EDGE | <=7,500 EDGE |
| IAS15 | 8,575* (1.8 ms) | 8,628* (1.8 ms) | 11,025* (2.3 ms) |
| IAS15 [cap=600 s] | <=28,206 EDGE | <=28,206 EDGE | <=28,206 EDGE |
| SRKN14A | 7,252* (1.1 ms) | 10,660* (1.5 ms) | 15,658* (2.3 ms) |
| WH-BM14A | <=912 EDGE | 1,824 (0.340 ms) | 1,824 (0.340 ms) |
| GJ8 | 4,332 (0.933 ms) | 4,510* (0.962 ms) | 5,183* (1.1 ms) |
| Encke-IAS15 | <=127,660 EDGE | <=127,660 EDGE | <=127,660 EDGE |

Findings:
- DOPRI5-adaptive: 1 run(s) did not complete the horizon: aborted at t=2.413E+005 s: scaled error stayed above 1 at the smallest step; step floor 8.640E-010 s reached at t=2.413E+005 s (last scaled error 2.664E+001); worst error at completed checkpoints 1.20E+007 m; excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 19 run(s); the step cap, not the tolerance, sets the cost there.
- RKN12(10) [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- RKN12(10): the uncapped branch reached only 1.75E-001 m while RKN12(10) [cap=600 s] reached 8.02E-005 m; the step cap is load-bearing on this horizon.
- TIE at 10 m: WH-Y6 (1,347), WH-BM14A (1,824) are within 56%; do not read the ranking as resolved.
- TIE at 1 m: WH-Y6 (1,795), WH-BM14A (1,824) are within 33%; do not read the ranking as resolved.

## Earth LEO 400 km polar

Reference floor: 1.46E-006 m (fixed DOP853 h=14.4397 s, ratio=336.92 order=8.40 (floor from finest disagreement), est 1.46E-006 m / 1.6E-009 m/s, finest d=1.46E-006 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 33,131* (3.4 ms) | 54,021* (5.3 ms) | 89,127* (9.3 ms) |
| DOPRI5-fixed | 19,948* (2.5 ms) | 33,160* (4.2 ms) | 53,370* (6.8 ms) |
| DOPRI5-adaptive | 10,952* (1.5 ms) | 33,320* (4.5 ms) | 53,436* (7.2 ms) |
| DOPRI5-adaptive [cap=600 s] | 10,952* (1.5 ms) | 33,320* (4.5 ms) | 53,436* (7.2 ms) |
| DOP853-adaptive | 6,229* (0.914 ms) | 7,600* (1.1 ms) | 9,769* (1.4 ms) |
| DOP853-adaptive [cap=600 s] | 6,229* (0.906 ms) | 7,600* (1.1 ms) | 9,769* (1.4 ms) |
| PEFRL | 21,584* (3.0 ms) | 38,385* (5.3 ms) | 68,260* (9.5 ms) |
| Y6 | 18,321* (2.5 ms) | 26,903* (3.8 ms) | 39,496* (5.5 ms) |
| Y6C | 18,321* (2.6 ms) | 26,903* (3.8 ms) | 39,496* (5.6 ms) |
| WH-Y6 | 1,347 (0.185 ms) | 1,347 (0.185 ms) | 1,795 (0.246 ms) |
| Leapfrog | 438,279* (61.0 ms) | 1.39E+006* (195.3 ms) | 4.38E+006* (609.4 ms) |
| RKN12(10) | 2,655* (0.355 ms) | 3,519* (0.471 ms) | 4,084* (0.545 ms) |
| RKN12(10) [cap=600 s] | <=7,500 EDGE | <=7,500 EDGE | <=7,500 EDGE |
| IAS15 | 8,587* (1.5 ms) | 8,646* (1.5 ms) | 11,060* (1.9 ms) |
| IAS15 [cap=600 s] | <=28,038 EDGE | <=28,038 EDGE | <=28,038 EDGE |
| SRKN14A | 7,252* (1.0 ms) | 10,660* (1.5 ms) | 15,658* (2.2 ms) |
| WH-BM14A | <=912 EDGE | 1,824 (0.262 ms) | 1,824 (0.262 ms) |
| GJ8 | 4,395 (0.681 ms) | 4,541* (0.676 ms) | 5,181* (0.761 ms) |
| Encke-IAS15 | <=114,544 EDGE | <=114,544 EDGE | <=114,544 EDGE |

Findings:
- DOPRI5-adaptive: 1 run(s) did not complete the horizon: aborted at t=2.413E+005 s: scaled error stayed above 1 at the smallest step; step floor 8.640E-010 s reached at t=2.413E+005 s (last scaled error 2.664E+001); worst error at completed checkpoints 1.20E+007 m; excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 19 run(s); the step cap, not the tolerance, sets the cost there.
- RKN12(10) [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- RKN12(10): the uncapped branch reached only 1.81E-001 m while RKN12(10) [cap=600 s] reached 7.39E-005 m; the step cap is load-bearing on this horizon.
- TIE at 1 m: WH-Y6 (1,795), WH-BM14A (1,824) are within 33%; do not read the ranking as resolved.

## Earth LEO 400 km sun-synchronous

Reference floor: 2.62E-006 m (fixed DOP853 h=14.4397 s, ratio=307.53 order=8.26 (floor from finest disagreement), est 2.62E-006 m / 2.9E-009 m/s, finest d=2.62E-006 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 33,161* (3.6 ms) | 54,075* (5.6 ms) | 89,226* (9.7 ms) |
| DOPRI5-fixed | 19,952* (2.6 ms) | 33,172* (4.4 ms) | 53,391* (7.0 ms) |
| DOPRI5-adaptive | 10,908* (1.5 ms) | 33,312* (4.7 ms) | 53,433* (7.4 ms) |
| DOPRI5-adaptive [cap=600 s] | 10,908* (1.5 ms) | 33,312* (4.7 ms) | 53,433* (7.5 ms) |
| DOP853-adaptive | 6,220* (0.934 ms) | 7,591* (1.1 ms) | 9,722* (1.5 ms) |
| DOP853-adaptive [cap=600 s] | 6,220* (0.932 ms) | 7,591* (1.1 ms) | 9,722* (1.5 ms) |
| PEFRL | 21,602* (3.1 ms) | 38,416* (5.5 ms) | 68,315* (9.7 ms) |
| Y6 | 18,334* (2.6 ms) | 26,921* (3.9 ms) | 39,522* (5.7 ms) |
| Y6C | 18,334* (2.7 ms) | 26,921* (3.9 ms) | 39,522* (5.8 ms) |
| WH-Y6 | 5,379 (0.777 ms) | 8,067 (1.2 ms) | 11,651 (1.7 ms) |
| Leapfrog | 438,889* (63.5 ms) | 1.39E+006* (198.3 ms) | 4.39E+006* (632.5 ms) |
| RKN12(10) | 2,655* (0.367 ms) | 3,430* (0.474 ms) | 4,236* (0.581 ms) |
| RKN12(10) [cap=600 s] | <=7,500 EDGE | <=7,500 EDGE | <=7,500 EDGE |
| IAS15 | 11,532* (2.1 ms) | 11,971* (2.1 ms) | 12,427* (2.2 ms) |
| IAS15 [cap=600 s] | <=28,185 EDGE | <=28,185 EDGE | <=28,185 EDGE |
| SRKN14A | 7,251* (1.0 ms) | 10,659* (1.5 ms) | 15,657* (2.2 ms) |
| WH-BM14A | 2,736 (0.392 ms) | 3,648 (0.522 ms) | 4,560 (0.649 ms) |
| GJ8 | 4,455 (0.680 ms) | 5,063* (0.771 ms) | 5,603* (0.825 ms) |
| Encke-IAS15 | <=106,284 EDGE | <=106,284 EDGE | <=106,284 EDGE |

Findings:
- DOPRI5-adaptive: 1 run(s) did not complete the horizon: aborted at t=2.468E+005 s: scaled error stayed above 1 at the smallest step; step floor 8.640E-010 s reached at t=2.468E+005 s (last scaled error 1.226E+002); worst error at completed checkpoints 1.20E+007 m; excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 19 run(s); the step cap, not the tolerance, sets the cost there.
- RKN12(10) [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- RKN12(10): the uncapped branch reached only 1.22E-001 m while RKN12(10) [cap=600 s] reached 7.76E-005 m; the step cap is load-bearing on this horizon.
- IAS15: the uncapped branch reached only 5.59E-004 m while IAS15 [cap=600 s] reached 3.12E-007 m; the step cap is load-bearing on this horizon.
- TIE at 100 m: RKN12(10) (2,655), WH-BM14A (2,736) are within 10%; do not read the ranking as resolved.
- TIE at 10 m: RKN12(10) (3,430), WH-BM14A (3,648) are within 10%; do not read the ranking as resolved.
- TIE at 1 m: RKN12(10) (4,236), WH-BM14A (4,560) are within 10%; do not read the ranking as resolved.

## Earth GTO 400 x 40000 km

Reference floor: 3.39E-006 m (fixed DOP853 h=14.4397 s, ratio=923.72 order=9.85 (floor from finest disagreement), est 3.39E-006 m / 1.7E-009 m/s, finest d=3.39E-006 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 86,411* (8.5 ms) | 148,621* (15.1 ms) | 258,530* (26.1 ms) |
| DOPRI5-fixed | 45,455* (6.1 ms) | 73,412* (10.0 ms) | 116,585* (15.5 ms) |
| DOPRI5-adaptive | 8,692* (1.1 ms) | 14,163* (1.9 ms) | 22,428* (3.1 ms) |
| DOPRI5-adaptive [cap=600 s] | 9,846* (1.3 ms) | 14,163* (1.9 ms) | 22,428* (3.0 ms) |
| DOP853-adaptive | 7,057* (1.0 ms) | 8,522* (1.2 ms) | 10,555* (1.5 ms) |
| DOP853-adaptive [cap=600 s] | 12,600* (1.8 ms) | 13,466* (1.9 ms) | 14,581* (2.1 ms) |
| PEFRL | 71,681* (9.9 ms) | 127,461* (18.0 ms) | 226,651* (31.5 ms) |
| Y6 | 51,059* (7.1 ms) | 74,955* (10.6 ms) | 110,026* (15.8 ms) |
| Y6C | 51,059* (7.2 ms) | 74,956* (10.6 ms) | 110,027* (15.5 ms) |
| WH-Y6 | 2,607 (0.396 ms) | 4,343 (0.658 ms) | 5,211 (0.784 ms) |
| Leapfrog | 2.67E+006* (368.3 ms) | not reached (best 4.04E+001) | not reached (best 4.04E+001) |
| RKN12(10) | 4,006* (0.534 ms) | 4,557* (0.613 ms) | 5,227* (0.705 ms) |
| RKN12(10) [cap=600 s] | <=15,915 EDGE | <=15,915 EDGE | 16,045* (2.1 ms) |
| IAS15 | 12,019* (2.1 ms) | 13,120* (2.3 ms) | 15,075* (2.6 ms) |
| IAS15 [cap=600 s] | <=34,539 EDGE | <=34,539 EDGE | <=34,539 EDGE |
| SRKN14A | 21,259* (2.9 ms) | 30,653* (4.1 ms) | 44,656* (6.0 ms) |
| WH-BM14A | <=1,767 EDGE | 3,534 (0.528 ms) | 5,301 (0.778 ms) |
| GJ8 | 18,468* (2.4 ms) | 21,815* (2.8 ms) | 26,640 (3.5 ms) |
| Encke-IAS15 | <=52,128 EDGE | <=52,128 EDGE | <=52,128 EDGE |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 10 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 16 run(s); the step cap, not the tolerance, sets the cost there.
- WH-Y6: error is non-monotonic at coarse h in 1 place(s) but recovers at finer h (h=2.77E+03 -> 1.85E+03 s, recovers by h=1.23E+03 s); that coarse branch is pre-asymptotic, not a round-off floor.
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- RKN12(10) [cap=600 s]: CAP-BOUND in 24 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- Encke-IAS15: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- TIE at 10 m: WH-Y6 (4,343), RKN12(10) (4,557), WH-BM14A (3,534) are within 56%; do not read the ranking as resolved.
- TIE at 1 m: WH-Y6 (5,211), RKN12(10) (5,227), WH-BM14A (5,301) are within 18%; do not read the ranking as resolved.

## Earth Molniya 500 x 40000 km

Reference floor: 9.52E-005 m (fixed DOP853 h=14.7608 s, ratio=553.83 order=9.11 (floor from finest disagreement), est 9.52E-005 m / 7.4E-008 m/s, finest d=9.52E-005 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 439,045* (40.7 ms) | 737,650* (72.7 ms) | 1.26E+006* (122.4 ms) |
| DOPRI5-fixed | 254,853* (32.2 ms) | 408,643* (51.7 ms) | 649,894* (82.1 ms) |
| DOPRI5-adaptive | 46,731* (6.1 ms) | 76,762* (10.0 ms) | 122,199* (15.9 ms) |
| DOPRI5-adaptive [cap=600 s] | 46,948* (6.1 ms) | 76,762* (10.0 ms) | 122,199* (15.9 ms) |
| DOP853-adaptive | 30,856* (4.4 ms) | 38,798* (5.5 ms) | 44,222* (6.3 ms) |
| DOP853-adaptive [cap=600 s] | 46,845* (6.6 ms) | 51,344* (7.2 ms) | 54,943* (7.7 ms) |
| PEFRL | 334,331* (46.0 ms) | 594,528* (82.0 ms) | 1.06E+006* (145.6 ms) |
| Y6 | 214,822* (29.9 ms) | 315,348* (44.0 ms) | 462,937* (64.4 ms) |
| Y6C | 214,822* (30.2 ms) | 315,349* (44.5 ms) | 462,894* (65.3 ms) |
| WH-Y6 | 11,987 (1.8 ms) | 17,979 (2.7 ms) | 26,967 (4.1 ms) |
| Leapfrog | not reached (best 1.59E+003) | not reached (best 1.59E+003) | not reached (best 1.59E+003) |
| RKN12(10) | 14,750* (2.0 ms) | 17,571* (2.4 ms) | 19,259* (2.6 ms) |
| RKN12(10) [cap=600 s] | <=54,726 EDGE | 55,027* (7.3 ms) | 56,054* (7.4 ms) |
| IAS15 | 51,775* (9.1 ms) | 54,566* (9.6 ms) | 58,935* (10.4 ms) |
| IAS15 [cap=600 s] | <=118,194 EDGE | <=118,194 EDGE | <=118,194 EDGE |
| SRKN14A | 88,374* (12.1 ms) | 128,263* (17.5 ms) | 187,357* (25.5 ms) |
| WH-BM14A | 12,198 (1.8 ms) | 12,198 (1.8 ms) | 18,297 (2.7 ms) |
| GJ8 | 66,686* (8.6 ms) | 81,057 (10.5 ms) | 89,424 (11.5 ms) |
| Encke-IAS15 | <=181,300 EDGE | <=181,300 EDGE | <=181,300 EDGE |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 10 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 18 run(s); the step cap, not the tolerance, sets the cost there.
- WH-Y6: error is non-monotonic at coarse h in 1 place(s) but recovers at finer h (h=2.83E+03 -> 1.89E+03 s, recovers by h=1.71E+03 s); that coarse branch is pre-asymptotic, not a round-off floor.
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- RKN12(10) [cap=600 s]: CAP-BOUND in 26 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- TIE at 100 m: WH-Y6 (11,987), WH-BM14A (12,198) are within 18%; do not read the ranking as resolved.
- TIE at 1 m: WH-Y6 (26,967), RKN12(10) (19,259), WH-BM14A (18,297) are within 56%; do not read the ranking as resolved.

## Earth GEO 35786 km

Reference floor: 1.80E-004 m (fixed DOP853 h=448.769 s, ratio=1688.84 order=10.72 (floor from finest disagreement), est 1.80E-004 m / 1.3E-008 m/s, finest d=1.80E-004 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 119,733* (11.3 ms) | 195,059* (18.6 ms) | 321,475* (29.4 ms) |
| DOPRI5-fixed | 76,584* (9.6 ms) | 122,534* (15.5 ms) | 194,942* (24.6 ms) |
| DOPRI5-adaptive | 76,692* (10.0 ms) | 122,848* (16.0 ms) | 195,096* (25.4 ms) |
| DOPRI5-adaptive [cap=600 s] | <=77,799 EDGE | 122,848* (16.0 ms) | 195,096* (25.4 ms) |
| DOP853-adaptive | 17,085* (2.4 ms) | 21,328* (3.0 ms) | 27,013* (3.8 ms) |
| DOP853-adaptive [cap=600 s] | <=155,595 EDGE | <=155,595 EDGE | <=155,595 EDGE |
| PEFRL | 77,408* (10.6 ms) | 137,654* (18.9 ms) | 244,783* (33.7 ms) |
| Y6 | 53,468* (7.5 ms) | 78,495* (10.9 ms) | 115,217* (16.0 ms) |
| Y6C | 53,468* (7.6 ms) | 78,495* (11.1 ms) | 115,220* (16.3 ms) |
| WH-Y6 | 5,043 (0.688 ms) | 6,723 (0.926 ms) | 10,083 (1.4 ms) |
| Leapfrog | 2.92E+006* (402.1 ms) | not reached (best 5.18E+001) | not reached (best 5.18E+001) |
| RKN12(10) | 6,625* (0.881 ms) | 8,106* (1.1 ms) | 9,554* (1.3 ms) |
| RKN12(10) [cap=600 s] | <=220,425 EDGE | <=220,425 EDGE | <=220,425 EDGE |
| IAS15 | 22,356* (3.9 ms) | 22,839* (4.0 ms) | 23,214* (4.0 ms) |
| IAS15 [cap=600 s] | <=401,487 EDGE | <=401,487 EDGE | <=401,487 EDGE |
| SRKN14A | 21,204* (2.9 ms) | 31,144* (4.2 ms) | 45,726* (6.2 ms) |
| WH-BM14A | 5,130 (0.716 ms) | 5,130 (0.716 ms) | 6,840 (0.951 ms) |
| GJ8 | 7,435* (1.0 ms) | 8,767* (1.2 ms) | 10,114* (1.4 ms) |
| Encke-IAS15 | <=208,360 EDGE | <=208,360 EDGE | <=208,360 EDGE |

Findings:
- DOPRI5-adaptive: 1 run(s) did not complete the horizon: aborted at t=1.227E+006 s: scaled error stayed above 1 at the smallest step; step floor 2.592E-008 s reached at t=1.227E+006 s (last scaled error 2.664E+001); worst error at completed checkpoints 7.64E+007 m; excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 26 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- RKN12(10) [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive: the uncapped branch reached only 8.44E-002 m while DOP853-adaptive [cap=600 s] reached 2.56E-004 m; the step cap is load-bearing on this horizon.
- RKN12(10): the uncapped branch reached only 7.02E-002 m while RKN12(10) [cap=600 s] reached 1.19E-004 m; the step cap is load-bearing on this horizon.
- TIE at 100 m: WH-Y6 (5,043), WH-BM14A (5,130) are within 10%; do not read the ranking as resolved.

## Sun 1 AU circular

Reference floor: 5.31E-004 m (fixed DOP853 h=82182.8 s, ratio=280.08 order=8.13 (floor from finest disagreement), est 5.31E-004 m / 1.1E-010 m/s, finest d=5.31E-004 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 9,277* (0.857 ms) | 16,436* (1.6 ms) | 29,153* (2.8 ms) |
| DOPRI5-fixed | 3,101* (0.392 ms) | 5,150* (0.647 ms) | 8,343* (1.1 ms) |
| DOPRI5-adaptive | 3,222* (0.420 ms) | 5,254* (0.687 ms) | 8,421* (1.1 ms) |
| DOPRI5-adaptive [cap=600 s] | below round-off floor (1.22E-001 m) | below round-off floor (1.22E-001 m) | below round-off floor (1.22E-001 m) |
| DOP853-adaptive | 972* (0.137 ms) | 1,227* (0.173 ms) | 1,800* (0.254 ms) |
| DOP853-adaptive [cap=600 s] | below round-off floor (1.22E-001 m) | below round-off floor (1.22E-001 m) | below round-off floor (1.22E-001 m) |
| PEFRL | 8,525* (1.2 ms) | 15,164* (2.1 ms) | 26,950* (3.7 ms) |
| Y6 | 3,955* (0.546 ms) | 5,804* (0.800 ms) | 8,518* (1.2 ms) |
| Y6C | 3,955* (0.559 ms) | 5,804* (0.818 ms) | 8,517* (1.2 ms) |
| WH-Y6 | <=2,691 EDGE | <=2,691 EDGE | <=2,691 EDGE |
| Leapfrog | not reached (best 1.60E+003) | not reached (best 1.60E+003) | not reached (best 1.60E+003) |
| RKN12(10) | <=564 EDGE | <=564 EDGE | 678* (0.091 ms) |
| RKN12(10) [cap=600 s] | below round-off floor (1.22E-001 m) | below round-off floor (1.22E-001 m) | below round-off floor (1.22E-001 m) |
| IAS15 | 1,786* (0.313 ms) | 1,793* (0.314 ms) | 1,799* (0.315 ms) |
| IAS15 [cap=600 s] | below round-off floor (1.22E-001 m) | below round-off floor (1.22E-001 m) | below round-off floor (1.22E-001 m) |
| SRKN14A | <=4,032 EDGE | <=4,032 EDGE | <=4,032 EDGE |
| WH-BM14A | below round-off floor (5.21E-003 m) | below round-off floor (5.21E-003 m) | below round-off floor (5.21E-003 m) |
| GJ8 | <=1,728 EDGE | <=1,728 EDGE | <=1,728 EDGE |
| Encke-IAS15 | below round-off floor (5.21E-003 m) | below round-off floor (5.21E-003 m) | below round-off floor (5.21E-003 m) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- RKN12(10) [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15: the uncapped branch reached only 6.30E-002 m while IAS15 [cap=600 s] reached 4.01E-004 m; the step cap is load-bearing on this horizon.
- double-precision round-off estimate 1.22E-001 m (16 eps * scale * sqrt(steps=52,600 of the most accurate run); each cell uses its own run's step count, and a run whose error is within that noise is reported as 'below round-off floor', not as a ranking result (18 cell(s) excluded).

## Sun 0.5 x 2 AU eccentric

Reference floor: 9.18E-004 m (fixed DOP853 h=29056 s, ratio=216.70 order=7.76 (floor from finest disagreement), est 9.18E-004 m / 7.8E-011 m/s, finest d=9.18E-004 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 29,791* (2.8 ms) | 52,839* (5.2 ms) | 94,052* (9.2 ms) |
| DOPRI5-fixed | 10,147* (1.3 ms) | 15,816* (2.0 ms) | 24,729* (3.1 ms) |
| DOPRI5-adaptive | 3,036* (0.398 ms) | 4,543* (0.593 ms) | 6,945* (0.912 ms) |
| DOPRI5-adaptive [cap=600 s] | below round-off floor (2.44E-001 m) | below round-off floor (2.44E-001 m) | below round-off floor (2.44E-001 m) |
| DOP853-adaptive | 1,251* (0.178 ms) | 1,601* (0.225 ms) | 2,030* (0.288 ms) |
| DOP853-adaptive [cap=600 s] | below round-off floor (2.44E-001 m) | below round-off floor (2.44E-001 m) | below round-off floor (2.44E-001 m) |
| PEFRL | 26,193* (3.6 ms) | 46,568* (6.4 ms) | 82,799* (11.3 ms) |
| Y6 | 11,988* (1.6 ms) | 17,600* (2.4 ms) | 25,842* (3.6 ms) |
| Y6C | 11,988* (1.7 ms) | 17,596* (2.5 ms) | 25,827* (3.6 ms) |
| WH-Y6 | <=2,691 EDGE | <=2,691 EDGE | <=2,691 EDGE |
| Leapfrog | not reached (best 1.97E+003) | not reached (best 1.97E+003) | not reached (best 1.97E+003) |
| RKN12(10) | 717* (0.096 ms) | 928* (0.124 ms) | 1,060* (0.141 ms) |
| RKN12(10) [cap=600 s] | below round-off floor (2.44E-001 m) | below round-off floor (2.44E-001 m) | below round-off floor (2.44E-001 m) |
| IAS15 | 1,824* (0.321 ms) | 1,841* (0.324 ms) | 1,859* (0.327 ms) |
| IAS15 [cap=600 s] | below round-off floor (2.44E-001 m) | below round-off floor (2.44E-001 m) | below round-off floor (2.44E-001 m) |
| SRKN14A | 4,380* (0.605 ms) | 6,412* (0.865 ms) | 9,395* (1.3 ms) |
| WH-BM14A | <=5,472 EDGE | <=5,472 EDGE | <=5,472 EDGE |
| GJ8 | 3,030 (0.431 ms) | 3,981 (0.549 ms) | 4,929* (0.671 ms) |
| Encke-IAS15 | below round-off floor (1.04E-002 m) | below round-off floor (1.04E-002 m) | below round-off floor (1.04E-002 m) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- RKN12(10) [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- GJ8: observed order 8.24 vs expected 10 (9 pairs): check the implementation or the regime.
- IAS15: the uncapped branch reached only 8.00E-002 m while IAS15 [cap=600 s] reached 2.78E-004 m; the step cap is load-bearing on this horizon.
- double-precision round-off estimate 2.44E-001 m (16 eps * scale * sqrt(steps=52,600 of the most accurate run); each cell uses its own run's step count, and a run whose error is within that noise is reported as 'below round-off floor', not as a ranking result (15 cell(s) excluded).

## Earth 400 km + J2

Reference floor: 2.57E-006 m (fixed DOP853 h=14.4397 s, ratio=350.49 order=8.45 (floor from finest disagreement), est 2.57E-006 m / 2.9E-009 m/s, finest d=2.57E-006 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 33,274* (3.5 ms) | 54,244* (5.6 ms) | 89,473* (9.6 ms) |
| DOPRI5-fixed | 20,072* (2.6 ms) | 33,345* (4.4 ms) | 53,659* (7.1 ms) |
| DOPRI5-adaptive | 20,090* (2.7 ms) | 33,406* (4.5 ms) | 53,822* (7.3 ms) |
| DOPRI5-adaptive [cap=600 s] | 20,090* (2.7 ms) | 33,406* (4.5 ms) | 53,822* (7.3 ms) |
| DOP853-adaptive | 6,248* (0.913 ms) | 7,628* (1.1 ms) | 9,800* (1.4 ms) |
| DOP853-adaptive [cap=600 s] | 6,248* (0.912 ms) | 7,628* (1.1 ms) | 9,800* (1.4 ms) |
| PEFRL | 21,662* (3.1 ms) | 38,524* (5.5 ms) | 68,507* (9.7 ms) |
| Y6 | 18,366* (2.7 ms) | 26,969* (4.0 ms) | 39,592* (5.9 ms) |
| Y6C | 18,366* (2.8 ms) | 26,969* (4.0 ms) | 39,592* (6.0 ms) |
| WH-Y6 | 2,243 (0.329 ms) | 3,139 (0.453 ms) | 4,035 (0.592 ms) |
| Leapfrog | 440,365* (62.9 ms) | 1.39E+006* (198.5 ms) | 4.40E+006* (631.1 ms) |
| RKN12(10) | 2,655* (0.376 ms) | 3,469* (0.485 ms) | 4,071* (0.569 ms) |
| RKN12(10) [cap=600 s] | <=7,500 EDGE | <=7,500 EDGE | <=7,500 EDGE |
| IAS15 | 11,454* (2.1 ms) | 11,691* (2.2 ms) | 11,932* (2.2 ms) |
| IAS15 [cap=600 s] | <=27,996 EDGE | <=27,996 EDGE | <=27,996 EDGE |
| SRKN14A | 7,276* (1.0 ms) | 10,696* (1.5 ms) | 15,711* (2.2 ms) |
| WH-BM14A | <=912 EDGE | 1,824 (0.262 ms) | 1,824 (0.262 ms) |
| GJ8 | 4,286* (0.685 ms) | 4,459* (0.696 ms) | 5,037* (0.767 ms) |
| Encke-IAS15 | <=74,952 EDGE | <=74,952 EDGE | <=74,952 EDGE |

Findings:
- DOPRI5-adaptive: 1 run(s) did not complete the horizon: aborted at t=2.373E+005 s: scaled error stayed above 1 at the smallest step; step floor 8.640E-010 s reached at t=2.373E+005 s (last scaled error 1.278E+002); worst error at completed checkpoints 1.20E+007 m; excluded from ranking, finer controls were still tried.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 2 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 19 run(s); the step cap, not the tolerance, sets the cost there.
- RKN12(10) [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- RKN12(10): the uncapped branch reached only 1.70E-001 m while RKN12(10) [cap=600 s] reached 8.09E-005 m; the step cap is load-bearing on this horizon.
- IAS15: the uncapped branch reached only 4.15E-005 m while IAS15 [cap=600 s] reached 2.53E-007 m; the step cap is load-bearing on this horizon.

## Earth 250 km + atmosphere drag

Reference floor: 1.92E-005 m (fixed DOP853 h=27.9251 s, ratio=240.21 order=7.91 (floor from finest disagreement), est 1.92E-005 m / 2.2E-008 m/s, finest d=1.92E-005 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 34,719* (6.0 ms) | 56,714* (9.9 ms) | 93,780* (16.4 ms) |
| DOPRI5-fixed | 20,888* (4.1 ms) | 34,643* (6.9 ms) | 55,730* (11.1 ms) |
| DOPRI5-adaptive | 21,032* (4.3 ms) | 34,780* (7.0 ms) | 55,976* (11.4 ms) |
| DOPRI5-adaptive [cap=600 s] | 21,032* (4.3 ms) | 34,780* (7.0 ms) | 55,976* (11.4 ms) |
| DOP853-adaptive | 6,292* (1.3 ms) | 8,204* (1.7 ms) | 10,063* (2.1 ms) |
| DOP853-adaptive [cap=600 s] | 6,292* (1.3 ms) | 8,204* (1.7 ms) | 10,063* (2.1 ms) |
| PEFRL | n/a | n/a | n/a |
| Y6 | n/a | n/a | n/a |
| Y6C | n/a | n/a | n/a |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | n/a | n/a | n/a |
| RKN12(10) | n/a | n/a | n/a |
| IAS15 | 11,062* (2.7 ms) | 11,440* (2.8 ms) | 11,830* (2.9 ms) |
| IAS15 [cap=600 s] | <=28,101 EDGE | <=28,101 EDGE | <=28,101 EDGE |
| SRKN14A | n/a | n/a | n/a |
| WH-BM14A | n/a | n/a | n/a |
| GJ8 | n/a | n/a | n/a |
| Encke-IAS15 | <=121,200 EDGE | <=121,200 EDGE | <=121,200 EDGE |

Findings:
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 19 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=600 s]: CAP-BOUND in 9 run(s); the step cap, not the tolerance, sets the cost there.

## Earth LEO 400 km + 600 s prograde burn

Reference floor: 1.78E-005 m (fixed DOP853 h=28.8795 s, ratio=1277.39 order=10.32 (floor from finest disagreement), est 1.78E-005 m / 8.5E-009 m/s, finest d=1.78E-005 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 26,148 (2.6 ms) | 53,028 (5.1 ms) | 107,568 (10.8 ms) |
| DOPRI5-fixed | 10,695 (1.4 ms) | 25,996* (3.3 ms) | 44,477* (5.6 ms) |
| DOPRI5-adaptive | 7,784* (1.0 ms) | 13,829* (1.8 ms) | 23,238* (3.0 ms) |
| DOPRI5-adaptive [cap=600 s] | 7,784* (1.0 ms) | 13,829* (1.8 ms) | 23,238* (3.0 ms) |
| DOP853-adaptive | 6,834* (0.971 ms) | 8,688* (1.2 ms) | 10,747* (1.5 ms) |
| DOP853-adaptive [cap=600 s] | 7,368* (1.0 ms) | 8,688* (1.2 ms) | 10,529* (1.5 ms) |
| PEFRL | n/a | n/a | n/a |
| Y6 | n/a | n/a | n/a |
| Y6C | n/a | n/a | n/a |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | n/a | n/a | n/a |
| RKN12(10) | n/a | n/a | n/a |
| IAS15 | 13,759* (2.4 ms) | 15,130* (2.6 ms) | 15,392* (2.7 ms) |
| IAS15 [cap=600 s] | <=22,083 EDGE | <=22,083 EDGE | <=22,083 EDGE |
| SRKN14A | n/a | n/a | n/a |
| WH-BM14A | n/a | n/a | n/a |
| GJ8 | n/a | n/a | n/a |
| Encke-IAS15 | <=85,161 EDGE | <=85,161 EDGE | <=85,161 EDGE |

Findings:
- RK4: error is non-monotonic at coarse h in 2 place(s) but recovers at finer h (h=36.1 -> 24 s, recovers by h=19.6 s); that coarse branch is pre-asymptotic, not a round-off floor.
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 10 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15: the uncapped branch reached only 9.37E-002 m while IAS15 [cap=600 s] reached 7.46E-005 m; the step cap is load-bearing on this horizon.

## Earth-Moon flyby inside lunar SOI

Reference floor: not certified (fixed DOP853 h=52.0833 s, ratio=4.81 order=2.27 UNRESOLVED (no converging pair), est NaN m / NaN m/s, finest d=9.09E-006 m retry 2). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 517* (0.047 ms) | 877* (0.080 ms) | 1,519* (0.137 ms) |
| DOPRI5-fixed | 434* (0.055 ms) | 629* (0.080 ms) | 916* (0.116 ms) |
| DOPRI5-adaptive | 608* (0.079 ms) | 801* (0.104 ms) | 1,102* (0.144 ms) |
| DOPRI5-adaptive [cap=600 s] | <=5,223 EDGE | <=5,223 EDGE | <=5,223 EDGE |
| DOP853-adaptive | <=435 EDGE | <=435 EDGE | 700* (0.099 ms) |
| DOP853-adaptive [cap=600 s] | <=10,443 EDGE | <=10,443 EDGE | <=10,443 EDGE |
| PEFRL | 584* (0.080 ms) | 1,038* (0.144 ms) | 1,847* (0.254 ms) |
| Y6 | 588* (0.083 ms) | 863* (0.118 ms) | 1,266* (0.177 ms) |
| Y6C | 588* (0.083 ms) | 863* (0.121 ms) | 1,266* (0.178 ms) |
| WH-Y6 | 899 (0.131 ms) | 1,347 (0.193 ms) | 1,795 (0.253 ms) |
| Leapfrog | 7,934* (1.1 ms) | 25,084* (3.4 ms) | 79,316* (10.9 ms) |
| RKN12(10) | <=615 EDGE | <=615 EDGE | <=615 EDGE |
| RKN12(10) [cap=600 s] | below round-off floor (2.34E-005 m) | below round-off floor (2.34E-005 m) | below round-off floor (2.34E-005 m) |
| IAS15 | <=1,632 EDGE | <=1,632 EDGE | <=1,632 EDGE |
| IAS15 [cap=600 s] | below round-off floor (2.34E-005 m) | below round-off floor (2.34E-005 m) | below round-off floor (2.34E-005 m) |
| SRKN14A | <=336 EDGE | <=336 EDGE | 672 (0.093 ms) |
| WH-BM14A | <=456 EDGE | <=456 EDGE | 912 (0.128 ms) |
| GJ8 | <=1,095 EDGE | <=1,095 EDGE | <=1,095 EDGE |
| Encke-IAS15 | <=6,380 EDGE | <=6,380 EDGE | <=6,380 EDGE |

Findings:
- REFERENCE NOT CERTIFIED: every number of this scenario is provisional (no reference floor). Reference: fixed DOP853 h=52.0833 s, ratio=4.81 order=2.27 UNRESOLVED (no converging pair), est NaN m / NaN m/s, finest d=9.09E-006 m retry 2
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- RKN12(10) [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- GJ8: error GROWS as h shrinks in 1 place(s): round-off/non-convergent regime (steps beyond that point are not 'more accurate').
- GJ8: observed order 7.30 vs expected 10 (5 pairs): check the implementation or the regime.
- DOPRI5-adaptive: the uncapped branch reached only 1.35E-001 m while DOPRI5-adaptive [cap=600 s] reached 5.89E-005 m; the step cap is load-bearing on this horizon.
- DOP853-adaptive: the uncapped branch reached only 1.95E-001 m while DOP853-adaptive [cap=600 s] reached 3.01E-005 m; the step cap is load-bearing on this horizon.
- TIE at 1 m: DOP853-adaptive (700), SRKN14A (672) are within 18%; do not read the ranking as resolved.
- double-precision round-off estimate 2.34E-005 m (16 eps * scale * sqrt(steps=290 of the most accurate run); each cell uses its own run's step count, and a run whose error is within that noise is reported as 'below round-off floor', not as a ranking result (6 cell(s) excluded).

## Earth LEO 400 km, 1000 orbits (~64 d)

Reference floor: 8.52E+000 m (fixed DOP853 h=43.3192 s, ratio=392.84 order=8.62, est 1.82E-004 m / 2.1E-007 m/s, finest d=7.15E-002 m + independent dd endpoint floor 8.522E+000 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | not reached (best 1.11E+004) | skip | skip |
| DOPRI5-fixed | not reached (best 1.62E+003) | skip | skip |
| DOPRI5-adaptive | not reached (best 4.03E+002) | skip | skip |
| DOPRI5-adaptive [cap=600 s] | not reached (best 4.03E+002) | skip | skip |
| DOP853-adaptive | 1.00E+006* (141.2 ms) | skip | skip |
| DOP853-adaptive [cap=600 s] | 1.00E+006* (141.0 ms) | skip | skip |
| PEFRL | 3.91E+006* (553.6 ms) | skip | skip |
| Y6 | 2.35E+006* (328.9 ms) | skip | skip |
| Y6C | 2.35E+006* (333.2 ms) | skip | skip |
| WH-Y6 | 93,187 (12.7 ms) | skip | skip |
| Leapfrog | not reached (best 1.82E+005) | skip | skip |
| RKN12(10) | 301,552* (40.1 ms) | skip | skip |
| RKN12(10) [cap=600 s] | <=483,177 EDGE | skip | skip |
| IAS15 | 720,847* (125.8 ms) | skip | skip |
| IAS15 [cap=600 s] | <=1.81E+006 EDGE | skip | skip |
| SRKN14A | 932,667* (127.3 ms) | skip | skip |
| WH-BM14A | 72,960 (10.1 ms) | skip | skip |
| GJ8 | 296,453* (38.1 ms) | skip | skip |
| Encke-IAS15 | not reached | skip | skip |

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
- RKN12(10) [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- Encke-IAS15: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').

## Earth HEO 300 x 120000 km

Reference floor: 2.32E-005 m (fixed DOP853 h=14.121 s, ratio=133.44 order=7.06 (floor from finest disagreement), est 2.32E-005 m / 8.9E-009 m/s, finest d=2.32E-005 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 302,949* (28.7 ms) | 531,340* (51.0 ms) | 937,085* (92.3 ms) |
| DOPRI5-fixed | 139,476* (17.6 ms) | 219,484* (27.7 ms) | 344,190* (43.4 ms) |
| DOPRI5-adaptive | 8,986* (1.2 ms) | 13,840* (1.8 ms) | 20,738* (2.7 ms) |
| DOPRI5-adaptive [cap=600 s] | 16,552* (2.2 ms) | 19,510* (2.5 ms) | 24,695* (3.2 ms) |
| DOP853-adaptive | 6,291* (0.889 ms) | 7,463* (1.1 ms) | 8,736* (1.2 ms) |
| DOP853-adaptive [cap=600 s] | 26,901* (3.8 ms) | 27,385* (3.9 ms) | 27,979* (3.9 ms) |
| PEFRL | 265,987* (36.6 ms) | 472,982* (65.1 ms) | 841,030* (115.9 ms) |
| Y6 | 168,091* (23.4 ms) | 246,745* (34.2 ms) | 362,179* (50.3 ms) |
| Y6C | 168,091* (23.7 ms) | 246,745* (34.8 ms) | 362,187* (51.0 ms) |
| WH-Y6 | 8,963 (1.3 ms) | 13,443 (2.0 ms) | 20,163 (3.0 ms) |
| Leapfrog | not reached (best 8.29E+002) | not reached (best 8.29E+002) | not reached (best 8.29E+002) |
| RKN12(10) | 5,556* (0.738 ms) | 6,010* (0.793 ms) | 6,626* (0.890 ms) |
| RKN12(10) [cap=600 s] | <=36,876 EDGE | <=36,876 EDGE | 37,123* (4.9 ms) |
| IAS15 | 14,692* (2.6 ms) | 14,895* (2.6 ms) | 16,181* (2.8 ms) |
| IAS15 [cap=600 s] | <=61,581 EDGE | <=61,581 EDGE | <=61,581 EDGE |
| SRKN14A | 71,127* (9.7 ms) | 103,288* (14.1 ms) | 150,908* (20.6 ms) |
| WH-BM14A | <=4,560 EDGE | 9,120 (1.3 ms) | 13,680 (2.0 ms) |
| GJ8 | 54,326* (7.0 ms) | 67,089 (8.7 ms) | 73,767 (9.6 ms) |
| Encke-IAS15 | <=66,232 EDGE | <=66,232 EDGE | <=66,232 EDGE |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 15 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 17 run(s); the step cap, not the tolerance, sets the cost there.
- Leapfrog: 1 run(s) hit the RHS budget (cost not reached = censored, not 'expensive').
- RKN12(10) [cap=600 s]: CAP-BOUND in 25 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.

## Earth hyperbolic flyby e=1.5

Reference floor: 1.82E-006 m (fixed DOP853 h=14.121 s, ratio=315.52 order=8.30 (floor from finest disagreement), est 1.82E-006 m / 2.0E-011 m/s, finest d=1.82E-006 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 26,571* (2.7 ms) | 46,883* (4.6 ms) | 82,989* (7.8 ms) |
| DOPRI5-fixed | 11,881* (1.5 ms) | 18,536* (2.3 ms) | 28,496* (3.6 ms) |
| DOPRI5-adaptive | 775* (0.102 ms) | 1,004* (0.131 ms) | 1,378* (0.180 ms) |
| DOPRI5-adaptive [cap=600 s] | 2,813* (0.369 ms) | 2,963* (0.384 ms) | 3,236* (0.420 ms) |
| DOP853-adaptive | 921* (0.129 ms) | 1,047* (0.147 ms) | 1,147* (0.161 ms) |
| DOP853-adaptive [cap=600 s] | 5,331* (0.746 ms) | 5,350* (0.752 ms) | 5,403* (0.758 ms) |
| PEFRL | 22,187* (3.0 ms) | 39,439* (5.4 ms) | 70,125* (9.7 ms) |
| Y6 | 20,938* (2.9 ms) | 30,752* (4.3 ms) | 45,151* (6.2 ms) |
| Y6C | 20,938* (3.0 ms) | 30,752* (4.4 ms) | 45,151* (6.4 ms) |
| WH-Y6 | <=451 EDGE | 899 (0.175 ms) | 2,243 (0.443 ms) |
| Leapfrog | 405,519* (55.9 ms) | 1.28E+006* (175.9 ms) | 4.06E+006* (558.2 ms) |
| RKN12(10) | <=1,074 EDGE | 1,074* (0.144 ms) | 1,203* (0.160 ms) |
| RKN12(10) [cap=600 s] | <=7,500 EDGE | <=7,500 EDGE | 7,500* (0.994 ms) |
| IAS15 | <=2,418 EDGE | <=2,418 EDGE | <=2,418 EDGE |
| IAS15 [cap=600 s] | <=11,280 EDGE | <=11,280 EDGE | <=11,280 EDGE |
| SRKN14A | 8,726* (1.2 ms) | 12,441* (1.7 ms) | 17,819* (2.4 ms) |
| WH-BM14A | <=912 EDGE | <=912 EDGE | <=912 EDGE |
| GJ8 | 10,335 (1.4 ms) | 12,042* (1.6 ms) | 14,904* (1.9 ms) |
| Encke-IAS15 | below round-off floor (1.42E-006 m) | below round-off floor (1.42E-006 m) | below round-off floor (1.42E-006 m) |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 14 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 13 run(s); the step cap, not the tolerance, sets the cost there.
- RKN12(10) [cap=600 s]: CAP-BOUND in 20 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- GJ8: observed order 8.62 vs expected 10 (8 pairs): check the implementation or the regime.
- TIE at 10 m: DOPRI5-adaptive (1,004), DOP853-adaptive (1,047), WH-Y6 (899), RKN12(10) (1,074) are within 56%; do not read the ranking as resolved.
- TIE at 1 m: DOP853-adaptive (1,147), RKN12(10) (1,203) are within 10%; do not read the ranking as resolved.
- double-precision round-off estimate 1.42E-006 m (16 eps * scale * sqrt(steps=16 of the most accurate run); each cell uses its own run's step count, and a run whose error is within that noise is reported as 'below round-off floor', not as a ranking result (3 cell(s) excluded).

## Earth LEO 400 km, 30 d

Reference floor: 2.54E-003 m (fixed DOP853 h=14.4397 s, ratio=560.12 order=9.13 (floor from finest disagreement), est 2.54E-003 m / 2.9E-006 m/s, finest d=2.54E-003 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 3.69E+006* (359.0 ms) | not reached (best 4.35E+001) | not reached (best 4.35E+001) |
| DOPRI5-fixed | 2.51E+006* (316.7 ms) | 3.98E+006* (503.3 ms) | not reached (best 6.29E+000) |
| DOPRI5-adaptive | 2.51E+006* (327.0 ms) | 3.98E+006* (519.5 ms) | not reached (best 2.82E+000) |
| DOPRI5-adaptive [cap=600 s] | 2.51E+006* (327.0 ms) | 3.98E+006* (519.3 ms) | not reached (best 2.82E+000) |
| DOP853-adaptive | 401,259* (56.5 ms) | 510,847* (71.9 ms) | 659,541* (92.7 ms) |
| DOP853-adaptive [cap=600 s] | 401,259* (56.7 ms) | 510,847* (72.1 ms) | 659,541* (93.0 ms) |
| PEFRL | 1.51E+006* (208.5 ms) | 2.69E+006* (370.9 ms) | not reached (best 1.44E+000) |
| Y6 | 968,214* (134.9 ms) | 1.42E+006* (197.8 ms) | 2.09E+006* (291.0 ms) |
| Y6C | 968,216* (136.7 ms) | 1.42E+006* (201.1 ms) | 2.09E+006* (295.4 ms) |
| WH-Y6 | 43,011 (5.9 ms) | 50,179 (6.9 ms) | 57,347 (7.8 ms) |
| Leapfrog | not reached (best 1.68E+004) | not reached (best 1.68E+004) | not reached (best 1.68E+004) |
| RKN12(10) | 134,737* (17.9 ms) | 158,632* (21.1 ms) | 180,361* (24.0 ms) |
| RKN12(10) [cap=600 s] | <=222,108 EDGE | <=222,108 EDGE | <=222,108 EDGE |
| IAS15 | 482,898* (84.5 ms) | 483,434* (84.6 ms) | 483,970* (84.7 ms) |
| IAS15 [cap=600 s] | <=839,649 EDGE | <=839,649 EDGE | 839,786* (146.7 ms) |
| SRKN14A | 383,992* (52.4 ms) | 563,928* (76.9 ms) | 828,359* (113.0 ms) |
| WH-BM14A | 29,184 (4.1 ms) | 43,776 (6.1 ms) | 58,368 (8.1 ms) |
| GJ8 | 121,672* (15.8 ms) | 149,544* (19.3 ms) | 184,677* (23.9 ms) |
| Encke-IAS15 | <=3.29E+006 EDGE | <=3.29E+006 EDGE | <=3.29E+006 EDGE |

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
- RKN12(10) [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=600 s]: CAP-BOUND in 9 run(s); the step cap, not the tolerance, sets the cost there.
- TIE at 100 m: WH-Y6 (43,011), WH-BM14A (29,184) are within 56%; do not read the ranking as resolved.
- TIE at 10 m: WH-Y6 (50,179), WH-BM14A (43,776) are within 18%; do not read the ranking as resolved.
- TIE at 1 m: WH-Y6 (57,347), WH-BM14A (58,368) are within 18%; do not read the ranking as resolved.

## Earth LEO 400 km + unaligned 600 s burn

Reference floor: 2.51E-005 m (fixed DOP853 h=28.8795 s, ratio=174.67 order=7.45 (floor from finest disagreement), est 2.51E-005 m / 2.4E-008 m/s, finest d=2.51E-005 m). Kepler-solve weight: 1.00 RHS. RHS multiplier: 3. RHS budget: 2,000,000.

| integrator | 100 m | 10 m | 1 m |
|---|---|---|---|
| RK4 | 30,165* (2.8 ms) | 51,765* (4.9 ms) | 89,139* (9.0 ms) |
| DOPRI5-fixed | 17,643 (2.2 ms) | 28,514* (3.6 ms) | 45,692* (5.8 ms) |
| DOPRI5-adaptive | 7,718* (1.0 ms) | 15,583* (2.0 ms) | 25,271* (3.3 ms) |
| DOPRI5-adaptive [cap=600 s] | 7,718* (1.0 ms) | 15,583* (2.0 ms) | 25,271* (3.3 ms) |
| DOP853-adaptive | 7,214* (1.0 ms) | 9,056* (1.3 ms) | 11,201* (1.6 ms) |
| DOP853-adaptive [cap=600 s] | 7,411* (1.0 ms) | 9,041* (1.3 ms) | 11,067* (1.6 ms) |
| PEFRL | n/a | n/a | n/a |
| Y6 | n/a | n/a | n/a |
| Y6C | n/a | n/a | n/a |
| WH-Y6 | n/a | n/a | n/a |
| Leapfrog | n/a | n/a | n/a |
| RKN12(10) | n/a | n/a | n/a |
| IAS15 | 14,596* (2.5 ms) | 15,370* (2.7 ms) | 16,171* (2.8 ms) |
| IAS15 [cap=600 s] | <=21,828 EDGE | <=21,828 EDGE | <=21,828 EDGE |
| SRKN14A | n/a | n/a | n/a |
| WH-BM14A | n/a | n/a | n/a |
| GJ8 | n/a | n/a | n/a |
| Encke-IAS15 | <=78,065 EDGE | <=78,065 EDGE | <=78,065 EDGE |

Findings:
- DOPRI5-adaptive [cap=600 s]: CAP-BOUND in 5 run(s); the step cap, not the tolerance, sets the cost there.
- DOP853-adaptive [cap=600 s]: CAP-BOUND in 10 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15 [cap=600 s]: CAP-BOUND in 7 run(s); the step cap, not the tolerance, sets the cost there.
- IAS15: the uncapped branch reached only 2.09E-002 m while IAS15 [cap=600 s] reached 1.45E-004 m; the step cap is load-bearing on this horizon.
- TIE at 100 m: DOPRI5-adaptive (7,718), DOP853-adaptive (7,214) are within 10%; do not read the ranking as resolved.
