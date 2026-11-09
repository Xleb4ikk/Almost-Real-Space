# Test559: analysis

Everything below is computed from `Test559_PureGravity.csv` by `StudyRegimeAnalysis` (one code path). Interpolation, floor checks and tie bands are the same `StudyReport` logic used by the sweep, not a second implementation.

Classes: `D` decisive (margin > 2), `M` marginal (1.5-2), `E` equivalent (< 1.5, set listed), `F` floor-limited (a cheaper grid point already met the target), `T` trivial (cost < 200), `S` same-family reference (not a decisive accuracy claim), `U` unresolved/no winner.

## Cell table (1 m and 0.01 m)

| scenario | target | class | winner | cost | stages | margin | equivalent / floor-limited |
|---|---|---|---|---:|---:|---|---|
| Moon orbit 1 km | 1 m | F | DOP853-adaptive | 217 | 12 | 1.03 | floor: WH-Y6, RKN12(10), WH-BM14A, GJ8, IAS15 |
| Moon orbit 1 km | 0.01 m | F | WH-Y6 | 225 | 7 | 1.39 | floor: RKN12(10), WH-BM14A, GJ8 |
| Moon orbit 10 km | 1 m | F | DOP853-adaptive | 217 | 12 | 1.03 | floor: WH-Y6, RKN12(10), WH-BM14A, GJ8, IAS15 |
| Moon orbit 10 km | 0.01 m | F | WH-Y6 | 225 | 7 | 1.39 | floor: RKN12(10), WH-BM14A, GJ8 |
| Moon orbit 100 km | 1 m | F | DOP853-adaptive | 223 | 12 | 1.00 | floor: WH-Y6, RKN12(10), WH-BM14A, GJ8, IAS15, Encke-IAS15 |
| Moon orbit 100 km | 0.01 m | F | WH-Y6 | 225 | 7 | 1.39 | floor: RKN12(10), WH-BM14A, GJ8, Encke-IAS15 |
| Moon orbit 1000 km | 1 m | F | SRKN14A | 224 | 14 | 1.00 | floor: WH-Y6, RKN12(10), WH-BM14A, GJ8 |
| Moon orbit 1000 km | 0.01 m | F | WH-Y6 | 225 | 7 | 1.39 | floor: RKN12(10), WH-BM14A, GJ8 |
| Moon orbit 100 km x10 orbits | 1 m | E | RKN12(10) | 749 | 16 | 1.12 | RKN12(10), WH-Y6, WH-BM14A |
| Moon orbit 100 km x10 orbits | 0.01 m | E | WH-BM14A | 870 | 14 | 1.19 | WH-BM14A, RKN12(10) |
| Moon orbit 100 km x100 orbits | 1 m | E | WH-BM14A | 8,700 | 14 | 1.08 | WH-BM14A, GJ8, RKN12(10), WH-Y6 |
| Moon orbit 100 km x100 orbits | 0.01 m | E | RKN12(10) | 13,684 | 16 | 1.01 | RKN12(10), GJ8, WH-BM14A |
| Moon orbit 100 km x1000 orbits | 1 m | E | WH-BM14A | 1.04E+005 | 14 | 1.25 | WH-BM14A, RKN12(10), GJ8 |
| Moon orbit 100 km x1000 orbits | 0.01 m | U | - | - | 0 | - | - |
| Moon hop 50 m/s | 1 m | F | Leapfrog | 9 | 1 | - | floor: Leapfrog, RK4, PEFRL, GJ8 |
| Moon hop 50 m/s | 0.01 m | T | Leapfrog | 17 | 1 | inf | - |
| Moon hop 200 m/s | 1 m | F | Leapfrog | 27 | 1 | inf | floor: RK4, PEFRL, DOPRI5-fixed, Y6, Y6C, DOPRI5-adaptive, GJ8 |
| Moon hop 200 m/s | 0.01 m | F | Leapfrog | 263 | 1 | inf | floor: RK4, PEFRL, DOPRI5-fixed, Y6, Y6C, DOPRI5-adaptive, GJ8 |
| Moon hop 500 m/s | 1 m | F | Leapfrog | 183 | 1 | inf | floor: RK4, PEFRL, DOPRI5-fixed, Y6, Y6C, DOPRI5-adaptive, SRKN14A, DOP853-adaptive, RKN12(10), GJ8 |
| Moon hop 500 m/s | 0.01 m | F | PEFRL | 71 | 4 | 1.36 | floor: DOPRI5-fixed, Y6, Y6C, DOPRI5-adaptive, SRKN14A, DOP853-adaptive, RKN12(10), GJ8 |
| Moon hop 1000 m/s | 1 m | F | DOPRI5-adaptive | 77 | 6 | 1.26 | floor: SRKN14A, WH-Y6, DOP853-adaptive, RKN12(10), GJ8 |
| Moon hop 1000 m/s | 0.01 m | F | DOPRI5-fixed | 145 | 6 | 1.01 | floor: SRKN14A, WH-Y6, RKN12(10) |
| Moon hop 1200 m/s | 1 m | F | DOPRI5-fixed | 97 | 6 | 1.11 | floor: SRKN14A, WH-Y6, DOP853-adaptive, RKN12(10) |
| Moon hop 1200 m/s | 0.01 m | F | DOP853-adaptive | 159 | 12 | 1.13 | floor: WH-Y6, RKN12(10) |
| Moon hop 1400 m/s | 1 m | F | DOPRI5-fixed | 97 | 6 | 1.00 | floor: SRKN14A, WH-Y6, DOP853-adaptive, GJ8, IAS15 |
| Moon hop 1400 m/s | 0.01 m | F | DOPRI5-fixed | 190 | 6 | 1.17 | floor: SRKN14A, WH-Y6, DOP853-adaptive, IAS15 |
| Moon hop 1500 m/s | 1 m | F | DOPRI5-fixed | 97 | 6 | 1.06 | floor: SRKN14A, WH-Y6, DOP853-adaptive, RKN12(10), GJ8 |
| Moon hop 1500 m/s | 0.01 m | F | DOPRI5-fixed | 187 | 6 | 1.14 | floor: SRKN14A, WH-Y6, DOP853-adaptive, RKN12(10) |
| Moon hop 1600 m/s | 1 m | F | DOPRI5-fixed | 97 | 6 | 1.14 | floor: SRKN14A, WH-Y6, DOP853-adaptive, RKN12(10), GJ8 |
| Moon hop 1600 m/s | 0.01 m | F | DOPRI5-fixed | 185 | 6 | 1.15 | floor: SRKN14A, WH-Y6, DOP853-adaptive, RKN12(10), GJ8 |
| Moon flyby vinf 50 alt 1 | 1 m | E | DOP853-adaptive | 396 | 12 | 1.13 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 50 alt 1 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.17 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 50 alt 10 | 1 m | E | DOP853-adaptive | 396 | 12 | 1.16 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 50 alt 10 | 0.01 m | E | RKN12(10) | 470 | 16 | 1.19 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 50 alt 100 | 1 m | E | DOP853-adaptive | 398 | 12 | 1.14 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 50 alt 100 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.17 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 50 alt 1000 | 1 m | E | DOP853-adaptive | 409 | 12 | 1.13 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 50 alt 1000 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.22 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 200 alt 1 | 1 m | E | DOP853-adaptive | 394 | 12 | 1.14 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 200 alt 1 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.17 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 200 alt 10 | 1 m | E | DOP853-adaptive | 395 | 12 | 1.14 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 200 alt 10 | 0.01 m | E | RKN12(10) | 472 | 16 | 1.18 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 200 alt 100 | 1 m | E | DOP853-adaptive | 394 | 12 | 1.11 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 200 alt 100 | 0.01 m | E | RKN12(10) | 473 | 16 | 1.19 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 200 alt 1000 | 1 m | E | DOP853-adaptive | 406 | 12 | 1.13 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 200 alt 1000 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.21 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 500 alt 1 | 1 m | E | DOP853-adaptive | 399 | 12 | 1.12 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 500 alt 1 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.15 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 500 alt 10 | 1 m | E | DOP853-adaptive | 399 | 12 | 1.13 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 500 alt 10 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.15 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 500 alt 100 | 1 m | E | DOP853-adaptive | 399 | 12 | 1.13 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 500 alt 100 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.15 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 500 alt 1000 | 1 m | E | DOP853-adaptive | 390 | 12 | 1.15 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 500 alt 1000 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.16 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 1000 alt 1 | 1 m | E | DOP853-adaptive | 380 | 12 | 1.05 | DOP853-adaptive, DOPRI5-adaptive, RKN12(10) |
| Moon flyby vinf 1000 alt 1 | 0.01 m | E | RKN12(10) | 472 | 16 | 1.10 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 1000 alt 10 | 1 m | E | DOP853-adaptive | 380 | 12 | 1.05 | DOP853-adaptive, DOPRI5-adaptive, RKN12(10) |
| Moon flyby vinf 1000 alt 10 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.09 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 1000 alt 100 | 1 m | E | DOP853-adaptive | 383 | 12 | 1.05 | DOP853-adaptive, DOPRI5-adaptive, RKN12(10) |
| Moon flyby vinf 1000 alt 100 | 0.01 m | E | RKN12(10) | 474 | 16 | 1.09 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 1000 alt 1000 | 1 m | E | DOP853-adaptive | 383 | 12 | 1.02 | DOP853-adaptive, DOPRI5-adaptive, RKN12(10) |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | E | RKN12(10) | 475 | 16 | 1.07 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 3000 alt 1 | 1 m | E | DOPRI5-adaptive | 283 | 6 | 1.30 | DOPRI5-adaptive, DOP853-adaptive |
| Moon flyby vinf 3000 alt 1 | 0.01 m | E | DOP853-adaptive | 409 | 12 | 1.13 | DOP853-adaptive, RKN12(10) |
| Moon flyby vinf 3000 alt 10 | 1 m | E | DOPRI5-adaptive | 283 | 6 | 1.30 | DOPRI5-adaptive, DOP853-adaptive |
| Moon flyby vinf 3000 alt 10 | 0.01 m | E | DOP853-adaptive | 409 | 12 | 1.17 | DOP853-adaptive, RKN12(10) |
| Moon flyby vinf 3000 alt 100 | 1 m | E | DOPRI5-adaptive | 280 | 6 | 1.31 | DOPRI5-adaptive, DOP853-adaptive |
| Moon flyby vinf 3000 alt 100 | 0.01 m | E | DOP853-adaptive | 410 | 12 | 1.12 | DOP853-adaptive, RKN12(10) |
| Moon flyby vinf 3000 alt 1000 | 1 m | E | DOPRI5-adaptive | 302 | 6 | 1.27 | DOPRI5-adaptive, DOP853-adaptive, RKN12(10) |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | E | DOP853-adaptive | 406 | 12 | 1.17 | DOP853-adaptive, RKN12(10) |
| Moon flyby vinf 10000 alt 1 | 1 m | E | DOPRI5-adaptive | 238 | 6 | 1.46 | DOPRI5-adaptive, DOP853-adaptive |
| Moon flyby vinf 10000 alt 1 | 0.01 m | F | DOP853-adaptive | 399 | 12 | 1.11 | floor: WH-BM14A, Encke-IAS15 |
| Moon flyby vinf 10000 alt 10 | 1 m | E | DOPRI5-adaptive | 238 | 6 | 1.46 | DOPRI5-adaptive, DOP853-adaptive |
| Moon flyby vinf 10000 alt 10 | 0.01 m | F | DOP853-adaptive | 399 | 12 | 1.11 | floor: WH-BM14A |
| Moon flyby vinf 10000 alt 100 | 1 m | E | DOPRI5-adaptive | 236 | 6 | 1.46 | DOPRI5-adaptive, DOP853-adaptive |
| Moon flyby vinf 10000 alt 100 | 0.01 m | F | DOP853-adaptive | 396 | 12 | 1.11 | floor: WH-BM14A, IAS15 |
| Moon flyby vinf 10000 alt 1000 | 1 m | E | DOPRI5-adaptive | 241 | 6 | 1.45 | DOPRI5-adaptive, DOP853-adaptive |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | E | DOP853-adaptive | 404 | 12 | 1.10 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Heliocentric 0.3 AU e 0 | 1 m | F | GJ8 | 466 | 1 | 1.06 | floor: RKN12(10), WH-Y6, WH-BM14A |
| Heliocentric 0.3 AU e 0 | 0.01 m | F | RKN12(10) | 355 | 16 | 1.26 | floor: WH-BM14A |
| Heliocentric 0.3 AU e 0.5 | 1 m | F | RKN12(10) | 370 | 16 | 1.52 | floor: WH-BM14A |
| Heliocentric 0.3 AU e 0.5 | 0.01 m | E | RKN12(10) | 480 | 16 | 1.45 | RKN12(10), WH-BM14A |
| Heliocentric 0.3 AU e 0.9 | 1 m | S | RKN12(10) | 989 | 16 | 1.88 | RKN12(10), RKN12(10) |
| Heliocentric 0.3 AU e 0.9 | 0.01 m | S | RKN12(10) | 1,234 | 16 | 8.27 | - |
| Heliocentric 0.3 AU e 0.99 | 1 m | E | RKN12(10) | 5,242 | 16 | 1.20 | RKN12(10), DOP853-adaptive |
| Heliocentric 0.3 AU e 0.99 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 1 AU e 0 | 1 m | S | RKN12(10) | 226 | 16 | 2.55 | - |
| Heliocentric 1 AU e 0 | 0.01 m | S | RKN12(10) | 404 | 16 | 2.39 | - |
| Heliocentric 1 AU e 0.5 | 1 m | S | RKN12(10) | 468 | 16 | 1.93 | RKN12(10), RKN12(10) |
| Heliocentric 1 AU e 0.5 | 0.01 m | S | RKN12(10) | 708 | 16 | inf | - |
| Heliocentric 1 AU e 0.9 | 1 m | D | RKN12(10) | 925 | 16 | 2.14 | - |
| Heliocentric 1 AU e 0.9 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 1 AU e 0.99 | 1 m | U | - | - | 0 | - | - |
| Heliocentric 1 AU e 0.99 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 5 AU e 0 | 1 m | F | DOPRI5-adaptive | 216 | 6 | 2.17 | floor: DOP853-adaptive, RKN12(10), IAS15 |
| Heliocentric 5 AU e 0 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 5 AU e 0.5 | 1 m | F | DOP853-adaptive | 205 | 12 | 2.44 | floor: RKN12(10), IAS15, DOPRI5-fixed, Y6, Y6C |
| Heliocentric 5 AU e 0.5 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 5 AU e 0.9 | 1 m | E | RKN12(10) | 369 | 16 | 1.50 | RKN12(10), DOP853-adaptive |
| Heliocentric 5 AU e 0.9 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 5 AU e 0.99 | 1 m | E | RKN12(10) | 1,729 | 16 | 1.12 | RKN12(10), DOP853-adaptive |
| Heliocentric 5 AU e 0.99 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 30 AU e 0 | 1 m | T | DOPRI5-adaptive | 103 | 6 | 1.55 | - |
| Heliocentric 30 AU e 0 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 30 AU e 0.5 | 1 m | F | DOPRI5-adaptive | 110 | 6 | inf | floor: DOP853-adaptive |
| Heliocentric 30 AU e 0.5 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 30 AU e 0.9 | 1 m | S | DOP853-adaptive | 218 | 12 | 1.69 | DOP853-adaptive, DOP853-adaptive |
| Heliocentric 30 AU e 0.9 | 0.01 m | U | - | - | 0 | - | - |
| Heliocentric 30 AU e 0.99 | 1 m | S | RKN12(10) | 375 | 16 | 1.74 | RKN12(10), RKN12(10) |
| Heliocentric 30 AU e 0.99 | 0.01 m | U | - | - | 0 | - | - |
| Far flight 30 AU e 0.5 (physical) | 1 m | F | DOPRI5-adaptive | 110 | 6 | inf | floor: DOP853-adaptive |
| Far flight 30 AU e 0.5 (physical) | 0.01 m | U | - | - | 0 | - | - |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | F | DOPRI5-adaptive | 131 | 6 | 180.23 | floor: DOP853-adaptive, RKN12(10), IAS15, GJ8, RK4, PEFRL, DOPRI5-fixed, Y6, Y6C, SRKN14A, WH-Y6, WH-BM14A, Encke-IAS15 |
| Far flight 30 AU e 0.5 (physical) (normalized) | 0.01 m | U | - | - | 0 | - | - |
| Earth flyby 1.1 R vinf 1000 | 1 m | F | DOP853-adaptive | 423 | 12 | 1.09 | floor: WH-BM14A, IAS15, Encke-IAS15 |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | E | RKN12(10) | 481 | 16 | 1.33 | RKN12(10), DOP853-adaptive |
| Earth flyby 1.1 R vinf 10000 | 1 m | E | DOPRI5-adaptive | 339 | 6 | 1.14 | DOPRI5-adaptive, DOP853-adaptive, RKN12(10) |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.03 | RKN12(10), DOP853-adaptive |
| Earth flyby 3 R vinf 1000 | 1 m | E | RKN12(10) | 455 | 16 | 1.03 | RKN12(10), DOP853-adaptive |
| Earth flyby 3 R vinf 1000 | 0.01 m | E | RKN12(10) | 494 | 16 | 1.38 | RKN12(10), DOP853-adaptive |
| Earth flyby 3 R vinf 10000 | 1 m | F | DOP853-adaptive | 389 | 12 | 1.01 | floor: WH-BM14A, IAS15 |
| Earth flyby 3 R vinf 10000 | 0.01 m | E | RKN12(10) | 477 | 16 | 1.04 | RKN12(10), DOP853-adaptive |
| Earth flyby 10 R vinf 1000 | 1 m | E | RKN12(10) | 473 | 16 | 1.05 | RKN12(10), DOP853-adaptive |
| Earth flyby 10 R vinf 1000 | 0.01 m | E | RKN12(10) | 501 | 16 | 1.43 | RKN12(10), DOP853-adaptive |
| Earth flyby 10 R vinf 10000 | 1 m | E | DOP853-adaptive | 401 | 12 | 1.06 | DOP853-adaptive, DOPRI5-adaptive, RKN12(10) |
| Earth flyby 10 R vinf 10000 | 0.01 m | E | RKN12(10) | 485 | 16 | 1.08 | RKN12(10), DOP853-adaptive |
| Small-body flyby vinf 10 | 1 m | F | DOPRI5-adaptive | 221 | 6 | 1.47 | floor: WH-Y6, RKN12(10), WH-BM14A, IAS15, Encke-IAS15 |
| Small-body flyby vinf 10 | 0.01 m | F | DOP853-adaptive | 367 | 12 | 1.01 | floor: WH-Y6, WH-BM14A, IAS15, Encke-IAS15 |
| Small-body flyby vinf 50 | 1 m | F | DOPRI5-adaptive | 221 | 6 | 1.47 | floor: WH-Y6, RKN12(10), WH-BM14A, IAS15, Encke-IAS15 |
| Small-body flyby vinf 50 | 0.01 m | F | DOP853-adaptive | 367 | 12 | 1.01 | floor: WH-Y6, WH-BM14A, IAS15, Encke-IAS15 |
| Small-body flyby vinf 100 | 1 m | F | DOPRI5-adaptive | 220 | 6 | 1.48 | floor: WH-Y6, RKN12(10), WH-BM14A, IAS15, Encke-IAS15 |
| Small-body flyby vinf 100 | 0.01 m | F | DOP853-adaptive | 366 | 12 | 1.00 | floor: WH-Y6, WH-BM14A, IAS15, Encke-IAS15 |

