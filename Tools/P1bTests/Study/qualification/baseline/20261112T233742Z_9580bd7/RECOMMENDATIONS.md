# Recommendations (Test564)

Generated from `matrix.csv`; the price is `calls + w·aux` (single-price rule).
All catalog problems have a VALID oracle.

Rule (frozen, TASK.md section 11): for each tag and level, use only problems whose
level is evaluated by the oracle; need at least 2 problems; candidates must PASS on
all of them; the price is normalised per problem and averaged geometrically; a
second method within 15% is declared a tie.

## 1pn

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R2

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R2

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M100

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M10

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## central-dominant

*Note:* WH-Y6 / WH-BM14A / Encke-IAS15 are N/A on R1: it is a 7-body
N-body task, while they require one particle in a central field (`PosDim == 3`).
For a single particle in a central field see the `ecc-*` and `kepler` tags and R4,
with the R2 caveat: R2 (1PN) is certified to A2 only, A3 is SKIP by the oracle floor.

### A1

*Problems:* K1, K2, K3, K4, K5, K6, R1
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* DOP853-adaptive (score 1.556, margin 55.6%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* Leapfrog, GJ8

### A2

*Problems:* K1, K2, K3, K4, K5, K6, R1
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* DOP853-adaptive (score 1.976, margin 97.6%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* Leapfrog, GJ8

### A3

*Problems:* K1, K2, K3, K4, K5, K6, R1
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* IAS15 (score 1.783, margin 78.3%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* RK4, PEFRL, Y6, Leapfrog, SRKN14A, GJ8
*Excluded SKIP:* DOPRI5-fixed

### M100

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R1

### M10

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R1

### M1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R1

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## close-encounter

### A1

*Problems:* K6, T3, R4
*Recommended:* **RKN12(10)** (score 1.067)
*Second:* DOP853-adaptive (score 1.369, margin 28.4%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* RK4, PEFRL, Leapfrog, GJ8
*Excluded SKIP:* DOPRI5-fixed, Y6, Y6C, SRKN14A

### A2

*Problems:* K6, T3, R4
*Recommended:* **IAS15** (score 1.000)
*Second:* none (score n/a, margin n/a)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* RK4, DOPRI5-adaptive, DOP853-adaptive, PEFRL, Leapfrog, RKN12(10), GJ8
*Excluded SKIP:* DOPRI5-fixed, Y6, Y6C, SRKN14A

### A3

*No method passes on all 2 problems; no recommendation.*
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* RK4, DOPRI5-adaptive, DOP853-adaptive, PEFRL, Leapfrog, RKN12(10), IAS15, GJ8
*Excluded SKIP:* DOPRI5-fixed, Y6, Y6C, SRKN14A

### M100

*Problems:* R4, S12, S15, P4, F03
*Recommended:* **RKN12(10)** (score 1.686) — tie with **DOP853-adaptive** within 15%
*Second:* DOP853-adaptive (score 1.690, margin 0.2%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* DOPRI5-fixed, GJ8
*Excluded SKIP:* Leapfrog

### M10

*Problems:* R4, S12, S15, P4, F03
*Recommended:* **RKN12(10)** (score 1.403) — tie with **DOP853-adaptive** within 15%
*Second:* DOP853-adaptive (score 1.499, margin 6.8%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* DOPRI5-fixed, GJ8
*Excluded SKIP:* Leapfrog

### M1

*Problems:* R4, S12, S15, P4, F03
*Recommended:* **DOPRI5-adaptive** (score 1.878) — tie with **IAS15** within 15%
*Second:* IAS15 (score 2.053, margin 9.3%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* DOPRI5-fixed, DOP853-adaptive, RKN12(10), GJ8
*Excluded SKIP:* RK4, PEFRL, Y6, Leapfrog

### M0.1

*Problems:* R4, S12, S15, F03
*Recommended:* **DOP853-adaptive** (score 1.049) — tie with **RKN12(10)** within 15%
*Second:* RKN12(10) (score 1.166, margin 11.2%)
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-fixed, Leapfrog, GJ8

### M0.01

*Problems:* R4, S12, S15, F03
*Recommended:* **DOP853-adaptive** (score 1.081) — tie with **RKN12(10)** within 15%
*Second:* RKN12(10) (score 1.091, margin 0.9%)
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-fixed, Leapfrog, GJ8

## drag

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M100

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S10

### M10

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S10

### M1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S10

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## ecc-high

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: K4

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: K4

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: K4

### M100

*Problems:* S04, S05, S14, P5
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* DOP853-adaptive (score 1.835, margin 83.5%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* GJ8
*Excluded SKIP:* Leapfrog

### M10

*Problems:* S04, S05, S14, P5
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* DOP853-adaptive (score 1.914, margin 91.4%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* Leapfrog, GJ8

### M1

*Problems:* S04, S05, S14, P5
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* DOP853-adaptive (score 1.942, margin 94.2%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* DOPRI5-adaptive, Leapfrog, GJ8
*Excluded SKIP:* RK4, Y6

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## ecc-low

### A1

*Problems:* K1, K2
*Recommended:* **Encke-IAS15** (score 1.000)
*Second:* RKN12(10) (score 2.383, margin 138.3%)
*Excluded N/A:* none
*Excluded FAIL:* GJ8

### A2

*Problems:* K1, K2
*Recommended:* **Encke-IAS15** (score 1.000)
*Second:* RKN12(10) (score 3.699, margin 269.9%)
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog, GJ8

### A3

*Problems:* K1, K2
*Recommended:* **Encke-IAS15** (score 1.000)
*Second:* RKN12(10) (score 5.616, margin 461.6%)
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog, GJ8

### M100

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M10

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## ecc-mid

### A1

*Problems:* K3, L2
*Recommended:* **Encke-IAS15** (score 1.000)
*Second:* WH-Y6 (score 7.006, margin 600.6%)
*Excluded N/A:* none
*Excluded FAIL:* GJ8
*Excluded SKIP:* Leapfrog

### A2

*Problems:* K3, L2
*Recommended:* **Encke-IAS15** (score 1.000)
*Second:* WH-Y6 (score 7.006, margin 600.6%)
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog, GJ8

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: K3

### M100

*Problems:* S08, F02
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* DOP853-adaptive (score 1.830, margin 83.0%)
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog, GJ8

### M10

*Problems:* S08, F02
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* DOP853-adaptive (score 1.828, margin 82.8%)
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog, GJ8

### M1

*Problems:* S08, F02
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* IAS15 (score 1.797, margin 79.7%)
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog, GJ8

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## escape

### A1

*Problems:* K5, K6
*Recommended:* **RKN12(10)** (score 1.037) — tie with **DOP853-adaptive** within 15%
*Second:* DOP853-adaptive (score 1.143, margin 10.1%)
*Excluded N/A:* none
*Excluded FAIL:* none

### A2

*Problems:* K5, K6
*Recommended:* **RKN12(10)** (score 1.159)
*Second:* Encke-IAS15 (score 1.426, margin 23.1%)
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog

### A3

*Problems:* K5, K6
*Recommended:* **Encke-IAS15** (score 1.222) — tie with **RKN12(10)** within 15%
*Second:* RKN12(10) (score 1.370, margin 12.1%)
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog

### M100

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S15

### M10

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S15

### M1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S15

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S15

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S15

## far-flight

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M100

*Problems:* S07, S08, F01
*Recommended:* **RKN12(10)** (score 1.409)
*Second:* DOP853-adaptive (score 1.800, margin 27.7%)
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog, GJ8

### M10

*Problems:* S07, S08, F01
*Recommended:* **RKN12(10)** (score 1.167)
*Second:* DOP853-adaptive (score 1.581, margin 35.6%)
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog, GJ8

### M1

*Problems:* S07, S08, F01
*Recommended:* **RKN12(10)** (score 1.122)
*Second:* DOP853-adaptive (score 1.700, margin 51.5%)
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog, GJ8

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## flyby

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M100

*Problems:* S12, S15, F03
*Recommended:* **DOPRI5-adaptive** (score 1.000)
*Second:* DOP853-adaptive (score 1.511, margin 51.1%)
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-fixed

### M10

*Problems:* S12, S15, F03
*Recommended:* **DOPRI5-adaptive** (score 1.006)
*Second:* DOP853-adaptive (score 1.265, margin 25.7%)
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-fixed

### M1

*Problems:* S12, S15, F03
*Recommended:* **DOPRI5-adaptive** (score 1.096) — tie with **DOP853-adaptive** within 15%
*Second:* DOP853-adaptive (score 1.124, margin 2.5%)
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-fixed

### M0.1

*Problems:* S12, S15, F03
*Recommended:* **DOP853-adaptive** (score 1.065) — tie with **RKN12(10)** within 15%
*Second:* RKN12(10) (score 1.214, margin 13.9%)
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-fixed, Leapfrog

### M0.01

*Problems:* S12, S15, F03
*Recommended:* **DOP853-adaptive** (score 1.072) — tie with **RKN12(10)** within 15%
*Second:* RKN12(10) (score 1.122, margin 4.7%)
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-fixed, Leapfrog

## geo

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M100

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S06

### M10

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S06

### M1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S06

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## gto

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M100

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S04

### M10

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S04

### M1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S04

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## heliocentric

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M100

*Problems:* S07, S08, F01, F02
*Recommended:* **RKN12(10)** (score 1.294)
*Second:* DOP853-adaptive (score 1.840, margin 42.3%)
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog, GJ8

### M10

*Problems:* S07, S08, F01, F02
*Recommended:* **RKN12(10)** (score 1.123)
*Second:* DOP853-adaptive (score 1.691, margin 50.6%)
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog, GJ8

### M1

*Problems:* S07, S08, F01, F02
*Recommended:* **RKN12(10)** (score 1.090)
*Second:* DOP853-adaptive (score 1.813, margin 66.3%)
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog, GJ8

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## heo

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M100

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S14

### M10

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S14

### M1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S14

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## j2

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M100

*Problems:* S03, S09
*Recommended:* **WH-BM14A** (score 1.148)
*Second:* RKN12(10) (score 1.501, margin 30.7%)
*Excluded N/A:* none
*Excluded FAIL:* RK4

### M10

*Problems:* S03, S09
*Recommended:* **RKN12(10)** (score 1.213) — tie with **WH-BM14A** within 15%
*Second:* WH-BM14A (score 1.296, margin 6.8%)
*Excluded N/A:* none
*Excluded FAIL:* RK4

### M1

*Problems:* S03, S09
*Recommended:* **WH-BM14A** (score 1.173) — tie with **RKN12(10)** within 15%
*Second:* RKN12(10) (score 1.348, margin 14.9%)
*Excluded N/A:* none
*Excluded FAIL:* RK4

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## kepler

### A1

*Problems:* K1, K2, K3, K4, K5, K6, L1, L2
*Recommended:* **Encke-IAS15** (score 1.854)
*Second:* RKN12(10) (score 4.092, margin 120.7%)
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog, GJ8

### A2

*Problems:* K1, K2, K3, K4, K5, K6, L1, L2
*Recommended:* **WH-Y6** (score 1.989)
*Second:* DOP853-adaptive (score 2.375, margin 19.4%)
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog, RKN12(10), IAS15, GJ8
*Excluded SKIP:* RK4, DOPRI5-fixed, DOPRI5-adaptive, PEFRL, Y6, Y6C, WH-BM14A, Encke-IAS15

### A3

*Problems:* K1, K2, K3, K4, K5, K6
*Recommended:* **Encke-IAS15** (score 1.069)
*Second:* RKN12(10) (score 4.685, margin 338.2%)
*Excluded N/A:* none
*Excluded FAIL:* RK4, PEFRL, Y6, Leapfrog, SRKN14A, GJ8

### M100

*Not proven (fewer than 2 problems with an evaluated level).* Problems: L1

### M10

*Not proven (fewer than 2 problems with an evaluated level).* Problems: L1

### M1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: L1

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: L1

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: L1

## leo

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M100

*Problems:* S01, S02, S03, S09, S10, S11, S17
*Recommended:* **DOP853-adaptive** (score 1.000)
*Second:* IAS15 (score 1.758, margin 75.8%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8
*Excluded FAIL:* RK4

### M10

*Problems:* S01, S02, S03, S09, S10, S11, S17
*Recommended:* **DOP853-adaptive** (score 1.003)
*Second:* IAS15 (score 1.359, margin 35.6%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8
*Excluded FAIL:* RK4

### M1

*Problems:* S01, S02, S03, S09, S10, S11, S17
*Recommended:* **DOP853-adaptive** (score 1.000)
*Second:* IAS15 (score 1.212, margin 21.2%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8
*Excluded FAIL:* RK4

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## long-arc

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M100

*Problems:* S06, P2, F04
*Recommended:* **RKN12(10)** (score 1.021) — tie with **GJ8** within 15%
*Second:* GJ8 (score 1.076, margin 5.4%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* RK4, DOPRI5-adaptive
*Excluded SKIP:* DOPRI5-fixed, Leapfrog

### M10

*Problems:* S06, F04
*Recommended:* **WH-BM14A** (score 1.000)
*Second:* RKN12(10) (score 1.354, margin 35.4%)
*Excluded N/A:* none
*Excluded FAIL:* RK4, Leapfrog

### M1

*Problems:* S06, F04
*Recommended:* **WH-BM14A** (score 1.000) — tie with **RKN12(10)** within 15%
*Second:* RKN12(10) (score 1.078, margin 7.8%)
*Excluded N/A:* none
*Excluded FAIL:* RK4, Leapfrog

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## long-horizon

### A1

*Problems:* T1, R1, R2, L1, L2
*Recommended:* **DOP853-adaptive** (score 1.108) — tie with **IAS15** within 15%
*Second:* IAS15 (score 1.202, margin 8.5%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15
*Excluded FAIL:* RK4, DOPRI5-adaptive, GJ8

### A2

*Problems:* T1, R1, R2, L1, L2
*Recommended:* **DOP853-adaptive** (score 1.000)
*Second:* none (score n/a, margin n/a)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15
*Excluded FAIL:* RK4, DOPRI5-adaptive, Leapfrog, RKN12(10), IAS15, GJ8
*Excluded SKIP:* DOPRI5-fixed

### A3

*Problems:* T1, R1
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* IAS15 (score 1.514, margin 51.4%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* RK4, DOPRI5-fixed, DOPRI5-adaptive, PEFRL, Y6, Leapfrog
*Excluded SKIP:* SRKN14A

### M100

*Problems:* R1, L1
*Recommended:* **GJ8** (score 1.000) — tie with **RKN12(10)** within 15%
*Second:* RKN12(10) (score 1.077, margin 7.7%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* none
*Excluded SKIP:* Leapfrog

### M10

*Problems:* R1, L1
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* GJ8 (score 1.156, margin 15.6%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* none
*Excluded SKIP:* Leapfrog

### M1

*Problems:* R1, L1
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* GJ8 (score 1.486, margin 48.6%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* none
*Excluded SKIP:* RK4, DOPRI5-fixed, PEFRL, Y6, Leapfrog, SRKN14A

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: L1

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: L1

## many-body

### A1

*Problems:* R1, R2
*Recommended:* **DOP853-adaptive** (score 1.000)
*Second:* IAS15 (score 1.434, margin 43.4%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15
*Excluded FAIL:* RK4, DOPRI5-adaptive

### A2

*Problems:* R1, R2
*Recommended:* **IAS15** (score 1.027) — tie with **DOP853-adaptive** within 15%
*Second:* DOP853-adaptive (score 1.050, margin 2.3%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15
*Excluded FAIL:* RK4, DOPRI5-adaptive

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R1

### M100

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R1

### M10

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R1

### M1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R1

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## molniya

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M100

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S05

### M10

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S05

### M1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S05

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## moon-soi

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M100

*Problems:* S12, F04, F05
*Recommended:* **WH-Y6** (score 2.653) — tie with **SRKN14A** within 15%
*Second:* SRKN14A (score 2.877, margin 8.4%)
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-fixed, Y6, Y6C, GJ8
*Excluded SKIP:* Leapfrog

### M10

*Problems:* S12, F04, F05
*Recommended:* **RKN12(10)** (score 2.031)
*Second:* DOP853-adaptive (score 2.386, margin 17.5%)
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-fixed, Y6, Y6C, GJ8
*Excluded SKIP:* Leapfrog

### M1

*Problems:* S12, F04, F05
*Recommended:* **RKN12(10)** (score 1.367)
*Second:* WH-Y6 (score 1.778, margin 30.0%)
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-fixed, DOPRI5-adaptive, Y6, Y6C, GJ8
*Excluded SKIP:* Leapfrog

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S12

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: S12

## nbody-chaotic

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: T3

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: T3

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: T3

### M100

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M10

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## nbody-regular

### A1

*Problems:* T1, T2, R1
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* SRKN14A (score 1.277, margin 27.7%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* GJ8

### A2

*Problems:* T1, T2, R1
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* IAS15 (score 2.221, margin 122.1%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* Leapfrog, GJ8

### A3

*Problems:* T1, T2, R1
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* IAS15 (score 1.703, margin 70.3%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* RK4, DOPRI5-fixed, DOPRI5-adaptive, PEFRL, Y6, Leapfrog, GJ8
*Excluded SKIP:* SRKN14A

### M100

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R1

### M10

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R1

### M1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R1

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## perturbed

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R3

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M100

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R3

### M10

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R3

### M1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R3

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R3

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## planets

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M100

*Problems:* P2, P3, P4, P5, P6
*Recommended:* **RKN12(10)** (score 1.043)
*Second:* GJ8 (score 2.016, margin 93.3%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* DOPRI5-adaptive, Leapfrog
*Excluded SKIP:* RK4, DOPRI5-fixed

### M10

*Problems:* P3, P4, P5, P6
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* IAS15 (score 2.296, margin 129.6%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* Leapfrog

### M1

*Problems:* P3, P4, P5, P6
*Recommended:* **IAS15** (score 1.128)
*Second:* GJ8 (score 1.507, margin 33.6%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* DOPRI5-adaptive, DOP853-adaptive, Leapfrog, RKN12(10)
*Excluded SKIP:* RK4, DOPRI5-fixed, PEFRL, Y6, SRKN14A

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## probe-central

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M100

*Not proven (fewer than 2 problems with an evaluated level).* Problems: P6

### M10

*Not proven (fewer than 2 problems with an evaluated level).* Problems: P6

### M1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: P6

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## sanity

### A1

*Problems:* K7, K8
*Recommended:* **DOP853-adaptive** (score 1.000)
*Second:* DOPRI5-adaptive (score 2.783, margin 178.3%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15
*Excluded FAIL:* none

### A2

*Problems:* K7, K8
*Recommended:* **DOP853-adaptive** (score 1.000)
*Second:* IAS15 (score 1.616, margin 61.6%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15
*Excluded FAIL:* Leapfrog

### A3

*Problems:* K7, K8
*Recommended:* **IAS15** (score 1.023)
*Second:* DOP853-adaptive (score 1.229, margin 20.1%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15
*Excluded FAIL:* Leapfrog

### M100

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M10

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## ship

### A1

*Problems:* R3, R4
*Recommended:* **RKN12(10)** (score 1.891)
*Second:* WH-BM14A (score 2.561, margin 35.5%)
*Excluded N/A:* none
*Excluded FAIL:* RK4, GJ8
*Excluded SKIP:* Leapfrog

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R4

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M100

*Problems:* R3, R4
*Recommended:* **WH-BM14A** (score 2.243) — tie with **RKN12(10)** within 15%
*Second:* RKN12(10) (score 2.530, margin 12.8%)
*Excluded N/A:* none
*Excluded FAIL:* RK4, GJ8
*Excluded SKIP:* Leapfrog

### M10

*Problems:* R3, R4
*Recommended:* **WH-BM14A** (score 1.956) — tie with **RKN12(10)** within 15%
*Second:* RKN12(10) (score 1.965, margin 0.5%)
*Excluded N/A:* none
*Excluded FAIL:* RK4, GJ8
*Excluded SKIP:* Leapfrog

### M1

*Problems:* R3, R4
*Recommended:* **RKN12(10)** (score 1.686)
*Second:* WH-BM14A (score 2.541, margin 50.7%)
*Excluded N/A:* none
*Excluded FAIL:* RK4, GJ8
*Excluded SKIP:* Leapfrog

### M0.1

*Problems:* R3, R4
*Recommended:* **RKN12(10)** (score 1.778)
*Second:* WH-BM14A (score 2.895, margin 62.8%)
*Excluded N/A:* none
*Excluded FAIL:* RK4, GJ8
*Excluded SKIP:* Leapfrog

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R4

## thrust

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M100

*Problems:* S11, S17
*Recommended:* **DOP853-adaptive** (score 1.000) — tie with **DOPRI5-adaptive** within 15%
*Second:* DOPRI5-adaptive (score 1.072, margin 7.2%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8
*Excluded FAIL:* RK4

### M10

*Problems:* S11, S17
*Recommended:* **DOP853-adaptive** (score 1.000)
*Second:* IAS15 (score 1.683, margin 68.3%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8
*Excluded FAIL:* RK4

### M1

*Problems:* S11, S17
*Recommended:* **DOP853-adaptive** (score 1.000)
*Second:* IAS15 (score 1.425, margin 42.5%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8
*Excluded FAIL:* RK4

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## velocity-dependent

### A1

*Problems:* K8, R2
*Recommended:* **DOP853-adaptive** (score 1.000)
*Second:* IAS15 (score 2.195, margin 119.5%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15
*Excluded FAIL:* RK4, DOPRI5-adaptive

### A2

*Problems:* K8, R2
*Recommended:* **DOP853-adaptive** (score 1.050)
*Second:* IAS15 (score 1.474, margin 40.4%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15
*Excluded FAIL:* RK4, DOPRI5-adaptive

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: K8

### M100

*Problems:* S10, S11, S17
*Recommended:* **DOP853-adaptive** (score 1.000)
*Second:* DOPRI5-adaptive (score 1.555, margin 55.5%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8
*Excluded FAIL:* RK4

### M10

*Problems:* S10, S11, S17
*Recommended:* **DOP853-adaptive** (score 1.000)
*Second:* IAS15 (score 1.538, margin 53.8%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8
*Excluded FAIL:* RK4

### M1

*Problems:* S10, S11, S17
*Recommended:* **DOP853-adaptive** (score 1.000)
*Second:* IAS15 (score 1.322, margin 32.2%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8
*Excluded FAIL:* RK4

### M0.1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### M0.01

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

