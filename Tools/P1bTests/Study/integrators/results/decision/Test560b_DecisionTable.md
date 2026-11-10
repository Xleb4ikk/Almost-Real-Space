# Test560b - decision table (compact)

Generated from `Test560b_DecisionMatrix.csv`; all numbers are executed runs.
`best_all` uses `cost_if_ported` for stand candidates; `best_production` uses
`cost_total`. Full acceptable percentages and selected totals are in
`Test560b_DecisionTable.csv` and `Test560b_DecisionDetails.md`.
`scope` is `config`/`max_step`/`chunk`; `bound` is the winner's heuristic label
and `production_bound` is the selected production row's label. `chunk` when
steps <= 1.2*chunks, `max-step` when
0.8*(duration/maxStep) <= steps <= 1.2*(duration/maxStep), otherwise `tolerance`.
It is not a proof of the controller state.
Per-method fixed-step `h_req`/`h_avg` is in the dedicated table
"Fixed-step h_req vs h_avg" below.

## Core decision table

| regime | scenario | scope | winner bound (heuristic) | production bound (heuristic) | target | best_production | best_ported | best_all | confidence | why |
|---|---|---|---|---|---|---|---|---|---|---|
| cislunar | "Cislunar Moon flyby, SOI entry-exit, 10 d" | config | chunk | chunk | 100 m | DOPRI5 [production] [kernel switch] | DOPRI5-adaptive (requires port) | tie: DOPRI5-adaptive (requires port)@30,096/1.18E-001, DOPRI5 [production]@33,408/2.25E-001 | low | best_all includes production; production option DOPRI5 [production] [kernel switch] is within 11.0% of best_all; production +11% vs best_all; edge 92,160 (206% more expensive than best_all, 176% more expensive than production); winner bound chunk, production bound chunk (heuristic) |
| far-flight | "Far flight 30 AU e 0.5, 10 y" | config | max-step | max-step | 100 km | DOPRI5 [production] [kernel switch] | Leapfrog (requires port) | Leapfrog (requires port) | medium | best_all is a port candidate; production +500% vs best_all; no edge runs; winner bound max-step-bound (600 s), production bound max-step-bound (600 s) (heuristic) |
| far-flight | "Far flight 30 AU e 0.5, 10 y [regime86400]" | config | chunk | chunk | 100 km | DOPRI5 [production] [kernel switch] | Leapfrog (requires port) | Leapfrog (requires port) | medium | best_all is a port candidate; production +450% vs best_all; no edge runs; winner bound chunk, production bound chunk (heuristic) |
| heliocentric | "Heliocentric 0.3 AU e 0.5, 1 y" | config | chunk | chunk | 10 km | DOPRI5 [production] [kernel switch] | PEFRL (requires port) | tie: PEFRL (requires port)@525,600/6.00E-005, RK4 (requires port)@525,600/1.22E-004 | low | best_all is a tie of port candidates; production +120% vs best_all; edge 3,679,200 (600% more expensive than best_all, 218% more expensive than production); winner bound chunk, production bound chunk (heuristic) |
| heliocentric | "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | config | chunk | chunk | 10 km | DOP853 [production] [current default] | WH-Y6 (requires port) | WH-Y6 (requires port) | medium | best_all is a port candidate; production +51% vs best_all; edge 30,350 (176% more expensive than best_all, 83% more expensive than production); winner bound chunk, production bound chunk (heuristic) |
| heliocentric | "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | config | chunk | chunk | 100 km | DOPRI5 [production] [kernel switch] | Leapfrog (requires port) | Leapfrog (requires port) | medium | best_all is a port candidate; production +450% vs best_all; no edge runs; winner bound chunk, production bound chunk (heuristic) |
| heliocentric | "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | config | chunk | chunk | 100 km | DOPRI5 [production] [kernel switch] | PEFRL (requires port) | tie: PEFRL (requires port)@36,640/4.26E-003, RK4 (requires port)@36,640/2.53E-002 | low | best_all is a tie of port candidates; production +120% vs best_all; edge 233,632 (538% more expensive than best_all, 190% more expensive than production); winner bound chunk, production bound chunk (heuristic) |
| high-orbit | "High orbit 200 000 km, 30 d" | config | chunk | chunk | 100 m | DOPRI5 [production] [kernel switch] | PEFRL (requires port) | tie: PEFRL (requires port)@43,200/1.71E-004, RK4 (requires port)@43,200/2.28E-004 | low | best_all is a tie of port candidates; production +120% vs best_all; edge 211,680 (390% more expensive than best_all, 123% more expensive than production); winner bound chunk, production bound chunk (heuristic) |
| high-orbit | "High orbit 35 786 km (GEO), 30 d" | config | chunk | chunk | 100 m | DOP853 [production] [current default] | tie: Y6 (requires port), Y6C (requires port) | tie: Y6 (requires port)@95,040/4.20E-002, Y6C (requires port)@95,040/4.20E-002, PEFRL (requires port)@95,040/3.11E-001 | low | best_all is a tie of port candidates; production +68% vs best_all; edge 82,080 (14% cheaper than best_all, 49% cheaper than production); winner bound chunk, production bound chunk (heuristic) |
| leo | "LEO 200 km, 1 d" | config | tolerance | chunk | 10 m | DOP853 [production] [current default] | DOPRI5-fixed (requires port) | tie: DOPRI5-fixed (requires port)@59,287/7.80E-001, DOP853-adaptive (requires port)@59,701/6.03E-002, DOP853 [production]@59,775/4.57E-002 | low | best_all includes production; production option DOP853 [production] [current default] is within 0.8% of best_all; production +1% vs best_all; edge 63,589 (7% more expensive than best_all, 6% more expensive than production); winner bound tolerance, production bound chunk (heuristic) |
| leo | "LEO 300 km, 30 d" | config | tolerance | tolerance | 100 m | DOP853 [production] [current default] | DOP853-adaptive (requires port) | DOP853-adaptive (requires port) | medium | best_all is a port candidate; production +45% vs best_all; edge 2,515,459 (282% more expensive than best_all, 163% more expensive than production); winner bound tolerance, production bound tolerance (heuristic) |
| surface | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | config | chunk | chunk | 0.1 m/s | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; winner bound chunk, production bound chunk (heuristic); prescribed thrust schedule; open-loop |
| surface | "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | config | chunk | chunk | 0.1 m | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; winner bound chunk, production bound chunk (heuristic); prescribed thrust schedule; open-loop |
| surface | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | config | chunk | chunk | 0.1 m/s | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; winner bound chunk, production bound chunk (heuristic); prescribed thrust schedule; open-loop |
| surface | "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | config | chunk | chunk | 0.1 m | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; winner bound chunk, production bound chunk (heuristic); prescribed thrust schedule; open-loop |