## 2D maps

### Heliocentric a x e — 1 m

| a \ e | 0 | 0.5 | 0.9 | 0.99 |
|---|---|---|---|---|
| 0.3 | floor: RKN12(10), WH-Y6, WH-BM14A | floor: WH-BM14A | RKN12(10) S | RKN12(10) ~ DOP853-adaptive E |
| 1 | RKN12(10) S | RKN12(10) S | RKN12(10) D 2.14 | — |
| 5 | floor: DOP853-adaptive, RKN12(10), IAS15 | floor: RKN12(10), IAS15, DOPRI5-fixed, Y6, Y6C | RKN12(10) ~ DOP853-adaptive E | RKN12(10) ~ DOP853-adaptive E |
| 30 | DOPRI5-adaptive T (6 st) | floor: DOP853-adaptive | DOP853-adaptive S | RKN12(10) S |

### Heliocentric a x e — 0.01 m

| a \ e | 0 | 0.5 | 0.9 | 0.99 |
|---|---|---|---|---|
| 0.3 | floor: WH-BM14A | RKN12(10) ~ WH-BM14A E | RKN12(10) S | — |
| 1 | RKN12(10) S | RKN12(10) S | — | — |
| 5 | — | — | — | — |
| 30 | — | — | — | — |

### Planet flyby periapsis x v_inf — 1 m

