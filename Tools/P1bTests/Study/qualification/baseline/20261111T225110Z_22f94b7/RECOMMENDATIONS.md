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

## ecc-high

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: K4

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: K4

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: K4

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

## nbody-chaotic

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: T3

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: T3

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: T3

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

## perturbed

### A1

*Not proven (fewer than 2 problems with an evaluated level).* Problems: R3

### A2

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

## sanity

### A1

*Problems:* K7, K8
*Recommended:* **DOP853-adaptive** (score 1.000)
*Second:* IAS15 (score 2.599, margin 159.9%)
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

## velocity-dependent

### A1

*Problems:* K8, R2
*Recommended:* **DOP853-adaptive** (score 1.000)
*Second:* IAS15 (score 2.039, margin 103.9%)
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

