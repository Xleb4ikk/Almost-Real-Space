# Test559: analysis

Everything below is computed from `Test559_PureGravity.csv` by `StudyRegimeAnalysis` (one code path). Interpolation, floor checks and tie bands are the same `StudyReport` logic used by the sweep, not a second implementation.

Classes: `D` decisive (margin > 2), `M` marginal (1.5-2), `E` equivalent (< 1.5, set listed), `F` floor-limited (a cheaper grid point already met the target), `T` trivial (cost < 200), `S` same-family reference (not a decisive accuracy claim), `U` unresolved/no winner.

## Cell table (1 m and 0.01 m)

| scenario | target | class | winner | cost | stages | margin | equivalent / floor-limited |
|---|---|---|---|---:|---:|---|---|
| Moon orbit 1 km | 1 m | F | DOP853-adaptive | 217 | 12 | 1.03 | floor: WH-Y6, RKN12(10), DOP853-adaptive [cap=600 s], WH-BM14A, RKN12(10) [cap=600 s], GJ8, IAS15 |
| Moon orbit 1 km | 0.01 m | F | WH-Y6 | 279 | 7 | 1.12 | floor: RKN12(10), WH-BM14A, RKN12(10) [cap=600 s], GJ8 |
| Moon orbit 10 km | 1 m | F | DOP853-adaptive | 217 | 12 | 1.03 | floor: WH-Y6, RKN12(10), WH-BM14A, DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], GJ8, IAS15, IAS15 [cap=600 s] |
| Moon orbit 10 km | 0.01 m | F | WH-Y6 | 191 | 7 | 1.64 | floor: RKN12(10), WH-BM14A, RKN12(10) [cap=600 s], GJ8, IAS15 [cap=600 s] |
| Moon orbit 100 km | 1 m | F | SRKN14A | 224 | 14 | 1.02 | floor: WH-Y6, RKN12(10), WH-BM14A, DOP853-adaptive [cap=600 s], RKN12(10) [cap=600 s], GJ8, IAS15, IAS15 [cap=600 s], Encke-IAS15 |
| Moon orbit 100 km | 0.01 m | F | WH-Y6 | 192 | 7 | 1.63 | floor: RKN12(10), WH-BM14A, RKN12(10) [cap=600 s], GJ8, IAS15 [cap=600 s], Encke-IAS15 |
| Moon orbit 1000 km | 1 m | F | SRKN14A | 224 | 14 | 1.02 | floor: WH-Y6, RKN12(10), WH-BM14A, DOP853-adaptive [cap=600 s], GJ8 |
| Moon orbit 1000 km | 0.01 m | F | WH-Y6 | 192 | 7 | 1.63 | floor: RKN12(10), WH-BM14A, DOP853-adaptive [cap=600 s], GJ8 |
| Moon orbit 100 km x10 orbits | 1 m | E | WH-BM14A | 733 | 14 | 1.02 | WH-BM14A, RKN12(10), WH-Y6 |
| Moon orbit 100 km x10 orbits | 0.01 m | M | WH-BM14A | 733 | 14 | 1.51 | WH-BM14A, WH-BM14A |
| Moon orbit 100 km x100 orbits | 1 m | E | WH-BM14A | 7,323 | 14 | 1.40 | WH-BM14A, RKN12(10), GJ8 |
| Moon orbit 100 km x100 orbits | 0.01 m | E | WH-BM14A | 12,204 | 14 | 1.12 | WH-BM14A, RKN12(10), GJ8 |
| Moon orbit 100 km x1000 orbits | 1 m | E | WH-BM14A | 1.18E+005 | 14 | 1.11 | WH-BM14A, RKN12(10), GJ8 |
| Moon orbit 100 km x1000 orbits | 0.01 m | U | - | - | 0 | - | - |
| Moon hop 50 m/s | 1 m | F | Leapfrog | 9 | 1 | - | floor: Leapfrog, RK4, PEFRL, GJ8 |
| Moon hop 50 m/s | 0.01 m | F | Leapfrog | 25 | 1 | inf | floor: RK4, PEFRL, GJ8 |
| Moon hop 200 m/s | 1 m | F | Leapfrog | 41 | 1 | inf | floor: RK4, PEFRL, DOPRI5-fixed, Y6, Y6C, DOPRI5-adaptive, DOPRI5-adaptive [cap=600 s], GJ8 |
| Moon hop 200 m/s | 0.01 m | F | Leapfrog | 281 | 1 | inf | floor: RK4, PEFRL, DOPRI5-fixed, Y6, Y6C, DOPRI5-adaptive, DOPRI5-adaptive [cap=600 s], GJ8 |
| Moon hop 500 m/s | 1 m | F | Leapfrog | 185 | 1 | inf | floor: RK4, PEFRL, DOPRI5-fixed, Y6, Y6C, DOPRI5-adaptive, DOPRI5-adaptive [cap=600 s], SRKN14A, DOP853-adaptive, DOP853-adaptive [cap=600 s], RKN12(10), RKN12(10) [cap=600 s], GJ8 |
| Moon hop 500 m/s | 0.01 m | F | PEFRL | 96 | 4 | 1.67 | floor: DOPRI5-fixed, Y6, Y6C, DOPRI5-adaptive, DOPRI5-adaptive [cap=600 s], SRKN14A, DOP853-adaptive, DOP853-adaptive [cap=600 s], RKN12(10), RKN12(10) [cap=600 s], GJ8 |
| Moon hop 1000 m/s | 1 m | F | DOPRI5-adaptive | 85 | 6 | 1.00 | floor: SRKN14A, WH-Y6, DOP853-adaptive, DOP853-adaptive [cap=600 s], RKN12(10), RKN12(10) [cap=600 s], GJ8 |
| Moon hop 1000 m/s | 0.01 m | F | DOPRI5-fixed | 145 | 6 | 1.16 | floor: SRKN14A, WH-Y6, RKN12(10), RKN12(10) [cap=600 s] |
| Moon hop 1200 m/s | 1 m | F | DOPRI5-fixed | 97 | 6 | 1.37 | floor: SRKN14A, WH-Y6, DOP853-adaptive, DOP853-adaptive [cap=600 s], RKN12(10), RKN12(10) [cap=600 s] |
| Moon hop 1200 m/s | 0.01 m | F | DOP853-adaptive | 168 | 12 | 1.00 | floor: WH-Y6, RKN12(10), RKN12(10) [cap=600 s] |
| Moon hop 1400 m/s | 1 m | F | DOPRI5-fixed | 97 | 6 | 1.16 | floor: SRKN14A, WH-Y6, DOP853-adaptive, DOP853-adaptive [cap=600 s], GJ8, IAS15, IAS15 [cap=600 s] |
| Moon hop 1400 m/s | 0.01 m | F | DOPRI5-adaptive | 235 | 6 | 1.00 | floor: SRKN14A, WH-Y6, DOP853-adaptive, DOP853-adaptive [cap=600 s], IAS15, IAS15 [cap=600 s] |
| Moon hop 1500 m/s | 1 m | F | DOPRI5-fixed | 97 | 6 | 1.16 | floor: SRKN14A, WH-Y6, DOP853-adaptive, DOP853-adaptive [cap=600 s], RKN12(10), RKN12(10) [cap=600 s], GJ8 |
| Moon hop 1500 m/s | 0.01 m | F | DOPRI5-fixed | 241 | 6 | 1.00 | floor: SRKN14A, WH-Y6, DOP853-adaptive, DOP853-adaptive [cap=600 s], RKN12(10), RKN12(10) [cap=600 s] |
| Moon hop 1600 m/s | 1 m | F | DOPRI5-fixed | 97 | 6 | 1.16 | floor: SRKN14A, WH-Y6, DOP853-adaptive, DOP853-adaptive [cap=600 s], RKN12(10), RKN12(10) [cap=600 s], GJ8 |
| Moon hop 1600 m/s | 0.01 m | F | DOPRI5-adaptive | 229 | 6 | 1.00 | floor: SRKN14A, WH-Y6, DOP853-adaptive, DOP853-adaptive [cap=600 s], RKN12(10), RKN12(10) [cap=600 s], GJ8 |
| Moon flyby vinf 50 alt 1 | 1 m | E | DOP853-adaptive | 409 | 12 | 1.12 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 50 alt 1 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.18 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 50 alt 10 | 1 m | E | DOP853-adaptive | 409 | 12 | 1.12 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 50 alt 10 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.18 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 50 alt 100 | 1 m | E | DOP853-adaptive | 409 | 12 | 1.12 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 50 alt 100 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.18 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 50 alt 1000 | 1 m | E | DOP853-adaptive | 409 | 12 | 1.12 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 50 alt 1000 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.29 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 200 alt 1 | 1 m | E | DOP853-adaptive | 409 | 12 | 1.12 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 200 alt 1 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.18 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 200 alt 10 | 1 m | E | DOP853-adaptive | 409 | 12 | 1.12 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 200 alt 10 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.18 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 200 alt 100 | 1 m | E | DOP853-adaptive | 409 | 12 | 1.12 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 200 alt 100 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.18 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 200 alt 1000 | 1 m | E | DOP853-adaptive | 409 | 12 | 1.12 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 200 alt 1000 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.29 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 500 alt 1 | 1 m | E | DOP853-adaptive | 409 | 12 | 1.12 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 500 alt 1 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.18 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 500 alt 10 | 1 m | E | DOP853-adaptive | 409 | 12 | 1.12 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 500 alt 10 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.18 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 500 alt 100 | 1 m | E | DOP853-adaptive | 409 | 12 | 1.12 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 500 alt 100 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.18 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 500 alt 1000 | 1 m | E | DOP853-adaptive | 397 | 12 | 1.16 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 500 alt 1000 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.18 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 1000 alt 1 | 1 m | E | DOP853-adaptive | 385 | 12 | 1.06 | DOP853-adaptive, DOPRI5-adaptive, RKN12(10) |
| Moon flyby vinf 1000 alt 1 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.11 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 1000 alt 10 | 1 m | E | DOP853-adaptive | 385 | 12 | 1.06 | DOP853-adaptive, DOPRI5-adaptive, RKN12(10) |
| Moon flyby vinf 1000 alt 10 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.11 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 1000 alt 100 | 1 m | E | DOP853-adaptive | 385 | 12 | 1.06 | DOP853-adaptive, DOPRI5-adaptive, RKN12(10) |
| Moon flyby vinf 1000 alt 100 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.11 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 1000 alt 1000 | 1 m | E | DOPRI5-adaptive | 385 | 6 | 1.00 | DOPRI5-adaptive, DOP853-adaptive, RKN12(10) |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.08 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 3000 alt 1 | 1 m | E | DOPRI5-adaptive | 283 | 6 | 1.32 | DOPRI5-adaptive, DOP853-adaptive |
| Moon flyby vinf 3000 alt 1 | 0.01 m | E | DOP853-adaptive | 409 | 12 | 1.12 | DOP853-adaptive, RKN12(10) |
| Moon flyby vinf 3000 alt 10 | 1 m | E | DOPRI5-adaptive | 283 | 6 | 1.32 | DOPRI5-adaptive, DOP853-adaptive |
| Moon flyby vinf 3000 alt 10 | 0.01 m | E | DOP853-adaptive | 409 | 12 | 1.17 | DOP853-adaptive, RKN12(10) |
| Moon flyby vinf 3000 alt 100 | 1 m | E | DOPRI5-adaptive | 283 | 6 | 1.32 | DOPRI5-adaptive, DOP853-adaptive |
| Moon flyby vinf 3000 alt 100 | 0.01 m | E | DOP853-adaptive | 433 | 12 | 1.06 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 3000 alt 1000 | 1 m | E | DOPRI5-adaptive | 313 | 6 | 1.23 | DOPRI5-adaptive, DOP853-adaptive, RKN12(10) |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | E | DOP853-adaptive | 409 | 12 | 1.17 | DOP853-adaptive, RKN12(10) |
| Moon flyby vinf 10000 alt 1 | 1 m | E | DOPRI5-adaptive | 241 | 6 | 1.40 | DOPRI5-adaptive, DOPRI5-adaptive [cap=600 s], DOP853-adaptive |
| Moon flyby vinf 10000 alt 1 | 0.01 m | E | DOP853-adaptive | 409 | 12 | 1.08 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive, DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s] |
| Moon flyby vinf 10000 alt 10 | 1 m | E | DOPRI5-adaptive | 241 | 6 | 1.40 | DOPRI5-adaptive, DOPRI5-adaptive [cap=600 s], DOP853-adaptive |
| Moon flyby vinf 10000 alt 10 | 0.01 m | E | DOP853-adaptive | 409 | 12 | 1.08 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive, DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s] |
| Moon flyby vinf 10000 alt 100 | 1 m | E | DOPRI5-adaptive | 241 | 6 | 1.40 | DOPRI5-adaptive, DOPRI5-adaptive [cap=600 s], DOP853-adaptive |
| Moon flyby vinf 10000 alt 100 | 0.01 m | E | DOP853-adaptive | 409 | 12 | 1.08 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive, DOPRI5-adaptive [cap=600 s], DOP853-adaptive [cap=600 s] |
| Moon flyby vinf 10000 alt 1000 | 1 m | E | DOPRI5-adaptive | 253 | 6 | 1.38 | DOPRI5-adaptive, DOP853-adaptive |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | E | DOP853-adaptive | 409 | 12 | 1.08 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Heliocentric 0.3 AU e 0 | 1 m | F | GJ8 | 500 | 1 | 1.01 | floor: RKN12(10), WH-Y6, WH-BM14A |
| Heliocentric 0.3 AU e 0 | 0.01 m | F | RKN12(10) | 389 | 16 | 2.25 | floor: WH-BM14A |
| Heliocentric 0.3 AU e 0.5 | 1 m | F | RKN12(10) | 355 | 16 | 1.65 | floor: WH-BM14A |
| Heliocentric 0.3 AU e 0.5 | 0.01 m | E | RKN12(10) | 506 | 16 | 1.44 | RKN12(10), WH-BM14A |
| Heliocentric 0.3 AU e 0.9 | 1 m | S | RKN12(10) | 997 | 16 | 1.95 | RKN12(10), RKN12(10) |
| Heliocentric 0.3 AU e 0.9 | 0.01 m | S | RKN12(10) | 1,264 | 16 | 8.47 | - |
| Heliocentric 0.3 AU e 0.99 | 1 m | U | - | - | 0 | - | - |
| Heliocentric 0.3 AU e 0.99 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 1 AU e 0 | 1 m | S | RKN12(10) | 238 | 16 | 2.42 | - |
| Heliocentric 1 AU e 0 | 0.01 m | S | RKN12(10) | 423 | 16 | 2.30 | - |
| Heliocentric 1 AU e 0.5 | 1 m | S | RKN12(10) | 490 | 16 | 1.85 | RKN12(10), RKN12(10) |
| Heliocentric 1 AU e 0.5 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 1 AU e 0.9 | 1 m | M | RKN12(10) | 1,131 | 16 | 1.79 | RKN12(10), RKN12(10) |
| Heliocentric 1 AU e 0.9 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 1 AU e 0.99 | 1 m | U | - | - | 0 | - | - |
| Heliocentric 1 AU e 0.99 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 5 AU e 0 | 1 m | F | DOPRI5-adaptive | 199 | 6 | 2.89 | floor: DOP853-adaptive, RKN12(10), IAS15 |
| Heliocentric 5 AU e 0 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 5 AU e 0.5 | 1 m | F | DOP853-adaptive | 217 | 12 | 2.38 | floor: RKN12(10), IAS15, DOPRI5-fixed, Y6, Y6C |
| Heliocentric 5 AU e 0.5 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 5 AU e 0.9 | 1 m | S | RKN12(10) | 374 | 16 | 1.51 | RKN12(10), RKN12(10) |
| Heliocentric 5 AU e 0.9 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 5 AU e 0.99 | 1 m | E | RKN12(10) | 1,735 | 16 | 1.14 | RKN12(10), DOP853-adaptive |
| Heliocentric 5 AU e 0.99 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 30 AU e 0 | 1 m | T | DOPRI5-adaptive | 103 | 6 | 1.98 | - |
| Heliocentric 30 AU e 0 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 30 AU e 0.5 | 1 m | F | DOPRI5-adaptive | 115 | 6 | inf | floor: DOP853-adaptive |
| Heliocentric 30 AU e 0.5 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 30 AU e 0.9 | 1 m | S | DOP853-adaptive | 228 | 12 | 1.61 | DOP853-adaptive, DOP853-adaptive |
| Heliocentric 30 AU e 0.9 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 30 AU e 0.99 | 1 m | S | RKN12(10) | 375 | 16 | 1.74 | RKN12(10), RKN12(10) |
| Heliocentric 30 AU e 0.99 | 0.01 m | U | - | - | 0 | - | - |
| Far flight 30 AU e 0.5 (physical) | 1 m | F | DOPRI5-adaptive | 115 | 6 | inf | floor: DOP853-adaptive |
| Far flight 30 AU e 0.5 (physical) | 0.01 m | U | - | - | 0 | - | - |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | F | DOPRI5-adaptive | 145 | 6 | 1.00 | floor: DOP853-adaptive, DOP853-adaptive [cap=600 s], RKN12(10), RKN12(10) [cap=600 s], IAS15, IAS15 [cap=600 s], GJ8, RK4, PEFRL, DOPRI5-fixed, Y6, Y6C, SRKN14A, WH-Y6, WH-BM14A, Encke-IAS15 |
| Far flight 30 AU e 0.5 (physical) (normalized) | 0.01 m | U | - | - | 0 | - | - |
| Earth flyby 1.1 R vinf 1000 | 1 m | E | DOP853-adaptive | 445 | 12 | 1.03 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | E | RKN12(10) | 494 | 16 | 1.39 | RKN12(10), DOP853-adaptive |
| Earth flyby 1.1 R vinf 10000 | 1 m | E | DOPRI5-adaptive | 355 | 6 | 1.12 | DOPRI5-adaptive, DOP853-adaptive, RKN12(10) |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.06 | RKN12(10), DOP853-adaptive |
| Earth flyby 3 R vinf 1000 | 1 m | E | RKN12(10) | 460 | 16 | 1.05 | RKN12(10), DOP853-adaptive |
| Earth flyby 3 R vinf 1000 | 0.01 m | E | RKN12(10) | 494 | 16 | 1.39 | RKN12(10), DOP853-adaptive |
| Earth flyby 3 R vinf 10000 | 1 m | E | DOP853-adaptive | 397 | 12 | 1.02 | DOP853-adaptive, DOPRI5-adaptive, RKN12(10) |
| Earth flyby 3 R vinf 10000 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.06 | RKN12(10), DOP853-adaptive |
| Earth flyby 10 R vinf 1000 | 1 m | E | RKN12(10) | 477 | 16 | 1.08 | RKN12(10), DOP853-adaptive |
| Earth flyby 10 R vinf 1000 | 0.01 m | E | RKN12(10) | 528 | 16 | 1.41 | RKN12(10), DOP853-adaptive |
| Earth flyby 10 R vinf 10000 | 1 m | E | DOP853-adaptive | 409 | 12 | 1.04 | DOP853-adaptive, DOPRI5-adaptive, RKN12(10) |
| Earth flyby 10 R vinf 10000 | 0.01 m | E | RKN12(10) | 494 | 16 | 1.10 | RKN12(10), DOP853-adaptive |
| Small-body flyby vinf 10 | 1 m | E | DOPRI5-adaptive | 229 | 6 | 1.00 | DOPRI5-adaptive, DOPRI5-adaptive [cap=600 s], DOP853-adaptive, DOP853-adaptive [cap=600 s] |
| Small-body flyby vinf 10 | 0.01 m | E | DOPRI5-adaptive | 373 | 6 | 1.00 | DOPRI5-adaptive, DOPRI5-adaptive [cap=600 s], DOP853-adaptive, DOP853-adaptive [cap=600 s], RKN12(10), RKN12(10) [cap=600 s] |
| Small-body flyby vinf 50 | 1 m | E | DOPRI5-adaptive | 229 | 6 | 1.00 | DOPRI5-adaptive, DOPRI5-adaptive [cap=600 s], DOP853-adaptive, DOP853-adaptive [cap=600 s] |
| Small-body flyby vinf 50 | 0.01 m | E | DOPRI5-adaptive | 373 | 6 | 1.00 | DOPRI5-adaptive, DOPRI5-adaptive [cap=600 s], DOP853-adaptive, DOP853-adaptive [cap=600 s], RKN12(10), RKN12(10) [cap=600 s] |
| Small-body flyby vinf 100 | 1 m | E | DOPRI5-adaptive | 229 | 6 | 1.00 | DOPRI5-adaptive, DOPRI5-adaptive [cap=600 s], DOP853-adaptive, DOP853-adaptive [cap=600 s] |
| Small-body flyby vinf 100 | 0.01 m | E | DOPRI5-adaptive | 373 | 6 | 1.00 | DOPRI5-adaptive, DOPRI5-adaptive [cap=600 s], DOP853-adaptive, DOP853-adaptive [cap=600 s], RKN12(10), RKN12(10) [cap=600 s] |