| R \ v_inf | 1000 | 10000 |
|---|---|---|
| 1.1 | floor: WH-BM14A, IAS15, Encke-IAS15 | DOPRI5-adaptive ~ DOP853-adaptive ~ RKN12(10) E |
| 3 | RKN12(10) ~ DOP853-adaptive E | floor: WH-BM14A, IAS15 |
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
| Moon orbit 1 km | 1 m | WH-Y6 | 113 | 0.01 | yes | EDGE |
| Moon orbit 1 km | 1 m | RKN12(10) | 188 | 0.00 | yes | EDGE |
| Moon orbit 1 km | 1 m | DOP853-adaptive | 133 | 6.44 | no | OK |
| Moon orbit 1 km | 1 m | SRKN14A | 112 | 11.41 | no | OK |
| Moon orbit 1 km | 1 m | WH-BM14A | 232 | 0.00 | yes | EDGE |
| Moon orbit 1 km | 1 m | DOPRI5-fixed | 49 | 180746.91 | no | OK |
| Moon orbit 1 km | 1 m | DOPRI5-adaptive | 67 | 148104.51 | no | OK |
| Moon orbit 1 km | 1 m | Y6C | 57 | 115034.48 | no | OK |
| Moon orbit 1 km | 1 m | Y6 | 57 | 115034.48 | no | OK |
| Moon orbit 1 km | 1 m | GJ8 | 452 | 0.00 | yes | EDGE |
| Moon orbit 1 km | 1 m | PEFRL | 32 | 66907.93 | no | OK |
| Moon orbit 1 km | 1 m | IAS15 | 593 | 0.02 | yes | EDGE |
| Moon orbit 1 km | 1 m | RK4 | 32 | 585918.46 | no | OK |
| Moon orbit 1 km | 1 m | Leapfrog | 9 | 2093654.13 | no | OK |
| Moon orbit 1 km | 0.01 m | RKN12(10) | 188 | 0.00 | yes | EDGE |
| Moon orbit 1 km | 0.01 m | WH-Y6 | 113 | 0.01 | no | OK |
| Moon orbit 1 km | 0.01 m | WH-BM14A | 232 | 0.00 | yes | EDGE |
| Moon orbit 1 km | 0.01 m | DOP853-adaptive | 133 | 6.44 | no | OK |
| Moon orbit 1 km | 0.01 m | SRKN14A | 112 | 11.41 | no | OK |
| Moon orbit 1 km | 0.01 m | GJ8 | 452 | 0.00 | yes | EDGE |
| Moon orbit 1 km | 0.01 m | IAS15 | 593 | 0.02 | no | OK |
| Moon orbit 1 km | 0.01 m | DOPRI5-fixed | 49 | 180746.91 | no | OK |
| Moon orbit 1 km | 0.01 m | DOPRI5-adaptive | 67 | 148104.51 | no | OK |
| Moon orbit 1 km | 0.01 m | Y6 | 57 | 115034.48 | no | OK |
| Moon orbit 1 km | 0.01 m | Y6C | 57 | 115034.48 | no | OK |
| Moon orbit 1 km | 0.01 m | PEFRL | 32 | 66907.93 | no | OK |
| Moon orbit 1 km | 0.01 m | RK4 | 32 | 585918.46 | no | OK |
| Moon orbit 10 km | 1 m | WH-Y6 | 113 | 0.01 | yes | EDGE |
| Moon orbit 10 km | 1 m | RKN12(10) | 188 | 0.00 | yes | EDGE |
| Moon orbit 10 km | 1 m | DOP853-adaptive | 133 | 6.47 | no | OK |
| Moon orbit 10 km | 1 m | SRKN14A | 112 | 11.47 | no | OK |
| Moon orbit 10 km | 1 m | WH-BM14A | 232 | 0.00 | yes | EDGE |
| Moon orbit 10 km | 1 m | DOPRI5-fixed | 49 | 181682.35 | no | OK |
| Moon orbit 10 km | 1 m | DOPRI5-adaptive | 67 | 148870.94 | no | OK |
| Moon orbit 10 km | 1 m | Y6C | 57 | 115629.87 | no | OK |
| Moon orbit 10 km | 1 m | Y6 | 57 | 115629.87 | no | OK |
| Moon orbit 10 km | 1 m | GJ8 | 452 | 0.00 | yes | EDGE |
| Moon orbit 10 km | 1 m | PEFRL | 32 | 67254.30 | no | OK |
| Moon orbit 10 km | 1 m | IAS15 | 593 | 0.02 | yes | EDGE |
| Moon orbit 10 km | 1 m | RK4 | 32 | 588951.48 | no | OK |
| Moon orbit 10 km | 1 m | Leapfrog | 9 | 2104494.91 | no | OK |
| Moon orbit 10 km | 0.01 m | RKN12(10) | 188 | 0.00 | yes | EDGE |
| Moon orbit 10 km | 0.01 m | WH-Y6 | 113 | 0.01 | no | OK |
| Moon orbit 10 km | 0.01 m | WH-BM14A | 232 | 0.00 | yes | EDGE |
| Moon orbit 10 km | 0.01 m | DOP853-adaptive | 133 | 6.47 | no | OK |
| Moon orbit 10 km | 0.01 m | SRKN14A | 112 | 11.47 | no | OK |
| Moon orbit 10 km | 0.01 m | GJ8 | 452 | 0.00 | yes | EDGE |
| Moon orbit 10 km | 0.01 m | IAS15 | 593 | 0.02 | no | OK |
| Moon orbit 10 km | 0.01 m | DOPRI5-fixed | 49 | 181682.35 | no | OK |
| Moon orbit 10 km | 0.01 m | DOPRI5-adaptive | 67 | 148870.94 | no | OK |
| Moon orbit 10 km | 0.01 m | Y6 | 57 | 115629.87 | no | OK |
| Moon orbit 10 km | 0.01 m | Y6C | 57 | 115629.87 | no | OK |
| Moon orbit 10 km | 0.01 m | PEFRL | 32 | 67254.30 | no | OK |
| Moon orbit 10 km | 0.01 m | RK4 | 32 | 588951.48 | no | OK |
| Moon orbit 100 km | 1 m | WH-Y6 | 113 | 0.01 | yes | EDGE |
| Moon orbit 100 km | 1 m | RKN12(10) | 188 | 0.00 | yes | EDGE |
| Moon orbit 100 km | 1 m | DOP853-adaptive | 133 | 6.80 | no | OK |
| Moon orbit 100 km | 1 m | SRKN14A | 112 | 12.06 | no | OK |
| Moon orbit 100 km | 1 m | WH-BM14A | 232 | 0.00 | yes | EDGE |
| Moon orbit 100 km | 1 m | DOPRI5-fixed | 49 | 191036.09 | no | OK |
| Moon orbit 100 km | 1 m | DOPRI5-adaptive | 67 | 156534.67 | no | OK |
| Moon orbit 100 km | 1 m | Y6 | 57 | 121583.39 | no | OK |
| Moon orbit 100 km | 1 m | Y6C | 57 | 121583.39 | no | OK |
| Moon orbit 100 km | 1 m | GJ8 | 459 | 0.00 | yes | EDGE |
| Moon orbit 100 km | 1 m | PEFRL | 32 | 70717.87 | no | OK |
| Moon orbit 100 km | 1 m | IAS15 | 586 | 0.02 | yes | EDGE |
| Moon orbit 100 km | 1 m | RK4 | 32 | 619280.38 | no | OK |
| Moon orbit 100 km | 1 m | Encke-IAS15 | 4,802 | 0.00 | yes | EDGE |
| Moon orbit 100 km | 1 m | Leapfrog | 9 | 2212901.54 | no | OK |
| Moon orbit 100 km | 0.01 m | RKN12(10) | 188 | 0.00 | yes | EDGE |
| Moon orbit 100 km | 0.01 m | WH-Y6 | 113 | 0.01 | no | OK |
| Moon orbit 100 km | 0.01 m | WH-BM14A | 232 | 0.00 | yes | EDGE |
| Moon orbit 100 km | 0.01 m | DOP853-adaptive | 133 | 6.80 | no | OK |
| Moon orbit 100 km | 0.01 m | SRKN14A | 112 | 12.06 | no | OK |
| Moon orbit 100 km | 0.01 m | GJ8 | 459 | 0.00 | yes | EDGE |
| Moon orbit 100 km | 0.01 m | IAS15 | 586 | 0.02 | no | OK |
| Moon orbit 100 km | 0.01 m | DOPRI5-fixed | 49 | 191036.09 | no | OK |
| Moon orbit 100 km | 0.01 m | DOPRI5-adaptive | 67 | 156534.67 | no | OK |
| Moon orbit 100 km | 0.01 m | Y6 | 57 | 121583.39 | no | OK |
| Moon orbit 100 km | 0.01 m | Y6C | 57 | 121583.39 | no | OK |
| Moon orbit 100 km | 0.01 m | PEFRL | 32 | 70717.87 | no | OK |
| Moon orbit 100 km | 0.01 m | RK4 | 32 | 619280.38 | no | OK |
| Moon orbit 100 km | 0.01 m | Encke-IAS15 | 4,802 | 0.00 | yes | EDGE |
| Moon orbit 1000 km | 1 m | WH-Y6 | 113 | 0.06 | yes | EDGE |
| Moon orbit 1000 km | 1 m | RKN12(10) | 188 | 0.00 | yes | EDGE |
| Moon orbit 1000 km | 1 m | SRKN14A | 112 | 17.96 | no | OK |
| Moon orbit 1000 km | 1 m | DOP853-adaptive | 133 | 10.14 | no | OK |
| Moon orbit 1000 km | 1 m | WH-BM14A | 232 | 0.00 | yes | EDGE |
| Moon orbit 1000 km | 1 m | DOPRI5-fixed | 49 | 284482.86 | no | OK |
| Moon orbit 1000 km | 1 m | DOPRI5-adaptive | 67 | 233086.19 | no | OK |
| Moon orbit 1000 km | 1 m | GJ8 | 452 | 0.00 | yes | EDGE |
| Moon orbit 1000 km | 1 m | Y6C | 57 | 181067.27 | no | OK |
| Moon orbit 1000 km | 1 m | Y6 | 57 | 181067.27 | no | OK |
| Moon orbit 1000 km | 1 m | PEFRL | 32 | 105336.11 | no | OK |
| Moon orbit 1000 km | 1 m | RK4 | 32 | 922386.09 | no | OK |
| Moon orbit 1000 km | 1 m | Leapfrog | 9 | 3296813.48 | no | OK |
| Moon orbit 1000 km | 0.01 m | RKN12(10) | 188 | 0.00 | yes | EDGE |
| Moon orbit 1000 km | 0.01 m | WH-Y6 | 113 | 0.06 | no | OK |
| Moon orbit 1000 km | 0.01 m | WH-BM14A | 232 | 0.00 | yes | EDGE |
| Moon orbit 1000 km | 0.01 m | DOP853-adaptive | 133 | 10.14 | no | OK |
| Moon orbit 1000 km | 0.01 m | SRKN14A | 112 | 17.96 | no | OK |
| Moon orbit 1000 km | 0.01 m | GJ8 | 452 | 0.00 | yes | EDGE |
| Moon orbit 1000 km | 0.01 m | DOPRI5-fixed | 49 | 284482.86 | no | OK |
| Moon orbit 1000 km | 0.01 m | DOPRI5-adaptive | 67 | 233086.19 | no | OK |
| Moon orbit 1000 km | 0.01 m | Y6 | 57 | 181067.27 | no | OK |
| Moon orbit 1000 km | 0.01 m | Y6C | 57 | 181067.27 | no | OK |
| Moon orbit 1000 km | 0.01 m | PEFRL | 32 | 105336.11 | no | OK |
| Moon orbit 1000 km | 0.01 m | RK4 | 32 | 922386.09 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | RKN12(10) | 409 | 10513.99 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | WH-Y6 | 281 | 12348.35 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | WH-BM14A | 290 | 1271.00 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | GJ8 | 1,207 | 10.75 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | DOP853-adaptive | 517 | 150539.63 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | IAS15 | 1,845 | 17.44 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | SRKN14A | 560 | 7020.28 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | Y6 | 281 | 3994857.66 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | Y6C | 281 | 3994857.66 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | DOPRI5-fixed | 241 | 203208108.41 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | DOPRI5-adaptive | 589 | 3494226.43 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | PEFRL | 160 | 3790909.61 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | RK4 | 160 | 238786798.89 | no | OK |
| Moon orbit 100 km x10 orbits | 1 m | Encke-IAS15 | 29,302 | 0.00 | yes | EDGE |
| Moon orbit 100 km x10 orbits | 1 m | Leapfrog | 41 | 12625009.08 | no | OK |
| Moon orbit 100 km x10 orbits | 0.01 m | WH-BM14A | 290 | 1271.00 | no | OK |
| Moon orbit 100 km x10 orbits | 0.01 m | RKN12(10) | 409 | 10513.99 | no | OK |
| Moon orbit 100 km x10 orbits | 0.01 m | GJ8 | 1,207 | 10.75 | no | OK |
| Moon orbit 100 km x10 orbits | 0.01 m | WH-Y6 | 281 | 12348.35 | no | OK |
| Moon orbit 100 km x10 orbits | 0.01 m | DOP853-adaptive | 517 | 150539.63 | no | OK |
| Moon orbit 100 km x10 orbits | 0.01 m | IAS15 | 1,845 | 17.44 | no | OK |
| Moon orbit 100 km x10 orbits | 0.01 m | SRKN14A | 560 | 7020.28 | no | OK |
| Moon orbit 100 km x10 orbits | 0.01 m | Y6 | 281 | 3994857.66 | no | OK |
| Moon orbit 100 km x10 orbits | 0.01 m | Y6C | 281 | 3994857.66 | no | OK |
| Moon orbit 100 km x10 orbits | 0.01 m | DOPRI5-fixed | 241 | 203208108.41 | no | OK |
| Moon orbit 100 km x10 orbits | 0.01 m | DOPRI5-adaptive | 589 | 3494226.43 | no | OK |
| Moon orbit 100 km x10 orbits | 0.01 m | Encke-IAS15 | 29,302 | 0.00 | yes | EDGE |
| Moon orbit 100 km x10 orbits | 0.01 m | PEFRL | 160 | 3790909.61 | no | OK |
| Moon orbit 100 km x10 orbits | 0.01 m | RK4 | 160 | 238786798.89 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | WH-BM14A | 2,900 | 25696.19 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | GJ8 | 3,909 | 5418.26 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | RKN12(10) | 3,469 | 2307233.06 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | WH-Y6 | 2,801 | 411214.78 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | IAS15 | 17,145 | 1005.49 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | DOP853-adaptive | 4,837 | 3647417.26 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | SRKN14A | 5,600 | 70199.32 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | Y6C | 2,801 | 4125476.85 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | Y6 | 2,801 | 4125476.85 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | PEFRL | 1,600 | 3856585.65 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | DOPRI5-fixed | 2,401 | 1912854320.38 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | DOPRI5-adaptive | 7,675 | 3620481.20 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | RK4 | 1,600 | 2476945412.77 | no | OK |
| Moon orbit 100 km x100 orbits | 1 m | Encke-IAS15 | 3.40E+005 | 0.00 | yes | EDGE |
| Moon orbit 100 km x100 orbits | 0.01 m | RKN12(10) | 3,469 | 2307233.06 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | GJ8 | 3,909 | 5418.26 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | WH-BM14A | 2,900 | 25696.19 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | WH-Y6 | 2,801 | 411214.78 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | IAS15 | 17,145 | 1005.49 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | DOP853-adaptive | 4,837 | 3647417.26 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | SRKN14A | 5,600 | 70199.32 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | Y6 | 2,801 | 4125476.85 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | Y6C | 2,801 | 4125476.85 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | Encke-IAS15 | 3.40E+005 | 0.00 | yes | EDGE |
| Moon orbit 100 km x100 orbits | 0.01 m | DOPRI5-adaptive | 7,675 | 3620481.20 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | DOPRI5-fixed | 2,401 | 1912854320.38 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | PEFRL | 1,600 | 3856585.65 | no | OK |
| Moon orbit 100 km x100 orbits | 0.01 m | RK4 | 1,600 | 2476945412.77 | no | OK |
| Moon orbit 100 km x1000 orbits | 1 m | WH-BM14A | 29,696 | 52090.04 | no | OK |
| Moon orbit 100 km x1000 orbits | 1 m | RKN12(10) | 33,243 | 3697542.84 | no | OK |
| Moon orbit 100 km x1000 orbits | 1 m | GJ8 | 67,701 | 1637.75 | no | OK |
| Moon orbit 100 km x1000 orbits | 1 m | WH-Y6 | 28,673 | 627786.28 | no | OK |
| Moon orbit 100 km x1000 orbits | 1 m | IAS15 | 1.96E+005 | 1066.57 | no | OK |
| Moon orbit 100 km x1000 orbits | 1 m | DOP853-adaptive | 52,621 | 3653321.00 | no | OK |
| Moon orbit 100 km x1000 orbits | 1 m | SRKN14A | 57,344 | 609552.17 | no | OK |
| Moon orbit 100 km x1000 orbits | 1 m | Y6 | 28,673 | 4191722.07 | no | OK |
| Moon orbit 100 km x1000 orbits | 1 m | Y6C | 28,673 | 4191722.07 | no | OK |
| Moon hop 50 m/s | 1 m | Leapfrog | 9 | 0.04 | yes | EDGE |
| Moon hop 50 m/s | 1 m | RK4 | 32 | 0.00 | yes | EDGE |
| Moon hop 50 m/s | 1 m | PEFRL | 32 | 0.00 | yes | EDGE |
| Moon hop 50 m/s | 1 m | GJ8 | 246 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 1 m | Leapfrog | 9 | 10.73 | no | OK |
| Moon hop 200 m/s | 1 m | RK4 | 32 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 1 m | PEFRL | 32 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 1 m | DOPRI5-fixed | 49 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 1 m | Y6 | 57 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 1 m | Y6C | 57 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 1 m | DOPRI5-adaptive | 73 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 1 m | GJ8 | 304 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 0.01 m | RK4 | 32 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 0.01 m | PEFRL | 32 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 0.01 m | DOPRI5-fixed | 49 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 0.01 m | Y6 | 57 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 0.01 m | Y6C | 57 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 0.01 m | DOPRI5-adaptive | 73 | 0.00 | yes | EDGE |
| Moon hop 200 m/s | 0.01 m | Leapfrog | 9 | 10.73 | no | OK |
| Moon hop 200 m/s | 0.01 m | GJ8 | 304 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 1 m | RK4 | 32 | 0.76 | yes | EDGE |
| Moon hop 500 m/s | 1 m | PEFRL | 32 | 0.24 | yes | EDGE |
| Moon hop 500 m/s | 1 m | DOPRI5-fixed | 49 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 1 m | Y6 | 57 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 1 m | Y6C | 57 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 1 m | DOPRI5-adaptive | 73 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 1 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 1 m | DOP853-adaptive | 145 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 1 m | Leapfrog | 9 | 516.00 | no | OK |
| Moon hop 500 m/s | 1 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 1 m | GJ8 | 330 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | DOPRI5-fixed | 49 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | Y6 | 57 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | Y6C | 57 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | PEFRL | 32 | 0.24 | no | OK |
| Moon hop 500 m/s | 0.01 m | DOPRI5-adaptive | 73 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | RK4 | 32 | 0.76 | no | OK |
| Moon hop 500 m/s | 0.01 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | DOP853-adaptive | 145 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | GJ8 | 330 | 0.00 | yes | EDGE |
| Moon hop 500 m/s | 0.01 m | Leapfrog | 9 | 516.00 | no | OK |
| Moon hop 1000 m/s | 1 m | DOPRI5-adaptive | 73 | 1.84 | no | OK |
| Moon hop 1000 m/s | 1 m | DOPRI5-fixed | 49 | 1.85 | no | OK |
| Moon hop 1000 m/s | 1 m | PEFRL | 32 | 94.32 | no | OK |
| Moon hop 1000 m/s | 1 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 1000 m/s | 1 m | Y6 | 57 | 25.09 | no | OK |
| Moon hop 1000 m/s | 1 m | Y6C | 57 | 25.09 | no | OK |
| Moon hop 1000 m/s | 1 m | WH-Y6 | 113 | 0.00 | yes | EDGE |
| Moon hop 1000 m/s | 1 m | RK4 | 32 | 170.91 | no | OK |
| Moon hop 1000 m/s | 1 m | DOP853-adaptive | 145 | 0.01 | yes | EDGE |
| Moon hop 1000 m/s | 1 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 1000 m/s | 1 m | GJ8 | 367 | 0.04 | yes | EDGE |
| Moon hop 1000 m/s | 1 m | Leapfrog | 17 | 4531.23 | no | OK |
| Moon hop 1000 m/s | 0.01 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 1000 m/s | 0.01 m | WH-Y6 | 113 | 0.00 | yes | EDGE |
| Moon hop 1000 m/s | 0.01 m | DOPRI5-fixed | 49 | 1.85 | no | OK |
| Moon hop 1000 m/s | 0.01 m | DOP853-adaptive | 145 | 0.01 | no | OK |
| Moon hop 1000 m/s | 0.01 m | DOPRI5-adaptive | 73 | 1.84 | no | OK |
| Moon hop 1000 m/s | 0.01 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 1000 m/s | 0.01 m | Y6C | 57 | 25.09 | no | OK |
| Moon hop 1000 m/s | 0.01 m | Y6 | 57 | 25.09 | no | OK |
| Moon hop 1000 m/s | 0.01 m | PEFRL | 32 | 94.32 | no | OK |
| Moon hop 1000 m/s | 0.01 m | GJ8 | 367 | 0.04 | no | OK |
| Moon hop 1000 m/s | 0.01 m | RK4 | 32 | 170.91 | no | OK |
| Moon hop 1000 m/s | 0.01 m | Leapfrog | 17 | 4531.23 | no | OK |
| Moon hop 1200 m/s | 1 m | DOPRI5-fixed | 49 | 2.81 | no | OK |
| Moon hop 1200 m/s | 1 m | RK4 | 32 | 137.04 | no | OK |
| Moon hop 1200 m/s | 1 m | SRKN14A | 112 | 0.02 | yes | EDGE |
| Moon hop 1200 m/s | 1 m | WH-Y6 | 113 | 0.00 | yes | EDGE |
| Moon hop 1200 m/s | 1 m | Y6 | 57 | 86.05 | no | OK |
| Moon hop 1200 m/s | 1 m | Y6C | 57 | 86.05 | no | OK |
| Moon hop 1200 m/s | 1 m | PEFRL | 32 | 222.11 | no | OK |
| Moon hop 1200 m/s | 1 m | DOPRI5-adaptive | 73 | 2.61 | no | OK |
| Moon hop 1200 m/s | 1 m | DOP853-adaptive | 145 | 0.02 | yes | EDGE |
| Moon hop 1200 m/s | 1 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 1200 m/s | 1 m | GJ8 | 372 | 1.64 | no | OK |
| Moon hop 1200 m/s | 1 m | Leapfrog | 17 | 8672.28 | no | OK |
| Moon hop 1200 m/s | 0.01 m | WH-Y6 | 113 | 0.00 | yes | EDGE |
| Moon hop 1200 m/s | 0.01 m | DOP853-adaptive | 145 | 0.02 | no | OK |
| Moon hop 1200 m/s | 0.01 m | DOPRI5-fixed | 49 | 2.81 | no | OK |
| Moon hop 1200 m/s | 0.01 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 1200 m/s | 0.01 m | DOPRI5-adaptive | 73 | 2.61 | no | OK |
| Moon hop 1200 m/s | 0.01 m | SRKN14A | 112 | 0.02 | no | OK |
| Moon hop 1200 m/s | 0.01 m | Y6C | 57 | 86.05 | no | OK |
| Moon hop 1200 m/s | 0.01 m | Y6 | 57 | 86.05 | no | OK |
| Moon hop 1200 m/s | 0.01 m | RK4 | 32 | 137.04 | no | OK |
| Moon hop 1200 m/s | 0.01 m | GJ8 | 372 | 1.64 | no | OK |
| Moon hop 1200 m/s | 0.01 m | PEFRL | 32 | 222.11 | no | OK |
| Moon hop 1200 m/s | 0.01 m | Leapfrog | 17 | 8672.28 | no | OK |
| Moon hop 1400 m/s | 1 m | DOPRI5-fixed | 49 | 10.18 | no | OK |
| Moon hop 1400 m/s | 1 m | RK4 | 32 | 81.14 | no | OK |
| Moon hop 1400 m/s | 1 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 1 m | Y6 | 57 | 46.00 | no | OK |
| Moon hop 1400 m/s | 1 m | Y6C | 57 | 46.00 | no | OK |
| Moon hop 1400 m/s | 1 m | WH-Y6 | 113 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 1 m | PEFRL | 32 | 184.49 | no | OK |
| Moon hop 1400 m/s | 1 m | DOPRI5-adaptive | 73 | 9.64 | no | OK |
| Moon hop 1400 m/s | 1 m | DOP853-adaptive | 145 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 1 m | GJ8 | 379 | 0.15 | yes | EDGE |
| Moon hop 1400 m/s | 1 m | IAS15 | 475 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 1 m | Leapfrog | 17 | 10066.11 | no | OK |
| Moon hop 1400 m/s | 0.01 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 0.01 m | WH-Y6 | 113 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 0.01 m | DOP853-adaptive | 145 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 0.01 m | DOPRI5-fixed | 49 | 10.18 | no | OK |
| Moon hop 1400 m/s | 0.01 m | DOPRI5-adaptive | 73 | 9.64 | no | OK |
| Moon hop 1400 m/s | 0.01 m | Y6C | 57 | 46.00 | no | OK |
| Moon hop 1400 m/s | 0.01 m | Y6 | 57 | 46.00 | no | OK |
| Moon hop 1400 m/s | 0.01 m | RK4 | 32 | 81.14 | no | OK |
| Moon hop 1400 m/s | 0.01 m | PEFRL | 32 | 184.49 | no | OK |
| Moon hop 1400 m/s | 0.01 m | GJ8 | 379 | 0.15 | no | OK |
| Moon hop 1400 m/s | 0.01 m | IAS15 | 475 | 0.00 | yes | EDGE |
| Moon hop 1400 m/s | 0.01 m | Leapfrog | 17 | 10066.11 | no | OK |
| Moon hop 1500 m/s | 1 m | DOPRI5-fixed | 49 | 12.63 | no | OK |
| Moon hop 1500 m/s | 1 m | RK4 | 32 | 125.82 | no | OK |
| Moon hop 1500 m/s | 1 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 1 m | PEFRL | 32 | 152.92 | no | OK |
| Moon hop 1500 m/s | 1 m | Y6 | 57 | 27.82 | no | OK |
| Moon hop 1500 m/s | 1 m | Y6C | 57 | 27.82 | no | OK |
| Moon hop 1500 m/s | 1 m | WH-Y6 | 113 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 1 m | DOPRI5-adaptive | 73 | 11.37 | no | OK |
| Moon hop 1500 m/s | 1 m | DOP853-adaptive | 145 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 1 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 1 m | GJ8 | 379 | 0.02 | yes | EDGE |
| Moon hop 1500 m/s | 1 m | Leapfrog | 17 | 10343.29 | no | OK |
| Moon hop 1500 m/s | 0.01 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 0.01 m | WH-Y6 | 113 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 0.01 m | DOP853-adaptive | 145 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 0.01 m | DOPRI5-fixed | 49 | 12.63 | no | OK |
| Moon hop 1500 m/s | 0.01 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 1500 m/s | 0.01 m | Y6 | 57 | 27.82 | no | OK |
| Moon hop 1500 m/s | 0.01 m | Y6C | 57 | 27.82 | no | OK |
| Moon hop 1500 m/s | 0.01 m | DOPRI5-adaptive | 73 | 11.37 | no | OK |
| Moon hop 1500 m/s | 0.01 m | RK4 | 32 | 125.82 | no | OK |
| Moon hop 1500 m/s | 0.01 m | PEFRL | 32 | 152.92 | no | OK |
| Moon hop 1500 m/s | 0.01 m | GJ8 | 379 | 0.02 | no | OK |
| Moon hop 1500 m/s | 0.01 m | Leapfrog | 17 | 10343.29 | no | OK |
| Moon hop 1600 m/s | 1 m | DOPRI5-fixed | 49 | 15.32 | no | OK |
| Moon hop 1600 m/s | 1 m | PEFRL | 32 | 140.70 | no | OK |
| Moon hop 1600 m/s | 1 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 1 m | RK4 | 32 | 188.49 | no | OK |
| Moon hop 1600 m/s | 1 m | Y6 | 57 | 28.72 | no | OK |
| Moon hop 1600 m/s | 1 m | Y6C | 57 | 28.72 | no | OK |
| Moon hop 1600 m/s | 1 m | WH-Y6 | 113 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 1 m | DOPRI5-adaptive | 73 | 12.80 | no | OK |
| Moon hop 1600 m/s | 1 m | DOP853-adaptive | 145 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 1 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 1 m | GJ8 | 379 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 1 m | Leapfrog | 17 | 10754.43 | no | OK |
| Moon hop 1600 m/s | 0.01 m | SRKN14A | 112 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 0.01 m | WH-Y6 | 113 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 0.01 m | DOP853-adaptive | 145 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 0.01 m | DOPRI5-fixed | 49 | 15.32 | no | OK |
| Moon hop 1600 m/s | 0.01 m | RKN12(10) | 205 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 0.01 m | Y6 | 57 | 28.72 | no | OK |
| Moon hop 1600 m/s | 0.01 m | Y6C | 57 | 28.72 | no | OK |
| Moon hop 1600 m/s | 0.01 m | DOPRI5-adaptive | 73 | 12.80 | no | OK |
| Moon hop 1600 m/s | 0.01 m | PEFRL | 32 | 140.70 | no | OK |
| Moon hop 1600 m/s | 0.01 m | RK4 | 32 | 188.49 | no | OK |
| Moon hop 1600 m/s | 0.01 m | GJ8 | 379 | 0.00 | yes | EDGE |
| Moon hop 1600 m/s | 0.01 m | Leapfrog | 17 | 10754.43 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | DOP853-adaptive | 313 | 2058.61 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | RKN12(10) | 426 | 8.22 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | DOPRI5-adaptive | 163 | 123641.24 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | IAS15 | 956 | 0.21 | yes | EDGE |
| Moon flyby vinf 50 alt 1 | 1 m | WH-BM14A | 580 | 6.76 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | WH-Y6 | 281 | 1207.86 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | GJ8 | 1,623 | 14826.35 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | SRKN14A | 1,120 | 1188.42 | no | OK |
| Moon flyby vinf 50 alt 1 | 1 m | Encke-IAS15 | 6,588 | 0.00 | yes | EDGE |
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
| Moon flyby vinf 50 alt 1 | 0.01 m | WH-BM14A | 580 | 6.76 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | WH-Y6 | 281 | 1207.86 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | GJ8 | 1,623 | 14826.35 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | Encke-IAS15 | 6,588 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 1 | 0.01 m | SRKN14A | 1,120 | 1188.42 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | DOPRI5-fixed | 481 | 11862213.85 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | Y6 | 561 | 11252965.69 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | Y6C | 561 | 11252965.69 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | PEFRL | 320 | 4184708.13 | no | OK |
| Moon flyby vinf 50 alt 1 | 0.01 m | RK4 | 320 | 17894618.18 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | DOP853-adaptive | 313 | 2069.26 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | RKN12(10) | 426 | 8.26 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | DOPRI5-adaptive | 163 | 124280.53 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | IAS15 | 949 | 0.21 | yes | EDGE |
| Moon flyby vinf 50 alt 10 | 1 m | WH-BM14A | 580 | 6.90 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | WH-Y6 | 281 | 1233.18 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | GJ8 | 1,616 | 14902.81 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | SRKN14A | 1,120 | 1194.73 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | Encke-IAS15 | 6,644 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 10 | 1 m | DOPRI5-fixed | 481 | 11923304.92 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | Y6 | 561 | 11312765.55 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | Y6C | 561 | 11312765.55 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | PEFRL | 320 | 4206314.06 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | RK4 | 320 | 17986638.40 | no | OK |
| Moon flyby vinf 50 alt 10 | 1 m | Leapfrog | 81 | 78756728.18 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | RKN12(10) | 426 | 8.26 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | DOP853-adaptive | 313 | 2069.26 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | IAS15 | 949 | 0.21 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | DOPRI5-adaptive | 163 | 124280.53 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | WH-BM14A | 580 | 6.90 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | WH-Y6 | 281 | 1233.18 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | GJ8 | 1,616 | 14902.81 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | Encke-IAS15 | 6,644 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 10 | 0.01 m | SRKN14A | 1,120 | 1194.73 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | DOPRI5-fixed | 481 | 11923304.92 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | Y6 | 561 | 11312765.55 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | Y6C | 561 | 11312765.55 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | PEFRL | 320 | 4206314.06 | no | OK |
| Moon flyby vinf 50 alt 10 | 0.01 m | RK4 | 320 | 17986638.40 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | DOP853-adaptive | 313 | 2175.77 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | RKN12(10) | 426 | 8.71 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | DOPRI5-adaptive | 163 | 130670.91 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | IAS15 | 970 | 0.22 | yes | EDGE |
| Moon flyby vinf 50 alt 100 | 1 m | WH-BM14A | 580 | 8.44 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | WH-Y6 | 281 | 1508.82 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | GJ8 | 1,623 | 15667.25 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | SRKN14A | 1,120 | 1257.95 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | DOPRI5-fixed | 481 | 12534184.26 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | Y6C | 561 | 11912960.54 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | Y6 | 561 | 11912960.54 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | Encke-IAS15 | 6,630 | 0.00 | yes | OK |
| Moon flyby vinf 50 alt 100 | 1 m | PEFRL | 320 | 4422407.29 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | RK4 | 320 | 18906588.91 | no | OK |
| Moon flyby vinf 50 alt 100 | 1 m | Leapfrog | 81 | 82923327.69 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | RKN12(10) | 426 | 8.71 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | DOP853-adaptive | 313 | 2175.77 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | IAS15 | 970 | 0.22 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | DOPRI5-adaptive | 163 | 130670.91 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | WH-BM14A | 580 | 8.44 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | WH-Y6 | 281 | 1508.82 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | GJ8 | 1,623 | 15667.25 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | SRKN14A | 1,120 | 1257.95 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | Encke-IAS15 | 6,630 | 0.00 | yes | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | DOPRI5-fixed | 481 | 12534184.26 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | Y6C | 561 | 11912960.54 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | Y6 | 561 | 11912960.54 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | PEFRL | 320 | 4422407.29 | no | OK |
| Moon flyby vinf 50 alt 100 | 0.01 m | RK4 | 320 | 18906588.91 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | DOP853-adaptive | 313 | 3256.46 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | RKN12(10) | 426 | 13.59 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | DOPRI5-adaptive | 163 | 193855.30 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | IAS15 | 949 | 0.33 | yes | EDGE |
| Moon flyby vinf 50 alt 1000 | 1 m | WH-BM14A | 580 | 41.72 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | WH-Y6 | 561 | 1960.60 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | GJ8 | 1,623 | 23359.74 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | SRKN14A | 1,120 | 1911.28 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | Encke-IAS15 | 6,490 | 0.00 | yes | EDGE |
| Moon flyby vinf 50 alt 1000 | 1 m | DOPRI5-fixed | 481 | 18690214.81 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | Y6 | 561 | 18309733.13 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | Y6C | 561 | 18309733.13 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | PEFRL | 320 | 6607177.50 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | RK4 | 320 | 28140076.77 | no | OK |
| Moon flyby vinf 50 alt 1000 | 1 m | Leapfrog | 81 | 127419606.15 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | RKN12(10) | 426 | 13.59 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | DOP853-adaptive | 313 | 3256.46 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | IAS15 | 949 | 0.33 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | DOPRI5-adaptive | 163 | 193855.30 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | WH-BM14A | 580 | 41.72 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | WH-Y6 | 561 | 1960.60 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | GJ8 | 1,623 | 23359.74 | no | OK |
| Moon flyby vinf 50 alt 1000 | 0.01 m | Encke-IAS15 | 6,490 | 0.00 | yes | EDGE |
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
| Moon flyby vinf 200 alt 1 | 1 m | WH-BM14A | 580 | 6.38 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | WH-Y6 | 281 | 1142.91 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | GJ8 | 1,616 | 14403.74 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | SRKN14A | 1,120 | 1208.56 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | Encke-IAS15 | 6,560 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 1 | 1 m | DOPRI5-fixed | 481 | 11076431.71 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | Y6 | 561 | 10925668.22 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | Y6C | 561 | 10925668.22 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | PEFRL | 320 | 4006272.60 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | RK4 | 320 | 16762943.04 | no | OK |
| Moon flyby vinf 200 alt 1 | 1 m | Leapfrog | 81 | 76669400.54 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | RKN12(10) | 426 | 8.31 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | DOP853-adaptive | 313 | 1971.00 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | IAS15 | 963 | 0.21 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | DOPRI5-adaptive | 163 | 120767.47 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | WH-BM14A | 580 | 6.38 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | WH-Y6 | 281 | 1142.91 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | GJ8 | 1,616 | 14403.74 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | Encke-IAS15 | 6,560 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 1 | 0.01 m | SRKN14A | 1,120 | 1208.56 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | DOPRI5-fixed | 481 | 11076431.71 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | Y6C | 561 | 10925668.22 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | Y6 | 561 | 10925668.22 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | PEFRL | 320 | 4006272.60 | no | OK |
| Moon flyby vinf 200 alt 1 | 0.01 m | RK4 | 320 | 16762943.04 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | DOP853-adaptive | 313 | 1980.77 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | RKN12(10) | 426 | 8.36 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | DOPRI5-adaptive | 163 | 121378.09 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | IAS15 | 956 | 0.21 | yes | EDGE |
| Moon flyby vinf 200 alt 10 | 1 m | WH-BM14A | 580 | 6.51 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | WH-Y6 | 281 | 1166.54 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | GJ8 | 1,623 | 14475.94 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | SRKN14A | 1,120 | 1215.09 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | Encke-IAS15 | 6,546 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 10 | 1 m | DOPRI5-fixed | 481 | 11129680.93 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | Y6C | 561 | 10982204.16 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | Y6 | 561 | 10982204.16 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | PEFRL | 320 | 4026076.79 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | RK4 | 320 | 16843678.77 | no | OK |
| Moon flyby vinf 200 alt 10 | 1 m | Leapfrog | 81 | 77067609.73 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | RKN12(10) | 426 | 8.36 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | DOP853-adaptive | 313 | 1980.77 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | IAS15 | 956 | 0.21 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | DOPRI5-adaptive | 163 | 121378.09 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | WH-BM14A | 580 | 6.51 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | WH-Y6 | 281 | 1166.54 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | GJ8 | 1,623 | 14475.94 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | Encke-IAS15 | 6,546 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 10 | 0.01 m | SRKN14A | 1,120 | 1215.09 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | DOPRI5-fixed | 481 | 11129680.93 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | Y6 | 561 | 10982204.16 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | Y6C | 561 | 10982204.16 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | PEFRL | 320 | 4026076.79 | no | OK |
| Moon flyby vinf 200 alt 10 | 0.01 m | RK4 | 320 | 16843678.77 | no | OK |
| Moon flyby vinf 200 alt 100 | 1 m | DOP853-adaptive | 313 | 2078.20 | no | OK |
| Moon flyby vinf 200 alt 100 | 1 m | RKN12(10) | 426 | 8.81 | no | OK |
| Moon flyby vinf 200 alt 100 | 1 m | DOPRI5-adaptive | 163 | 127474.83 | no | OK |
| Moon flyby vinf 200 alt 100 | 1 m | IAS15 | 1,000 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 100 | 1 m | WH-BM14A | 580 | 7.94 | no | OK |
| Moon flyby vinf 200 alt 100 | 0.01 m | RKN12(10) | 426 | 8.81 | no | OK |
| Moon flyby vinf 200 alt 100 | 0.01 m | DOP853-adaptive | 313 | 2078.20 | no | OK |
| Moon flyby vinf 200 alt 100 | 0.01 m | IAS15 | 1,000 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 100 | 0.01 m | DOPRI5-adaptive | 163 | 127474.83 | no | OK |
| Moon flyby vinf 200 alt 100 | 0.01 m | WH-BM14A | 580 | 7.94 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | DOP853-adaptive | 313 | 3045.53 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | RKN12(10) | 426 | 13.85 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | DOPRI5-adaptive | 163 | 187130.13 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | IAS15 | 949 | 0.33 | yes | EDGE |
| Moon flyby vinf 200 alt 1000 | 1 m | WH-BM14A | 580 | 38.07 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | WH-Y6 | 561 | 1819.54 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | GJ8 | 1,616 | 22349.59 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | SRKN14A | 1,120 | 1968.98 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | Encke-IAS15 | 6,518 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 1000 | 1 m | DOPRI5-fixed | 481 | 16824636.26 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | Y6C | 561 | 17541132.99 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | Y6 | 561 | 17541132.99 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | PEFRL | 320 | 6177337.82 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | RK4 | 320 | 25449484.51 | no | OK |
| Moon flyby vinf 200 alt 1000 | 1 m | Leapfrog | 81 | 123369719.33 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | RKN12(10) | 426 | 13.85 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | DOP853-adaptive | 313 | 3045.53 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | IAS15 | 949 | 0.33 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | DOPRI5-adaptive | 163 | 187130.13 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | WH-BM14A | 580 | 38.07 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | WH-Y6 | 561 | 1819.54 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | GJ8 | 1,616 | 22349.59 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | Encke-IAS15 | 6,518 | 0.00 | yes | EDGE |
| Moon flyby vinf 200 alt 1000 | 0.01 m | SRKN14A | 1,120 | 1968.98 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | DOPRI5-fixed | 481 | 16824636.26 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | Y6C | 561 | 17541132.99 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | Y6 | 561 | 17541132.99 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | PEFRL | 320 | 6177337.82 | no | OK |
| Moon flyby vinf 200 alt 1000 | 0.01 m | RK4 | 320 | 25449484.51 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | DOP853-adaptive | 313 | 1587.44 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | RKN12(10) | 426 | 8.58 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | DOPRI5-adaptive | 163 | 108846.00 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | IAS15 | 949 | 0.22 | yes | EDGE |
| Moon flyby vinf 500 alt 1 | 1 m | WH-BM14A | 580 | 4.82 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | WH-Y6 | 281 | 880.23 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | GJ8 | 1,616 | 12594.49 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | SRKN14A | 1,120 | 1323.17 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | Encke-IAS15 | 6,432 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 1 | 1 m | DOPRI5-fixed | 481 | 7988595.41 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | Y6 | 561 | 9450612.64 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | Y6C | 561 | 9450612.64 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | PEFRL | 320 | 3233141.32 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | RK4 | 320 | 12429753.91 | no | OK |
| Moon flyby vinf 500 alt 1 | 1 m | Leapfrog | 81 | 68561567.87 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | RKN12(10) | 426 | 8.58 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | DOP853-adaptive | 313 | 1587.44 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | DOPRI5-adaptive | 163 | 108846.00 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | IAS15 | 949 | 0.22 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | WH-BM14A | 580 | 4.82 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | WH-Y6 | 281 | 880.23 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | GJ8 | 1,616 | 12594.49 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | Encke-IAS15 | 6,432 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 1 | 0.01 m | SRKN14A | 1,120 | 1323.17 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | DOPRI5-fixed | 481 | 7988595.41 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | Y6 | 561 | 9450612.64 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | Y6C | 561 | 9450612.64 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | PEFRL | 320 | 3233141.32 | no | OK |
| Moon flyby vinf 500 alt 1 | 0.01 m | RK4 | 320 | 12429753.91 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | DOP853-adaptive | 313 | 1593.72 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | RKN12(10) | 426 | 8.62 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | DOPRI5-adaptive | 163 | 109349.65 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | IAS15 | 956 | 0.22 | yes | EDGE |
| Moon flyby vinf 500 alt 10 | 1 m | WH-BM14A | 580 | 4.91 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | WH-Y6 | 281 | 897.41 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | GJ8 | 1,602 | 12650.51 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | SRKN14A | 1,120 | 1330.88 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | Encke-IAS15 | 6,446 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 10 | 1 m | DOPRI5-fixed | 481 | 8015662.49 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | Y6 | 561 | 9493694.75 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | Y6C | 561 | 9493694.75 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | PEFRL | 320 | 3245995.76 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | RK4 | 320 | 12474051.44 | no | OK |
| Moon flyby vinf 500 alt 10 | 1 m | Leapfrog | 81 | 68882402.16 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | RKN12(10) | 426 | 8.62 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | DOP853-adaptive | 313 | 1593.72 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | DOPRI5-adaptive | 163 | 109349.65 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | IAS15 | 956 | 0.22 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | WH-BM14A | 580 | 4.91 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | WH-Y6 | 281 | 897.41 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | GJ8 | 1,602 | 12650.51 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | Encke-IAS15 | 6,446 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 10 | 0.01 m | SRKN14A | 1,120 | 1330.88 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | DOPRI5-fixed | 481 | 8015662.49 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | Y6C | 561 | 9493694.75 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | Y6 | 561 | 9493694.75 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | PEFRL | 320 | 3245995.76 | no | OK |
| Moon flyby vinf 500 alt 10 | 0.01 m | RK4 | 320 | 12474051.44 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | DOP853-adaptive | 313 | 1655.79 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | RKN12(10) | 426 | 9.10 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | DOPRI5-adaptive | 163 | 114357.55 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | IAS15 | 935 | 0.24 | yes | EDGE |
| Moon flyby vinf 500 alt 100 | 1 m | WH-BM14A | 580 | 5.92 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | WH-Y6 | 281 | 1082.64 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | GJ8 | 1,602 | 13206.73 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | SRKN14A | 1,120 | 1408.50 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | Encke-IAS15 | 6,348 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 100 | 1 m | DOPRI5-fixed | 481 | 8280928.77 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | Y6 | 561 | 9923470.97 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | Y6C | 561 | 9923470.97 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | PEFRL | 320 | 3372883.04 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | RK4 | 320 | 12909254.87 | no | OK |
| Moon flyby vinf 500 alt 100 | 1 m | Leapfrog | 81 | 72083644.01 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | RKN12(10) | 426 | 9.10 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | DOP853-adaptive | 313 | 1655.79 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | DOPRI5-adaptive | 163 | 114357.55 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | IAS15 | 935 | 0.24 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | WH-BM14A | 580 | 5.92 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | WH-Y6 | 281 | 1082.64 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | GJ8 | 1,602 | 13206.73 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | Encke-IAS15 | 6,348 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 100 | 0.01 m | SRKN14A | 1,120 | 1408.50 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | DOPRI5-fixed | 481 | 8280928.77 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | Y6 | 561 | 9923470.97 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | Y6C | 561 | 9923470.97 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | PEFRL | 320 | 3372883.04 | no | OK |
| Moon flyby vinf 500 alt 100 | 0.01 m | RK4 | 320 | 12909254.87 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | DOP853-adaptive | 313 | 2210.34 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | RKN12(10) | 426 | 14.27 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | DOPRI5-adaptive | 163 | 161559.33 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | IAS15 | 928 | 0.39 | yes | EDGE |
| Moon flyby vinf 500 alt 1000 | 1 m | WH-BM14A | 580 | 25.29 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | WH-Y6 | 561 | 1295.60 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | GJ8 | 1,616 | 18466.73 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | SRKN14A | 1,120 | 2251.90 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | Encke-IAS15 | 6,348 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 1000 | 1 m | DOPRI5-fixed | 481 | 10520125.67 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | Y6C | 561 | 14325744.94 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | Y6 | 561 | 14325744.94 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | PEFRL | 320 | 4506388.55 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | RK4 | 320 | 16636375.30 | no | OK |
| Moon flyby vinf 500 alt 1000 | 1 m | Leapfrog | 81 | 104888476.23 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | RKN12(10) | 426 | 14.27 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | DOP853-adaptive | 313 | 2210.34 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | IAS15 | 928 | 0.39 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | DOPRI5-adaptive | 163 | 161559.33 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | WH-BM14A | 580 | 25.29 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | WH-Y6 | 561 | 1295.60 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | GJ8 | 1,616 | 18466.73 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | Encke-IAS15 | 6,348 | 0.00 | yes | EDGE |
| Moon flyby vinf 500 alt 1000 | 0.01 m | SRKN14A | 1,120 | 2251.90 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | DOPRI5-fixed | 481 | 10520125.67 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | Y6 | 561 | 14325744.94 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | Y6C | 561 | 14325744.94 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | PEFRL | 320 | 4506388.55 | no | OK |
| Moon flyby vinf 500 alt 1000 | 0.01 m | RK4 | 320 | 16636375.30 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | DOP853-adaptive | 325 | 873.75 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | DOPRI5-adaptive | 163 | 81952.39 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | RKN12(10) | 426 | 7.75 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | IAS15 | 900 | 0.29 | yes | EDGE |
| Moon flyby vinf 1000 alt 1 | 1 m | WH-BM14A | 580 | 2.54 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | WH-Y6 | 281 | 492.77 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | GJ8 | 1,609 | 9167.23 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | SRKN14A | 1,120 | 1517.94 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | DOPRI5-fixed | 481 | 3531410.40 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | Y6C | 561 | 6342090.65 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | Y6 | 561 | 6342090.65 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | Encke-IAS15 | 6,250 | 0.00 | yes | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | PEFRL | 320 | 1881719.12 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | RK4 | 320 | 6250977.77 | no | OK |
| Moon flyby vinf 1000 alt 1 | 1 m | Leapfrog | 81 | 49776193.51 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | RKN12(10) | 426 | 7.75 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | DOP853-adaptive | 325 | 873.75 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | DOPRI5-adaptive | 163 | 81952.39 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | IAS15 | 900 | 0.29 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | WH-BM14A | 580 | 2.54 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | WH-Y6 | 281 | 492.77 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | GJ8 | 1,609 | 9167.23 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | SRKN14A | 1,120 | 1517.94 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | Encke-IAS15 | 6,250 | 0.00 | yes | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | DOPRI5-fixed | 481 | 3531410.40 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | Y6 | 561 | 6342090.65 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | Y6C | 561 | 6342090.65 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | PEFRL | 320 | 1881719.12 | no | OK |
| Moon flyby vinf 1000 alt 1 | 0.01 m | RK4 | 320 | 6250977.77 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | DOP853-adaptive | 325 | 875.22 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | DOPRI5-adaptive | 163 | 82226.16 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | RKN12(10) | 426 | 7.78 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | IAS15 | 907 | 0.29 | yes | EDGE |
| Moon flyby vinf 1000 alt 10 | 1 m | WH-BM14A | 580 | 2.58 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | WH-Y6 | 281 | 501.59 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | GJ8 | 1,595 | 9198.51 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | SRKN14A | 1,120 | 1526.42 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | DOPRI5-fixed | 481 | 3533626.86 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | Encke-IAS15 | 6,250 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 10 | 1 m | Y6 | 561 | 6361153.57 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | Y6C | 561 | 6361153.57 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | PEFRL | 320 | 1885988.45 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | RK4 | 320 | 6260280.20 | no | OK |
| Moon flyby vinf 1000 alt 10 | 1 m | Leapfrog | 81 | 49947644.98 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | RKN12(10) | 426 | 7.78 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | DOP853-adaptive | 325 | 875.22 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | DOPRI5-adaptive | 163 | 82226.16 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | IAS15 | 907 | 0.29 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | WH-BM14A | 580 | 2.58 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | WH-Y6 | 281 | 501.59 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | GJ8 | 1,595 | 9198.51 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | Encke-IAS15 | 6,250 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 10 | 0.01 m | SRKN14A | 1,120 | 1526.42 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | DOPRI5-fixed | 481 | 3533626.86 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | Y6C | 561 | 6361153.57 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | Y6 | 561 | 6361153.57 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | PEFRL | 320 | 1885988.45 | no | OK |
| Moon flyby vinf 1000 alt 10 | 0.01 m | RK4 | 320 | 6260280.20 | no | OK |
| Moon flyby vinf 1000 alt 100 | 1 m | DOP853-adaptive | 313 | 830.23 | no | OK |
| Moon flyby vinf 1000 alt 100 | 1 m | DOPRI5-adaptive | 163 | 84899.39 | no | OK |
| Moon flyby vinf 1000 alt 100 | 1 m | RKN12(10) | 426 | 8.11 | no | OK |
| Moon flyby vinf 1000 alt 100 | 1 m | IAS15 | 951 | 0.01 | yes | EDGE |
| Moon flyby vinf 1000 alt 100 | 1 m | WH-BM14A | 580 | 3.06 | no | OK |
| Moon flyby vinf 1000 alt 100 | 0.01 m | RKN12(10) | 426 | 8.11 | no | OK |
| Moon flyby vinf 1000 alt 100 | 0.01 m | DOP853-adaptive | 313 | 830.23 | no | OK |
| Moon flyby vinf 1000 alt 100 | 0.01 m | DOPRI5-adaptive | 163 | 84899.39 | no | OK |
| Moon flyby vinf 1000 alt 100 | 0.01 m | IAS15 | 951 | 0.01 | yes | EDGE |
| Moon flyby vinf 1000 alt 100 | 0.01 m | WH-BM14A | 580 | 3.06 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | DOP853-adaptive | 325 | 981.93 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | DOPRI5-adaptive | 157 | 105173.29 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | RKN12(10) | 426 | 11.17 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | IAS15 | 879 | 0.51 | yes | EDGE |
| Moon flyby vinf 1000 alt 1000 | 1 m | WH-BM14A | 580 | 11.32 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | WH-Y6 | 281 | 2257.73 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | GJ8 | 1,924 | 1008.04 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | SRKN14A | 1,120 | 2457.03 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | DOPRI5-fixed | 481 | 3595196.76 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | Encke-IAS15 | 6,208 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 1000 | 1 m | Y6 | 561 | 8258382.78 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | Y6C | 561 | 8258382.78 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | PEFRL | 320 | 2252271.77 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | RK4 | 320 | 6886990.47 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 1 m | Leapfrog | 81 | 67816492.98 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | RKN12(10) | 426 | 11.17 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | DOP853-adaptive | 325 | 981.93 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | DOPRI5-adaptive | 157 | 105173.29 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | IAS15 | 879 | 0.51 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | WH-BM14A | 580 | 11.32 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | WH-Y6 | 281 | 2257.73 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | GJ8 | 1,924 | 1008.04 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | Encke-IAS15 | 6,208 | 0.00 | yes | EDGE |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | SRKN14A | 1,120 | 2457.03 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | DOPRI5-fixed | 481 | 3595196.76 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | Y6C | 561 | 8258382.78 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | Y6 | 561 | 8258382.78 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | PEFRL | 320 | 2252271.77 | no | OK |
| Moon flyby vinf 1000 alt 1000 | 0.01 m | RK4 | 320 | 6886990.47 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | DOPRI5-adaptive | 157 | 15876.96 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | DOP853-adaptive | 313 | 1165.33 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | RKN12(10) | 426 | 1.54 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | WH-BM14A | 580 | 0.38 | yes | EDGE |
| Moon flyby vinf 3000 alt 1 | 1 m | IAS15 | 816 | 0.21 | yes | EDGE |
| Moon flyby vinf 3000 alt 1 | 1 m | WH-Y6 | 281 | 80.98 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | SRKN14A | 840 | 5029.11 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | GJ8 | 1,582 | 3852.80 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | DOPRI5-fixed | 481 | 237171.16 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | Encke-IAS15 | 5,914 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 1 | 1 m | Y6C | 561 | 526008.56 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | Y6 | 561 | 526008.56 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | PEFRL | 320 | 913611.01 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | RK4 | 320 | 1236546.24 | no | OK |
| Moon flyby vinf 3000 alt 1 | 1 m | Leapfrog | 81 | 12406881.46 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | DOP853-adaptive | 313 | 1165.33 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | RKN12(10) | 426 | 1.54 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | DOPRI5-adaptive | 157 | 15876.96 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | IAS15 | 816 | 0.21 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | WH-BM14A | 580 | 0.38 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | WH-Y6 | 281 | 80.98 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | GJ8 | 1,582 | 3852.80 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | Encke-IAS15 | 5,914 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 1 | 0.01 m | SRKN14A | 840 | 5029.11 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | DOPRI5-fixed | 481 | 237171.16 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | Y6 | 561 | 526008.56 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | Y6C | 561 | 526008.56 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | PEFRL | 320 | 913611.01 | no | OK |
| Moon flyby vinf 3000 alt 1 | 0.01 m | RK4 | 320 | 1236546.24 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | DOPRI5-adaptive | 157 | 15887.79 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | DOP853-adaptive | 301 | 1165.79 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | RKN12(10) | 426 | 1.54 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | WH-BM14A | 580 | 0.39 | yes | EDGE |
| Moon flyby vinf 3000 alt 10 | 1 m | IAS15 | 830 | 0.21 | yes | EDGE |
| Moon flyby vinf 3000 alt 10 | 1 m | WH-Y6 | 281 | 82.14 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | SRKN14A | 840 | 5039.94 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | GJ8 | 1,589 | 3864.35 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | DOPRI5-fixed | 481 | 237080.47 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | Encke-IAS15 | 5,984 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 10 | 1 m | Y6C | 561 | 522686.54 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | Y6 | 561 | 522686.54 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | PEFRL | 320 | 916482.08 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | RK4 | 320 | 1238847.21 | no | OK |
| Moon flyby vinf 3000 alt 10 | 1 m | Leapfrog | 81 | 12420534.97 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | DOP853-adaptive | 301 | 1165.79 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | RKN12(10) | 426 | 1.54 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | DOPRI5-adaptive | 157 | 15887.79 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | IAS15 | 830 | 0.21 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | WH-BM14A | 580 | 0.39 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | WH-Y6 | 281 | 82.14 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | GJ8 | 1,589 | 3864.35 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | Encke-IAS15 | 5,984 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 10 | 0.01 m | SRKN14A | 840 | 5039.94 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | DOPRI5-fixed | 481 | 237080.47 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | Y6 | 561 | 522686.54 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | Y6C | 561 | 522686.54 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | PEFRL | 320 | 916482.08 | no | OK |
| Moon flyby vinf 3000 alt 10 | 0.01 m | RK4 | 320 | 1238847.21 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | DOPRI5-adaptive | 157 | 16002.16 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | DOP853-adaptive | 301 | 1216.26 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | RKN12(10) | 426 | 1.53 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | WH-BM14A | 580 | 0.44 | yes | EDGE |
| Moon flyby vinf 3000 alt 100 | 1 m | IAS15 | 823 | 0.21 | yes | EDGE |
| Moon flyby vinf 3000 alt 100 | 1 m | WH-Y6 | 281 | 94.23 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | SRKN14A | 840 | 5146.01 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | GJ8 | 1,597 | 3977.07 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | DOPRI5-fixed | 481 | 236399.69 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | Encke-IAS15 | 5,914 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 100 | 1 m | Y6C | 561 | 490053.26 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | Y6 | 561 | 490053.26 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | PEFRL | 320 | 944316.05 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | RK4 | 320 | 1261195.65 | no | OK |
| Moon flyby vinf 3000 alt 100 | 1 m | Leapfrog | 81 | 12553813.00 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | DOP853-adaptive | 301 | 1216.26 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | RKN12(10) | 426 | 1.53 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | DOPRI5-adaptive | 157 | 16002.16 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | IAS15 | 823 | 0.21 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | WH-BM14A | 580 | 0.44 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | WH-Y6 | 281 | 94.23 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | GJ8 | 1,597 | 3977.07 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | Encke-IAS15 | 5,914 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 100 | 0.01 m | SRKN14A | 840 | 5146.01 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | DOPRI5-fixed | 481 | 236399.69 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | Y6 | 561 | 490053.26 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | Y6C | 561 | 490053.26 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | PEFRL | 320 | 944316.05 | no | OK |
| Moon flyby vinf 3000 alt 100 | 0.01 m | RK4 | 320 | 1261195.65 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | DOPRI5-adaptive | 157 | 17524.87 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | DOP853-adaptive | 301 | 1727.68 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | RKN12(10) | 426 | 1.56 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | IAS15 | 844 | 0.26 | yes | EDGE |
| Moon flyby vinf 3000 alt 1000 | 1 m | WH-BM14A | 580 | 1.24 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | WH-Y6 | 281 | 264.76 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | SRKN14A | 1,120 | 1101.44 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | GJ8 | 1,627 | 4870.70 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | DOPRI5-fixed | 481 | 246566.10 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | Encke-IAS15 | 6,042 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 1000 | 1 m | Y6C | 561 | 220743.20 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | Y6 | 561 | 220743.20 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | PEFRL | 320 | 1159603.50 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | RK4 | 320 | 1428801.55 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 1 m | Leapfrog | 81 | 13646400.95 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | DOP853-adaptive | 301 | 1727.68 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | RKN12(10) | 426 | 1.56 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | DOPRI5-adaptive | 157 | 17524.87 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | IAS15 | 844 | 0.26 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | WH-BM14A | 580 | 1.24 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | WH-Y6 | 281 | 264.76 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | GJ8 | 1,627 | 4870.70 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | Encke-IAS15 | 6,042 | 0.00 | yes | EDGE |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | SRKN14A | 1,120 | 1101.44 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | DOPRI5-fixed | 481 | 246566.10 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | Y6 | 561 | 220743.20 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | Y6C | 561 | 220743.20 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | PEFRL | 320 | 1159603.50 | no | OK |
| Moon flyby vinf 3000 alt 1000 | 0.01 m | RK4 | 320 | 1428801.55 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | DOPRI5-adaptive | 151 | 8265.63 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | DOP853-adaptive | 301 | 244.65 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | RKN12(10) | 426 | 0.12 | yes | EDGE |
| Moon flyby vinf 10000 alt 1 | 1 m | WH-Y6 | 281 | 1.56 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | WH-BM14A | 580 | 0.01 | yes | EDGE |
| Moon flyby vinf 10000 alt 1 | 1 m | IAS15 | 760 | 0.03 | yes | EDGE |
| Moon flyby vinf 10000 alt 1 | 1 m | SRKN14A | 560 | 3653.53 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | DOPRI5-fixed | 481 | 23531.65 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | GJ8 | 1,339 | 3891.27 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | Y6C | 561 | 67895.64 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | Y6 | 561 | 67895.64 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | Encke-IAS15 | 5,312 | 0.00 | yes | EDGE |
| Moon flyby vinf 10000 alt 1 | 1 m | PEFRL | 320 | 155195.36 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | RK4 | 320 | 178614.20 | no | OK |
| Moon flyby vinf 10000 alt 1 | 1 m | Leapfrog | 81 | 1502718.96 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | DOP853-adaptive | 301 | 244.65 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | DOPRI5-adaptive | 151 | 8265.63 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | RKN12(10) | 426 | 0.12 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | WH-BM14A | 580 | 0.01 | yes | EDGE |
| Moon flyby vinf 10000 alt 1 | 0.01 m | IAS15 | 760 | 0.03 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | WH-Y6 | 281 | 1.56 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | GJ8 | 1,339 | 3891.27 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | SRKN14A | 560 | 3653.53 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | Encke-IAS15 | 5,312 | 0.00 | yes | EDGE |
| Moon flyby vinf 10000 alt 1 | 0.01 m | DOPRI5-fixed | 481 | 23531.65 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | Y6C | 561 | 67895.64 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | Y6 | 561 | 67895.64 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | PEFRL | 320 | 155195.36 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | RK4 | 320 | 178614.20 | no | OK |
| Moon flyby vinf 10000 alt 1 | 0.01 m | Leapfrog | 81 | 1502718.96 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | DOPRI5-adaptive | 151 | 8301.67 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | DOP853-adaptive | 301 | 245.57 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | RKN12(10) | 426 | 0.12 | yes | EDGE |
| Moon flyby vinf 10000 alt 10 | 1 m | WH-Y6 | 281 | 1.58 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | WH-BM14A | 580 | 0.01 | yes | EDGE |
| Moon flyby vinf 10000 alt 10 | 1 m | IAS15 | 725 | 0.03 | yes | EDGE |
| Moon flyby vinf 10000 alt 10 | 1 m | SRKN14A | 560 | 3654.97 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | DOPRI5-fixed | 481 | 23536.22 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | GJ8 | 1,339 | 3892.56 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | Y6 | 561 | 68009.73 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | Y6C | 561 | 68009.73 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | PEFRL | 320 | 155250.65 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | RK4 | 320 | 178669.24 | no | OK |
| Moon flyby vinf 10000 alt 10 | 1 m | Leapfrog | 81 | 1503105.91 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | DOP853-adaptive | 301 | 245.57 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | DOPRI5-adaptive | 151 | 8301.67 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | RKN12(10) | 426 | 0.12 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | WH-BM14A | 580 | 0.01 | yes | EDGE |
| Moon flyby vinf 10000 alt 10 | 0.01 m | IAS15 | 725 | 0.03 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | WH-Y6 | 281 | 1.58 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | GJ8 | 1,339 | 3892.56 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | SRKN14A | 560 | 3654.97 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | DOPRI5-fixed | 481 | 23536.22 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | Y6 | 561 | 68009.73 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | Y6C | 561 | 68009.73 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | PEFRL | 320 | 155250.65 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | RK4 | 320 | 178669.24 | no | OK |
| Moon flyby vinf 10000 alt 10 | 0.01 m | Leapfrog | 81 | 1503105.91 | no | OK |
| Moon flyby vinf 10000 alt 100 | 1 m | DOPRI5-adaptive | 151 | 8659.03 | no | OK |
| Moon flyby vinf 10000 alt 100 | 1 m | DOP853-adaptive | 301 | 254.73 | no | OK |
| Moon flyby vinf 10000 alt 100 | 1 m | RKN12(10) | 426 | 0.12 | yes | EDGE |
| Moon flyby vinf 10000 alt 100 | 1 m | WH-BM14A | 580 | 0.01 | yes | EDGE |
| Moon flyby vinf 10000 alt 100 | 1 m | IAS15 | 805 | 0.00 | yes | EDGE |
| Moon flyby vinf 10000 alt 100 | 0.01 m | DOP853-adaptive | 301 | 254.73 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | RKN12(10) | 426 | 0.12 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | DOPRI5-adaptive | 151 | 8659.03 | no | OK |
| Moon flyby vinf 10000 alt 100 | 0.01 m | WH-BM14A | 580 | 0.01 | yes | EDGE |
| Moon flyby vinf 10000 alt 100 | 0.01 m | IAS15 | 805 | 0.00 | yes | EDGE |
| Moon flyby vinf 10000 alt 1000 | 1 m | DOPRI5-adaptive | 151 | 11951.14 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | DOP853-adaptive | 301 | 340.66 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | RKN12(10) | 426 | 0.12 | yes | EDGE |
| Moon flyby vinf 10000 alt 1000 | 1 m | WH-Y6 | 281 | 4.01 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | WH-BM14A | 580 | 0.02 | yes | EDGE |
| Moon flyby vinf 10000 alt 1000 | 1 m | IAS15 | 746 | 0.03 | yes | EDGE |
| Moon flyby vinf 10000 alt 1000 | 1 m | SRKN14A | 560 | 3767.26 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | DOPRI5-fixed | 481 | 23935.15 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | GJ8 | 1,391 | 3985.34 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | Y6C | 561 | 76764.23 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | Y6 | 561 | 76764.23 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | PEFRL | 320 | 159709.44 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | Encke-IAS15 | 5,624 | 0.00 | yes | EDGE |
| Moon flyby vinf 10000 alt 1000 | 1 m | RK4 | 320 | 183096.42 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 1 m | Leapfrog | 81 | 1526689.99 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | DOP853-adaptive | 301 | 340.66 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | RKN12(10) | 426 | 0.12 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | DOPRI5-adaptive | 151 | 11951.14 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | IAS15 | 746 | 0.03 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | WH-BM14A | 580 | 0.02 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | WH-Y6 | 281 | 4.01 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | GJ8 | 1,391 | 3985.34 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | SRKN14A | 560 | 3767.26 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | Encke-IAS15 | 5,624 | 0.00 | yes | EDGE |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | DOPRI5-fixed | 481 | 23935.15 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | Y6 | 561 | 76764.23 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | Y6C | 561 | 76764.23 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | PEFRL | 320 | 159709.44 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | RK4 | 320 | 183096.42 | no | OK |
| Moon flyby vinf 10000 alt 1000 | 0.01 m | Leapfrog | 81 | 1526689.99 | no | OK |
| Heliocentric 0.3 AU e 0 | 1 m | RKN12(10) | 188 | 0.03 | yes | EDGE |
| Heliocentric 0.3 AU e 0 | 1 m | WH-Y6 | 225 | 0.16 | yes | EDGE |
| Heliocentric 0.3 AU e 0 | 1 m | WH-BM14A | 464 | 0.00 | yes | EDGE |
| Heliocentric 0.3 AU e 0 | 1 m | GJ8 | 459 | 1.86 | no | OK |
| Heliocentric 0.3 AU e 0 | 1 m | DOP853-adaptive | 133 | 166118.46 | no | OK |
| Heliocentric 0.3 AU e 0 | 1 m | SRKN14A | 224 | 4717.71 | no | OK |
| Heliocentric 0.3 AU e 0 | 1 m | DOPRI5-fixed | 97 | 49484760.20 | no | OK |
| Heliocentric 0.3 AU e 0 | 1 m | DOPRI5-adaptive | 67 | 3825247514.93 | no | OK |
| Heliocentric 0.3 AU e 0 | 1 m | Y6 | 113 | 72811020.71 | no | OK |
| Heliocentric 0.3 AU e 0 | 1 m | Y6C | 113 | 72811020.71 | no | OK |
| Heliocentric 0.3 AU e 0 | 1 m | PEFRL | 64 | 114781354.78 | no | OK |
| Heliocentric 0.3 AU e 0 | 1 m | RK4 | 64 | 498299676.89 | no | OK |
| Heliocentric 0.3 AU e 0 | 0.01 m | RKN12(10) | 188 | 0.03 | no | OK |
| Heliocentric 0.3 AU e 0 | 0.01 m | WH-Y6 | 225 | 0.16 | no | OK |
| Heliocentric 0.3 AU e 0 | 0.01 m | WH-BM14A | 464 | 0.00 | yes | EDGE |
| Heliocentric 0.3 AU e 0 | 0.01 m | GJ8 | 459 | 1.86 | no | OK |
| Heliocentric 0.3 AU e 0 | 0.01 m | DOP853-adaptive | 133 | 166118.46 | no | OK |
| Heliocentric 0.3 AU e 0 | 0.01 m | SRKN14A | 224 | 4717.71 | no | OK |
| Heliocentric 0.3 AU e 0 | 0.01 m | Y6C | 113 | 72811020.71 | no | OK |
| Heliocentric 0.3 AU e 0 | 0.01 m | Y6 | 113 | 72811020.71 | no | OK |
| Heliocentric 0.3 AU e 0 | 0.01 m | DOPRI5-adaptive | 67 | 3825247514.93 | no | OK |
| Heliocentric 0.3 AU e 0 | 0.01 m | DOPRI5-fixed | 97 | 49484760.20 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | RKN12(10) | 205 | 157996.21 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | WH-BM14A | 464 | 0.05 | yes | EDGE |
| Heliocentric 0.3 AU e 0.5 | 1 m | WH-Y6 | 225 | 307.63 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | IAS15 | 700 | 9394.70 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | DOP853-adaptive | 180 | 4974566.10 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | GJ8 | 772 | 20233.30 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | SRKN14A | 224 | 4349568.66 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | DOPRI5-adaptive | 109 | 2613515982.13 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | Y6 | 113 | 40578668472.70 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | Y6C | 113 | 40578668472.70 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | DOPRI5-fixed | 97 | 48400386194.71 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | PEFRL | 64 | 17869565809.96 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 1 m | RK4 | 64 | 65487047838.76 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 0.01 m | RKN12(10) | 205 | 157996.21 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 0.01 m | WH-BM14A | 464 | 0.05 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 0.01 m | WH-Y6 | 225 | 307.63 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 0.01 m | IAS15 | 700 | 9394.70 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 0.01 m | DOP853-adaptive | 180 | 4974566.10 | no | OK |
| Heliocentric 0.3 AU e 0.5 | 0.01 m | SRKN14A | 224 | 4349568.66 | no | OK |
| Heliocentric 0.3 AU e 0.99 | 1 m | RKN12(10) | 4,538 | 7718908.34 | no | OK |
| Heliocentric 0.3 AU e 0.99 | 1 m | DOP853-adaptive | 3,204 | 2219164617.79 | no | OK |
| Heliocentric 0.3 AU e 0.99 | 1 m | IAS15 | 8,085 | 1618.36 | no | OK |
| Heliocentric 0.3 AU e 0.99 | 1 m | DOPRI5-adaptive | 1,609 | 11547194557.51 | no | OK |
| Heliocentric 0.3 AU e 0.99 | 1 m | WH-BM14A | 7,424 | 141.32 | no | OK |
| Heliocentric 0.3 AU e 0.99 | 1 m | WH-Y6 | 14,337 | 1146.23 | no | OK |
| Heliocentric 0.3 AU e 0.99 | 1 m | GJ8 | 1.01E+005 | 489768517.37 | no | OK |
| Heliocentric 5 AU e 0 | 1 m | DOP853-adaptive | 121 | 0.97 | yes | EDGE |
| Heliocentric 5 AU e 0 | 1 m | RKN12(10) | 171 | 0.03 | yes | EDGE |
| Heliocentric 5 AU e 0 | 1 m | DOPRI5-adaptive | 61 | 822.64 | no | OK |
| Heliocentric 5 AU e 0 | 1 m | IAS15 | 346 | 0.01 | yes | EDGE |
| Heliocentric 5 AU e 0 | 1 m | PEFRL | 384 | 2.19 | no | OK |
| Heliocentric 5 AU e 0 | 1 m | RK4 | 384 | 3.85 | no | OK |
| Heliocentric 5 AU e 0.5 | 1 m | RKN12(10) | 188 | 0.03 | yes | EDGE |
| Heliocentric 5 AU e 0.5 | 1 m | DOP853-adaptive | 133 | 11.89 | no | OK |
| Heliocentric 5 AU e 0.5 | 1 m | IAS15 | 417 | 0.02 | yes | EDGE |
| Heliocentric 5 AU e 0.5 | 1 m | DOPRI5-adaptive | 67 | 354593.97 | no | OK |
| Heliocentric 5 AU e 0.5 | 1 m | DOPRI5-fixed | 577 | 0.74 | yes | EDGE |
| Heliocentric 5 AU e 0.5 | 1 m | Y6 | 673 | 0.68 | yes | EDGE |
| Heliocentric 5 AU e 0.5 | 1 m | Y6C | 673 | 0.68 | yes | EDGE |
| Heliocentric 5 AU e 0.5 | 1 m | PEFRL | 384 | 243.01 | no | OK |
| Heliocentric 5 AU e 0.5 | 1 m | RK4 | 384 | 678.30 | no | OK |
| Heliocentric 5 AU e 0.9 | 1 m | RKN12(10) | 205 | 1182.55 | no | OK |
| Heliocentric 5 AU e 0.9 | 1 m | DOP853-adaptive | 157 | 6641761.43 | no | OK |
| Heliocentric 5 AU e 0.9 | 1 m | IAS15 | 566 | 544.49 | no | OK |
| Heliocentric 5 AU e 0.9 | 1 m | WH-Y6 | 1,345 | 0.12 | yes | EDGE |
| Heliocentric 5 AU e 0.9 | 1 m | DOPRI5-adaptive | 85 | 4419145817.58 | no | OK |
| Heliocentric 5 AU e 0.9 | 1 m | GJ8 | 859 | 1376857.62 | no | OK |
| Heliocentric 5 AU e 0.9 | 1 m | SRKN14A | 1,344 | 189.45 | no | OK |
| Heliocentric 5 AU e 0.9 | 1 m | DOPRI5-fixed | 577 | 431927.44 | no | OK |
| Heliocentric 5 AU e 0.9 | 1 m | Y6 | 673 | 5351468.05 | no | OK |
| Heliocentric 5 AU e 0.9 | 1 m | Y6C | 673 | 5351468.05 | no | OK |
| Heliocentric 5 AU e 0.9 | 1 m | PEFRL | 384 | 22947649.73 | no | OK |
| Heliocentric 5 AU e 0.9 | 1 m | RK4 | 384 | 60224352.93 | no | OK |
| Heliocentric 5 AU e 0.99 | 1 m | RKN12(10) | 1,616 | 142833.95 | no | OK |
| Heliocentric 5 AU e 0.99 | 1 m | DOP853-adaptive | 1,141 | 64233939.45 | no | OK |
| Heliocentric 5 AU e 0.99 | 1 m | IAS15 | 2,937 | 33973.22 | no | OK |
| Heliocentric 5 AU e 0.99 | 1 m | DOPRI5-adaptive | 577 | 24531002588.06 | no | OK |
| Heliocentric 5 AU e 0.99 | 1 m | WH-BM14A | 5,162 | 7.78 | no | OK |
| Heliocentric 5 AU e 0.99 | 1 m | WH-Y6 | 2,493 | 8806.15 | no | OK |
| Heliocentric 5 AU e 0.99 | 1 m | SRKN14A | 6,230 | 40517370.95 | no | OK |
| Heliocentric 30 AU e 0.5 | 1 m | DOP853-adaptive | 109 | 0.91 | yes | EDGE |
| Heliocentric 30 AU e 0.5 | 1 m | DOPRI5-adaptive | 55 | 26.12 | no | OK |
| Far flight 30 AU e 0.5 (physical) | 1 m | DOP853-adaptive | 109 | 0.90 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) | 1 m | DOPRI5-adaptive | 55 | 26.13 | no | OK |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | DOPRI5-adaptive | 67 | 25.73 | no | OK |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | DOP853-adaptive | 133 | 0.86 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | RKN12(10) | 188 | 0.03 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | IAS15 | 313 | 0.01 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | GJ8 | 906 | 0.00 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | RK4 | 1,472 | 0.00 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | PEFRL | 1,472 | 0.01 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | DOPRI5-fixed | 2,209 | 0.00 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | Y6 | 2,577 | 0.00 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | Y6C | 2,577 | 0.00 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | SRKN14A | 5,152 | 0.00 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | WH-Y6 | 5,153 | 0.01 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | WH-BM14A | 10,672 | 0.01 | yes | EDGE |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | Leapfrog | 369 | 4159.28 | no | OK |
| Far flight 30 AU e 0.5 (physical) (normalized) | 1 m | Encke-IAS15 | 73,284 | 0.00 | yes | EDGE |
| Earth flyby 1.1 R vinf 1000 | 1 m | DOP853-adaptive | 313 | 7861.46 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | RKN12(10) | 426 | 33.40 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | WH-BM14A | 580 | 0.10 | yes | EDGE |
| Earth flyby 1.1 R vinf 1000 | 1 m | DOPRI5-adaptive | 163 | 483875.59 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | IAS15 | 963 | 0.83 | yes | EDGE |
| Earth flyby 1.1 R vinf 1000 | 1 m | WH-Y6 | 281 | 27.41 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | GJ8 | 1,623 | 57738.05 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | SRKN14A | 1,120 | 4851.94 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | Encke-IAS15 | 6,600 | 0.00 | yes | EDGE |
| Earth flyby 1.1 R vinf 1000 | 1 m | DOPRI5-fixed | 481 | 43944208.10 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | Y6 | 561 | 43340971.17 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | Y6C | 561 | 43340971.17 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | PEFRL | 320 | 15988365.54 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | RK4 | 320 | 66614230.38 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 1 m | Leapfrog | 81 | 305201074.48 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | RKN12(10) | 426 | 33.40 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | DOP853-adaptive | 313 | 7861.46 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | IAS15 | 963 | 0.83 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | DOPRI5-adaptive | 163 | 483875.59 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | WH-BM14A | 580 | 0.10 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | WH-Y6 | 281 | 27.41 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | GJ8 | 1,623 | 57738.05 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | Encke-IAS15 | 6,600 | 0.00 | yes | EDGE |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | SRKN14A | 1,120 | 4851.94 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | DOPRI5-fixed | 481 | 43944208.10 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | Y6C | 561 | 43340971.17 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | Y6 | 561 | 43340971.17 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | PEFRL | 320 | 15988365.54 | no | OK |
| Earth flyby 1.1 R vinf 1000 | 0.01 m | RK4 | 320 | 66614230.38 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | DOPRI5-adaptive | 163 | 109889.43 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | DOP853-adaptive | 313 | 671.61 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | RKN12(10) | 426 | 11.45 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | WH-BM14A | 580 | 0.04 | yes | EDGE |
| Earth flyby 1.1 R vinf 10000 | 1 m | WH-Y6 | 281 | 8.93 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | IAS15 | 851 | 1.07 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | GJ8 | 1,889 | 2433.71 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | SRKN14A | 1,120 | 4812.30 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | Encke-IAS15 | 5,840 | 0.00 | yes | EDGE |
| Earth flyby 1.1 R vinf 10000 | 1 m | DOPRI5-fixed | 481 | 1996521.58 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | Y6C | 561 | 6125954.26 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | Y6 | 561 | 6125954.26 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | PEFRL | 320 | 4331711.71 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | RK4 | 320 | 7272048.62 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 1 m | Leapfrog | 81 | 79146744.36 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | RKN12(10) | 426 | 11.45 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | DOP853-adaptive | 313 | 671.61 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | DOPRI5-adaptive | 163 | 109889.43 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | IAS15 | 851 | 1.07 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | WH-BM14A | 580 | 0.04 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | WH-Y6 | 281 | 8.93 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | Encke-IAS15 | 5,840 | 0.00 | yes | EDGE |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | GJ8 | 1,889 | 2433.71 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | SRKN14A | 1,120 | 4812.30 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | DOPRI5-fixed | 481 | 1996521.58 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | Y6 | 561 | 6125954.26 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | Y6C | 561 | 6125954.26 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | PEFRL | 320 | 4331711.71 | no | OK |
| Earth flyby 1.1 R vinf 10000 | 0.01 m | RK4 | 320 | 7272048.62 | no | OK |
| Earth flyby 3 R vinf 1000 | 1 m | RKN12(10) | 426 | 93.59 | no | OK |
| Earth flyby 3 R vinf 1000 | 1 m | DOP853-adaptive | 313 | 19545.95 | no | OK |
| Earth flyby 3 R vinf 1000 | 1 m | DOPRI5-adaptive | 163 | 1262038.33 | no | OK |
| Earth flyby 3 R vinf 1000 | 1 m | IAS15 | 993 | 0.13 | yes | EDGE |
| Earth flyby 3 R vinf 1000 | 1 m | WH-BM14A | 580 | 6.52 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | RKN12(10) | 426 | 93.59 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | DOP853-adaptive | 313 | 19545.95 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | IAS15 | 993 | 0.13 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | DOPRI5-adaptive | 163 | 1262038.33 | no | OK |
| Earth flyby 3 R vinf 1000 | 0.01 m | WH-BM14A | 580 | 6.52 | no | OK |
| Earth flyby 3 R vinf 10000 | 1 m | DOP853-adaptive | 313 | 1928.26 | no | OK |
| Earth flyby 3 R vinf 10000 | 1 m | DOPRI5-adaptive | 157 | 130273.40 | no | OK |
| Earth flyby 3 R vinf 10000 | 1 m | RKN12(10) | 426 | 10.76 | no | OK |
| Earth flyby 3 R vinf 10000 | 1 m | WH-BM14A | 580 | 0.65 | yes | EDGE |
| Earth flyby 3 R vinf 10000 | 1 m | IAS15 | 818 | 0.05 | yes | EDGE |
| Earth flyby 3 R vinf 10000 | 0.01 m | RKN12(10) | 426 | 10.76 | no | OK |
| Earth flyby 3 R vinf 10000 | 0.01 m | DOP853-adaptive | 313 | 1928.26 | no | OK |
| Earth flyby 3 R vinf 10000 | 0.01 m | IAS15 | 818 | 0.05 | no | OK |
| Earth flyby 3 R vinf 10000 | 0.01 m | DOPRI5-adaptive | 157 | 130273.40 | no | OK |
| Earth flyby 3 R vinf 10000 | 0.01 m | WH-BM14A | 580 | 0.65 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | RKN12(10) | 426 | 327.23 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | DOP853-adaptive | 313 | 47482.09 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | DOPRI5-adaptive | 163 | 3850068.26 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | IAS15 | 949 | 9.57 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | WH-BM14A | 580 | 1248.56 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | WH-Y6 | 561 | 20795.78 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | GJ8 | 2,543 | 1977.57 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | SRKN14A | 1,120 | 54655.29 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | Encke-IAS15 | 6,922 | 0.00 | yes | EDGE |
| Earth flyby 10 R vinf 1000 | 1 m | DOPRI5-fixed | 481 | 221017705.07 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | Y6C | 561 | 305596490.66 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | Y6 | 561 | 305596490.66 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | PEFRL | 320 | 97365637.83 | no | OK |
| Earth flyby 10 R vinf 1000 | 1 m | RK4 | 320 | 355682548.28 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | RKN12(10) | 426 | 327.23 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | DOP853-adaptive | 313 | 47482.09 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | IAS15 | 949 | 9.57 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | DOPRI5-adaptive | 163 | 3850068.26 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | WH-BM14A | 580 | 1248.56 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | Encke-IAS15 | 6,922 | 0.00 | yes | EDGE |
| Earth flyby 10 R vinf 1000 | 0.01 m | GJ8 | 2,543 | 1977.57 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | WH-Y6 | 561 | 20795.78 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | SRKN14A | 1,120 | 54655.29 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | DOPRI5-fixed | 481 | 221017705.07 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | Y6 | 561 | 305596490.66 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | Y6C | 561 | 305596490.66 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | PEFRL | 320 | 97365637.83 | no | OK |
| Earth flyby 10 R vinf 1000 | 0.01 m | RK4 | 320 | 355682548.28 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | DOP853-adaptive | 301 | 10879.77 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | DOPRI5-adaptive | 151 | 328365.42 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | RKN12(10) | 426 | 10.13 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | IAS15 | 746 | 2.28 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | WH-BM14A | 580 | 11.06 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | WH-Y6 | 281 | 2925.33 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | GJ8 | 1,840 | 6185.37 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | SRKN14A | 1,120 | 11292.19 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | Encke-IAS15 | 5,558 | 0.00 | yes | EDGE |
| Earth flyby 10 R vinf 10000 | 1 m | DOPRI5-fixed | 481 | 1847506.08 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | Y6C | 561 | 3583980.44 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | Y6 | 561 | 3583980.44 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | PEFRL | 320 | 11672876.92 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | RK4 | 320 | 13589281.30 | no | OK |
| Earth flyby 10 R vinf 10000 | 1 m | Leapfrog | 81 | 114969325.68 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | RKN12(10) | 426 | 10.13 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | DOP853-adaptive | 301 | 10879.77 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | IAS15 | 746 | 2.28 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | DOPRI5-adaptive | 151 | 328365.42 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | WH-BM14A | 580 | 11.06 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | WH-Y6 | 281 | 2925.33 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | Encke-IAS15 | 5,558 | 0.00 | yes | EDGE |
| Earth flyby 10 R vinf 10000 | 0.01 m | GJ8 | 1,840 | 6185.37 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | SRKN14A | 1,120 | 11292.19 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | DOPRI5-fixed | 481 | 1847506.08 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | Y6 | 561 | 3583980.44 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | Y6C | 561 | 3583980.44 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | PEFRL | 320 | 11672876.92 | no | OK |
| Earth flyby 10 R vinf 10000 | 0.01 m | RK4 | 320 | 13589281.30 | no | OK |
| Small-body flyby vinf 10 | 1 m | DOPRI5-adaptive | 151 | 850.59 | no | OK |
| Small-body flyby vinf 10 | 1 m | WH-Y6 | 281 | 0.00 | yes | EDGE |
| Small-body flyby vinf 10 | 1 m | DOP853-adaptive | 301 | 9.94 | no | OK |
| Small-body flyby vinf 10 | 1 m | RKN12(10) | 426 | 0.01 | yes | EDGE |
| Small-body flyby vinf 10 | 1 m | WH-BM14A | 580 | 0.00 | yes | EDGE |
| Small-body flyby vinf 10 | 1 m | IAS15 | 963 | 0.00 | yes | EDGE |
| Small-body flyby vinf 10 | 1 m | SRKN14A | 280 | 691.60 | no | OK |
| Small-body flyby vinf 10 | 1 m | DOPRI5-fixed | 481 | 13711.62 | no | OK |
| Small-body flyby vinf 10 | 1 m | GJ8 | 1,336 | 856.40 | no | OK |
| Small-body flyby vinf 10 | 1 m | PEFRL | 320 | 4829.48 | no | OK |
| Small-body flyby vinf 10 | 1 m | Y6C | 561 | 12862.73 | no | OK |
| Small-body flyby vinf 10 | 1 m | Y6 | 561 | 12862.73 | no | OK |
| Small-body flyby vinf 10 | 1 m | RK4 | 320 | 20691.66 | no | OK |
| Small-body flyby vinf 10 | 1 m | Encke-IAS15 | 6,418 | 0.00 | yes | EDGE |
| Small-body flyby vinf 10 | 1 m | Leapfrog | 81 | 89611.31 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | WH-Y6 | 281 | 0.00 | yes | EDGE |
| Small-body flyby vinf 10 | 0.01 m | DOP853-adaptive | 301 | 9.94 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | DOPRI5-adaptive | 151 | 850.59 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | RKN12(10) | 426 | 0.01 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | WH-BM14A | 580 | 0.00 | yes | EDGE |
| Small-body flyby vinf 10 | 0.01 m | IAS15 | 963 | 0.00 | yes | EDGE |
| Small-body flyby vinf 10 | 0.01 m | SRKN14A | 280 | 691.60 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | GJ8 | 1,336 | 856.40 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | DOPRI5-fixed | 481 | 13711.62 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | Encke-IAS15 | 6,418 | 0.00 | yes | EDGE |
| Small-body flyby vinf 10 | 0.01 m | Y6 | 561 | 12862.73 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | Y6C | 561 | 12862.73 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | PEFRL | 320 | 4829.48 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | RK4 | 320 | 20691.66 | no | OK |
| Small-body flyby vinf 10 | 0.01 m | Leapfrog | 81 | 89611.31 | no | OK |
| Small-body flyby vinf 50 | 1 m | DOPRI5-adaptive | 151 | 839.07 | no | OK |
| Small-body flyby vinf 50 | 1 m | WH-Y6 | 281 | 0.00 | yes | EDGE |
| Small-body flyby vinf 50 | 1 m | DOP853-adaptive | 301 | 9.71 | no | OK |
| Small-body flyby vinf 50 | 1 m | RKN12(10) | 426 | 0.01 | yes | EDGE |
| Small-body flyby vinf 50 | 1 m | WH-BM14A | 580 | 0.00 | yes | EDGE |
| Small-body flyby vinf 50 | 1 m | IAS15 | 977 | 0.00 | yes | EDGE |
| Small-body flyby vinf 50 | 1 m | SRKN14A | 280 | 678.00 | no | OK |
| Small-body flyby vinf 50 | 1 m | DOPRI5-fixed | 481 | 13368.28 | no | OK |
| Small-body flyby vinf 50 | 1 m | GJ8 | 1,336 | 841.92 | no | OK |
| Small-body flyby vinf 50 | 1 m | PEFRL | 320 | 4752.63 | no | OK |
| Small-body flyby vinf 50 | 1 m | Y6C | 561 | 12720.89 | no | OK |
| Small-body flyby vinf 50 | 1 m | Y6 | 561 | 12720.89 | no | OK |
| Small-body flyby vinf 50 | 1 m | RK4 | 320 | 20195.62 | no | OK |
| Small-body flyby vinf 50 | 1 m | Encke-IAS15 | 6,290 | 0.00 | yes | EDGE |
| Small-body flyby vinf 50 | 1 m | Leapfrog | 81 | 88900.75 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | WH-Y6 | 281 | 0.00 | yes | EDGE |
| Small-body flyby vinf 50 | 0.01 m | DOP853-adaptive | 301 | 9.71 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | DOPRI5-adaptive | 151 | 839.07 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | RKN12(10) | 426 | 0.01 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | WH-BM14A | 580 | 0.00 | yes | EDGE |
| Small-body flyby vinf 50 | 0.01 m | IAS15 | 977 | 0.00 | yes | EDGE |
| Small-body flyby vinf 50 | 0.01 m | SRKN14A | 280 | 678.00 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | GJ8 | 1,336 | 841.92 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | DOPRI5-fixed | 481 | 13368.28 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | Encke-IAS15 | 6,290 | 0.00 | yes | EDGE |
| Small-body flyby vinf 50 | 0.01 m | Y6C | 561 | 12720.89 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | Y6 | 561 | 12720.89 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | PEFRL | 320 | 4752.63 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | RK4 | 320 | 20195.62 | no | OK |
| Small-body flyby vinf 50 | 0.01 m | Leapfrog | 81 | 88900.75 | no | OK |
| Small-body flyby vinf 100 | 1 m | DOPRI5-adaptive | 151 | 806.58 | no | OK |
| Small-body flyby vinf 100 | 1 m | WH-Y6 | 281 | 0.00 | yes | EDGE |
| Small-body flyby vinf 100 | 1 m | DOP853-adaptive | 301 | 9.07 | no | OK |
| Small-body flyby vinf 100 | 1 m | RKN12(10) | 426 | 0.01 | yes | EDGE |
| Small-body flyby vinf 100 | 1 m | WH-BM14A | 580 | 0.00 | yes | EDGE |
| Small-body flyby vinf 100 | 1 m | IAS15 | 970 | 0.00 | yes | EDGE |
| Small-body flyby vinf 100 | 1 m | SRKN14A | 280 | 638.49 | no | OK |
| Small-body flyby vinf 100 | 1 m | DOPRI5-fixed | 481 | 12392.49 | no | OK |
| Small-body flyby vinf 100 | 1 m | GJ8 | 1,322 | 800.66 | no | OK |
| Small-body flyby vinf 100 | 1 m | PEFRL | 320 | 4528.01 | no | OK |
| Small-body flyby vinf 100 | 1 m | Y6C | 561 | 12303.06 | no | OK |
| Small-body flyby vinf 100 | 1 m | Y6 | 561 | 12303.06 | no | OK |
| Small-body flyby vinf 100 | 1 m | RK4 | 320 | 18799.43 | no | OK |
| Small-body flyby vinf 100 | 1 m | Encke-IAS15 | 6,402 | 0.00 | yes | EDGE |
| Small-body flyby vinf 100 | 1 m | Leapfrog | 81 | 86759.05 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | WH-Y6 | 281 | 0.00 | yes | EDGE |
| Small-body flyby vinf 100 | 0.01 m | DOP853-adaptive | 301 | 9.07 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | DOPRI5-adaptive | 151 | 806.58 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | RKN12(10) | 426 | 0.01 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | WH-BM14A | 580 | 0.00 | yes | EDGE |
| Small-body flyby vinf 100 | 0.01 m | IAS15 | 970 | 0.00 | yes | EDGE |
| Small-body flyby vinf 100 | 0.01 m | SRKN14A | 280 | 638.49 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | GJ8 | 1,322 | 800.66 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | DOPRI5-fixed | 481 | 12392.49 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | Encke-IAS15 | 6,402 | 0.00 | yes | EDGE |
| Small-body flyby vinf 100 | 0.01 m | Y6C | 561 | 12303.06 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | Y6 | 561 | 12303.06 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | PEFRL | 320 | 4528.01 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | RK4 | 320 | 18799.43 | no | OK |
| Small-body flyby vinf 100 | 0.01 m | Leapfrog | 81 | 86759.05 | no | OK |
## Tie rerun at grid ratio 1.15

