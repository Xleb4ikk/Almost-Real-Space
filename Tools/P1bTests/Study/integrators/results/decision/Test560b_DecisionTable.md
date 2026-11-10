# Test560b - decision table (compact)

Generated from `Test560b_DecisionMatrix.csv`; all numbers are executed runs.
`best_all` uses `cost_if_ported` for stand candidates; `best_production` uses
`cost_total`. Full acceptable percentages and selected totals are in
`Test560b_DecisionTable.csv` and `Test560b_DecisionDetails.md`.

## Core decision table

| regime | scenario | target | best_production | best_ported | best_all | confidence | why |
|---|---|---|---|---|---|---|---|
| cislunar | "Cislunar Moon flyby, SOI entry-exit, 10 d" | 100 m | DOPRI5 [production] | DOPRI5-adaptive (requires port) | tie: DOPRI5-adaptive (requires port), DOPRI5 [production] | low | best_all includes production; production option DOPRI5 [production] is within 11.0% of best_all; production +11% vs best_all; edge 92,160 (206% more expensive than best_all, 176% more expensive than production) |
| far-flight | "Far flight 30 AU e 0.5, 10 y" | 100 km | DOPRI5 [production] | Leapfrog (requires port) | Leapfrog (requires port) | medium | best_all is a port candidate; production +500% vs best_all; no edge runs |
| far-flight | "Far flight 30 AU e 0.5, 10 y [regime86400]" | 100 km | DOPRI5 [production] | Leapfrog (requires port) | Leapfrog (requires port) | medium | best_all is a port candidate; production +450% vs best_all; no edge runs |
| heliocentric | "Heliocentric 0.3 AU e 0.5, 1 y" | 10 km | DOPRI5 [production] | RK4 (requires port) | tie: RK4 (requires port), PEFRL (requires port) | low | best_all is a tie of port candidates; production +120% vs best_all; edge 3,679,200 (600% more expensive than best_all, 218% more expensive than production) |
| heliocentric | "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | 10 km | DOP853 [production] | WH-Y6 (requires port) | WH-Y6 (requires port) | medium | best_all is a port candidate; production +51% vs best_all; edge 30,350 (176% more expensive than best_all, 83% more expensive than production) |
| heliocentric | "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | 100 km | DOPRI5 [production] | Leapfrog (requires port) | Leapfrog (requires port) | medium | best_all is a port candidate; production +450% vs best_all; no edge runs |
| heliocentric | "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | 100 km | DOPRI5 [production] | RK4 (requires port) | tie: RK4 (requires port), PEFRL (requires port) | low | best_all is a tie of port candidates; production +120% vs best_all; edge 233,632 (538% more expensive than best_all, 190% more expensive than production) |
| high-orbit | "High orbit 200 000 km, 30 d" | 100 m | DOPRI5 [production] | RK4 (requires port) | tie: RK4 (requires port), PEFRL (requires port) | low | best_all is a tie of port candidates; production +120% vs best_all; edge 211,680 (390% more expensive than best_all, 123% more expensive than production) |
| high-orbit | "High orbit 35 786 km (GEO), 30 d" | 100 m | DOP853 [production] | Y6C (requires port) | tie: Y6C (requires port), Y6 (requires port), PEFRL (requires port) | low | best_all is a tie of port candidates; production +68% vs best_all; edge 82,080 (14% cheaper than best_all, 49% cheaper than production) |
| leo | "LEO 200 km, 1 d" | 10 m | DOP853 [production] | DOPRI5-fixed (requires port) | tie: DOPRI5-fixed (requires port), DOP853-adaptive (requires port), DOP853 [production] | low | best_all includes production; production option DOP853 [production] is within 0.8% of best_all; production +1% vs best_all; edge 63,589 (7% more expensive than best_all, 6% more expensive than production) |
| leo | "LEO 300 km, 30 d" | 100 m | DOP853 [production] | DOP853-adaptive (requires port) | DOP853-adaptive (requires port) | medium | best_all is a port candidate; production +45% vs best_all; edge 2,515,459 (282% more expensive than best_all, 163% more expensive than production) |
| surface | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | 0.1 m/s | DOPRI5 [production] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs |
| surface | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | 0.1 m | DOPRI5 [production] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs |
| surface | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | 0.1 m/s | DOPRI5 [production] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs |
| surface | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | 0.1 m | DOPRI5 [production] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs |

## Prescribed-force surface hover/level (separate section)

These are prescribed-force trajectories, not closed-loop altitude hold;
`ThrustScalesWithMass` uses a linear mass estimate for the force schedule.

| regime | scenario | target | best_production | best_ported | best_all | confidence | why |
|---|---|---|---|---|---|---|---|
| surface | "Surface hover 0.5 km, 0 m/s relative" | 0.1 m/s | DOPRI5 [production] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; prescribed-force; no closed-loop altitude hold |
| surface | "Surface hover 0.5 km, 0 m/s relative" | 0.1 m | DOPRI5 [production] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; prescribed-force; no closed-loop altitude hold |
| surface | "Surface level flight 0.5 km, 100 m/s horizontal" | 0.1 m/s | DOPRI5 [production] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; prescribed-force; no closed-loop altitude hold |
| surface | "Surface level flight 0.5 km, 100 m/s horizontal" | 0.1 m | DOPRI5 [production] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; prescribed-force; no closed-loop altitude hold |
| surface | "Surface level flight 1 km, 100 m/s horizontal" | 0.1 m | DOPRI5 [production] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; prescribed-force; no closed-loop altitude hold |
| surface | "Surface level flight 1 km, 100 m/s horizontal" | 0.1 m/s | DOPRI5 [production] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; prescribed-force; no closed-loop altitude hold |

## Sensitivity (separate from the decision table)