## 2D maps

### Heliocentric a x e — 1 m

| a \ e | 0 | 0.5 | 0.9 | 0.99 |
|---|---|---|---|---|
| 0.3 | floor: RKN12(10), WH-Y6, WH-BM14A | floor: WH-BM14A | RKN12(10) S | — |
| 1 | RKN12(10) S | RKN12(10) S | RKN12(10) M 1.79 | — |
| 5 | floor: DOP853-adaptive, RKN12(10), IAS15 | floor: RKN12(10), IAS15, DOPRI5-fixed, Y6, Y6C | RKN12(10) S | RKN12(10) ~ DOP853-adaptive E |
| 30 | DOPRI5-adaptive T (6 st) | floor: DOP853-adaptive | DOP853-adaptive S | RKN12(10) S |

### Heliocentric a x e — 0.01 m

| a \ e | 0 | 0.5 | 0.9 | 0.99 |
|---|---|---|---|---|
| 0.3 | floor: WH-BM14A | RKN12(10) ~ WH-BM14A E | RKN12(10) S | — |
| 1 | RKN12(10) S | — | — | — |
| 5 | — | — | — | — |
| 30 | — | — | — | — |

### Planet flyby periapsis x v_inf — 1 m

| R \ v_inf | 1000 | 10000 |
|---|---|---|
| 1.1 | DOP853-adaptive ~ RKN12(10) ~ DOPRI5-adaptive E | DOPRI5-adaptive ~ DOP853-adaptive ~ RKN12(10) E |
| 3 | RKN12(10) ~ DOP853-adaptive E | DOP853-adaptive ~ DOPRI5-adaptive ~ RKN12(10) E |
| 10 | RKN12(10) ~ DOP853-adaptive E | DOP853-adaptive ~ DOPRI5-adaptive ~ RKN12(10) E |

### Planet flyby periapsis x v_inf — 0.01 m

| R \ v_inf | 1000 | 10000 |
|---|---|---|
| 1.1 | RKN12(10) ~ DOP853-adaptive E | RKN12(10) ~ DOP853-adaptive E |
| 3 | RKN12(10) ~ DOP853-adaptive E | RKN12(10) ~ DOP853-adaptive E |
| 10 | RKN12(10) ~ DOP853-adaptive E | RKN12(10) ~ DOP853-adaptive E |

## Floor-limited detail

For every method in a floor-limited/equivalent cell: cost and error at its cheapest tested grid point, and whether that point already met the target. A method whose cheapest point is the grid minimum cannot be priced lower without a genuinely coarser run; it is not an equivalent method.