The cells below were rerun with a 1.15 control grid (instead of 1.5). If the margin stays below 1.5, the methods are **неразличимы по цене**; no winner is named.

| scenario | target | class | first | second | margin | equivalent set |
|---|---|---|---|---|---:|---|
| Moon flyby vinf 200 alt 100 | 1 m | equivalent | DOP853-adaptive | RKN12(10) | 1.11 | DOP853-adaptive, RKN12(10), DOPRI5-adaptive |
| Moon flyby vinf 200 alt 100 | 0.01 m | equivalent | RKN12(10) | DOP853-adaptive | 1.19 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 1000 alt 100 | 1 m | equivalent | DOP853-adaptive | DOPRI5-adaptive | 1.05 | DOP853-adaptive, DOPRI5-adaptive, RKN12(10) |
| Moon flyby vinf 1000 alt 100 | 0.01 m | equivalent | RKN12(10) | DOP853-adaptive | 1.09 | RKN12(10), DOP853-adaptive |
| Moon flyby vinf 10000 alt 100 | 1 m | equivalent | DOPRI5-adaptive | DOP853-adaptive | 1.46 | DOPRI5-adaptive, DOP853-adaptive |
| Moon flyby vinf 10000 alt 100 | 0.01 m | floor-limited | DOP853-adaptive | - | 1.11 |  |
| Heliocentric 1 AU e 0.9 | 1 m | decisive | RKN12(10) | - | 2.14 | RKN12(10) |
| Heliocentric 1 AU e 0.9 | 0.01 m | no-winner | - | - | - |  |
| Earth flyby 3 R vinf 1000 | 1 m | equivalent | RKN12(10) | DOP853-adaptive | 1.03 | RKN12(10), DOP853-adaptive |
| Earth flyby 3 R vinf 1000 | 0.01 m | equivalent | RKN12(10) | DOP853-adaptive | 1.38 | RKN12(10), DOP853-adaptive |
| Earth flyby 3 R vinf 10000 | 1 m | floor-limited | DOP853-adaptive | - | 1.01 |  |
| Earth flyby 3 R vinf 10000 | 0.01 m | equivalent | RKN12(10) | DOP853-adaptive | 1.04 | RKN12(10), DOP853-adaptive |
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
| 1 | SRKN14A | 86,016 | 5.76E+000 | 5.08E+003 | 7.15E+005 |
| 2 | WH-Y6 | 93,187 | 5.76E+000 | 5.09E+003 | 5.29E-002 |
| 3 | WH-BM14A | 58,368 | 5.77E+000 | 5.09E+003 | 8.28E-002 |
| 4 | GJ8 | 95,673 | 7.01E+000 | 6.19E+003 | 1.38E+003 |
| 5 | RKN12(10) | 91,599 | 2.03E+001 | 1.79E+004 | 8.54E+003 |
| 6 | PEFRL | 76,800 | 3.08E+001 | 5.27E+003 | 1.20E+007 |
| 7 | Leapfrog | 96,003 | 5.19E+002 | 5.22E+003 | 1.36E+007 |
| 8 | Y6 | 91,395 | 1.27E+003 | 5.08E+003 | 1.36E+007 |
| 9 | Y6C | 91,395 | 1.27E+003 | 5.08E+003 | 1.36E+007 |
| 10 | DOP853-adaptive | 92,307 | 8.55E+004 | 7.53E+007 | 1.35E+007 |
| 11 | DOPRI5-fixed | 78,339 | 5.95E+008 | 4.33E+013 | 7.68E+010 |
| 12 | RK4 | 76,800 | 4.16E+009 | 7.46E+014 | 2.13E+011 |