| scenario | config | method | target | err | total | status |
|---|---|---|---|---:|---:|---|
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853 [production] | 0.1 m | 1.272E-006 | 4736 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853 [production] | 0.1 m/s | 4.285E-008 | 4736 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5 [production] | 0.1 m | 2.424E-007 | 2816 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5 [production] | 0.1 m/s | 7.075E-008 | 2816 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | RK4 | 0.1 m | 3.712E-004 | 516 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | RK4 | 0.1 m/s | 3.394E-005 | 516 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-fixed | 0.1 m | 2.307E-007 | 902 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-fixed | 0.1 m/s | 5.595E-008 | 902 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive | 0.1 m | 2.307E-007 | 902 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive | 0.1 m/s | 5.595E-008 | 902 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive | 0.1 m | 1.240E-006 | 1676 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive | 0.1 m/s | 4.285E-008 | 1676 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | PEFRL | 0.1 m | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | PEFRL | 0.1 m/s | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | Y6 | 0.1 m | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | Y6 | 0.1 m/s | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | Y6C | 0.1 m | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | Y6C | 0.1 m/s | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | WH-Y6 | 0.1 m | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | WH-Y6 | 0.1 m/s | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | Leapfrog | 0.1 m | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | Leapfrog | 0.1 m/s | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) | 0.1 m | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) | 0.1 m/s | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 | 0.1 m | 9.753E-008 | 6716 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 | 0.1 m/s | 9.145E-009 | 6716 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | SRKN14A | 0.1 m | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | SRKN14A | 0.1 m/s | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | WH-BM14A | 0.1 m | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | WH-BM14A | 0.1 m/s | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | GJ8 | 0.1 m | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | GJ8 | 0.1 m/s | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | Encke-IAS15 | 0.1 m | 9.273E-008 | 41232.644652843606 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | Encke-IAS15 | 0.1 m/s | 6.181E-009 | 41232.644652843606 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive [cap=600 s] | 0.1 m | - |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive [cap=600 s] | 0.1 m/s | - |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive [cap=86400 s] | 0.1 m | - |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive [cap=86400 s] | 0.1 m/s | - |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive [cap=600 s] | 0.1 m | - |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive [cap=600 s] | 0.1 m/s | - |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive [cap=86400 s] | 0.1 m | - |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive [cap=86400 s] | 0.1 m/s | - |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) [cap=600 s] | 0.1 m | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) [cap=600 s] | 0.1 m/s | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) [cap=86400 s] | 0.1 m | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) [cap=86400 s] | 0.1 m/s | - |  | n/a |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 [cap=600 s] | 0.1 m | - |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 [cap=600 s] | 0.1 m/s | - |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 [cap=86400 s] | 0.1 m | - |  | not run |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 [cap=86400 s] | 0.1 m/s | - |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853 [production] | 0.1 m | 4.433E-005 | 2368 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853 [production] | 0.1 m/s | 8.302E-006 | 2368 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5 [production] | 0.1 m | 2.139E-003 | 1408 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5 [production] | 0.1 m/s | 4.224E-004 | 1408 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | RK4 | 0.1 m | 1.265E-003 | 448 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | RK4 | 0.1 m/s | 2.554E-004 | 448 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-fixed | 0.1 m | 2.139E-003 | 736 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-fixed | 0.1 m/s | 4.223E-004 | 736 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive | 0.1 m | 2.139E-003 | 448 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive | 0.1 m/s | 4.224E-004 | 448 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive | 0.1 m | 4.435E-005 | 832 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive | 0.1 m/s | 8.302E-006 | 832 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | PEFRL | 0.1 m | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | PEFRL | 0.1 m/s | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | Y6 | 0.1 m | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | Y6 | 0.1 m/s | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | Y6C | 0.1 m | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | Y6C | 0.1 m/s | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | WH-Y6 | 0.1 m | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | WH-Y6 | 0.1 m/s | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | Leapfrog | 0.1 m | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | Leapfrog | 0.1 m/s | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) | 0.1 m | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) | 0.1 m/s | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 | 0.1 m | 7.710E-005 | 3781 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 | 0.1 m/s | 1.532E-005 | 3781 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | SRKN14A | 0.1 m | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | SRKN14A | 0.1 m/s | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | WH-BM14A | 0.1 m | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | WH-BM14A | 0.1 m/s | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | GJ8 | 0.1 m | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | GJ8 | 0.1 m/s | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | Encke-IAS15 | 0.1 m | 6.392E-006 | 34060.77879184555 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | Encke-IAS15 | 0.1 m/s | 1.250E-006 | 34060.77879184555 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive [cap=600 s] | 0.1 m | - |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive [cap=600 s] | 0.1 m/s | - |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive [cap=86400 s] | 0.1 m | - |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-adaptive [cap=86400 s] | 0.1 m/s | - |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive [cap=600 s] | 0.1 m | - |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive [cap=600 s] | 0.1 m/s | - |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive [cap=86400 s] | 0.1 m | - |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOP853-adaptive [cap=86400 s] | 0.1 m/s | - |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) [cap=600 s] | 0.1 m | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) [cap=600 s] | 0.1 m/s | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) [cap=86400 s] | 0.1 m | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | RKN12(10) [cap=86400 s] | 0.1 m/s | - |  | n/a |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 [cap=600 s] | 0.1 m | - |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 [cap=600 s] | 0.1 m/s | - |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 [cap=86400 s] | 0.1 m | - |  | not run |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | IAS15 [cap=86400 s] | 0.1 m/s | - |  | not run |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | DOP853 [production] | 10 km | 7.098E+003 | 16583 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | DOPRI5 [production] | 10 km | 2.908E+003 | 34634 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | RK4 | 10 km | 4.525E+003 | 29184 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | DOPRI5-fixed | 10 km | 4.977E+003 | 15728 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | DOPRI5-adaptive | 10 km | 7.500E+003 | 9866 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | DOP853-adaptive | 10 km | 2.055E+002 | 6210 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | PEFRL | 10 km | 5.204E+003 | 23360 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | Y6 | 10 km | 2.656E+003 | 15712 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | Y6C | 10 km | 2.656E+003 | 15712 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | WH-Y6 | 10 km | 1.325E-001 | 5835.769021676357 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | Leapfrog | 10 km | 1.026E+005 | 374128 | no_hit |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | RKN12(10) | 10 km | 3.749E-003 | 6624 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | IAS15 | 10 km | 5.840E-003 | 14382 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | SRKN14A | 10 km | 4.384E+003 | 5152 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | WH-BM14A | 10 km | 2.086E-002 | 11348.647903592191 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | GJ8 | 10 km | 1.084E-003 | 51820 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | Encke-IAS15 | 10 km | 7.323E-003 | 128261.21066492127 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | DOPRI5-adaptive [cap=600 s] | 10 km | 2.309E-002 | 315728 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | DOPRI5-adaptive [cap=86400 s] | 10 km | - |  | not run |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | DOP853-adaptive [cap=600 s] | 10 km | 3.305E-002 | 631088 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | DOP853-adaptive [cap=86400 s] | 10 km | - |  | not run |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | RKN12(10) [cap=600 s] | 10 km | 3.410E-002 | 893888 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | RKN12(10) [cap=86400 s] | 10 km | - |  | not run |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | IAS15 [cap=600 s] | 10 km | 1.153E-002 | 954517 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | IAS15 [cap=86400 s] | 10 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | DOP853 [production] | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | DOPRI5 [production] | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | RK4 | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | DOPRI5-fixed | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | DOPRI5-adaptive | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | DOP853-adaptive | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | PEFRL | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | Y6 | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | Y6C | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | WH-Y6 | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | Leapfrog | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | RKN12(10) | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | IAS15 | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | SRKN14A | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | WH-BM14A | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | GJ8 | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | Encke-IAS15 | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | DOPRI5-adaptive [cap=600 s] | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | DOPRI5-adaptive [cap=86400 s] | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | DOP853-adaptive [cap=600 s] | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | DOP853-adaptive [cap=86400 s] | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | RKN12(10) [cap=600 s] | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | RKN12(10) [cap=86400 s] | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | IAS15 [cap=600 s] | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y" | auto-bug | IAS15 [cap=86400 s] | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | DOP853 [production] | 100 km | 7.844E-002 | 135568 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | DOPRI5 [production] | 100 km | 1.347E+000 | 80608 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | RK4 | 100 km | 2.530E+003 | 14656 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | DOPRI5-fixed | 100 km | 1.230E+000 | 25648 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | DOPRI5-adaptive | 100 km | 1.230E+000 | 25648 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | DOP853-adaptive | 100 km | 1.014E-001 | 47632 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | PEFRL | 100 km | 4.260E+002 | 14656 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | Y6 | 100 km | 4.154E+000 | 29312 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | Y6C | 100 km | 4.187E+000 | 29312 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | WH-Y6 | 100 km | 4.178E+000 | 57757.32207410219 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | Leapfrog | 100 km | 7.156E+004 | 87616 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | RKN12(10) | 100 km | 1.603E-002 | 65952 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | IAS15 | 100 km | 6.981E-002 | 81784 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | SRKN14A | 100 km | 2.191E-001 | 51296 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | WH-BM14A | 100 km | 3.425E-001 | 112250.26158736183 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | GJ8 | 100 km | 6.981E-002 | 92776 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | Encke-IAS15 | 100 km | 4.119E-002 | 813975.000569287 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | DOPRI5-adaptive [cap=600 s] | 100 km | 4.238E-001 | 3157264 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | DOPRI5-adaptive [cap=86400 s] | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | DOP853-adaptive [cap=600 s] | 100 km | 4.004E-001 | 6310864 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | DOP853-adaptive [cap=86400 s] | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | RKN12(10) [cap=600 s] | 100 km | 3.530E-001 | 8938864 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | RKN12(10) [cap=86400 s] | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | IAS15 [cap=600 s] | 100 km | 6.591E-002 | 7887318 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400]" | regime86400 | IAS15 [cap=86400 s] | 100 km | - |  | not run |