| scenario | target | method | cheapest cost | error at cheapest | meets target | class |
|---|---|---|---:|---:|---|---|
| Moon orbit 1 km | 1 m | WH-Y6 | 140 | 0.01 | yes | EDGE |
| Moon orbit 1 km | 1 m | RKN12(10) | 188 | 0.00 | yes | EDGE |
| Moon orbit 1 km | 1 m | DOP853-adaptive | 133 | 6.44 | no | OK |
| Moon orbit 1 km | 1 m | SRKN14A | 112 | 11.41 | no | OK |
| Moon orbit 1 km | 1 m | DOP853-adaptive [cap=600 s] | 229 | 0.44 | yes | EDGE |
| Moon orbit 1 km | 1 m | WH-BM14A | 290 | 0.00 | yes | EDGE |
| Moon orbit 1 km | 1 m | RKN12(10) [cap=600 s] | 324 | 0.00 | yes | EDGE |
| Moon orbit 1 km | 1 m | DOPRI5-adaptive | 67 | 148104.51 | no | OK |
| Moon orbit 1 km | 1 m | DOPRI5-adaptive [cap=600 s] | 115 | 18608.34 | no | OK |
| Moon orbit 1 km | 1 m | DOPRI5-fixed | 49 | 180746.91 | no | OK |
| Moon orbit 1 km | 1 m | GJ8 | 452 | 0.00 | yes | EDGE |
| Moon orbit 1 km | 1 m | Y6C | 57 | 115034.48 | no | OK |
| Moon orbit 1 km | 1 m | Y6 | 57 | 115034.48 | no | OK |
| Moon orbit 1 km | 1 m | IAS15 | 593 | 0.02 | yes | EDGE |
| Moon orbit 1 km | 1 m | PEFRL | 32 | 66907.93 | no | OK |
| Moon orbit 1 km | 1 m | RK4 | 32 | 585918.46 | no | OK |
| Moon orbit 1 km | 1 m | IAS15 [cap=600 s] | 908 | 0.00 | yes | OK |
| Moon orbit 1 km | 1 m | Leapfrog | 9 | 2093654.13 | no | OK |
| Moon orbit 1 km | 0.01 m | RKN12(10) | 188 | 0.00 | yes | EDGE |
| Moon orbit 1 km | 0.01 m | WH-Y6 | 140 | 0.01 | no | OK |
| Moon orbit 1 km | 0.01 m | WH-BM14A | 290 | 0.00 | yes | EDGE |
| Moon orbit 1 km | 0.01 m | DOP853-adaptive | 133 | 6.44 | no | OK |
| Moon orbit 1 km | 0.01 m | DOP853-adaptive [cap=600 s] | 229 | 0.44 | no | OK |
| Moon orbit 1 km | 0.01 m | RKN12(10) [cap=600 s] | 324 | 0.00 | yes | EDGE |
| Moon orbit 1 km | 0.01 m | SRKN14A | 112 | 11.41 | no | OK |
| Moon orbit 1 km | 0.01 m | GJ8 | 452 | 0.00 | yes | EDGE |
| Moon orbit 1 km | 0.01 m | IAS15 | 593 | 0.02 | no | OK |
| Moon orbit 1 km | 0.01 m | DOPRI5-adaptive | 67 | 148104.51 | no | OK |
| Moon orbit 1 km | 0.01 m | DOPRI5-adaptive [cap=600 s] | 115 | 18608.34 | no | OK |
| Moon orbit 1 km | 0.01 m | IAS15 [cap=600 s] | 908 | 0.00 | yes | OK |
| Moon orbit 1 km | 0.01 m | DOPRI5-fixed | 49 | 180746.91 | no | OK |
| Moon orbit 1 km | 0.01 m | Y6C | 57 | 115034.48 | no | OK |
| Moon orbit 1 km | 0.01 m | Y6 | 57 | 115034.48 | no | OK |
| Moon orbit 1 km | 0.01 m | PEFRL | 32 | 66907.93 | no | OK |
| Moon orbit 1 km | 0.01 m | RK4 | 32 | 585918.46 | no | OK |
| Moon orbit 10 km | 1 m | WH-Y6 | 96 | 0.01 | yes | EDGE |
| Moon orbit 10 km | 1 m | RKN12(10) | 188 | 0.00 | yes | EDGE |
| Moon orbit 10 km | 1 m | WH-BM14A | 196 | 0.00 | yes | EDGE |
| Moon orbit 10 km | 1 m | DOP853-adaptive | 133 | 6.47 | no | OK |
| Moon orbit 10 km | 1 m | SRKN14A | 112 | 11.47 | no | OK |
| Moon orbit 10 km | 1 m | DOP853-adaptive [cap=600 s] | 229 | 0.42 | yes | EDGE |
| Moon orbit 10 km | 1 m | RKN12(10) [cap=600 s] | 324 | 0.00 | yes | EDGE |
| Moon orbit 10 km | 1 m | DOPRI5-adaptive | 67 | 148870.94 | no | OK |
| Moon orbit 10 km | 1 m | DOPRI5-adaptive [cap=600 s] | 115 | 17654.72 | no | OK |
| Moon orbit 10 km | 1 m | DOPRI5-fixed | 49 | 181682.35 | no | OK |
| Moon orbit 10 km | 1 m | GJ8 | 452 | 0.00 | yes | EDGE |
| Moon orbit 10 km | 1 m | Y6C | 57 | 115629.87 | no | OK |
| Moon orbit 10 km | 1 m | Y6 | 57 | 115629.87 | no | OK |
| Moon orbit 10 km | 1 m | IAS15 | 593 | 0.02 | yes | EDGE |
| Moon orbit 10 km | 1 m | PEFRL | 32 | 67254.30 | no | OK |
| Moon orbit 10 km | 1 m | RK4 | 32 | 588951.48 | no | OK |
| Moon orbit 10 km | 1 m | IAS15 [cap=600 s] | 894 | 0.00 | yes | EDGE |
| Moon orbit 10 km | 1 m | Leapfrog | 9 | 2104494.91 | no | OK |
| Moon orbit 10 km | 0.01 m | RKN12(10) | 188 | 0.00 | yes | EDGE |
| Moon orbit 10 km | 0.01 m | WH-Y6 | 96 | 0.01 | no | OK |
| Moon orbit 10 km | 0.01 m | WH-BM14A | 196 | 0.00 | yes | EDGE |
| Moon orbit 10 km | 0.01 m | DOP853-adaptive | 133 | 6.47 | no | OK |
| Moon orbit 10 km | 0.01 m | DOP853-adaptive [cap=600 s] | 229 | 0.42 | no | OK |
| Moon orbit 10 km | 0.01 m | RKN12(10) [cap=600 s] | 324 | 0.00 | yes | EDGE |
| Moon orbit 10 km | 0.01 m | SRKN14A | 112 | 11.47 | no | OK |
| Moon orbit 10 km | 0.01 m | GJ8 | 452 | 0.00 | yes | EDGE |
| Moon orbit 10 km | 0.01 m | IAS15 | 593 | 0.02 | no | OK |
| Moon orbit 10 km | 0.01 m | DOPRI5-adaptive | 67 | 148870.94 | no | OK |
| Moon orbit 10 km | 0.01 m | DOPRI5-adaptive [cap=600 s] | 115 | 17654.72 | no | OK |
| Moon orbit 10 km | 0.01 m | IAS15 [cap=600 s] | 894 | 0.00 | yes | EDGE |
| Moon orbit 10 km | 0.01 m | DOPRI5-fixed | 49 | 181682.35 | no | OK |
| Moon orbit 10 km | 0.01 m | Y6C | 57 | 115629.87 | no | OK |
| Moon orbit 10 km | 0.01 m | Y6 | 57 | 115629.87 | no | OK |
| Moon orbit 10 km | 0.01 m | PEFRL | 32 | 67254.30 | no | OK |
| Moon orbit 10 km | 0.01 m | RK4 | 32 | 588951.48 | no | OK |
| Moon orbit 100 km | 1 m | WH-Y6 | 96 | 0.01 | yes | EDGE |
| Moon orbit 100 km | 1 m | RKN12(10) | 188 | 0.00 | yes | EDGE |
| Moon orbit 100 km | 1 m | WH-BM14A | 196 | 0.00 | yes | EDGE |
| Moon orbit 100 km | 1 m | SRKN14A | 112 | 12.06 | no | OK |
| Moon orbit 100 km | 1 m | DOP853-adaptive | 133 | 6.80 | no | OK |
| Moon orbit 100 km | 1 m | DOP853-adaptive [cap=600 s] | 229 | 0.22 | yes | EDGE |
| Moon orbit 100 km | 1 m | RKN12(10) [cap=600 s] | 324 | 0.00 | yes | EDGE |
| Moon orbit 100 km | 1 m | DOPRI5-adaptive | 67 | 156534.67 | no | OK |
| Moon orbit 100 km | 1 m | DOPRI5-adaptive [cap=600 s] | 115 | 10594.41 | no | OK |
| Moon orbit 100 km | 1 m | DOPRI5-fixed | 49 | 191036.09 | no | OK |
| Moon orbit 100 km | 1 m | GJ8 | 459 | 0.00 | yes | EDGE |
| Moon orbit 100 km | 1 m | Y6 | 57 | 121583.39 | no | OK |
| Moon orbit 100 km | 1 m | Y6C | 57 | 121583.39 | no | OK |
| Moon orbit 100 km | 1 m | IAS15 | 586 | 0.02 | yes | EDGE |
| Moon orbit 100 km | 1 m | RK4 | 32 | 619280.38 | no | OK |
| Moon orbit 100 km | 1 m | PEFRL | 32 | 70717.87 | no | OK |
| Moon orbit 100 km | 1 m | IAS15 [cap=600 s] | 908 | 0.00 | yes | EDGE |
| Moon orbit 100 km | 1 m | Encke-IAS15 | 4,085 | 0.00 | yes | EDGE |
| Moon orbit 100 km | 1 m | Leapfrog | 9 | 2212901.54 | no | OK |
| Moon orbit 100 km | 0.01 m | RKN12(10) | 188 | 0.00 | yes | EDGE |
| Moon orbit 100 km | 0.01 m | WH-Y6 | 96 | 0.01 | no | OK |
| Moon orbit 100 km | 0.01 m | WH-BM14A | 196 | 0.00 | yes | EDGE |
| Moon orbit 100 km | 0.01 m | DOP853-adaptive | 133 | 6.80 | no | OK |
| Moon orbit 100 km | 0.01 m | DOP853-adaptive [cap=600 s] | 229 | 0.22 | no | OK |
| Moon orbit 100 km | 0.01 m | RKN12(10) [cap=600 s] | 324 | 0.00 | yes | EDGE |
| Moon orbit 100 km | 0.01 m | SRKN14A | 112 | 12.06 | no | OK |
| Moon orbit 100 km | 0.01 m | GJ8 | 459 | 0.00 | yes | EDGE |
| Moon orbit 100 km | 0.01 m | IAS15 | 586 | 0.02 | no | OK |
| Moon orbit 100 km | 0.01 m | DOPRI5-adaptive | 67 | 156534.67 | no | OK |
| Moon orbit 100 km | 0.01 m | DOPRI5-adaptive [cap=600 s] | 115 | 10594.41 | no | OK |
| Moon orbit 100 km | 0.01 m | IAS15 [cap=600 s] | 908 | 0.00 | yes | EDGE |
| Moon orbit 100 km | 0.01 m | DOPRI5-fixed | 49 | 191036.09 | no | OK |
| Moon orbit 100 km | 0.01 m | Y6C | 57 | 121583.39 | no | OK |
| Moon orbit 100 km | 0.01 m | Y6 | 57 | 121583.39 | no | OK |
| Moon orbit 100 km | 0.01 m | RK4 | 32 | 619280.38 | no | OK |
| Moon orbit 100 km | 0.01 m | PEFRL | 32 | 70717.87 | no | OK |
| Moon orbit 100 km | 0.01 m | Encke-IAS15 | 4,085 | 0.00 | yes | EDGE |
| Moon orbit 1000 km | 1 m | WH-Y6 | 96 | 0.06 | yes | EDGE |
| Moon orbit 1000 km | 1 m | RKN12(10) | 188 | 0.00 | yes | EDGE |
| Moon orbit 1000 km | 1 m | WH-BM14A | 197 | 0.00 | yes | EDGE |
| Moon orbit 1000 km | 1 m | SRKN14A | 112 | 17.96 | no | OK |
| Moon orbit 1000 km | 1 m | DOP853-adaptive | 133 | 10.14 | no | OK |
| Moon orbit 1000 km | 1 m | DOP853-adaptive [cap=600 s] | 325 | 0.00 | yes | EDGE |
| Moon orbit 1000 km | 1 m | DOPRI5-adaptive | 67 | 233086.19 | no | OK |
| Moon orbit 1000 km | 1 m | DOPRI5-adaptive [cap=600 s] | 163 | 335.94 | no | OK |
| Moon orbit 1000 km | 1 m | DOPRI5-fixed | 49 | 284482.86 | no | OK |
| Moon orbit 1000 km | 1 m | GJ8 | 452 | 0.00 | yes | EDGE |
| Moon orbit 1000 km | 1 m | Y6 | 57 | 181067.27 | no | OK |
| Moon orbit 1000 km | 1 m | Y6C | 57 | 181067.27 | no | OK |
| Moon orbit 1000 km | 1 m | PEFRL | 32 | 105336.11 | no | OK |
| Moon orbit 1000 km | 1 m | RK4 | 32 | 922386.09 | no | OK |
| Moon orbit 1000 km | 1 m | Leapfrog | 9 | 3296813.48 | no | OK |
| Moon orbit 1000 km | 0.01 m | RKN12(10) | 188 | 0.00 | yes | EDGE |
| Moon orbit 1000 km | 0.01 m | WH-Y6 | 96 | 0.06 | no | OK |
| Moon orbit 1000 km | 0.01 m | WH-BM14A | 197 | 0.00 | yes | EDGE |
| Moon orbit 1000 km | 0.01 m | DOP853-adaptive | 133 | 10.14 | no | OK |
| Moon orbit 1000 km | 0.01 m | DOP853-adaptive [cap=600 s] | 325 | 0.00 | yes | EDGE |
| Moon orbit 1000 km | 0.01 m | SRKN14A | 112 | 17.96 | no | OK |
| Moon orbit 1000 km | 0.01 m | GJ8 | 452 | 0.00 | yes | EDGE |
| Moon orbit 1000 km | 0.01 m | DOPRI5-adaptive | 67 | 233086.19 | no | OK |
| Moon orbit 1000 km | 0.01 m | DOPRI5-adaptive [cap=600 s] | 163 | 335.94 | no | OK |
| Moon orbit 1000 km | 0.01 m | DOPRI5-fixed | 49 | 284482.86 | no | OK |
| Moon orbit 1000 km | 0.01 m | Y6 | 57 | 181067.27 | no | OK |
| Moon orbit 1000 km | 0.01 m | Y6C | 57 | 181067.27 | no | OK |
| Moon orbit 1000 km | 0.01 m | RK4 | 32 | 922386.09 | no | OK |
| Moon orbit 1000 km | 0.01 m | PEFRL | 32 | 105336.11 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | WH-BM14A | 244 | 1271.00 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | RKN12(10) | 409 | 10513.99 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | WH-Y6 | 238 | 12348.35 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | GJ8 | 1,207 | 10.75 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | DOP853-adaptive | 517 | 150539.63 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | DOP853-adaptive [cap=600 s] | 1,477 | 2.00 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | IAS15 | 1,845 | 17.44 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | RKN12(10) [cap=600 s] | 2,092 | 0.00 | yes | EDGE |
| Moon orbit 100 km x10 orbits | 1 m | SRKN14A | 560 | 7020.28 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | IAS15 [cap=600 s] | 7,004 | 0.00 | yes | EDGE |
| Moon orbit 100 km x10 orbits | 1 m | Y6 | 281 | 3994857.66 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | Y6C | 281 | 3994857.66 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | DOPRI5-adaptive | 589 | 3494226.43 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | DOPRI5-adaptive [cap=600 s] | 739 | 971647.18 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | DOPRI5-fixed | 241 | 203208108.41 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | RK4 | 160 | 238786798.89 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | PEFRL | 160 | 3790909.61 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | Encke-IAS15 | 24,840 | 0.00 | yes | EDGE |
| Moon orbit 100 km x10 orbits | 1 m | Leapfrog | 41 | 12625009.08 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | WH-BM14A | 2,441 | 25696.19 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | RKN12(10) | 3,469 | 2307233.06 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | GJ8 | 3,909 | 5418.26 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | WH-Y6 | 2,372 | 411214.78 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | RKN12(10) [cap=600 s] | 20,452 | 0.00 | yes | EDGE |
| Moon orbit 100 km x100 orbits | 1 m | IAS15 | 17,145 | 1005.49 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | DOP853-adaptive | 4,837 | 3647417.26 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | DOP853-adaptive [cap=600 s] | 14,437 | 531.67 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | SRKN14A | 5,600 | 70199.32 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | IAS15 [cap=600 s] | 69,754 | 0.00 | yes | EDGE |
| Moon orbit 100 km x100 orbits | 1 m | Y6C | 2,801 | 4125476.85 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | Y6 | 2,801 | 4125476.85 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | DOPRI5-adaptive | 8,083 | 3627166.50 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | DOPRI5-adaptive [cap=600 s] | 7,675 | 3620481.20 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | PEFRL | 1,600 | 3856585.65 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | DOPRI5-fixed | 2,401 | 1912854320.38 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | Encke-IAS15 | 2.88E+005 | 0.00 | yes | EDGE |
| Moon orbit 100 km x100 orbits | 1 m | RK4 | 1,600 | 2476945412.77 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | WH-BM14A | 2,441 | 25696.19 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | RKN12(10) | 3,469 | 2307233.06 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | GJ8 | 3,909 | 5418.26 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | RKN12(10) [cap=600 s] | 20,452 | 0.00 | yes | EDGE |
| Moon orbit 100 km x100 orbits | 0.01 m | WH-Y6 | 2,372 | 411214.78 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | IAS15 | 17,145 | 1005.49 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | DOP853-adaptive | 4,837 | 3647417.26 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | DOP853-adaptive [cap=600 s] | 14,437 | 531.67 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | IAS15 [cap=600 s] | 69,754 | 0.00 | yes | EDGE |
| Moon orbit 100 km x100 orbits | 0.01 m | SRKN14A | 5,600 | 70199.32 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | Y6C | 2,801 | 4125476.85 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | Y6 | 2,801 | 4125476.85 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | Encke-IAS15 | 2.88E+005 | 0.00 | yes | EDGE |
| Moon orbit 100 km x100 orbits | 0.01 m | DOPRI5-adaptive [cap=600 s] | 7,675 | 3620481.20 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | DOPRI5-adaptive | 8,083 | 3627166.50 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | PEFRL | 1,600 | 3856585.65 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | DOPRI5-fixed | 2,401 | 1912854320.38 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | RK4 | 1,600 | 2476945412.77 | no | OK |
| Moon orbit 100 km x1000 orbits | 1 m | WH-BM14A | 26,131 | 52090.04 | no | OK |
| Moon orbit 100 km x1000 orbits | 1 m | RKN12(10) | 33,243 | 3697542.84 | no | OK |
| Moon orbit 100 km x1000 orbits | 1 m | GJ8 | 67,701 | 1637.75 | no | OK |
| Moon orbit 100 km x1000 orbits | 1 m | WH-Y6 | 25,346 | 627786.28 | no | OK |
| Moon orbit 100 km x1000 orbits | 1 m | RKN12(10) [cap=600 s] | 2.00E+005 | 0.02 | yes | EDGE |
| Moon orbit 100 km x1000 orbits | 1 m | IAS15 | 1.96E+005 | 1066.57 | no | OK |
| Moon orbit 100 km x1000 orbits | 1 m | DOP853-adaptive | 52,621 | 3653321.00 | no | OK |
| Moon orbit 100 km x1000 orbits | 1 m | DOP853-adaptive [cap=600 s] | 1.41E+005 | 59980.01 | no | OK |
| Moon orbit 100 km x1000 orbits | 1 m | SRKN14A | 57,344 | 609552.17 | no | OK |
| Moon orbit 100 km x1000 orbits | 1 m | IAS15 [cap=600 s] | 6.90E+005 | 0.01 | yes | EDGE |
| Moon orbit 100 km x1000 orbits | 1 m | Y6 | 28,673 | 4191722.07 | no | OK |
| Moon orbit 100 km x1000 orbits | 1 m | Y6C | 28,673 | 4191722.07 | no | OK |
| Moon hop 50 m/s | 1 m | Leapfrog | 9 | 0.04 | yes | EDGE |
| Moon hop 50 m/s | 1 m | RK4 | 32 | 0.00 | yes | EDGE |
| Moon hop 50 m/s | 1 m | PEFRL | 32 | 0.00 | yes | EDGE |
| Moon hop 50 m/s | 1 m | GJ8 | 246 | 0.00 | yes | EDGE |
| Moon hop 50 m/s | 0.01 m | Leapfrog | 9 | 0.04 | no | OK |
| Moon hop 50 m/s | 0.01 m | RK4 | 32 | 0.00 | yes | EDGE |
| Moon hop 50 m/s | 0.01 m | PEFRL | 32 | 0.00 | yes | EDGE |
| Moon hop 50 m/s | 0.01 m | GJ8 | 246 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 1 m | RK4 | 32 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 1 m | PEFRL | 32 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 1 m | Leapfrog | 9 | 10.73 | no | OK |
| Moon hop 200 m/s | 1 m | DOPRI5-fixed | 49 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 1 m | Y6 | 57 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 1 m | Y6C | 57 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 1 m | DOPRI5-adaptive | 73 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 1 m | DOPRI5-adaptive [cap=600 s] | 73 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 1 m | GJ8 | 304 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 0.01 m | RK4 | 32 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 0.01 m | PEFRL | 32 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 0.01 m | DOPRI5-fixed | 49 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 0.01 m | Y6 | 57 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 0.01 m | Y6C | 57 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 0.01 m | DOPRI5-adaptive | 73 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 0.01 m | DOPRI5-adaptive [cap=600 s] | 73 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 0.01 m | Leapfrog | 9 | 10.73 | no | OK |
| Moon hop 200 m/s | 0.01 m | GJ8 | 304 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 1 m | RK4 | 32 | 0.76 | yes | EDGE |
| Moon hop 500 m/s | 1 m | PEFRL | 32 | 0.24 | yes | EDGE |
| Moon hop 500 m/s | 1 m | DOPRI5-fixed | 49 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 1 m | Y6 | 57 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 1 m | Y6C | 57 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 1 m | DOPRI5-adaptive | 73 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 1 m | DOPRI5-adaptive [cap=600 s] | 73 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 1 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 1 m | DOP853-adaptive | 145 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 1 m | DOP853-adaptive [cap=600 s] | 145 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 1 m | Leapfrog | 9 | 516.00 | no | OK |
| Moon hop 500 m/s | 1 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 1 m | RKN12(10) [cap=600 s] | 205 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 1 m | GJ8 | 330 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | DOPRI5-fixed | 49 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | Y6 | 57 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | Y6C | 57 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | DOPRI5-adaptive | 73 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | DOPRI5-adaptive [cap=600 s] | 73 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | PEFRL | 32 | 0.24 | no | OK |
| Moon hop 500 m/s | 0.01 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | DOP853-adaptive | 145 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | DOP853-adaptive [cap=600 s] | 145 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | RK4 | 32 | 0.76 | no | OK |
| Moon hop 500 m/s | 0.01 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | RKN12(10) [cap=600 s] | 205 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | GJ8 | 330 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | Leapfrog | 9 | 516.00 | no | OK |
| Moon hop 1000 m/s | 1 m | DOPRI5-adaptive | 73 | 1.84 | no | OK |
| Moon hop 1000 m/s | 1 m | DOPRI5-adaptive [cap=600 s] | 73 | 1.84 | no | OK |
| Moon hop 1000 m/s | 1 m | DOPRI5-fixed | 49 | 1.85 | no | OK |
| Moon hop 1000 m/s | 1 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 1000 m/s | 1 m | Y6 | 57 | 25.09 | no | OK |
| Moon hop 1000 m/s | 1 m | Y6C | 57 | 25.09 | no | OK |
| Moon hop 1000 m/s | 1 m | WH-Y6 | 134 | 0.00 | yes | EDGE |
| Moon hop 1000 m/s | 1 m | DOP853-adaptive | 145 | 0.01 | yes | EDGE |
| Moon hop 1000 m/s | 1 m | DOP853-adaptive [cap=600 s] | 145 | 0.01 | yes | EDGE |
| Moon hop 1000 m/s | 1 m | RK4 | 32 | 170.91 | no | OK |
| Moon hop 1000 m/s | 1 m | PEFRL | 32 | 94.32 | no | OK |
| Moon hop 1000 m/s | 1 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 1000 m/s | 1 m | RKN12(10) [cap=600 s] | 205 | 0.00 | yes | EDGE |
| Moon hop 1000 m/s | 1 m | GJ8 | 367 | 0.04 | yes | EDGE |
| Moon hop 1000 m/s | 1 m | Leapfrog | 17 | 4531.23 | no | OK |
| Moon hop 1000 m/s | 0.01 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 1000 m/s | 0.01 m | WH-Y6 | 134 | 0.00 | yes | EDGE |
| Moon hop 1000 m/s | 0.01 m | DOPRI5-fixed | 49 | 1.85 | no | OK |
| Moon hop 1000 m/s | 0.01 m | DOP853-adaptive | 145 | 0.01 | no | OK |
| Moon hop 1000 m/s | 0.01 m | DOP853-adaptive [cap=600 s] | 145 | 0.01 | no | OK |
| Moon hop 1000 m/s | 0.01 m | DOPRI5-adaptive | 73 | 1.84 | no | OK |
| Moon hop 1000 m/s | 0.01 m | DOPRI5-adaptive [cap=600 s] | 73 | 1.84 | no | OK |
| Moon hop 1000 m/s | 0.01 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 1000 m/s | 0.01 m | RKN12(10) [cap=600 s] | 205 | 0.00 | yes | EDGE |
| Moon hop 1000 m/s | 0.01 m | Y6 | 57 | 25.09 | no | OK |
| Moon hop 1000 m/s | 0.01 m | Y6C | 57 | 25.09 | no | OK |
| Moon hop 1000 m/s | 0.01 m | PEFRL | 32 | 94.32 | no | OK |
| Moon hop 1000 m/s | 0.01 m | GJ8 | 367 | 0.04 | no | OK |
| Moon hop 1000 m/s | 0.01 m | RK4 | 32 | 170.91 | no | OK |
| Moon hop 1000 m/s | 0.01 m | Leapfrog | 17 | 4531.23 | no | OK |
| Moon hop 1200 m/s | 1 m | DOPRI5-fixed | 49 | 2.81 | no | OK |
| Moon hop 1200 m/s | 1 m | SRKN14A | 112 | 0.02 | yes | EDGE |
| Moon hop 1200 m/s | 1 m | DOPRI5-adaptive | 73 | 2.61 | no | OK |
| Moon hop 1200 m/s | 1 m | DOPRI5-adaptive [cap=600 s] | 73 | 2.61 | no | OK |
| Moon hop 1200 m/s | 1 m | WH-Y6 | 134 | 0.00 | yes | EDGE |
| Moon hop 1200 m/s | 1 m | DOP853-adaptive | 145 | 0.02 | yes | EDGE |
| Moon hop 1200 m/s | 1 m | DOP853-adaptive [cap=600 s] | 145 | 0.02 | yes | EDGE |
| Moon hop 1200 m/s | 1 m | RK4 | 32 | 137.04 | no | OK |
| Moon hop 1200 m/s | 1 m | PEFRL | 32 | 222.11 | no | OK |
| Moon hop 1200 m/s | 1 m | Y6 | 57 | 86.05 | no | OK |
| Moon hop 1200 m/s | 1 m | Y6C | 57 | 86.05 | no | OK |
| Moon hop 1200 m/s | 1 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 1200 m/s | 1 m | RKN12(10) [cap=600 s] | 205 | 0.00 | yes | EDGE |
| Moon hop 1200 m/s | 1 m | GJ8 | 372 | 1.64 | no | OK |
| Moon hop 1200 m/s | 1 m | Leapfrog | 17 | 8672.28 | no | OK |
| Moon hop 1200 m/s | 0.01 m | WH-Y6 | 134 | 0.00 | yes | EDGE |
| Moon hop 1200 m/s | 0.01 m | DOP853-adaptive | 145 | 0.02 | no | OK |
| Moon hop 1200 m/s | 0.01 m | DOP853-adaptive [cap=600 s] | 145 | 0.02 | no | OK |
| Moon hop 1200 m/s | 0.01 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 1200 m/s | 0.01 m | RKN12(10) [cap=600 s] | 205 | 0.00 | yes | EDGE |
| Moon hop 1200 m/s | 0.01 m | DOPRI5-adaptive | 73 | 2.61 | no | OK |
| Moon hop 1200 m/s | 0.01 m | DOPRI5-adaptive [cap=600 s] | 73 | 2.61 | no | OK |
| Moon hop 1200 m/s | 0.01 m | SRKN14A | 112 | 0.02 | no | OK |
| Moon hop 1200 m/s | 0.01 m | DOPRI5-fixed | 49 | 2.81 | no | OK |
| Moon hop 1200 m/s | 0.01 m | Y6 | 57 | 86.05 | no | OK |
| Moon hop 1200 m/s | 0.01 m | Y6C | 57 | 86.05 | no | OK |
| Moon hop 1200 m/s | 0.01 m | RK4 | 32 | 137.04 | no | OK |
| Moon hop 1200 m/s | 0.01 m | GJ8 | 372 | 1.64 | no | OK |
| Moon hop 1200 m/s | 0.01 m | PEFRL | 32 | 222.11 | no | OK |
| Moon hop 1200 m/s | 0.01 m | Leapfrog | 17 | 8672.28 | no | OK |
| Moon hop 1400 m/s | 1 m | DOPRI5-fixed | 49 | 10.18 | no | OK |
| Moon hop 1400 m/s | 1 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 1 m | Y6 | 57 | 46.00 | no | OK |
| Moon hop 1400 m/s | 1 m | Y6C | 57 | 46.00 | no | OK |
| Moon hop 1400 m/s | 1 m | WH-Y6 | 135 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 1 m | DOPRI5-adaptive | 73 | 9.64 | no | OK |
| Moon hop 1400 m/s | 1 m | DOPRI5-adaptive [cap=600 s] | 73 | 9.64 | no | OK |
| Moon hop 1400 m/s | 1 m | DOP853-adaptive | 145 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 1 m | DOP853-adaptive [cap=600 s] | 145 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 1 m | RK4 | 32 | 81.14 | no | OK |
| Moon hop 1400 m/s | 1 m | PEFRL | 32 | 184.49 | no | OK |
| Moon hop 1400 m/s | 1 m | GJ8 | 379 | 0.15 | yes | EDGE |
| Moon hop 1400 m/s | 1 m | IAS15 | 475 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 1 m | IAS15 [cap=600 s] | 475 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 1 m | Leapfrog | 17 | 10066.11 | no | OK |
| Moon hop 1400 m/s | 0.01 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 0.01 m | WH-Y6 | 135 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 0.01 m | DOP853-adaptive | 145 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 0.01 m | DOP853-adaptive [cap=600 s] | 145 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 0.01 m | DOPRI5-adaptive | 73 | 9.64 | no | OK |
| Moon hop 1400 m/s | 0.01 m | DOPRI5-adaptive [cap=600 s] | 73 | 9.64 | no | OK |
| Moon hop 1400 m/s | 0.01 m | DOPRI5-fixed | 49 | 10.18 | no | OK |
| Moon hop 1400 m/s | 0.01 m | Y6 | 57 | 46.00 | no | OK |
| Moon hop 1400 m/s | 0.01 m | Y6C | 57 | 46.00 | no | OK |
| Moon hop 1400 m/s | 0.01 m | RK4 | 32 | 81.14 | no | OK |
| Moon hop 1400 m/s | 0.01 m | GJ8 | 379 | 0.15 | no | OK |
| Moon hop 1400 m/s | 0.01 m | IAS15 | 475 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 0.01 m | IAS15 [cap=600 s] | 475 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 0.01 m | PEFRL | 32 | 184.49 | no | OK |
| Moon hop 1400 m/s | 0.01 m | Leapfrog | 17 | 10066.11 | no | OK |
| Moon hop 1500 m/s | 1 m | DOPRI5-fixed | 49 | 12.63 | no | OK |
| Moon hop 1500 m/s | 1 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 1 m | Y6 | 57 | 27.82 | no | OK |
| Moon hop 1500 m/s | 1 m | Y6C | 57 | 27.82 | no | OK |
| Moon hop 1500 m/s | 1 m | DOPRI5-adaptive | 73 | 11.37 | no | OK |
| Moon hop 1500 m/s | 1 m | DOPRI5-adaptive [cap=600 s] | 73 | 11.37 | no | OK |
| Moon hop 1500 m/s | 1 m | WH-Y6 | 134 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 1 m | DOP853-adaptive | 145 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 1 m | DOP853-adaptive [cap=600 s] | 145 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 1 m | RK4 | 32 | 125.82 | no | OK |
| Moon hop 1500 m/s | 1 m | PEFRL | 32 | 152.92 | no | OK |
| Moon hop 1500 m/s | 1 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 1 m | RKN12(10) [cap=600 s] | 205 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 1 m | GJ8 | 379 | 0.02 | yes | EDGE |
| Moon hop 1500 m/s | 1 m | Leapfrog | 17 | 10343.29 | no | OK |
| Moon hop 1500 m/s | 0.01 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 0.01 m | WH-Y6 | 134 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 0.01 m | DOP853-adaptive | 145 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 0.01 m | DOP853-adaptive [cap=600 s] | 145 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 0.01 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 0.01 m | RKN12(10) [cap=600 s] | 205 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 0.01 m | DOPRI5-fixed | 49 | 12.63 | no | OK |
| Moon hop 1500 m/s | 0.01 m | DOPRI5-adaptive | 73 | 11.37 | no | OK |
| Moon hop 1500 m/s | 0.01 m | DOPRI5-adaptive [cap=600 s] | 73 | 11.37 | no | OK |
| Moon hop 1500 m/s | 0.01 m | Y6 | 57 | 27.82 | no | OK |
| Moon hop 1500 m/s | 0.01 m | Y6C | 57 | 27.82 | no | OK |
| Moon hop 1500 m/s | 0.01 m | RK4 | 32 | 125.82 | no | OK |
| Moon hop 1500 m/s | 0.01 m | GJ8 | 379 | 0.02 | no | OK |
| Moon hop 1500 m/s | 0.01 m | PEFRL | 32 | 152.92 | no | OK |
| Moon hop 1500 m/s | 0.01 m | Leapfrog | 17 | 10343.29 | no | OK |
| Moon hop 1600 m/s | 1 m | DOPRI5-fixed | 49 | 15.32 | no | OK |
| Moon hop 1600 m/s | 1 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 1 m | Y6 | 57 | 28.72 | no | OK |
| Moon hop 1600 m/s | 1 m | Y6C | 57 | 28.72 | no | OK |
| Moon hop 1600 m/s | 1 m | DOPRI5-adaptive | 73 | 12.80 | no | OK |
| Moon hop 1600 m/s | 1 m | DOPRI5-adaptive [cap=600 s] | 73 | 12.80 | no | OK |
| Moon hop 1600 m/s | 1 m | WH-Y6 | 134 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 1 m | DOP853-adaptive | 145 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 1 m | DOP853-adaptive [cap=600 s] | 145 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 1 m | RK4 | 32 | 188.49 | no | OK |
| Moon hop 1600 m/s | 1 m | PEFRL | 32 | 140.70 | no | OK |
| Moon hop 1600 m/s | 1 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 1 m | RKN12(10) [cap=600 s] | 205 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 1 m | GJ8 | 379 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 1 m | Leapfrog | 17 | 10754.43 | no | OK |
| Moon hop 1600 m/s | 0.01 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 0.01 m | WH-Y6 | 134 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 0.01 m | DOP853-adaptive | 145 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 0.01 m | DOP853-adaptive [cap=600 s] | 145 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 0.01 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 0.01 m | RKN12(10) [cap=600 s] | 205 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 0.01 m | DOPRI5-adaptive | 73 | 12.80 | no | OK |
| Moon hop 1600 m/s | 0.01 m | DOPRI5-adaptive [cap=600 s] | 73 | 12.80 | no | OK |
| Moon hop 1600 m/s | 0.01 m | DOPRI5-fixed | 49 | 15.32 | no | OK |
| Moon hop 1600 m/s | 0.01 m | Y6 | 57 | 28.72 | no | OK |
| Moon hop 1600 m/s | 0.01 m | Y6C | 57 | 28.72 | no | OK |
| Moon hop 1600 m/s | 0.01 m | RK4 | 32 | 188.49 | no | OK |
| Moon hop 1600 m/s | 0.01 m | PEFRL | 32 | 140.70 | no | OK |
| Moon hop 1600 m/s | 0.01 m | GJ8 | 379 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 0.01 m | Leapfrog | 17 | 10754.43 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | DOP853-adaptive | 313 | 2058.61 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | RKN12(10) | 426 | 8.22 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | DOPRI5-adaptive | 163 | 123641.24 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | IAS15 | 956 | 0.21 | yes | EDGE |
| Moon flyby vinf 50 alt 1 | 1 m | WH-BM14A | 590 | 6.76 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | DOPRI5-adaptive [cap=600 s] | 979 | 74283.77 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | DOP853-adaptive [cap=600 s] | 1,957 | 59.74 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | WH-Y6 | 286 | 1207.86 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | RKN12(10) [cap=600 s] | 2,772 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 1 | 1 m | IAS15 [cap=600 s] | 4,398 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 1 | 1 m | GJ8 | 1,623 | 14826.35 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | SRKN14A | 1,120 | 1188.42 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | Encke-IAS15 | 6,698 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 1 | 1 m | DOPRI5-fixed | 481 | 11862213.85 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | Y6C | 561 | 11252965.69 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | Y6 | 561 | 11252965.69 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | PEFRL | 320 | 4184708.13 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | RK4 | 320 | 17894618.18 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | Leapfrog | 81 | 78341493.90 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | RKN12(10) | 426 | 8.22 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | DOP853-adaptive | 313 | 2058.61 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | IAS15 | 956 | 0.21 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | DOPRI5-adaptive | 163 | 123641.24 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 979 | 74283.77 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | DOP853-adaptive [cap=600 s] | 1,957 | 59.74 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | RKN12(10) [cap=600 s] | 2,772 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 1 | 0.01 m | WH-BM14A | 590 | 6.76 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | IAS15 [cap=600 s] | 4,398 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 1 | 0.01 m | WH-Y6 | 286 | 1207.86 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | Encke-IAS15 | 6,698 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 1 | 0.01 m | GJ8 | 1,623 | 14826.35 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | SRKN14A | 1,120 | 1188.42 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | DOPRI5-fixed | 481 | 11862213.85 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | Y6C | 561 | 11252965.69 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | Y6 | 561 | 11252965.69 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | PEFRL | 320 | 4184708.13 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | RK4 | 320 | 17894618.18 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | DOP853-adaptive | 313 | 2069.26 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | RKN12(10) | 426 | 8.26 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | DOPRI5-adaptive | 163 | 124280.53 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | IAS15 | 949 | 0.21 | yes | EDGE |
| Moon flyby vinf 50 alt 10 | 1 m | WH-BM14A | 591 | 6.90 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | DOPRI5-adaptive [cap=600 s] | 979 | 74924.12 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | DOP853-adaptive [cap=600 s] | 1,957 | 60.67 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | WH-Y6 | 286 | 1233.18 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | RKN12(10) [cap=600 s] | 2,772 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 10 | 1 m | IAS15 [cap=600 s] | 4,413 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 10 | 1 m | GJ8 | 1,616 | 14902.81 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | SRKN14A | 1,120 | 1194.73 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | Encke-IAS15 | 6,766 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 10 | 1 m | DOPRI5-fixed | 481 | 11923304.92 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | Y6C | 561 | 11312765.55 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | Y6 | 561 | 11312765.55 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | PEFRL | 320 | 4206314.06 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | RK4 | 320 | 17986638.40 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | Leapfrog | 81 | 78756728.18 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | RKN12(10) | 426 | 8.26 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | DOP853-adaptive | 313 | 2069.26 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | IAS15 | 949 | 0.21 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | DOPRI5-adaptive | 163 | 124280.53 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 979 | 74924.12 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | DOP853-adaptive [cap=600 s] | 1,957 | 60.67 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | RKN12(10) [cap=600 s] | 2,772 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 10 | 0.01 m | WH-BM14A | 591 | 6.90 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | IAS15 [cap=600 s] | 4,413 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 10 | 0.01 m | WH-Y6 | 286 | 1233.18 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | Encke-IAS15 | 6,766 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 10 | 0.01 m | GJ8 | 1,616 | 14902.81 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | SRKN14A | 1,120 | 1194.73 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | DOPRI5-fixed | 481 | 11923304.92 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | Y6C | 561 | 11312765.55 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | Y6 | 561 | 11312765.55 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | PEFRL | 320 | 4206314.06 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | RK4 | 320 | 17986638.40 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | DOP853-adaptive | 313 | 2175.77 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | RKN12(10) | 426 | 8.71 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | DOPRI5-adaptive | 163 | 130670.91 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | IAS15 | 970 | 0.22 | yes | EDGE |
| Moon flyby vinf 50 alt 100 | 1 m | WH-BM14A | 593 | 8.44 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | DOPRI5-adaptive [cap=600 s] | 1,099 | 59119.08 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | WH-Y6 | 287 | 1508.82 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | DOP853-adaptive [cap=600 s] | 2,197 | 44.48 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | RKN12(10) [cap=600 s] | 3,112 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 100 | 1 m | GJ8 | 1,623 | 15667.25 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | IAS15 [cap=600 s] | 4,873 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 100 | 1 m | SRKN14A | 1,120 | 1257.95 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | DOPRI5-fixed | 481 | 12534184.26 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | Encke-IAS15 | 6,770 | 0.00 | yes | OK |
| Moon flyby vinf 50 alt 100 | 1 m | Y6C | 561 | 11912960.54 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | Y6 | 561 | 11912960.54 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | PEFRL | 320 | 4422407.29 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | RK4 | 320 | 18906588.91 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | Leapfrog | 81 | 82923327.69 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | RKN12(10) | 426 | 8.71 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | DOP853-adaptive | 313 | 2175.77 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | IAS15 | 970 | 0.22 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | DOPRI5-adaptive | 163 | 130670.91 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 1,099 | 59119.08 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | DOP853-adaptive [cap=600 s] | 2,197 | 44.48 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | WH-BM14A | 593 | 8.44 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | RKN12(10) [cap=600 s] | 3,112 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 100 | 0.01 m | WH-Y6 | 287 | 1508.82 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | IAS15 [cap=600 s] | 4,873 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 100 | 0.01 m | GJ8 | 1,623 | 15667.25 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | SRKN14A | 1,120 | 1257.95 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | Encke-IAS15 | 6,770 | 0.00 | yes | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | DOPRI5-fixed | 481 | 12534184.26 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | Y6C | 561 | 11912960.54 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | Y6 | 561 | 11912960.54 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | PEFRL | 320 | 4422407.29 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | RK4 | 320 | 18906588.91 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | DOP853-adaptive | 313 | 3256.46 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | RKN12(10) | 426 | 13.59 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | DOPRI5-adaptive | 163 | 193855.30 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | IAS15 | 949 | 0.33 | yes | EDGE |
| Moon flyby vinf 50 alt 1000 | 1 m | WH-BM14A | 592 | 41.72 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | DOPRI5-adaptive [cap=600 s] | 1,933 | 750.19 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | WH-Y6 | 572 | 1960.60 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | DOP853-adaptive [cap=600 s] | 3,865 | 0.42 | yes | EDGE |
| Moon flyby vinf 50 alt 1000 | 1 m | GJ8 | 1,623 | 23359.74 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | RKN12(10) [cap=600 s] | 5,475 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 1000 | 1 m | SRKN14A | 1,120 | 1911.28 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | Encke-IAS15 | 6,621 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 1000 | 1 m | IAS15 [cap=600 s] | 7,868 | 0.00 | yes | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | DOPRI5-fixed | 481 | 18690214.81 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | Y6C | 561 | 18309733.13 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | Y6 | 561 | 18309733.13 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | PEFRL | 320 | 6607177.50 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | RK4 | 320 | 28140076.77 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | Leapfrog | 81 | 127419606.15 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | RKN12(10) | 426 | 13.59 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | DOP853-adaptive | 313 | 3256.46 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | IAS15 | 949 | 0.33 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | DOPRI5-adaptive | 163 | 193855.30 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 1,933 | 750.19 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | DOP853-adaptive [cap=600 s] | 3,865 | 0.42 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | WH-BM14A | 592 | 41.72 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | WH-Y6 | 572 | 1960.60 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | RKN12(10) [cap=600 s] | 5,475 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 1000 | 0.01 m | Encke-IAS15 | 6,621 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 1000 | 0.01 m | GJ8 | 1,623 | 23359.74 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | IAS15 [cap=600 s] | 7,868 | 0.00 | yes | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | SRKN14A | 1,120 | 1911.28 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | DOPRI5-fixed | 481 | 18690214.81 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | Y6C | 561 | 18309733.13 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | Y6 | 561 | 18309733.13 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | PEFRL | 320 | 6607177.50 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | RK4 | 320 | 28140076.77 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | DOP853-adaptive | 313 | 1971.00 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | RKN12(10) | 426 | 8.31 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | DOPRI5-adaptive | 163 | 120767.47 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | IAS15 | 963 | 0.21 | yes | EDGE |
| Moon flyby vinf 200 alt 1 | 1 m | DOPRI5-adaptive [cap=600 s] | 979 | 71095.09 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | DOP853-adaptive [cap=600 s] | 1,957 | 58.83 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | RKN12(10) [cap=600 s] | 2,772 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 1 | 1 m | WH-BM14A | 1,618 | 6.38 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | IAS15 [cap=600 s] | 4,384 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 1 | 1 m | GJ8 | 1,616 | 14403.74 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | WH-Y6 | 765 | 1142.91 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | SRKN14A | 1,120 | 1208.56 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | DOPRI5-fixed | 481 | 11076431.71 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | Y6 | 561 | 10925668.22 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | Y6C | 561 | 10925668.22 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | Encke-IAS15 | 17,904 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 1 | 1 m | PEFRL | 320 | 4006272.60 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | RK4 | 320 | 16762943.04 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | Leapfrog | 81 | 76669400.54 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | RKN12(10) | 426 | 8.31 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | DOP853-adaptive | 313 | 1971.00 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | IAS15 | 963 | 0.21 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | DOPRI5-adaptive | 163 | 120767.47 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 979 | 71095.09 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | DOP853-adaptive [cap=600 s] | 1,957 | 58.83 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | RKN12(10) [cap=600 s] | 2,772 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 1 | 0.01 m | IAS15 [cap=600 s] | 4,384 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 1 | 0.01 m | GJ8 | 1,616 | 14403.74 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | WH-BM14A | 1,618 | 6.38 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | SRKN14A | 1,120 | 1208.56 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | WH-Y6 | 765 | 1142.91 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | Encke-IAS15 | 17,904 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 1 | 0.01 m | DOPRI5-fixed | 481 | 11076431.71 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | Y6C | 561 | 10925668.22 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | Y6 | 561 | 10925668.22 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | PEFRL | 320 | 4006272.60 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | RK4 | 320 | 16762943.04 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | DOP853-adaptive | 313 | 1980.77 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | RKN12(10) | 426 | 8.36 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | DOPRI5-adaptive | 163 | 121378.09 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | IAS15 | 956 | 0.21 | yes | EDGE |
| Moon flyby vinf 200 alt 10 | 1 m | DOPRI5-adaptive [cap=600 s] | 979 | 71684.54 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | DOP853-adaptive [cap=600 s] | 1,957 | 59.76 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | RKN12(10) [cap=600 s] | 2,772 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 10 | 1 m | WH-BM14A | 1,609 | 6.51 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | IAS15 [cap=600 s] | 4,399 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 10 | 1 m | GJ8 | 1,623 | 14475.94 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | WH-Y6 | 761 | 1166.54 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | SRKN14A | 1,120 | 1215.09 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | DOPRI5-fixed | 481 | 11129680.93 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | Y6 | 561 | 10982204.16 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | Y6C | 561 | 10982204.16 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | Encke-IAS15 | 17,776 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 10 | 1 m | PEFRL | 320 | 4026076.79 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | RK4 | 320 | 16843678.77 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | Leapfrog | 81 | 77067609.73 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | RKN12(10) | 426 | 8.36 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | DOP853-adaptive | 313 | 1980.77 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | IAS15 | 956 | 0.21 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | DOPRI5-adaptive | 163 | 121378.09 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 979 | 71684.54 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | DOP853-adaptive [cap=600 s] | 1,957 | 59.76 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | RKN12(10) [cap=600 s] | 2,772 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 10 | 0.01 m | IAS15 [cap=600 s] | 4,399 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 10 | 0.01 m | GJ8 | 1,623 | 14475.94 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | WH-BM14A | 1,609 | 6.51 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | SRKN14A | 1,120 | 1215.09 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | WH-Y6 | 761 | 1166.54 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | Encke-IAS15 | 17,776 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 10 | 0.01 m | DOPRI5-fixed | 481 | 11129680.93 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | Y6C | 561 | 10982204.16 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | Y6 | 561 | 10982204.16 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | PEFRL | 320 | 4026076.79 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | RK4 | 320 | 16843678.77 | no | OK |
| Moon flyby vinf 200 alt 100 | 1 m | DOP853-adaptive | 313 | 2078.20 | no | OK |
| Moon flyby vinf 200 alt 100 | 1 m | RKN12(10) | 426 | 8.81 | no | OK |
| Moon flyby vinf 200 alt 100 | 1 m | DOPRI5-adaptive | 163 | 127474.83 | no | OK |
| Moon flyby vinf 200 alt 100 | 1 m | IAS15 | 956 | 0.22 | yes | EDGE |
| Moon flyby vinf 200 alt 100 | 1 m | DOPRI5-adaptive [cap=600 s] | 1,099 | 58092.48 | no | OK |
| Moon flyby vinf 200 alt 100 | 1 m | DOP853-adaptive [cap=600 s] | 2,197 | 45.56 | no | OK |
| Moon flyby vinf 200 alt 100 | 1 m | RKN12(10) [cap=600 s] | 3,112 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 100 | 1 m | WH-BM14A | 1,615 | 7.94 | no | OK |
| Moon flyby vinf 200 alt 100 | 1 m | GJ8 | 1,609 | 15196.81 | no | OK |
| Moon flyby vinf 200 alt 100 | 1 m | IAS15 [cap=600 s] | 4,838 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 100 | 1 m | WH-Y6 | 764 | 1423.27 | no | OK |
| Moon flyby vinf 200 alt 100 | 1 m | SRKN14A | 1,120 | 1280.66 | no | OK |
| Moon flyby vinf 200 alt 100 | 1 m | DOPRI5-fixed | 481 | 11660185.24 | no | OK |
| Moon flyby vinf 200 alt 100 | 1 m | Y6 | 561 | 11548976.63 | no | OK |
| Moon flyby vinf 200 alt 100 | 1 m | Y6C | 561 | 11548976.63 | no | OK |
| Moon flyby vinf 200 alt 100 | 1 m | Encke-IAS15 | 18,184 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 100 | 1 m | PEFRL | 320 | 4223683.15 | no | OK |
| Moon flyby vinf 200 alt 100 | 1 m | RK4 | 320 | 17647953.77 | no | OK |
| Moon flyby vinf 200 alt 100 | 1 m | Leapfrog | 81 | 81059446.88 | no | OK |
| Moon flyby vinf 200 alt 100 | 0.01 m | RKN12(10) | 426 | 8.81 | no | OK |
| Moon flyby vinf 200 alt 100 | 0.01 m | DOP853-adaptive | 313 | 2078.20 | no | OK |
| Moon flyby vinf 200 alt 100 | 0.01 m | IAS15 | 956 | 0.22 | no | OK |
| Moon flyby vinf 200 alt 100 | 0.01 m | DOPRI5-adaptive | 163 | 127474.83 | no | OK |
| Moon flyby vinf 200 alt 100 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 1,099 | 58092.48 | no | OK |
| Moon flyby vinf 200 alt 100 | 0.01 m | DOP853-adaptive [cap=600 s] | 2,197 | 45.56 | no | OK |
| Moon flyby vinf 200 alt 100 | 0.01 m | RKN12(10) [cap=600 s] | 3,112 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 100 | 0.01 m | IAS15 [cap=600 s] | 4,838 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 100 | 0.01 m | GJ8 | 1,609 | 15196.81 | no | OK |
| Moon flyby vinf 200 alt 100 | 0.01 m | WH-BM14A | 1,615 | 7.94 | no | OK |
| Moon flyby vinf 200 alt 100 | 0.01 m | SRKN14A | 1,120 | 1280.66 | no | OK |
| Moon flyby vinf 200 alt 100 | 0.01 m | WH-Y6 | 764 | 1423.27 | no | OK |
| Moon flyby vinf 200 alt 100 | 0.01 m | Encke-IAS15 | 18,184 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 100 | 0.01 m | DOPRI5-fixed | 481 | 11660185.24 | no | OK |
| Moon flyby vinf 200 alt 100 | 0.01 m | Y6C | 561 | 11548976.63 | no | OK |
| Moon flyby vinf 200 alt 100 | 0.01 m | Y6 | 561 | 11548976.63 | no | OK |
| Moon flyby vinf 200 alt 100 | 0.01 m | PEFRL | 320 | 4223683.15 | no | OK |
| Moon flyby vinf 200 alt 100 | 0.01 m | RK4 | 320 | 17647953.77 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | DOP853-adaptive | 313 | 3045.53 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | RKN12(10) | 426 | 13.85 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | DOPRI5-adaptive | 163 | 187130.13 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | IAS15 | 949 | 0.33 | yes | EDGE |
| Moon flyby vinf 200 alt 1000 | 1 m | DOPRI5-adaptive [cap=600 s] | 1,933 | 765.56 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | DOP853-adaptive [cap=600 s] | 3,865 | 0.40 | yes | EDGE |
| Moon flyby vinf 200 alt 1000 | 1 m | WH-BM14A | 1,515 | 38.07 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | GJ8 | 1,616 | 22349.59 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | WH-Y6 | 1,434 | 1819.54 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | RKN12(10) [cap=600 s] | 5,475 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 1000 | 1 m | SRKN14A | 1,120 | 1968.98 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | IAS15 [cap=600 s] | 7,861 | 0.00 | yes | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | DOPRI5-fixed | 481 | 16824636.26 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | Y6 | 561 | 17541132.99 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | Y6C | 561 | 17541132.99 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | Encke-IAS15 | 16,678 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 1000 | 1 m | PEFRL | 320 | 6177337.82 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | RK4 | 320 | 25449484.51 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | Leapfrog | 81 | 123369719.33 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | RKN12(10) | 426 | 13.85 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | DOP853-adaptive | 313 | 3045.53 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | IAS15 | 949 | 0.33 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | DOPRI5-adaptive | 163 | 187130.13 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 1,933 | 765.56 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | DOP853-adaptive [cap=600 s] | 3,865 | 0.40 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | RKN12(10) [cap=600 s] | 5,475 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 1000 | 0.01 m | GJ8 | 1,616 | 22349.59 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | WH-BM14A | 1,515 | 38.07 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | IAS15 [cap=600 s] | 7,861 | 0.00 | yes | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | WH-Y6 | 1,434 | 1819.54 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | SRKN14A | 1,120 | 1968.98 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | Encke-IAS15 | 16,678 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 1000 | 0.01 m | DOPRI5-fixed | 481 | 16824636.26 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | Y6C | 561 | 17541132.99 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | Y6 | 561 | 17541132.99 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | PEFRL | 320 | 6177337.82 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | RK4 | 320 | 25449484.51 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | DOP853-adaptive | 313 | 1587.44 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | RKN12(10) | 426 | 8.58 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | DOPRI5-adaptive | 163 | 108846.00 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | IAS15 | 949 | 0.22 | yes | EDGE |
| Moon flyby vinf 500 alt 1 | 1 m | DOPRI5-adaptive [cap=600 s] | 979 | 57598.13 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | DOP853-adaptive [cap=600 s] | 1,957 | 55.72 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | WH-BM14A | 1,315 | 4.82 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | RKN12(10) [cap=600 s] | 2,772 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 1 | 1 m | SRKN14A | 1,120 | 1323.17 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | IAS15 [cap=600 s] | 4,314 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 1 | 1 m | WH-Y6 | 624 | 880.23 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | GJ8 | 1,616 | 12594.49 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | DOPRI5-fixed | 481 | 7988595.41 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | Encke-IAS15 | 14,310 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 1 | 1 m | Y6C | 561 | 9450612.64 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | Y6 | 561 | 9450612.64 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | PEFRL | 320 | 3233141.32 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | RK4 | 320 | 12429753.91 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | Leapfrog | 81 | 68561567.87 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | RKN12(10) | 426 | 8.58 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | DOP853-adaptive | 313 | 1587.44 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | IAS15 | 949 | 0.22 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | DOPRI5-adaptive | 163 | 108846.00 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 979 | 57598.13 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | DOP853-adaptive [cap=600 s] | 1,957 | 55.72 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | RKN12(10) [cap=600 s] | 2,772 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 1 | 0.01 m | IAS15 [cap=600 s] | 4,314 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 1 | 0.01 m | WH-BM14A | 1,315 | 4.82 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | GJ8 | 1,616 | 12594.49 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | SRKN14A | 1,120 | 1323.17 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | WH-Y6 | 624 | 880.23 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | Encke-IAS15 | 14,310 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 1 | 0.01 m | DOPRI5-fixed | 481 | 7988595.41 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | Y6C | 561 | 9450612.64 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | Y6 | 561 | 9450612.64 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | PEFRL | 320 | 3233141.32 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | RK4 | 320 | 12429753.91 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | DOP853-adaptive | 313 | 1593.72 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | RKN12(10) | 426 | 8.62 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | DOPRI5-adaptive | 163 | 109349.65 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | IAS15 | 956 | 0.22 | yes | EDGE |
| Moon flyby vinf 500 alt 10 | 1 m | DOPRI5-adaptive [cap=600 s] | 979 | 57990.11 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | DOP853-adaptive [cap=600 s] | 1,957 | 56.67 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | WH-BM14A | 1,318 | 4.91 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | RKN12(10) [cap=600 s] | 2,772 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 10 | 1 m | SRKN14A | 1,120 | 1330.88 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | IAS15 [cap=600 s] | 4,342 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 10 | 1 m | WH-Y6 | 625 | 897.41 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | GJ8 | 1,602 | 12650.51 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | DOPRI5-fixed | 481 | 8015662.49 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | Encke-IAS15 | 14,369 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 10 | 1 m | Y6C | 561 | 9493694.75 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | Y6 | 561 | 9493694.75 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | PEFRL | 320 | 3245995.76 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | RK4 | 320 | 12474051.44 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | Leapfrog | 81 | 68882402.16 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | RKN12(10) | 426 | 8.62 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | DOP853-adaptive | 313 | 1593.72 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | IAS15 | 956 | 0.22 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | DOPRI5-adaptive | 163 | 109349.65 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 979 | 57990.11 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | DOP853-adaptive [cap=600 s] | 1,957 | 56.67 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | RKN12(10) [cap=600 s] | 2,772 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 10 | 0.01 m | IAS15 [cap=600 s] | 4,342 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 10 | 0.01 m | WH-BM14A | 1,318 | 4.91 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | GJ8 | 1,602 | 12650.51 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | SRKN14A | 1,120 | 1330.88 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | WH-Y6 | 625 | 897.41 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | Encke-IAS15 | 14,369 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 10 | 0.01 m | DOPRI5-fixed | 481 | 8015662.49 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | Y6C | 561 | 9493694.75 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | Y6 | 561 | 9493694.75 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | PEFRL | 320 | 3245995.76 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | RK4 | 320 | 12474051.44 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | DOP853-adaptive | 313 | 1655.79 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | RKN12(10) | 426 | 9.10 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | DOPRI5-adaptive | 163 | 114357.55 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | IAS15 | 935 | 0.24 | yes | EDGE |
| Moon flyby vinf 500 alt 100 | 1 m | DOPRI5-adaptive [cap=600 s] | 1,093 | 54192.06 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | DOP853-adaptive [cap=600 s] | 2,185 | 53.72 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | WH-BM14A | 1,321 | 5.92 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | RKN12(10) [cap=600 s] | 3,095 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 100 | 1 m | WH-Y6 | 627 | 1082.64 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | IAS15 [cap=600 s] | 4,768 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 100 | 1 m | GJ8 | 1,602 | 13206.73 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | SRKN14A | 1,120 | 1408.50 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | DOPRI5-fixed | 481 | 8280928.77 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | Encke-IAS15 | 14,184 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 100 | 1 m | Y6C | 561 | 9923470.97 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | Y6 | 561 | 9923470.97 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | PEFRL | 320 | 3372883.04 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | RK4 | 320 | 12909254.87 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | Leapfrog | 81 | 72083644.01 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | RKN12(10) | 426 | 9.10 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | DOP853-adaptive | 313 | 1655.79 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | DOPRI5-adaptive | 163 | 114357.55 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | IAS15 | 935 | 0.24 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 1,093 | 54192.06 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | DOP853-adaptive [cap=600 s] | 2,185 | 53.72 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | RKN12(10) [cap=600 s] | 3,095 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 100 | 0.01 m | IAS15 [cap=600 s] | 4,768 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 100 | 0.01 m | WH-BM14A | 1,321 | 5.92 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | GJ8 | 1,602 | 13206.73 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | SRKN14A | 1,120 | 1408.50 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | WH-Y6 | 627 | 1082.64 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | Encke-IAS15 | 14,184 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 100 | 0.01 m | DOPRI5-fixed | 481 | 8280928.77 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | Y6C | 561 | 9923470.97 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | Y6 | 561 | 9923470.97 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | PEFRL | 320 | 3372883.04 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | RK4 | 320 | 12909254.87 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | DOP853-adaptive | 313 | 2210.34 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | RKN12(10) | 426 | 14.27 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | DOPRI5-adaptive | 163 | 161559.33 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | IAS15 | 928 | 0.39 | yes | EDGE |
| Moon flyby vinf 500 alt 1000 | 1 m | DOPRI5-adaptive [cap=600 s] | 1,819 | 873.70 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | DOP853-adaptive [cap=600 s] | 3,637 | 0.35 | yes | EDGE |
| Moon flyby vinf 500 alt 1000 | 1 m | WH-BM14A | 1,429 | 25.29 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | WH-Y6 | 1,354 | 1295.60 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | GJ8 | 1,616 | 18466.73 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | RKN12(10) [cap=600 s] | 5,152 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 1000 | 1 m | SRKN14A | 1,120 | 2251.90 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | DOPRI5-fixed | 481 | 10520125.67 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | Y6C | 561 | 14325744.94 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | Y6 | 561 | 14325744.94 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | Encke-IAS15 | 15,334 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 1000 | 1 m | PEFRL | 320 | 4506388.55 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | RK4 | 320 | 16636375.30 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | Leapfrog | 81 | 104888476.23 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | RKN12(10) | 426 | 14.27 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | DOP853-adaptive | 313 | 2210.34 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | IAS15 | 928 | 0.39 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | DOPRI5-adaptive | 163 | 161559.33 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 1,819 | 873.70 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | DOP853-adaptive [cap=600 s] | 3,637 | 0.35 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | RKN12(10) [cap=600 s] | 5,152 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 1000 | 0.01 m | GJ8 | 1,616 | 18466.73 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | WH-BM14A | 1,429 | 25.29 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | WH-Y6 | 1,354 | 1295.60 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | SRKN14A | 1,120 | 2251.90 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | Encke-IAS15 | 15,334 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 1000 | 0.01 m | DOPRI5-fixed | 481 | 10520125.67 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | Y6C | 561 | 14325744.94 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | Y6 | 561 | 14325744.94 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | PEFRL | 320 | 4506388.55 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | RK4 | 320 | 16636375.30 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | DOP853-adaptive | 325 | 873.75 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | DOPRI5-adaptive | 163 | 81952.39 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | RKN12(10) | 426 | 7.75 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | IAS15 | 900 | 0.29 | yes | EDGE |
| Moon flyby vinf 1000 alt 1 | 1 m | DOPRI5-adaptive [cap=600 s] | 973 | 34005.88 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | DOP853-adaptive [cap=600 s] | 1,945 | 53.07 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | WH-BM14A | 1,224 | 2.54 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | RKN12(10) [cap=600 s] | 2,755 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 1 | 1 m | SRKN14A | 1,120 | 1517.94 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | WH-Y6 | 582 | 492.77 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | IAS15 [cap=600 s] | 4,195 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 1 | 1 m | GJ8 | 1,609 | 9167.23 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | DOPRI5-fixed | 481 | 3531410.40 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | Y6 | 561 | 6342090.65 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | Y6C | 561 | 6342090.65 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | RK4 | 320 | 6250977.77 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | PEFRL | 320 | 1881719.12 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | Encke-IAS15 | 12,959 | 0.00 | yes | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | Leapfrog | 81 | 49776193.51 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | RKN12(10) | 426 | 7.75 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | DOP853-adaptive | 325 | 873.75 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | DOPRI5-adaptive | 163 | 81952.39 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | IAS15 | 900 | 0.29 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 973 | 34005.88 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | DOP853-adaptive [cap=600 s] | 1,945 | 53.07 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | RKN12(10) [cap=600 s] | 2,755 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 1 | 0.01 m | IAS15 [cap=600 s] | 4,195 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 1 | 0.01 m | WH-BM14A | 1,224 | 2.54 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | WH-Y6 | 582 | 492.77 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | GJ8 | 1,609 | 9167.23 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | SRKN14A | 1,120 | 1517.94 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | DOPRI5-fixed | 481 | 3531410.40 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | Y6C | 561 | 6342090.65 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | Y6 | 561 | 6342090.65 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | Encke-IAS15 | 12,959 | 0.00 | yes | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | PEFRL | 320 | 1881719.12 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | RK4 | 320 | 6250977.77 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | DOP853-adaptive | 325 | 875.22 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | DOPRI5-adaptive | 163 | 82226.16 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | RKN12(10) | 426 | 7.78 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | IAS15 | 907 | 0.29 | yes | EDGE |
| Moon flyby vinf 1000 alt 10 | 1 m | DOPRI5-adaptive [cap=600 s] | 973 | 34122.80 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | DOP853-adaptive [cap=600 s] | 1,945 | 54.14 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | WH-BM14A | 1,226 | 2.58 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | RKN12(10) [cap=600 s] | 2,755 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 10 | 1 m | SRKN14A | 1,120 | 1526.42 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | WH-Y6 | 582 | 501.59 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | IAS15 [cap=600 s] | 4,202 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 10 | 1 m | GJ8 | 1,595 | 9198.51 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | DOPRI5-fixed | 481 | 3533626.86 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | Y6 | 561 | 6361153.57 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | Y6C | 561 | 6361153.57 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | Encke-IAS15 | 12,978 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 10 | 1 m | PEFRL | 320 | 1885988.45 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | RK4 | 320 | 6260280.20 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | Leapfrog | 81 | 49947644.98 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | RKN12(10) | 426 | 7.78 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | DOP853-adaptive | 325 | 875.22 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | DOPRI5-adaptive | 163 | 82226.16 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | IAS15 | 907 | 0.29 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 973 | 34122.80 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | DOP853-adaptive [cap=600 s] | 1,945 | 54.14 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | RKN12(10) [cap=600 s] | 2,755 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 10 | 0.01 m | IAS15 [cap=600 s] | 4,202 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 10 | 0.01 m | WH-BM14A | 1,226 | 2.58 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | WH-Y6 | 582 | 501.59 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | GJ8 | 1,595 | 9198.51 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | SRKN14A | 1,120 | 1526.42 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | Encke-IAS15 | 12,978 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 10 | 0.01 m | DOPRI5-fixed | 481 | 3533626.86 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | Y6C | 561 | 6361153.57 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | Y6 | 561 | 6361153.57 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | PEFRL | 320 | 1885988.45 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | RK4 | 320 | 6260280.20 | no | OK |
| Moon flyby vinf 1000 alt 100 | 1 m | DOP853-adaptive | 325 | 889.27 | no | OK |
| Moon flyby vinf 1000 alt 100 | 1 m | DOPRI5-adaptive | 163 | 84899.39 | no | OK |
| Moon flyby vinf 1000 alt 100 | 1 m | RKN12(10) | 426 | 8.11 | no | OK |
| Moon flyby vinf 1000 alt 100 | 1 m | IAS15 | 893 | 0.31 | yes | EDGE |
| Moon flyby vinf 1000 alt 100 | 1 m | DOPRI5-adaptive [cap=600 s] | 979 | 35331.33 | no | OK |
| Moon flyby vinf 1000 alt 100 | 1 m | DOP853-adaptive [cap=600 s] | 1,957 | 63.16 | no | OK |
| Moon flyby vinf 1000 alt 100 | 1 m | WH-BM14A | 1,281 | 3.06 | no | OK |
| Moon flyby vinf 1000 alt 100 | 1 m | RKN12(10) [cap=600 s] | 2,772 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 100 | 1 m | SRKN14A | 1,120 | 1611.16 | no | OK |
| Moon flyby vinf 1000 alt 100 | 1 m | IAS15 [cap=600 s] | 4,153 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 100 | 1 m | WH-Y6 | 608 | 595.89 | no | OK |
| Moon flyby vinf 1000 alt 100 | 1 m | GJ8 | 1,609 | 9505.71 | no | OK |
| Moon flyby vinf 1000 alt 100 | 1 m | DOPRI5-fixed | 481 | 3553064.74 | no | OK |
| Moon flyby vinf 1000 alt 100 | 1 m | Y6 | 561 | 6548156.79 | no | OK |
| Moon flyby vinf 1000 alt 100 | 1 m | Y6C | 561 | 6548156.79 | no | OK |
| Moon flyby vinf 1000 alt 100 | 1 m | Encke-IAS15 | 13,396 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 100 | 1 m | PEFRL | 320 | 1927480.45 | no | OK |
| Moon flyby vinf 1000 alt 100 | 1 m | RK4 | 320 | 6348570.82 | no | OK |
| Moon flyby vinf 1000 alt 100 | 1 m | Leapfrog | 81 | 51639305.75 | no | OK |
| Moon flyby vinf 1000 alt 100 | 0.01 m | RKN12(10) | 426 | 8.11 | no | OK |
| Moon flyby vinf 1000 alt 100 | 0.01 m | DOP853-adaptive | 325 | 889.27 | no | OK |
| Moon flyby vinf 1000 alt 100 | 0.01 m | DOPRI5-adaptive | 163 | 84899.39 | no | OK |
| Moon flyby vinf 1000 alt 100 | 0.01 m | IAS15 | 893 | 0.31 | no | OK |
| Moon flyby vinf 1000 alt 100 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 979 | 35331.33 | no | OK |
| Moon flyby vinf 1000 alt 100 | 0.01 m | DOP853-adaptive [cap=600 s] | 1,957 | 63.16 | no | OK |
| Moon flyby vinf 1000 alt 100 | 0.01 m | RKN12(10) [cap=600 s] | 2,772 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 100 | 0.01 m | IAS15 [cap=600 s] | 4,153 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 100 | 0.01 m | WH-BM14A | 1,281 | 3.06 | no | OK |
| Moon flyby vinf 1000 alt 100 | 0.01 m | WH-Y6 | 608 | 595.89 | no | OK |
| Moon flyby vinf 1000 alt 100 | 0.01 m | GJ8 | 1,609 | 9505.71 | no | OK |
| Moon flyby vinf 1000 alt 100 | 0.01 m | SRKN14A | 1,120 | 1611.16 | no | OK |
| Moon flyby vinf 1000 alt 100 | 0.01 m | Encke-IAS15 | 13,396 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 100 | 0.01 m | DOPRI5-fixed | 481 | 3553064.74 | no | OK |
| Moon flyby vinf 1000 alt 100 | 0.01 m | Y6C | 561 | 6548156.79 | no | OK |
| Moon flyby vinf 1000 alt 100 | 0.01 m | Y6 | 561 | 6548156.79 | no | OK |
| Moon flyby vinf 1000 alt 100 | 0.01 m | PEFRL | 320 | 1927480.45 | no | OK |
| Moon flyby vinf 1000 alt 100 | 0.01 m | RK4 | 320 | 6348570.82 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | DOPRI5-adaptive | 157 | 105173.29 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | DOP853-adaptive | 325 | 981.93 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | RKN12(10) | 426 | 11.17 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | IAS15 | 879 | 0.51 | yes | EDGE |
| Moon flyby vinf 1000 alt 1000 | 1 m | DOPRI5-adaptive [cap=600 s] | 1,693 | 1146.33 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | DOP853-adaptive [cap=600 s] | 3,385 | 0.60 | yes | EDGE |
| Moon flyby vinf 1000 alt 1000 | 1 m | WH-BM14A | 1,523 | 11.32 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | GJ8 | 1,924 | 1008.04 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | RKN12(10) [cap=600 s] | 4,795 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 1000 | 1 m | WH-Y6 | 721 | 2257.73 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | SRKN14A | 1,120 | 2457.03 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | DOPRI5-fixed | 481 | 3595196.76 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | Y6C | 561 | 8258382.78 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | Y6 | 561 | 8258382.78 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | Encke-IAS15 | 15,962 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 1000 | 1 m | PEFRL | 320 | 2252271.77 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | RK4 | 320 | 6886990.47 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | Leapfrog | 81 | 67816492.98 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | RKN12(10) | 426 | 11.17 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | DOP853-adaptive | 325 | 981.93 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | DOPRI5-adaptive | 157 | 105173.29 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | IAS15 | 879 | 0.51 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 1,693 | 1146.33 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | DOP853-adaptive [cap=600 s] | 3,385 | 0.60 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | RKN12(10) [cap=600 s] | 4,795 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | GJ8 | 1,924 | 1008.04 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | WH-BM14A | 1,523 | 11.32 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | SRKN14A | 1,120 | 2457.03 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | WH-Y6 | 721 | 2257.73 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | Encke-IAS15 | 15,962 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | DOPRI5-fixed | 481 | 3595196.76 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | Y6C | 561 | 8258382.78 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | Y6 | 561 | 8258382.78 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | PEFRL | 320 | 2252271.77 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | RK4 | 320 | 6886990.47 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | DOPRI5-adaptive | 157 | 15876.96 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | DOP853-adaptive | 313 | 1165.33 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | RKN12(10) | 426 | 1.54 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | DOPRI5-adaptive [cap=600 s] | 625 | 4879.10 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | IAS15 | 816 | 0.21 | yes | EDGE |
| Moon flyby vinf 3000 alt 1 | 1 m | WH-BM14A | 1,154 | 0.38 | yes | EDGE |
| Moon flyby vinf 3000 alt 1 | 1 m | DOP853-adaptive [cap=600 s] | 1,249 | 84.54 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | RKN12(10) [cap=600 s] | 1,769 | 0.03 | yes | EDGE |
| Moon flyby vinf 3000 alt 1 | 1 m | IAS15 [cap=600 s] | 2,596 | 0.01 | yes | EDGE |
| Moon flyby vinf 3000 alt 1 | 1 m | WH-Y6 | 549 | 80.98 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | SRKN14A | 840 | 5029.11 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | GJ8 | 1,582 | 3852.80 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | DOPRI5-fixed | 481 | 237171.16 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | Y6 | 561 | 526008.56 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | Y6C | 561 | 526008.56 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | Encke-IAS15 | 11,569 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 1 | 1 m | PEFRL | 320 | 913611.01 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | RK4 | 320 | 1236546.24 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | Leapfrog | 81 | 12406881.46 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | DOP853-adaptive | 313 | 1165.33 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | RKN12(10) | 426 | 1.54 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | DOPRI5-adaptive | 157 | 15876.96 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | IAS15 | 816 | 0.21 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 625 | 4879.10 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | DOP853-adaptive [cap=600 s] | 1,249 | 84.54 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | RKN12(10) [cap=600 s] | 1,769 | 0.03 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | IAS15 [cap=600 s] | 2,596 | 0.01 | yes | EDGE |
| Moon flyby vinf 3000 alt 1 | 0.01 m | WH-BM14A | 1,154 | 0.38 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | WH-Y6 | 549 | 80.98 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | GJ8 | 1,582 | 3852.80 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | SRKN14A | 840 | 5029.11 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | Encke-IAS15 | 11,569 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 1 | 0.01 m | DOPRI5-fixed | 481 | 237171.16 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | Y6C | 561 | 526008.56 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | Y6 | 561 | 526008.56 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | PEFRL | 320 | 913611.01 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | RK4 | 320 | 1236546.24 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | DOPRI5-adaptive | 157 | 15887.79 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | DOP853-adaptive | 301 | 1165.79 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | RKN12(10) | 426 | 1.54 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | DOPRI5-adaptive [cap=600 s] | 625 | 4783.95 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | IAS15 | 830 | 0.21 | yes | EDGE |
| Moon flyby vinf 3000 alt 10 | 1 m | DOP853-adaptive [cap=600 s] | 1,249 | 82.08 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | WH-BM14A | 1,267 | 0.39 | yes | EDGE |
| Moon flyby vinf 3000 alt 10 | 1 m | RKN12(10) [cap=600 s] | 1,769 | 0.02 | yes | EDGE |
| Moon flyby vinf 3000 alt 10 | 1 m | IAS15 [cap=600 s] | 2,596 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 10 | 1 m | WH-Y6 | 601 | 82.14 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | SRKN14A | 840 | 5039.94 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | GJ8 | 1,589 | 3864.35 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | DOPRI5-fixed | 481 | 237080.47 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | Y6 | 561 | 522686.54 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | Y6C | 561 | 522686.54 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | RK4 | 320 | 1238847.21 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | PEFRL | 320 | 916482.08 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | Encke-IAS15 | 12,833 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 10 | 1 m | Leapfrog | 81 | 12420534.97 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | DOP853-adaptive | 301 | 1165.79 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | RKN12(10) | 426 | 1.54 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | DOPRI5-adaptive | 157 | 15887.79 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | IAS15 | 830 | 0.21 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 625 | 4783.95 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | DOP853-adaptive [cap=600 s] | 1,249 | 82.08 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | RKN12(10) [cap=600 s] | 1,769 | 0.02 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | IAS15 [cap=600 s] | 2,596 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 10 | 0.01 m | WH-BM14A | 1,267 | 0.39 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | WH-Y6 | 601 | 82.14 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | GJ8 | 1,589 | 3864.35 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | SRKN14A | 840 | 5039.94 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | DOPRI5-fixed | 481 | 237080.47 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | Encke-IAS15 | 12,833 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 10 | 0.01 m | Y6C | 561 | 522686.54 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | Y6 | 561 | 522686.54 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | PEFRL | 320 | 916482.08 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | RK4 | 320 | 1238847.21 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | DOPRI5-adaptive | 157 | 16002.16 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | DOP853-adaptive | 301 | 1216.26 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | RKN12(10) | 426 | 1.53 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | DOPRI5-adaptive [cap=600 s] | 739 | 4145.26 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | IAS15 | 823 | 0.21 | yes | EDGE |
| Moon flyby vinf 3000 alt 100 | 1 m | WH-BM14A | 1,264 | 0.44 | yes | EDGE |
| Moon flyby vinf 3000 alt 100 | 1 m | DOP853-adaptive [cap=600 s] | 1,477 | 64.41 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | RKN12(10) [cap=600 s] | 2,092 | 0.02 | yes | EDGE |
| Moon flyby vinf 3000 alt 100 | 1 m | WH-Y6 | 600 | 94.23 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | IAS15 [cap=600 s] | 3,098 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 100 | 1 m | SRKN14A | 840 | 5146.01 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | GJ8 | 1,597 | 3977.07 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | DOPRI5-fixed | 481 | 236399.69 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | Y6 | 561 | 490053.26 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | Y6C | 561 | 490053.26 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | RK4 | 320 | 1261195.65 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | PEFRL | 320 | 944316.05 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | Encke-IAS15 | 12,654 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 100 | 1 m | Leapfrog | 81 | 12553813.00 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | DOP853-adaptive | 301 | 1216.26 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | RKN12(10) | 426 | 1.53 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | DOPRI5-adaptive | 157 | 16002.16 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | IAS15 | 823 | 0.21 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 739 | 4145.26 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | DOP853-adaptive [cap=600 s] | 1,477 | 64.41 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | RKN12(10) [cap=600 s] | 2,092 | 0.02 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | IAS15 [cap=600 s] | 3,098 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 100 | 0.01 m | WH-BM14A | 1,264 | 0.44 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | WH-Y6 | 600 | 94.23 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | GJ8 | 1,597 | 3977.07 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | SRKN14A | 840 | 5146.01 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | DOPRI5-fixed | 481 | 236399.69 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | Encke-IAS15 | 12,654 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 100 | 0.01 m | Y6C | 561 | 490053.26 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | Y6 | 561 | 490053.26 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | PEFRL | 320 | 944316.05 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | RK4 | 320 | 1261195.65 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | DOPRI5-adaptive | 157 | 17524.87 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | DOP853-adaptive | 301 | 1727.68 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | RKN12(10) | 426 | 1.56 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | IAS15 | 844 | 0.26 | yes | EDGE |
| Moon flyby vinf 3000 alt 1000 | 1 m | DOPRI5-adaptive [cap=600 s] | 1,093 | 4651.06 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | DOP853-adaptive [cap=600 s] | 2,185 | 60.84 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | WH-BM14A | 1,258 | 1.24 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | WH-Y6 | 598 | 264.76 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | RKN12(10) [cap=600 s] | 3,095 | 0.01 | yes | EDGE |
| Moon flyby vinf 3000 alt 1000 | 1 m | SRKN14A | 1,120 | 1101.44 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | IAS15 [cap=600 s] | 4,453 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 1000 | 1 m | GJ8 | 1,627 | 4870.70 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | DOPRI5-fixed | 481 | 246566.10 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | Y6 | 561 | 220743.20 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | Y6C | 561 | 220743.20 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | RK4 | 320 | 1428801.55 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | PEFRL | 320 | 1159603.50 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | Encke-IAS15 | 12,872 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 1000 | 1 m | Leapfrog | 81 | 13646400.95 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | DOP853-adaptive | 301 | 1727.68 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | RKN12(10) | 426 | 1.56 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | DOPRI5-adaptive | 157 | 17524.87 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | IAS15 | 844 | 0.26 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 1,093 | 4651.06 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | DOP853-adaptive [cap=600 s] | 2,185 | 60.84 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | RKN12(10) [cap=600 s] | 3,095 | 0.01 | yes | EDGE |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | IAS15 [cap=600 s] | 4,453 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | WH-BM14A | 1,258 | 1.24 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | WH-Y6 | 598 | 264.76 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | GJ8 | 1,627 | 4870.70 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | SRKN14A | 1,120 | 1101.44 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | DOPRI5-fixed | 481 | 246566.10 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | Encke-IAS15 | 12,872 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | Y6C | 561 | 220743.20 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | Y6 | 561 | 220743.20 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | PEFRL | 320 | 1159603.50 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | RK4 | 320 | 1428801.55 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | DOPRI5-adaptive | 151 | 8265.63 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | DOPRI5-adaptive [cap=600 s] | 265 | 8410.97 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | DOP853-adaptive | 301 | 244.65 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | RKN12(10) | 426 | 0.12 | yes | EDGE |
| Moon flyby vinf 10000 alt 1 | 1 m | DOP853-adaptive [cap=600 s] | 529 | 249.40 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | RKN12(10) [cap=600 s] | 749 | 0.12 | yes | EDGE |
| Moon flyby vinf 10000 alt 1 | 1 m | IAS15 | 760 | 0.03 | yes | EDGE |
| Moon flyby vinf 10000 alt 1 | 1 m | WH-Y6 | 548 | 1.56 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | WH-BM14A | 1,153 | 0.01 | yes | EDGE |
| Moon flyby vinf 10000 alt 1 | 1 m | IAS15 [cap=600 s] | 1,200 | 0.02 | yes | EDGE |
| Moon flyby vinf 10000 alt 1 | 1 m | SRKN14A | 560 | 3653.53 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | GJ8 | 1,339 | 3891.27 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | DOPRI5-fixed | 481 | 23531.65 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | Y6 | 561 | 67895.64 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | Y6C | 561 | 67895.64 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | RK4 | 320 | 178614.20 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | PEFRL | 320 | 155195.36 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | Encke-IAS15 | 10,384 | 0.00 | yes | EDGE |
| Moon flyby vinf 10000 alt 1 | 1 m | Leapfrog | 81 | 1502718.96 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | DOP853-adaptive | 301 | 244.65 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | RKN12(10) | 426 | 0.12 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | DOPRI5-adaptive | 151 | 8265.63 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 265 | 8410.97 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | DOP853-adaptive [cap=600 s] | 529 | 249.40 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | RKN12(10) [cap=600 s] | 749 | 0.12 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | IAS15 | 760 | 0.03 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | WH-BM14A | 1,153 | 0.01 | yes | EDGE |
| Moon flyby vinf 10000 alt 1 | 0.01 m | IAS15 [cap=600 s] | 1,200 | 0.02 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | WH-Y6 | 548 | 1.56 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | GJ8 | 1,339 | 3891.27 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | SRKN14A | 560 | 3653.53 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | DOPRI5-fixed | 481 | 23531.65 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | Y6 | 561 | 67895.64 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | Y6C | 561 | 67895.64 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | Encke-IAS15 | 10,384 | 0.00 | yes | EDGE |
| Moon flyby vinf 10000 alt 1 | 0.01 m | PEFRL | 320 | 155195.36 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | RK4 | 320 | 178614.20 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | Leapfrog | 81 | 1502718.96 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | DOPRI5-adaptive | 151 | 8301.67 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | DOPRI5-adaptive [cap=600 s] | 265 | 8447.11 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | DOP853-adaptive | 301 | 245.57 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | RKN12(10) | 426 | 0.12 | yes | EDGE |
| Moon flyby vinf 10000 alt 10 | 1 m | DOP853-adaptive [cap=600 s] | 529 | 250.33 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | IAS15 | 725 | 0.03 | yes | EDGE |
| Moon flyby vinf 10000 alt 10 | 1 m | RKN12(10) [cap=600 s] | 749 | 0.12 | yes | EDGE |
| Moon flyby vinf 10000 alt 10 | 1 m | WH-Y6 | 551 | 1.58 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | IAS15 [cap=600 s] | 1,158 | 0.00 | yes | EDGE |
| Moon flyby vinf 10000 alt 10 | 1 m | WH-BM14A | 1,158 | 0.01 | yes | EDGE |
| Moon flyby vinf 10000 alt 10 | 1 m | SRKN14A | 560 | 3654.97 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | GJ8 | 1,339 | 3892.56 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | DOPRI5-fixed | 481 | 23536.22 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | Y6C | 561 | 68009.73 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | Y6 | 561 | 68009.73 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | PEFRL | 320 | 155250.65 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | RK4 | 320 | 178669.24 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | Leapfrog | 81 | 1503105.91 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | DOP853-adaptive | 301 | 245.57 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | RKN12(10) | 426 | 0.12 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | DOPRI5-adaptive | 151 | 8301.67 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 265 | 8447.11 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | DOP853-adaptive [cap=600 s] | 529 | 250.33 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | RKN12(10) [cap=600 s] | 749 | 0.12 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | IAS15 | 725 | 0.03 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | IAS15 [cap=600 s] | 1,158 | 0.00 | yes | EDGE |
| Moon flyby vinf 10000 alt 10 | 0.01 m | WH-BM14A | 1,158 | 0.01 | yes | EDGE |
| Moon flyby vinf 10000 alt 10 | 0.01 m | WH-Y6 | 551 | 1.58 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | GJ8 | 1,339 | 3892.56 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | SRKN14A | 560 | 3654.97 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | DOPRI5-fixed | 481 | 23536.22 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | Y6C | 561 | 68009.73 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | Y6 | 561 | 68009.73 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | PEFRL | 320 | 155250.65 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | RK4 | 320 | 178669.24 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | Leapfrog | 81 | 1503105.91 | no | OK |
| Moon flyby vinf 10000 alt 100 | 1 m | DOPRI5-adaptive | 151 | 8659.03 | no | OK |
| Moon flyby vinf 10000 alt 100 | 1 m | DOPRI5-adaptive [cap=600 s] | 265 | 8805.01 | no | OK |
| Moon flyby vinf 10000 alt 100 | 1 m | DOP853-adaptive | 301 | 254.73 | no | OK |
| Moon flyby vinf 10000 alt 100 | 1 m | RKN12(10) | 426 | 0.12 | yes | EDGE |
| Moon flyby vinf 10000 alt 100 | 1 m | DOP853-adaptive [cap=600 s] | 529 | 259.54 | no | OK |
| Moon flyby vinf 10000 alt 100 | 1 m | RKN12(10) [cap=600 s] | 749 | 0.12 | yes | EDGE |
| Moon flyby vinf 10000 alt 100 | 1 m | IAS15 | 753 | 0.03 | yes | EDGE |
| Moon flyby vinf 10000 alt 100 | 1 m | WH-Y6 | 552 | 1.75 | no | OK |
| Moon flyby vinf 10000 alt 100 | 1 m | IAS15 [cap=600 s] | 1,158 | 0.02 | yes | EDGE |
| Moon flyby vinf 10000 alt 100 | 1 m | WH-BM14A | 1,161 | 0.01 | yes | EDGE |
| Moon flyby vinf 10000 alt 100 | 1 m | SRKN14A | 560 | 3668.84 | no | OK |
| Moon flyby vinf 10000 alt 100 | 1 m | GJ8 | 1,405 | 3904.83 | no | OK |
| Moon flyby vinf 10000 alt 100 | 1 m | DOPRI5-fixed | 481 | 23580.46 | no | OK |
| Moon flyby vinf 10000 alt 100 | 1 m | Y6 | 561 | 69097.40 | no | OK |
| Moon flyby vinf 10000 alt 100 | 1 m | Y6C | 561 | 69097.40 | no | OK |
| Moon flyby vinf 10000 alt 100 | 1 m | RK4 | 320 | 179192.55 | no | OK |
| Moon flyby vinf 10000 alt 100 | 1 m | PEFRL | 320 | 155776.45 | no | OK |
| Moon flyby vinf 10000 alt 100 | 1 m | Encke-IAS15 | 10,596 | 0.00 | yes | EDGE |
| Moon flyby vinf 10000 alt 100 | 1 m | Leapfrog | 81 | 1506766.27 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | DOP853-adaptive | 301 | 254.73 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | RKN12(10) | 426 | 0.12 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | DOPRI5-adaptive | 151 | 8659.03 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 265 | 8805.01 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | DOP853-adaptive [cap=600 s] | 529 | 259.54 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | RKN12(10) [cap=600 s] | 749 | 0.12 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | IAS15 | 753 | 0.03 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | WH-BM14A | 1,161 | 0.01 | yes | EDGE |
| Moon flyby vinf 10000 alt 100 | 0.01 m | IAS15 [cap=600 s] | 1,158 | 0.02 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | WH-Y6 | 552 | 1.75 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | GJ8 | 1,405 | 3904.83 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | SRKN14A | 560 | 3668.84 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | DOPRI5-fixed | 481 | 23580.46 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | Y6 | 561 | 69097.40 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | Y6C | 561 | 69097.40 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | Encke-IAS15 | 10,596 | 0.00 | yes | EDGE |
| Moon flyby vinf 10000 alt 100 | 0.01 m | PEFRL | 320 | 155776.45 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | RK4 | 320 | 179192.55 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | Leapfrog | 81 | 1506766.27 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | DOPRI5-adaptive | 151 | 11951.14 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | DOP853-adaptive | 301 | 340.66 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | RKN12(10) | 426 | 0.12 | yes | EDGE |
| Moon flyby vinf 10000 alt 1000 | 1 m | DOPRI5-adaptive [cap=600 s] | 385 | 5177.31 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | IAS15 | 746 | 0.03 | yes | EDGE |
| Moon flyby vinf 10000 alt 1000 | 1 m | DOP853-adaptive [cap=600 s] | 769 | 325.82 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | RKN12(10) [cap=600 s] | 1,089 | 0.12 | yes | EDGE |
| Moon flyby vinf 10000 alt 1000 | 1 m | WH-Y6 | 557 | 4.01 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | WH-BM14A | 1,172 | 0.02 | yes | EDGE |
| Moon flyby vinf 10000 alt 1000 | 1 m | IAS15 [cap=600 s] | 1,632 | 0.02 | yes | EDGE |
| Moon flyby vinf 10000 alt 1000 | 1 m | SRKN14A | 560 | 3767.26 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | GJ8 | 1,391 | 3985.34 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | DOPRI5-fixed | 481 | 23935.15 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | Y6 | 561 | 76764.23 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | Y6C | 561 | 76764.23 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | RK4 | 320 | 183096.42 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | PEFRL | 320 | 159709.44 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | Encke-IAS15 | 11,168 | 0.00 | yes | EDGE |
| Moon flyby vinf 10000 alt 1000 | 1 m | Leapfrog | 81 | 1526689.99 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | DOP853-adaptive | 301 | 340.66 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | RKN12(10) | 426 | 0.12 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | DOPRI5-adaptive | 151 | 11951.14 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 385 | 5177.31 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | IAS15 | 746 | 0.03 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | DOP853-adaptive [cap=600 s] | 769 | 325.82 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | RKN12(10) [cap=600 s] | 1,089 | 0.12 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | IAS15 [cap=600 s] | 1,632 | 0.02 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | WH-BM14A | 1,172 | 0.02 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | WH-Y6 | 557 | 4.01 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | GJ8 | 1,391 | 3985.34 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | SRKN14A | 560 | 3767.26 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | DOPRI5-fixed | 481 | 23935.15 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | Y6 | 561 | 76764.23 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | Y6C | 561 | 76764.23 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | Encke-IAS15 | 11,168 | 0.00 | yes | EDGE |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | PEFRL | 320 | 159709.44 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | RK4 | 320 | 183096.42 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | Leapfrog | 81 | 1526689.99 | no | OK |
| Heliocentric 0.3 AU e 0 | 1 m | RKN12(10) | 188 | 0.03 | yes | EDGE |
| Heliocentric 0.3 AU e 0 | 1 m | WH-Y6 | 195 | 0.16 | yes | EDGE |
| Heliocentric 0.3 AU e 0 | 1 m | WH-BM14A | 400 | 0.00 | yes | EDGE |
| Heliocentric 0.3 AU e 0 | 1 m | GJ8 | 459 | 1.86 | no | OK |
| Heliocentric 0.3 AU e 0 | 1 m | DOP853-adaptive | 133 | 166118.46 | no | OK |
| Heliocentric 0.3 AU e 0 | 1 m | SRKN14A | 224 | 4717.71 | no | OK |
| Heliocentric 0.3 AU e 0 | 1 m | DOPRI5-adaptive | 67 | 3825247514.93 | no | OK |
| Heliocentric 0.3 AU e 0 | 1 m | DOPRI5-fixed | 97 | 49484760.20 | no | OK |
| Heliocentric 0.3 AU e 0 | 1 m | Y6 | 113 | 72811020.71 | no | OK |
| Heliocentric 0.3 AU e 0 | 1 m | Y6C | 113 | 72811020.71 | no | OK |
| Heliocentric 0.3 AU e 0 | 1 m | RK4 | 64 | 498299676.89 | no | OK |
| Heliocentric 0.3 AU e 0 | 1 m | PEFRL | 64 | 114781354.78 | no | OK |
| Heliocentric 0.3 AU e 0 | 0.01 m | RKN12(10) | 188 | 0.03 | no | OK |
| Heliocentric 0.3 AU e 0 | 0.01 m | WH-BM14A | 400 | 0.00 | yes | EDGE |
| Heliocentric 0.3 AU e 0 | 0.01 m | DOP853-adaptive | 133 | 166118.46 | no | OK |
| Heliocentric 0.3 AU e 0 | 0.01 m | SRKN14A | 224 | 4717.71 | no | OK |
| Heliocentric 0.3 AU e 0 | 0.01 m | DOPRI5-adaptive | 67 | 3825247514.93 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | RKN12(10) | 205 | 157996.21 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | WH-BM14A | 485 | 0.05 | yes | EDGE |
| Heliocentric 0.3 AU e 0.5 | 1 m | WH-Y6 | 235 | 307.63 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | IAS15 | 700 | 9394.70 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | DOP853-adaptive | 180 | 4974566.10 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | GJ8 | 772 | 20233.30 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | DOPRI5-adaptive | 109 | 2613515982.13 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | SRKN14A | 224 | 4349568.66 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | DOPRI5-fixed | 97 | 48400386194.71 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | Y6 | 113 | 40578668472.70 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | Y6C | 113 | 40578668472.70 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | RK4 | 64 | 65487047838.76 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | PEFRL | 64 | 17869565809.96 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 0.01 m | RKN12(10) | 205 | 157996.21 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 0.01 m | WH-BM14A | 485 | 0.05 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 0.01 m | IAS15 | 700 | 9394.70 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 0.01 m | WH-Y6 | 235 | 307.63 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 0.01 m | DOP853-adaptive | 180 | 4974566.10 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 0.01 m | DOPRI5-adaptive | 109 | 2613515982.13 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 0.01 m | SRKN14A | 224 | 4349568.66 | no | OK |
| Heliocentric 5 AU e 0 | 1 m | DOP853-adaptive | 121 | 0.97 | yes | EDGE |
| Heliocentric 5 AU e 0 | 1 m | RKN12(10) | 171 | 0.03 | yes | EDGE |
| Heliocentric 5 AU e 0 | 1 m | DOPRI5-adaptive | 61 | 822.64 | no | OK |
| Heliocentric 5 AU e 0 | 1 m | IAS15 | 346 | 0.01 | yes | EDGE |
| Heliocentric 5 AU e 0 | 1 m | RK4 | 384 | 3.85 | no | OK |
| Heliocentric 5 AU e 0 | 1 m | PEFRL | 384 | 2.19 | no | OK |
| Heliocentric 5 AU e 0.5 | 1 m | RKN12(10) | 188 | 0.03 | yes | EDGE |
| Heliocentric 5 AU e 0.5 | 1 m | DOP853-adaptive | 133 | 11.89 | no | OK |
| Heliocentric 5 AU e 0.5 | 1 m | IAS15 | 417 | 0.02 | yes | EDGE |
| Heliocentric 5 AU e 0.5 | 1 m | DOPRI5-adaptive | 67 | 354593.97 | no | OK |
| Heliocentric 5 AU e 0.5 | 1 m | DOPRI5-fixed | 577 | 0.74 | yes | EDGE |
| Heliocentric 5 AU e 0.5 | 1 m | Y6 | 673 | 0.68 | yes | EDGE |
| Heliocentric 5 AU e 0.5 | 1 m | Y6C | 673 | 0.68 | yes | EDGE |
| Heliocentric 5 AU e 0.5 | 1 m | PEFRL | 384 | 243.01 | no | OK |
| Heliocentric 5 AU e 0.5 | 1 m | RK4 | 384 | 678.30 | no | OK |
| Heliocentric 5 AU e 0.99 | 1 m | RKN12(10) | 1,616 | 142833.95 | no | OK |
| Heliocentric 5 AU e 0.99 | 1 m | DOP853-adaptive | 1,141 | 64233939.45 | no | OK |
| Heliocentric 5 AU e 0.99 | 1 m | IAS15 | 2,937 | 33973.22 | no | OK |
| Heliocentric 5 AU e 0.99 | 1 m | DOPRI5-adaptive | 577 | 24531002588.06 | no | OK |
| Heliocentric 5 AU e 0.99 | 1 m | WH-BM14A | 4,844 | 7.78 | no | OK |
| Heliocentric 30 AU e 0.5 | 1 m | DOP853-adaptive | 109 | 0.91 | yes | EDGE |
| Heliocentric 30 AU e 0.5 | 1 m | DOPRI5-adaptive | 55 | 26.12 | no | OK |
| Far flight 30 AU e 0.5 (physical) | 1 m | DOP853-adaptive | 109 | 0.90 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) | 1 m | DOPRI5-adaptive | 55 | 26.13 | no | OK |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | DOP853-adaptive | 133 | 0.86 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | DOP853-adaptive [cap=600 s] | 133 | 0.86 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | DOPRI5-adaptive | 67 | 25.73 | no | OK |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | DOPRI5-adaptive [cap=600 s] | 67 | 25.73 | no | OK |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | RKN12(10) | 188 | 0.03 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | RKN12(10) [cap=600 s] | 188 | 0.03 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | IAS15 | 313 | 0.01 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | IAS15 [cap=600 s] | 313 | 0.01 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | GJ8 | 906 | 0.00 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | RK4 | 1,472 | 0.00 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | PEFRL | 1,472 | 0.01 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | DOPRI5-fixed | 2,209 | 0.00 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | Y6 | 2,577 | 0.00 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | Y6C | 2,577 | 0.00 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | SRKN14A | 5,152 | 0.00 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | WH-Y6 | 6,619 | 0.01 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | WH-BM14A | 13,812 | 0.01 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | Leapfrog | 369 | 4159.28 | no | OK |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | Encke-IAS15 | 94,130 | 0.00 | yes | EDGE |
| Earth flyby 1.1 R vinf 1000 | 1 m | DOP853-adaptive | 313 | 7861.46 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | RKN12(10) | 426 | 33.40 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | DOPRI5-adaptive | 163 | 483875.59 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | IAS15 | 963 | 0.83 | yes | EDGE |
| Earth flyby 1.1 R vinf 1000 | 1 m | DOPRI5-adaptive [cap=600 s] | 859 | 267373.86 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | WH-BM14A | 1,427 | 0.10 | yes | EDGE |
| Earth flyby 1.1 R vinf 1000 | 1 m | DOP853-adaptive [cap=600 s] | 1,717 | 179.48 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | RKN12(10) [cap=600 s] | 2,432 | 0.02 | yes | EDGE |
| Earth flyby 1.1 R vinf 1000 | 1 m | WH-Y6 | 676 | 27.41 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | IAS15 [cap=600 s] | 3,924 | 0.00 | yes | EDGE |
| Earth flyby 1.1 R vinf 1000 | 1 m | GJ8 | 1,623 | 57738.05 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | SRKN14A | 1,120 | 4851.94 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | DOPRI5-fixed | 481 | 43944208.10 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | Y6 | 561 | 43340971.17 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | Y6C | 561 | 43340971.17 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | Encke-IAS15 | 15,917 | 0.00 | yes | EDGE |
| Earth flyby 1.1 R vinf 1000 | 1 m | PEFRL | 320 | 15988365.54 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | RK4 | 320 | 66614230.38 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | Leapfrog | 81 | 305201074.48 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | RKN12(10) | 426 | 33.40 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | DOP853-adaptive | 313 | 7861.46 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | IAS15 | 963 | 0.83 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | DOPRI5-adaptive | 163 | 483875.59 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 859 | 267373.86 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | DOP853-adaptive [cap=600 s] | 1,717 | 179.48 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | RKN12(10) [cap=600 s] | 2,432 | 0.02 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | IAS15 [cap=600 s] | 3,924 | 0.00 | yes | EDGE |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | WH-BM14A | 1,427 | 0.10 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | WH-Y6 | 676 | 27.41 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | GJ8 | 1,623 | 57738.05 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | SRKN14A | 1,120 | 4851.94 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | Encke-IAS15 | 15,917 | 0.00 | yes | EDGE |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | DOPRI5-fixed | 481 | 43944208.10 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | Y6C | 561 | 43340971.17 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | Y6 | 561 | 43340971.17 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | PEFRL | 320 | 15988365.54 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | RK4 | 320 | 66614230.38 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | DOPRI5-adaptive | 163 | 109889.43 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | DOP853-adaptive | 313 | 671.61 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | RKN12(10) | 426 | 11.45 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | DOPRI5-adaptive [cap=600 s] | 739 | 40218.41 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | IAS15 | 851 | 1.07 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | WH-BM14A | 1,170 | 0.04 | yes | EDGE |
| Earth flyby 1.1 R vinf 10000 | 1 m | DOP853-adaptive [cap=600 s] | 1,477 | 270.20 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | WH-Y6 | 556 | 8.93 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | RKN12(10) [cap=600 s] | 2,092 | 0.07 | yes | EDGE |
| Earth flyby 1.1 R vinf 10000 | 1 m | IAS15 [cap=600 s] | 3,084 | 0.02 | yes | EDGE |
| Earth flyby 1.1 R vinf 10000 | 1 m | GJ8 | 1,889 | 2433.71 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | SRKN14A | 1,120 | 4812.30 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | DOPRI5-fixed | 481 | 1996521.58 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | Encke-IAS15 | 11,582 | 0.00 | yes | EDGE |
| Earth flyby 1.1 R vinf 10000 | 1 m | Y6C | 561 | 6125954.26 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | Y6 | 561 | 6125954.26 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | PEFRL | 320 | 4331711.71 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | RK4 | 320 | 7272048.62 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | Leapfrog | 81 | 79146744.36 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | RKN12(10) | 426 | 11.45 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | DOP853-adaptive | 313 | 671.61 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | DOPRI5-adaptive | 163 | 109889.43 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | IAS15 | 851 | 1.07 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 739 | 40218.41 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | DOP853-adaptive [cap=600 s] | 1,477 | 270.20 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | RKN12(10) [cap=600 s] | 2,092 | 0.07 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | WH-BM14A | 1,170 | 0.04 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | IAS15 [cap=600 s] | 3,084 | 0.02 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | WH-Y6 | 556 | 8.93 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | GJ8 | 1,889 | 2433.71 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | Encke-IAS15 | 11,582 | 0.00 | yes | EDGE |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | SRKN14A | 1,120 | 4812.30 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | DOPRI5-fixed | 481 | 1996521.58 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | Y6C | 561 | 6125954.26 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | Y6 | 561 | 6125954.26 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | PEFRL | 320 | 4331711.71 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | RK4 | 320 | 7272048.62 | no | OK |
| Earth flyby 3 R vinf 1000 | 1 m | RKN12(10) | 426 | 93.59 | no | OK |
| Earth flyby 3 R vinf 1000 | 1 m | DOP853-adaptive | 313 | 19545.95 | no | OK |
| Earth flyby 3 R vinf 1000 | 1 m | DOPRI5-adaptive | 163 | 1262038.33 | no | OK |
| Earth flyby 3 R vinf 1000 | 1 m | IAS15 | 963 | 2.32 | no | OK |
| Earth flyby 3 R vinf 1000 | 1 m | DOPRI5-adaptive [cap=600 s] | 3,733 | 223.95 | no | OK |
| Earth flyby 3 R vinf 1000 | 1 m | GJ8 | 1,938 | 3118.18 | no | OK |
| Earth flyby 3 R vinf 1000 | 1 m | WH-BM14A | 1,609 | 6.52 | no | OK |
| Earth flyby 3 R vinf 1000 | 1 m | WH-Y6 | 761 | 1991.33 | no | OK |
| Earth flyby 3 R vinf 1000 | 1 m | SRKN14A | 1,120 | 13770.65 | no | OK |
| Earth flyby 3 R vinf 1000 | 1 m | DOP853-adaptive [cap=600 s] | 7,465 | 0.02 | yes | EDGE |
| Earth flyby 3 R vinf 1000 | 1 m | RKN12(10) [cap=600 s] | 10,575 | 0.00 | yes | EDGE |
| Earth flyby 3 R vinf 1000 | 1 m | DOPRI5-fixed | 481 | 104008720.50 | no | OK |
| Earth flyby 3 R vinf 1000 | 1 m | Encke-IAS15 | 18,406 | 0.00 | yes | EDGE |
| Earth flyby 3 R vinf 1000 | 1 m | Y6C | 561 | 110958866.62 | no | OK |
| Earth flyby 3 R vinf 1000 | 1 m | Y6 | 561 | 110958866.62 | no | OK |
| Earth flyby 3 R vinf 1000 | 1 m | PEFRL | 320 | 39768164.19 | no | OK |
| Earth flyby 3 R vinf 1000 | 1 m | RK4 | 320 | 159341214.07 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | RKN12(10) | 426 | 93.59 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | DOP853-adaptive | 313 | 19545.95 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | IAS15 | 963 | 2.32 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | DOPRI5-adaptive | 163 | 1262038.33 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 3,733 | 223.95 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | GJ8 | 1,938 | 3118.18 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | DOP853-adaptive [cap=600 s] | 7,465 | 0.02 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | WH-BM14A | 1,609 | 6.52 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | RKN12(10) [cap=600 s] | 10,575 | 0.00 | yes | EDGE |
| Earth flyby 3 R vinf 1000 | 0.01 m | WH-Y6 | 761 | 1991.33 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | SRKN14A | 1,120 | 13770.65 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | Encke-IAS15 | 18,406 | 0.00 | yes | EDGE |
| Earth flyby 3 R vinf 1000 | 0.01 m | DOPRI5-fixed | 481 | 104008720.50 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | Y6C | 561 | 110958866.62 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | Y6 | 561 | 110958866.62 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | PEFRL | 320 | 39768164.19 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | RK4 | 320 | 159341214.07 | no | OK |
| Earth flyby 3 R vinf 10000 | 1 m | DOP853-adaptive | 313 | 1649.72 | no | OK |
| Earth flyby 3 R vinf 10000 | 1 m | DOPRI5-adaptive | 157 | 130273.40 | no | OK |
| Earth flyby 3 R vinf 10000 | 1 m | RKN12(10) | 426 | 10.76 | no | OK |
| Earth flyby 3 R vinf 10000 | 1 m | IAS15 | 753 | 1.75 | no | OK |
| Earth flyby 3 R vinf 10000 | 1 m | WH-BM14A | 1,269 | 0.65 | yes | EDGE |
| Earth flyby 3 R vinf 10000 | 1 m | DOPRI5-adaptive [cap=600 s] | 2,059 | 319.57 | no | OK |
| Earth flyby 3 R vinf 10000 | 1 m | WH-Y6 | 602 | 150.29 | no | OK |
| Earth flyby 3 R vinf 10000 | 1 m | DOP853-adaptive [cap=600 s] | 4,117 | 0.76 | yes | EDGE |
| Earth flyby 3 R vinf 10000 | 1 m | GJ8 | 1,854 | 4411.92 | no | OK |
| Earth flyby 3 R vinf 10000 | 1 m | RKN12(10) [cap=600 s] | 5,832 | 0.00 | yes | EDGE |
| Earth flyby 3 R vinf 10000 | 1 m | SRKN14A | 1,120 | 8029.53 | no | OK |
| Earth flyby 3 R vinf 10000 | 1 m | DOPRI5-fixed | 481 | 1699808.05 | no | OK |
| Earth flyby 3 R vinf 10000 | 1 m | Encke-IAS15 | 12,027 | 0.00 | yes | EDGE |
| Earth flyby 3 R vinf 10000 | 1 m | Y6C | 561 | 1748480.43 | no | OK |
| Earth flyby 3 R vinf 10000 | 1 m | Y6 | 561 | 1748480.43 | no | OK |
| Earth flyby 3 R vinf 10000 | 1 m | PEFRL | 320 | 8321758.79 | no | OK |
| Earth flyby 3 R vinf 10000 | 1 m | RK4 | 320 | 10481205.08 | no | OK |
| Earth flyby 3 R vinf 10000 | 1 m | Leapfrog | 81 | 97519709.12 | no | OK |
| Earth flyby 3 R vinf 10000 | 0.01 m | RKN12(10) | 426 | 10.76 | no | OK |
| Earth flyby 3 R vinf 10000 | 0.01 m | DOP853-adaptive | 313 | 1649.72 | no | OK |
| Earth flyby 3 R vinf 10000 | 0.01 m | IAS15 | 753 | 1.75 | no | OK |
| Earth flyby 3 R vinf 10000 | 0.01 m | DOPRI5-adaptive | 157 | 130273.40 | no | OK |
| Earth flyby 3 R vinf 10000 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 2,059 | 319.57 | no | OK |
| Earth flyby 3 R vinf 10000 | 0.01 m | WH-BM14A | 1,269 | 0.65 | no | OK |
| Earth flyby 3 R vinf 10000 | 0.01 m | DOP853-adaptive [cap=600 s] | 4,117 | 0.76 | no | OK |
| Earth flyby 3 R vinf 10000 | 0.01 m | RKN12(10) [cap=600 s] | 5,832 | 0.00 | yes | EDGE |
| Earth flyby 3 R vinf 10000 | 0.01 m | WH-Y6 | 602 | 150.29 | no | OK |
| Earth flyby 3 R vinf 10000 | 0.01 m | GJ8 | 1,854 | 4411.92 | no | OK |
| Earth flyby 3 R vinf 10000 | 0.01 m | Encke-IAS15 | 12,027 | 0.00 | yes | EDGE |
| Earth flyby 3 R vinf 10000 | 0.01 m | SRKN14A | 1,120 | 8029.53 | no | OK |
| Earth flyby 3 R vinf 10000 | 0.01 m | DOPRI5-fixed | 481 | 1699808.05 | no | OK |
| Earth flyby 3 R vinf 10000 | 0.01 m | Y6C | 561 | 1748480.43 | no | OK |
| Earth flyby 3 R vinf 10000 | 0.01 m | Y6 | 561 | 1748480.43 | no | OK |
| Earth flyby 3 R vinf 10000 | 0.01 m | PEFRL | 320 | 8321758.79 | no | OK |
| Earth flyby 3 R vinf 10000 | 0.01 m | RK4 | 320 | 10481205.08 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | RKN12(10) | 426 | 327.23 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | DOP853-adaptive | 313 | 47482.09 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | DOPRI5-adaptive | 163 | 3850068.26 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | IAS15 | 949 | 9.57 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | GJ8 | 2,543 | 1977.57 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | WH-BM14A | 1,482 | 1248.56 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | SRKN14A | 1,120 | 54655.29 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | WH-Y6 | 1,403 | 20795.78 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | Encke-IAS15 | 17,327 | 0.00 | yes | EDGE |
| Earth flyby 10 R vinf 1000 | 1 m | DOPRI5-fixed | 481 | 221017705.07 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | Y6 | 561 | 305596490.66 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | Y6C | 561 | 305596490.66 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | DOPRI5-adaptive [cap=600 s] | 21,847 | 0.05 | yes | EDGE |
| Earth flyby 10 R vinf 1000 | 1 m | RK4 | 320 | 355682548.28 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | PEFRL | 320 | 97365637.83 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | DOP853-adaptive [cap=600 s] | 43,693 | 0.00 | yes | EDGE |
| Earth flyby 10 R vinf 1000 | 0.01 m | RKN12(10) | 426 | 327.23 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | DOP853-adaptive | 313 | 47482.09 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | IAS15 | 949 | 9.57 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | DOPRI5-adaptive | 163 | 3850068.26 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | GJ8 | 2,543 | 1977.57 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | WH-BM14A | 1,482 | 1248.56 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | Encke-IAS15 | 17,327 | 0.00 | yes | EDGE |
| Earth flyby 10 R vinf 1000 | 0.01 m | SRKN14A | 1,120 | 54655.29 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 21,847 | 0.05 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | WH-Y6 | 1,403 | 20795.78 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | DOPRI5-fixed | 481 | 221017705.07 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | DOP853-adaptive [cap=600 s] | 43,693 | 0.00 | yes | EDGE |
| Earth flyby 10 R vinf 1000 | 0.01 m | Y6 | 561 | 305596490.66 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | Y6C | 561 | 305596490.66 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | RK4 | 320 | 355682548.28 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | PEFRL | 320 | 97365637.83 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | DOP853-adaptive | 301 | 10879.77 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | DOPRI5-adaptive | 151 | 328365.42 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | RKN12(10) | 426 | 10.13 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | IAS15 | 746 | 2.28 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | WH-BM14A | 1,172 | 11.06 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | WH-Y6 | 557 | 2925.33 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | GJ8 | 1,840 | 6185.37 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | SRKN14A | 1,120 | 11292.19 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | DOPRI5-adaptive [cap=600 s] | 7,573 | 0.29 | yes | EDGE |
| Earth flyby 10 R vinf 10000 | 1 m | DOPRI5-fixed | 481 | 1847506.08 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | Y6C | 561 | 3583980.44 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | Y6 | 561 | 3583980.44 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | Encke-IAS15 | 11,042 | 0.00 | yes | EDGE |
| Earth flyby 10 R vinf 10000 | 1 m | DOP853-adaptive [cap=600 s] | 15,145 | 0.00 | yes | EDGE |
| Earth flyby 10 R vinf 10000 | 1 m | PEFRL | 320 | 11672876.92 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | RK4 | 320 | 13589281.30 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | RKN12(10) [cap=600 s] | 21,455 | 0.00 | yes | EDGE |
| Earth flyby 10 R vinf 10000 | 1 m | Leapfrog | 81 | 114969325.68 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | RKN12(10) | 426 | 10.13 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | DOP853-adaptive | 301 | 10879.77 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | IAS15 | 746 | 2.28 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | DOPRI5-adaptive | 151 | 328365.42 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | WH-BM14A | 1,172 | 11.06 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | GJ8 | 1,840 | 6185.37 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 7,573 | 0.29 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | WH-Y6 | 557 | 2925.33 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | Encke-IAS15 | 11,042 | 0.00 | yes | EDGE |
| Earth flyby 10 R vinf 10000 | 0.01 m | SRKN14A | 1,120 | 11292.19 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | DOP853-adaptive [cap=600 s] | 15,145 | 0.00 | yes | EDGE |
| Earth flyby 10 R vinf 10000 | 0.01 m | DOPRI5-fixed | 481 | 1847506.08 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | RKN12(10) [cap=600 s] | 21,455 | 0.00 | yes | EDGE |
| Earth flyby 10 R vinf 10000 | 0.01 m | Y6C | 561 | 3583980.44 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | Y6 | 561 | 3583980.44 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | PEFRL | 320 | 11672876.92 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | RK4 | 320 | 13589281.30 | no | OK |
| Small-body flyby vinf 10 | 1 m | DOPRI5-adaptive | 151 | 850.59 | no | OK |
| Small-body flyby vinf 10 | 1 m | DOPRI5-adaptive [cap=600 s] | 151 | 850.59 | no | OK |
| Small-body flyby vinf 10 | 1 m | DOP853-adaptive | 301 | 9.94 | no | OK |
| Small-body flyby vinf 10 | 1 m | DOP853-adaptive [cap=600 s] | 301 | 9.94 | no | OK |
| Small-body flyby vinf 10 | 1 m | RKN12(10) [cap=600 s] | 426 | 0.01 | yes | EDGE |
| Small-body flyby vinf 10 | 1 m | RKN12(10) | 426 | 0.01 | yes | EDGE |
| Small-body flyby vinf 10 | 1 m | IAS15 [cap=600 s] | 963 | 0.00 | yes | EDGE |
| Small-body flyby vinf 10 | 1 m | IAS15 | 963 | 0.00 | yes | EDGE |
| Small-body flyby vinf 10 | 1 m | SRKN14A | 280 | 691.60 | no | OK |
| Small-body flyby vinf 10 | 1 m | WH-Y6 | 1,753 | 0.00 | yes | EDGE |
| Small-body flyby vinf 10 | 1 m | GJ8 | 1,336 | 856.40 | no | OK |
| Small-body flyby vinf 10 | 1 m | DOPRI5-fixed | 481 | 13711.62 | no | OK |
| Small-body flyby vinf 10 | 1 m | RK4 | 320 | 20691.66 | no | OK |
| Small-body flyby vinf 10 | 1 m | PEFRL | 320 | 4829.48 | no | OK |
| Small-body flyby vinf 10 | 1 m | WH-BM14A | 3,734 | 0.00 | yes | EDGE |
| Small-body flyby vinf 10 | 1 m | Y6C | 561 | 12862.73 | no | OK |
| Small-body flyby vinf 10 | 1 m | Y6 | 561 | 12862.73 | no | OK |
| Small-body flyby vinf 10 | 1 m | Leapfrog | 81 | 89611.31 | no | OK |
| Small-body flyby vinf 10 | 1 m | Encke-IAS15 | 40,159 | 0.00 | yes | EDGE |
| Small-body flyby vinf 10 | 0.01 m | DOPRI5-adaptive | 151 | 850.59 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 151 | 850.59 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | DOP853-adaptive | 301 | 9.94 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | DOP853-adaptive [cap=600 s] | 301 | 9.94 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | RKN12(10) | 426 | 0.01 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | RKN12(10) [cap=600 s] | 426 | 0.01 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | IAS15 | 963 | 0.00 | yes | EDGE |
| Small-body flyby vinf 10 | 0.01 m | IAS15 [cap=600 s] | 963 | 0.00 | yes | EDGE |
| Small-body flyby vinf 10 | 0.01 m | WH-Y6 | 1,753 | 0.00 | yes | EDGE |
| Small-body flyby vinf 10 | 0.01 m | GJ8 | 1,336 | 856.40 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | WH-BM14A | 3,734 | 0.00 | yes | EDGE |
| Small-body flyby vinf 10 | 0.01 m | SRKN14A | 280 | 691.60 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | DOPRI5-fixed | 481 | 13711.62 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | Y6 | 561 | 12862.73 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | Y6C | 561 | 12862.73 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | RK4 | 320 | 20691.66 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | PEFRL | 320 | 4829.48 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | Encke-IAS15 | 40,159 | 0.00 | yes | EDGE |
| Small-body flyby vinf 10 | 0.01 m | Leapfrog | 81 | 89611.31 | no | OK |
| Small-body flyby vinf 50 | 1 m | DOPRI5-adaptive | 151 | 839.07 | no | OK |
| Small-body flyby vinf 50 | 1 m | DOPRI5-adaptive [cap=600 s] | 151 | 839.07 | no | OK |
| Small-body flyby vinf 50 | 1 m | DOP853-adaptive | 301 | 9.71 | no | OK |
| Small-body flyby vinf 50 | 1 m | DOP853-adaptive [cap=600 s] | 301 | 9.71 | no | OK |
| Small-body flyby vinf 50 | 1 m | RKN12(10) | 426 | 0.01 | yes | EDGE |
| Small-body flyby vinf 50 | 1 m | RKN12(10) [cap=600 s] | 426 | 0.01 | yes | EDGE |
| Small-body flyby vinf 50 | 1 m | IAS15 [cap=600 s] | 977 | 0.00 | yes | EDGE |
| Small-body flyby vinf 50 | 1 m | IAS15 | 977 | 0.00 | yes | EDGE |
| Small-body flyby vinf 50 | 1 m | WH-Y6 | 1,660 | 0.00 | yes | EDGE |
| Small-body flyby vinf 50 | 1 m | SRKN14A | 280 | 678.00 | no | OK |
| Small-body flyby vinf 50 | 1 m | GJ8 | 1,336 | 841.92 | no | OK |
| Small-body flyby vinf 50 | 1 m | DOPRI5-fixed | 481 | 13368.28 | no | OK |
| Small-body flyby vinf 50 | 1 m | WH-BM14A | 3,534 | 0.00 | yes | EDGE |
| Small-body flyby vinf 50 | 1 m | RK4 | 320 | 20195.62 | no | OK |
| Small-body flyby vinf 50 | 1 m | PEFRL | 320 | 4752.63 | no | OK |
| Small-body flyby vinf 50 | 1 m | Y6C | 561 | 12720.89 | no | OK |
| Small-body flyby vinf 50 | 1 m | Y6 | 561 | 12720.89 | no | OK |
| Small-body flyby vinf 50 | 1 m | Leapfrog | 81 | 88900.75 | no | OK |
| Small-body flyby vinf 50 | 1 m | Encke-IAS15 | 37,258 | 0.00 | yes | EDGE |
| Small-body flyby vinf 50 | 0.01 m | DOPRI5-adaptive | 151 | 839.07 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 151 | 839.07 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | DOP853-adaptive | 301 | 9.71 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | DOP853-adaptive [cap=600 s] | 301 | 9.71 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | RKN12(10) | 426 | 0.01 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | RKN12(10) [cap=600 s] | 426 | 0.01 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | IAS15 | 977 | 0.00 | yes | EDGE |
| Small-body flyby vinf 50 | 0.01 m | IAS15 [cap=600 s] | 977 | 0.00 | yes | EDGE |
| Small-body flyby vinf 50 | 0.01 m | WH-Y6 | 1,660 | 0.00 | yes | EDGE |
| Small-body flyby vinf 50 | 0.01 m | GJ8 | 1,336 | 841.92 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | WH-BM14A | 3,534 | 0.00 | yes | EDGE |
| Small-body flyby vinf 50 | 0.01 m | SRKN14A | 280 | 678.00 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | DOPRI5-fixed | 481 | 13368.28 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | Y6 | 561 | 12720.89 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | Y6C | 561 | 12720.89 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | RK4 | 320 | 20195.62 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | PEFRL | 320 | 4752.63 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | Encke-IAS15 | 37,258 | 0.00 | yes | EDGE |
| Small-body flyby vinf 50 | 0.01 m | Leapfrog | 81 | 88900.75 | no | OK |
| Small-body flyby vinf 100 | 1 m | DOPRI5-adaptive | 151 | 806.58 | no | OK |
| Small-body flyby vinf 100 | 1 m | DOPRI5-adaptive [cap=600 s] | 151 | 806.58 | no | OK |
| Small-body flyby vinf 100 | 1 m | DOP853-adaptive | 301 | 9.07 | no | OK |
| Small-body flyby vinf 100 | 1 m | DOP853-adaptive [cap=600 s] | 301 | 9.07 | no | OK |
| Small-body flyby vinf 100 | 1 m | RKN12(10) | 426 | 0.01 | yes | EDGE |
| Small-body flyby vinf 100 | 1 m | RKN12(10) [cap=600 s] | 426 | 0.01 | yes | EDGE |
| Small-body flyby vinf 100 | 1 m | IAS15 [cap=600 s] | 970 | 0.00 | yes | EDGE |
| Small-body flyby vinf 100 | 1 m | IAS15 | 970 | 0.00 | yes | EDGE |
| Small-body flyby vinf 100 | 1 m | WH-Y6 | 1,387 | 0.00 | yes | EDGE |
| Small-body flyby vinf 100 | 1 m | SRKN14A | 280 | 638.49 | no | OK |
| Small-body flyby vinf 100 | 1 m | GJ8 | 1,322 | 800.66 | no | OK |
| Small-body flyby vinf 100 | 1 m | DOPRI5-fixed | 481 | 12392.49 | no | OK |
| Small-body flyby vinf 100 | 1 m | WH-BM14A | 2,949 | 0.00 | yes | EDGE |
| Small-body flyby vinf 100 | 1 m | RK4 | 320 | 18799.43 | no | OK |
| Small-body flyby vinf 100 | 1 m | PEFRL | 320 | 4528.01 | no | OK |
| Small-body flyby vinf 100 | 1 m | Y6C | 561 | 12303.06 | no | OK |
| Small-body flyby vinf 100 | 1 m | Y6 | 561 | 12303.06 | no | OK |
| Small-body flyby vinf 100 | 1 m | Leapfrog | 81 | 86759.05 | no | OK |
| Small-body flyby vinf 100 | 1 m | Encke-IAS15 | 31,682 | 0.00 | yes | EDGE |
| Small-body flyby vinf 100 | 0.01 m | DOPRI5-adaptive | 151 | 806.58 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | DOPRI5-adaptive [cap=600 s] | 151 | 806.58 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | DOP853-adaptive | 301 | 9.07 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | DOP853-adaptive [cap=600 s] | 301 | 9.07 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | RKN12(10) | 426 | 0.01 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | RKN12(10) [cap=600 s] | 426 | 0.01 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | IAS15 | 970 | 0.00 | yes | EDGE |
| Small-body flyby vinf 100 | 0.01 m | IAS15 [cap=600 s] | 970 | 0.00 | yes | EDGE |
| Small-body flyby vinf 100 | 0.01 m | WH-Y6 | 1,387 | 0.00 | yes | EDGE |
| Small-body flyby vinf 100 | 0.01 m | WH-BM14A | 2,949 | 0.00 | yes | EDGE |
| Small-body flyby vinf 100 | 0.01 m | GJ8 | 1,322 | 800.66 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | SRKN14A | 280 | 638.49 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | DOPRI5-fixed | 481 | 12392.49 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | Y6 | 561 | 12303.06 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | Y6C | 561 | 12303.06 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | RK4 | 320 | 18799.43 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | PEFRL | 320 | 4528.01 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | Encke-IAS15 | 31,682 | 0.00 | yes | EDGE |
| Small-body flyby vinf 100 | 0.01 m | Leapfrog | 81 | 86759.05 | no | OK |
## WH invalid (failed validity)