### Earth LEO 400 km, 30 d — budget 1,000,000 RHS

| rank | method | cost | dE | dL | position error |
|---:|---|---:|---:|---:|---:|
| 1 | DOP853-adaptive | 8.20E+005 | 5.77E+000 | 5.09E+003 | 1.41E-001 |
| 2 | PEFRL | 8.63E+005 | 5.77E+000 | 5.09E+003 | 9.44E+002 |
| 3 | Y6 | 9.14E+005 | 5.77E+000 | 5.09E+003 | 1.41E+002 |
| 4 | Y6C | 9.14E+005 | 5.77E+000 | 5.09E+003 | 1.41E+002 |
| 5 | IAS15 | 8.45E+005 | 5.77E+000 | 5.09E+003 | 1.52E-003 |
| 6 | SRKN14A | 9.03E+005 | 5.77E+000 | 5.09E+003 | 5.96E-001 |
| 7 | Leapfrog | 7.28E+005 | 5.86E+000 | 5.08E+003 | 9.70E+005 |
| 8 | DOPRI5-fixed | 8.66E+005 | 3.84E+001 | 3.39E+004 | 1.81E+004 |
| 9 | DOPRI5-adaptive | 8.16E+005 | 5.05E+001 | 4.46E+004 | 2.44E+004 |
| 10 | RK4 | 8.63E+005 | 2.74E+002 | 2.42E+005 | 1.39E+005 |