## Prescribed-force surface hover/level (separate section)

These are prescribed-force trajectories, not closed-loop altitude hold;
`ThrustScalesWithMass` uses a linear mass estimate for the force schedule.

| regime | scenario | scope | winner bound (heuristic) | production bound (heuristic) | target | best_production | best_ported | best_all | confidence | why |
|---|---|---|---|---|---|---|---|---|---|---|
| surface | "Surface hover 0.5 km, 0 m/s relative" | config | chunk | chunk | 0.1 m/s | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; prescribed-force; no closed-loop altitude hold; winner bound chunk, production bound chunk (heuristic) |
| surface | "Surface hover 0.5 km, 0 m/s relative" | config | chunk | chunk | 0.1 m | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; prescribed-force; no closed-loop altitude hold; winner bound chunk, production bound chunk (heuristic) |
| surface | "Surface level flight 0.5 km, 100 m/s horizontal" | config | chunk | chunk | 0.1 m/s | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; prescribed-force; no closed-loop altitude hold; winner bound chunk, production bound chunk (heuristic) |
| surface | "Surface level flight 0.5 km, 100 m/s horizontal" | config | chunk | chunk | 0.1 m | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; prescribed-force; no closed-loop altitude hold; winner bound chunk, production bound chunk (heuristic) |
| surface | "Surface level flight 1 km, 100 m/s horizontal" | config | chunk | chunk | 0.1 m | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; prescribed-force; no closed-loop altitude hold; winner bound chunk, production bound chunk (heuristic) |
| surface | "Surface level flight 1 km, 100 m/s horizontal" | config | chunk | chunk | 0.1 m/s | DOPRI5 [production] [kernel switch] | RK4 (requires port) | RK4 (requires port) | medium | best_all is a port candidate; production +120% vs best_all; no edge runs; prescribed-force; no closed-loop altitude hold; winner bound chunk, production bound chunk (heuristic) |

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
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | DOP853 [production] | 100 km | 7.844E-002 | 135568 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | DOPRI5 [production] | 100 km | 1.347E+000 | 80608 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | RK4 | 100 km | 2.530E+003 | 14656 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | DOPRI5-fixed | 100 km | 1.230E+000 | 25648 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | DOPRI5-adaptive | 100 km | 1.230E+000 | 25648 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | DOP853-adaptive | 100 km | 1.014E-001 | 47632 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | PEFRL | 100 km | 4.260E+002 | 14656 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | Y6 | 100 km | 4.154E+000 | 29312 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | Y6C | 100 km | 4.187E+000 | 29312 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | WH-Y6 | 100 km | 4.178E+000 | 57757.32207410219 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | Leapfrog | 100 km | 7.156E+004 | 87616 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | RKN12(10) | 100 km | 1.603E-002 | 65952 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | IAS15 | 100 km | 6.981E-002 | 81784 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | SRKN14A | 100 km | 2.191E-001 | 51296 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | WH-BM14A | 100 km | 3.425E-001 | 112250.26158736183 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | GJ8 | 100 km | 6.981E-002 | 92776 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | Encke-IAS15 | 100 km | 4.119E-002 | 813975.000569287 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | DOPRI5-adaptive [cap=600 s] | 100 km | 4.238E-001 | 3157264 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | DOPRI5-adaptive [cap=86400 s] | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | DOP853-adaptive [cap=600 s] | 100 km | 4.004E-001 | 6310864 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | DOP853-adaptive [cap=86400 s] | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | RKN12(10) [cap=600 s] | 100 km | 3.530E-001 | 8938864 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | RKN12(10) [cap=86400 s] | 100 km | - |  | not run |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | IAS15 [cap=600 s] | 100 km | 6.591E-002 | 7887318 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | IAS15 [cap=86400 s] | 100 km | - |  | not run |