Only rows whose run aborted in the Kepler drift are listed. `h<0` is a negative *substep* of the BM14A composition, not a negative outer step; the outer step is positive. After the Kepler solver fallback fix this section is expected to be empty.

| scenario | method | config | note |
|---|---|---|---|
| — | — | — | no Kepler-drift abort in this run |

## Energy and angular-momentum drift at equal RHS budgets

For each scenario and budget, the run of each method with the largest cost inside `[0.5 x budget, budget]`. Runs that stop well below the budget are not compared as if they had spent it; a method with no run in that band is omitted. `dE` and `dL` are the maximum absolute changes over the checkpoints; for J2, `dE` uses the J2 potential. Values are only comparable within one row of the table.

### Earth LEO 400 km, 30 d — budget 100,000 RHS

| rank | method | cost | dE | dL | position error |
|---:|---|---:|---:|---:|---:|
| 1 | RKN12(10) | 69,735 | 3.87E-004 | 3.42E-001 | 2.00E-001 |
| 2 | GJ8 | 70,484 | 4.39E-004 | 3.87E-001 | 2.29E-001 |
| 3 | SRKN14A | 89,600 | 8.45E-004 | 7.45E-001 | 8.45E+002 |
| 4 | PEFRL | 86,016 | 1.18E-001 | 1.04E+002 | 1.18E+005 |
| 5 | Y6C | 66,305 | 1.34E+000 | 1.13E+003 | 1.27E+006 |
| 6 | Y6 | 66,305 | 1.34E+000 | 1.13E+003 | 1.27E+006 |
| 7 | DOP853-adaptive | 86,053 | 9.07E+000 | 8.01E+003 | 4.52E+003 |
| 8 | Leapfrog | 71,937 | 2.55E+001 | 6.63E+003 | 9.86E+006 |
| 9 | DOPRI5-fixed | 86,017 | 5.95E+003 | 5.25E+006 | 3.02E+006 |
| 10 | DOPRI5-adaptive | 86,029 | 6.58E+003 | 5.80E+006 | 3.34E+006 |
| 11 | RK4 | 86,016 | 1.17E+005 | 1.03E+008 | 1.35E+007 |