### J2 x1 inc 98 — budget 100,000 RHS

| rank | method | cost | dE | dL | position error |
|---:|---|---:|---:|---:|---:|
| 1 | Leapfrog | 81,891 | 2.27E+000 | - | 2.87E+003 |
| 2 | DOPRI5-adaptive | 85,845 | 2.59E+000 | - | 9.63E-002 |
| 3 | DOPRI5-fixed | 97,059 | 2.59E+000 | - | 5.22E-002 |
| 4 | PEFRL | 97,152 | 2.59E+000 | - | 2.44E-001 |
| 5 | Y6C | 92,739 | 2.59E+000 | - | 6.00E-003 |
| 6 | Y6 | 92,739 | 2.59E+000 | - | 5.99E-003 |
| 7 | RK4 | 97,152 | 2.61E+000 | - | 6.80E-001 |

### J2 x1 inc 98 — budget 1,000,000 RHS

| rank | method | cost | dE | dL | position error |
|---:|---|---:|---:|---:|---:|
| 1 | Leapfrog | 9.33E+005 | 2.59E+000 | - | 2.21E+001 |

### Moon orbit 100 km x1000 orbits — budget 100,000 RHS

| rank | method | cost | dE | dL | position error |
|---:|---|---:|---:|---:|---:|
| 1 | PEFRL | 81,920 | 3.34E+001 | 3.75E+004 | 1.67E+006 |
| 2 | WH-Y6 | 96,769 | 3.36E+001 | 3.78E+004 | 2.33E+001 |
| 3 | WH-BM14A | 96,512 | 3.36E+001 | 3.78E+004 | 1.13E+000 |
| 4 | RKN12(10) | 95,830 | 3.36E+001 | 3.78E+004 | 5.72E+001 |
| 5 | GJ8 | 91,195 | 3.36E+001 | 3.78E+004 | 6.61E+001 |
| 6 | SRKN14A | 86,016 | 3.37E+001 | 3.79E+004 | 5.74E+004 |
| 7 | Y6 | 94,977 | 3.37E+001 | 3.72E+004 | 3.68E+006 |
| 8 | Y6C | 94,977 | 3.37E+001 | 3.72E+004 | 3.68E+006 |
| 9 | Leapfrog | 68,353 | 5.54E+001 | 3.74E+004 | 3.68E+006 |
| 10 | DOP853-adaptive | 89,125 | 5.97E+002 | 6.71E+005 | 3.19E+006 |
| 11 | DOPRI5-fixed | 81,409 | 7.33E+007 | 7.87E+013 | 4.63E+010 |
| 12 | RK4 | 81,920 | 4.11E+009 | 9.48E+015 | 1.61E+011 |

