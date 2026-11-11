# Recommendations (Test564)

Generated from `matrix.csv`; the price is `calls + w·aux` (single-price rule).
L1 is excluded: its oracle is INVALID (see `oracle_audit.md` and `FOUND.md` F8).

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

### A1

*Problems:* K1, K2, K3, K4, K5, K6, R1
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* DOP853-adaptive (score 1.581, margin 58.1%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* DOPRI5-adaptive, Leapfrog

### A2

*Problems:* K1, K2, K3, K4, K5, K6, R1
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* DOP853-adaptive (score 1.981, margin 98.1%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* DOPRI5-adaptive, Leapfrog

### A3

*Problems:* K1, K2, K3, K4, K5, K6
*Recommended:* **Encke-IAS15** (score 1.069)
*Second:* RKN12(10) (score 4.685, margin 338.2%)
*Excluded N/A:* none
*Excluded FAIL:* RK4, PEFRL, Leapfrog
*Excluded SKIP:* Y6, SRKN14A

## close-encounter

### A1

*Problems:* K6, T3, R4
*Recommended:* **RKN12(10)** (score 1.061)
*Second:* DOP853-adaptive (score 1.362, margin 28.4%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* DOPRI5-adaptive
*Excluded SKIP:* RK4, DOPRI5-fixed, PEFRL, Y6, Y6C, Leapfrog, SRKN14A, GJ8

### A2

*No method passes on all 3 problems; no recommendation.*
*Excluded N/A:* none
*Excluded FAIL:* RK4, DOPRI5-fixed, DOPRI5-adaptive, DOP853-adaptive, PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), IAS15, SRKN14A, WH-BM14A, GJ8, Encke-IAS15

### A3

*No method passes on all 2 problems; no recommendation.*
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* DOPRI5-adaptive, DOP853-adaptive, Leapfrog, RKN12(10), IAS15
*Excluded SKIP:* RK4, DOPRI5-fixed, PEFRL, Y6, Y6C, SRKN14A, GJ8

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
*Excluded FAIL:* none

### A2

*Problems:* K1, K2
*Recommended:* **Encke-IAS15** (score 1.000)
*Second:* RKN12(10) (score 3.699, margin 269.9%)
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog

### A3

*Problems:* K1, K2
*Recommended:* **Encke-IAS15** (score 1.000)
*Second:* RKN12(10) (score 5.616, margin 461.6%)
*Excluded N/A:* none
*Excluded FAIL:* Leapfrog

## ecc-mid

### A1

*Problems:* K3, L2
*Recommended:* **Encke-IAS15** (score 1.000)
*Second:* WH-Y6 (score 7.006, margin 600.6%)
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-adaptive
*Excluded SKIP:* Leapfrog

### A2

*Problems:* K3, L2
*Recommended:* **Encke-IAS15** (score 1.000)
*Second:* WH-Y6 (score 7.006, margin 600.6%)
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-adaptive, Leapfrog
*Excluded SKIP:* RK4, DOPRI5-fixed, PEFRL

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

*Problems:* K1, K2, K3, K4, K5, K6, L2
*Recommended:* **Encke-IAS15** (score 1.158)
*Second:* RKN12(10) (score 4.399, margin 279.8%)
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-adaptive, Leapfrog

### A2

*Problems:* K1, K2, K3, K4, K5, K6, L2
*Recommended:* **Encke-IAS15** (score 1.107)
*Second:* RKN12(10) (score 6.338, margin 472.6%)
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-adaptive, Leapfrog
*Excluded SKIP:* RK4, DOPRI5-fixed, PEFRL

### A3

*Problems:* K1, K2, K3, K4, K5, K6
*Recommended:* **Encke-IAS15** (score 1.069)
*Second:* RKN12(10) (score 4.685, margin 338.2%)
*Excluded N/A:* none
*Excluded FAIL:* RK4, PEFRL, Leapfrog
*Excluded SKIP:* Y6, SRKN14A

## long-horizon

### A1

*Problems:* T1, R1, R2, L2
*Recommended:* **DOP853-adaptive** (score 1.000)
*Second:* IAS15 (score 1.226, margin 22.6%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15
*Excluded FAIL:* DOPRI5-adaptive

### A2

*Problems:* T1, R1, R2, L2
*Recommended:* **IAS15** (score 1.000)
*Second:* DOP853-adaptive (score 1.260, margin 26.0%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15
*Excluded FAIL:* DOPRI5-adaptive, Leapfrog
*Excluded SKIP:* RK4, DOPRI5-fixed

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: T1

## many-body

### A1

*Problems:* R1, R2
*Recommended:* **DOP853-adaptive** (score 1.000)
*Second:* IAS15 (score 1.362, margin 36.2%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15
*Excluded FAIL:* DOPRI5-adaptive

### A2

*Problems:* R1, R2
*Recommended:* **IAS15** (score 1.000) — tie with **DOP853-adaptive** within 15%
*Second:* DOP853-adaptive (score 1.101, margin 10.1%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15
*Excluded FAIL:* DOPRI5-adaptive

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: none

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
*Recommended:* **RKN12(10)** (score 1.001)
*Second:* SRKN14A (score 1.444, margin 44.3%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* DOPRI5-adaptive

### A2

*Problems:* T1, T2, R1
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* GJ8 (score 1.503, margin 50.3%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* DOPRI5-adaptive, Leapfrog

### A3

*Problems:* T1, T2
*Recommended:* **RKN12(10)** (score 1.000)
*Second:* IAS15 (score 1.639, margin 63.9%)
*Excluded N/A:* WH-Y6, WH-BM14A, Encke-IAS15
*Excluded FAIL:* DOPRI5-adaptive, Leapfrog
*Excluded SKIP:* RK4, DOPRI5-fixed, PEFRL, Y6

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
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15
*Excluded FAIL:* Leapfrog

### A3

*Problems:* K7, K8
*Recommended:* **IAS15** (score 1.023)
*Second:* DOP853-adaptive (score 1.229, margin 20.1%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15
*Excluded FAIL:* Leapfrog

## ship

### A1

*Problems:* R3, R4
*Recommended:* **RKN12(10)** (score 1.876)
*Second:* WH-BM14A (score 2.541, margin 35.5%)
*Excluded N/A:* none
*Excluded FAIL:* DOPRI5-adaptive
*Excluded SKIP:* RK4, Leapfrog

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
*Excluded FAIL:* DOPRI5-adaptive

### A2

*Problems:* K8, R2
*Recommended:* **DOP853-adaptive** (score 1.050)
*Second:* IAS15 (score 1.474, margin 40.4%)
*Excluded N/A:* PEFRL, Y6, Y6C, WH-Y6, Leapfrog, RKN12(10), SRKN14A, WH-BM14A, GJ8, Encke-IAS15
*Excluded FAIL:* DOPRI5-adaptive

### A3

*Not proven (fewer than 2 problems with an evaluated level).* Problems: K8