### Earth LEO 400 km, 30 d — budget 1,000,000 RHS

| rank | method | cost | dE | dL | position error |
|---:|---|---:|---:|---:|---:|
| 1 | Y6C | 7.56E+005 | 4.31E-006 | 3.80E-003 | 6.07E-001 |
| 2 | Y6 | 7.56E+005 | 6.20E-006 | 5.47E-003 | 6.02E-001 |
| 3 | PEFRL | 9.71E+005 | 1.01E-005 | 8.93E-003 | 7.29E+000 |
| 4 | Leapfrog | 8.19E+005 | 8.53E-002 | 7.50E+001 | 8.53E+004 |
| 5 | DOPRI5-fixed | 9.71E+005 | 9.37E-002 | 8.27E+001 | 4.75E+001 |
| 6 | DOPRI5-adaptive | 8.57E+005 | 1.74E-001 | 1.54E+002 | 8.82E+001 |
| 7 | RK4 | 9.71E+005 | 6.24E-001 | 5.50E+002 | 3.26E+002 |

### J2 x1 inc 98 — budget 100,000 RHS

| rank | method | cost | dE | dL | position error |
|---:|---|---:|---:|---:|---:|
| 1 | Encke-IAS15 | 65,176 | 1.38E-007 | - | 1.89E-006 |
| 2 | PEFRL | 72,832 | 1.39E-006 | - | 9.55E-003 |
| 3 | DOPRI5-fixed | 72,865 | 5.49E-005 | - | 9.24E-004 |
| 4 | DOPRI5-adaptive | 71,815 | 5.89E-005 | - | 9.94E-004 |
| 5 | RK4 | 72,832 | 3.62E-004 | - | 1.90E-002 |
| 6 | Leapfrog | 92,129 | 3.15E-002 | - | 2.52E+002 |