### Moon orbit 100 km x1000 orbits — budget 1,000,000 RHS

| rank | method | cost | dE | dL | position error |
|---:|---|---:|---:|---:|---:|
| 1 | RK4 | 9.24E+005 | 3.18E+001 | 3.58E+004 | 2.27E+004 |
| 2 | Leapfrog | 7.78E+005 | 3.35E+001 | 3.76E+004 | 2.50E+005 |
| 3 | DOP853-adaptive | 7.80E+005 | 3.36E+001 | 3.78E+004 | 1.05E-002 |
| 4 | IAS15 | 6.99E+005 | 3.36E+001 | 3.78E+004 | 1.14E-002 |
| 5 | SRKN14A | 9.57E+005 | 3.36E+001 | 3.78E+004 | 4.47E-002 |
| 6 | Y6 | 9.73E+005 | 3.36E+001 | 3.78E+004 | 7.42E+000 |
| 7 | Y6C | 9.73E+005 | 3.36E+001 | 3.78E+004 | 7.42E+000 |
| 8 | PEFRL | 9.24E+005 | 3.36E+001 | 3.78E+004 | 1.08E+002 |
| 9 | DOPRI5-fixed | 9.23E+005 | 3.40E+001 | 3.82E+004 | 3.24E+003 |
| 10 | DOPRI5-adaptive | 9.20E+005 | 3.40E+001 | 3.83E+004 | 3.32E+003 |