## Max-step variants (informational, not mixed into best_all)

These are the same methods run with an explicit max step that differs from the
scenario's config max step. They are **not** candidates for `best_all` or
`best_production`; the production/regime86400 separation is unchanged.

| scenario | target | method | max step | config max step | err | cost total | ported | bound (heuristic) | status |
|---|---|---:|---:|---:|---:|---:|---:|---|---|
| "Far flight 30 AU e 0.5, 10 y" | 100 km | DOPRI5-adaptive [cap=86400 s] | 86400 | 600 | 9.174E-003 | 25648 | 69616 | chunk | ok |
| "Far flight 30 AU e 0.5, 10 y" | 100 km | DOP853-adaptive [cap=86400 s] | 86400 | 600 | 9.203E-003 | 47632 | 135568 | chunk | ok |
| "Far flight 30 AU e 0.5, 10 y" | 100 km | RKN12(10) [cap=86400 s] | 86400 | 600 | 5.720E-003 | 65952 | 190528 | chunk | ok |
| "Far flight 30 AU e 0.5, 10 y" | 100 km | IAS15 [cap=86400 s] | 86400 | 600 | 9.392E-003 | 80496 | 234160 | chunk | ok |

Far-flight DOPRI5-adaptive `[cap=86400 s]` is 69616 comparable / 0.00917 m at 100 km, 22.7x cheaper than best_all Leapfrog (requires port) (1,580,464). It is a max-step configuration, not the 600 s production default, and is not
selected in `best_all`.

## Fixed-step h_req vs h_avg

`h_req` is the fixed-step control requested from the grid/refinement; `h_avg` =
duration / accepted steps. They differ when several steps are packed into a chunk
(for example cislunar DOPRI5-fixed h_req=424.26 s really runs 300 s steps).
The 424.26 s refinement point is the same executed trajectory as h_req=300 s, so
it is kept in `Test560b_Runs.csv` (h_avg=300) but not duplicated in the selection list.