### J2 x1 inc 98 — budget 1,000,000 RHS

| rank | method | cost | dE | dL | position error |
|---:|---|---:|---:|---:|---:|
| 1 | Leapfrog | 7.00E+005 | 5.49E-004 | - | 4.37E+000 |

### Moon orbit 100 km x1000 orbits — budget 100,000 RHS

| rank | method | cost | dE | dL | position error |
|---:|---|---:|---:|---:|---:|
| 1 | WH-BM14A | 78,394 | 4.45E-004 | 5.01E-001 | 1.50E+000 |
| 2 | WH-Y6 | 85,541 | 5.86E-003 | 6.59E+000 | 2.33E+001 |
| 3 | RKN12(10) | 95,830 | 9.49E-003 | 1.07E+001 | 5.72E+001 |
| 4 | GJ8 | 67,701 | 2.71E-001 | 3.05E+002 | 1.64E+003 |
| 5 | SRKN14A | 86,016 | 1.22E+000 | 1.37E+003 | 5.74E+004 |
| 6 | PEFRL | 81,920 | 3.35E+001 | 3.77E+004 | 1.67E+006 |
| 7 | Y6 | 94,977 | 4.10E+001 | 4.55E+004 | 3.68E+006 |
| 8 | Y6C | 94,977 | 4.10E+001 | 4.55E+004 | 3.68E+006 |
| 9 | Leapfrog | 68,353 | 6.30E+001 | 4.63E+004 | 3.68E+006 |
| 10 | DOP853-adaptive | 89,125 | 6.18E+002 | 6.95E+005 | 3.19E+006 |
| 11 | DOPRI5-fixed | 81,409 | 7.33E+007 | 7.87E+013 | 4.63E+010 |
| 12 | RK4 | 81,920 | 4.11E+009 | 9.48E+015 | 1.61E+011 |