### Heliocentric 1 AU e 0.5 — budget 100,000 RHS

| rank | method | cost | dE | dL | position error |
|---:|---|---:|---:|---:|---:|
| 1 | Y6 | 82,937 | 1.84E+004 | 2.70E+011 | 1.24E-001 |
| 2 | DOPRI5-fixed | 71,089 | 1.84E+004 | 2.70E+011 | 4.67E-002 |
| 3 | RK4 | 71,104 | 1.84E+004 | 2.70E+011 | 1.72E-002 |
| 4 | PEFRL | 71,104 | 1.84E+004 | 2.70E+011 | 6.96E-002 |
| 5 | Leapfrog | 89,969 | 1.84E+004 | 2.70E+011 | 3.27E+004 |

### Heliocentric 1 AU e 0.5 — budget 1,000,000 RHS

| rank | method | cost | dE | dL | position error |
|---:|---|---:|---:|---:|---:|
| 1 | PEFRL | 8.10E+005 | 1.84E+004 | 2.70E+011 | 2.10E-001 |
| 2 | DOP853-adaptive | 6.31E+005 | 1.84E+004 | 2.70E+011 | 4.77E-002 |
| 3 | IAS15 | 7.93E+005 | 1.84E+004 | 2.70E+011 | 1.42E-004 |
| 4 | RKN12(10) | 8.94E+005 | 1.84E+004 | 2.70E+011 | 8.16E-002 |
| 5 | RK4 | 5.40E+005 | 1.84E+004 | 2.70E+011 | 1.61E-001 |