| scenario | config | method | target | h_req | h_avg | err | cost total | status |
|---|---|---|---:|---:|---:|---:|---:|---|
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | production | RK4 | 0.1 m | 0.05 | 0.04991680532445923 | 6.702E-006 | 4808 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | production | RK4 | 0.1 m/s | 0.05 | 0.04991680532445923 | 1.773E-007 | 4808 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | production | DOPRI5-fixed | 0.1 m | 0.05 | 0.04991680532445923 | 6.702E-006 | 8412 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | production | DOPRI5-fixed | 0.1 m/s | 0.05 | 0.04991680532445923 | 1.817E-007 | 8412 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | production | Encke-IAS15 | 0.1 m | 0.05 | 0.04991680532445923 | 7.817E-006 | 307600.5116618657 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22" | production | Encke-IAS15 | 0.1 m/s | 0.05 | 0.04991680532445923 | 1.291E-007 | 307600.5116618657 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | RK4 | 0.1 m | 0.5 | 0.46511627906976744 | 3.712E-004 | 516 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | RK4 | 0.1 m/s | 0.5 | 0.46511627906976744 | 3.394E-005 | 516 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-fixed | 0.1 m | 0.5 | 0.46511627906976744 | 2.307E-007 | 902 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-fixed | 0.1 m/s | 0.5 | 0.46511627906976744 | 5.595E-008 | 902 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | Encke-IAS15 | 0.1 m | 0.5 | 0.46511627906976744 | 9.273E-008 | 41232.644652843606 | ok |
| "Surface ascent 0.5 km, 100 m/s horizontal, TWR 1.22 [surface ascent chunk 0.5 s sensitivity]" | chunk-sweep | Encke-IAS15 | 0.1 m/s | 0.5 | 0.46511627906976744 | 6.181E-009 | 41232.644652843606 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | production | RK4 | 0.1 m | 0.05 | 0.049342105263157895 | 1.701E-007 | 2432 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | production | RK4 | 0.1 m/s | 0.05 | 0.049342105263157895 | 5.362E-008 | 2432 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | production | DOPRI5-fixed | 0.1 m | 0.05 | 0.049342105263157895 | 1.372E-007 | 4256 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | production | DOPRI5-fixed | 0.1 m/s | 0.05 | 0.049342105263157895 | 4.233E-008 | 4256 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | production | Encke-IAS15 | 0.1 m | 0.05 | 0.049342105263157895 | 4.233E-007 | 158302.64673520016 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal" | production | Encke-IAS15 | 0.1 m/s | 0.05 | 0.049342105263157895 | 2.921E-008 | 158302.64673520016 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | RK4 | 0.1 m | 0.46875 | 0.26785714285714285 | 1.265E-003 | 448 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | RK4 | 0.1 m/s | 0.46875 | 0.26785714285714285 | 2.554E-004 | 448 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-fixed | 0.1 m | 0.46875 | 0.26785714285714285 | 2.139E-003 | 736 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | DOPRI5-fixed | 0.1 m/s | 0.46875 | 0.26785714285714285 | 4.223E-004 | 736 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | Encke-IAS15 | 0.1 m | 0.46875 | 0.26785714285714285 | 6.392E-006 | 34060.77879184555 | ok |
| "Surface descent 2 km, -50 m/s vertical, 100 m/s horizontal [surface descent chunk 0.5 s sensitivity]" | chunk-sweep | Encke-IAS15 | 0.1 m/s | 0.46875 | 0.26785714285714285 | 1.250E-006 | 34060.77879184555 | ok |
| "Surface hover 0.5 km, 0 m/s relative" | production | RK4 | 0.1 m | 0.05 | 0.05 | 6.473E-006 | 4800 | ok |
| "Surface hover 0.5 km, 0 m/s relative" | production | RK4 | 0.1 m/s | 0.05 | 0.05 | 3.654E-009 | 4800 | ok |
| "Surface hover 0.5 km, 0 m/s relative" | production | DOPRI5-fixed | 0.1 m | 0.05 | 0.05 | 6.473E-006 | 8400 | ok |
| "Surface hover 0.5 km, 0 m/s relative" | production | DOPRI5-fixed | 0.1 m/s | 0.05 | 0.05 | 3.691E-009 | 8400 | ok |
| "Surface hover 0.5 km, 0 m/s relative" | production | Encke-IAS15 | 0.1 m | 0.05 | 0.05 | 1.222E-005 | 252245.34886314248 | ok |
| "Surface hover 0.5 km, 0 m/s relative" | production | Encke-IAS15 | 0.1 m/s | 0.05 | 0.05 | 3.472E-007 | 252245.34886314248 | ok |
| "Surface level flight 0.5 km, 100 m/s horizontal" | production | RK4 | 0.1 m | 0.05 | 0.05 | 7.365E-006 | 4800 | ok |
| "Surface level flight 0.5 km, 100 m/s horizontal" | production | RK4 | 0.1 m/s | 0.05 | 0.05 | 3.934E-008 | 4800 | ok |
| "Surface level flight 0.5 km, 100 m/s horizontal" | production | DOPRI5-fixed | 0.1 m | 0.05 | 0.05 | 7.362E-006 | 8400 | ok |
| "Surface level flight 0.5 km, 100 m/s horizontal" | production | DOPRI5-fixed | 0.1 m/s | 0.05 | 0.05 | 3.949E-008 | 8400 | ok |
| "Surface level flight 0.5 km, 100 m/s horizontal" | production | Encke-IAS15 | 0.1 m | 0.05 | 0.05 | 8.817E-006 | 311558.87280880555 | ok |
| "Surface level flight 0.5 km, 100 m/s horizontal" | production | Encke-IAS15 | 0.1 m/s | 0.05 | 0.05 | 9.484E-008 | 311558.87280880555 | ok |
| "Surface level flight 1 km, 100 m/s horizontal" | production | RK4 | 0.1 m | 0.05 | 0.05 | 7.832E-006 | 4800 | ok |
| "Surface level flight 1 km, 100 m/s horizontal" | production | RK4 | 0.1 m/s | 0.05 | 0.05 | 2.328E-008 | 4800 | ok |
| "Surface level flight 1 km, 100 m/s horizontal" | production | DOPRI5-fixed | 0.1 m | 0.05 | 0.05 | 7.836E-006 | 8400 | ok |
| "Surface level flight 1 km, 100 m/s horizontal" | production | DOPRI5-fixed | 0.1 m/s | 0.05 | 0.05 | 2.339E-008 | 8400 | ok |
| "Surface level flight 1 km, 100 m/s horizontal" | production | Encke-IAS15 | 0.1 m | 0.05 | 0.05 | 9.312E-006 | 310153.2851769782 | ok |
| "Surface level flight 1 km, 100 m/s horizontal" | production | Encke-IAS15 | 0.1 m/s | 0.05 | 0.05 | 9.695E-008 | 310153.2851769782 | ok |
| "LEO 200 km, 1 d" | production | RK4 | 10 m | 7.883403893003574 | 7.033539563660046 | 5.369E+000 | 49136 | ok |
| "LEO 200 km, 1 d" | production | DOPRI5-fixed | 10 m | 44.59526681260204 | 26.84063373718546 | 7.804E+000 | 20659 | ok |
| "LEO 200 km, 1 d" | production | Encke-IAS15 | 10 m | 600 | 64.09495548961425 | 1.515E-002 | 205178.74275353766 | ok |
| "LEO 300 km, 30 d" | production | RK4 | 100 m | 7.883403893003574 | 7.764291448495668 | 6.431E+001 | 1335344 | ok |
| "LEO 300 km, 30 d" | production | DOPRI5-fixed | 100 m | 9.375 | 9.334552971427337 | 3.123E+000 | 1671589 | ok |
| "LEO 300 km, 30 d" | production | Encke-IAS15 | 100 m | 600 | 469.22519913106447 | 4.606E-001 | 1474318.4817347967 | ok |
| "High orbit 35 786 km (GEO), 30 d" | production | RK4 | 100 m | 218.6924072023403 | 200 | 2.899E+001 | 51840 | ok |
| "High orbit 35 786 km (GEO), 30 d" | production | DOPRI5-fixed | 100 m | 156.21684288230003 | 150 | 9.767E-002 | 108000 | ok |
| "High orbit 35 786 km (GEO), 30 d" | production | PEFRL | 100 m | 306.15372891634036 | 300 | 3.111E+001 | 34560 | ok |
| "High orbit 35 786 km (GEO), 30 d" | production | Y6 | 100 m | 600 | 600 | 4.204E+000 | 34560 | ok |
| "High orbit 35 786 km (GEO), 30 d" | production | Y6C | 100 m | 600 | 600 | 4.204E+000 | 34560 | ok |
| "High orbit 35 786 km (GEO), 30 d" | production | WH-Y6 | 100 m | 600 | 600 | 1.659E-004 | 55265.877540868074 | ok |
| "High orbit 35 786 km (GEO), 30 d" | production | Leapfrog | 100 m | 2.3302619873873702 | 2.3255813953488373 | 7.614E+001 | 1118880 | ok |
| "High orbit 35 786 km (GEO), 30 d" | production | SRKN14A | 100 m | 600 | 600 | 1.450E-004 | 60480 | ok |
| "High orbit 35 786 km (GEO), 30 d" | production | WH-BM14A | 100 m | 600 | 600 | 3.202E-005 | 104849.73758757445 | ok |
| "High orbit 35 786 km (GEO), 30 d" | production | GJ8 | 100 m | 600 | 600 | 2.277E-005 | 266915 | ok |
| "High orbit 35 786 km (GEO), 30 d" | production | Encke-IAS15 | 100 m | 600 | 600 | 4.072E-005 | 1027499.5051888291 | ok |
| "High orbit 200 000 km, 30 d" | production | RK4 | 100 m | 600 | 600 | 2.281E-002 | 17280 | ok |
| "High orbit 200 000 km, 30 d" | production | DOPRI5-fixed | 100 m | 600 | 600 | 8.184E-005 | 30240 | ok |
| "High orbit 200 000 km, 30 d" | production | PEFRL | 100 m | 600 | 600 | 1.706E-002 | 17280 | ok |
| "High orbit 200 000 km, 30 d" | production | Y6 | 100 m | 600 | 600 | 5.015E-005 | 34560 | ok |
| "High orbit 200 000 km, 30 d" | production | Y6C | 100 m | 600 | 600 | 4.529E-005 | 34560 | ok |
| "High orbit 200 000 km, 30 d" | production | WH-Y6 | 100 m | 600 | 600 | 6.678E-005 | 54129.36542669584 | ok |
| "High orbit 200 000 km, 30 d" | production | Leapfrog | 100 m | 35.58850273433909 | 35.294117647058826 | 7.411E+001 | 77760 | ok |
| "High orbit 200 000 km, 30 d" | production | SRKN14A | 100 m | 600 | 600 | 3.447E-005 | 60480 | ok |
| "High orbit 200 000 km, 30 d" | production | WH-BM14A | 100 m | 600 | 600 | 3.767E-005 | 102414.3544857768 | ok |
| "High orbit 200 000 km, 30 d" | production | GJ8 | 100 m | 600 | 600 | 4.682E-005 | 111955 | ok |
| "High orbit 200 000 km, 30 d" | production | Encke-IAS15 | 100 m | 600 | 600 | 5.422E-005 | 824727.133155037 | ok |
| "Cislunar Moon flyby, SOI entry-exit, 10 d" | production | RK4 | 100 m | 89.19053362520408 | 85.71428571428571 | 8.000E+001 | 40320 | ok |
| "Cislunar Moon flyby, SOI entry-exit, 10 d" | production | DOPRI5-fixed | 100 m | 300 | 300 | 3.090E+001 | 18720 | ok |
| "Cislunar Moon flyby, SOI entry-exit, 10 d" | production | PEFRL | 100 m | 126.13446228805718 | 120 | 6.749E+001 | 28800 | ok |
| "Cislunar Moon flyby, SOI entry-exit, 10 d" | production | Y6 | 100 m | 150 | 150 | 1.508E+001 | 41760 | ok |
| "Cislunar Moon flyby, SOI entry-exit, 10 d" | production | Y6C | 100 m | 150 | 150 | 1.508E+001 | 41760 | ok |
| "Cislunar Moon flyby, SOI entry-exit, 10 d" | production | WH-Y6 | 100 m | 300 | 300 | 7.743E+001 | 73724.24283017719 | ok |
| "Cislunar Moon flyby, SOI entry-exit, 10 d" | production | Leapfrog | 100 m | 1.171875 | 1.171875 | 4.362E+001 | 738720 | ok |
| "Cislunar Moon flyby, SOI entry-exit, 10 d" | production | SRKN14A | 100 m | 600 | 600 | 8.709E-001 | 20160 | ok |
| "Cislunar Moon flyby, SOI entry-exit, 10 d" | production | WH-BM14A | 100 m | 600 | 600 | 9.659E-002 | 76007.40303233272 | ok |
| "Cislunar Moon flyby, SOI entry-exit, 10 d" | production | GJ8 | 100 m | 91.69562395312947 | 85.71428571428571 | 2.167E-003 | 225425 | ok |
| "Cislunar Moon flyby, SOI entry-exit, 10 d" | production | Encke-IAS15 | 100 m | 600 | 600 | 1.733E-003 | 582110.4575138525 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y" | production | RK4 | 10 km | 600 | 600 | 1.221E+000 | 210240 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y" | production | DOPRI5-fixed | 10 km | 600 | 600 | 2.309E-002 | 367920 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y" | production | PEFRL | 10 km | 600 | 600 | 6.000E-001 | 210240 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y" | production | Y6 | 10 km | 600 | 600 | 1.783E-001 | 420480 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y" | production | Y6C | 10 km | 600 | 600 | 7.948E-002 | 420480 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y" | production | WH-Y6 | 10 km | 600 | 600 | 1.748E-002 | 833500.0537481231 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y" | production | Leapfrog | 10 km | 22.053430523056218 | 21.428571428571427 | 6.617E+003 | 1524240 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y" | production | SRKN14A | 10 km | 600 | 600 | 2.391E-001 | 735840 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y" | production | WH-BM14A | 10 km | 600 | 600 | 6.649E-001 | 1620882.9723174064 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y" | production | GJ8 | 10 km | 600 | 600 | 3.407E-002 | 1314497 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y" | production | Encke-IAS15 | 10 km | 600 | 600 | 1.417E-001 | 13850610.325630464 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | RK4 | 10 km | 4540.840642370058 | 4322.368421052632 | 4.525E+003 | 29184 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | DOPRI5-fixed | 10 km | 12843.436842029387 | 12318.75 | 4.977E+003 | 15728 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | PEFRL | 10 km | 5400 | 5400 | 5.204E+003 | 23360 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | Y6 | 10 km | 15273.506473629426 | 14386.861313868612 | 2.656E+003 | 15712 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | Y6C | 10 km | 15273.506473629426 | 14386.861313868612 | 2.656E+003 | 15712 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | WH-Y6 | 10 km | 86400 | 85695.65217391304 | 1.325E-001 | 5835.769021676357 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | SRKN14A | 10 km | 86400 | 85695.65217391304 | 4.384E+003 | 5152 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | WH-BM14A | 10 km | 86400 | 85695.65217391304 | 2.086E-002 | 11348.647903592191 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | GJ8 | 10 km | 36667.310284499064 | 28985.29411764706 | 1.084E-003 | 51820 | ok |
| "Heliocentric 0.3 AU e 0.5, 1 y [regime86400]" | regime86400 | Encke-IAS15 | 10 km | 86400 | 85695.65217391304 | 7.323E-003 | 128261.21066492127 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | production | RK4 | 100 km | 600 | 600 | 4.197E-001 | 2102400 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | production | DOPRI5-fixed | 100 km | 600 | 600 | 4.238E-001 | 3679200 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | production | PEFRL | 100 km | 600 | 600 | 1.114E+000 | 2102400 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | production | Y6 | 100 km | 600 | 600 | 2.778E+000 | 4204800 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | production | Y6C | 100 km | 600 | 600 | 4.299E-001 | 4204800 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | production | WH-Y6 | 100 km | 600 | 600 | 1.776E+000 | 8285275.240760947 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | production | Leapfrog | 100 km | 600 | 600 | 1.825E+003 | 1051200 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | production | SRKN14A | 100 km | 600 | 600 | 4.287E-001 | 7358400 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | production | WH-BM14A | 100 km | 600 | 600 | 1.742E+001 | 16102275.515916314 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | production | GJ8 | 100 km | 600 | 600 | 4.120E-001 | 9504242 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [forced Sun]" | production | Encke-IAS15 | 100 km | 600 | 600 | 6.414E+000 | 102035429.03064662 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | RK4 | 100 km | 86400 | 86069.8689956332 | 2.530E+003 | 14656 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | DOPRI5-fixed | 100 km | 86400 | 86069.8689956332 | 1.230E+000 | 25648 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | PEFRL | 100 km | 86400 | 86069.8689956332 | 4.260E+002 | 14656 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | Y6 | 100 km | 86400 | 86069.8689956332 | 4.154E+000 | 29312 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | Y6C | 100 km | 86400 | 86069.8689956332 | 4.187E+000 | 29312 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | WH-Y6 | 100 km | 86400 | 86069.8689956332 | 4.178E+000 | 57757.32207410219 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | Leapfrog | 100 km | 3768.1583105087434 | 3756.432246998285 | 7.156E+004 | 87616 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | SRKN14A | 100 km | 86400 | 86069.8689956332 | 2.191E-001 | 51296 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | WH-BM14A | 100 km | 86400 | 86069.8689956332 | 3.425E-001 | 112250.26158736183 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | GJ8 | 100 km | 86400 | 86069.8689956332 | 6.981E-002 | 92776 | ok |
| "Heliocentric 5 AU e 0.3, 10 y [regime86400] [forced Sun]" | regime86400 | Encke-IAS15 | 100 km | 86400 | 86069.8689956332 | 4.119E-002 | 813975.000569287 | ok |
| "Far flight 30 AU e 0.5, 10 y" | production | RK4 | 100 km | 600 | 600 | 2.503E-001 | 2102400 | ok |
| "Far flight 30 AU e 0.5, 10 y" | production | DOPRI5-fixed | 100 km | 600 | 600 | 2.508E-001 | 3157264 | ok |
| "Far flight 30 AU e 0.5, 10 y" | production | PEFRL | 100 km | 600 | 600 | 2.376E-001 | 2102400 | ok |
| "Far flight 30 AU e 0.5, 10 y" | production | Y6 | 100 km | 600 | 600 | 1.397E-001 | 3682864 | ok |
| "Far flight 30 AU e 0.5, 10 y" | production | Y6C | 100 km | 600 | 600 | 9.174E-003 | 3682864 | ok |
| "Far flight 30 AU e 0.5, 10 y" | production | WH-Y6 | 100 km | 600 | 600 | 3.196E+000 | 7794673.069684051 | ok |
| "Far flight 30 AU e 0.5, 10 y" | production | Leapfrog | 100 km | 600 | 600 | 1.337E+000 | 529264 | ok |
| "Far flight 30 AU e 0.5, 10 y" | production | SRKN14A | 100 km | 600 | 600 | 5.361E-001 | 7358400 | ok |
| "Far flight 30 AU e 0.5, 10 y" | production | WH-BM14A | 100 km | 600 | 600 | 5.657E+001 | 16169419.435037253 | ok |
| "Far flight 30 AU e 0.5, 10 y" | production | GJ8 | 100 km | 600 | 600 | 6.694E-002 | 1468896 | ok |
| "Far flight 30 AU e 0.5, 10 y" | production | Encke-IAS15 | 100 km | 600 | 600 | 6.759E-001 | 102397186.4630782 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | RK4 | 100 km | 86400 | 86069.8689956332 | 9.265E-003 | 14656 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | DOPRI5-fixed | 100 km | 86400 | 86069.8689956332 | 9.174E-003 | 25648 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | PEFRL | 100 km | 86400 | 86069.8689956332 | 1.739E-002 | 14656 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | Y6 | 100 km | 86400 | 86069.8689956332 | 4.787E-002 | 29312 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | Y6C | 100 km | 86400 | 86069.8689956332 | 1.005E-002 | 29312 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | WH-Y6 | 100 km | 86400 | 86069.8689956332 | 3.514E-002 | 57975.752723216065 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | Leapfrog | 100 km | 86400 | 86069.8689956332 | 2.828E+004 | 7328 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | SRKN14A | 100 km | 86400 | 86069.8689956332 | 2.073E-002 | 51296 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | WH-BM14A | 100 km | 86400 | 86069.8689956332 | 4.102E-002 | 112718.32726403442 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | GJ8 | 100 km | 86400 | 86069.8689956332 | 9.392E-003 | 91488 | ok |
| "Far flight 30 AU e 0.5, 10 y [regime86400]" | regime86400 | Encke-IAS15 | 100 km | 86400 | 86069.8689956332 | 1.480E-002 | 775920.2982429579 | ok |