### Moon orbit 100 km x1000 orbits — budget 1,000,000 RHS

| rank | method | cost | dE | dL | position error |
|---:|---|---:|---:|---:|---:|
| 1 | SRKN14A | 9.57E+005 | 2.59E-006 | 2.91E-003 | 4.47E-002 |
| 2 | DOP853-adaptive | 5.87E+005 | 2.94E-005 | 3.31E-002 | 1.73E-001 |
| 3 | Y6 | 7.19E+005 | 9.73E-004 | 1.09E+000 | 4.57E+001 |
| 4 | Y6C | 7.19E+005 | 9.73E-004 | 1.09E+000 | 4.57E+001 |
| 5 | PEFRL | 9.24E+005 | 2.31E-003 | 2.59E+000 | 1.08E+002 |
| 6 | DOPRI5-fixed | 9.23E+005 | 5.40E-001 | 6.07E+002 | 3.24E+003 |
| 7 | DOPRI5-adaptive | 9.20E+005 | 5.53E-001 | 6.21E+002 | 3.32E+003 |
| 8 | RK4 | 9.24E+005 | 3.75E+000 | 4.22E+003 | 2.27E+004 |
| 9 | Leapfrog | 7.78E+005 | 5.34E+000 | 6.01E+003 | 2.50E+005 |

### Heliocentric 1 AU e 0.5 — budget 100,000 RHS

| rank | method | cost | dE | dL | position error |
|---:|---|---:|---:|---:|---:|
| 1 | RK4 | 71,104 | 1.17E-005 | 2.50E+001 | 1.72E-002 |
| 2 | PEFRL | 71,104 | 1.53E-005 | 5.10E+001 | 6.96E-002 |
| 3 | Leapfrog | 89,969 | 5.87E+000 | 1.57E+004 | 3.27E+004 |

### Heliocentric 1 AU e 0.5 — budget 1,000,000 RHS

| rank | method | cost | dE | dL | position error |
|---:|---|---:|---:|---:|---:|
| 1 | PEFRL | 8.10E+005 | 7.63E-005 | 3.41E+002 | 2.10E-001 |

